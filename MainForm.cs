using System.ComponentModel;
using System.Diagnostics;
using System.Security.Authentication;

namespace VramOp;

internal sealed class MainForm : Form
{
    private const string AppDisplayName = "VRAM Vue";

    private readonly AppSettings _settings;
    private readonly SystemTelemetryCollector _collector = new();
    private readonly TelemetryServer _server;
    private readonly RemoteTelemetryClient _remoteClient = new();
    private readonly Dictionary<Guid, HostSnapshot> _hostSnapshots = [];
    private readonly Dictionary<Guid, HostCard> _hostCards = [];
    private readonly BindingSource _processBinding = new();
    private readonly System.Windows.Forms.Timer _pollTimer = new();
    private readonly NotifyIcon _notifyIcon = new();
    private readonly Icon _appIcon;

    private readonly Panel _dashboardPage = new BufferedPanel();
    private readonly Panel _settingsPage = new BufferedPanel();
    private readonly BufferedFlowLayoutPanel _hostCardsPanel = new();
    private readonly DataGridView _processGrid = new BufferedDataGridView();
    private readonly MetricCard _cpuCard = new() { Title = "CPU", AccentColor = AppTheme.Accent };
    private readonly MetricCard _ramCard = new() { Title = "RAM", AccentColor = AppTheme.Good };
    private readonly MetricCard _gpuCard = new() { Title = "GPU", AccentColor = AppTheme.Warning };
    private readonly MetricCard _vramCard = new() { Title = "VRAM", AccentColor = AppTheme.Danger };
    private readonly Label _statusLabel = new BufferedLabel();
    private readonly Label _listenerStatusLabel = new BufferedLabel();
    private readonly MaskedTextBox _intervalBox = new("9999");
    private readonly RoundedButton _killButton = new() { Text = "Kill selected", Width = 132 };
    private readonly RoundedButton _killParentButton = new() { Text = "End parent", Width = 124 };
    private readonly RoundedButton _stopServiceButton = new() { Text = "Stop svc", Width = 96 };
    private readonly RoundedButton _startServiceButton = new() { Text = "Start svc", Width = 96 };
    private readonly RoundedButton _disableServiceButton = new() { Text = "Disable svc", Width = 112 };
    private readonly RoundedButton _enableServiceButton = new() { Text = "Enable svc", Width = 104 };
    private readonly RoundedButton _dashboardButton = new() { Text = "Dashboard", Width = 160 };
    private readonly RoundedButton _settingsButton = new() { Text = "Settings", Width = 140 };
    private readonly CheckBox _listenerEnabledBox = new();
    private readonly CheckBox _confirmKillsBox = new();
    private readonly NumericUpDown _listenerPortBox = new();
    private readonly TextBox _listenerUserBox = new();
    private readonly TextBox _listenerPasswordBox = new();
    private readonly ListBox _remoteListBox = new();
    private readonly TextBox _remoteNameBox = new();
    private readonly TextBox _remoteHostBox = new();
    private readonly NumericUpDown _remotePortBox = new();
    private readonly TextBox _remoteUserBox = new();
    private readonly TextBox _remotePasswordBox = new();
    private readonly TextBox _remoteThumbprintBox = new();

    private Guid _localHostId = Guid.Empty;
    private Guid? _selectedHostId;
    private bool _exitRequested;
    private bool _refreshInProgress;
    private bool _hasShownTrayTip;
    private bool _hostListDirty = true;
    private Guid? _lastRenderedProcessHostId;
    private string _lastRenderedProcessSignature = string.Empty;
    private string _lastStatusText = string.Empty;
    private string _lastListenerStatusText = string.Empty;
    private CancellationTokenSource? _refreshCts;

    public MainForm()
    {
        _settings = SettingsStore.Load();
        _server = new TelemetryServer(_collector);
        _appIcon = AppIconFactory.CreateIcon();

        BuildUi();
        LoadSettingsIntoControls();
        WireEvents();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _refreshCts?.Cancel();
            _refreshCts?.Dispose();
            _pollTimer.Dispose();
            _notifyIcon.Dispose();
            _appIcon.Dispose();
            _collector.Dispose();
            _server.StopAsync().GetAwaiter().GetResult();
        }

        base.Dispose(disposing);
    }

    private void BuildUi()
    {
        Text = AppDisplayName;
        Icon = _appIcon;
        BackColor = AppTheme.Background;
        ForeColor = AppTheme.Text;
        Font = new Font("Segoe UI", 9F);
        AutoScaleMode = AutoScaleMode.Font;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1120, 760);
        Size = new Size(1320, 860);

        ConfigureTrayIcon();

        var root = new BufferedTableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = AppTheme.Background,
            Padding = new Padding(14),
            ColumnCount = 1,
            RowCount = 3
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 104));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));

        root.Controls.Add(BuildHeader(), 0, 0);
        root.Controls.Add(BuildPages(), 0, 1);
        root.Controls.Add(_statusLabel, 0, 2);

        _statusLabel.Dock = DockStyle.Fill;
        _statusLabel.ForeColor = AppTheme.MutedText;
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        SetStatusText("Starting");

        Controls.Add(root);
    }

    private Control BuildHeader()
    {
        var header = new BufferedTableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 2,
            BackColor = AppTheme.Background
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 340));
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));

        var title = new Label
        {
            Dock = DockStyle.Fill,
            Text = AppDisplayName,
            Font = new Font("Segoe UI", 16F, FontStyle.Bold),
            ForeColor = AppTheme.Text,
            TextAlign = ContentAlignment.MiddleLeft
        };

        _listenerStatusLabel.Dock = DockStyle.Fill;
        _listenerStatusLabel.ForeColor = AppTheme.MutedText;
        _listenerStatusLabel.TextAlign = ContentAlignment.MiddleLeft;
        _listenerStatusLabel.Text = "Listener starting";

        var nav = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            BackColor = AppTheme.Background
        };
        nav.Controls.Add(_settingsButton);
        nav.Controls.Add(_dashboardButton);

        header.Controls.Add(title, 0, 0);
        header.Controls.Add(_listenerStatusLabel, 0, 1);
        header.Controls.Add(nav, 1, 0);
        header.SetRowSpan(nav, 2);
        return header;
    }

    private Control BuildPages()
    {
        var pages = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = AppTheme.Background
        };

        _dashboardPage.Dock = DockStyle.Fill;
        _settingsPage.Dock = DockStyle.Fill;
        _dashboardPage.BackColor = AppTheme.Background;
        _settingsPage.BackColor = AppTheme.Background;

        BuildDashboardPage();
        BuildSettingsPage();

        pages.Controls.Add(_settingsPage);
        pages.Controls.Add(_dashboardPage);
        ShowDashboardPage();
        return pages;
    }

    private void BuildDashboardPage()
    {
        var dashboard = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = AppTheme.Background
        };
        dashboard.RowStyles.Add(new RowStyle(SizeType.Absolute, 176));
        dashboard.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        dashboard.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));

        var metrics = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 1,
            BackColor = AppTheme.Background
        };

        for (var i = 0; i < 4; i++)
        {
            metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        }

        metrics.Controls.Add(_cpuCard, 0, 0);
        metrics.Controls.Add(_ramCard, 1, 0);
        metrics.Controls.Add(_gpuCard, 2, 0);
        metrics.Controls.Add(_vramCard, 3, 0);

        foreach (MetricCard card in metrics.Controls)
        {
            card.Dock = DockStyle.Fill;
        }

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            BackColor = AppTheme.Background
        };
        split.SizeChanged += (_, _) =>
        {
            const int panel1MinSize = 320;
            const int panel2MinSize = 520;

            if (split.Width <= panel1MinSize + panel2MinSize)
            {
                return;
            }

            var desired = Math.Min(430, split.Width - panel2MinSize);
            split.SplitterDistance = Math.Max(panel1MinSize, desired);
        };

        var hostPanel = new RoundedPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            BackColor = AppTheme.Surface
        };
        _hostCardsPanel.Dock = DockStyle.Fill;
        _hostCardsPanel.FlowDirection = FlowDirection.TopDown;
        _hostCardsPanel.WrapContents = false;
        _hostCardsPanel.AutoScroll = true;
        _hostCardsPanel.BackColor = AppTheme.Surface;
        hostPanel.Controls.Add(_hostCardsPanel);

        var processPanel = new RoundedPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            BackColor = AppTheme.Surface
        };
        ConfigureProcessGrid();
        processPanel.Controls.Add(_processGrid);

        split.Panel1.Controls.Add(hostPanel);
        split.Panel2.Controls.Add(processPanel);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            BackColor = AppTheme.Background,
            AutoScroll = true,
            Padding = new Padding(0, 8, 0, 0)
        };
        var refreshButton = new RoundedButton { Text = "Refresh now", Width = 132 };
        var hideButton = new RoundedButton { Text = "Hide to tray", Width = 132 };
        refreshButton.Click += async (_, _) => await RefreshAllHostsAsync();
        hideButton.Click += (_, _) => HideToTray(showTip: true);
        actions.Controls.Add(hideButton);
        actions.Controls.Add(refreshButton);
        actions.Controls.Add(_enableServiceButton);
        actions.Controls.Add(_disableServiceButton);
        actions.Controls.Add(_startServiceButton);
        actions.Controls.Add(_stopServiceButton);
        actions.Controls.Add(_killParentButton);
        actions.Controls.Add(_killButton);

        dashboard.Controls.Add(metrics, 0, 0);
        dashboard.Controls.Add(split, 0, 1);
        dashboard.Controls.Add(actions, 0, 2);
        _dashboardPage.Controls.Add(dashboard);
    }

    private void ConfigureProcessGrid()
    {
        _processGrid.Dock = DockStyle.Fill;
        _processGrid.AutoGenerateColumns = false;
        _processGrid.AllowUserToAddRows = false;
        _processGrid.AllowUserToDeleteRows = false;
        _processGrid.AllowUserToResizeRows = false;
        _processGrid.BackgroundColor = AppTheme.Surface;
        _processGrid.BorderStyle = BorderStyle.None;
        _processGrid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        _processGrid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        _processGrid.DataSource = _processBinding;
        _processGrid.EnableHeadersVisualStyles = false;
        _processGrid.GridColor = AppTheme.Border;
        _processGrid.MultiSelect = false;
        _processGrid.ReadOnly = true;
        _processGrid.RowHeadersVisible = false;
        _processGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _processGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        _processGrid.Font = new Font("Segoe UI", 9F);
        _processGrid.ColumnHeadersDefaultCellStyle.BackColor = AppTheme.SurfaceRaised;
        _processGrid.ColumnHeadersDefaultCellStyle.ForeColor = AppTheme.Text;
        _processGrid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        _processGrid.ColumnHeadersHeight = Math.Max(38, _processGrid.ColumnHeadersDefaultCellStyle.Font.Height + 16);
        _processGrid.RowTemplate.Height = Math.Max(34, _processGrid.Font.Height + 14);
        _processGrid.DefaultCellStyle.BackColor = AppTheme.Surface;
        _processGrid.DefaultCellStyle.ForeColor = AppTheme.Text;
        _processGrid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(46, 55, 70);
        _processGrid.DefaultCellStyle.SelectionForeColor = AppTheme.Text;
        _processGrid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(25, 31, 40);

        AddProcessColumn(nameof(GpuProcessInfo.ProcessName), "Process", DataGridViewAutoSizeColumnMode.AllCells, 0, DataGridViewContentAlignment.MiddleLeft);
        AddProcessColumn(nameof(GpuProcessInfo.ProcessId), "PID", DataGridViewAutoSizeColumnMode.AllCells, 0, DataGridViewContentAlignment.MiddleRight);
        AddProcessColumn(nameof(GpuProcessInfo.LocalVramBytes), "VRAM", DataGridViewAutoSizeColumnMode.AllCells, 0, DataGridViewContentAlignment.MiddleRight);
        AddProcessColumn(nameof(GpuProcessInfo.SharedBytes), "Shared", DataGridViewAutoSizeColumnMode.AllCells, 0, DataGridViewContentAlignment.MiddleRight);
        AddProcessColumn(nameof(GpuProcessInfo.RestartBehavior), "Restart", DataGridViewAutoSizeColumnMode.AllCells, 0, DataGridViewContentAlignment.MiddleLeft);
        AddProcessColumn(nameof(GpuProcessInfo.WindowTitle), "Window", DataGridViewAutoSizeColumnMode.Fill, 65, DataGridViewContentAlignment.MiddleLeft);
        AddProcessColumn(nameof(GpuProcessInfo.Notes), "Notes", DataGridViewAutoSizeColumnMode.Fill, 35, DataGridViewContentAlignment.MiddleLeft);

        _processGrid.CellFormatting += (_, e) =>
        {
            if (e.RowIndex < 0 || e.Value is not long bytes)
            {
                return;
            }

            var propertyName = _processGrid.Columns[e.ColumnIndex].DataPropertyName;
            if (propertyName is nameof(GpuProcessInfo.LocalVramBytes) or nameof(GpuProcessInfo.SharedBytes))
            {
                e.Value = Formatters.Bytes(bytes);
                e.FormattingApplied = true;
            }
        };
    }

    private void AddProcessColumn(
        string propertyName,
        string header,
        DataGridViewAutoSizeColumnMode autoSizeMode,
        float fillWeight,
        DataGridViewContentAlignment alignment)
    {
        var column = new DataGridViewTextBoxColumn
        {
            DataPropertyName = propertyName,
            HeaderText = header,
            Name = propertyName,
            AutoSizeMode = autoSizeMode,
            FillWeight = fillWeight <= 0 ? 100 : fillWeight,
            MinimumWidth = autoSizeMode == DataGridViewAutoSizeColumnMode.Fill ? 80 : 48,
            SortMode = DataGridViewColumnSortMode.Automatic
        };
        column.DefaultCellStyle.Alignment = alignment;
        _processGrid.Columns.Add(column);
    }

    private void BuildSettingsPage()
    {
        var scroll = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = AppTheme.Background,
            Padding = new Padding(0, 6, 0, 0)
        };

        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = AppTheme.Background
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

        content.Controls.Add(BuildListenerSettings(), 0, 0);
        content.Controls.Add(BuildRemoteSettings(), 1, 0);
        scroll.Controls.Add(content);
        _settingsPage.Controls.Add(scroll);
    }

    private Control BuildListenerSettings()
    {
        var panel = new RoundedPanel
        {
            Dock = DockStyle.Top,
            Margin = new Padding(0, 0, 12, 0),
            BackColor = AppTheme.Surface,
            MinimumSize = new Size(420, 420)
        };

        var layout = CreateSettingsLayout();
        layout.Controls.Add(CreateSectionTitle("Local HTTPS listener"), 0, 0);
        layout.SetColumnSpan(layout.GetControlFromPosition(0, 0)!, 2);

        _listenerEnabledBox.Text = "Enable telemetry listener";
        _listenerEnabledBox.ForeColor = AppTheme.Text;
        _listenerEnabledBox.AutoSize = true;
        layout.Controls.Add(_listenerEnabledBox, 0, 1);
        layout.SetColumnSpan(_listenerEnabledBox, 2);

        _confirmKillsBox.Text = "Confirm before ending tasks";
        _confirmKillsBox.ForeColor = AppTheme.Text;
        _confirmKillsBox.AutoSize = true;
        layout.Controls.Add(_confirmKillsBox, 0, 2);
        layout.SetColumnSpan(_confirmKillsBox, 2);

        AddLabeledControl(layout, "Update every", CreateIntervalEditor(), 3);
        AddLabeledControl(layout, "Port", _listenerPortBox, 4);
        AddLabeledControl(layout, "Username", _listenerUserBox, 5);
        AddLabeledControl(layout, "Password", _listenerPasswordBox, 6);

        _listenerPortBox.Minimum = 1024;
        _listenerPortBox.Maximum = 65535;
        _listenerPortBox.BackColor = AppTheme.SurfaceRaised;
        _listenerPortBox.ForeColor = AppTheme.Text;
        _listenerUserBox.BackColor = AppTheme.SurfaceRaised;
        _listenerUserBox.ForeColor = AppTheme.Text;
        _listenerPasswordBox.BackColor = AppTheme.SurfaceRaised;
        _listenerPasswordBox.ForeColor = AppTheme.Text;
        _listenerPasswordBox.UseSystemPasswordChar = true;
        ConfigureTextInput(_listenerUserBox, 64, InputRules.IsBasicAuthUsernameChar, InputRules.NormalizeBasicAuthUsername);
        ConfigureTextInput(_listenerPasswordBox, 128, InputRules.IsPasswordChar, InputRules.NormalizePassword);

        var saveButton = new RoundedButton { Text = "Save and restart listener", Width = 190 };
        saveButton.Click += async (_, _) => await SaveListenerSettingsAsync();
        layout.Controls.Add(saveButton, 1, 7);

        var note = CreateNoteLabel("Each host uses a local self-signed certificate and requires TLS 1.3. Remote clients pin the certificate hash after the first successful connection.");
        layout.Controls.Add(note, 0, 8);
        layout.SetColumnSpan(note, 2);

        panel.Controls.Add(layout);
        return panel;
    }

    private Control CreateIntervalEditor()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = AppTheme.Surface
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 64));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 42));

        _intervalBox.BackColor = AppTheme.SurfaceRaised;
        _intervalBox.ForeColor = AppTheme.Text;
        _intervalBox.BorderStyle = BorderStyle.FixedSingle;
        _intervalBox.TextAlign = HorizontalAlignment.Right;
        _intervalBox.PromptChar = ' ';
        _intervalBox.HidePromptOnLeave = true;
        _intervalBox.CutCopyMaskFormat = MaskFormat.ExcludePromptAndLiterals;
        _intervalBox.TextMaskFormat = MaskFormat.ExcludePromptAndLiterals;
        _intervalBox.Width = 54;
        _intervalBox.Dock = DockStyle.Left;
        _intervalBox.Margin = new Padding(0, 7, 0, 7);

        var unitLabel = new Label
        {
            Text = "ms",
            Dock = DockStyle.Fill,
            ForeColor = AppTheme.MutedText,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(8, 0, 0, 0)
        };

        panel.Controls.Add(_intervalBox, 0, 0);
        panel.Controls.Add(unitLabel, 1, 0);
        return panel;
    }

    private Control BuildRemoteSettings()
    {
        var panel = new RoundedPanel
        {
            Dock = DockStyle.Top,
            Margin = new Padding(12, 0, 0, 0),
            BackColor = AppTheme.Surface,
            MinimumSize = new Size(500, 560)
        };

        var layout = CreateSettingsLayout();
        layout.Controls.Add(CreateSectionTitle("Remote hosts"), 0, 0);
        layout.SetColumnSpan(layout.GetControlFromPosition(0, 0)!, 2);

        _remoteListBox.Height = 140;
        _remoteListBox.BackColor = AppTheme.SurfaceRaised;
        _remoteListBox.ForeColor = AppTheme.Text;
        _remoteListBox.BorderStyle = BorderStyle.FixedSingle;
        layout.Controls.Add(_remoteListBox, 0, 1);
        layout.SetColumnSpan(_remoteListBox, 2);

        AddLabeledControl(layout, "Name", _remoteNameBox, 2);
        AddLabeledControl(layout, "Host/IP", _remoteHostBox, 3);
        AddLabeledControl(layout, "Port", _remotePortBox, 4);
        AddLabeledControl(layout, "Username", _remoteUserBox, 5);
        AddLabeledControl(layout, "Password", _remotePasswordBox, 6);
        AddLabeledControl(layout, "Pinned cert", _remoteThumbprintBox, 7);

        _remotePortBox.Minimum = 1024;
        _remotePortBox.Maximum = 65535;
        _remotePortBox.Value = 54545;
        _remotePasswordBox.UseSystemPasswordChar = true;
        foreach (var textBox in new[] { _remoteNameBox, _remoteHostBox, _remoteUserBox, _remotePasswordBox, _remoteThumbprintBox })
        {
            textBox.BackColor = AppTheme.SurfaceRaised;
            textBox.ForeColor = AppTheme.Text;
            textBox.BorderStyle = BorderStyle.FixedSingle;
        }
        ConfigureTextInput(_remoteNameBox, 64, InputRules.IsDisplayNameChar, InputRules.NormalizeDisplayName);
        ConfigureTextInput(_remoteHostBox, 253, InputRules.IsHostChar, InputRules.NormalizeHost);
        ConfigureTextInput(_remoteUserBox, 64, InputRules.IsBasicAuthUsernameChar, InputRules.NormalizeBasicAuthUsername);
        ConfigureTextInput(_remotePasswordBox, 128, InputRules.IsPasswordChar, InputRules.NormalizePassword);
        ConfigureTextInput(_remoteThumbprintBox, 95, InputRules.IsCertificateThumbprintChar, InputRules.NormalizeThumbprint);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            BackColor = AppTheme.Surface
        };
        var saveRemoteButton = new RoundedButton { Text = "Add/update", Width = 120 };
        var removeRemoteButton = new RoundedButton { Text = "Remove", Width = 92 };
        var clearPinButton = new RoundedButton { Text = "Clear pin", Width = 94 };
        saveRemoteButton.Click += (_, _) => SaveRemoteHost();
        removeRemoteButton.Click += (_, _) => RemoveSelectedRemoteHost();
        clearPinButton.Click += (_, _) => _remoteThumbprintBox.Text = string.Empty;
        buttons.Controls.Add(saveRemoteButton);
        buttons.Controls.Add(removeRemoteButton);
        buttons.Controls.Add(clearPinButton);
        layout.Controls.Add(buttons, 0, 8);
        layout.SetColumnSpan(buttons, 2);

        var note = CreateNoteLabel("First successful connection pins the server certificate SHA-256 hash here. Clear the pin only when you intentionally replaced that host certificate.");
        layout.Controls.Add(note, 0, 9);
        layout.SetColumnSpan(note, 2);

        panel.Controls.Add(layout);
        return panel;
    }

    private static TableLayoutPanel CreateSettingsLayout()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            BackColor = AppTheme.Surface
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 128));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        return layout;
    }

    private static Label CreateSectionTitle(string text) =>
        new()
        {
            Text = text,
            Dock = DockStyle.Fill,
            ForeColor = AppTheme.Text,
            Font = new Font("Segoe UI", 15F, FontStyle.Bold),
            Height = 44,
            TextAlign = ContentAlignment.MiddleLeft
        };

    private static Label CreateNoteLabel(string text) =>
        new()
        {
            Text = text,
            Dock = DockStyle.Top,
            AutoSize = true,
            ForeColor = AppTheme.MutedText,
            MaximumSize = new Size(720, 0),
            Padding = new Padding(0, 12, 0, 0)
        };

    private static void AddLabeledControl(TableLayoutPanel layout, string label, Control control, int row)
    {
        var labelControl = new Label
        {
            Text = label,
            Dock = DockStyle.Fill,
            ForeColor = AppTheme.MutedText,
            TextAlign = ContentAlignment.MiddleLeft,
            Height = 42
        };
        control.Dock = DockStyle.Fill;
        control.Margin = new Padding(0, 6, 0, 6);
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        layout.Controls.Add(labelControl, 0, row);
        layout.Controls.Add(control, 1, row);
    }

    private static void ConfigureTextInput(TextBox textBox, int maxLength, Func<char, bool> isAllowed, Func<string, string> normalize)
    {
        textBox.MaxLength = maxLength;
        textBox.KeyPress += (_, args) =>
        {
            if (!char.IsControl(args.KeyChar) && !isAllowed(args.KeyChar))
            {
                args.Handled = true;
            }
        };
        textBox.Leave += (_, _) =>
        {
            var cursor = textBox.SelectionStart;
            var normalized = normalize(textBox.Text);
            if (string.Equals(textBox.Text, normalized, StringComparison.Ordinal))
            {
                return;
            }

            textBox.Text = normalized;
            textBox.SelectionStart = Math.Min(cursor, textBox.TextLength);
        };
    }

    private void WireEvents()
    {
        Shown += async (_, _) =>
        {
            await RestartServerAsync();
            await RefreshAllHostsAsync();
            _pollTimer.Start();
        };

        Resize += (_, _) =>
        {
            if (WindowState == FormWindowState.Minimized)
            {
                HideToTray(showTip: true);
            }
        };

        FormClosing += OnFormClosing;
        _pollTimer.Tick += async (_, _) => await RefreshAllHostsAsync();
        _intervalBox.Leave += (_, _) => ApplyUpdateIntervalFromBox();
        _intervalBox.KeyDown += (_, args) =>
        {
            if (args.KeyCode != Keys.Enter)
            {
                return;
            }

            ApplyUpdateIntervalFromBox();
            args.SuppressKeyPress = true;
        };
        _intervalBox.Enter += (_, _) => _intervalBox.SelectAll();
        _killButton.Click += async (_, _) => await KillSelectedProcessAsync();
        _killParentButton.Click += async (_, _) => await KillSelectedParentProcessAsync();
        _stopServiceButton.Click += async (_, _) => await ControlSelectedServiceAsync(ServiceControlAction.Stop);
        _startServiceButton.Click += async (_, _) => await ControlSelectedServiceAsync(ServiceControlAction.Start);
        _disableServiceButton.Click += async (_, _) => await ControlSelectedServiceAsync(ServiceControlAction.Disable);
        _enableServiceButton.Click += async (_, _) => await ControlSelectedServiceAsync(ServiceControlAction.Enable);
        _dashboardButton.Click += (_, _) => ShowDashboardPage();
        _settingsButton.Click += (_, _) => ShowSettingsPage();
        _remoteListBox.SelectedIndexChanged += (_, _) => PopulateRemoteEditorFromSelection();
        _processGrid.SelectionChanged += (_, _) => UpdateActionButtons();
    }

    private void LoadSettingsIntoControls()
    {
        _settings.UpdateIntervalMs = Math.Clamp(_settings.UpdateIntervalMs, 250, 9999);
        _intervalBox.Text = _settings.UpdateIntervalMs.ToString("0000");
        _pollTimer.Interval = _settings.UpdateIntervalMs;

        _listenerEnabledBox.Checked = _settings.ListenerEnabled;
        _confirmKillsBox.Checked = _settings.ConfirmTaskKills;
        _listenerPortBox.Value = Math.Clamp(_settings.ListenerPort, 1024, 65535);
        _listenerUserBox.Text = _settings.Username;
        _listenerPasswordBox.Text = _settings.GetPassword();
        RefreshRemoteList();
    }

    private void ApplyUpdateIntervalFromBox()
    {
        var rawText = _intervalBox.Text.Trim();
        if (!int.TryParse(rawText, out var interval))
        {
            interval = _settings.UpdateIntervalMs;
        }

        interval = Math.Clamp(interval, 250, 9999);
        if (_settings.UpdateIntervalMs != interval)
        {
            _settings.UpdateIntervalMs = interval;
            _pollTimer.Interval = interval;
            SettingsStore.Save(_settings);
            SetStatusText($"Live telemetry - {_settings.UpdateIntervalMs:N0} ms updates");
        }

        var formatted = interval.ToString("0000");
        if (!string.Equals(_intervalBox.Text, formatted, StringComparison.Ordinal))
        {
            _intervalBox.Text = formatted;
        }
    }

    private void ConfigureTrayIcon()
    {
        var trayMenu = new ContextMenuStrip();
        trayMenu.Items.Add("Show", null, (_, _) => ShowFromTray());
        trayMenu.Items.Add("Refresh", null, async (_, _) => await RefreshAllHostsAsync());
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add("Exit", null, (_, _) =>
        {
            _exitRequested = true;
            Close();
        });

        _notifyIcon.ContextMenuStrip = trayMenu;
        _notifyIcon.Icon = _appIcon;
        _notifyIcon.Text = AppDisplayName;
        _notifyIcon.Visible = true;
        _notifyIcon.DoubleClick += (_, _) => ShowFromTray();
    }

    private async Task RestartServerAsync()
    {
        try
        {
            await _server.StartAsync(_settings);
            UpdateListenerStatus();
        }
        catch (Exception ex)
        {
            SetListenerStatusText($"Listener error: {ex.Message}");
            SetStatusText($"Listener error: {ex.Message}");
        }
    }

    private async Task RefreshAllHostsAsync()
    {
        if (_refreshInProgress)
        {
            return;
        }

        _refreshInProgress = true;
        _refreshCts?.Cancel();
        _refreshCts?.Dispose();
        _refreshCts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        try
        {
            RefreshLocalHost();
            await RefreshRemoteHostsAsync(_refreshCts.Token);
            UpdateHostCards();
            RefreshSelectedHostView();
            UpdateListenerStatus();
            SetStatusText($"Live telemetry - {_settings.UpdateIntervalMs:N0} ms updates");
        }
        finally
        {
            _refreshInProgress = false;
        }
    }

    private void RefreshLocalHost()
    {
        var telemetry = _collector.Read();

        if (_localHostId == Guid.Empty)
        {
            _localHostId = Guid.NewGuid();
            _selectedHostId ??= _localHostId;
        }

        _hostSnapshots[_localHostId] = new HostSnapshot
        {
            Id = _localHostId,
            DisplayName = $"{telemetry.HostName} (local)",
            IsLocal = true,
            Endpoint = "local",
            Telemetry = telemetry,
            Status = string.IsNullOrWhiteSpace(telemetry.ErrorMessage) ? "Local" : telemetry.ErrorMessage!,
            LastSeen = telemetry.CapturedAt,
            TrustedCertificateThumbprint = _server.CertificateThumbprint
        };
    }

    private async Task RefreshRemoteHostsAsync(CancellationToken cancellationToken)
    {
        foreach (var remote in _settings.RemoteHosts.ToArray())
        {
            if (string.IsNullOrWhiteSpace(remote.Host))
            {
                continue;
            }

            try
            {
                var result = await _remoteClient.ReadTelemetryAsync(remote, cancellationToken);
                if (result.Success && result.Telemetry is not null)
                {
                    if (string.IsNullOrWhiteSpace(remote.TrustedCertificateThumbprint)
                        && !string.IsNullOrWhiteSpace(result.CertificateThumbprint))
                    {
                        remote.TrustedCertificateThumbprint = result.CertificateThumbprint;
                        SettingsStore.Save(_settings);
                        RefreshRemoteList();
                    }

                    _hostSnapshots[remote.Id] = new HostSnapshot
                    {
                        Id = remote.Id,
                        DisplayName = remote.DisplayName,
                        IsLocal = false,
                        Endpoint = remote.BaseUrl,
                        Telemetry = result.Telemetry,
                        Status = "Online",
                        LastSeen = result.Telemetry.CapturedAt,
                        TrustedCertificateThumbprint = remote.TrustedCertificateThumbprint
                    };
                }
                else
                {
                    MarkRemoteOffline(remote, result.ErrorMessage ?? "Unavailable");
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException or AuthenticationException)
            {
                MarkRemoteOffline(remote, ex.Message);
            }
        }
    }

    private void MarkRemoteOffline(RemoteHostConfig remote, string status)
    {
        var existing = _hostSnapshots.TryGetValue(remote.Id, out var previous)
            ? previous.Status
            : string.Empty;

        _hostSnapshots[remote.Id] = new HostSnapshot
        {
            Id = remote.Id,
            DisplayName = remote.DisplayName,
            IsLocal = false,
            Endpoint = remote.BaseUrl,
            Status = status,
            LastSeen = DateTimeOffset.Now,
            TrustedCertificateThumbprint = remote.TrustedCertificateThumbprint
        };

        if (!string.Equals(existing, status, StringComparison.Ordinal))
        {
            _hostListDirty = true;
        }
    }

    private void UpdateHostCards()
    {
        var orderedSnapshots = _hostSnapshots.Values
            .OrderByDescending(host => host.IsLocal)
            .ThenBy(host => host.DisplayName)
            .ToArray();
        var desiredIds = orderedSnapshots.Select(snapshot => snapshot.Id).ToHashSet();
        var removedIds = _hostCards.Keys.Where(id => !desiredIds.Contains(id)).ToArray();
        var orderChanged = _hostCardsPanel.Controls.Count != orderedSnapshots.Length;

        for (var index = 0; index < orderedSnapshots.Length && !orderChanged; index++)
        {
            orderChanged = _hostCardsPanel.Controls[index] is not HostCard card
                || card.Snapshot?.Id != orderedSnapshots[index].Id;
        }

        _hostCardsPanel.SuspendLayout();

        foreach (var id in removedIds)
        {
            if (_hostCards.Remove(id, out var card))
            {
                _hostCardsPanel.Controls.Remove(card);
                card.Dispose();
            }
        }

        if (orderChanged || _hostListDirty)
        {
            _hostCardsPanel.Controls.Clear();
        }

        foreach (var snapshot in orderedSnapshots)
        {
            if (!_hostCards.TryGetValue(snapshot.Id, out var card) || card.IsDisposed)
            {
                card = new HostCard();
                var hostId = snapshot.Id;
                card.Click += (_, _) =>
                {
                    _selectedHostId = hostId;
                    UpdateHostCards();
                    RefreshSelectedHostView(forceProcessRefresh: true);
                };
                _hostCards[snapshot.Id] = card;
            }

            card.Snapshot = snapshot;
            card.IsSelected = snapshot.Id == _selectedHostId;
            card.Height = Math.Max(208, Font.Height * 12);
            card.Width = Math.Max(300, _hostCardsPanel.ClientSize.Width - 32);
            card.Invalidate();

            if (orderChanged || _hostListDirty)
            {
                _hostCardsPanel.Controls.Add(card);
            }
        }

        _hostListDirty = false;
        _hostCardsPanel.ResumeLayout();
    }

    private void RefreshSelectedHostView(bool forceProcessRefresh = false)
    {
        if (_selectedHostId is null || !_hostSnapshots.TryGetValue(_selectedHostId.Value, out var selected))
        {
            selected = _hostSnapshots.Values.FirstOrDefault();
            _selectedHostId = selected?.Id;
        }

        if (selected is null)
        {
            UpdateMetricCards(null);
            UpdateProcessRows(Array.Empty<GpuProcessInfo>(), forceProcessRefresh: true);
            UpdateActionButtons();
            return;
        }

        UpdateMetricCards(selected);
        UpdateProcessRows(selected.TopGpuProcesses, forceProcessRefresh || _lastRenderedProcessHostId != selected.Id);
        _lastRenderedProcessHostId = selected.Id;
        UpdateActionButtons();
    }

    private void UpdateProcessRows(IReadOnlyList<GpuProcessInfo> rows, bool forceProcessRefresh)
    {
        var signature = string.Join("|", rows.Select(row =>
            $"{row.ProcessId}:{row.ProcessName}:{row.LocalVramBytes}:{row.SharedBytes}:{row.RestartBehavior}:{row.ServiceName}:{row.ServiceState}:{row.ServiceStartMode}:{row.ServiceCount}:{row.ParentProcessId}:{row.ParentProcessName}:{row.WindowTitle}:{row.Notes}:{row.CanKill}"));

        if (!forceProcessRefresh && string.Equals(signature, _lastRenderedProcessSignature, StringComparison.Ordinal))
        {
            return;
        }

        var selectedPid = _processGrid.SelectedRows
            .Cast<DataGridViewRow>()
            .Select(row => row.DataBoundItem)
            .OfType<GpuProcessInfo>()
            .Select(row => row.ProcessId)
            .FirstOrDefault();

        _processGrid.SuspendLayout();
        _processBinding.DataSource = rows.ToList();
        _lastRenderedProcessSignature = signature;

        _processGrid.ClearSelection();
        _processGrid.CurrentCell = null;

        if (selectedPid != 0)
        {
            foreach (DataGridViewRow gridRow in _processGrid.Rows)
            {
                if (gridRow.DataBoundItem is GpuProcessInfo process && process.ProcessId == selectedPid)
                {
                    _processGrid.CurrentCell = gridRow.Cells[0];
                    gridRow.Selected = true;
                    break;
                }
            }
        }

        _processGrid.ResumeLayout();
    }

    private void UpdateMetricCards(HostSnapshot? host)
    {
        if (host?.Telemetry is null)
        {
            SetMetric(_cpuCard, "CPU", "Waiting", string.Empty, 0);
            SetMetric(_ramCard, "RAM", "Waiting", string.Empty, 0);
            SetMetric(_gpuCard, "GPU", "Waiting", string.Empty, 0);
            SetMetric(_vramCard, "VRAM", "Waiting", string.Empty, 0);
            return;
        }

        SetMetric(_cpuCard, "CPU", Formatters.Percent(host.CpuPercent), host.DisplayName, host.CpuPercent / 100);
        SetMetric(_ramCard, "RAM", Formatters.Bytes(host.RamUsedBytes), $"{Formatters.Bytes(host.RamTotalBytes)} total", Formatters.Ratio(host.RamUsedBytes, host.RamTotalBytes));
        SetMetric(_gpuCard, "GPU", Formatters.Percent(host.GpuPercent), "Engine utilization", host.GpuPercent / 100);
        SetMetric(_vramCard, "VRAM", Formatters.Bytes(host.VramUsedBytes), $"{Formatters.Bytes(host.VramTotalBytes)} detected", Formatters.Ratio(host.VramUsedBytes, host.VramTotalBytes));
    }

    private static void SetMetric(MetricCard card, string title, string value, string detail, double ratio)
    {
        if (card.Title == title
            && card.ValueText == value
            && card.DetailText == detail
            && Math.Abs(card.Ratio - ratio) < 0.005)
        {
            return;
        }

        card.Title = title;
        card.ValueText = value;
        card.DetailText = detail;
        card.Ratio = ratio;
        card.Invalidate();
    }

    private async Task KillSelectedProcessAsync()
    {
        var row = _processGrid.SelectedRows
            .Cast<DataGridViewRow>()
            .Select(item => item.DataBoundItem)
            .OfType<GpuProcessInfo>()
            .FirstOrDefault();

        if (row is null || _selectedHostId is null || !_hostSnapshots.TryGetValue(_selectedHostId.Value, out var host))
        {
            return;
        }

        if (!row.CanKill)
        {
            MessageBox.Show(this, "That process is protected or not killable.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (!ConfirmTaskKill(
            $"Terminate {row.ProcessName} ({row.ProcessId}) on {host.DisplayName}?{Environment.NewLine}{Environment.NewLine}VRAM reported: {Formatters.Bytes(row.LocalVramBytes)}",
            "Kill GPU task?"))
        {
            return;
        }

        var result = await RunHostActionAsync(
            host,
            () => _collector.KillProcess(row.ProcessId),
            config => _remoteClient.KillProcessAsync(config, row.ProcessId, CancellationToken.None));

        MessageBox.Show(this, result.Message, Text, MessageBoxButtons.OK, result.Success ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        await RefreshAllHostsAsync();
    }

    private async Task KillSelectedParentProcessAsync()
    {
        var row = _processGrid.SelectedRows
            .Cast<DataGridViewRow>()
            .Select(item => item.DataBoundItem)
            .OfType<GpuProcessInfo>()
            .FirstOrDefault();

        if (row is null || _selectedHostId is null || !_hostSnapshots.TryGetValue(_selectedHostId.Value, out var host))
        {
            return;
        }

        if (row.ParentProcessId is null)
        {
            MessageBox.Show(this, "That process does not have a killable parent process.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var parentName = string.IsNullOrWhiteSpace(row.ParentProcessName)
            ? $"PID {row.ParentProcessId.Value}"
            : $"{row.ParentProcessName} ({row.ParentProcessId.Value})";

        if (!ConfirmTaskKill(
            $"Terminate parent {parentName} for {row.ProcessName} ({row.ProcessId}) on {host.DisplayName}?{Environment.NewLine}{Environment.NewLine}This may close more than the selected GPU task.",
            "End parent process?"))
        {
            return;
        }

        var result = await RunHostActionAsync(
            host,
            () => _collector.KillParentProcess(row.ProcessId),
            config => _remoteClient.KillParentProcessAsync(config, row.ProcessId, CancellationToken.None));

        MessageBox.Show(this, result.Message, Text, MessageBoxButtons.OK, result.Success ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        await RefreshAllHostsAsync();
    }

    private async Task ControlSelectedServiceAsync(ServiceControlAction action)
    {
        var row = _processGrid.SelectedRows
            .Cast<DataGridViewRow>()
            .Select(item => item.DataBoundItem)
            .OfType<GpuProcessInfo>()
            .FirstOrDefault();

        if (row is null || _selectedHostId is null || !_hostSnapshots.TryGetValue(_selectedHostId.Value, out var host))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(row.ServiceName))
        {
            MessageBox.Show(this, "That process is not currently associated with a Windows service.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (!ConfirmServiceAction(row, host, action))
        {
            return;
        }

        var serviceName = row.ServiceName;
        var result = await RunHostActionAsync(
            host,
            () => _collector.ControlService(serviceName, action),
            config => _remoteClient.ControlServiceAsync(config, serviceName, action, CancellationToken.None));

        MessageBox.Show(this, result.Message, Text, MessageBoxButtons.OK, result.Success ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        await RefreshAllHostsAsync();
    }

    private async Task<KillProcessResponse> RunHostActionAsync(
        HostSnapshot host,
        Func<KillProcessResponse> localAction,
        Func<RemoteHostConfig, Task<KillProcessResponse>> remoteAction)
    {
        if (host.IsLocal)
        {
            return localAction();
        }

        var config = _settings.RemoteHosts.FirstOrDefault(item => item.Id == host.Id);
        return config is null
            ? new KillProcessResponse(false, "Remote host configuration is missing.")
            : await remoteAction(config);
    }

    private bool ConfirmTaskKill(string message, string caption)
    {
        if (!_settings.ConfirmTaskKills)
        {
            return true;
        }

        return MessageBox.Show(
            this,
            message,
            caption,
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2) == DialogResult.Yes;
    }

    private bool ConfirmServiceAction(GpuProcessInfo row, HostSnapshot host, ServiceControlAction action)
    {
        if (action is ServiceControlAction.Start or ServiceControlAction.Enable)
        {
            return true;
        }

        var serviceLabel = string.IsNullOrWhiteSpace(row.ServiceDisplayName)
            ? row.ServiceName
            : $"{row.ServiceDisplayName} ({row.ServiceName})";
        var verb = action == ServiceControlAction.Stop ? "Stop" : "Disable";
        var extra = action == ServiceControlAction.Disable
            ? $"{Environment.NewLine}{Environment.NewLine}Disabled services will not automatically start again until re-enabled."
            : string.Empty;

        return MessageBox.Show(
            this,
            $"{verb} service {serviceLabel} on {host.DisplayName}?{extra}",
            $"{verb} Windows service?",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2) == DialogResult.Yes;
    }

    private async Task SaveListenerSettingsAsync()
    {
        ApplyUpdateIntervalFromBox();
        var username = InputRules.NormalizeBasicAuthUsername(_listenerUserBox.Text);
        var password = InputRules.NormalizePassword(_listenerPasswordBox.Text);

        if (_listenerEnabledBox.Checked && (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password)))
        {
            MessageBox.Show(this, "Set a listener username and password before enabling telemetry.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        _settings.ListenerEnabled = _listenerEnabledBox.Checked;
        _settings.ConfirmTaskKills = _confirmKillsBox.Checked;
        _settings.ListenerPort = (int)_listenerPortBox.Value;
        _settings.Username = username;
        _settings.SetPassword(password);
        _listenerUserBox.Text = username;
        _listenerPasswordBox.Text = password;
        SettingsStore.Save(_settings);
        await RestartServerAsync();
    }

    private void SaveRemoteHost()
    {
        var name = InputRules.NormalizeDisplayName(_remoteNameBox.Text);
        var host = InputRules.NormalizeHost(_remoteHostBox.Text);
        var username = InputRules.NormalizeBasicAuthUsername(_remoteUserBox.Text);
        var password = InputRules.NormalizePassword(_remotePasswordBox.Text);
        var thumbprint = InputRules.NormalizeThumbprint(_remoteThumbprintBox.Text);

        if (!InputRules.IsValidHost(host))
        {
            MessageBox.Show(this, "Enter a valid host name or IP address.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
        {
            MessageBox.Show(this, "Enter the remote username and password.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (!InputRules.IsValidThumbprint(thumbprint))
        {
            MessageBox.Show(this, "The pinned certificate must be a 64-character SHA-256 hex thumbprint, or blank.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var selected = _remoteListBox.SelectedItem as RemoteHostConfig;
        var remote = selected ?? new RemoteHostConfig();
        remote.Name = name;
        remote.Host = host;
        remote.Port = (int)_remotePortBox.Value;
        remote.Username = username;
        remote.SetPassword(password);
        remote.TrustedCertificateThumbprint = thumbprint;

        if (selected is null)
        {
            _settings.RemoteHosts.Add(remote);
        }

        SettingsStore.Save(_settings);
        _remoteNameBox.Text = name;
        _remoteHostBox.Text = host;
        _remoteUserBox.Text = username;
        _remotePasswordBox.Text = password;
        _remoteThumbprintBox.Text = thumbprint;
        RefreshRemoteList();
    }

    private void RemoveSelectedRemoteHost()
    {
        if (_remoteListBox.SelectedItem is not RemoteHostConfig remote)
        {
            return;
        }

        _settings.RemoteHosts.RemoveAll(item => item.Id == remote.Id);
        _hostSnapshots.Remove(remote.Id);
        SettingsStore.Save(_settings);
        RefreshRemoteList();
        _hostListDirty = true;
        UpdateHostCards();
        RefreshSelectedHostView(forceProcessRefresh: true);
    }

    private void RefreshRemoteList()
    {
        var selectedId = (_remoteListBox.SelectedItem as RemoteHostConfig)?.Id;
        _remoteListBox.DisplayMember = nameof(RemoteHostConfig.DisplayName);
        _remoteListBox.DataSource = null;
        _remoteListBox.DataSource = _settings.RemoteHosts.ToList();

        if (selectedId is not null)
        {
            _remoteListBox.SelectedItem = _settings.RemoteHosts.FirstOrDefault(item => item.Id == selectedId.Value);
        }
    }

    private void PopulateRemoteEditorFromSelection()
    {
        if (_remoteListBox.SelectedItem is not RemoteHostConfig remote)
        {
            _remoteNameBox.Text = string.Empty;
            _remoteHostBox.Text = string.Empty;
            _remotePortBox.Value = 54545;
            _remoteUserBox.Text = "vram";
            _remotePasswordBox.Text = string.Empty;
            _remoteThumbprintBox.Text = string.Empty;
            return;
        }

        _remoteNameBox.Text = remote.Name;
        _remoteHostBox.Text = remote.Host;
        _remotePortBox.Value = Math.Clamp(remote.Port, 1024, 65535);
        _remoteUserBox.Text = remote.Username;
        _remotePasswordBox.Text = remote.GetPassword();
        _remoteThumbprintBox.Text = remote.TrustedCertificateThumbprint;
    }

    private void UpdateActionButtons()
    {
        var row = _processGrid.SelectedRows
            .Cast<DataGridViewRow>()
            .Select(item => item.DataBoundItem)
            .OfType<GpuProcessInfo>()
            .FirstOrDefault();

        _killButton.Enabled = row?.CanKill == true;
        _killParentButton.Enabled = row?.ParentProcessId is not null;

        var hasService = !string.IsNullOrWhiteSpace(row?.ServiceName);
        var serviceStopped = string.Equals(row?.ServiceState, "Stopped", StringComparison.OrdinalIgnoreCase);
        var serviceDisabled = string.Equals(row?.ServiceStartMode, "Disabled", StringComparison.OrdinalIgnoreCase);

        _stopServiceButton.Enabled = hasService && !serviceStopped;
        _startServiceButton.Enabled = hasService && serviceStopped && !serviceDisabled;
        _disableServiceButton.Enabled = hasService && !serviceDisabled;
        _enableServiceButton.Enabled = hasService && serviceDisabled;
    }

    private void UpdateListenerStatus()
    {
        var thumbprint = string.IsNullOrWhiteSpace(_server.CertificateThumbprint)
            ? string.Empty
            : $" | Cert {ShortThumbprint(_server.CertificateThumbprint)}";
        SetListenerStatusText($"{_server.Status}{thumbprint}");
        SetTrayText(_hostSnapshots.TryGetValue(_localHostId, out var local)
            ? $"{AppDisplayName} - VRAM {Formatters.Bytes(local.VramUsedBytes)}"
            : AppDisplayName);
    }

    private static string ShortThumbprint(string thumbprint) =>
        thumbprint.Length <= 16 ? thumbprint : $"{thumbprint[..8]}...{thumbprint[^8..]}";

    private void ShowDashboardPage()
    {
        _dashboardPage.BringToFront();
        _dashboardPage.Visible = true;
        _settingsPage.Visible = false;
        _dashboardButton.FillColor = Color.FromArgb(37, 74, 119);
        _settingsButton.FillColor = AppTheme.SurfaceRaised;
        _dashboardButton.Invalidate();
        _settingsButton.Invalidate();
    }

    private void ShowSettingsPage()
    {
        _settingsPage.BringToFront();
        _settingsPage.Visible = true;
        _dashboardPage.Visible = false;
        _settingsButton.FillColor = Color.FromArgb(37, 74, 119);
        _dashboardButton.FillColor = AppTheme.SurfaceRaised;
        _dashboardButton.Invalidate();
        _settingsButton.Invalidate();
    }

    private void HideToTray(bool showTip)
    {
        Hide();
        ShowInTaskbar = false;

        if (showTip && !_hasShownTrayTip)
        {
            _notifyIcon.ShowBalloonTip(1800, $"{AppDisplayName} is still running", "Double-click the tray icon to reopen it.", ToolTipIcon.Info);
            _hasShownTrayTip = true;
        }
    }

    private void ShowFromTray()
    {
        ShowInTaskbar = true;
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    private void SetTrayText(string text)
    {
        _notifyIcon.Text = text.Length <= 63 ? text : text[..63];
    }

    private void SetStatusText(string text)
    {
        if (string.Equals(_lastStatusText, text, StringComparison.Ordinal))
        {
            return;
        }

        _lastStatusText = text;
        _statusLabel.Text = text;
    }

    private void SetListenerStatusText(string text)
    {
        if (string.Equals(_lastListenerStatusText, text, StringComparison.Ordinal))
        {
            return;
        }

        _lastListenerStatusText = text;
        _listenerStatusLabel.Text = text;
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_exitRequested)
        {
            _notifyIcon.Visible = false;
            return;
        }

        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            HideToTray(showTip: true);
        }
    }
}
