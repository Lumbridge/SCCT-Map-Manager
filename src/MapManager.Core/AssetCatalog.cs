using System.Text.Json;
using System.Text.RegularExpressions;

namespace MapManager.Core;

// Large packages live on versioned GitHub releases; the pinned manifest supplies their exact blob hashes.
public static class AssetCatalog
{
    public static List<MapEntry> Parse(string json, string commit)
    {
        CatalogParser.ValidateHash(commit);
        var entries = JsonSerializer.Deserialize<List<MapEntry>>(json) ?? throw new InvalidDataException("Invalid asset catalog.");
        foreach (var entry in entries)
        {
            var port = entry.IsPort;
            if ((!entry.IsAssetPack && !port) || entry.Id.Split('/').Length != 2 || string.IsNullOrWhiteSpace(entry.Name)
                || !Regex.IsMatch(entry.Version, @"^v\d+\.\d+\.\d+$") || entry.Files.Count == 0)
                throw new InvalidDataException("Invalid asset pack identity or version.");
            if (port && string.IsNullOrWhiteSpace(entry.Game)) throw new InvalidDataException("Port entries must identify their source game.");
            SafePaths.ValidateRelative(entry.Id);SafePaths.ValidateRelative(entry.NotesPath);
            if (entry.NotesPath != (port ? $"{entry.Id}/README.md" : $"{entry.Id}/{entry.Version}/README.md")) throw new InvalidDataException("Invalid asset release notes path.");
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in entry.Files)
            {
                SafePaths.ValidateRelative(file.Source);SafePaths.ValidateRelative(file.Destination);CatalogParser.ValidateHash(file.Hash);
                var source = file.Source.Split('/');
                if (source.Length != 4 || source[0] != "releases" || source[1] != "download" || !source[2].EndsWith("-" + entry.Version, StringComparison.Ordinal)
                    || !(port ? SafePaths.IsMapAsset(file.Destination) : SafePaths.IsEditorAsset(file.Destination)) || !paths.Add(file.Destination) || file.Size < 0 || file.Size > 2L * 1024 * 1024 * 1024)
                    throw new InvalidDataException("Invalid asset download.");
            }
        }
        return entries.GroupBy(p => p.Id).Select(g => g.OrderByDescending(p => Version.Parse(p.Version[1..])).First() with { Commit = commit }).ToList();
    }
}
