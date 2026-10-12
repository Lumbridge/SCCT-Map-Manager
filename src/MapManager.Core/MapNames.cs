using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MapManager.Core;

// Display names for original maps, keyed by package folder (release/<Package>/...).
// The repository's release/names.json takes precedence; the built-in table is the fallback.
public static class MapNames
{
    public const string Path = "release/names.json";
    public const int MaxBytes = 64 * 1024;
    private const int MaxEntries = 1000, MaxNameLength = 60;
    private static readonly Dictionary<string, string> BuiltIn = new(StringComparer.Ordinal)
    {
        ["ShipD"] = "Shipment",
        ["MoonD"] = "Moonlight Park"
    };

    public static string BuiltInName(string package) => BuiltIn.TryGetValue(package, out var name) ? name : package;

    // Returns the usable entries of a names file. Entries that are not plain, short text are skipped.
    public static Dictionary<string, string> Parse(string json)
    {
        using var doc = JsonDocument.Parse(json.TrimStart((char)0xFEFF));
        if (doc.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException("The map names file must be a JSON object.");
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in doc.RootElement.EnumerateObject())
        {
            if (names.Count >= MaxEntries) break;
            if (entry.Value.ValueKind != JsonValueKind.String || !Regex.IsMatch(entry.Name, "^[A-Za-z0-9_-]{1,64}$")) continue;
            var name = entry.Value.GetString()!.Trim();
            if (name.Length == 0 || name.Length > MaxNameLength
                || name.Any(c => char.IsControl(c) || char.IsSurrogate(c) || char.GetUnicodeCategory(c) == UnicodeCategory.Format)) continue;
            names[entry.Name] = name;
        }
        return names;
    }

    public static Catalog Apply(Catalog catalog, IReadOnlyDictionary<string, string> names)
    {
        if (names.Count == 0) return catalog;
        MapEntry Rename(MapEntry map) => CatalogPresentation.IsJpMap(map) && names.TryGetValue(map.Id["original/".Length..], out var name) && name != map.Name
            ? map with { Name = name } : map;
        return catalog with
        {
            Maps = CatalogPresentation.Order(catalog.Maps.Select(Rename)).ToList(),
            Versions = catalog.Versions.ToDictionary(v => v.Key, v => v.Value.Select(Rename).ToList(), StringComparer.Ordinal)
        };
    }
}
