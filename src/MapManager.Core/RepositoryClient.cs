using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace MapManager.Core;

public interface IRepositoryClient
{
    Task<Catalog> FetchCatalogAsync(CancellationToken cancel);
    Task DownloadAsync(MapEntry map, MapFile file, string destination, CancellationToken cancel);
    Task<string> NotesAsync(MapEntry map, CancellationToken cancel);
}

public sealed class RepositoryClient : IRepositoryClient, IDisposable
{
    public const string RepositoryUrl = "https://github.com/Lumbridge/SCCT-Maps";
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromMinutes(10) };
    public RepositoryClient() => http.DefaultRequestHeaders.UserAgent.ParseAdd("SCCT-Map-Manager/0.1");
    public async Task<Catalog> FetchCatalogAsync(CancellationToken cancel)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);timeout.CancelAfter(TimeSpan.FromSeconds(25));
        try
        {
        using var response = await http.GetAsync("https://api.github.com/repos/Lumbridge/SCCT-Maps/git/trees/main?recursive=1", timeout.Token);
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
            throw new IOException("GitHub's request limit was reached. Your downloaded maps still work offline; refresh again later.");
        response.EnsureSuccessStatusCode();
        return CatalogParser.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
        }
        catch (OperationCanceledException) when (!cancel.IsCancellationRequested) { throw new IOException("The catalog server timed out. The saved or bundled catalog is still available."); }
    }
    private static string Raw(MapEntry map, string path)
    {
        CatalogParser.ValidateHash(map.Commit); SafePaths.ValidateRelative(path);
        return "https://raw.githubusercontent.com/Lumbridge/SCCT-Maps/" + map.Commit + "/" + string.Join('/', path.Split('/').Select(Uri.EscapeDataString));
    }
    public async Task DownloadAsync(MapEntry map, MapFile file, string destination, CancellationToken cancel)
    {
        using var response = await http.GetAsync(Raw(map, file.Source), HttpCompletionOption.ResponseHeadersRead, cancel);
        response.EnsureSuccessStatusCode();
        await using var input = await response.Content.ReadAsStreamAsync(cancel);
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 131072, true);
        var buffer = new byte[131072];long length = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, cancel)) != 0)
        {
            length += read;
            if (length > file.Size) throw new InvalidDataException("Download is larger than its catalog entry.");
            await output.WriteAsync(buffer.AsMemory(0, read), cancel);
        }
        if (length != file.Size) throw new InvalidDataException("The map download was incomplete.");
    }
    public async Task<string> NotesAsync(MapEntry map, CancellationToken cancel)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);timeout.CancelAfter(TimeSpan.FromSeconds(12));
        using var response = await http.GetAsync(Raw(map, map.NotesPath), timeout.Token);
        if (response.StatusCode == HttpStatusCode.NotFound) return "No extra notes were supplied. Install into Enhanced SCCT Versus 3.6 and keep the included assets together.";
        response.EnsureSuccessStatusCode();return await response.Content.ReadAsStringAsync(timeout.Token);
    }
    public void Dispose() => http.Dispose();
}

public static class Hashing
{
    // Compare the exact downloaded bytes to the blob ID in the pinned Git tree.
    public static string GitBlob(string path)
    {
        using var stream = File.OpenRead(path);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        hash.AppendData(Encoding.ASCII.GetBytes("blob " + stream.Length + "\0"));
        var buffer = new byte[131072];int n;
        while ((n = stream.Read(buffer)) > 0) hash.AppendData(buffer.AsSpan(0, n));
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }
}
