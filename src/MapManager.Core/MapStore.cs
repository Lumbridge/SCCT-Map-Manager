using System.Text.Json;

namespace MapManager.Core;

public sealed class InstalledMap
{
    public required MapEntry Entry { get; set; }
    public bool Enabled { get; set; }
    public bool IncludeSource { get; set; }
    public bool External { get; set; }
    public Dictionary<string, string> Files { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
public sealed class StoreState
{
    public int Format { get; set; } = 1;
    public Dictionary<string, MapEntry> Downloads { get; set; } = new();
    public Dictionary<string, InstalledMap> Installed { get; set; } = new();
}
public record Change(string Destination, string? BeforeHash, string? AfterHash);
public record Journal(string Id, StoreState Before, List<Change> Changes);

public sealed class MapStore : IDisposable
{
    public string GameRoot { get; }
    public string DataRoot { get; }
    private readonly IRepositoryClient repository;
    private readonly Action guard;
    private readonly FileStream fileLock;
    private readonly SemaphoreSlim operations = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    public StoreState State { get; private set; }
    public Catalog? Catalog { get; private set; }
    public string CatalogMessage { get; private set; } = "Catalog not yet checked";
    public MapStore(string gameRoot, IRepositoryClient repository, Action? guard = null)
    {
        GameRoot = Path.GetFullPath(gameRoot);this.repository = repository;this.guard = guard ?? (() => { });
        if (!File.Exists(Path.Combine(GameRoot, "System", "SCCT_Versus.exe")) || !Directory.Exists(Path.Combine(GameRoot, "Packages")))
            throw new DirectoryNotFoundException("Choose an SCCT Versus installation containing System/SCCT_Versus.exe and Packages.");
        DataRoot = SafePaths.Under(GameRoot, "System/SCCTMapManagerData"); Directory.CreateDirectory(DataRoot);
        fileLock = new FileStream(Path.Combine(DataRoot, "manager.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        try
        {
            State = Normalize(Load<StoreState>("state.json") ?? new StoreState());
            if (State.Format != 1) throw new InvalidDataException("This library was created by a newer map manager.");
            if (Load<Journal>("pending.json") is { } pending) { this.guard(); Rollback(pending); }
            Catalog = Load<Catalog>("catalog.json");
            if (Catalog != null) CatalogMessage = "Saved catalog • " + Catalog.CheckedAt.LocalDateTime.ToString("g");
        }
        catch { fileLock.Dispose(); throw; }
    }
    private T? Load<T>(string name)
    {
        var path = SafePaths.Under(DataRoot, name);
        return File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path)) ?? throw new InvalidDataException("Invalid " + name) : default;
    }
    private void Save<T>(string name, T data)
    {
        var dest = SafePaths.Under(DataRoot, name);var temp = dest + ".new";
        using (var file = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        { JsonSerializer.Serialize(file, data, JsonOptions); file.Flush(true); }
        File.Move(temp, dest, true);
    }
    private string Cache(string hash) { CatalogParser.ValidateHash(hash); return SafePaths.Under(DataRoot, "Cache/" + hash.ToLowerInvariant() + ".blob"); }
    public void UseFallbackCatalog(Catalog fallback)
    {
        if (Catalog != null) return;Catalog = fallback;CatalogMessage = "Bundled catalog • refresh to check for updates";
    }
    public async Task RefreshAsync(CancellationToken cancel)
    {
        await operations.WaitAsync(cancel);
        try
        {
            var fresh = await repository.FetchCatalogAsync(cancel);
            Save("catalog.json", fresh); Catalog = fresh;
            CatalogMessage = "Up to date • " + fresh.CheckedAt.LocalDateTime.ToString("g");
        }
        catch (Exception ex) when (ex is not OperationCanceledException && Catalog != null)
        { CatalogMessage = "Offline catalog • " + ex.Message; }
        finally { operations.Release(); }
    }
    public string Status(MapEntry map)
    {
        if (State.Installed.TryGetValue(map.Id, out var installed))
        {
            if (installed.Enabled && installed.Files.Keys.Where(p => map.IsAssetPack || installed.Entry.IsActivationFile(p)).Any(p => !File.Exists(SafePaths.Under(GameRoot, p)))) return "Files missing";
            if (!installed.Enabled && installed.Files.Keys.Where(installed.Entry.IsActivationFile).Any(p => File.Exists(SafePaths.Under(GameRoot, p)))) return "Disabled • other files present";
            if (installed.External) return installed.Enabled ? "Enabled • existing copy" : "Disabled • existing copy";
            if (!installed.Enabled && State.Downloads.TryGetValue(map.Id, out var ready) && ready.Fingerprint != installed.Entry.Fingerprint) return "Disabled • update ready";
            return (installed.Enabled ? (map.IsAssetPack ? "Installed" : "Enabled") : "Disabled") + (DifferentVersion(installed.Entry, map) ? " • update available" : "");
        }
        if (State.Downloads.TryGetValue(map.Id, out var cached)) return !DifferentVersion(cached, map) ? "Downloaded" : "Downloaded • update available";
        if (map.Files.Any(f => f.Destination.StartsWith("Packages/Maps/", StringComparison.OrdinalIgnoreCase) && File.Exists(SafePaths.Under(GameRoot, f.Destination)))) return "Installed outside manager";
        return "Not downloaded";
    }
    public bool CanDisable(MapEntry map) => !map.IsAssetPack && (State.Installed.TryGetValue(map.Id, out var m) ? m.Enabled : map.Files.Any(f => f.Destination.StartsWith("Packages/Maps/") && File.Exists(SafePaths.Under(GameRoot, f.Destination))));
    private static bool DifferentVersion(MapEntry saved, MapEntry available) => saved.Fingerprint != available.Fingerprint || (available.IsAssetPack && saved.Version != available.Version);
    public bool HasUpdate(MapEntry map) => State.Installed.TryGetValue(map.Id, out var m) ? DifferentVersion(m.Entry, map) : State.Downloads.TryGetValue(map.Id, out var d) && DifferentVersion(d, map);
    public async Task DownloadAsync(MapEntry map, bool source, IProgress<TransferProgress>? progress, CancellationToken cancel)
    {
        await operations.WaitAsync(cancel);
        try
        {
            await CacheFiles(map, source, progress, cancel);
            // Updating an enabled map keeps it enabled; a downloaded/disabled map stays off.
            if (State.Installed.TryGetValue(map.Id, out var active) && active.Enabled)
                EnableCached(map, source, cancel);
            else
            {
                var next = Clone(State);next.Downloads[map.Id] = map;Save("state.json", next);State = next;
            }
        }
        finally { operations.Release(); }
    }
    private async Task CacheFiles(MapEntry map, bool source, IProgress<TransferProgress>? progress, CancellationToken cancel)
    {
        var files = map.InstallFiles(source);int done = 0;
        foreach (var file in files)
        {
            cancel.ThrowIfCancellationRequested();var cache = Cache(file.Hash);Directory.CreateDirectory(Path.GetDirectoryName(cache)!);
            progress?.Report(new TransferProgress("Downloading " + Path.GetFileName(file.Destination), done, files.Count, file.Size));
            if (!File.Exists(cache) || Hashing.GitBlob(cache) != file.Hash)
            {
                var temp = cache + "." + Guid.NewGuid().ToString("N") + ".part";
                try
                {
                    await repository.DownloadAsync(map, file, temp, cancel);
                    if (Hashing.GitBlob(temp) != file.Hash) throw new InvalidDataException("Downloaded file failed verification: " + file.Destination);
                    File.Move(temp, cache, true);
                }
                finally { if (File.Exists(temp)) File.Delete(temp); }
            }
            progress?.Report(new TransferProgress("Verified " + Path.GetFileName(file.Destination), ++done, files.Count));
        }
    }
    public async Task EnableAsync(MapEntry map, bool source, IProgress<TransferProgress>? progress, CancellationToken cancel)
    {
        await operations.WaitAsync(cancel);
        try
        {
            guard(); await CacheFiles(map, source, progress, cancel);
            EnableCached(map, source, cancel);
        }
        finally { operations.Release(); }
    }
    private static StoreState Normalize(StoreState state)
    {
        foreach (var map in state.Installed.Values) map.Files = new Dictionary<string, string>(map.Files, StringComparer.OrdinalIgnoreCase);
        return state;
    }
    private static StoreState Clone(StoreState state) => Normalize(JsonSerializer.Deserialize<StoreState>(JsonSerializer.Serialize(state))!);
    private bool KnownDisabledFile(string path, string hash) => State.Installed.Values.Any(m => !m.Enabled && m.Files.TryGetValue(path, out var owned) && owned == hash);
    private void EnableCached(MapEntry map, bool source, CancellationToken cancel)
    {
        guard();var desired = map.InstallFiles(source);State.Installed.TryGetValue(map.Id, out var old);
        // Source maps installed explicitly stay protected when the checkbox is off.
        var owned = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (old != null) foreach (var file in old.Files.Where(f => !source && f.Key.StartsWith("Packages/MapsEd/", StringComparison.OrdinalIgnoreCase))) owned[file.Key] = file.Value;
        var changes = new List<Change>();
        foreach (var retained in owned)
        {
            var dest = SafePaths.Under(GameRoot, retained.Key);
            if (!File.Exists(dest)) changes.Add(new Change(retained.Key, null, retained.Value));
        }
        foreach (var file in desired)
        {
            if (!SafePaths.IsMapAsset(file.Destination)) throw new InvalidDataException("Unsupported map asset.");
            if (map.IsAssetPack && !SafePaths.IsEditorAsset(file.Destination)) throw new InvalidDataException("Unsupported editor asset.");
            var dest = SafePaths.Under(GameRoot, file.Destination);
            var current = File.Exists(dest) ? Hashing.GitBlob(dest) : null;
            if (map.IsAssetPack && current != null && current != file.Hash && (old == null || !old.Files.ContainsKey(file.Destination)))
                throw new IOException("An existing file differs at " + file.Destination + ". Preserve it elsewhere before installing this asset pack.");
            foreach (var other in State.Installed.Where(p => p.Key != map.Id && p.Value.Enabled))
                if (other.Value.Files.TryGetValue(file.Destination, out var hash) && (hash != file.Hash || map.IsActivationFile(file.Destination)))
                    throw new IOException($"{other.Value.Entry.Name} ({other.Value.Entry.Category}) uses {file.Destination}. Disable that map before switching versions.");
            if (old != null && old.Files.TryGetValue(file.Destination, out var previous) && current != null && current != previous && current != file.Hash && !KnownDisabledFile(file.Destination, current))
                throw new IOException("Local changes were detected in " + file.Destination + ". Move your edited copy somewhere safe before updating.");
            owned[file.Destination] = file.Hash;
            if (current != file.Hash) changes.Add(new Change(file.Destination, current, file.Hash));
        }
        if (old != null)
        {
            foreach (var obsolete in old.Files.Where(f => !owned.ContainsKey(f.Key) && old.Entry.IsActivationFile(f.Key)))
            {
                var dest = SafePaths.Under(GameRoot, obsolete.Key);var current = File.Exists(dest) ? Hashing.GitBlob(dest) : null;
                if (current != null && current != obsolete.Value) throw new IOException("Local changes were detected in " + obsolete.Key);
                if (current != null) changes.Add(new Change(obsolete.Key, current, null));
            }
        }
        var next = Clone(State);next.Downloads[map.Id] = map;next.Installed[map.Id] = new InstalledMap { Entry = map, Enabled = true, IncludeSource = source || old?.IncludeSource == true, Files = owned };
        Execute(changes, next, cancel);
    }
    public async Task DisableAsync(MapEntry map, CancellationToken cancel)
    {
        if (map.IsAssetPack) throw new InvalidOperationException("Editor assets stay installed because maps may depend on them.");
        await operations.WaitAsync(cancel);
        try
        {
            guard();State.Installed.TryGetValue(map.Id, out var old);
            var installedEntry = old?.Entry ?? map;
            var files = old?.Files ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var changes = new List<Change>();
            // Unmanaged installations can be disabled without replacing their actual bytes.
            var candidates = old != null ? old.Files.Keys : map.Files.Select(f => f.Destination).Where(p => !p.StartsWith("Packages/MapsEd/", StringComparison.OrdinalIgnoreCase));
            foreach (var relative in candidates.Where(installedEntry.IsActivationFile))
            {
                var path = SafePaths.Under(GameRoot, relative);
                if (!File.Exists(path)) continue;
                var hash = Hashing.GitBlob(path);
                if (old != null && files[relative] != hash) throw new IOException("Local changes were detected in " + relative + ". Preserve your edited copy before disabling.");
                if (State.Installed.Any(p => p.Key != map.Id && p.Value.Enabled && p.Value.Files.ContainsKey(relative))) throw new IOException("Another enabled map owns " + relative);
                files[relative] = hash; var cache = Cache(hash);Directory.CreateDirectory(Path.GetDirectoryName(cache)!);
                if (!File.Exists(cache)) File.Copy(path, cache);
                changes.Add(new Change(relative, hash, null));
            }
            if (old == null && changes.Count == 0) throw new IOException("This map is not installed.");
            var next = Clone(State);
            next.Installed[map.Id] = new InstalledMap { Entry = installedEntry, Enabled = false, External = old?.External ?? true, IncludeSource = old?.IncludeSource ?? false, Files = new Dictionary<string, string>(files, StringComparer.OrdinalIgnoreCase) };
            Execute(changes, next, cancel);
        }
        finally { operations.Release(); }
    }
    public async Task RestoreDisabledAsync(MapEntry map, CancellationToken cancel)
    {
        await operations.WaitAsync(cancel);
        try
        {
            guard();var old = State.Installed[map.Id];if (old.Enabled) return;
            var changes = new List<Change>();
            foreach (var file in old.Files)
            {
                var path = SafePaths.Under(GameRoot, file.Key);var current = File.Exists(path) ? Hashing.GitBlob(path) : null;
                if (current != null && current != file.Value && (old.Entry.IsActivationFile(file.Key) || !KnownDisabledFile(file.Key, current)))
                    throw new IOException("Another file is now installed at " + file.Key + ". Preserve any local edits and disable its map before switching.");
                if (State.Installed.Any(p => p.Key != map.Id && p.Value.Enabled && p.Value.Files.TryGetValue(file.Key, out var hash) && (hash != file.Value || old.Entry.IsActivationFile(file.Key)))) throw new IOException("Another enabled map owns " + file.Key);
                if (current != file.Value) changes.Add(new Change(file.Key, current, file.Value));
            }
            var next = Clone(State);next.Installed[map.Id].Enabled = true;Execute(changes, next, cancel);
        }
        finally { operations.Release(); }
    }
    private void Execute(List<Change> changes, StoreState next, CancellationToken cancel)
    {
        if (File.Exists(SafePaths.Under(DataRoot, "pending.json"))) throw new IOException("An interrupted operation needs recovery. Restart the manager before changing maps; the journal and backups have been preserved.");
        guard();cancel.ThrowIfCancellationRequested();
        var journal = new Journal(DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N"), Clone(State), changes);
        // Stage backups and verify the whole plan before touching the live installation.
        foreach (var change in changes)
        {
            cancel.ThrowIfCancellationRequested();var path = SafePaths.Under(GameRoot, change.Destination);
            var current = File.Exists(path) ? Hashing.GitBlob(path) : null;
            if (current != change.BeforeHash) throw new IOException("Files changed during the operation. Retry: " + change.Destination);
            if (change.AfterHash != null && (!File.Exists(Cache(change.AfterHash)) || Hashing.GitBlob(Cache(change.AfterHash)) != change.AfterHash)) throw new InvalidDataException("A cached file failed verification. Download the map again.");
            if (current != null)
            {
                var backup = SafePaths.Under(DataRoot, "Backups/" + journal.Id + "/" + change.Destination);
                Directory.CreateDirectory(Path.GetDirectoryName(backup)!);File.Copy(path, backup);
            }
        }
        Save("pending.json", journal);
        try
        {
            foreach (var change in changes)
            {
                cancel.ThrowIfCancellationRequested();var path = SafePaths.Under(GameRoot, change.Destination);
                if (change.AfterHash == null) File.Delete(path); else Replace(Cache(change.AfterHash), path);
            }
            Save("state.json", next);File.Delete(SafePaths.Under(DataRoot, "pending.json"));State = next;
        }
        catch { Rollback(journal);throw; }
    }
    private static void Replace(string source, string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);var temp = destination + ".mapmanager-" + Guid.NewGuid().ToString("N");
        try { File.Copy(source, temp);File.Move(temp, destination, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    private void Rollback(Journal journal)
    {
        // A persistent journal restores interrupted installs on the next launch.
        foreach (var change in journal.Changes.AsEnumerable().Reverse())
        {
            var path = SafePaths.Under(GameRoot, change.Destination);var current = File.Exists(path) ? Hashing.GitBlob(path) : null;
            if (current == change.BeforeHash) continue;
            if (current != change.AfterHash) throw new IOException("Recovery found a file edited outside the manager: " + change.Destination + ". Backups and the recovery journal have been kept.");
            if (change.BeforeHash == null) File.Delete(path);
            else
            {
                var backup = SafePaths.Under(DataRoot, "Backups/" + journal.Id + "/" + change.Destination);
                if (!File.Exists(backup) || Hashing.GitBlob(backup) != change.BeforeHash) throw new IOException("A recovery backup is missing or changed.");
                Replace(backup, path);
            }
        }
        Save("state.json", journal.Before);State = Normalize(journal.Before);File.Delete(SafePaths.Under(DataRoot, "pending.json"));
    }
    public void Dispose() { fileLock.Dispose();operations.Dispose(); }
}
