using System.Security.Cryptography;

namespace HomeTracker.Core.AppUpdater;

/// <summary>Thrown when a downloaded installer doesn't match the checksum GitHub published for it.</summary>
public sealed class DamagedDownloadException() : Exception("The downloaded file does not match the published checksum.");

/// <summary>
/// Downloads an update's installer to a file. When GitHub published a SHA-256 for the file, the download
/// is checked against it and deleted if it doesn't match, so a broken or altered file is never installed.
/// </summary>
public sealed class UpdateDownloader(HttpClient http)
{
    /// <param name="progress">Receives 0–1 when the size is known.</param>
    /// <exception cref="HttpRequestException">No connection.</exception>
    /// <exception cref="DamagedDownloadException">The file is incomplete or doesn't match its checksum.</exception>
    public async Task DownloadAsync(AvailableUpdate update, string targetPath, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        var partial = targetPath + ".part";
        try
        {
            using var response = await http.GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength ?? update.Size;

            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long received = 0;
            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var target = File.Create(partial))
            {
                var buffer = new byte[81920];
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    hash.AppendData(buffer, 0, read);
                    received += read;
                    if (total is > 0)
                    {
                        progress?.Report(Math.Min(1, (double)received / total.Value));
                    }
                }
            }

            var actual = Convert.ToHexStringLower(hash.GetHashAndReset());
            if ((update.Size is { } size && size != received) || (update.Sha256 is { } expected && expected != actual))
            {
                throw new DamagedDownloadException();
            }

            File.Move(partial, targetPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(partial))
            {
                File.Delete(partial);
            }
        }
    }
}
