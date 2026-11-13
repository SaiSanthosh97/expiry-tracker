using System.IO.Compression;
using System.Text.Json;

namespace HomeTracker.Core.Sync;

/// <summary>
/// One message between the phones of a home. <paramref name="Kind"/> says what <paramref name="Body"/> holds;
/// the app defines its own kinds (e.g. "items", "state-request", "check-in").
/// </summary>
/// <param name="From">Sending phone's device id, so a phone can skip its own messages.</param>
public sealed record SyncEnvelope(int V, string Kind, string From, string FromName, DateTimeOffset SentAt, JsonElement? Body = null)
{
    public const int CurrentVersion = 1;

    public T? BodyAs<T>() => Body is { } body ? body.Deserialize<T>(SyncCodec.Json) : default;
}

/// <summary>Turns messages into compact, encrypted text for a relay, and back.</summary>
public static class SyncCodec
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    public static SyncEnvelope Create<T>(string kind, string from, string fromName, DateTimeOffset sentAt, T body) =>
        new(SyncEnvelope.CurrentVersion, kind, from, fromName, sentAt, JsonSerializer.SerializeToElement(body, Json));

    public static string Encode(SyncEnvelope envelope, byte[] key, string label)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(envelope, Json);
        using var buffer = new MemoryStream();
        using (var gzip = new GZipStream(buffer, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            gzip.Write(json);
        }

        return SealedBox.Seal(key, buffer.ToArray(), label);
    }

    /// <returns>The message, or null when it isn't a valid message for this key and label, or comes from a newer app version.</returns>
    public static SyncEnvelope? Decode(string text, byte[] key, string label)
    {
        var compressed = SealedBox.Open(key, text, label);
        if (compressed is null)
        {
            return null;
        }

        try
        {
            using var gzip = new GZipStream(new MemoryStream(compressed), CompressionMode.Decompress);
            var envelope = JsonSerializer.Deserialize<SyncEnvelope>(gzip, Json);
            return envelope is { V: SyncEnvelope.CurrentVersion } ? envelope : null;
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException)
        {
            return null;
        }
    }

    /// <summary>
    /// Splits a list into as few encoded messages as possible, each at most <paramref name="maxLength"/> characters
    /// (relays limit message size; ntfy.sh allows 4096 bytes).
    /// </summary>
    /// <exception cref="ArgumentException">A single entry alone is larger than the limit.</exception>
    public static IReadOnlyList<string> Pack<T>(IReadOnlyList<T> entries, Func<IReadOnlyList<T>, string> encode, int maxLength)
    {
        var messages = new List<string>();
        var start = 0;
        while (start < entries.Count)
        {
            // Grow the batch while it fits; compressed size isn't linear, so measure each step.
            var count = 1;
            var best = encode(Slice(entries, start, 1));
            if (best.Length > maxLength)
            {
                throw new ArgumentException($"Entry {start} is too large for one message.", nameof(entries));
            }

            while (start + count < entries.Count)
            {
                var candidate = encode(Slice(entries, start, count + 1));
                if (candidate.Length > maxLength)
                {
                    break;
                }

                best = candidate;
                count++;
            }

            messages.Add(best);
            start += count;
        }

        return messages;
    }

    private static List<T> Slice<T>(IReadOnlyList<T> list, int start, int count)
    {
        var slice = new List<T>(count);
        for (var i = start; i < start + count; i++)
        {
            slice.Add(list[i]);
        }

        return slice;
    }
}
