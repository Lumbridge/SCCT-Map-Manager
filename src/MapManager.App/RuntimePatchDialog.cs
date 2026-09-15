using MapManager.Core;

namespace MapManager.App;

internal sealed class RuntimePatchDialog : Form
{
    private readonly RuntimePatch patch;
    private readonly Label status = new() { AutoSize = true, MaximumSize = new Size(640, 0), Margin = new Padding(0, 12, 0, 12) };
    private readonly ComboBox backups = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 620 };
    private readonly Button install = new() { Text = "Use patched DLL", AutoSize = true };
    private readonly Button restore = new() { Text = "Restore selected backup", AutoSize = true };
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 2000 };

    public RuntimePatchDialog(string root)
    {
        patch = new RuntimePatch(root, () => Program.GuardGame(root));
        Text = "Map-loading crash fix — Reloaded Core";
        Font = new Font("Segoe UI", 10);ClientSize = new Size(680, 390);
        FormBorderStyle = FormBorderStyle.FixedDialog;MaximizeBox = false;MinimizeBox = false;StartPosition = FormStartPosition.CenterParent;
        var layout = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(20), AutoScroll = true };
        Controls.Add(layout);
        layout.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(630, 0), Text =
            "Crashing when loading maps installed through the manager? Try my patched Reloaded.Core.dll. It fixes unsafe custom-map filename handling and some missing player/profile data crashes. It may not fix every crash.\n\nClose the game and editor first. Each replaced DLL gets a dated backup. You can restore it below; manual replacements and renames are detected automatically." });
        layout.Controls.Add(status);layout.Controls.Add(install);
        layout.Controls.Add(new Label { AutoSize = true, Text = "Dated backups (UTC, newest first):", Margin = new Padding(0, 15, 0, 4) });
        layout.Controls.Add(backups);layout.Controls.Add(restore);
        install.Click += (_, _) => Change(patch.Install);
        restore.Click += (_, _) => { if (backups.SelectedItem is RuntimeBackup backup) Change(() => patch.Restore(backup)); };
        timer.Tick += (_, _) => RefreshStatus();
        Shown += (_, _) => { RefreshStatus();timer.Start(); };
        FormClosed += (_, _) => timer.Dispose();
        RefreshStatus();
    }
    private void RefreshStatus()
    {
        try
        {
            status.Text = patch.Status();install.Enabled = !status.Text.StartsWith("Patched Reloaded.Core.dll is installed.");
            var list = patch.ListBackups();
            if (!backups.Items.Cast<RuntimeBackup>().SequenceEqual(list))
            {
                var selected = backups.SelectedItem as RuntimeBackup;
                backups.Items.Clear();backups.Items.AddRange(list.Cast<object>().ToArray());
                if (selected != null && list.Contains(selected)) backups.SelectedItem = selected;
                else if (backups.Items.Count > 0) backups.SelectedItem = list.FirstOrDefault(b => b.Hash != RuntimePatch.PatchHash) ?? list[0];
            }
            restore.Enabled = backups.SelectedItem != null;
        }
        catch (Exception ex) { status.Text = "Cannot inspect runtime: " + ex.Message;install.Enabled = restore.Enabled = false; }
    }
    private void Change(Action action)
    {
        try { action();RefreshStatus(); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "DLL change stopped", MessageBoxButtons.OK, MessageBoxIcon.Information);RefreshStatus(); }
    }
}
