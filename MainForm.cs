using System.ComponentModel;
using System.Diagnostics;
using System.Security.Authentication;

namespace VramOp;

internal sealed class MainForm : Form
{
    private readonly AppSettings _settings;
    private readonly SystemTelemetryCollector _collector = new();
    private readonly TelemetryServer _server;
    private readonly RemoteTelemetryClient _remoteClient = new();
    private readonly Dictionary<Guid, HostSnapshot> _hostSnapshots = [];
    private readonly BindingSource _processBinding = new();
    private readonly System.Windows.Forms.Timer _pollTimer = new();
    private readonly NotifyIcon _notifyIcon = new();
    private readonly Icon _appIcon;

    private readonly Panel _dashboardPage = new();
    private readonly Panel _settingsPage = new();
    private readonly BufferedFlowLayoutPanel _hostCardsPanel = new();
    private readonly DataGridView _processGrid = new();
    private readonly MetricCard _cpuCard = new() { Title = "CPU", AccentColor = AppTheme.Accent };
    private readonly MetricCard _ramCard = new() { Title = "RAM", AccentColor = AppTheme.Good };
    private readonly MetricCard _gpuCard = new() { Title = "GPU", AccentColor = AppTheme.Warning };
    private readonly MetricCard _vramCard = new() { Title = "VRAM", AccentColor = AppTheme.Danger };
    private readonly Label _statusLabel = new();
    private readonly Label _listenerStatusLabel = new();
    private readonly NumericUpDown _intervalBox = new();
    private readonly RoundedButton _killButton = new() { Text = "Kill selected", Width = 148 };
    private readonly RoundedButton _dashboardButton = new() { Text = "Dashboard", Width = 138 };
    private readonly RoundedButton _settingsButton = new() { Text = "Settings", Width = 118 };
    private readonly CheckBox _listenerEnabledBox = new();
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
        Text = "VRAM Op";
        Icon = _appIcon;
        BackColor = AppTheme.Background;
        ForeColor = AppTheme.Text;
        Font = new Font("Segoe UI", 9F);
        AutoScaleMode = AutoScaleMode.Font;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1120, 760);
        Size = new Size(1320, 860);

        ConfigureTrayIcon();

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = AppTheme.Background,
            Padding = new Padding(14),
            ColumnCount = 1,
            RowCount = 3
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 96));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));

        root.Controls.Add(BuildHeader(), 0, 0);
        root.Controls.Add(BuildPages(), 0, 1);
        root.Controls.Add(_statusLabel, 0, 2);

        _statusLabel.Dock = DockStyle.Fill;
        _statusLabel.ForeColor = AppTheme.MutedText;
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        _statusLabel.Text = "Starting";

        Controls.Add(root);
    }

    private Control BuildHeader()
    {
        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 2,
            BackColor = AppTheme.Background
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 27));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 21));
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));

        var title = new Label
        {
            Dock = DockStyle.Fill,
            Text = "VRAM Op",
            Font = new Font("Segoe UI", 16F, FontStyle.Bold),
            ForeColor = AppTheme.Text,
            TextAlign = ContentAlignment.MiddleLeft
        };

        _listenerStatusLabel.Dock = DockStyle.Fill;
        _listenerStatusLabel.ForeColor = AppTheme.MutedText;
        _listenerStatusLabel.TextAlign = ContentAlignment.MiddleLeft;
        _listenerStatusLabel.Text = "Listener starting";

        var intervalPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            BackColor = AppTheme.Background
        };
        var intervalLabel = new Label
        {
            Text = "ms",
            ForeColor = AppTheme.MutedText,
            AutoSize = true,
            Margin = new Padding(6, 12, 0, 0)
        };
        _intervalBox.Minimum = 250;
        _intervalBox.Maximum = 60000;
        _intervalBox.Increment = 250;
        _intervalBox.Width = 112;
        _intervalBox.Height = 34;
        _intervalBox.BackColor = AppTheme.Surface;
        _intervalBox.ForeColor = AppTheme.Text;
        _intervalBox.BorderStyle = BorderStyle.FixedSingle;
        intervalPanel.Controls.Add(intervalLabel);
        intervalPanel.Controls.Add(_intervalBox);
        intervalPanel.Controls.Add(new Label
        {
            Text = "Update every",
            ForeColor = AppTheme.MutedText,
            AutoSize = true,
            Margin = new Padding(0, 12, 8, 0)
        });

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
        header.Controls.Add(intervalPanel, 1, 0);
        header.SetRowSpan(intervalPanel, 2);
        header.Controls.Add(nav, 2, 0);
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
        dashboard.RowStyles.Add(new RowStyle(SizeType.Absolute, 150));
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
            Padding = new Padding(0, 8, 0, 0)
        };
        var refreshButton = new RoundedButton { Text = "Refresh now", Width = 132 };
        var hideButton = new RoundedButton { Text = "Hide to tray", Width = 132 };
        refreshButton.Click += async (_, _) => await RefreshAllHostsAsync();
        hideButton.Click += (_, _) => HideToTray(showTip: true);
        actions.Controls.Add(hideButton);
        actions.Controls.Add(refreshButton);
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
        _processGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _processGrid.Font = new Font("Segoe UI", 9F);
        _processGrid.ColumnHeadersDefaultCellStyle.BackColor = AppTheme.SurfaceRaised;
        _processGrid.ColumnHeadersDefaultCellStyle.ForeColor = AppTheme.Text;
        _processGrid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        _processGrid.ColumnHeadersHeight = Math.Max(38, _processGrid.ColumnHeadersDefaultCellStyle.Font.Height + 16);
        _processGrid.RowTemplate.Height = Math.Max(34, _processGrid.Font.Height + 14);
        _processGrid.DefaultCellStyle.BackColor = AppTheme.Surface;
        _processGrid.DefaultCellStyle.ForeColor = AppTheme.Text;
        _processGrid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(37, 74, 119);
        _processGrid.DefaultCellStyle.SelectionForeColor = AppTheme.Text;
        _processGrid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(25, 31, 40);

        AddProcessColumn(nameof(GpuProcessInfo.ProcessName), "Process", 1.35F, DataGridViewContentAlignment.MiddleLeft);
        AddProcessColumn(nameof(GpuProcessInfo.ProcessId), "PID", 0.55F, DataGridViewContentAlignment.MiddleRight);
        AddProcessColumn(nameof(GpuProcessInfo.LocalVramBytes), "Local VRAM", 0.9F, DataGridViewContentAlignment.MiddleRight);
        AddProcessColumn(nameof(GpuProcessInfo.SharedBytes), "Shared", 0.8F, DataGridViewContentAlignment.MiddleRight);
        AddProcessColumn(nameof(GpuProcessInfo.WindowTitle), "Window", 1.4F, DataGridViewContentAlignment.MiddleLeft);
        AddProcessColumn(nameof(GpuProcessInfo.Notes), "Notes", 1.0F, DataGridViewContentAlignment.MiddleLeft);

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

    private void AddProcessColumn(string propertyName, string header, float fillWeight, DataGridViewContentAlignment alignment)
    {
        var column = new DataGridViewTextBoxColumn
        {
            DataPropertyName = propertyName,
            HeaderText = header,
            Name = propertyName,
            FillWeight = fillWeight,
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

        AddLabeledControl(layout, "Port", _listenerPortBox, 2);
        AddLabeledControl(layout, "Username", _listenerUserBox, 3);
        AddLabeledControl(layout, "Password", _listenerPasswordBox, 4);

        _listenerPortBox.Minimum = 1024;
        _listenerPortBox.Maximum = 65535;
        _listenerPortBox.BackColor = AppTheme.SurfaceRaised;
        _listenerPortBox.ForeColor = AppTheme.Text;
        _listenerUserBox.BackColor = AppTheme.SurfaceRaised;
        _listenerUserBox.ForeColor = AppTheme.Text;
        _listenerPasswordBox.BackColor = AppTheme.SurfaceRaised;
        _listenerPasswordBox.ForeColor = AppTheme.Text;
        _listenerPasswordBox.UseSystemPasswordChar = true;

        var saveButton = new RoundedButton { Text = "Save and restart listener", Width = 190 };
        saveButton.Click += async (_, _) => await SaveListenerSettingsAsync();
        layout.Controls.Add(saveButton, 1, 5);

        var note = CreateNoteLabel("Each host uses a local self-signed certificate and requires TLS 1.3. Remote clients pin the certificate hash after the first successful connection.");
        layout.Controls.Add(note, 0, 6);
        layout.SetColumnSpan(note, 2);

        panel.Controls.Add(layout);
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
        _intervalBox.ValueChanged += (_, _) =>
        {
            _settings.UpdateIntervalMs = (int)_intervalBox.Value;
            _pollTimer.Interval = _settings.UpdateIntervalMs;
            SettingsStore.Save(_settings);
        };
        _killButton.Click += async (_, _) => await KillSelectedProcessAsync();
        _dashboardButton.Click += (_, _) => ShowDashboardPage();
        _settingsButton.Click += (_, _) => ShowSettingsPage();
        _remoteListBox.SelectedIndexChanged += (_, _) => PopulateRemoteEditorFromSelection();
        _processGrid.SelectionChanged += (_, _) => UpdateKillButton();
    }

    private void LoadSettingsIntoControls()
    {
        _settings.UpdateIntervalMs = Math.Clamp(_settings.UpdateIntervalMs, 250, 60000);
        _intervalBox.Value = _settings.UpdateIntervalMs;
        _pollTimer.Interval = _settings.UpdateIntervalMs;

        _listenerEnabledBox.Checked = _settings.ListenerEnabled;
        _listenerPortBox.Value = Math.Clamp(_settings.ListenerPort, 1024, 65535);
        _listenerUserBox.Text = _settings.Username;
        _listenerPasswordBox.Text = _settings.GetPassword();
        RefreshRemoteList();
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
        _notifyIcon.Text = "VRAM Op";
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
            _listenerStatusLabel.Text = $"Listener error: {ex.Message}";
            _statusLabel.Text = $"Listener error: {ex.Message}";
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
            RebuildHostCards();
            RefreshSelectedHostView();
            UpdateListenerStatus();
            _statusLabel.Text = $"Last update {DateTimeOffset.Now:HH:mm:ss}";
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
    }

    private void RebuildHostCards()
    {
        _hostCardsPanel.SuspendLayout();
        _hostCardsPanel.Controls.Clear();

        foreach (var snapshot in _hostSnapshots.Values.OrderByDescending(host => host.IsLocal).ThenBy(host => host.DisplayName))
        {
            var card = new HostCard
            {
                Snapshot = snapshot,
                IsSelected = snapshot.Id == _selectedHostId,
                Height = Math.Max(208, Font.Height * 12),
                Width = Math.Max(300, _hostCardsPanel.ClientSize.Width - 32)
            };
            card.Click += (_, _) =>
            {
                _selectedHostId = snapshot.Id;
                RebuildHostCards();
                RefreshSelectedHostView();
            };
            _hostCardsPanel.Controls.Add(card);
        }

        _hostCardsPanel.ResumeLayout();
    }

    private void RefreshSelectedHostView()
    {
        if (_selectedHostId is null || !_hostSnapshots.TryGetValue(_selectedHostId.Value, out var selected))
        {
            selected = _hostSnapshots.Values.FirstOrDefault();
            _selectedHostId = selected?.Id;
        }

        if (selected is null)
        {
            UpdateMetricCards(null);
            _processBinding.DataSource = Array.Empty<GpuProcessInfo>();
            UpdateKillButton();
            return;
        }

        UpdateMetricCards(selected);
        _processBinding.DataSource = selected.TopGpuProcesses.ToList();
        UpdateKillButton();
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

        var confirmation = MessageBox.Show(
            this,
            $"Terminate {row.ProcessName} ({row.ProcessId}) on {host.DisplayName}?{Environment.NewLine}{Environment.NewLine}Local VRAM reported: {Formatters.Bytes(row.LocalVramBytes)}",
            "Kill GPU task?",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);

        if (confirmation != DialogResult.Yes)
        {
            return;
        }

        KillProcessResponse result;
        if (host.IsLocal)
        {
            result = _collector.KillProcess(row.ProcessId);
        }
        else
        {
            var config = _settings.RemoteHosts.FirstOrDefault(item => item.Id == host.Id);
            if (config is null)
            {
                result = new KillProcessResponse(false, "Remote host configuration is missing.");
            }
            else
            {
                result = await _remoteClient.KillProcessAsync(config, row.ProcessId, CancellationToken.None);
            }
        }

        MessageBox.Show(this, result.Message, Text, MessageBoxButtons.OK, result.Success ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        await RefreshAllHostsAsync();
    }

    private async Task SaveListenerSettingsAsync()
    {
        _settings.ListenerEnabled = _listenerEnabledBox.Checked;
        _settings.ListenerPort = (int)_listenerPortBox.Value;
        _settings.Username = _listenerUserBox.Text.Trim();
        _settings.SetPassword(_listenerPasswordBox.Text);
        SettingsStore.Save(_settings);
        await RestartServerAsync();
    }

    private void SaveRemoteHost()
    {
        if (string.IsNullOrWhiteSpace(_remoteHostBox.Text))
        {
            MessageBox.Show(this, "Enter a host name or IP address.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var selected = _remoteListBox.SelectedItem as RemoteHostConfig;
        var remote = selected ?? new RemoteHostConfig();
        remote.Name = _remoteNameBox.Text.Trim();
        remote.Host = _remoteHostBox.Text.Trim();
        remote.Port = (int)_remotePortBox.Value;
        remote.Username = _remoteUserBox.Text.Trim();
        remote.SetPassword(_remotePasswordBox.Text);
        remote.TrustedCertificateThumbprint = _remoteThumbprintBox.Text.Trim();

        if (selected is null)
        {
            _settings.RemoteHosts.Add(remote);
        }

        SettingsStore.Save(_settings);
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
        RebuildHostCards();
        RefreshSelectedHostView();
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

    private void UpdateKillButton()
    {
        var row = _processGrid.SelectedRows
            .Cast<DataGridViewRow>()
            .Select(item => item.DataBoundItem)
            .OfType<GpuProcessInfo>()
            .FirstOrDefault();
        _killButton.Enabled = row?.CanKill == true;
    }

    private void UpdateListenerStatus()
    {
        var thumbprint = string.IsNullOrWhiteSpace(_server.CertificateThumbprint)
            ? string.Empty
            : $" | Cert {ShortThumbprint(_server.CertificateThumbprint)}";
        _listenerStatusLabel.Text = $"{_server.Status}{thumbprint}";
        SetTrayText(_hostSnapshots.TryGetValue(_localHostId, out var local)
            ? $"VRAM Op - VRAM {Formatters.Bytes(local.VramUsedBytes)}"
            : "VRAM Op");
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
            _notifyIcon.ShowBalloonTip(1800, "VRAM Op is still running", "Double-click the tray icon to reopen it.", ToolTipIcon.Info);
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
