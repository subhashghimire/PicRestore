using System.Security.Cryptography;

namespace PicRestore.Ml;

/// <summary>
/// Finds the LaMa model on disk, downloading it once (~92 MB) on first use. The file is verified against
/// a pinned SHA-256 so a truncated or tampered download is never loaded.
///
/// Search order: %LOCALAPPDATA%\PicRestore\Models, then a "Models" folder next to the app. Placing the
/// file in either location manually (e.g. on an offline machine) skips the download.
/// </summary>
public static class LamaModelStore
{
    public const string FileName = "inpainting_lama_2025jan.onnx";

    /// <summary>OpenCV Zoo (Apache-2.0). Pinned by <see cref="Sha256"/>, not by URL alone.</summary>
    public const string DownloadUrl =
        "https://media.githubusercontent.com/media/opencv/opencv_zoo/main/models/inpainting_lama/inpainting_lama_2025jan.onnx";

    public const string Sha256 = "7df918ac3921d3daf0aae1d219776cf0dc4e4935f035af81841b40adcf74fdf2";

    public const long ExpectedBytes = 92_591_623;

    public static string UserModelDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PicRestore", "Models");

    /// <summary>Path of an already-present model file, or null.</summary>
    public static string? FindExisting()
    {
        foreach (string dir in new[] { UserModelDirectory, Path.Combine(AppContext.BaseDirectory, "Models") })
        {
            string path = Path.Combine(dir, FileName);
            if (File.Exists(path) && new FileInfo(path).Length == ExpectedBytes)
            {
                return path;
            }
        }

        return null;
    }

    /// <summary>Returns the model path, downloading and verifying it first if needed.</summary>
    /// <param name="progress">Download progress 0..1 (only reported when a download actually happens).</param>
    public static async Task<string> EnsureAsync(IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        string? existing = FindExisting();
        if (existing is not null)
        {
            return existing;
        }

        Directory.CreateDirectory(UserModelDirectory);
        string target = Path.Combine(UserModelDirectory, FileName);
        string partial = target + ".part";

        using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(15) })
        using (HttpResponseMessage response = await http.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
        {
            response.EnsureSuccessStatusCode();
            long total = response.Content.Headers.ContentLength ?? ExpectedBytes;

            await using Stream source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using (var file = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true))
            {
                var buffer = new byte[1 << 16];
                long received = 0;
                int read;
                double lastReported = -1;
                while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    received += read;
                    double fraction = Math.Min(1.0, (double)received / total);
                    if (fraction - lastReported >= 0.01)
                    {
                        lastReported = fraction;
                        progress?.Report(fraction);
                    }
                }
            }
        }

        string actual;
        await using (FileStream check = File.OpenRead(partial))
        {
            actual = Convert.ToHexString(await SHA256.HashDataAsync(check, cancellationToken).ConfigureAwait(false)).ToLowerInvariant();
        }

        if (!string.Equals(actual, Sha256, StringComparison.Ordinal))
        {
            File.Delete(partial);
            throw new InvalidDataException($"Downloaded model failed its integrity check (SHA-256 {actual[..12]}..., expected {Sha256[..12]}...).");
        }

        File.Move(partial, target, overwrite: true);
        return target;
    }
}
