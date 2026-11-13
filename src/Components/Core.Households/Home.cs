using System.Security.Cryptography;

namespace HomeTracker.Core.Households;

/// <summary>
/// A household whose list is shared between family phones. A phone can belong to several homes
/// (its own, and e.g. the parents' home). A home that has never been shared has no key.
/// </summary>
/// <param name="Id">Random, the same on every phone in the home.</param>
/// <param name="Key">32-byte secret for end-to-end encryption; null until the home is shared.</param>
/// <param name="Topic">Unguessable relay channel name; null until the home is shared.</param>
/// <param name="RelayUrl">Relay server, e.g. https://ntfy.sh (or a self-hosted ntfy).</param>
public sealed record Home(string Id, string Name, byte[]? Key, string? Topic, Uri RelayUrl, DateTimeOffset CreatedAt)
{
    public static readonly Uri DefaultRelay = new("https://ntfy.sh");

    public bool IsShared => Key is { Length: 32 } && !string.IsNullOrEmpty(Topic);

    public static Home CreateLocal(string name, DateTimeOffset now) =>
        new(NewId(), name, null, null, DefaultRelay, now);

    /// <summary>Gives the home a new secret key and relay channel so other phones can join.</summary>
    public Home StartSharing() => this with
    {
        Key = RandomNumberGenerator.GetBytes(32),
        // ntfy topics allow [A-Za-z0-9_-]; 26 random base-32 characters (130 bits) can't be guessed.
        Topic = "ht" + RandomBase32(26),
    };

    public static string NewId() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));

    private static string RandomBase32(int length)
    {
        const string alphabet = "abcdefghijklmnopqrstuvwxyz234567";
        return string.Create(length, 0, (span, _) =>
        {
            for (var i = 0; i < span.Length; i++)
            {
                span[i] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
            }
        });
    }
}

/// <summary>This phone, as other phones in a home see it.</summary>
public sealed record DeviceIdentity(string DeviceId, string Name)
{
    public static string NewId() => Home.NewId();
}

/// <summary>Another phone in a home, and when it was last heard from.</summary>
public sealed record FamilyDevice(string HomeId, string DeviceId, string Name, DateTimeOffset LastSeen, DateTimeOffset? SilentAlertSent = null)
{
    /// <summary>True when the phone hasn't checked in for <paramref name="threshold"/> and no alert was sent for this silence yet.</summary>
    public bool NeedsSilentAlert(DateTimeOffset now, TimeSpan threshold) =>
        now - LastSeen >= threshold && (SilentAlertSent is null || SilentAlertSent < LastSeen);
}
