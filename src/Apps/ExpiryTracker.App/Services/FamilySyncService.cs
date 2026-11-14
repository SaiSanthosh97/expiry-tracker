using System.Text.Json;
using ExpiryTracker.App.Resources.Strings;
using HomeTracker.Core.Households;
using HomeTracker.Core.Items;
using HomeTracker.Core.Medicines;
using HomeTracker.Core.Relay;
using HomeTracker.Core.Sync;
using HomeTracker.Device.BackgroundWork;
using HomeTracker.Device.Reminders;

namespace ExpiryTracker.App.Services;

/// <summary>
/// Keeps the lists of a shared home the same on every family phone, through an ntfy relay, end-to-end encrypted
/// with the home's key. Changes are sent a few seconds after they happen; each phone fetches new messages every
/// minute while the app is open and about every 15 minutes in the background. The relay keeps messages for
/// 12 hours, so a phone that was away longer asks the others to send the whole list again.
/// </summary>
public sealed class FamilySyncService
{
    private const string ItemsKind = "items";
    private const string StateRequestKind = "state-request";
    private const string CheckInKind = "check-in";
    private const string PhotoKind = "photo";
    private const string PhotoRequestKind = "photo-request";
    private const string MedicinesKind = "medicines";
    private const string MedOutboxKey = "sync.outbox.meds";
    private const string DoseOutboxKey = "sync.outbox.doses";
    private static readonly TimeSpan PhotoAskAgainAfter = TimeSpan.FromHours(6);
    private static readonly TimeSpan PhotoResendAtMostEvery = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan StateAskAgainAfter = TimeSpan.FromMinutes(30);

    private const string OutboxKey = "sync.outbox";
    private static readonly TimeSpan CheckInEvery = TimeSpan.FromHours(6);
    private static readonly TimeSpan SilentAfter = TimeSpan.FromHours(24);
    private static readonly TimeSpan StateReplyAtMostEvery = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan RelayKeeps = TimeSpan.FromHours(11);
    private static readonly TimeSpan ForegroundEvery = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan SendDelay = TimeSpan.FromSeconds(3);

    private readonly HomeService _homes;
    private readonly ItemService _items;
    private readonly MedicineService _medicines;
    private readonly HttpClient _http;
    private readonly Clock _clock;
    private readonly SemaphoreSlim _running = new(1, 1);
    private readonly object _outboxLock = new();
    private CancellationTokenSource? _sendSoon;
    private CancellationTokenSource? _foreground;

    public FamilySyncService(HomeService homes, ItemService items, MedicineService medicines, HttpClient http, Clock clock)
    {
        _homes = homes;
        _items = items;
        _medicines = medicines;
        _medicines.Changed += OnMedicinesChanged;
        _http = http;
        _clock = clock;
        _items.Changed += OnItemsChanged;
    }

    /// <summary>Raised after a sync run that changed something or contacted the relay.</summary>
    public event EventHandler? Synced;

    private sealed record ItemsBody(List<TrackedItem> Items);

    private sealed record EmptyBody;

    /// <param name="Replaces">Set by a phone restored from a backup: the old phone it takes over from.</param>
    private sealed record CheckInBody(string? Replaces);

    private sealed record PhotoBody(string File);

    private sealed record PhotoRequestBody(List<string> Files);

    private sealed record MedicinesBody(List<Medicine> Medicines, List<DoseEvent> Doses);

    /// <summary>When this phone last reached the relay for a home, or null.</summary>
    public static DateTimeOffset? LastSync(string homeId) => GetTime("sync.last." + homeId);

    /// <summary>Turns background sync on when at least one home is shared.</summary>
    public async Task UpdateBackgroundAsync()
    {
        var anyShared = (await _homes.GetHomesAsync()).Any(h => h.IsShared);
        BackgroundWork.SetEnabled(anyShared);
    }

    /// <summary>Syncs every minute while the app is on screen.</summary>
    public void SetForeground(bool visible)
    {
        _foreground?.Cancel();
        _foreground = null;
        if (!visible)
        {
            return;
        }

        var cts = _foreground = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(ForegroundEvery);
            do
            {
                await SyncNowAsync(cts.Token);
            }
            while (await timer.WaitForNextTickAsync(cts.Token).AsTaskSafe());
        });
    }

    private bool _askPhotosNow;

    /// <summary>"Refresh": sync now, and ask for any missing photos straight away.</summary>
    public async Task RefreshAsync()
    {
        _askPhotosNow = true;
        try
        {
            await SyncNowAsync();
        }
        finally
        {
            _askPhotosNow = false;
        }
    }

    /// <summary>Sends pending changes and fetches new ones for every shared home. Never throws.</summary>
    public async Task SyncNowAsync(CancellationToken cancellationToken = default)
    {
        if (!await _running.WaitAsync(0, cancellationToken).AsTaskSafe())
        {
            return; // Already running (foreground timer and background job share one process).
        }

        try
        {
            var replacing = HomeService.ReplacesDeviceId is not null;
            var allOk = true;
            var allHomes = await _homes.GetHomesAsync();
            foreach (var home in allHomes.Where(h => h.IsShared))
            {
                cancellationToken.ThrowIfCancellationRequested();
                allOk &= await SyncHomeAsync(home, cancellationToken);
            }

            await DropUnsharedFromOutboxAsync(allHomes);
            await DropUnsharedMedicinesAsync(allHomes);

            if (replacing && allOk)
            {
                HomeService.ReplacementAnnounced();
            }
        }
        catch (OperationCanceledException)
        {
            // Stopped by Android or the app went to the background; the next run continues.
        }
        catch (Exception ex)
        {
            // Never let one bad run stop syncing for good; the next run tries again.
            Android.Util.Log.Error("HomeTracker", $"Family sync failed: {ex}");
        }
        finally
        {
            _running.Release();
        }
    }

    /// <summary>After joining: introduce this phone and ask the family for the whole list.</summary>
    public async Task AfterJoinAsync(Home home)
    {
        Preferences.Default.Remove("sync.since." + home.Id);
        // Keep asking (every 30 minutes) until another phone has sent the list, in case they were all off.
        Preferences.Default.Set("sync.awaiting." + home.Id, true);
        await TryAsync(async () =>
        {
            var relay = Relay(home);
            await relay.PublishAsync(home.Topic!, Encode(home, CheckInKind, new CheckInBody(null)));
            await relay.PublishAsync(home.Topic!, Encode(home, StateRequestKind, new EmptyBody()));
            SetTime("sync.checkin." + home.Id, _clock.Now);
            SetTime("sync.statereq." + home.Id, _clock.Now);
        });
        await UpdateBackgroundAsync();
        await SyncNowAsync();
    }

    /// <summary>After starting to share: send the current list so phones that join soon get it straight away.</summary>
    public async Task AfterShareAsync(Home home)
    {
        await TryAsync(() => PublishStateAsync(home, CancellationToken.None));
        await UpdateBackgroundAsync();
    }

    /// <returns>False when the relay couldn't be reached.</returns>
    private async Task<bool> SyncHomeAsync(Home home, CancellationToken cancellationToken)
    {
        var ok = await TryAsync(async () =>
        {
            await FlushOutboxAsync(home, cancellationToken);
            var changed = await FetchAsync(home, cancellationToken);
            await CheckInIfDueAsync(home, cancellationToken);
            SetTime("sync.last." + home.Id, _clock.Now);
            if (changed)
            {
                Synced?.Invoke(this, EventArgs.Empty);
            }
        });

        if (ok)
        {
            await AlertSilentPhonesAsync(home);
        }

        return ok;
    }

    private async Task<bool> FetchAsync(Home home, CancellationToken cancellationToken)
    {
        var sinceKey = "sync.since." + home.Id;
        var since = Preferences.Default.Get<string?>(sinceKey, null);

        // Away longer than the relay keeps messages, or just joined and nothing received yet:
        // ask the family to send the whole list (again).
        var awaiting = Preferences.Default.Get("sync.awaiting." + home.Id, false);
        var away = LastSync(home.Id) is { } last && _clock.Now - last > RelayKeeps;
        var askedRecently = GetTime("sync.statereq." + home.Id) is { } asked && _clock.Now - asked < StateAskAgainAfter;
        if (away || (awaiting && !askedRecently))
        {
            await Relay(home).PublishAsync(home.Topic!, Encode(home, StateRequestKind, new EmptyBody()), cancellationToken);
            SetTime("sync.statereq." + home.Id, _clock.Now);
        }

        var messages = await Relay(home).FetchAsync(home.Topic!, since, cancellationToken);
        var me = _homes.Me.DeviceId;
        var changed = false;
        var stateRequested = false;
        var incoming = new List<TrackedItem>();
        var photosRequested = new HashSet<string>();
        var photoLinks = new Dictionary<string, Uri>();
        var incomingMeds = new List<Medicine>();
        var incomingDoses = new List<DoseEvent>();

        foreach (var message in messages)
        {
            var envelope = SyncCodec.Decode(message.Text, home.Key!, home.Id);
            if (envelope is null || envelope.From == me)
            {
                continue;
            }

            await SeenAsync(home, envelope);
            switch (envelope.Kind)
            {
                case ItemsKind when envelope.BodyAs<ItemsBody>() is { } body:
                    // Items always land in the home they were sent to.
                    incoming.AddRange(body.Items.Select(i => i with { HomeId = home.Id }));
                    break;
                case StateRequestKind:
                    stateRequested = true;
                    break;
                case CheckInKind when envelope.BodyAs<CheckInBody>() is { Replaces: { } old } && old != me:
                    await _homes.ForgetDeviceAsync(home.Id, old);
                    break;
                case PhotoKind when envelope.BodyAs<PhotoBody>() is { } photo && message.AttachmentUrl is { } url && PhotoStore.IsValidName(photo.File):
                    photoLinks[photo.File] = url;
                    break;
                case PhotoRequestKind when envelope.BodyAs<PhotoRequestBody>() is { } request:
                    photosRequested.UnionWith(request.Files.Where(PhotoStore.IsValidName));
                    break;
                case MedicinesKind when envelope.BodyAs<MedicinesBody>() is { } meds:
                    incomingMeds.AddRange(meds.Medicines.Select(m => m with { HomeId = home.Id }));
                    incomingDoses.AddRange(meds.Doses.Select(d => d with { HomeId = home.Id }));
                    break;
            }
        }

        if (incoming.Count > 0)
        {
            changed = (await _items.ApplyFromFamilyAsync(incoming)).Count > 0;
            Preferences.Default.Remove("sync.awaiting." + home.Id);
        }

        if (incomingMeds.Count > 0 || incomingDoses.Count > 0)
        {
            var applied = await _medicines.ApplyFromFamilyAsync(incomingMeds, incomingDoses);
            changed |= applied.Medicines.Count > 0 || applied.Doses.Count > 0;
            Preferences.Default.Remove("sync.awaiting." + home.Id);
            await AlertFamilyAsync(home, applied);
        }

        changed |= await ReceivePhotosAsync(home, photoLinks, cancellationToken);
        await SendRequestedPhotosAsync(home, photosRequested, cancellationToken);
        await RequestMissingPhotosAsync(home, cancellationToken);

        if (stateRequested && (GetTime("sync.statereply." + home.Id) is not { } replied || _clock.Now - replied > StateReplyAtMostEvery))
        {
            await PublishStateAsync(home, cancellationToken);
        }

        if (messages.Count > 0)
        {
            Preferences.Default.Set(sinceKey, messages[^1].Id);
        }

        return changed || messages.Count > 0;
    }

    private async Task PublishStateAsync(Home home, CancellationToken cancellationToken)
    {
        // Everything still open, plus recent changes (so recent "used up" and deletions arrive too).
        var cutoff = _clock.Now.AddDays(-60);
        var items = (await _items.GetEverythingAsync(home.Id))
            .Where(i => (!i.IsClosed && !i.IsDeleted) || i.UpdatedAt > cutoff)
            .ToList();
        await PublishItemsAsync(home, items, cancellationToken);
        var (meds, doses) = await _medicines.GetForHomeAsync(home.Id);
        await PublishMedicinesAsync(home, meds, doses, cancellationToken);
        SetTime("sync.statereply." + home.Id, _clock.Now);

        // Photos of open items go with the list, so a phone that just joined doesn't have to ask for each.
        var photos = items
            .Where(i => !i.IsClosed && !i.IsDeleted && PhotoStore.Exists(i.PhotoPath))
            .OrderByDescending(i => i.UpdatedAt)
            .Select(i => i.PhotoPath!)
            .Take(40);
        await SendRequestedPhotosAsync(home, photos, cancellationToken);
    }

    private async Task PublishItemsAsync(Home home, IReadOnlyList<TrackedItem> items, CancellationToken cancellationToken)
    {
        if (items.Count == 0)
        {
            return;
        }

        var relay = Relay(home);
        var messages = SyncCodec.Pack(items, batch => Encode(home, ItemsKind, new ItemsBody([.. batch])), NtfyRelay.MaxMessageLength);
        foreach (var message in messages)
        {
            await relay.PublishAsync(home.Topic!, message, cancellationToken);
        }
    }

    private async Task PublishMedicinesAsync(Home home, IReadOnlyList<Medicine> meds, IReadOnlyList<DoseEvent> doses, CancellationToken cancellationToken)
    {
        var relay = Relay(home);
        if (meds.Count > 0)
        {
            foreach (var message in SyncCodec.Pack(meds, batch => Encode(home, MedicinesKind, new MedicinesBody([.. batch], [])), NtfyRelay.MaxMessageLength))
            {
                await relay.PublishAsync(home.Topic!, message, cancellationToken);
            }
        }

        if (doses.Count > 0)
        {
            foreach (var message in SyncCodec.Pack(doses, batch => Encode(home, MedicinesKind, new MedicinesBody([], [.. batch])), NtfyRelay.MaxMessageLength))
            {
                await relay.PublishAsync(home.Topic!, message, cancellationToken);
            }
        }
    }

    /// <summary>
    /// On family phones (not the phone that rings for the medicine): a missed dose and a medicine running low
    /// each give one notification.
    /// </summary>
    private async Task AlertFamilyAsync(Home home, MedicinesChangedEventArgs applied)
    {
        if (!Features.Medicines || !HomeService.GetRemind(home.Id))
        {
            return;
        }

        var me = _homes.Me.DeviceId;
        var all = (await _medicines.GetAllAsync(includeDeleted: true)).ToDictionary(m => m.Id);
        foreach (var dose in applied.Doses.Where(d => d.Status == DoseStatus.Missed))
        {
            if (!all.TryGetValue(dose.MedicineId, out var med) || med.ReminderDeviceId == me || med.IsDeleted
                || Preferences.Default.ContainsKey("family.missed." + dose.Id))
            {
                continue;
            }

            Preferences.Default.Set("family.missed." + dose.Id, true);
            DailyReminders.ShowMessage(
                "missed/" + dose.Id,
                string.Format(AppResources.Family_MissedTitle, med.Person),
                string.Format(AppResources.Family_MissedText, med.Name, dose.ScheduledAt.ToString("t", System.Globalization.CultureInfo.CurrentCulture), home.Name));
        }

        foreach (var med in applied.Medicines.Where(m => !m.IsDeleted && m.ReminderDeviceId != me))
        {
            var key = "family.refill." + med.Id;
            if (!DoseStock.NeedsRefill(med))
            {
                Preferences.Default.Remove(key);
            }
            else if (!Preferences.Default.ContainsKey(key))
            {
                Preferences.Default.Set(key, true);
                DailyReminders.ShowMessage(
                    "refill/" + med.Id,
                    string.Format(AppResources.Refill_FamilyTitle, med.Person, med.Name),
                    MedicineText.Refill(med));
            }
        }
    }

    // ---- Photos: sent as encrypted files (ntfy.sh keeps files 3 hours); a phone that missed one asks again ----

    private async Task<bool> ReceivePhotosAsync(Home home, Dictionary<string, Uri> links, CancellationToken cancellationToken)
    {
        if (links.Count == 0)
        {
            return false;
        }

        var wanted = (await _items.GetEverythingAsync(home.Id))
            .Where(i => !i.IsDeleted && i.PhotoPath is not null && !PhotoStore.Exists(i.PhotoPath))
            .Select(i => i.PhotoPath!)
            .ToHashSet();
        var received = false;
        foreach (var (file, url) in links.Where(l => wanted.Contains(l.Key)))
        {
            try
            {
                var sealedBytes = await Relay(home).DownloadAsync(url, cancellationToken);
                if (SealedBox.OpenBytes(home.Key!, sealedBytes, home.Id + "/" + file) is { } photo)
                {
                    await PhotoStore.SaveReceivedAsync(file, photo);
                    received = true;
                }
            }
            catch (HttpRequestException)
            {
                // Expired (older than 3 hours): it is asked for again below.
            }
        }

        return received;
    }

    private async Task RequestMissingPhotosAsync(Home home, CancellationToken cancellationToken)
    {
        var now = _clock.Now;
        var missing = (await _items.GetEverythingAsync(home.Id))
            .Where(i => !i.IsDeleted && !i.IsClosed && PhotoStore.IsValidName(i.PhotoPath) && !PhotoStore.Exists(i.PhotoPath))
            .Select(i => i.PhotoPath!)
            .Where(f => _askPhotosNow || GetTime("sync.photoask." + f) is not { } asked || now - asked > PhotoAskAgainAfter)
            .Take(20)
            .ToList();
        if (missing.Count == 0)
        {
            return;
        }

        await Relay(home).PublishAsync(home.Topic!, Encode(home, PhotoRequestKind, new PhotoRequestBody(missing)), cancellationToken);
        foreach (var file in missing)
        {
            SetTime("sync.photoask." + file, now);
        }
    }

    private async Task SendRequestedPhotosAsync(Home home, IEnumerable<string> files, CancellationToken cancellationToken)
    {
        foreach (var file in files)
        {
            if (GetTime("sync.photosent." + file) is { } sent && _clock.Now - sent < PhotoResendAtMostEvery)
            {
                continue; // Another request for the same photo: it was just sent.
            }

            await SendPhotoAsync(home, file, cancellationToken);
        }
    }

    private async Task SendPhotoAsync(Home home, string file, CancellationToken cancellationToken)
    {
        if (PhotoStore.ReadBytes(file) is not { } photo)
        {
            return;
        }

        var sealedBytes = SealedBox.SealBytes(home.Key!, photo, home.Id + "/" + file);
        await Relay(home).PublishFileAsync(home.Topic!, sealedBytes, Encode(home, PhotoKind, new PhotoBody(file)), cancellationToken);
        SetTime("sync.photosent." + file, _clock.Now);
    }

    private async Task CheckInIfDueAsync(Home home, CancellationToken cancellationToken)
    {
        // A phone restored from a backup checks in at once, telling the family which old phone it replaces.
        var replaces = HomeService.ReplacesDeviceId;
        if (replaces is null && GetTime("sync.checkin." + home.Id) is { } last && _clock.Now - last < CheckInEvery)
        {
            return;
        }

        await Relay(home).PublishAsync(home.Topic!, Encode(home, CheckInKind, new CheckInBody(replaces)), cancellationToken);
        SetTime("sync.checkin." + home.Id, _clock.Now);
    }

    private async Task SeenAsync(Home home, SyncEnvelope envelope)
    {
        var devices = await _homes.GetDevicesAsync(home.Id);
        var known = devices.FirstOrDefault(d => d.DeviceId == envelope.From);
        // A phone with a wrong clock can't claim to be seen in the future.
        var seen = envelope.SentAt > _clock.Now ? _clock.Now : envelope.SentAt;
        if (known is null || seen > known.LastSeen || known.Name != envelope.FromName)
        {
            await _homes.SaveDeviceAsync((known ?? new FamilyDevice(home.Id, envelope.From, envelope.FromName, seen)) with
            {
                Name = envelope.FromName,
                LastSeen = known is null || seen > known.LastSeen ? seen : known.LastSeen,
            });
        }
    }

    private async Task AlertSilentPhonesAsync(Home home)
    {
        var now = _clock.Now;
        foreach (var device in await _homes.GetDevicesAsync(home.Id))
        {
            if (device.DeviceId == _homes.Me.DeviceId || !device.NeedsSilentAlert(now, SilentAfter))
            {
                continue;
            }

            DailyReminders.ShowMessage(
                "silent/" + home.Id + "/" + device.DeviceId,
                string.Format(AppResources.Family_SilentTitle, device.Name),
                string.Format(AppResources.Family_SilentText, device.Name, home.Name));
            await _homes.SaveDeviceAsync(device with { SilentAlertSent = now });
        }
    }

    // ---- Outbox: changes made on this phone, sent a few seconds later (or on the next run if offline) ----

    private void OnItemsChanged(object? sender, ItemsChangedEventArgs e)
    {
        if (e.FromSync || e.Items.Count == 0)
        {
            return;
        }

        lock (_outboxLock)
        {
            var outbox = ReadOutbox();
            outbox.UnionWith(e.Items.Where(i => i.HomeId is not null).Select(i => i.Id));
            Preferences.Default.Set(OutboxKey, JsonSerializer.Serialize(outbox));
        }

        _sendSoon?.Cancel();
        var cts = _sendSoon = new CancellationTokenSource();
        _ = Task.Delay(SendDelay, cts.Token).ContinueWith(
            _ => SyncNowAsync(),
            cts.Token,
            TaskContinuationOptions.OnlyOnRanToCompletion,
            TaskScheduler.Default).Unwrap();
    }

    private void OnMedicinesChanged(object? sender, MedicinesChangedEventArgs e)
    {
        if (e.FromSync || (e.Medicines.Count == 0 && e.Doses.Count == 0))
        {
            return;
        }

        lock (_outboxLock)
        {
            var meds = ReadSet(MedOutboxKey);
            meds.UnionWith(e.Medicines.Select(m => m.Id));
            Preferences.Default.Set(MedOutboxKey, JsonSerializer.Serialize(meds));
            var doses = ReadSet(DoseOutboxKey);
            doses.UnionWith(e.Doses.Select(d => d.Id));
            Preferences.Default.Set(DoseOutboxKey, JsonSerializer.Serialize(doses));
        }

        _sendSoon?.Cancel();
        var cts = _sendSoon = new CancellationTokenSource();
        _ = Task.Delay(SendDelay, cts.Token).ContinueWith(
            _ => SyncNowAsync(),
            cts.Token,
            TaskContinuationOptions.OnlyOnRanToCompletion,
            TaskScheduler.Default).Unwrap();
    }

    private async Task FlushMedicinesAsync(Home home, CancellationToken cancellationToken)
    {
        HashSet<string> medIds, doseIds;
        lock (_outboxLock)
        {
            medIds = ReadSet(MedOutboxKey);
            doseIds = ReadSet(DoseOutboxKey);
        }

        if (medIds.Count == 0 && doseIds.Count == 0)
        {
            return;
        }

        var meds = (await _medicines.GetAllAsync(includeDeleted: true)).Where(m => m.HomeId == home.Id && medIds.Contains(m.Id)).ToList();
        var doses = (await _medicines.GetRecentEventsAsync()).Where(d => d.HomeId == home.Id && doseIds.Contains(d.Id)).ToList();
        await PublishMedicinesAsync(home, meds, doses, cancellationToken);

        lock (_outboxLock)
        {
            var m = ReadSet(MedOutboxKey);
            m.ExceptWith(meds.Select(x => x.Id));
            Preferences.Default.Set(MedOutboxKey, JsonSerializer.Serialize(m));
            var d = ReadSet(DoseOutboxKey);
            d.ExceptWith(doses.Select(x => x.Id));
            Preferences.Default.Set(DoseOutboxKey, JsonSerializer.Serialize(d));
        }
    }

    /// <summary>Medicine changes in homes that aren't shared, or no longer known, have nowhere to go.</summary>
    private async Task DropUnsharedMedicinesAsync(IReadOnlyList<Home> allHomes)
    {
        var shared = allHomes.Where(h => h.IsShared).Select(h => h.Id).ToHashSet();
        var meds = (await _medicines.GetAllAsync(includeDeleted: true)).Where(m => shared.Contains(m.HomeId)).Select(m => m.Id).ToHashSet();
        var doses = (await _medicines.GetRecentEventsAsync()).Where(d => shared.Contains(d.HomeId)).Select(d => d.Id).ToHashSet();
        lock (_outboxLock)
        {
            var m = ReadSet(MedOutboxKey);
            m.IntersectWith(meds);
            Preferences.Default.Set(MedOutboxKey, JsonSerializer.Serialize(m));
            var d = ReadSet(DoseOutboxKey);
            d.IntersectWith(doses);
            Preferences.Default.Set(DoseOutboxKey, JsonSerializer.Serialize(d));
        }
    }

    private static HashSet<string> ReadSet(string key)
    {
        try
        {
            return JsonSerializer.Deserialize<HashSet<string>>(Preferences.Default.Get(key, "[]")) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private async Task FlushOutboxAsync(Home home, CancellationToken cancellationToken)
    {
        await FlushMedicinesAsync(home, cancellationToken);

        HashSet<string> pending;
        lock (_outboxLock)
        {
            pending = ReadOutbox();
        }

        if (pending.Count == 0)
        {
            return;
        }

        var items = (await _items.GetByIdsAsync(pending)).Where(i => i.HomeId == home.Id).ToList();
        await PublishItemsAsync(home, items, cancellationToken);
        foreach (var file in items.Where(i => !i.IsDeleted && PhotoStore.Exists(i.PhotoPath)).Select(i => i.PhotoPath!))
        {
            if (GetTime("sync.photosent." + file) is null)
            {
                await SendPhotoAsync(home, file, cancellationToken);
            }
        }

        lock (_outboxLock)
        {
            var outbox = ReadOutbox();
            outbox.ExceptWith(items.Select(i => i.Id));
            Preferences.Default.Set(OutboxKey, JsonSerializer.Serialize(outbox));
        }
    }

    /// <summary>Changes in homes that aren't shared have nowhere to go; when a home starts sharing, its whole list is sent anyway.</summary>
    private async Task DropUnsharedFromOutboxAsync(IReadOnlyList<Home> allHomes)
    {
        HashSet<string> pending;
        lock (_outboxLock)
        {
            pending = ReadOutbox();
        }

        if (pending.Count == 0)
        {
            return;
        }

        var shared = allHomes.Where(h => h.IsShared).Select(h => h.Id).ToHashSet();
        var drop = (await _items.GetByIdsAsync(pending)).Where(i => i.HomeId is null || !shared.Contains(i.HomeId)).Select(i => i.Id).ToList();
        var gone = pending.Except((await _items.GetByIdsAsync(pending)).Select(i => i.Id));
        lock (_outboxLock)
        {
            var outbox = ReadOutbox();
            outbox.ExceptWith(drop);
            outbox.ExceptWith(gone);
            Preferences.Default.Set(OutboxKey, JsonSerializer.Serialize(outbox));
        }
    }

    private static HashSet<string> ReadOutbox()
    {
        try
        {
            return JsonSerializer.Deserialize<HashSet<string>>(Preferences.Default.Get(OutboxKey, "[]")) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    // ---- Helpers ----

    private NtfyRelay Relay(Home home) => new(_http, home.RelayUrl, "HomeTracker");

    private string Encode<T>(Home home, string kind, T body)
    {
        var me = _homes.Me;
        return SyncCodec.Encode(SyncCodec.Create(kind, me.DeviceId, me.Name, _clock.Now, body), home.Key!, home.Id);
    }

    /// <returns>False when the relay couldn't be reached (offline, limit reached); the next run tries again.</returns>
    private static async Task<bool> TryAsync(Func<Task> action)
    {
        try
        {
            await action();
            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or IOException)
        {
            Android.Util.Log.Warn("HomeTracker", $"Family sync: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    private static DateTimeOffset? GetTime(string key) =>
        Preferences.Default.Get(key, 0L) is var ms and > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(ms) : null;

    private static void SetTime(string key, DateTimeOffset value) => Preferences.Default.Set(key, value.ToUnixTimeMilliseconds());
}

internal static class TaskExtensions
{
    /// <summary>Turns cancellation into a false result instead of an exception, for loop conditions.</summary>
    public static async Task<bool> AsTaskSafe(this ValueTask<bool> task)
    {
        try
        {
            return await task;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    public static async Task<bool> AsTaskSafe(this Task<bool> task)
    {
        try
        {
            return await task;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
