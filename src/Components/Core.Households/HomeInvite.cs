using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace HomeTracker.Core.Households;

/// <summary>
/// The invite that lets another phone join a home: shown as a QR code, or sent as text (e.g. on WhatsApp).
/// It contains the home's secret key, so it should only be given to family.
/// </summary>
public static partial class HomeInvite
{
    public const string Prefix = "HTHOME1:";

    public static string Encode(Home home)
    {
        if (!home.IsShared)
        {
            throw new InvalidOperationException("Start sharing the home before creating an invite.");
        }

        var payload = new InvitePayload(1, home.Id, home.Name, Base64Url(home.Key!), home.Topic!, home.RelayUrl.ToString());
        return Prefix + Base64Url(JsonSerializer.SerializeToUtf8Bytes(payload));
    }

    /// <summary>Finds an invite anywhere in <paramref name="text"/>, e.g. a whole forwarded WhatsApp message.</summary>
    public static bool TryDecode(string? text, DateTimeOffset now, out Home home)
    {
        home = null!;
        var match = text is null ? Match.Empty : InviteRegex().Match(text);
        if (!match.Success)
        {
            return false;
        }

        try
        {
            var payload = JsonSerializer.Deserialize<InvitePayload>(FromBase64Url(match.Groups[1].Value));
            if (payload is not { V: 1 } || string.IsNullOrWhiteSpace(payload.Id) || string.IsNullOrWhiteSpace(payload.Topic)
                || !Uri.TryCreate(payload.Relay, UriKind.Absolute, out var relay) || relay.Scheme != Uri.UriSchemeHttps)
            {
                return false;
            }

            var key = FromBase64Url(payload.Key);
            if (key.Length != 32)
            {
                return false;
            }

            home = new Home(payload.Id, string.IsNullOrWhiteSpace(payload.Name) ? "Family home" : payload.Name.Trim(), key, payload.Topic, relay, now);
            return true;
        }
        catch (Exception ex) when (ex is JsonException or FormatException)
        {
            return false;
        }
    }

    private static string Base64Url(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string text)
    {
        var s = text.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(s.PadRight(s.Length + ((4 - (s.Length % 4)) % 4), '='));
    }

    [GeneratedRegex(@"HTHOME1:([A-Za-z0-9_-]{40,})")]
    private static partial Regex InviteRegex();

    private sealed record InvitePayload(int V, string Id, string Name, string Key, string Topic, string Relay);
}
