using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace HomeTracker.Core.Relay;

/// <param name="Id">The relay's message id; pass the last one seen as <c>since</c> to get only newer messages.</param>
public sealed record RelayMessage(string Id, DateTimeOffset Time, string Text);

/// <summary>
/// Sends and fetches short text messages through an ntfy server (the free public ntfy.sh or a self-hosted one).
/// No account is needed: anyone who knows a topic name can read and write it, so topics must be unguessable
/// and messages should be encrypted before they are sent.
/// </summary>
public sealed partial class NtfyRelay(HttpClient http, Uri server, string userAgent)
{
    /// <summary>ntfy.sh turns larger messages into attachments; stay below this.</summary>
    public const int MaxMessageLength = 3800;

    /// <exception cref="HttpRequestException">No connection, or the server refused (e.g. daily limit reached).</exception>
    public async Task<string> PublishAsync(string topic, string text, CancellationToken cancellationToken = default)
    {
        CheckTopic(topic);
        if (text.Length > MaxMessageLength)
        {
            throw new ArgumentException($"Messages must be at most {MaxMessageLength} characters.", nameof(text));
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, TopicUrl(topic))
        {
            Content = new StringContent(text, Encoding.UTF8, "text/plain"),
        };
        AddHeaders(request);
        // Don't forward to Google's push service: only our own app reads these messages.
        request.Headers.Add("X-Firebase", "no");

        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return json.RootElement.GetProperty("id").GetString() ?? "";
    }

    /// <param name="since">The id of the last message already seen, or null for everything the server still keeps (ntfy.sh: 12 hours).</param>
    /// <exception cref="HttpRequestException">No connection or server error.</exception>
    public async Task<IReadOnlyList<RelayMessage>> FetchAsync(string topic, string? since, CancellationToken cancellationToken = default)
    {
        CheckTopic(topic);
        var sinceValue = since is not null && IdRegex().IsMatch(since) ? since : "all";
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(TopicUrl(topic) + $"/json?poll=1&since={sinceValue}"));
        AddHeaders(request);

        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return Parse(await response.Content.ReadAsStringAsync(cancellationToken));
    }

    /// <summary>Reads ntfy's one-JSON-object-per-line format, keeping only real messages.</summary>
    internal static IReadOnlyList<RelayMessage> Parse(string body)
    {
        var messages = new List<RelayMessage>();
        foreach (var line in body.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                using var json = JsonDocument.Parse(line);
                var root = json.RootElement;
                if (root.TryGetProperty("event", out var ev) && ev.GetString() == "message"
                    && root.TryGetProperty("id", out var id) && root.TryGetProperty("message", out var message))
                {
                    var time = root.TryGetProperty("time", out var t) && t.TryGetInt64(out var seconds)
                        ? DateTimeOffset.FromUnixTimeSeconds(seconds)
                        : DateTimeOffset.MinValue;
                    messages.Add(new RelayMessage(id.GetString() ?? "", time, message.GetString() ?? ""));
                }
            }
            catch (JsonException)
            {
                // Skip a broken line; the rest is still usable.
            }
        }

        return messages;
    }

    private string TopicUrl(string topic) => server.ToString().TrimEnd('/') + "/" + topic;

    private void AddHeaders(HttpRequestMessage request) =>
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue(userAgent, null));

    private static void CheckTopic(string topic)
    {
        if (!TopicRegex().IsMatch(topic))
        {
            throw new ArgumentException("Topic names may only contain letters, digits, '-' and '_'.", nameof(topic));
        }
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{1,64}$")]
    private static partial Regex TopicRegex();

    [GeneratedRegex("^[A-Za-z0-9]{1,32}$")]
    private static partial Regex IdRegex();
}
