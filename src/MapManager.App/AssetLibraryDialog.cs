using MapManager.Core;

namespace MapManager.App;

public sealed class AssetLibraryDialog : Form
{
    private readonly MapStore store;
    private readonly IRepositoryClient repository;
    private readonly TextBox search = new() { Dock = DockStyle.Fill, PlaceholderText = "Search packs or package names" };
    private readonly ComboBox filter = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly DataGridView grid = new() { Dock = DockStyle.Fill, ReadOnly = true, MultiSelect = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
        RowHeadersVisible = false, BackgroundColor = Color.White, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill };
    private readonly RichTextBox details = new() { Dock = DockStyle.Fill, ReadOnly = true, BackColor = Color.White, BorderStyle = BorderStyle.None };
    private readonly Label status = new() { Dock = DockStyle.Fill, AutoSize = true };
    private readonly Button install = new() { Text = "Download + install", AutoSize = true };
    private readonly Button download = new() { Text = "Download for later", AutoSize = true };
    private readonly Button refresh = new() { Text = "Check for updates", AutoSize = true };
    private readonly Button cancel = new() { Text = "Cancel", AutoSize = true, Visible = false };
    private CancellationTokenSource? operation, notesOperation;
    private bool busy, closeAfterOperation;
    private MapEntry? Selected => grid.CurrentRow?.Tag as MapEntry;

    public AssetLibraryDialog(MapStore store, IRepositoryClient repository)
    {
        this.store = store;this.repository = repository;
        Text = "Textures & static meshes";Font = new Font("Segoe UI", 10);Size = new Size(1000, 760);
        MinimumSize = new Size(850, 650);StartPosition = FormStartPosition.CenterParent;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 1, RowCount = 7 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (var height in new[] { 76f, 38f, 0f, 180f, 46f, 44f, 44f })
            layout.RowStyles.Add(height == 0 ? new RowStyle(SizeType.Percent, 100) : new RowStyle(SizeType.Absolute, height));
        layout.Controls.Add(new Label { Text = "EDITOR ASSETS\nOptional texture and static mesh packs for map makers. Mesh packs include their bundled materials.\nAssets stay installed because your maps may depend on them.", Dock = DockStyle.Fill, AutoSize = true }, 0, 0);
        var filters = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70));filters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
        filter.Items.AddRange(["All packs", "Textures", "Static meshes", "Updates available"]);filter.SelectedIndex = 0;
        filters.Controls.Add(search, 0, 0);filters.Controls.Add(filter, 1, 0);layout.Controls.Add(filters, 0, 1);
        foreach (var name in new[] { "Pack", "Available", "Installed", "Status" }) grid.Columns.Add(name, name);
        grid.BorderStyle = BorderStyle.None;grid.EnableHeadersVisualStyles = false;grid.ColumnHeadersHeight = 36;grid.RowTemplate.Height = 36;
        grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle { BackColor = Color.FromArgb(229, 236, 240), Font = new Font(Font, FontStyle.Bold), Padding = new Padding(6) };
        grid.DefaultCellStyle = new DataGridViewCellStyle { Padding = new Padding(6, 3, 3, 3), SelectionBackColor = Color.FromArgb(218, 240, 242), SelectionForeColor = Color.FromArgb(28, 39, 54) };
        grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;grid.GridColor = Color.FromArgb(241, 245, 249);
        grid.Columns[0].FillWeight = 180;grid.Columns[3].FillWeight = 170;
        foreach (DataGridViewColumn column in grid.Columns) column.SortMode = DataGridViewColumnSortMode.NotSortable;
        layout.Controls.Add(grid, 0, 2);layout.Controls.Add(details, 0, 3);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill };
        actions.Controls.AddRange([install, download, refresh, cancel]);layout.Controls.Add(actions, 0, 4);
        layout.Controls.Add(new Label { Text = "Install to: " + store.GameRoot, Dock = DockStyle.Fill, AutoEllipsis = true, UseMnemonic = false }, 0, 5);
        layout.Controls.Add(status, 0, 6);Controls.Add(layout);
        search.TextChanged += (_, _) => Fill();filter.SelectedIndexChanged += (_, _) => Fill();
        grid.SelectionChanged += async (_, _) => await ShowDetails();
        refresh.Click += async (_, _) => await Run(ct => store.RefreshAsync(ct));
        install.Click += async (_, _) => { if (Selected is { } pack) await Run(ct => store.EnableAsync(pack, false, Reporter(), ct)); };
        download.Click += async (_, _) => { if (Selected is { } pack) await Run(ct => store.DownloadAsync(pack, false, Reporter(), ct)); };
        cancel.Click += (_, _) => { operation?.Cancel();cancel.Enabled = false; };
        FormClosing += (_, e) => { if (busy) { e.Cancel = true;closeAfterOperation = true;operation?.Cancel(); } else notesOperation?.Cancel(); };
        FormClosed += (_, _) => { notesOperation?.Dispose(); };
        Fill();
    }
    private void Fill()
    {
        if (busy) return;
        var selected = Selected?.Id;grid.Rows.Clear();
        var packs = (store.Catalog?.AssetPacks ?? []).Concat(store.State.Installed.Values.Select(i => i.Entry))
            .Concat(store.State.Downloads.Values).Where(p => p.IsAssetPack).DistinctBy(p => p.Id).OrderBy(p => p.Name);
        foreach (var pack in packs)
        {
            if (!(pack.Name + " " + string.Join(' ', pack.Files.Select(f => f.Destination))).Contains(search.Text.Trim(), StringComparison.OrdinalIgnoreCase)) continue;
            if (filter.Text == "Textures" && !pack.Files.Any(f => f.Destination.StartsWith("Packages/Textures/"))) continue;
            if (filter.Text == "Static meshes" && !pack.Files.Any(f => f.Destination.StartsWith("Packages/StaticMeshes/"))) continue;
            if (filter.Text == "Updates available" && !store.HasUpdate(pack)) continue;
            store.State.Installed.TryGetValue(pack.Id, out var installed);
            int row = grid.Rows.Add(pack.Name, pack.Version, installed?.Entry.Version ?? "—", store.Status(pack));grid.Rows[row].Tag = pack;
            grid.Rows[row].Cells[0].ToolTipText = pack.Name;
            if (pack.Id == selected) grid.CurrentCell = grid.Rows[row].Cells[0];
        }
        status.Text = grid.Rows.Count == 0 ? "No matching asset packs. Refresh to check for published packs." : store.CatalogMessage;
        _ = ShowDetails();
    }
    private async Task ShowDetails()
    {
        notesOperation?.Cancel();notesOperation?.Dispose();notesOperation = new CancellationTokenSource();var ct = notesOperation.Token;
        var pack = Selected;install.Enabled = download.Enabled = pack != null && !busy;
        if (pack == null) { details.Text = "Published texture and static mesh packs appear here. Maps remain in the main library.";return; }
        install.Text = store.HasUpdate(pack) ? "Update installed pack" : "Download + install";
        if (!store.State.Installed.ContainsKey(pack.Id)) install.Text = "Download + install";
        var cached = store.State.Downloads.GetValueOrDefault(pack.Id);
        var heading = $"{pack.Name} • {pack.Version} • {pack.Bytes / (1024d * 1024):0.0} MB\n" +
            (cached != null ? $"Downloaded: {cached.Version}\n" : "") + string.Join("\n", pack.Files.Select(f => f.Destination)) + "\n\n";
        details.Text = heading + "Loading release notes…";
        try { var notes = await repository.NotesAsync(pack, ct);if (!ct.IsCancellationRequested && !IsDisposed) details.Text = heading + notes; }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch { if (!ct.IsCancellationRequested && !IsDisposed) details.Text = heading + "Release notes are unavailable offline. Downloaded packs can still be installed."; }
    }
    private IProgress<TransferProgress> Reporter() => new Progress<TransferProgress>(p =>
    {
        if (!IsDisposed && IsHandleCreated) BeginInvoke((Action)(() =>
        { if (!IsDisposed && busy) status.Text = $"{p.Message}   {p.Completed}/{p.Total}"; }));
    });
    public async Task SmokeAsync(string artifact)
    {
        if (grid.Rows.Count != 2) throw new InvalidOperationException("Asset library did not list both fixture packs.");
        filter.SelectedItem = "Updates available";
        if (grid.Rows.Count != 1 || grid.Rows[0].Cells[1].Value?.ToString() != "v1.1.0"
            || grid.Rows[0].Cells[2].Value?.ToString() != "v1.0.0") throw new InvalidOperationException("Asset version display or update filter failed.");
        filter.SelectedItem = "Textures";if (grid.Rows.Count != 1) throw new InvalidOperationException("Texture filter failed.");
        filter.SelectedItem = "Static meshes";if (grid.Rows.Count != 1) throw new InvalidOperationException("Mesh filter failed.");
        filter.SelectedIndex = 0;search.Text = "R6V.usx";if (grid.Rows.Count != 1) throw new InvalidOperationException("Asset package search failed.");
        search.Clear();await ShowDetails();Size = MinimumSize;PerformLayout();Refresh();
        using var bitmap = new Bitmap(Width, Height);DrawToBitmap(bitmap, new Rectangle(Point.Empty, Size));
        bitmap.Save(Path.Combine(artifact, "editor-assets.png"));
    }
    private async Task Run(Func<CancellationToken, Task> action)
    {
        if (busy) return;
        busy = true;operation = new CancellationTokenSource();
        search.Enabled = filter.Enabled = grid.Enabled = install.Enabled = download.Enabled = refresh.Enabled = false;
        cancel.Visible = cancel.Enabled = true;status.Text = "Working…";
        string result;
        try { await Task.Run(() => action(operation.Token));result = "Done. " + store.CatalogMessage; }
        catch (OperationCanceledException) { result = "Cancelled. Any in-progress installation was rolled back."; }
        catch (Exception ex) { result = ex.Message;if (!closeAfterOperation) MessageBox.Show(this, result, "Asset operation stopped", MessageBoxButtons.OK, MessageBoxIcon.Information); }
        finally
        {
            busy = false;operation.Dispose();operation = null;cancel.Visible = false;
            search.Enabled = filter.Enabled = grid.Enabled = refresh.Enabled = true;
        }
        Fill();status.Text = result;if (closeAfterOperation) Close();
    }
}
