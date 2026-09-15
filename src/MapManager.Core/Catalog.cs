using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MapManager.Core;

public record MapFile(string Source, string Destination, string Hash, long Size);
public record MapEntry(string Id, string Name, string Category, string Version, string Commit, string NotesPath, List<MapFile> Files)
{
    public string Fingerprint => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n',
        Files.OrderBy(f => f.Destination, StringComparer.OrdinalIgnoreCase).Select(f => f.Destination.ToLowerInvariant() + ":" + f.Hash)))));
    public long Bytes => Files.Sum(f => f.Size);
    public string Packages => string.Join(", ", Files.Where(f => f.Destination.StartsWith("Packages/Maps/", StringComparison.OrdinalIgnoreCase)).Select(f => Path.GetFileNameWithoutExtension(f.Destination)));
    public bool HasSource => Files.Any(f => f.Destination.StartsWith("Packages/MapsEd/", StringComparison.OrdinalIgnoreCase));
    public List<MapFile> InstallFiles(bool source) => Files.Where(f => source || !f.Destination.StartsWith("Packages/MapsEd/", StringComparison.OrdinalIgnoreCase)).ToList();
    public bool IsActivationFile(string path) => path.StartsWith("Packages/Maps/", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("Packages/MapsEd/", StringComparison.OrdinalIgnoreCase)
        || (path.StartsWith("Packages/Textures/", StringComparison.OrdinalIgnoreCase) && Files.Any(f =>
            f.Destination.StartsWith("Packages/Maps/", StringComparison.OrdinalIgnoreCase) &&
            Path.GetFileName(path).Equals(Path.GetFileNameWithoutExtension(f.Destination) + "-i.utc", StringComparison.OrdinalIgnoreCase)));
}
public record Catalog(string Commit, DateTimeOffset CheckedAt, List<MapEntry> Maps);
public record TransferProgress(string Message, int Completed, int Total, long Bytes = 0);

public static class CatalogPresentation
{
    public const string JpMaps = "JP's Maps";
    public static bool IsJpMap(MapEntry map) => map.Id.StartsWith("original/", StringComparison.Ordinal);
    public static IEnumerable<MapEntry> Order(IEnumerable<MapEntry> maps) => maps
        .Select(m => IsJpMap(m) ? m with { Category = JpMaps } : m)
        .OrderByDescending(IsJpMap)
        .ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
        .ThenBy(m => m.Category, StringComparer.OrdinalIgnoreCase);
}

public static class CatalogParser
{
    private sealed record Blob(string Path, string Hash, long Size);
    public static Catalog Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.GetProperty("truncated").GetBoolean()) throw new InvalidDataException("GitHub returned an incomplete catalog. Please retry later.");
        var commit = root.GetProperty("sha").GetString()!;
        ValidateHash(commit);
        var blobs = root.GetProperty("tree").EnumerateArray().Where(x => x.GetProperty("type").GetString() == "blob")
            .Select(x => new Blob(x.GetProperty("path").GetString()!, x.GetProperty("sha").GetString()!, x.GetProperty("size").GetInt64())).ToList();
        var roots = new Dictionary<string, (string Id, string Name, string Category, string Version)>(StringComparer.Ordinal);
        foreach (var b in blobs)
        {
            var parts = b.Path.Split('/');
            if (parts.Length < 5 || !b.Path.EndsWith(".sdc", StringComparison.OrdinalIgnoreCase)) continue;
            if (parts[0] == "release" && parts.Length == 6 && parts[3] == "Packages" && parts[4] == "Maps")
                roots[string.Join('/', parts.Take(3))] = ($"original/{parts[1]}", parts[1] == "ShipD" ? "Shipment" : parts[1], "JP's Maps", parts[2]);
            else if (parts.Length == 5 && parts[2] == "Packages" && parts[3] == "Maps" && parts[1] != "_shared")
            {
                string? category = parts[0] switch { "community" => "Community", "enhanced" => "Enhanced", "recovered" => "Recovered", _ => null };
                if (category != null) roots[string.Join('/', parts.Take(2))] = ($"{parts[0]}/{parts[1]}", parts[1], category, "Latest");
            }
        }
        var maps = new List<MapEntry>();
        foreach (var group in roots.GroupBy(r => r.Value.Id))
        {
            var selected = group.OrderByDescending(r => VersionKey(r.Value.Version), StringComparer.Ordinal).First();
            var mapRoot = selected.Key;
            var info = selected.Value;
            var files = new Dictionary<string, MapFile>(StringComparer.OrdinalIgnoreCase);
            void Include(string prefix)
            {
                foreach (var b in blobs.Where(b => b.Path.StartsWith(prefix + "/", StringComparison.Ordinal)))
                {
                    var dest = b.Path[(prefix.Length + 1)..];
                    if (!SafePaths.IsMapAsset(dest)) continue;
                    SafePaths.ValidateRelative(dest); ValidateHash(b.Hash);
                    if (b.Size < 0 || b.Size > 2L * 1024 * 1024 * 1024) throw new InvalidDataException("Unsupported map file size.");
                    if (files.TryGetValue(dest, out var prior) && prior.Hash != b.Hash)
                        throw new InvalidDataException($"Conflicting shared files in {info.Name}: {dest}");
                    files[dest] = new MapFile(b.Path, dest, b.Hash, b.Size);
                }
            }
            Include(mapRoot);
            if (info.Category == "Community") Include("community/_shared");
            if (files.Count == 0) continue;
            var notes = blobs.FirstOrDefault(b => b.Path.Equals(mapRoot + "/README.md", StringComparison.OrdinalIgnoreCase))?.Path
                ?? blobs.FirstOrDefault(b => b.Path.Equals(mapRoot + "/README.txt", StringComparison.OrdinalIgnoreCase))?.Path ?? mapRoot + "/README.md";
            maps.Add(new MapEntry(info.Id, info.Name, info.Category, info.Version, commit, notes, files.Values.ToList()));
        }
        if (maps.Count == 0) throw new InvalidDataException("The repository contains no supported maps.");
        return new Catalog(commit, DateTimeOffset.UtcNow, CatalogPresentation.Order(maps).ToList());
    }
    private static string VersionKey(string value) => Regex.Replace(value.TrimStart('v', 'V'), @"\d+", m => m.Value.PadLeft(10, '0'));
    public static void ValidateHash(string hash)
    {
        if (!Regex.IsMatch(hash, "^[a-fA-F0-9]{40}$")) throw new InvalidDataException("Invalid Git object hash.");
    }
}

public static class SafePaths
{
    public static void ValidateRelative(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Contains('\\') || Path.IsPathRooted(path)) throw new InvalidDataException("Unsafe map path.");
        foreach (var part in path.Split('/'))
        {
            if (part.Length == 0 || part is "." or ".." || part.EndsWith('.') || part.EndsWith(' ') || part.IndexOfAny([':', '*', '?', '"', '<', '>', '|', '\0']) >= 0 || part.Any(char.IsControl))
                throw new InvalidDataException("Unsafe map path: " + path);
            if (Regex.IsMatch(part.Split('.')[0], "^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])$", RegexOptions.IgnoreCase)) throw new InvalidDataException("Reserved file name.");
        }
    }
    public static string Under(string root, string relative)
    {
        ValidateRelative(relative);
        var basePath = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(basePath, relative));
        if (!path.StartsWith(basePath, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("File escapes the installation.");
        for (string? p = path; p != null && p.Length >= basePath.TrimEnd(Path.DirectorySeparatorChar).Length; p = Path.GetDirectoryName(p))
            if ((File.Exists(p) || Directory.Exists(p)) && (File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Linked folders are not supported: " + p);
        return path;
    }
    public static bool IsMapAsset(string path)
    {
        var p = path.Split('/');
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (p.Length == 3 && p[0] == "System" && p[1] == "_PC_") return new[] { ".int", ".eng", ".fra", ".deu", ".ita", ".esp" }.Contains(ext);
        if (p.Length != 3 || p[0] != "Packages") return false;
        return p[1] switch
        {
            "Maps" or "MapsEd" => ext == ".sdc",
            "Textures" => ext is ".utx" or ".utc",
            "StaticMeshes" => ext == ".usx",
            "Sounds" => ext is ".uax" or ".uas" or ".mux" or ".usxx",
            "Animations" => ext == ".ukx",
            _ => false
        };
    }
}
