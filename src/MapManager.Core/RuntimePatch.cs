using System.Security.Cryptography;

namespace MapManager.Core;

public record RuntimeBackup(string Name, string Hash)
{
    public override string ToString() => Name[..23].Replace('_', ' ') +
        (Hash == RuntimePatch.PatchHash ? " — patched DLL" : " — previous DLL");
}

// The caller holds the installation's MapStore lock. No remembered enabled flag:
// the bytes at the runtime's actual load path are always the source of truth.
public sealed class RuntimePatch(string gameRoot, Action guard)
{
    public const string PatchHash = "95e3f812a58d03fde82983d397183dc3fb87da85a663cafac766c8d28ace866d";
    private string Target => SafePaths.Under(gameRoot, "System/Reloaded.Core.dll");
    private string Backups => SafePaths.Under(gameRoot, "System/SCCTMapManagerData/Backups/Runtime");
    private static string Hash(string path)
    {
        using var file = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(file)).ToLowerInvariant();
    }
    public IReadOnlyList<RuntimeBackup> ListBackups()
    {
        if (!Directory.Exists(Backups)) return [];
        return Directory.EnumerateFiles(Backups, "*.dll").Select(Path.GetFileName)
            .OfType<string>().Where(n => n.Length == 125 && n.EndsWith(".dll", StringComparison.Ordinal))
            .Select(n => new RuntimeBackup(n, n.Substring(57, 64)))
            .Where(b => b.Hash.All(c => "0123456789abcdef".Contains(c)))
            .OrderByDescending(b => b.Name).ToArray();
    }
    public string Status()
    {
        if (!File.Exists(Target)) return "Reloaded.Core.dll is missing or renamed; the patched DLL is not active.";
        var hash = Hash(Target);
        if (hash == PatchHash) return "Patched Reloaded.Core.dll is installed.";
        return ListBackups().Any(b => b.Hash == hash)
            ? "Your previous DLL is installed; the patch is not active (manual swaps are detected)."
            : "A different DLL is installed; the bundled patch is not active.";
    }
    public void Install()
    {
        using var source = typeof(RuntimePatch).Assembly.GetManifestResourceStream("MapManager.RuntimePatch")
            ?? throw new FileNotFoundException("The bundled runtime patch is missing.");
        Swap(source, PatchHash);
    }
    public void Restore(RuntimeBackup backup)
    {
        if (!ListBackups().Contains(backup)) throw new IOException("This dated backup is no longer available.");
        using var source = File.OpenRead(SafePaths.Under(Backups, backup.Name));
        Swap(source, backup.Hash);
    }
    private void Swap(Stream source, string expectedHash)
    {
        guard();
        var target = Target;
        var stage = SafePaths.Under(gameRoot, "System/.runtime-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var file = new FileStream(stage, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { source.CopyTo(file); file.Flush(true); }
            if (Hash(stage) != expectedHash) throw new InvalidDataException("DLL verification failed. No installed file was changed.");
            guard();
            if (File.Exists(target))
            {
                var before = Hash(target);
                if (before == expectedHash) return;
                Directory.CreateDirectory(Backups);
                var name = DateTime.UtcNow.ToString("yyyy-MM-dd_HH-mm-ss-fff") + "_" + Guid.NewGuid().ToString("N") + "_" + before + ".dll";
                // Windows replaces the file and preserves its old bytes as one operation.
                // A crash cannot leave a deleted target between backup and installation.
                File.Replace(stage, target, SafePaths.Under(Backups, name));
            }
            else File.Move(stage, target);
        }
        finally { if (File.Exists(stage)) File.Delete(stage); }
    }
}
