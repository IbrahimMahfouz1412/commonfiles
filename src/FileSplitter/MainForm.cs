namespace FileSplitter;

public sealed class MainForm : Form
{
    private static readonly string[] Units = ["KB", "MB", "GB"];

    // Split tab
    private readonly TextBox _splitInput = new() { AccessibleName = "File to split", Dock = DockStyle.Fill, AllowDrop = true };
    private readonly NumericUpDown _sizeValue = new() { AccessibleName = "Max part size", Minimum = 1, Maximum = 1_000_000, Value = 100, DecimalPlaces = 0, Width = 110 };
    private readonly ComboBox _sizeUnit = new() { AccessibleName = "Size unit", DropDownStyle = ComboBoxStyle.DropDownList, Width = 70 };
    private readonly TextBox _splitOutDir = new() { AccessibleName = "Output folder", Dock = DockStyle.Fill };
    private readonly Label _splitInfo = new() { AutoSize = true, ForeColor = SystemColors.GrayText };
    private readonly Button _splitButton = new() { Text = "Split", Width = 110, Height = 32 };

    // Join tab
    private readonly TextBox _joinInput = new() { AccessibleName = "Part file", Dock = DockStyle.Fill, AllowDrop = true };
    private readonly TextBox _joinOutput = new() { AccessibleName = "Output file", Dock = DockStyle.Fill };
    private readonly Label _joinInfo = new() { AutoSize = true, ForeColor = SystemColors.GrayText };
    private readonly Button _joinButton = new() { Text = "Join", Width = 110, Height = 32 };

    // Shared
    private readonly ProgressBar _progress = new() { Dock = DockStyle.Fill, Maximum = 1000, Height = 22 };
    private readonly Label _status = new() { Dock = DockStyle.Fill, Text = "Ready", AutoEllipsis = true };
    private readonly Button _cancelButton = new() { Text = "Cancel", Width = 90, Enabled = false };

    private CancellationTokenSource? _cts;

    public MainForm()
    {
        Text = "File Splitter";
        Font = new Font("Segoe UI", 9.5f);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(640, 330);
        MinimumSize = new Size(560, 360);
        StartPosition = FormStartPosition.CenterScreen;
        Icon = SystemIcons.Application;

        _sizeUnit.Items.AddRange(Units);
        _sizeUnit.SelectedItem = "MB";

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(BuildSplitTab());
        tabs.TabPages.Add(BuildJoinTab());

        var bottom = new TableLayoutPanel { Dock = DockStyle.Bottom, ColumnCount = 2, RowCount = 2, Height = 70, Padding = new Padding(8) };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bottom.Controls.Add(_progress, 0, 0);
        bottom.Controls.Add(_cancelButton, 1, 0);
        bottom.Controls.Add(_status, 0, 1);
        bottom.SetColumnSpan(_status, 2);

        Controls.Add(tabs);
        Controls.Add(bottom);

        _splitInput.TextChanged += (_, _) => UpdateSplitInfo();
        _sizeValue.ValueChanged += (_, _) => UpdateSplitInfo();
        _sizeUnit.SelectedIndexChanged += (_, _) => UpdateSplitInfo();
        _joinInput.TextChanged += (_, _) => { SuggestJoinOutput(); UpdateJoinInfo(); };
        _splitButton.Click += async (_, _) => await RunSplitAsync();
        _joinButton.Click += async (_, _) => await RunJoinAsync();
        _cancelButton.Click += (_, _) => _cts?.Cancel();
        FormClosing += (_, e) =>
        {
            if (_cts is null) return;
            if (MessageBox.Show(this, "An operation is in progress. Cancel it and exit?", Text,
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.No)
                e.Cancel = true;
            else
                _cts.Cancel();
        };

        EnableDrop(_splitInput, path =>
        {
            _splitInput.Text = path;
            _splitOutDir.Text = Path.GetDirectoryName(path) ?? "";
        });
        EnableDrop(_joinInput, SetJoinInput);
    }

    public void PreloadSplitFile(string path)
    {
        _splitInput.Text = path;
        _splitOutDir.Text = Path.GetDirectoryName(path) ?? "";
    }

    private TabPage BuildSplitTab()
    {
        var page = new TabPage("Split");
        var grid = NewGrid();

        var browseIn = new Button { Text = "Browse...", AutoSize = true };
        browseIn.Click += (_, _) =>
        {
            using var dlg = new OpenFileDialog { Title = "Select file to split" };
            if (dlg.ShowDialog(this) == DialogResult.OK) PreloadSplitFile(dlg.FileName);
        };

        var browseOut = new Button { Text = "Browse...", AutoSize = true };
        browseOut.Click += (_, _) =>
        {
            using var dlg = new FolderBrowserDialog { Description = "Select output folder", UseDescriptionForTitle = true };
            if (dlg.ShowDialog(this) == DialogResult.OK) _splitOutDir.Text = dlg.SelectedPath;
        };

        var sizeRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        sizeRow.Controls.Add(_sizeValue);
        sizeRow.Controls.Add(_sizeUnit);

        AddRow(grid, 0, "File to split:", _splitInput, browseIn);
        AddRow(grid, 1, "Max part size:", sizeRow, null);
        AddRow(grid, 2, "Output folder:", _splitOutDir, browseOut);
        grid.Controls.Add(_splitInfo, 1, 3);
        grid.Controls.Add(_splitButton, 1, 4);

        page.Controls.Add(grid);
        return page;
    }

    private TabPage BuildJoinTab()
    {
        var page = new TabPage("Join");
        var grid = NewGrid();

        var browseIn = new Button { Text = "Browse...", AutoSize = true };
        browseIn.Click += (_, _) =>
        {
            using var dlg = new OpenFileDialog { Title = "Select the first part (e.g. file.zip.001)", Filter = "Part files (*.0*)|*.0*|All files (*.*)|*.*" };
            if (dlg.ShowDialog(this) == DialogResult.OK) SetJoinInput(dlg.FileName);
        };

        var browseOut = new Button { Text = "Browse...", AutoSize = true };
        browseOut.Click += (_, _) =>
        {
            using var dlg = new SaveFileDialog { Title = "Save joined file as", FileName = Path.GetFileName(_joinOutput.Text) };
            if (dlg.ShowDialog(this) == DialogResult.OK) _joinOutput.Text = dlg.FileName;
        };

        AddRow(grid, 0, "Any part file:", _joinInput, browseIn);
        AddRow(grid, 1, "Output file:", _joinOutput, browseOut);
        grid.Controls.Add(_joinInfo, 1, 2);
        grid.Controls.Add(_joinButton, 1, 3);

        page.Controls.Add(grid);
        return page;
    }

    private static TableLayoutPanel NewGrid()
    {
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Padding = new Padding(10) };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        return grid;
    }

    private static void AddRow(TableLayoutPanel grid, int row, string label, Control field, Control? button)
    {
        grid.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 8, 8, 3) }, 0, row);
        grid.Controls.Add(field, 1, row);
        if (button != null) grid.Controls.Add(button, 2, row);
    }

    private static void EnableDrop(TextBox box, Action<string> onDrop)
    {
        box.DragEnter += (_, e) =>
            e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;
        box.DragDrop += (_, e) =>
        {
            if (e.Data?.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files) onDrop(files[0]);
        };
    }

    private void SetJoinInput(string path)
    {
        _joinInput.Text = path;
        _joinOutput.Text = SplitEngine.DefaultJoinOutput(path);
    }

    private string _autoJoinOutput = "";

    // Typed/pasted part paths get the same default output as Browse/drop, unless the user chose one.
    private void SuggestJoinOutput()
    {
        if (_joinOutput.Text.Length != 0 && _joinOutput.Text != _autoJoinOutput) return;
        string path = _joinInput.Text.Trim();
        _autoJoinOutput = path.Length == 0 ? "" : SplitEngine.DefaultJoinOutput(path);
        _joinOutput.Text = _autoJoinOutput;
    }

    private long MaxPartBytes()
    {
        long mult = (_sizeUnit.SelectedItem as string) switch
        {
            "KB" => 1L << 10,
            "GB" => 1L << 30,
            _ => 1L << 20,
        };
        return (long)_sizeValue.Value * mult;
    }

    private void UpdateSplitInfo()
    {
        string path = _splitInput.Text.Trim();
        if (!File.Exists(path)) { _splitInfo.Text = ""; return; }
        long len = new FileInfo(path).Length;
        long max = MaxPartBytes();
        long parts = Math.Max(1, (len + max - 1) / max);
        _splitInfo.Text = $"File size: {SplitEngine.FormatSize(len)}  →  {parts} part(s)";
    }

    private void UpdateJoinInfo()
    {
        try
        {
            var parts = SplitEngine.FindParts(_joinInput.Text.Trim());
            long total = parts.Sum(p => new FileInfo(p).Length);
            _joinInfo.Text = $"Found {parts.Count} part(s), total {SplitEngine.FormatSize(total)}";
        }
        catch
        {
            _joinInfo.Text = "";
        }
    }

    private async Task RunSplitAsync()
    {
        string input = _splitInput.Text.Trim();
        string outDir = _splitOutDir.Text.Trim();
        if (!File.Exists(input)) { Warn("Please choose an existing file to split."); return; }
        if (outDir.Length == 0) outDir = Path.GetDirectoryName(input) ?? ".";

        await RunOperationAsync("Splitting", async (progress, ct) =>
        {
            var parts = await SplitEngine.SplitAsync(input, MaxPartBytes(), outDir, progress, ct);
            return $"Created {parts.Count} part(s) in {outDir}";
        });
    }

    private async Task RunJoinAsync()
    {
        IReadOnlyList<string> parts;
        try { parts = SplitEngine.FindParts(_joinInput.Text.Trim()); }
        catch (Exception ex) { Warn(ex.Message); return; }

        string output = _joinOutput.Text.Trim();
        if (output.Length == 0) { Warn("Please choose an output file."); return; }
        if (parts.Any(p => string.Equals(Path.GetFullPath(p), Path.GetFullPath(output), StringComparison.OrdinalIgnoreCase)))
        {
            Warn("Output file cannot be one of the part files.");
            return;
        }
        if (File.Exists(output) && MessageBox.Show(this, $"\"{output}\" already exists. Overwrite?", Text,
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;

        await RunOperationAsync("Joining", async (progress, ct) =>
        {
            await SplitEngine.JoinAsync(parts, output, progress, ct);
            return $"Joined {parts.Count} part(s) into {output}";
        });
    }

    private async Task RunOperationAsync(string verb, Func<IProgress<double>, CancellationToken, Task<string>> work)
    {
        _cts = new CancellationTokenSource();
        SetBusy(true);
        _status.Text = $"{verb}...";
        var progress = new Progress<double>(p =>
        {
            _progress.Value = (int)Math.Clamp(p * 1000, 0, 1000);
            _status.Text = $"{verb}... {p:P0}";
        });

        try
        {
            string result = await Task.Run(() => work(progress, _cts.Token));
            _progress.Value = 1000;
            _status.Text = result;
            MessageBox.Show(this, result, Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (OperationCanceledException)
        {
            _progress.Value = 0;
            _status.Text = "Cancelled.";
        }
        catch (Exception ex)
        {
            _progress.Value = 0;
            _status.Text = "Failed: " + ex.Message;
            MessageBox.Show(this, ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        _splitButton.Enabled = _joinButton.Enabled = !busy;
        _cancelButton.Enabled = busy;
        UseWaitCursor = busy;
    }

    private void Warn(string message) =>
        MessageBox.Show(this, message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
}
