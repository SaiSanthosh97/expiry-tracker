using System.Security.Cryptography;
using System.Text;
using HomeTracker.Core.Sync;

namespace HomeTracker.Core.Sync.Tests;

public class SyncTests
{
    private static readonly byte[] Key = RandomNumberGenerator.GetBytes(32);
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 9, 0, 0, TimeSpan.Zero);

    private sealed record Note(string Id, string Text);

    [Fact]
    public void Sealed_text_opens_only_with_the_same_key_and_label()
    {
        var sealedText = SealedBox.Seal(Key, Encoding.UTF8.GetBytes("Eye drops"), "home-1");

        Assert.Equal("Eye drops", Encoding.UTF8.GetString(SealedBox.Open(Key, sealedText, "home-1")!));
        Assert.Null(SealedBox.Open(Key, sealedText, "home-2"));
        Assert.Null(SealedBox.Open(RandomNumberGenerator.GetBytes(32), sealedText, "home-1"));
        Assert.DoesNotContain("Eye", sealedText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Altered_or_foreign_text_is_rejected()
    {
        var sealedText = SealedBox.Seal(Key, [1, 2, 3], "h");
        var bytes = Convert.FromBase64String(sealedText[2..]);
        bytes[13] ^= 0x01; // flip one bit of the encrypted content
        var altered = "1." + Convert.ToBase64String(bytes);

        Assert.Null(SealedBox.Open(Key, altered, "h"));
        Assert.Null(SealedBox.Open(Key, "hello", "h"));
        Assert.Null(SealedBox.Open(Key, "1.!!!", "h"));
    }

    [Fact]
    public void Sealed_bytes_round_trip_and_reject_other_labels()
    {
        var photo = RandomNumberGenerator.GetBytes(5000);
        var sealedBytes = SealedBox.SealBytes(Key, photo, "home-1/photo.jpg");

        Assert.Equal(photo, SealedBox.OpenBytes(Key, sealedBytes, "home-1/photo.jpg"));
        Assert.Null(SealedBox.OpenBytes(Key, sealedBytes, "home-1/other.jpg"));
    }

    [Fact]
    public void Same_message_encrypts_differently_each_time()
    {
        Assert.NotEqual(SealedBox.Seal(Key, [1], "h"), SealedBox.Seal(Key, [1], "h"));
    }

    [Fact]
    public void Envelope_round_trips_with_its_body()
    {
        var envelope = SyncCodec.Create("items", "dev-a", "Amma's phone", Now, new[] { new Note("1", "Milk") });

        var decoded = SyncCodec.Decode(SyncCodec.Encode(envelope, Key, "home"), Key, "home");

        Assert.NotNull(decoded);
        Assert.Equal("items", decoded.Kind);
        Assert.Equal("dev-a", decoded.From);
        Assert.Equal("Amma's phone", decoded.FromName);
        Assert.Equal(Now, decoded.SentAt);
        Assert.Equal("Milk", decoded.BodyAs<Note[]>()![0].Text);
    }

    [Fact]
    public void Pack_splits_into_messages_under_the_limit_and_keeps_every_entry()
    {
        var notes = Enumerable.Range(0, 120)
            .Select(i => new Note(Guid.NewGuid().ToString("N"), Convert.ToHexString(RandomNumberGenerator.GetBytes(30))))
            .ToList();
        string Encode(IReadOnlyList<Note> batch) => SyncCodec.Encode(SyncCodec.Create("items", "d", "D", Now, batch), Key, "home");

        var messages = SyncCodec.Pack(notes, Encode, 3800);

        Assert.True(messages.Count > 1);
        Assert.All(messages, m => Assert.True(m.Length <= 3800));
        var received = messages.SelectMany(m => SyncCodec.Decode(m, Key, "home")!.BodyAs<Note[]>()!).ToList();
        Assert.Equal(notes, received);
    }

    [Fact]
    public void Pack_of_nothing_is_no_messages()
    {
        Assert.Empty(SyncCodec.Pack(Array.Empty<Note>(), _ => "x", 10));
    }
}
