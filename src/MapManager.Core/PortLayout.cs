namespace MapManager.Core;

// Preserve installed/downloaded map ownership when moving the original flat port folders.
public static class PortLayout
{
    public static string CanonicalId(string id) => id switch
    {
        "ports/rainbow-six-vegas" => "ports/rainbow-six-vegas/calypso-casino",
        "ports/scda-blkg1" => "ports/splinter-cell-double-agent/blackwing",
        "ports/scda-bosg2" => "ports/splinter-cell-double-agent/boss-house",
        "ports/scda-dwg" => "ports/splinter-cell-double-agent/dawn-waves",
        "ports/scda-motg4" => "ports/splinter-cell-double-agent/motorway-90",
        "ports/scda-redg6" => "ports/splinter-cell-double-agent/red-diamond",
        "ports/scda-slhg7" => "ports/splinter-cell-double-agent/slaughterhouse",
        "ports/scda-terg5" => "ports/splinter-cell-double-agent/terminus",
        "ports/scda-ussg8" => "ports/splinter-cell-double-agent/uss-wisdom",
        _ => id
    };

    // Keep source and notes paths pinned to their original commit, including saved offline catalogs.
    public static MapEntry Normalize(MapEntry map) => map with { Id = CanonicalId(map.Id) };
    public static Catalog Normalize(Catalog catalog) => catalog with { Maps = catalog.Maps.Select(Normalize).ToList() };
}
