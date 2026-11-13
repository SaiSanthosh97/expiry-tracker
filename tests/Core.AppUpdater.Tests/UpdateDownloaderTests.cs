using System.Net;
using System.Security.Cryptography;

namespace HomeTracker.Core.AppUpdater.Tests;

public class UpdateDownloaderTests : IDisposable
{
    private static readonly byte[] Payload = [1, 2, 3, 4, 5, 6, 7, 8, 9];
    private readonly string _target = Path.Combine(Path.GetTempPath(), $"ht-update-{Guid.NewGuid():N}.apk");

    private sealed class FixedHandler(byte[] body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) });
    }

    private static AvailableUpdate Update(string? sha256, long? size = null) => new(
        new Version(0, 3, 0),
        "v0.3.0",
        new Uri("https://github.com/SaiSanthosh97/expiry-tracker/releases/download/v0.3.0/home-tracker.apk"),
        new Uri("https://github.com/SaiSanthosh97/expiry-tracker/releases/tag/v0.3.0"),
        null,
        sha256,
        size);

    private static string Sha(byte[] data) => Convert.ToHexStringLower(SHA256.HashData(data));

    public void Dispose()
    {
        File.Delete(_target);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Saves_the_file_when_the_checksum_matches_and_reports_progress()
    {
        var reports = new List<double>();
        var downloader = new UpdateDownloader(new HttpClient(new FixedHandler(Payload)));

        await downloader.DownloadAsync(Update(Sha(Payload), Payload.Length), _target, new SyncProgress(reports.Add));

        Assert.Equal(Payload, await File.ReadAllBytesAsync(_target));
        Assert.Equal(1.0, reports[^1]);
    }

    [Fact]
    public async Task Deletes_the_file_when_the_checksum_differs()
    {
        var downloader = new UpdateDownloader(new HttpClient(new FixedHandler(Payload)));

        await Assert.ThrowsAsync<DamagedDownloadException>(() => downloader.DownloadAsync(Update(Sha([9, 9, 9])), _target));

        Assert.False(File.Exists(_target));
        Assert.False(File.Exists(_target + ".part"));
    }

    [Fact]
    public async Task Rejects_an_incomplete_download()
    {
        var downloader = new UpdateDownloader(new HttpClient(new FixedHandler(Payload)));

        await Assert.ThrowsAsync<DamagedDownloadException>(() => downloader.DownloadAsync(Update(null, Payload.Length + 10), _target));

        Assert.False(File.Exists(_target));
    }

    private sealed class SyncProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }
}
