using System.Text.Json;
using ExpiryTracker.App.Resources.Strings;
using HomeTracker.Core.Households;
using HomeTracker.Core.Items;
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

    private const string OutboxKey = "sync.outbox";
    private static readonly TimeSpan CheckInEvery = TimeSpan.FromHours(6);
    private static readonly TimeSpan SilentAfter = TimeSpan.FromHours(24);
    private static readonly TimeSpan StateReplyAtMostEvery = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan RelayKeeps = TimeSpan.FromHours(11);
    private static readonly TimeSpan ForegroundEvery = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan SendDelay = TimeSpan.FromSeconds(3);

    private readonly HomeService _homes;
    private readonly ItemService _items;
    private readonly HttpClient _http;
    private readonly Clock _clock;
    private readonly SemaphoreSlim _running = new(1, 1);
    private readonly object _outboxLock = new();
    private CancellationTokenSource? _sendSoon;
    private CancellationTokenSource? _foreground;

    public FamilySyncService(HomeService homes, ItemService items, HttpClient http, Clock clock)
    {
        _homes = homes;
        _items = items;
        _http = http;
        _clock = clock;
        _items.Changed += OnItemsChanged;
    }

    /// <summary>Raised after a sync run that changed something or contacted the relay.</summary>
    public event EventHandler? Synced;

    private sealed record ItemsBody(List<TrackedItem> Items);

    private sealed record EmptyBody;

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

    /// <summary>Sends pending changes and fetches new ones for every shared home. Never throws.</summary>
    public async Task SyncNowAsync(CancellationToken cancellationToken = default)
    {
        if (!await _running.WaitAsync(0, cancellationToken).AsTaskSafe())
        {
            return; // Already running (foreground timer and background job share one process).
        }

        try
        {
            foreach (var home in (await _homes.GetHomesAsync()).Where(h => h.IsShared))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await SyncHomeAsync(home, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Stopped by Android or the app went to the background; the next run continues.
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
        await TryAsync(async () =>
        {
            var relay = Relay(home);
            await relay.PublishAsync(home.Topic!, Encode(home, CheckInKind, new EmptyBody()));
            await relay.PublishAsync(home.Topic!, Encode(home, StateRequestKind, new EmptyBody()));
            SetTime("sync.checkin." + home.Id, _clock.Now);
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

    private async Task SyncHomeAsync(Home home, CancellationToken cancellationToken)
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
    }

    private async Task<bool> FetchAsync(Home home, CancellationToken cancellationToken)
    {
        var sinceKey = "sync.since." + home.Id;
        var since = Preferences.Default.Get<string?>(sinceKey, null);

        // Away longer than the relay keeps messages: ask the family to send the whole list again.
        if (LastSync(home.Id) is { } last && _clock.Now - last > RelayKeeps)
        {
            await Relay(home).PublishAsync(home.Topic!, Encode(home, StateRequestKind, new EmptyBody()), cancellationToken);
        }

        var messages = await Relay(home).FetchAsync(home.Topic!, since, cancellationToken);
        var me = _homes.Me.DeviceId;
        var changed = false;
        var stateRequested = false;
        var incoming = new List<TrackedItem>();

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
            }
        }

        if (incoming.Count > 0)
        {
            changed = (await _items.ApplyFromFamilyAsync(incoming)).Count > 0;
        }

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
        SetTime("sync.statereply." + home.Id, _clock.Now);
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

    private async Task CheckInIfDueAsync(Home home, CancellationToken cancellationToken)
    {
        if (GetTime("sync.checkin." + home.Id) is { } last && _clock.Now - last < CheckInEvery)
        {
            return;
        }

        await Relay(home).PublishAsync(home.Topic!, Encode(home, CheckInKind, new EmptyBody()), cancellationToken);
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

    private async Task FlushOutboxAsync(Home home, CancellationToken cancellationToken)
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

        var items = (await _items.GetByIdsAsync(pending)).Where(i => i.HomeId == home.Id).ToList();
        await PublishItemsAsync(home, items, cancellationToken);

        lock (_outboxLock)
        {
            var outbox = ReadOutbox();
            outbox.ExceptWith(items.Select(i => i.Id));
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
