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
        var catalog = CatalogParser.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
        using var assets = await http.GetAsync($"https://raw.githubusercontent.com/Lumbridge/SCCT-Maps/{catalog.Commit}/asset-catalog.json", timeout.Token);
        if (assets.StatusCode != HttpStatusCode.NotFound)
        {
            assets.EnsureSuccessStatusCode();
            var packs = AssetCatalog.Parse(await assets.Content.ReadAsStringAsync(timeout.Token), catalog.Commit);
            catalog = catalog with {
                Maps = CatalogPresentation.Order(catalog.Maps.Concat(packs.Where(p => p.IsPort))).ToList(),
                AssetPacks = packs.Where(p => p.IsAssetPack).Concat(catalog.AssetPacks).DistinctBy(p => p.Id).OrderBy(p => p.Name).ToList() };
        }
        return catalog;
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
        var url = (map.IsAssetPack || map.IsPort) && file.Source.StartsWith("releases/download/", StringComparison.Ordinal)
            ? ReleaseAsset(file.Source) : Raw(map, file.Source);
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancel);
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
    private static string ReleaseAsset(string path)
    {
        SafePaths.ValidateRelative(path);
        if (path.Split('/').Length != 4) throw new InvalidDataException("Invalid release asset path.");
        return RepositoryUrl + "/" + string.Join('/', path.Split('/').Select(Uri.EscapeDataString));
    }
    public async Task<string> NotesAsync(MapEntry map, CancellationToken cancel)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);timeout.CancelAfter(TimeSpan.FromSeconds(12));
        using var response = await http.GetAsync(Raw(map, map.NotesPath), timeout.Token);
        if (response.StatusCode == HttpStatusCode.NotFound) return map.IsAssetPack
            ? "No release notes were supplied. Keep all files in this pack together when using them in the editor."
            : "No extra notes were supplied. Install into Enhanced SCCT Versus 3.6 and keep the included assets together.";
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
