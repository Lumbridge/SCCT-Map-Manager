using System.Text;
using System.Text.Json;
using MapManager.Core;

int assertions = 0;
void Check(bool value, string message) { if (!value) throw new Exception("FAIL: " + message);assertions++;Console.WriteLine("PASS " + message); }
async Task Reject(Func<Task> action, string message)
{
    bool rejected = false;try { await action(); } catch (Exception) { rejected = true; }Check(rejected, message);
}
var output = Path.GetFullPath(Path.Combine("artifacts", "tests", Guid.NewGuid().ToString("N")));Directory.CreateDirectory(output);
string Fixture(string name)
{
    var root = Path.Combine(output, name);Directory.CreateDirectory(Path.Combine(root, "System"));Directory.CreateDirectory(Path.Combine(root, "Packages"));File.WriteAllText(Path.Combine(root, "System", "SCCT_Versus.exe"), "test fixture, not an executable");return root;
}
void Put(string root, string path, string text) { var dest = SafePaths.Under(root, path);Directory.CreateDirectory(Path.GetDirectoryName(dest)!);File.WriteAllText(dest, text); }
string Read(string root, string path) => File.ReadAllText(SafePaths.Under(root, path));
var client = new FakeClient(output);
var runtimeRoot = Fixture("runtime");
Put(runtimeRoot, "System/Reloaded.Core.dll", "original runtime");
var runtime = new RuntimePatch(runtimeRoot, () => { });
Check(runtime.Status().StartsWith("A different DLL"), "runtime initially detects an unpatched copy");
runtime.Install();
Check(runtime.Status().StartsWith("Patched"), "verified bundled runtime is installed");
var originalBackup = runtime.ListBackups().Single();
Check(File.ReadAllText(Path.Combine(runtimeRoot, "System/SCCTMapManagerData/Backups/Runtime", originalBackup.Name)) == "original runtime", "dated runtime backup preserves original bytes");
runtime.Install();Check(runtime.ListBackups().Count == 1, "repeated patch install does not back up itself");
runtime.Restore(originalBackup);
Check(Read(runtimeRoot, "System/Reloaded.Core.dll") == "original runtime", "restore dated runtime backup byte for byte");
Check(runtime.ListBackups().Count == 2, "restore also preserves the replaced patched DLL");
runtime.Install();Put(runtimeRoot, "System/Reloaded.Core.dll", "original runtime");
Check(new RuntimePatch(runtimeRoot, () => { }).Status().StartsWith("Your previous DLL"), "manual native restoration detected across restart without stale state");
File.Move(Path.Combine(runtimeRoot, "System/Reloaded.Core.dll"), Path.Combine(runtimeRoot, "System/Reloaded.Core.dll.off"));
Check(runtime.Status().Contains("missing or renamed"), "manual runtime rename detected");
runtime.Restore(originalBackup);Check(Read(runtimeRoot, "System/Reloaded.Core.dll") == "original runtime", "restore works when active DLL was renamed");
Check(Read(runtimeRoot, "System/Reloaded.Core.dll.off") == "original runtime", "manually renamed DLL is preserved");
Put(runtimeRoot, "System/Reloaded.Core.dll", "another native version");
Check(runtime.Status().StartsWith("A different DLL"), "unknown external replacement is not claimed to be patched");
runtime.Install();
Check(runtime.ListBackups().Any(b => File.ReadAllText(Path.Combine(runtimeRoot, "System/SCCTMapManagerData/Backups/Runtime", b.Name)) == "another native version"), "reapply preserves externally replaced DLL");
var corruptBackupPath = Path.Combine(runtimeRoot, "System/SCCTMapManagerData/Backups/Runtime", originalBackup.Name);
File.WriteAllText(corruptBackupPath, "damaged");
await Reject(() => { runtime.Restore(originalBackup);return Task.CompletedTask; }, "corrupt runtime backup rejected before changing installed DLL");
Check(runtime.Status().StartsWith("Patched"), "corrupt restore leaves patched runtime intact");
await Reject(() => { new RuntimePatch(runtimeRoot, () => throw new IOException("Game is open")).Restore(runtime.ListBackups().First());return Task.CompletedTask; }, "runtime changes enforce game/editor guard");
if (OperatingSystem.IsWindows())
{
    Put(runtimeRoot, "System/Reloaded.Core.dll", "locked native runtime");
    using (var locked = new FileStream(Path.Combine(runtimeRoot, "System/Reloaded.Core.dll"), FileMode.Open, FileAccess.Read, FileShare.Read))
        await Reject(() => { runtime.Install();return Task.CompletedTask; }, "locked runtime replacement fails safely");
    Check(Read(runtimeRoot, "System/Reloaded.Core.dll") == "locked native runtime", "locked DLL remains intact");
}
Check(!Directory.EnumerateFiles(Path.Combine(runtimeRoot, "System"), ".runtime-*.tmp").Any(), "runtime staging files cleaned after success and failure");
var a = client.Map("original/A", "Alpha", "alpha-v1", "shared-v1", true);
var b = client.Map("community/B", "Bravo", "bravo-v1", "shared-v1");
var update = client.Map("original/A", "Alpha", "alpha-v2", "shared-v1", true);
var jpZulu = a with { Id = "original/Z", Name = "Zulu", Category = "Originals" };
var communityAlpha = b with { Name = "Alpha", Category = "Community" };
var ordered = CatalogPresentation.Order(new[] { communityAlpha, jpZulu, a }).ToList();
Check(ordered.Select(m => m.Id).SequenceEqual(new[] { a.Id, jpZulu.Id, b.Id }), "original maps stay pinned above alphabetically earlier community maps");
Check(ordered.Take(2).All(m => m.Category == "JP's Maps") && ordered[2].Category == "Community", "cached Originals entries display as JP's Maps without changing other collections");
Check(ordered[0].Id == a.Id && ordered[0].Fingerprint == a.Fingerprint, "collection rename preserves installed identity and content fingerprint");


foreach (var path in new[] { "../escape", "Packages/Maps/../../escape", "Packages/Maps/C:.sdc", "Packages/Maps/CON.sdc", "Packages/Maps/name. ", "Packages\\Maps\\a.sdc", "/absolute" })
    await Reject(() => { SafePaths.ValidateRelative(path);return Task.CompletedTask; }, "reject unsafe path " + path);
Check(!SafePaths.IsMapAsset("System/hack.exe") && !SafePaths.IsMapAsset("Packages/Maps/file.dll"), "exclude executables and arbitrary repository content");
Check(SafePaths.IsMapAsset("System/_PC_/Level.INT"), "include map localization sidecars");

var root = Fixture("lifecycle");
using (var store = new MapStore(root, client))
{
    Put(root, "Packages/MapsEd/Alpha.sdc", "my source edits");
    await store.DownloadAsync(a, false, null, default);
    Check(store.Status(a) == "Downloaded" && !File.Exists(Path.Combine(root, "Packages/Maps/Alpha.sdc")), "download does not enable a map");
    await store.EnableAsync(a, false, null, default);
    Check(Read(root, "Packages/Maps/Alpha.sdc") == "alpha-v1" && Read(root, "Packages/MapsEd/Alpha.sdc") == "my source edits", "enable installs playable map and preserves unchecked editor source");
    await store.EnableAsync(b, false, null, default);await store.DisableAsync(a, default);
    Check(!File.Exists(Path.Combine(root, "Packages/Maps/Alpha.sdc")) && !File.Exists(Path.Combine(root, "Packages/Textures/Alpha-i.utc")), "disable removes map and its menu entry");
    Check(Read(root, "Packages/Textures/Shared.utx") == "shared-v1" && File.Exists(Path.Combine(root, "Packages/Maps/Bravo.sdc")), "disable preserves assets needed by other maps");
    client.Offline = true;await store.RestoreDisabledAsync(a, default);client.Offline = false;
    Check(Read(root, "Packages/Maps/Alpha.sdc") == "alpha-v1", "re-enable works offline from verified cache");
    await store.DownloadAsync(update, false, null, default);
    Check(Read(root, "Packages/Maps/Alpha.sdc") == "alpha-v2" && store.State.Installed[a.Id].Enabled, "update replaces an enabled map and preserves enabled state");
    Check(Directory.GetFiles(Path.Combine(store.DataRoot, "Backups"), "*.sdc", SearchOption.AllDirectories).Any(f => File.ReadAllText(f) == "alpha-v1"), "updates keep the replaced version in backups");
    var conflict = client.Map("enhanced/C", "Charlie", "charlie", "shared-v2");
    await Reject(() => store.EnableAsync(conflict, false, null, default), "conflicting shared dependency blocks another enabled map");
    Check(Read(root, "Packages/Textures/Shared.utx") == "shared-v1" && !File.Exists(Path.Combine(root, "Packages/Maps/Charlie.sdc")), "conflict leaves installation untouched");
    Put(root, "Packages/Maps/Alpha.sdc", "local playable changes");
    await Reject(() => store.EnableAsync(a, false, null, default), "update refuses to overwrite locally modified managed map");
    Check(Read(root, "Packages/Maps/Alpha.sdc") == "local playable changes", "local edits remain intact");
}
using (var store = new MapStore(root, client))
{
    Check(store.State.Installed.Count == 2 && store.State.Downloads.ContainsKey(a.Id), "installed and downloaded state persists across restart");
    Check(store.State.Installed[a.Id].Files.ContainsKey("packages/maps/ALPHA.sdc"), "file ownership stays case insensitive after restart");
    await Reject(() => { using var duplicate = new MapStore(root, client);return Task.CompletedTask; }, "only one manager can modify an installation");
}

var externalRoot = Fixture("existing-copy");Put(externalRoot, "Packages/Maps/Alpha.sdc", "existing custom build");Put(externalRoot, "Packages/Textures/Alpha-i.utc", "custom menu");
using (var store = new MapStore(externalRoot, client))
{
    await store.DisableAsync(a, default);Check(store.State.Installed[a.Id].External, "detect and disable a pre-existing unmanaged copy");
    await store.RestoreDisabledAsync(a, default);
    Check(Read(externalRoot, "Packages/Maps/Alpha.sdc") == "existing custom build" && Read(externalRoot, "Packages/Textures/Alpha-i.utc") == "custom menu", "restore an unmanaged copy byte for byte");
    await store.DisableAsync(a, default);await store.DownloadAsync(a, false, null, default);await store.EnableAsync(a, false, null, default);
    Check(Read(externalRoot, "Packages/Maps/Alpha.sdc") == "alpha-v1" && !store.State.Installed[a.Id].External, "explicit download replaces a disabled existing copy with the repository version");
}

var sourceRoot = Fixture("editable");
using (var store = new MapStore(sourceRoot, client))
{
    await store.EnableAsync(a, true, null, default);Check(Read(sourceRoot, "Packages/MapsEd/Alpha.sdc") == "source alpha-v1", "editable maps are installed only when requested");
    await store.DisableAsync(a, default);await store.RestoreDisabledAsync(a, default);
    Check(Read(sourceRoot, "Packages/MapsEd/Alpha.sdc") == "source alpha-v1", "editable source survives a disable/enable round trip");
}

var switchRoot = Fixture("switch-shared-versions");
using (var store = new MapStore(switchRoot, client))
{
    var other = client.Map("enhanced/C", "Charlie", "charlie", "shared-v2");
    await store.EnableAsync(a, false, null, default);await store.DisableAsync(a, default);
    await store.EnableAsync(other, false, null, default);
    await Reject(() => store.RestoreDisabledAsync(a, default), "re-enable cannot replace an active map's shared dependency");
    await store.DisableAsync(other, default);await store.RestoreDisabledAsync(a, default);
    Check(Read(switchRoot, "Packages/Textures/Shared.utx") == "shared-v1", "switching back restores the prior shared version after conflicting maps are disabled");
    await store.DisableAsync(a, default);await store.RestoreDisabledAsync(other, default);
    Check(Read(switchRoot, "Packages/Textures/Shared.utx") == "shared-v2", "shared version switching works in both directions");
}

var corruptRoot = Fixture("bad-download");
using (var store = new MapStore(corruptRoot, client))
{
    client.Corrupt = true;await Reject(() => store.EnableAsync(a, false, null, default), "reject a corrupted download");client.Corrupt = false;
    Check(store.State.Installed.Count == 0 && !Directory.Exists(Path.Combine(corruptRoot, "Packages/Maps")), "failed downloads never partially install maps");
    using var cancelled = new CancellationTokenSource();cancelled.Cancel();
    await Reject(() => store.EnableAsync(a, false, null, cancelled.Token), "honour cancellation before installation");
}

if (OperatingSystem.IsWindows())
{
    var lockedRoot = Fixture("rollback");Put(lockedRoot, "Packages/Maps/Alpha.sdc", "original installed map");Put(lockedRoot, "Packages/Textures/Shared.utx", "original shared");
    using var store = new MapStore(lockedRoot, client);
    using (var locked = new FileStream(Path.Combine(lockedRoot, "Packages/Textures/Shared.utx"), FileMode.Open, FileAccess.Read, FileShare.Read))
        await Reject(() => store.EnableAsync(a, false, null, default), "locked destination rolls back an interrupted installation");
    Check(Read(lockedRoot, "Packages/Maps/Alpha.sdc") == "original installed map" && Read(lockedRoot, "Packages/Textures/Shared.utx") == "original shared" && store.State.Installed.Count == 0, "rollback restores original files and ownership state");
}

var recoveryRoot = Fixture("crash-recovery");string dataRoot;
using (var store = new MapStore(recoveryRoot, client)) { dataRoot = store.DataRoot; }
Put(recoveryRoot, "Packages/Maps/Alpha.sdc", "before crash");var beforeHash = Hashing.GitBlob(Path.Combine(recoveryRoot, "Packages/Maps/Alpha.sdc"));
Put(dataRoot, "Backups/simulated/Packages/Maps/Alpha.sdc", "before crash");Put(recoveryRoot, "Packages/Maps/Alpha.sdc", "partial update");
var afterHash = Hashing.GitBlob(Path.Combine(recoveryRoot, "Packages/Maps/Alpha.sdc"));
File.WriteAllText(Path.Combine(dataRoot, "pending.json"), JsonSerializer.Serialize(new Journal("simulated", new StoreState(), [new Change("Packages/Maps/Alpha.sdc", beforeHash, afterHash)])));
using (var store = new MapStore(recoveryRoot, client))
    Check(Read(recoveryRoot, "Packages/Maps/Alpha.sdc") == "before crash" && !File.Exists(Path.Combine(dataRoot, "pending.json")), "startup journal recovery repairs an interrupted process");

var guardRoot = Fixture("game-running");
using (var store = new MapStore(guardRoot, client, () => throw new IOException("game running")))
{
    await Reject(() => store.EnableAsync(a, false, null, default), "game/editor guard blocks live file changes");
    Check(store.State.Installed.Count == 0, "game guard has no installation side effects");
}

var pendingRoot = Fixture("pending-operation");
using (var store = new MapStore(pendingRoot, client))
{
    var pendingPath = Path.Combine(store.DataRoot, "pending.json");File.WriteAllText(pendingPath, "preserve unresolved journal");
    await Reject(() => store.EnableAsync(a, false, null, default), "unresolved recovery blocks another installation");
    Check(File.ReadAllText(pendingPath) == "preserve unresolved journal", "later operations cannot replace an unresolved recovery journal");
}

var offlineRoot = Fixture("bundled-catalog");
using (var store = new MapStore(offlineRoot, client))
{
    store.UseFallbackCatalog(new Catalog(a.Commit, DateTimeOffset.UtcNow, [a]));await store.RefreshAsync(default);
    Check(store.Catalog?.Maps.Count == 1 && store.CatalogMessage.StartsWith("Offline catalog"), "first-launch bundled catalog remains available when the server fails");
}

var treeArg = Array.IndexOf(args, "--tree");
var assetBase = client.Map("assets/Rainbow Six Vegas", "Rainbow Six Vegas", "unused", "asset-v1");
var asset = assetBase with { Version = "v1.0.0", Category = "Editor assets", Files = assetBase.Files.Where(f => f.Destination == "Packages/Textures/Shared.utx").ToList() };
var assetNewBase = client.Map(asset.Id, asset.Name, "unused", "asset-v2");
var assetUpdate = asset with { Version = "v1.1.0", Files = assetNewBase.Files.Where(f => f.Destination == "Packages/Textures/Shared.utx").ToList() };
var assetRoot = Fixture("asset-lifecycle");
using (var store = new MapStore(assetRoot, client))
{
    await store.DownloadAsync(asset, false, null, default);
    Check(!File.Exists(Path.Combine(assetRoot, "Packages/Textures/Shared.utx")), "asset download for later does not install");
    client.Offline = true;await store.EnableAsync(asset, false, null, default);client.Offline = false;
    Check(store.Status(asset) == "Installed" && !store.CanDisable(asset), "asset packs install offline and cannot be disabled like maps");
    await Reject(() => store.DisableAsync(asset, default), "shared editor assets cannot be removed through map disable");
    Check(store.HasUpdate(assetUpdate) && store.Status(assetUpdate).Contains("update available"), "asset updates are detected");
    Check(store.HasUpdate(asset with { Version = "v1.0.1" }), "version-only releases are detected");
    await store.DownloadAsync(assetUpdate, false, null, default);
    Check(store.State.Installed[asset.Id].Entry.Version == "v1.1.0" && Read(assetRoot, "Packages/Textures/Shared.utx") == "asset-v2", "asset update persists version and updates active files");
    Check(Directory.GetFiles(Path.Combine(store.DataRoot, "Backups"), "*.utx", SearchOption.AllDirectories).Any(f => File.ReadAllText(f) == "asset-v1"), "asset updates back up previous files");
    File.Delete(Path.Combine(assetRoot, "Packages/Textures/Shared.utx"));
    Check(store.Status(assetUpdate) == "Files missing", "asset status detects missing texture files");
    await store.EnableAsync(assetUpdate, false, null, default);
    Put(assetRoot, "Packages/Textures/Shared.utx", "editor changes");
    await Reject(() => store.EnableAsync(asset, false, null, default), "asset update preserves local editor changes");
    Check(Read(assetRoot, "Packages/Textures/Shared.utx") == "editor changes", "editor changes stay intact");
}
using (var store = new MapStore(assetRoot, client))
    Check(store.State.Installed[asset.Id].Entry.Version == "v1.1.0", "installed asset version survives restart");
var assetConflictRoot = Fixture("asset-conflicts");
using (var store = new MapStore(assetConflictRoot, client))
{
    Put(assetConflictRoot, "Packages/Textures/Shared.utx", "unmanaged edit");
    await Reject(() => store.EnableAsync(asset, false, null, default), "asset install refuses to overwrite an unmanaged editor package");
    Check(Read(assetConflictRoot, "Packages/Textures/Shared.utx") == "unmanaged edit", "unmanaged package remains intact");
    File.Delete(Path.Combine(assetConflictRoot, "Packages/Textures/Shared.utx"));
    await store.EnableAsync(asset, false, null, default);
    var matchingMap = client.Map("community/AssetUser", "AssetUser", "map", "asset-v1");
    await store.EnableAsync(matchingMap, false, null, default);
    await Reject(() => store.EnableAsync(assetUpdate, false, null, default), "asset update cannot replace an enabled map dependency");
    await store.DisableAsync(matchingMap, default);
    Check(Read(assetConflictRoot, "Packages/Textures/Shared.utx") == "asset-v1", "disabling maps keeps installed editor assets");
}
var releaseAsset = asset with { NotesPath = asset.Id + "/v1.0.0/README.md", Files = asset.Files.Select(f => f with { Source = "releases/download/r6v-v1.0.0/Shared.utx" }).ToList() };
var manifest = JsonSerializer.Serialize(new[] { releaseAsset, releaseAsset with { Version = "v1.10.0", NotesPath = asset.Id + "/v1.10.0/README.md", Files = releaseAsset.Files.Select(f => f with { Source = "releases/download/r6v-v1.10.0/Shared.utx" }).ToList() } });
Check(AssetCatalog.Parse(manifest, a.Commit).Single().Version == "v1.10.0", "release catalog chooses newest semantic version");
await Reject(() => { AssetCatalog.Parse(JsonSerializer.Serialize(new[] { releaseAsset with { Files = [releaseAsset.Files[0] with { Destination = "System/hack.exe" }] } }), a.Commit);return Task.CompletedTask; }, "release catalog rejects executable destinations");
var seedPath = Path.Combine("src", "MapManager.App", "catalog-seed.json");
var seed = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(seedPath))!;
foreach (var version in new[] { "v1.9.0", "v1.10.0" })
    seed["tree"]!.AsArray().Add(new System.Text.Json.Nodes.JsonObject { ["path"] = $"assets/Rainbow Six Vegas/{version}/Packages/StaticMeshes/R6V.usx", ["type"] = "blob", ["sha"] = a.Commit, ["size"] = 123 });
var assetCatalog = CatalogParser.Parse(seed.ToJsonString());
Check(assetCatalog.AssetPacks.Single().Version == "v1.10.0" && assetCatalog.Maps.All(m => !m.IsAssetPack), "tree catalog versions editor packs separately from maps");
var portTree = new System.Text.Json.Nodes.JsonObject { ["sha"] = a.Commit, ["truncated"] = false, ["tree"] = new System.Text.Json.Nodes.JsonArray(
    new System.Text.Json.Nodes.JsonObject { ["path"] = "ports/rainbow-six-vegas/Packages/Maps/CalyD.sdc", ["type"] = "blob", ["sha"] = a.Commit, ["size"] = 123 },
    new System.Text.Json.Nodes.JsonObject { ["path"] = "ports/rainbow-six-vegas/Packages/MapsEd/CalyD.sdc", ["type"] = "blob", ["sha"] = a.Commit, ["size"] = 456 },
    new System.Text.Json.Nodes.JsonObject { ["path"] = "ports/rainbow-six-vegas/Packages/Textures/CalyD-i.utc", ["type"] = "blob", ["sha"] = a.Commit, ["size"] = 7 }) };
var portCatalog = CatalogParser.Parse(portTree.ToJsonString());
Check(portCatalog.Maps.Single().Id == "ports/rainbow-six-vegas" && portCatalog.Maps.Single().Name == "CalyD"
    && portCatalog.Maps.Single().Category == "Ports" && portCatalog.Maps.Single().Game == "Rainbow Six Vegas"
    && portCatalog.Maps.Single().IsPort, "catalog discovers port maps and records their source game");
var portManifest = JsonSerializer.Serialize(new[] { portCatalog.Maps.Single() with {
    Version = "v1.0.0", NotesPath = "ports/rainbow-six-vegas/README.md",
    Files = portCatalog.Maps.Single().Files.Select(f => f with { Source = "releases/download/calyd-v1.0.0/" + Path.GetFileName(f.Source) }).ToList() } });
Check(AssetCatalog.Parse(portManifest, a.Commit).Single().IsPort, "release catalog accepts port map packages");
Check(JsonSerializer.Deserialize<Catalog>("{\"Commit\":\"old\",\"CheckedAt\":\"2026-01-01T00:00:00Z\",\"Maps\":[]}")!.AssetPacks.Count == 0, "old saved catalogs load with an empty asset library");
if (treeArg >= 0)
{
    var catalog = CatalogParser.Parse(await File.ReadAllTextAsync(args[treeArg + 1]));
    Check(catalog.Maps.Count(m => m.Category == "Community") == 56, "discover all 56 community maps");
    using var tree = JsonDocument.Parse(await File.ReadAllTextAsync(args[treeArg + 1]));
    var shipmentVersions = tree.RootElement.GetProperty("tree").EnumerateArray()
        .Select(entry => entry.GetProperty("path").GetString()!)
        .Where(path => path.StartsWith("release/ShipD/") && path.EndsWith("/Packages/Maps/ShipD.sdc"))
        .Select(path => path.Split('/')[2]).Distinct()
        .OrderByDescending(version => Version.Parse(version.TrimStart('v').Contains('.') ? version.TrimStart('v') : version.TrimStart('v') + ".0"));
    Check(catalog.Maps.Any(m => m.Category == CatalogPresentation.JpMaps && m.Name == "Shipment" && m.Version == shipmentVersions.First()), "select the latest original Shipment release");
    Check(catalog.Maps.Any(m => m.Category == "Enhanced") && catalog.Maps.Count(m => m.Category == "Recovered") == 5, "include enhanced and recovered collections");
    Check(catalog.Maps.Where(m => m.Category == "Community").All(m => m.Files.Any(f => f.Source.StartsWith("community/_shared/"))), "bundle shared community dependencies with each map");
    Check(catalog.Maps.All(m => m.Files.All(f => !f.Source.Contains("/src/") && !f.Source.EndsWith(".exe"))), "catalog excludes source artwork and executables");
    Console.WriteLine($"Catalog: {catalog.Maps.Count} maps at {catalog.Commit}");
    if (args.Contains("--live"))
    {
        using var live = new RepositoryClient();var liveRoot = Fixture("live-download");using var store = new MapStore(liveRoot, live);
        await store.RefreshAsync(default);Check(store.Catalog?.Maps.Count >= 62, "refresh catalog from public GitHub without credentials");
        var shipment = store.Catalog!.Maps.Single(m => m.Name == "Shipment");
        await store.EnableAsync(shipment, false, new Progress<TransferProgress>(p => Console.WriteLine(p.Message)), default);
        Check(shipment.InstallFiles(false).All(f => Hashing.GitBlob(SafePaths.Under(liveRoot, f.Destination)) == f.Hash), "real Shipment map and every dependency match the pinned repository blobs");
        await store.DisableAsync(shipment, default);await store.RestoreDisabledAsync(shipment, default);
        Check(File.Exists(Path.Combine(liveRoot, "Packages/Maps/ShipD.sdc")), "real downloaded map survives disable and enable");
        File.WriteAllText(Path.Combine(output, "live-root.txt"), liveRoot);
    }
}
var draftArg = Array.IndexOf(args, "--asset-draft");
if (draftArg >= 0)
{
    var draftRoot = Path.GetFullPath(args[draftArg + 1]);
    var entries = AssetCatalog.Parse(File.ReadAllText(Path.Combine(draftRoot, "repository-files", "asset-catalog.json")), a.Commit);
    var draftClient = new DraftClient(draftRoot);
    var installRoot = Fixture("staged-asset-install");
    using var store = new MapStore(installRoot, draftClient);
    foreach (var pack in entries)
    {
        foreach (var file in pack.Files)
        {
            var path = draftClient.Source(file);
            Check(new FileInfo(path).Length == file.Size && Hashing.GitBlob(path) == file.Hash, "staged package matches manifest: " + file.Destination);
        }
        await store.EnableAsync(pack, false, null, default);
        Check(pack.Files.All(f => Hashing.GitBlob(SafePaths.Under(installRoot, f.Destination)) == f.Hash), "staged pack installs and verifies in a disposable installation: " + pack.Name);
    }
    Check(store.State.Installed.Count == entries.Count, "all staged pack versions recorded independently");
}
Console.WriteLine($"PASS {assertions} checks. Fixtures: {output}");

sealed class DraftClient(string root) : IRepositoryClient
{
    public string Source(MapFile file) => SafePaths.Under(root, "release-files/" + string.Join('/', file.Source.Split('/').Skip(2)));
    public Task<Catalog> FetchCatalogAsync(CancellationToken cancel) => throw new NotSupportedException();
    public Task<string> NotesAsync(MapEntry map, CancellationToken cancel) => Task.FromResult("Draft pack");
    public Task DownloadAsync(MapEntry map, MapFile file, string destination, CancellationToken cancel)
    { cancel.ThrowIfCancellationRequested();File.Copy(Source(file), destination);return Task.CompletedTask; }
}

sealed class FakeClient(string output) : IRepositoryClient
{
    private readonly Dictionary<string, byte[]> bytes = new();
    public bool Corrupt { get; set; }
    public bool Offline { get; set; }
    public MapEntry Map(string id, string name, string map, string shared, bool source = false)
    {
        MapFile File(string path, string content)
        {
            var data = Encoding.UTF8.GetBytes(content);var temp = Path.Combine(output, Guid.NewGuid().ToString("N"));System.IO.File.WriteAllBytes(temp, data);var hash = Hashing.GitBlob(temp);bytes[hash] = data;
            return new MapFile(id + "/" + path, path, hash, data.Length);
        }
        List<MapFile> files = [File("Packages/Maps/" + name + ".sdc", map), File("Packages/Textures/Shared.utx", shared), File("Packages/Textures/" + name + "-i.utc", "menu " + map)];
        if (source) files.Add(File("Packages/MapsEd/" + name + ".sdc", "source " + map));
        return new MapEntry(id, name, "Originals", "Latest", new string('a', 40), id + "/README.md", files);
    }
    public Task<Catalog> FetchCatalogAsync(CancellationToken cancel) => throw new IOException("offline fixture");
    public Task<string> NotesAsync(MapEntry map, CancellationToken cancel) => Task.FromResult("Test map");
    public Task DownloadAsync(MapEntry map, MapFile file, string destination, CancellationToken cancel)
    {
        if (Offline) throw new IOException("offline");
        return System.IO.File.WriteAllBytesAsync(destination, Corrupt ? Encoding.UTF8.GetBytes("corrupt") : bytes[file.Hash], cancel);
    }
}
