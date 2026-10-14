using System.Net.Http.Headers;
using System.Text.Json;

namespace AITweaker.App.Platform;

public static class Downloader
{
    public static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromHours(3) };
        c.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("AITweaker", "1.0"));
        return c;
    }

    public static async Task DownloadAsync(string url, string target, Action<long, long?>? progress, CancellationToken ct = default)
    {
        using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        long? total = resp.Content.Headers.ContentLength;
        var tmp = target + ".part";
        try
        {
            await using (var src = await resp.Content.ReadAsStreamAsync(ct))
            await using (var dst = File.Create(tmp))
            {
                var buffer = new byte[1 << 20];
                long done = 0, lastReport = 0;
                int read;
                while ((read = await src.ReadAsync(buffer, ct)) > 0)
                {
                    await dst.WriteAsync(buffer.AsMemory(0, read), ct);
                    done += read;
                    if (done - lastReport > 4 << 20 || done == total)
                    {
                        progress?.Invoke(done, total);
                        lastReport = done;
                    }
                }
            }
            File.Move(tmp, target, overwrite: true);
        }
        catch
        {
            // a cancelled or broken multi-GB download doesn't stay on the disk
            try { File.Delete(tmp); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            throw;
        }
    }

    /// <summary>Throws (and deletes the file) unless Windows confirms a valid Authenticode signature by <paramref name="signer"/>.</summary>
    public static async Task RequireSignatureAsync(string file, string signer, CancellationToken ct = default)
    {
        var sig = await ProcessRunner.PowerShellAsync($"$s = Get-AuthenticodeSignature -FilePath {ProcessRunner.PsQuote(file)}; \"$($s.Status)|$($s.SignerCertificate.Subject)\"", TimeSpan.FromSeconds(30), ct: ct);
        if (sig.Output.Contains("Valid|") && sig.Output.Contains(signer, StringComparison.OrdinalIgnoreCase)) return;
        File.Delete(file);
        throw new InvalidOperationException($"{Path.GetFileName(file)} is not signed by {signer}; it was deleted. {sig.Output.Trim()}");
    }

    /// <summary>Returns the download URL of the first asset in the latest GitHub release whose name ends with <paramref name="suffix"/>.</summary>
    public static Task<(string Name, string Url)> LatestGitHubAssetAsync(string repo, string suffix, CancellationToken ct = default) =>
        LatestGitHubAssetAsync(repo, n => n.EndsWith(suffix, StringComparison.OrdinalIgnoreCase), ct);

    /// <summary>Returns the first asset in the latest GitHub release whose file name matches <paramref name="match"/>.</summary>
    public static async Task<(string Name, string Url)> LatestGitHubAssetAsync(string repo, Func<string, bool> match, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{repo}/releases/latest");
        req.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var resp = await Http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        foreach (var asset in doc.RootElement.GetProperty("assets").EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString() ?? "";
            if (match(name))
                return (name, asset.GetProperty("browser_download_url").GetString()!);
        }
        throw new InvalidOperationException($"No matching asset in the latest {repo} release");
    }
}
