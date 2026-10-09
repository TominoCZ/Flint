using System.IO.Compression;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Flint.App.Setup;

/// <summary>Fetches the tools setup needs (adb, jadx) and unpacks them into Flint's own folder.</summary>
internal static class Download
{
    private static readonly HttpClient Http = new() { DefaultRequestHeaders = { { "User-Agent", "Flint" } } }; // GitHub's API wants one

    /// <summary>Downloads a zip and unpacks it into <paramref name="folder"/>, reporting the share downloaded (0 to 1) as it goes.</summary>
    public static async Task ZipAsync(Uri url, string folder, IProgress<double> progress, CancellationToken cancel)
    {
        Directory.CreateDirectory(folder);
        var zip = Path.Combine(folder, "download.zip");
        try
        {
            using (var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancel))
            {
                response.EnsureSuccessStatusCode();
                var total = response.Content.Headers.ContentLength;
                await using var from = await response.Content.ReadAsStreamAsync(cancel);
                await using var to = File.Create(zip);
                var buffer = new byte[81920];
                long done = 0;
                int read;
                while ((read = await from.ReadAsync(buffer, cancel)) > 0)
                {
                    await to.WriteAsync(buffer.AsMemory(0, read), cancel);
                    done += read;
                    if (total > 0) progress.Report((double)done / total.Value);
                }
            }
            // Keeps the files' Unix permissions, so the tools' launchers stay executable.
            await Task.Run(() => ZipFile.ExtractToDirectory(zip, folder, overwriteFiles: true), cancel);
        }
        finally
        {
            File.Delete(zip);
        }
    }

    /// <summary>The download URL of the newest release asset of a GitHub repository whose name matches <paramref name="asset"/>.</summary>
    public static async Task<Uri> LatestReleaseAsync(string repository, Regex asset, CancellationToken cancel)
    {
        var release = await Http.GetFromJsonAsync<JsonObject>($"https://api.github.com/repos/{repository}/releases/latest", cancel)
                      ?? throw new IOException($"no release of {repository}");
        var url = release["assets"]!.AsArray().FirstOrDefault(a => asset.IsMatch((string)a!["name"]!))?["browser_download_url"]
                  ?? throw new IOException($"{repository}'s latest release has no {asset}");
        return new Uri((string)url!);
    }
}
