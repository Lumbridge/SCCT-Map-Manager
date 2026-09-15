using System.Diagnostics;
using MapManager.Core;

namespace MapManager.App;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        Application.ThreadException += (_, e) => MessageBox.Show(e.Exception.Message, "SCCT Map Manager", MessageBoxButtons.OK, MessageBoxIcon.Error);
        try
        {
            string? root = null;
            var rootIndex = Array.IndexOf(args, "--root");if (rootIndex >= 0 && rootIndex + 1 < args.Length) root = args[rootIndex + 1];
            root ??= Directory.GetParent(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar))?.FullName;
            if (root == null || !File.Exists(Path.Combine(root, "System", "SCCT_Versus.exe")))
            {
                using var picker = new FolderBrowserDialog { Description = "Select your SCCT Versus installation (the folder containing Packages and System)", UseDescriptionForTitle = true };
                if (picker.ShowDialog() != DialogResult.OK) return; root = picker.SelectedPath;
            }
            using var client = new RepositoryClient();
            using var form = new MainForm(root, client);
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
                        using var runtimeDialog = new RuntimePatchDialog(root);
                        runtimeDialog.Show(form);
                        using var runtimeBitmap = new Bitmap(runtimeDialog.Width, runtimeDialog.Height);
                        runtimeDialog.DrawToBitmap(runtimeBitmap, new Rectangle(Point.Empty, runtimeDialog.Size));
                        runtimeBitmap.Save(Path.Combine(artifact, "runtime-patch.png"));
                        runtimeDialog.Close();
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
