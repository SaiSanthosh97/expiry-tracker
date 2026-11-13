using System.Text.Json;

namespace HomeTracker.Core.AppUpdater.Tests;

public class GitHubReleaseCheckerTests
{
    private static readonly GitHubReleaseChecker Checker = new(new HttpClient(), "SaiSanthosh97", "expiry-tracker", ".apk");

    private static JsonElement Release(string tag, string assetName, string url) => JsonDocument.Parse($$"""
        {
          "tag_name": "{{tag}}",
          "html_url": "https://github.com/SaiSanthosh97/expiry-tracker/releases/tag/{{tag}}",
          "body": "Bug fixes",
          "assets": [ { "name": "{{assetName}}", "browser_download_url": "{{url}}" } ]
        }
        """).RootElement;

    private const string GoodUrl = "https://github.com/SaiSanthosh97/expiry-tracker/releases/download/v0.2.0/home-tracker.apk";

    [Theory]
    [InlineData("v1.2.0", 1, 2, 0)]
    [InlineData("1.2", 1, 2, 0)]
    [InlineData("V2", 2, 0, 0)]
    [InlineData("v1.2.3-beta", 1, 2, 3)]
    public void Parses_version_tags(string tag, int major, int minor, int build)
    {
        Assert.True(GitHubReleaseChecker.TryParseVersion(tag, out var version));
        Assert.Equal(new Version(major, minor, build), version);
    }

    [Theory]
    [InlineData("latest")]
    [InlineData("")]
    public void Rejects_non_version_tags(string tag)
    {
        Assert.False(GitHubReleaseChecker.TryParseVersion(tag, out _));
    }

    [Fact]
    public void Newer_release_with_apk_is_offered()
    {
        var update = Checker.Parse(Release("v0.2.0", "home-tracker.apk", GoodUrl), new Version(0, 1));

        Assert.NotNull(update);
        Assert.Equal(new Version(0, 2, 0), update.Version);
        Assert.Equal(new Uri(GoodUrl), update.DownloadUrl);
    }

    [Fact]
    public void Reads_the_published_checksum_and_size()
    {
        var json = JsonDocument.Parse($$"""
            {
              "tag_name": "v0.3.0",
              "assets": [ { "name": "home-tracker.apk", "browser_download_url": "{{GoodUrl}}", "size": 1234,
                            "digest": "sha256:D660256F6B67C95190BAEA8937E952E91FB022710CED072FCC087AA3E5F97272" } ]
            }
            """).RootElement;

        var update = Checker.Parse(json, new Version(0, 2));

        Assert.Equal("d660256f6b67c95190baea8937e952e91fb022710ced072fcc087aa3e5f97272", update?.Sha256);
        Assert.Equal(1234, update?.Size);
    }

    [Fact]
    public void Missing_or_malformed_checksum_is_null()
    {
        Assert.Null(Checker.Parse(Release("v0.3.0", "home-tracker.apk", GoodUrl), new Version(0, 2))?.Sha256);
    }

    [Fact]
    public void Same_or_older_release_is_ignored()
    {
        Assert.Null(Checker.Parse(Release("v0.2.0", "home-tracker.apk", GoodUrl), new Version(0, 2, 0)));
        Assert.Null(Checker.Parse(Release("v0.1.0", "home-tracker.apk", GoodUrl), new Version(0, 2)));
    }

    [Fact]
    public void Release_without_apk_is_ignored()
    {
        Assert.Null(Checker.Parse(Release("v0.2.0", "notes.txt", GoodUrl.Replace(".apk", ".txt")), new Version(0, 1)));
    }

    [Theory]
    [InlineData("https://example.com/home-tracker.apk")]
    [InlineData("https://github.com/someone-else/expiry-tracker/releases/download/v0.2.0/home-tracker.apk")]
    [InlineData("http://github.com/SaiSanthosh97/expiry-tracker/releases/download/v0.2.0/home-tracker.apk")]
    public void Download_links_outside_this_repository_are_rejected(string url)
    {
        Assert.Null(Checker.Parse(Release("v0.2.0", "home-tracker.apk", url), new Version(0, 1)));
    }
}
