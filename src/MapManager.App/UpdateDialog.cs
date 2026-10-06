using MapManager.Core;

namespace MapManager.App;

// Shows a release's notes as formatted text and asks whether to install it.
public sealed class UpdateDialog : Form
{
    public UpdateDialog(AppRelease release, string currentVersion, bool canSelfUpdate)
    {
        Text = "Update available";Font = new Font("Segoe UI", 10);BackColor = Color.White;
        AutoScaleDimensions = new SizeF(96, 96);AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterParent;MinimizeBox = MaximizeBox = false;ShowInTaskbar = false;
        Size = new Size(620, 560);MinimumSize = new Size(460, 380);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(20, 16, 20, 12) };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new Label { Text = $"SCCT Map Manager {release.Tag} is available", AutoSize = true, Font = new Font(Font.FontFamily, 15, FontStyle.Bold), Margin = new Padding(0, 0, 0, 4) }, 0, 0);
        layout.Controls.Add(new Label { Text = $"You have {currentVersion}." + (canSelfUpdate ? " Your maps and settings are kept." : ""), AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(0, 0, 0, 12) }, 0, 1);
        var notes = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = Color.FromArgb(246, 248, 250), DetectUrls = true, WordWrap = true, Margin = new Padding(0, 0, 0, 12) };
        var notesPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12), BackColor = notes.BackColor, Margin = new Padding(0, 0, 0, 12) };
        notesPanel.Controls.Add(notes);layout.Controls.Add(notesPanel, 0, 2);
        // Render once fonts and DPI scaling have settled: a RichTextBox font change resets all character formatting.
        Shown += (_, _) => MarkdownView.Render(notes, string.IsNullOrWhiteSpace(release.Notes) ? "No release notes were supplied." : release.Notes);
        MarkdownView.EnableLinks(notes);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Margin = Padding.Empty };
        var accept = new Button { Text = canSelfUpdate ? "Update and restart" : "Open download page", DialogResult = DialogResult.Yes, AutoSize = true, MinimumSize = new Size(150, 36),
            FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 104, 118), ForeColor = Color.White, Margin = new Padding(8, 0, 0, 0) };
        var later = new Button { Text = "Not now", DialogResult = DialogResult.No, AutoSize = true, MinimumSize = new Size(110, 36), FlatStyle = FlatStyle.Flat, Margin = new Padding(8, 0, 0, 0) };
        buttons.Controls.Add(accept);buttons.Controls.Add(later);layout.Controls.Add(buttons, 0, 3);
        Controls.Add(layout);AcceptButton = accept;CancelButton = later;
        notes.TabStop = false;Shown += (_, _) => accept.Focus();
    }
}
