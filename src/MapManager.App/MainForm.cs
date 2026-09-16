using System.Diagnostics;
using MapManager.Core;

namespace MapManager.App;

public sealed class MainForm : Form
{
    private static readonly Color Ink = Color.FromArgb(28, 39, 54), Accent = Color.FromArgb(0, 104, 118), Pale = Color.FromArgb(241, 245, 249);
    private readonly IRepositoryClient repository;
    private MapStore store;
    private readonly TextBox search = new() { PlaceholderText = "Search maps or package names", Dock = DockStyle.Fill };
    private readonly ComboBox category = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly ComboBox statusFilter = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly DataGridView grid = new() { Dock = DockStyle.Fill, ReadOnly = true, MultiSelect = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToResizeRows = false, RowHeadersVisible = false, AutoGenerateColumns = false,
        BackgroundColor = Color.White, BorderStyle = BorderStyle.None, CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal, GridColor = Pale };
    private readonly Label mapName = new() { AutoSize = true, MaximumSize = new Size(360, 0), Font = new Font("Segoe UI", 20, FontStyle.Bold) };
    private readonly Label metadata = new() { AutoSize = true, MaximumSize = new Size(360, 0), ForeColor = Color.DimGray };
    private readonly Label summary = new() { AutoSize = true, MaximumSize = new Size(360, 0) };
    private readonly CheckBox source = new() { AutoSize = true, Text = "Include editable source maps (MapsEd)" };
    private readonly Button download = Button("Download / update"), enable = Button("Enable map", true), disable = Button("Disable map"), refresh = Button("Refresh"), folder = Button("Change folder");
    private readonly Button backups = Button("Open backups"), repo = Button("Repository"), cancel = Button("Cancel");
    private readonly Button runtime = Button("DLL patch / restore…");
    private readonly RichTextBox notes = new() { Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = Color.White, DetectUrls = true, WordWrap = true };
    private readonly Label target = new() { AutoSize = false, ForeColor = Color.DimGray, Dock = DockStyle.Fill, AutoEllipsis = true, UseMnemonic = false };
    private readonly Label count = new() { AutoSize = true, Dock = DockStyle.Fill, ForeColor = Color.DimGray };
    private readonly Label catalogStatus = new() { AutoSize = true, Dock = DockStyle.Fill, ForeColor = Color.DimGray };
    private readonly Label operationStatus = new() { AutoSize = false, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill };
    private readonly ProgressBar progress = new() { Dock = DockStyle.Fill, Height = 8 };
    private CancellationTokenSource? operation, notesCancel;
    private bool busy, closing;
    private readonly TaskCompletionSource initial = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task InitialLoad => initial.Task;
    public Task NotesLoad { get; private set; } = Task.CompletedTask;
    public int VisibleMapCount => grid.Rows.Count;
    public string CatalogStatus => catalogStatus.Text;
    private MapEntry? Selected => grid.CurrentRow?.Tag as MapEntry;

    public MainForm(string root, IRepositoryClient repository)
    {
        this.repository = repository;store = new MapStore(root, repository, () => Program.GuardGame(root));
        SeedCatalog();
        Text = "SCCT Map Manager";Font = new Font("Segoe UI", 10);ForeColor = Ink;BackColor = Pale;
        AutoScaleDimensions = new SizeF(96, 96);AutoScaleMode = AutoScaleMode.Dpi;MinimumSize = new Size(1050, 800);Size = new Size(1280, 900);StartPosition = FormStartPosition.CenterScreen;
        BuildLayout();WireEvents();
        var toolsMenu = new MenuStrip();
        var tools = new ToolStripMenuItem("Tools");
        var assets = new ToolStripMenuItem("Textures & static meshes…");
        assets.Click += (_, _) => { using var dialog = new AssetLibraryDialog(store, repository);dialog.ShowDialog(this);FillMaps(); };
        tools.DropDownOpening += (_, _) => assets.Enabled = !busy;
        tools.DropDownItems.Add(assets);toolsMenu.Items.Add(tools);Controls.Add(toolsMenu);MainMenuStrip = toolsMenu;
        target.Text = store.GameRoot;FillMaps();
        Shown += async (_, _) => { try { await Run("Checking the map catalog…", ct => store.RefreshAsync(ct)); } finally { initial.TrySetResult(); } };
    }
    private static Button Button(string text, bool primary = false) => new()
    {
        Text = text, AutoSize = true, MinimumSize = new Size(110, 36), FlatStyle = FlatStyle.Flat,
        BackColor = primary ? Accent : Color.White, ForeColor = primary ? Color.White : Ink, Cursor = Cursors.Hand,
        Padding = new Padding(10, 3, 10, 3), Margin = new Padding(0, 0, 8, 8)
    };
    private static TableLayoutPanel Table(int columns, params float[] widths)
    {
        var t = new TableLayoutPanel { ColumnCount = columns, Dock = DockStyle.Fill, AutoSize = true, Margin = Padding.Empty };
        foreach (var w in widths) t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, w));return t;
    }
    private void BuildLayout()
    {
        var outer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(24), BackColor = Pale };
        outer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 100));outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));Controls.Add(outer);
        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 2, Margin = Padding.Empty };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < 3; i++) header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 55));header.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
        var title = new Label { Text = "SCCT MAP MANAGER", Font = new Font("Segoe UI", 23, FontStyle.Bold), AutoSize = true, Margin = new Padding(0, 0, 0, 8) };
        foreach (var button in new[] { repo, refresh, backups, folder, runtime })
        {
            button.AutoSize = false;button.Height = 36;button.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            button.Margin = new Padding(8, 0, 0, 8);button.Padding = new Padding(6, 0, 6, 0);
        }
        header.Controls.Add(title, 0, 0);header.Controls.Add(repo, 1, 0);header.Controls.Add(refresh, 2, 0);header.Controls.Add(folder, 3, 0);
        header.Controls.Add(target, 0, 1);header.Controls.Add(backups, 1, 1);header.Controls.Add(runtime, 2, 1);header.SetColumnSpan(runtime, 2);outer.Controls.Add(header, 0, 0);
        var filters = Table(3, 54, 23, 23);filters.AutoSize = false;filters.Padding = new Padding(0, 6, 0, 12);
        filters.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));filters.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        category.Items.AddRange(["All collections", CatalogPresentation.JpMaps, "Community", "Enhanced", "Recovered"]);category.SelectedIndex = 0;
        statusFilter.Items.AddRange(["All maps", "Enabled", "Disabled", "Updates available", "Downloaded", "Not downloaded"]);statusFilter.SelectedIndex = 0;
        filters.Controls.Add(new Label { Text = "Search maps", AutoSize = true }, 0, 0);filters.Controls.Add(new Label { Text = "Collection", AutoSize = true }, 1, 0);filters.Controls.Add(new Label { Text = "Show", AutoSize = true }, 2, 0);
        filters.Controls.Add(search, 0, 1);filters.Controls.Add(category, 1, 1);filters.Controls.Add(statusFilter, 2, 1);outer.Controls.Add(filters, 0, 1);
        var split = new SplitContainer { Size = new Size(1200, 580), Dock = DockStyle.Fill, SplitterDistance = 740, FixedPanel = FixedPanel.Panel2, BackColor = Pale, SplitterWidth = 16 };
        split.Panel1MinSize = 440;split.Panel2MinSize = 345;
        grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle { BackColor = Color.FromArgb(229, 236, 240), ForeColor = Ink, Font = new Font(Font, FontStyle.Bold), Padding = new Padding(8) };
        grid.DefaultCellStyle = new DataGridViewCellStyle { Padding = new Padding(8, 4, 4, 4), SelectionBackColor = Color.FromArgb(218, 240, 242), SelectionForeColor = Ink };
        grid.EnableHeadersVisualStyles = false;grid.ColumnHeadersHeight = 42;grid.RowTemplate.Height = 42;
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Map", HeaderText = "Map", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, MinimumWidth = 140, FillWeight = 135 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Collection", HeaderText = "Collection", Width = 125 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Status", HeaderText = "Status", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, MinimumWidth = 145, FillWeight = 110 });
        foreach (DataGridViewColumn column in grid.Columns) column.SortMode = DataGridViewColumnSortMode.NotSortable;
        split.Panel1.Controls.Add(grid);
        var details = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7, BackColor = Color.White, Padding = new Padding(18) };
        for (int i = 0; i < 6; i++) details.RowStyles.Add(new RowStyle(SizeType.AutoSize));details.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        details.Controls.Add(mapName, 0, 0);details.Controls.Add(metadata, 0, 1);summary.Margin = new Padding(0, 14, 0, 14);details.Controls.Add(summary, 0, 2);
        source.Margin = new Padding(0, 0, 0, 16);details.Controls.Add(source, 0, 3);
        var mapActions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };mapActions.Controls.Add(enable);mapActions.Controls.Add(disable);mapActions.Controls.Add(download);details.Controls.Add(mapActions, 0, 4);
        var notesTitle = new Label { Text = "MAP NOTES & DEPENDENCIES", UseMnemonic = false, Font = new Font(Font, FontStyle.Bold), AutoSize = true, Margin = new Padding(0, 8, 0, 10) };details.Controls.Add(notesTitle, 0, 5);details.Controls.Add(notes, 0, 6);
        split.Panel2.Controls.Add(details);outer.Controls.Add(split, 0, 2);
        var libraryFooter = Table(1, 100);libraryFooter.Padding = new Padding(0, 12, 0, 8);libraryFooter.Controls.Add(count, 0, 0);libraryFooter.Controls.Add(catalogStatus, 0, 1);
        outer.Controls.Add(libraryFooter, 0, 3);
        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, RowCount = 2, Margin = Padding.Empty };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 8));
        cancel.AutoSize = false;cancel.Size = new Size(140, 36);cancel.Anchor = AnchorStyles.Right;
        cancel.Margin = new Padding(10, 4, 0, 4);cancel.Visible = false;
        progress.Margin = Padding.Empty;progress.Visible = false;
        footer.Controls.Add(operationStatus, 0, 0);footer.Controls.Add(cancel, 1, 0);
        footer.Controls.Add(progress, 0, 1);footer.SetColumnSpan(progress, 2);outer.Controls.Add(footer, 0, 4);
        operationStatus.Text = "Choose a map to download, enable or disable.";
    }
    private void WireEvents()
    {
        search.TextChanged += (_, _) => FillMaps();category.SelectedIndexChanged += (_, _) => FillMaps();statusFilter.SelectedIndexChanged += (_, _) => FillMaps();
        grid.CellPainting += PaintPinnedMap;
        grid.SelectionChanged += (_, _) => NotesLoad = Detail();
        refresh.Click += async (_, _) => await Run("Refreshing the catalog…", ct => store.RefreshAsync(ct));
        repo.Click += (_, _) => Open(RepositoryClient.RepositoryUrl);
        runtime.Click += (_, _) => { using var dialog = new RuntimePatchDialog(store.GameRoot);dialog.ShowDialog(this); };
        backups.Click += (_, _) => { var path = SafePaths.Under(store.DataRoot, "Backups");Directory.CreateDirectory(path);Open(path); };
        cancel.Click += (_, _) => { operation?.Cancel();cancel.Enabled = false;cancel.Text = "Cancelling…"; };
        download.Click += async (_, _) => { if (Selected is { } map) { bool include = source.Checked;await Run("Downloading " + map.Name, ct => store.DownloadAsync(map, include, Reporter(), ct)); } };
        enable.Click += async (_, _) =>
        {
            if (Selected is not { } map) return;bool include = source.Checked;
            await Run("Enabling " + map.Name, async ct =>
            {
                if (store.State.Installed.TryGetValue(map.Id, out var installed) && !installed.Enabled &&
                    (!store.State.Downloads.TryGetValue(map.Id, out var cached) || (!installed.External && cached.Fingerprint == installed.Entry.Fingerprint)))
                    await store.RestoreDisabledAsync(map, ct);
                else await store.EnableAsync(map, include, Reporter(), ct);
            });
        };
        disable.Click += async (_, _) => { if (Selected is { } map) await Run("Disabling " + map.Name, ct => store.DisableAsync(map, ct)); };
        notes.LinkClicked += (_, e) => { if (Uri.TryCreate(e.LinkText, UriKind.Absolute, out var uri) && uri.Scheme == "https") Open(uri.AbsoluteUri); };
        folder.Click += (_, _) =>
        {
            using var picker = new FolderBrowserDialog { Description = "Choose an SCCT Versus installation", UseDescriptionForTitle = true, SelectedPath = store.GameRoot };
            if (picker.ShowDialog(this) != DialogResult.OK || Path.GetFullPath(picker.SelectedPath) == store.GameRoot) return;
            try
            {
                string root = picker.SelectedPath;var replacement = new MapStore(root, repository, () => Program.GuardGame(root));
                store.Dispose();store = replacement;SeedCatalog();target.Text = store.GameRoot;FillMaps();_ = Run("Checking the catalog…", ct => store.RefreshAsync(ct));
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Cannot open installation", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        };
        FormClosing += (_, e) =>
        {
            if (busy) { e.Cancel = true;closing = true;operation?.Cancel();operationStatus.Text = "Finishing safely before closing…"; }
            else { notesCancel?.Cancel(); }
        };
        FormClosed += (_, _) => { notesCancel?.Dispose();store.Dispose(); };
    }
    private static void Open(string path) => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    private void SeedCatalog()
    {
        if (store.Catalog != null) return;
        using var stream = typeof(MainForm).Assembly.GetManifestResourceStream("MapManager.CatalogSeed")!;
        using var reader = new StreamReader(stream);store.UseFallbackCatalog(CatalogParser.Parse(reader.ReadToEnd()));
    }
    private IProgress<TransferProgress> Reporter() => new Progress<TransferProgress>(p =>
    {
        if (IsDisposed || !IsHandleCreated) return;
        BeginInvoke((Action)(() =>
        {
            if (IsDisposed || !busy) return;operationStatus.Text = p.Message + $"   {p.Completed}/{p.Total}";progress.Style = ProgressBarStyle.Continuous;
            progress.Maximum = Math.Max(1, p.Total);progress.Value = Math.Clamp(p.Completed, 0, progress.Maximum);
        }));
    });
    private async Task Run(string message, Func<CancellationToken, Task> action)
    {
        if (busy) return;
        busy = true;operation = new CancellationTokenSource();SetBusy();operationStatus.Text = message;progress.Style = ProgressBarStyle.Marquee;
        try { await Task.Run(() => action(operation.Token));operationStatus.Text = "Done. " + store.CatalogMessage; }
        catch (OperationCanceledException) { operationStatus.Text = "Cancelled. Any in-progress installation was rolled back."; }
        catch (Exception ex)
        {
            operationStatus.Text = ex.Message;
            if (!closing) MessageBox.Show(this, ex.Message, "Map operation stopped", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        finally
        {
            busy = false;operation.Dispose();operation = null;progress.Style = ProgressBarStyle.Continuous;progress.Value = 0;SetBusy();FillMaps();
            if (closing) Close();
        }
    }
    private void SetBusy()
    {
        foreach (Control c in new Control[] { search, category, statusFilter, grid, source, download, enable, disable, folder, refresh, runtime }) c.Enabled = !busy;
        cancel.Visible = busy;cancel.Enabled = busy;cancel.Text = "Cancel";progress.Visible = busy;
    }
    private void FillMaps()
    {
        if (busy) return;string? selected = Selected?.Id;
        grid.Rows.Clear();var maps = CatalogPresentation.Order((store.Catalog?.Maps ?? []).Concat(store.State.Installed.Values.Select(m => m.Entry)).Concat(store.State.Downloads.Values)
            .Where(m => !m.IsAssetPack).DistinctBy(m => m.Id)).ToList();
        string q = search.Text.Trim();
        foreach (var map in maps)
        {
            string state = store.Status(map);
            if (q.Length > 0 && !(map.Name + " " + map.Packages).Contains(q, StringComparison.OrdinalIgnoreCase)) continue;
            if (category.SelectedIndex > 0 && map.Category != category.Text) continue;
            bool matches = statusFilter.Text switch
            {
                "Updates available" => store.HasUpdate(map), "Enabled" => state.StartsWith("Enabled"), "Disabled" => state.StartsWith("Disabled"),
                "Downloaded" => store.State.Downloads.ContainsKey(map.Id), "Not downloaded" => !store.State.Downloads.ContainsKey(map.Id), _ => true
            };
            if (!matches) continue;
            int index = grid.Rows.Add(map.Name, map.Category, state);grid.Rows[index].Tag = map;
            if (CatalogPresentation.IsJpMap(map))
            {
                grid.Rows[index].Cells[0].Style.Padding = new Padding(30, 4, 4, 4);
                grid.Rows[index].Cells[0].ToolTipText = "Pinned • JP's Maps";
            }
            if (map.Id == selected) grid.CurrentCell = grid.Rows[index].Cells[0];
        }
        count.Text = $"{grid.Rows.Count} maps shown  •  {maps.Count} in the library  •  {store.State.Installed.Values.Count(m => m.Enabled && !m.Entry.IsAssetPack)} managed and enabled";
        catalogStatus.Text = store.CatalogMessage;NotesLoad = Detail();
    }
    private void PaintPinnedMap(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex != 0 || grid.Rows[e.RowIndex].Tag is not MapEntry map || !CatalogPresentation.IsJpMap(map)) return;
        e.Paint(e.ClipBounds, DataGridViewPaintParts.All);e.Handled = true;
        var graphics = e.Graphics!;var saved = graphics.Save();
        try
        {
            float scale = DeviceDpi / 96f;
            graphics.SetClip(e.CellBounds);
            graphics.TranslateTransform(e.CellBounds.Left + 17 * scale, e.CellBounds.Top + e.CellBounds.Height / 2f);
            graphics.ScaleTransform(scale, scale);graphics.RotateTransform(35);
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var brush = new SolidBrush(Accent);using var pen = new Pen(Accent, 1.6f);
            graphics.FillPolygon(brush, new PointF[] { new(-5, -7), new(5, -7), new(3, -4), new(3, 1), new(6, 4), new(-6, 4), new(-3, 1), new(-3, -4) });
            graphics.DrawLine(pen, 0, 4, 0, 10);
        }
        finally { graphics.Restore(saved); }
    }
    private async Task Detail()
    {
        notesCancel?.Cancel();notesCancel?.Dispose();notesCancel = new CancellationTokenSource();var ct = notesCancel.Token;
        var map = Selected;download.Enabled = enable.Enabled = map != null && !busy;disable.Enabled = map != null && !busy && store.CanDisable(map);
        if (map == null) { mapName.Text = "Your map library";metadata.Text = "JP's Maps • Community • Enhanced • Recovered";summary.Text = "Refresh the catalog to browse available maps.";notes.Clear();source.Enabled = false;return; }
        mapName.Text = map.Name;metadata.Text = $"{map.Category}  /  {map.Version}\n{map.Packages}  •  {SizeText(map.Bytes)}  •  {map.Files.Count} files";
        summary.Text = store.Status(map) + "\nVerified downloads. Automatic backups.\nDisabling keeps shared assets installed.";
        source.Enabled = map.HasSource && !busy;source.Checked = store.State.Installed.TryGetValue(map.Id, out var installed) && installed.IncludeSource;
        enable.Text = installed?.Enabled == true ? "Repair / enable" : "Enable map";
        download.Text = store.HasUpdate(map) ? "Download update" : "Download map";
        notes.Text = "Loading map notes…";
        try { var text = await repository.NotesAsync(map, ct);if (!ct.IsCancellationRequested && !IsDisposed) notes.Text = text; }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception) { if (!ct.IsCancellationRequested && !IsDisposed) notes.Text = "Map notes are unavailable offline. Previously downloaded maps can still be enabled.\n\n" + RepositoryClient.RepositoryUrl; }
    }
    private static string SizeText(long bytes) => bytes >= 1024 * 1024 * 1024 ? $"{bytes / (1024.0 * 1024 * 1024):0.0} GB" : $"{bytes / (1024.0 * 1024):0.0} MB";
    public async Task<string> SmokeAsync(string? artifact = null)
    {
        var allMaps = grid.Rows.Cast<DataGridViewRow>().Select(row => (MapEntry)row.Tag!).ToList();
        int jpCount = allMaps.Count(CatalogPresentation.IsJpMap);
        if (jpCount == 0 || !allMaps.Take(jpCount).All(CatalogPresentation.IsJpMap)) throw new InvalidOperationException("JP's Maps are not pinned first.");
        category.SelectedItem = CatalogPresentation.JpMaps;
        if (grid.Rows.Count != jpCount) throw new InvalidOperationException("JP's Maps filter failed.");
        category.SelectedItem = "Enhanced";if (grid.Rows.Count != 1) throw new InvalidOperationException("Enhanced filter failed.");
        category.SelectedIndex = 0;search.Text = "Shipment";if (grid.Rows.Count != 1) throw new InvalidOperationException("Map search failed.");
        search.Clear();foreach (DataGridViewRow row in grid.Rows) if (row.Tag is MapEntry m && m.Name == "Shipment") { grid.CurrentCell = row.Cells[0];break; }
        await NotesLoad;
        if (artifact != null)
        {
            Directory.CreateDirectory(artifact);
            var assetRoot = Path.Combine(artifact, "asset-ui-fixture");
            Directory.CreateDirectory(Path.Combine(assetRoot, "System"));Directory.CreateDirectory(Path.Combine(assetRoot, "Packages"));
            File.WriteAllText(Path.Combine(assetRoot, "System", "SCCT_Versus.exe"), "disposable UI fixture");
            var assetRepository = new AssetSmokeRepository();
            using (var assetStore = new MapStore(assetRoot, assetRepository))
            {
                assetStore.UseFallbackCatalog(await assetRepository.FetchCatalogAsync(default));
                var pack = assetStore.Catalog!.AssetPacks[0];
                assetStore.State.Installed[pack.Id] = new InstalledMap { Entry = pack with { Version = "v1.0.0" }, Enabled = true };
                using var assets = new AssetLibraryDialog(assetStore, assetRepository);assets.Show(this);await assets.SmokeAsync(artifact);assets.Close();
            }
            var originalSize = Size;var originalStatus = operationStatus.Text;
            try
            {
                Size = MinimumSize;busy = true;SetBusy();operationStatus.Text = "Downloading Shipment…   2/5";
                PerformLayout();Refresh();
                using var bitmap = new Bitmap(Width, Height);DrawToBitmap(bitmap, new Rectangle(Point.Empty, Size));
                bitmap.Save(Path.Combine(artifact, "map-manager-busy-minimum.png"));
            }
            finally { busy = false;SetBusy();Size = originalSize;operationStatus.Text = originalStatus;NotesLoad = Detail(); }
        }
        await NotesLoad;
        return $"Collection: {category.Text}\nShow: {statusFilter.Text}\nSelected: {Selected?.Name}\nDisable enabled: {disable.Enabled}\nMap and asset filter, search and version checks passed.";
    }
    private sealed class AssetSmokeRepository : IRepositoryClient
    {
        public Task<Catalog> FetchCatalogAsync(CancellationToken cancel)
        {
            var hash = new string('a', 40);
            return Task.FromResult(new Catalog(hash, DateTimeOffset.UtcNow, []) { AssetPacks = [
                new MapEntry("assets/Rainbow Six Vegas", "Rainbow Six Vegas — Casino", "Editor assets", "v1.1.0", hash, "README.md", [new MapFile("R6V.usx", "Packages/StaticMeshes/R6V.usx", hash, 96915499)]),
                new MapEntry("assets/Calypso textures", "Calypso textures", "Editor assets", "v1.0.0", hash, "README.md", [new MapFile("Calypso.utx", "Packages/Textures/Calypso.utx", hash, 5401739)])] });
        }
        public Task DownloadAsync(MapEntry map, MapFile file, string destination, CancellationToken cancel) => throw new InvalidOperationException("UI fixture does not download files.");
        public Task<string> NotesAsync(MapEntry map, CancellationToken cancel) => Task.FromResult("Sample release notes for the disposable UI check.\n\nTextures and meshes stay installed so maps can continue to use them.");
    }
}
