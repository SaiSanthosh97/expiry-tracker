using System.Net;
using HomeTracker.Core.Relay;

namespace HomeTracker.Core.Relay.Tests;

public class NtfyRelayTests
{
    private sealed class Recorder(string responseBody) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(responseBody) };
        }
    }

    private static readonly Uri Server = new("https://ntfy.sh");

    [Fact]
    public async Task Publish_posts_text_to_the_topic_without_firebase()
    {
        var handler = new Recorder("""{"id":"abc123","time":1,"event":"message","topic":"httopic","message":"x"}""");
        var relay = new NtfyRelay(new HttpClient(handler), Server, "HomeTracker");

        var id = await relay.PublishAsync("httopic", "1.sealed");

        Assert.Equal("abc123", id);
        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal("https://ntfy.sh/httopic", handler.Request.RequestUri!.ToString());
        Assert.Equal("1.sealed", handler.Body);
        Assert.Equal("no", handler.Request.Headers.GetValues("X-Firebase").Single());
    }

    [Fact]
    public async Task Fetch_asks_for_messages_since_the_last_seen_id_and_keeps_only_messages()
    {
        var handler = new Recorder(
            """
            {"id":"m1","time":1763100000,"event":"message","topic":"t","message":"first"}
            {"id":"k1","time":1763100001,"event":"keepalive","topic":"t"}
            not json
            {"id":"m2","time":1763100002,"event":"message","topic":"t","message":"second"}
            """);
        var relay = new NtfyRelay(new HttpClient(handler), Server, "HomeTracker");

        var messages = await relay.FetchAsync("t", "m0");

        Assert.Equal("https://ntfy.sh/t/json?poll=1&since=m0", handler.Request!.RequestUri!.ToString());
        Assert.Equal(["first", "second"], messages.Select(m => m.Text));
        Assert.Equal("m2", messages[^1].Id);
    }

    [Fact]
    public async Task Fetch_without_a_valid_since_gets_everything_kept()
    {
        var handler = new Recorder("");
        var relay = new NtfyRelay(new HttpClient(handler), Server, "HomeTracker");

        await relay.FetchAsync("t", "../../etc");

        Assert.EndsWith("since=all", handler.Request!.RequestUri!.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("a/b")]
    [InlineData("a b")]
    public async Task Rejects_bad_topic_names(string topic)
    {
        var relay = new NtfyRelay(new HttpClient(new Recorder("")), Server, "HomeTracker");
        await Assert.ThrowsAsync<ArgumentException>(() => relay.PublishAsync(topic, "x"));
    }

    [Fact]
    public async Task Rejects_messages_that_are_too_long()
    {
        var relay = new NtfyRelay(new HttpClient(new Recorder("")), Server, "HomeTracker");
        await Assert.ThrowsAsync<ArgumentException>(() => relay.PublishAsync("t", new string('x', NtfyRelay.MaxMessageLength + 1)));
    }
}
