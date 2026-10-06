using System.Diagnostics;
using MapManager.Core;

namespace MapManager.App;

internal static class Program
{
    public static Version Version { get; } = typeof(Program).Assembly.GetName().Version ?? new Version(0, 0, 0);
    public static string VersionText => "v" + Version.ToString(3);
    // Only the published single-file build can replace itself; development builds open the release page instead.
    public static string? UpdatableExecutable =>
        string.IsNullOrEmpty(typeof(Program).Assembly.Location) && Environment.ProcessPath is { } path ? path : null;

    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        Application.ThreadException += (_, e) => MessageBox.Show(e.Exception.Message, "SCCT Map Manager", MessageBoxButtons.OK, MessageBoxIcon.Error);
        try
        {
            // A self-update relaunches before the old process has released the installation lock.
            var waitIndex = Array.IndexOf(args, "--wait-for-pid");
            if (waitIndex >= 0 && waitIndex + 1 < args.Length && int.TryParse(args[waitIndex + 1], out var pid))
            {
                try { using var previous = Process.GetProcessById(pid);previous.WaitForExit(TimeSpan.FromSeconds(30)); }
                catch (ArgumentException) { }
            }
            if (UpdatableExecutable is { } executable) AppUpdater.CleanUp(executable);
            string? root = null;
            var rootIndex = Array.IndexOf(args, "--root");if (rootIndex >= 0 && rootIndex + 1 < args.Length) root = args[rootIndex + 1];
            root ??= Directory.GetParent(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar))?.FullName;
            if (root == null || !File.Exists(Path.Combine(root, "System", "SCCT_Versus.exe")))
            {
                using var picker = new FolderBrowserDialog { Description = "Select your SCCT Versus installation (the folder containing Packages and System)", UseDescriptionForTitle = true };
                if (picker.ShowDialog() != DialogResult.OK) return; root = picker.SelectedPath;
            }
            using var client = new RepositoryClient();
            using var updater = new AppUpdater(Version);
            using var form = new MainForm(root, client, updater, checkForUpdates: !args.Contains("--ui-smoke") && !args.Contains("--no-update-check"));
            if (args.Contains("--ui-smoke"))
            {
                var artifact = args.SkipWhile(a => a != "--ui-smoke").Skip(1).FirstOrDefault();
                form.Shown += async (_, _) =>
                {
                    await form.InitialLoad;
                    var checks = await form.SmokeAsync(artifact);
                    if (artifact != null)
                    {
                        Directory.CreateDirectory(artifact);
                        using var bitmap = new Bitmap(form.Width, form.Height);form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
                        bitmap.Save(Path.Combine(artifact, "map-manager.png"));
                        File.WriteAllText(Path.Combine(artifact, "ui-smoke.txt"), $"Map rows: {form.VisibleMapCount}\n{form.CatalogStatus}\n{checks}");
                    }
                    form.Close();
                };
            }
            Application.Run(form);
        }
        catch (Exception ex)
        {
            if (args.Contains("--ui-smoke"))
            {
                var path = args.SkipWhile(a => a != "--ui-smoke").Skip(1).First();Directory.CreateDirectory(path);File.WriteAllText(Path.Combine(path, "error.txt"), ex.ToString());Environment.ExitCode = 1;
            }
            else MessageBox.Show(ex.Message, "SCCT Map Manager", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
    public static void GuardGame(string root)
    {
        var system = Path.GetFullPath(Path.Combine(root, "System"));
        foreach (var name in new[] { "SCCT_Versus", "SCCT_Editor", "Reloaded_Editor" })
        foreach (var process in Process.GetProcessesByName(name))
        {
            using (process)
            {
                string? executable;
                try { executable = process.MainModule?.FileName; }
                catch { throw new IOException("Close the game and editor before changing installed maps."); }
                if (executable != null && string.Equals(Path.GetDirectoryName(executable), system, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Close the game and editor in this installation before enabling, disabling or updating maps.");
            }
        }
    }
}
