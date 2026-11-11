using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;

namespace HomeTracker.Core.AppUpdater;

/// <param name="DownloadUrl">Direct link to the installer file attached to the release.</param>
public sealed record AvailableUpdate(Version Version, string TagName, Uri DownloadUrl, Uri ReleasePageUrl, string? Notes);

/// <summary>
/// Checks a public GitHub repository's latest release (drafts and pre-releases are ignored by GitHub)
/// for a newer version. Only download links inside that repository's releases are accepted.
/// </summary>
public sealed class GitHubReleaseChecker
{
    private readonly HttpClient _http;
    private readonly string _owner;
    private readonly string _repo;
    private readonly string _assetSuffix;

    /// <param name="assetSuffix">File ending of the installer to pick from the release, e.g. ".apk".</param>
    public GitHubReleaseChecker(HttpClient http, string owner, string repo, string assetSuffix)
    {
        _http = http;
        _owner = owner;
        _repo = repo;
        _assetSuffix = assetSuffix;
    }

    /// <returns>The newer release, or null when up to date or the release has no matching file.</returns>
    /// <exception cref="HttpRequestException">No connection or GitHub unavailable.</exception>
    public async Task<AvailableUpdate?> CheckAsync(Version currentVersion, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{_owner}/{_repo}/releases/latest");
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("HomeTracker", currentVersion.ToString()));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        using var response = await _http.SendAsync(request, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null; // No releases published yet.
        }

        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        return Parse(json.RootElement, currentVersion);
    }

    internal AvailableUpdate? Parse(JsonElement release, Version currentVersion)
    {
        var tag = release.GetProperty("tag_name").GetString() ?? "";
        if (!TryParseVersion(tag, out var version) || version <= Normalize(currentVersion))
        {
            return null;
        }

        var allowedPrefix = $"https://github.com/{_owner}/{_repo}/releases/download/";
        foreach (var asset in release.GetProperty("assets").EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString() ?? "";
            var url = asset.GetProperty("browser_download_url").GetString() ?? "";
            if (name.EndsWith(_assetSuffix, StringComparison.OrdinalIgnoreCase)
                && url.StartsWith(allowedPrefix, StringComparison.OrdinalIgnoreCase)
                && Uri.TryCreate(url, UriKind.Absolute, out var downloadUrl))
            {
                var page = release.TryGetProperty("html_url", out var html) && Uri.TryCreate(html.GetString(), UriKind.Absolute, out var p)
                    ? p
                    : new Uri($"https://github.com/{_owner}/{_repo}/releases");
                var notes = release.TryGetProperty("body", out var body) ? body.GetString() : null;
                return new AvailableUpdate(version, tag, downloadUrl, page, notes);
            }
        }

        return null;
    }

    /// <summary>Accepts tags like "v1.2.0", "1.2" or "v1.2.0-beta" (suffix ignored).</summary>
    public static bool TryParseVersion(string tag, out Version version)
    {
        var text = tag.Trim().TrimStart('v', 'V');
        var dash = text.IndexOfAny(['-', '+']);
        if (dash >= 0)
        {
            text = text[..dash];
        }

        if (int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var major))
        {
            text = $"{major}.0";
        }

        if (!Version.TryParse(text, out var parsed))
        {
            version = new Version(0, 0, 0);
            return false;
        }

        version = Normalize(parsed);
        return true;
    }

    /// <summary>Treats missing parts as 0 so that 1.2 equals 1.2.0.</summary>
    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));
}
