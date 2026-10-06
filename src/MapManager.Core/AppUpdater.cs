using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MapManager.Core;

public record AppRelease(Version Version, string Tag, string Notes, string PageUrl, string ExeUrl, long Size, string? Sha256, string? ChecksumsUrl);

// Finds, verifies and swaps in new manager builds published on GitHub releases.
public sealed class AppUpdater : IDisposable
{
    public const string ReleasesUrl = "https://github.com/Lumbridge/SCCT-Map-Manager/releases";
    private const string LatestApi = "https://api.github.com/repos/Lumbridge/SCCT-Map-Manager/releases/latest";
    private const string DownloadPrefix = ReleasesUrl + "/download/";
    // GitHub replaces spaces in uploaded asset names with dots.
    public const string ExeAsset = "SCCT.Map.Manager.exe";
    public const string ExeName = "SCCT Map Manager.exe";
    private const long MaxSize = 512L * 1024 * 1024;
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromMinutes(10) };
    public AppUpdater(Version current)
    {
        Current = Normalize(current);
        http.DefaultRequestHeaders.UserAgent.ParseAdd("SCCT-Map-Manager/" + Current.ToString(3));
    }
    public Version Current { get; }

    public async Task<AppRelease?> CheckAsync(CancellationToken cancel)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);timeout.CancelAfter(TimeSpan.FromSeconds(20));
        using var response = await http.GetAsync(LatestApi, timeout.Token);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
            throw new IOException("GitHub's request limit was reached. Check for updates again later.");
        response.EnsureSuccessStatusCode();
        return ParseLatest(await response.Content.ReadAsStringAsync(timeout.Token), Current);
    }

    // Returns the release only when it is newer than the running build and carries a usable executable.
    public static AppRelease? ParseLatest(string json, Version current)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.TryGetProperty("draft", out var draft) && draft.GetBoolean()) return null;
        if (root.TryGetProperty("prerelease", out var pre) && pre.GetBoolean()) return null;
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        var match = Regex.Match(tag, @"^v?(\d+)\.(\d+)\.(\d+)$");
        if (!match.Success) return null;
        var version = new Version(int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value), int.Parse(match.Groups[3].Value));
        if (version <= Normalize(current)) return null;
        JsonElement? exe = null, sums = null;
        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString();
            if (name == ExeAsset || name == ExeName) exe = asset;
            else if (name == "SHA256SUMS.txt") sums = asset;
        }
        if (exe is not { } found) return null;
        var url = TrustedUrl(found.GetProperty("browser_download_url").GetString());
        var size = found.GetProperty("size").GetInt64();
        if (size <= 0 || size > MaxSize) throw new InvalidDataException("The update download has an unexpected size.");
        string? sha = null;
        if (found.TryGetProperty("digest", out var digest) && digest.ValueKind == JsonValueKind.String
            && Regex.Match(digest.GetString()!, "^sha256:([a-fA-F0-9]{64})$") is { Success: true } d)
            sha = d.Groups[1].Value.ToLowerInvariant();
        var sumsUrl = sums is { } s ? TrustedUrl(s.GetProperty("browser_download_url").GetString()) : null;
        if (sha == null && sumsUrl == null) throw new InvalidDataException("The update has no checksum, so it cannot be verified.");
        var page = root.TryGetProperty("html_url", out var html) && html.GetString() is { } h && h.StartsWith(ReleasesUrl + "/", StringComparison.Ordinal) ? h : ReleasesUrl + "/latest";
        var notes = root.TryGetProperty("body", out var body) ? body.GetString() ?? "" : "";
        return new AppRelease(version, tag, notes, page, url, size, sha, sumsUrl);
    }

    public static string? ChecksumFromList(string sums)
    {
        foreach (var line in sums.Split('\n'))
        {
            var m = Regex.Match(line.Trim(), @"^([a-fA-F0-9]{64})\s+\*?(.+)$");
            if (m.Success && (m.Groups[2].Value == ExeName || m.Groups[2].Value == ExeAsset)) return m.Groups[1].Value.ToLowerInvariant();
        }
        return null;
    }

    // Downloads next to the running executable so the final swap is a same-volume rename.
    public async Task DownloadAsync(AppRelease release, string destination, IProgress<TransferProgress>? progress, CancellationToken cancel)
    {
        var expected = release.Sha256;
        if (expected == null)
        {
            using var sumsResponse = await http.GetAsync(release.ChecksumsUrl!, cancel);
            sumsResponse.EnsureSuccessStatusCode();
            expected = ChecksumFromList(await sumsResponse.Content.ReadAsStringAsync(cancel))
                ?? throw new InvalidDataException("The update checksum list does not include the manager.");
        }
        if (File.Exists(destination)) File.Delete(destination);
        try
        {
            using var response = await http.GetAsync(release.ExeUrl, HttpCompletionOption.ResponseHeadersRead, cancel);
            response.EnsureSuccessStatusCode();
            await using (var input = await response.Content.ReadAsStreamAsync(cancel))
            await using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 131072, true))
            {
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = new byte[131072];long length = 0;int read;
                int total = (int)Math.Max(1, release.Size / 1048576);
                while ((read = await input.ReadAsync(buffer, cancel)) != 0)
                {
                    length += read;
                    if (length > release.Size) throw new InvalidDataException("The update download is larger than expected.");
                    hash.AppendData(buffer, 0, read);
                    await output.WriteAsync(buffer.AsMemory(0, read), cancel);
                    progress?.Report(new TransferProgress($"Downloading {release.Tag} ({length / 1048576} MB)", (int)(length / 1048576), total, length));
                }
                if (length != release.Size) throw new InvalidDataException("The update download was incomplete.");
                if (Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant() != expected)
                    throw new InvalidDataException("The update failed checksum verification and was discarded.");
                await output.FlushAsync(cancel);
            }
        }
        catch { if (File.Exists(destination)) File.Delete(destination); throw; }
    }

    // Windows allows renaming a running executable, so the old build steps aside and the new one takes its name.
    public static void Install(string downloaded, string executable)
    {
        var previous = PreviousPath(executable);
        if (File.Exists(previous)) File.Delete(previous);
        File.Move(executable, previous);
        try { File.Move(downloaded, executable); }
        catch { File.Move(previous, executable); throw; }
    }

    public static string PreviousPath(string executable) => executable + ".old";
    public static string DownloadPath(string executable) => executable + ".download";

    // Removes leftovers from an earlier update once the old process has exited.
    public static void CleanUp(string executable)
    {
        foreach (var path in new[] { PreviousPath(executable), DownloadPath(executable) })
            try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private static string TrustedUrl(string? url)
    {
        if (url == null || !url.StartsWith(DownloadPrefix, StringComparison.Ordinal) || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Host != "github.com"
            || !uri.AbsolutePath.StartsWith("/Lumbridge/SCCT-Map-Manager/releases/download/", StringComparison.Ordinal))
            throw new InvalidDataException("The update points outside the manager's GitHub releases.");
        return url;
    }
    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build));
    public void Dispose() => http.Dispose();
}
