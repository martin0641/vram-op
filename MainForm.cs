using System.ComponentModel;
using System.Diagnostics;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text.Json;

namespace VramOp;

internal sealed class MainForm : Form
{
    private const string AppDisplayName = "VRAM Vue";
    private const int WmEnterSizeMove = 0x0231;
    private const int WmExitSizeMove = 0x0232;
    private const int MaxBarSmoothingMs = 6000;
    private const int MaxNetworkInterfaces = 4;
    private const int MaxAverageWindowMinutes = 72 * 60;

    private readonly AppSettings _settings;
    private readonly SystemTelemetryCollector _collector = new();
    private readonly TelemetryServer _server;
    private readonly RemoteTelemetryClient _remoteClient = new();
    private readonly Dictionary<Guid, HostSnapshot> _hostSnapshots = [];
    private readonly Dictionary<Guid, HostCard> _hostCards = [];
    private readonly Dictionary<Guid, HostMonitorForm> _monitorWindows = [];
    private readonly Dictionary<Guid, RollingHostMetricHistory> _hostMetricHistories = [];
    private readonly BindingSource _processBinding = new();
    private readonly NotifyIcon _notifyIcon = new();
    private readonly ToolTip _toolTip = new();
    private readonly Icon _appIcon;

    private readonly Panel _dashboardPage = new BufferedPanel();
    private readonly Panel _settingsPage = new BufferedPanel();
    private readonly BufferedFlowLayoutPanel _hostCardsPanel = new();
    private readonly DataGridView _processGrid = new BufferedDataGridView();
    private readonly BufferedTableLayoutPanel _rootLayout = new();
    private readonly BufferedTableLayoutPanel _headerLayout = new();
    private readonly TableLayoutPanel _dashboardLayout = new();
    private readonly FlowLayoutPanel _actionsPanel = new();
    private readonly SplitContainer _dashboardSplit = new();
    private readonly TableLayoutPanel _settingsContent = new();
    private readonly MetricCard _cpuCard = new() { Title = "CPU", AccentColor = AppTheme.Accent };
    private readonly MetricCard _ramCard = new() { Title = "RAM", AccentColor = AppTheme.Good };
    private readonly MetricCard _gpuCard = new() { Title = "GPU", AccentColor = AppTheme.Warning };
    private readonly MetricCard _vramCard = new() { Title = "VRAM", AccentColor = AppTheme.Danger };
    private readonly Label _titleLabel = new BufferedLabel();
    private readonly Label _statusLabel = new BufferedLabel();
    private readonly Label _listenerStatusLabel = new BufferedLabel();
    private readonly NumericUpDown _intervalBox = new();
    private readonly NumericUpDown _barSmoothingBox = new();
    private readonly RoundedButton _killButton = new() { Text = "Kill selected", Width = 132 };
    private readonly RoundedButton _killParentButton = new() { Text = "End parent", Width = 124, Visible = false };
    private readonly RoundedButton _stopServiceButton = new() { Text = "Stop svc", Width = 96, Visible = false };
    private readonly RoundedButton _startServiceButton = new() { Text = "Start svc", Width = 96, Visible = false };
    private readonly RoundedButton _disableServiceButton = new() { Text = "Disable svc", Width = 112, Visible = false };
    private readonly RoundedButton _enableServiceButton = new() { Text = "Enable svc", Width = 104, Visible = false };
    private readonly RoundedButton _dashboardButton = new() { Text = "Dashboard", Width = 160 };
    private readonly RoundedButton _settingsButton = new() { Text = "Settings", Width = 140 };
    private readonly RoundedButton _refreshButton = new() { Text = "Refresh now", Width = 132 };
    private readonly RoundedButton _hideButton = new() { Text = "Hide to tray", Width = 132 };
    private readonly CheckBox _listenerEnabledBox = new();
    private readonly CheckBox _confirmKillsBox = new();
    private readonly CheckBox _monitorTopMostBox = new();
    private readonly CheckBox _networkAutoBox = new();
    private readonly NumericUpDown _listenerPortBox = new();
    private readonly NumericUpDown _monitorOpacityBox = new();
    private readonly NumericUpDown _averageWindowBox = new();
    private readonly TextBox _listenerUserBox = new();
    private readonly TextBox _listenerPasswordBox = new();
    private readonly BufferedFlowLayoutPanel _remotePillsPanel = new();
    private readonly TextBox _remoteNameBox = new();
    private readonly TextBox _remoteHostBox = new();
    private readonly NumericUpDown _remotePortBox = new();
    private readonly TextBox _remoteUserBox = new();
    private readonly TextBox _remotePasswordBox = new();
    private readonly TextBox _remoteThumbprintBox = new();
    private readonly TextBox _settingsTransferPasswordBox = new();
    private readonly BufferedFlowLayoutPanel _themeSwatchesPanel = new();
    private readonly ComboBox _networkUnitBox = new();
    private readonly ComboBox[] _networkInterfaceBoxes = Enumerable.Range(0, MaxNetworkInterfaces)
        .Select(_ => new ComboBox())
        .ToArray();

    private Guid _localHostId = Guid.Empty;
    private Guid? _selectedHostId;
    private Guid? _selectedRemoteHostId;
    private bool _exitRequested;
    private int _refreshInProgress;
    private bool _hasShownTrayTip;
    private bool _hostListDirty = true;
    private bool _isMovingOrSizing;
    private int _openContextMenuCount;
    private bool _updatingProcessRows;
    private bool _loadingSettingsControls;
    private Guid? _lastRenderedProcessHostId;
    private string _lastRenderedProcessSignature = string.Empty;
    private string _lastStatusText = string.Empty;
    private string _lastListenerStatusText = string.Empty;
    private RefreshResults? _pendingRefreshResults;
    private CancellationTokenSource? _pollLoopCts;
    private Task? _pollLoopTask;
    private CancellationTokenSource? _refreshCts;

    public MainForm()
    {
        _settings = SettingsStore.Load();
        _settings.RemoteHosts ??= [];
        _settings.ThemeColors ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        _settings.TrackedNetworkInterfaceIds ??= [];
        _settings.AverageWindowMinutes = Math.Clamp(_settings.AverageWindowMinutes, 1, MaxAverageWindowMinutes);
        _collector.ApplySettings(_settings);
        AppTheme.Apply(_settings.ThemeColors);
        _server = new TelemetryServer(_collector);
        _appIcon = AppIconFactory.CreateIcon();

        BuildUi();
        LoadSettingsIntoControls();
        WireEvents();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        NativeWindowStyler.ApplyDarkTitleBar(this);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _refreshCts?.Cancel();
            _refreshCts?.Dispose();
            StopPolling();
            foreach (var window in _monitorWindows.Values.ToArray())
            {
                window.Close();
                window.Dispose();
            }

            _monitorWindows.Clear();
            _notifyIcon.Dispose();
            _toolTip.Dispose();
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
        ApplyThemePaletteToStaticControls();
        BackColor = AppTheme.Background;
        ForeColor = AppTheme.Text;
        Font = new Font("Segoe UI", 9F);
        AutoScaleMode = AutoScaleMode.Font;
        StartPosition = FormStartPosition.CenterScreen;
        ApplyInitialWindowBounds();

        ConfigureTrayIcon();

        _rootLayout.Dock = DockStyle.Fill;
        _rootLayout.BackColor = AppTheme.Background;
        _rootLayout.Padding = new Padding(14);
        _rootLayout.ColumnCount = 1;
        _rootLayout.RowCount = 3;
        _rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 104));
        _rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));

        _rootLayout.Controls.Add(BuildHeader(), 0, 0);
        _rootLayout.Controls.Add(BuildPages(), 0, 1);
        _rootLayout.Controls.Add(_statusLabel, 0, 2);

        _statusLabel.Dock = DockStyle.Fill;
        _statusLabel.ForeColor = AppTheme.MutedText;
        _statusLabel.TextAlign = ContentAlignment.MiddleRight;
        SetStatusText("Starting");

        Controls.Add(_rootLayout);
        ApplyResponsiveLayout();
        EnsureStartupCanShowTenProcessRows();
        ApplyResponsiveLayout();
    }

    private void ApplyInitialWindowBounds()
    {
        const int preferredWidth = 1500;
        const int preferredHeight = 1080;
        const int comfortableMinimumWidth = 1240;
        const int comfortableMinimumHeight = 900;

        var workingArea = GetStartupWorkingArea();
        var availableWidth = Math.Max(760, workingArea.Width - 16);
        var availableHeight = Math.Max(520, workingArea.Height - 16);

        MinimumSize = new Size(
            Math.Min(comfortableMinimumWidth, availableWidth),
            Math.Min(comfortableMinimumHeight, availableHeight));

        var width = Math.Max(MinimumSize.Width, Math.Min(preferredWidth, availableWidth));
        var height = Math.Max(MinimumSize.Height, Math.Min(preferredHeight, availableHeight));
        StartPosition = FormStartPosition.Manual;
        Bounds = new Rectangle(
            workingArea.Left + Math.Max(0, (workingArea.Width - width) / 2),
            workingArea.Top + Math.Max(0, (workingArea.Height - height) / 2),
            width,
            height);
    }

    private static Rectangle GetStartupWorkingArea()
    {
        var currentScreen = Screen.FromPoint(Cursor.Position);
        if (currentScreen.WorkingArea.Width >= 1180 && currentScreen.WorkingArea.Height >= 720)
        {
            return currentScreen.WorkingArea;
        }

        return Screen.AllScreens
            .OrderByDescending(screen => screen.WorkingArea.Width * screen.WorkingArea.Height)
            .FirstOrDefault()?.WorkingArea ?? currentScreen.WorkingArea;
    }

    private Control BuildHeader()
    {
        _headerLayout.Dock = DockStyle.Fill;
        _headerLayout.ColumnCount = 2;
        _headerLayout.RowCount = 2;
        _headerLayout.BackColor = AppTheme.Background;
        _headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 340));
        _headerLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        _headerLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));

        _titleLabel.Dock = DockStyle.Fill;
        _titleLabel.Text = AppDisplayName;
        _titleLabel.Font = new Font("Segoe UI", 16F, FontStyle.Bold);
        _titleLabel.ForeColor = AppTheme.Text;
        _titleLabel.TextAlign = ContentAlignment.MiddleLeft;

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

        _headerLayout.Controls.Add(_titleLabel, 0, 0);
        _headerLayout.Controls.Add(_listenerStatusLabel, 0, 1);
        _headerLayout.Controls.Add(nav, 1, 0);
        _headerLayout.SetRowSpan(nav, 2);
        return _headerLayout;
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
        _dashboardLayout.Dock = DockStyle.Fill;
        _dashboardLayout.ColumnCount = 1;
        _dashboardLayout.RowCount = 3;
        _dashboardLayout.BackColor = AppTheme.Background;
        _dashboardLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 176));
        _dashboardLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _dashboardLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));

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

        _dashboardSplit.Dock = DockStyle.Fill;
        _dashboardSplit.BackColor = AppTheme.Background;
        _dashboardSplit.SizeChanged += (_, _) =>
        {
            AdjustDashboardSplit();
            UpdateHostCards();
            UpdateProcessGridColumns();
        };

        var hostPanel = new RoundedPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(4),
            BackColor = AppTheme.Background,
            BorderColor = AppTheme.Background
        };
        _hostCardsPanel.Dock = DockStyle.Fill;
        _hostCardsPanel.FlowDirection = FlowDirection.TopDown;
        _hostCardsPanel.WrapContents = false;
        _hostCardsPanel.AutoScroll = true;
        _hostCardsPanel.AutoScrollMargin = Size.Empty;
        _hostCardsPanel.AutoScrollMinSize = Size.Empty;
        _hostCardsPanel.BackColor = AppTheme.Background;
        _hostCardsPanel.HorizontalScroll.Enabled = false;
        _hostCardsPanel.HorizontalScroll.Visible = false;
        hostPanel.Controls.Add(_hostCardsPanel);

        var processPanel = new RoundedPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            BackColor = AppTheme.Surface
        };
        ConfigureProcessGrid();
        processPanel.Controls.Add(_processGrid);

        _dashboardSplit.Panel1.Controls.Add(hostPanel);
        _dashboardSplit.Panel2.Controls.Add(processPanel);

        _actionsPanel.Dock = DockStyle.Fill;
        _actionsPanel.FlowDirection = FlowDirection.RightToLeft;
        _actionsPanel.WrapContents = true;
        _actionsPanel.BackColor = AppTheme.Background;
        _actionsPanel.AutoScroll = false;
        _actionsPanel.Padding = new Padding(0, 8, 0, 0);
        _refreshButton.Click += async (_, _) => await RefreshAllHostsAsync();
        _hideButton.Click += (_, _) => HideToTray(showTip: true);
        _actionsPanel.Controls.Add(_hideButton);
        _actionsPanel.Controls.Add(_refreshButton);
        _actionsPanel.Controls.Add(_enableServiceButton);
        _actionsPanel.Controls.Add(_disableServiceButton);
        _actionsPanel.Controls.Add(_startServiceButton);
        _actionsPanel.Controls.Add(_stopServiceButton);
        _actionsPanel.Controls.Add(_killButton);
        _actionsPanel.Controls.Add(_killParentButton);

        _dashboardLayout.Controls.Add(metrics, 0, 0);
        _dashboardLayout.Controls.Add(_dashboardSplit, 0, 1);
        _dashboardLayout.Controls.Add(_actionsPanel, 0, 2);
        _dashboardPage.Controls.Add(_dashboardLayout);
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
        AddProcessColumn(nameof(GpuProcessInfo.ProcessId), "PID", DataGridViewAutoSizeColumnMode.AllCells, 0, DataGridViewContentAlignment.MiddleCenter, isResizable: false, headerAlignment: DataGridViewContentAlignment.MiddleCenter);
        AddProcessColumn(nameof(GpuProcessInfo.LocalVramBytes), "VRAM", DataGridViewAutoSizeColumnMode.AllCells, 0, DataGridViewContentAlignment.MiddleCenter, isResizable: false, headerAlignment: DataGridViewContentAlignment.MiddleCenter);
        AddProcessColumn(nameof(GpuProcessInfo.SystemGpuMemoryBytes), "Sys RAM", DataGridViewAutoSizeColumnMode.AllCells, 0, DataGridViewContentAlignment.MiddleCenter, isResizable: false, headerAlignment: DataGridViewContentAlignment.MiddleCenter);
        AddProcessColumn(nameof(GpuProcessInfo.SpilloverStatus), "Spill", DataGridViewAutoSizeColumnMode.AllCells, 0, DataGridViewContentAlignment.MiddleLeft);
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
            if (propertyName is nameof(GpuProcessInfo.LocalVramBytes) or nameof(GpuProcessInfo.SystemGpuMemoryBytes))
            {
                e.Value = Formatters.BytesPrecise(bytes);
                e.FormattingApplied = true;
            }
        };
    }

    private void AddProcessColumn(
        string propertyName,
        string header,
        DataGridViewAutoSizeColumnMode autoSizeMode,
        float fillWeight,
        DataGridViewContentAlignment alignment,
        bool isResizable = true,
        DataGridViewContentAlignment? headerAlignment = null)
    {
        var column = new DataGridViewTextBoxColumn
        {
            DataPropertyName = propertyName,
            HeaderText = header,
            Name = propertyName,
            AutoSizeMode = autoSizeMode,
            FillWeight = fillWeight <= 0 ? 100 : fillWeight,
            MinimumWidth = autoSizeMode == DataGridViewAutoSizeColumnMode.Fill ? 80 : 48,
            Resizable = isResizable ? DataGridViewTriState.True : DataGridViewTriState.False,
            SortMode = DataGridViewColumnSortMode.Automatic
        };
        column.DefaultCellStyle.Alignment = alignment;
        column.HeaderCell.Style.Alignment = headerAlignment ?? DataGridViewContentAlignment.MiddleLeft;
        _processGrid.Columns.Add(column);
    }

    private void ApplyResponsiveLayout()
    {
        if (_rootLayout.RowStyles.Count == 0 || _dashboardLayout.RowStyles.Count == 0)
        {
            return;
        }

        var compactWidth = ClientSize.Width < 980;
        var padding = compactWidth ? 10 : 14;
        _rootLayout.Padding = new Padding(padding);
        FitButtonWidths();

        var titleRowHeight = Math.Max(56, _titleLabel.Font.Height + 16);
        var listenerRowHeight = Math.Max(34, _listenerStatusLabel.Font.Height + 10);
        var headerHeight = titleRowHeight + listenerRowHeight;
        var statusHeight = Math.Max(36, _statusLabel.Font.Height + 14);
        var actionsHeight = CalculateActionRowHeight();
        var metricsHeight = CalculateMetricsRowHeight(headerHeight, statusHeight, actionsHeight);

        _rootLayout.RowStyles[0].Height = headerHeight;
        _rootLayout.RowStyles[2].Height = statusHeight;
        _headerLayout.RowStyles[0].Height = titleRowHeight;
        _headerLayout.RowStyles[1].Height = listenerRowHeight;
        _dashboardLayout.RowStyles[0].Height = metricsHeight;
        _dashboardLayout.RowStyles[2].Height = actionsHeight;

        AdjustDashboardSplit();
        UpdateProcessGridColumns();
        UpdateHostCards();
    }

    private int CalculateMetricsRowHeight(int headerHeight, int statusHeight, int actionsHeight)
    {
        var desired = ClientSize.Height < 900 ? 168 : 190;
        var minimum = Math.Max(150, _cpuCard.MinimumSize.Height + _cpuCard.Margin.Vertical + 22);
        var processRowsHeight = RequiredProcessGridHeight();
        var availableDashboardHeight = ClientSize.Height - _rootLayout.Padding.Vertical - headerHeight - statusHeight;
        var maxMetricsHeight = availableDashboardHeight - actionsHeight - processRowsHeight;

        if (maxMetricsHeight >= minimum)
        {
            return Math.Min(desired, maxMetricsHeight);
        }

        return minimum;
    }

    private void EnsureStartupCanShowTenProcessRows()
    {
        var workingArea = Screen.FromControl(this).WorkingArea;
        var headerHeight = (int)Math.Ceiling(_rootLayout.RowStyles[0].Height);
        var statusHeight = (int)Math.Ceiling(_rootLayout.RowStyles[2].Height);
        var actionsHeight = CalculateActionRowHeight();
        var metricsHeight = Math.Max(168, _cpuCard.MinimumSize.Height + _cpuCard.Margin.Vertical + 22);
        var requiredClientHeight = _rootLayout.Padding.Vertical
            + headerHeight
            + metricsHeight
            + RequiredProcessGridHeight()
            + actionsHeight
            + statusHeight;
        var requiredClientWidth = Math.Max(1240, ClientSize.Width);
        var requiredWindowSize = SizeFromClientSize(new Size(requiredClientWidth, requiredClientHeight));
        var maxWidth = Math.Max(MinimumSize.Width, workingArea.Width - 16);
        var maxHeight = Math.Max(MinimumSize.Height, workingArea.Height - 16);
        var targetWidth = Math.Min(Math.Max(Width, requiredWindowSize.Width), maxWidth);
        var targetHeight = Math.Min(Math.Max(Height, requiredWindowSize.Height), maxHeight);

        MinimumSize = new Size(
            Math.Min(Math.Max(MinimumSize.Width, requiredWindowSize.Width), maxWidth),
            Math.Min(Math.Max(MinimumSize.Height, requiredWindowSize.Height), maxHeight));

        if (targetWidth == Width && targetHeight == Height)
        {
            return;
        }

        Bounds = new Rectangle(
            workingArea.Left + Math.Max(0, (workingArea.Width - targetWidth) / 2),
            workingArea.Top + Math.Max(0, (workingArea.Height - targetHeight) / 2),
            targetWidth,
            targetHeight);
    }

    private int RequiredProcessGridHeight() =>
        _processGrid.ColumnHeadersHeight + (_processGrid.RowTemplate.Height * 10) + 40;

    private int CalculateActionRowHeight()
    {
        var visibleControls = _actionsPanel.Controls
            .Cast<Control>()
            .Where(control => control.Visible)
            .ToArray();

        if (visibleControls.Length == 0)
        {
            return Math.Max(56, Font.Height + 32);
        }

        var availableWidth = Math.Max(240, _actionsPanel.ClientSize.Width > 0
            ? _actionsPanel.ClientSize.Width - _actionsPanel.Padding.Horizontal
            : ClientSize.Width - _rootLayout.Padding.Horizontal);
        var rowHeight = visibleControls.Max(control => control.Height + control.Margin.Vertical);
        var rows = 1;
        var currentWidth = 0;

        foreach (var control in visibleControls)
        {
            var controlWidth = control.Width + control.Margin.Horizontal;
            if (currentWidth > 0 && currentWidth + controlWidth > availableWidth)
            {
                rows++;
                currentWidth = 0;
            }

            currentWidth += controlWidth;
        }

        return Math.Max(58, _actionsPanel.Padding.Vertical + (rows * rowHeight) + ((rows - 1) * 6) + 4);
    }

    private void FitButtonWidths()
    {
        FitButton(_dashboardButton, 120);
        FitButton(_settingsButton, 112);
        FitButton(_killButton, 118);
        FitButton(_killParentButton, 116);
        FitButton(_stopServiceButton, 88);
        FitButton(_startServiceButton, 88);
        FitButton(_disableServiceButton, 104);
        FitButton(_enableServiceButton, 96);
        FitButton(_refreshButton, 118);
        FitButton(_hideButton, 112);

        if (_headerLayout.ColumnStyles.Count > 1)
        {
            var navWidth = _dashboardButton.Width + _settingsButton.Width + 32;
            _headerLayout.ColumnStyles[1].Width = Math.Min(Math.Max(260, navWidth), Math.Max(240, ClientSize.Width / 2));
        }
    }

    private static void FitButton(RoundedButton button, int minimumWidth)
    {
        var measured = TextRenderer.MeasureText(button.Text, button.Font).Width + 34;
        button.Width = Math.Max(minimumWidth, measured);
    }

    private void AdjustDashboardSplit()
    {
        if (_dashboardSplit.Width <= 0)
        {
            return;
        }

        var compact = _dashboardSplit.Width < 920;
        var hostMin = compact ? 260 : 340;
        var processMin = compact ? 320 : 500;
        _dashboardSplit.Panel1MinSize = Math.Min(hostMin, Math.Max(80, _dashboardSplit.Width - 120));
        _dashboardSplit.Panel2MinSize = Math.Min(processMin, Math.Max(160, _dashboardSplit.Width - _dashboardSplit.Panel1MinSize - _dashboardSplit.SplitterWidth));

        var maxDistance = _dashboardSplit.Width - _dashboardSplit.SplitterWidth - _dashboardSplit.Panel2MinSize;
        if (maxDistance <= _dashboardSplit.Panel1MinSize)
        {
            return;
        }

        var desired = compact
            ? Math.Min(360, Math.Max(_dashboardSplit.Panel1MinSize, (int)Math.Round(_dashboardSplit.Width * 0.4)))
            : Math.Min(500, _dashboardSplit.Width - _dashboardSplit.Panel2MinSize);
        _dashboardSplit.SplitterDistance = Math.Clamp(desired, _dashboardSplit.Panel1MinSize, maxDistance);
    }

    private void UpdateProcessGridColumns()
    {
        if (_processGrid.Columns.Count == 0)
        {
            return;
        }

        var compact = _processGrid.ClientSize.Width < 980;
        _processGrid.ScrollBars = ScrollBars.Vertical;

        ConfigureProcessColumn(nameof(GpuProcessInfo.ProcessName), compact ? DataGridViewAutoSizeColumnMode.Fill : DataGridViewAutoSizeColumnMode.AllCells, compact ? 38 : 100, compact ? 110 : 80, visible: true);
        ConfigureProcessColumn(nameof(GpuProcessInfo.ProcessId), DataGridViewAutoSizeColumnMode.AllCells, 100, 54, visible: true);
        ConfigureProcessColumn(nameof(GpuProcessInfo.LocalVramBytes), DataGridViewAutoSizeColumnMode.AllCells, 100, 86, visible: true);
        ConfigureProcessColumn(nameof(GpuProcessInfo.SystemGpuMemoryBytes), DataGridViewAutoSizeColumnMode.AllCells, 100, 86, visible: true);
        ConfigureProcessColumn(nameof(GpuProcessInfo.SpilloverStatus), DataGridViewAutoSizeColumnMode.AllCells, 100, 72, visible: true);
        ConfigureProcessColumn(nameof(GpuProcessInfo.RestartBehavior), compact ? DataGridViewAutoSizeColumnMode.Fill : DataGridViewAutoSizeColumnMode.AllCells, compact ? 36 : 100, compact ? 92 : 90, visible: true);
        ConfigureProcessColumn(nameof(GpuProcessInfo.WindowTitle), DataGridViewAutoSizeColumnMode.Fill, 65, 90, visible: !compact);
        ConfigureProcessColumn(nameof(GpuProcessInfo.Notes), DataGridViewAutoSizeColumnMode.Fill, 35, 90, visible: !compact);
    }

    private void ConfigureProcessColumn(string name, DataGridViewAutoSizeColumnMode mode, float fillWeight, int minimumWidth, bool visible)
    {
        if (!_processGrid.Columns.Contains(name))
        {
            return;
        }

        var column = _processGrid.Columns[name];
        column.Visible = visible;
        column.MinimumWidth = minimumWidth;
        column.AutoSizeMode = mode;
        column.FillWeight = fillWeight;
    }

    private void BuildSettingsPage()
    {
        var scroll = new BufferedPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = AppTheme.Background,
            Padding = new Padding(0, 6, 0, 0)
        };

        _settingsContent.Dock = DockStyle.Top;
        _settingsContent.AutoSize = true;
        _settingsContent.ColumnCount = 2;
        _settingsContent.RowCount = 3;
        _settingsContent.BackColor = AppTheme.Background;
        _settingsContent.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        _settingsContent.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        _settingsContent.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _settingsContent.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _settingsContent.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _settingsContent.Controls.Add(BuildListenerSettings(), 0, 0);
        _settingsContent.Controls.Add(BuildRemoteSettings(), 1, 0);
        _settingsContent.Controls.Add(BuildMonitorAndTransferSettings(), 0, 1);
        _settingsContent.Controls.Add(BuildNetworkSettings(), 1, 1);
        _settingsContent.Controls.Add(BuildThemeSettings(), 1, 2);
        scroll.Controls.Add(_settingsContent);
        _settingsPage.Controls.Add(scroll);
    }

    private Control BuildMonitorAndTransferSettings()
    {
        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0, 12, 12, 0),
            BackColor = AppTheme.Background
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        row.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var monitor = BuildMonitorSettings();
        var transfer = BuildSettingsTransferSettings();
        monitor.Margin = new Padding(0, 0, 6, 0);
        transfer.Margin = new Padding(6, 0, 0, 0);
        row.Controls.Add(monitor, 0, 0);
        row.Controls.Add(transfer, 1, 0);
        return row;
    }

    private Control BuildListenerSettings()
    {
        var panel = new RoundedPanel
        {
            Dock = DockStyle.Top,
            Margin = new Padding(0, 0, 12, 0),
            BackColor = AppTheme.Surface,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(0, 0)
        };

        var layout = CreateSettingsLayout();
        AddSectionTitle(layout, "Local HTTPS listener", 0);

        _listenerEnabledBox.Text = "Enable telemetry listener";
        _listenerEnabledBox.ForeColor = AppTheme.Text;
        _listenerEnabledBox.AutoSize = true;
        SetSettingsRowHeight(layout, 1, CheckBoxRowHeight(_listenerEnabledBox));
        layout.Controls.Add(_listenerEnabledBox, 0, 1);
        layout.SetColumnSpan(_listenerEnabledBox, 2);

        _confirmKillsBox.Text = "Confirm before ending tasks";
        _confirmKillsBox.ForeColor = AppTheme.Text;
        _confirmKillsBox.AutoSize = true;
        SetSettingsRowHeight(layout, 2, CheckBoxRowHeight(_confirmKillsBox));
        layout.Controls.Add(_confirmKillsBox, 0, 2);
        layout.SetColumnSpan(_confirmKillsBox, 2);

        AddLabeledControl(layout, "Update every", CreateIntervalEditor(), 3);
        AddLabeledControl(layout, "Bar smoothing", CreateBarSmoothingEditor(), 4);
        AddLabeledControl(layout, "Port", _listenerPortBox, 5);
        AddLabeledControl(layout, "Username", _listenerUserBox, 6);
        AddLabeledControl(layout, "Password", _listenerPasswordBox, 7);

        _listenerPortBox.Minimum = 1024;
        _listenerPortBox.Maximum = 65535;
        _listenerPortBox.BackColor = AppTheme.SurfaceRaised;
        _listenerPortBox.ForeColor = AppTheme.Text;
        _listenerPortBox.TextAlign = HorizontalAlignment.Left;
        _barSmoothingBox.Minimum = 0;
        _barSmoothingBox.Maximum = MaxBarSmoothingMs;
        _barSmoothingBox.Increment = 50;
        _barSmoothingBox.BackColor = AppTheme.SurfaceRaised;
        _barSmoothingBox.ForeColor = AppTheme.Text;
        _listenerUserBox.BackColor = AppTheme.SurfaceRaised;
        _listenerUserBox.ForeColor = AppTheme.Text;
        _listenerUserBox.TextAlign = HorizontalAlignment.Left;
        _listenerPasswordBox.BackColor = AppTheme.SurfaceRaised;
        _listenerPasswordBox.ForeColor = AppTheme.Text;
        _listenerPasswordBox.TextAlign = HorizontalAlignment.Left;
        _listenerPasswordBox.UseSystemPasswordChar = true;
        ConfigureTextInput(_listenerUserBox, 64, InputRules.IsBasicAuthUsernameChar, InputRules.NormalizeBasicAuthUsername);
        ConfigureTextInput(_listenerPasswordBox, 128, InputRules.IsPasswordChar, InputRules.NormalizePassword);

        var saveButton = new RoundedButton { Text = "Save and restart listener", Width = 190 };
        saveButton.Click += async (_, _) => await SaveListenerSettingsAsync();
        SetSettingsRowHeight(layout, 8, ButtonRowHeight(saveButton));
        layout.Controls.Add(saveButton, 1, 8);

        var note = CreateNoteLabel("Each host uses a local self-signed certificate and requires TLS 1.3. Remote clients pin the certificate hash after the first successful connection.");
        SetSettingsRowAutoSize(layout, 9);
        layout.Controls.Add(note, 0, 9);
        layout.SetColumnSpan(note, 2);

        panel.Controls.Add(layout);
        return panel;
    }

    private Control BuildMonitorSettings()
    {
        var panel = new RoundedPanel
        {
            Dock = DockStyle.Top,
            Margin = new Padding(0, 12, 12, 0),
            BackColor = AppTheme.Surface,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(0, 0)
        };

        var layout = CreateSettingsLayout();
        AddSectionTitle(layout, "Monitor popouts", 0);

        _monitorTopMostBox.Text = "Keep popout monitors on top";
        _monitorTopMostBox.ForeColor = AppTheme.Text;
        _monitorTopMostBox.AutoSize = true;
        SetSettingsRowHeight(layout, 1, CheckBoxRowHeight(_monitorTopMostBox));
        layout.Controls.Add(_monitorTopMostBox, 0, 1);
        layout.SetColumnSpan(_monitorTopMostBox, 2);

        AddLabeledControl(layout, "Opacity", CreateMonitorOpacityEditor(), 2);
        _monitorOpacityBox.Minimum = 30;
        _monitorOpacityBox.Maximum = 100;
        _monitorOpacityBox.Increment = 5;
        _monitorOpacityBox.BackColor = AppTheme.SurfaceRaised;
        _monitorOpacityBox.ForeColor = AppTheme.Text;

        panel.Controls.Add(layout);
        return panel;
    }

    private Control BuildSettingsTransferSettings()
    {
        var panel = new RoundedPanel
        {
            Dock = DockStyle.Top,
            BackColor = AppTheme.Surface,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(0, 0)
        };

        var layout = CreateSettingsLayout();
        AddSectionTitle(layout, "Settings transfer", 0);

        _settingsTransferPasswordBox.BackColor = AppTheme.SurfaceRaised;
        _settingsTransferPasswordBox.ForeColor = AppTheme.Text;
        _settingsTransferPasswordBox.BorderStyle = BorderStyle.FixedSingle;
        _settingsTransferPasswordBox.PlaceholderText = " Required password";
        _settingsTransferPasswordBox.UseSystemPasswordChar = true;
        _settingsTransferPasswordBox.TextAlign = HorizontalAlignment.Left;
        ConfigureTextInput(_settingsTransferPasswordBox, 128, InputRules.IsPasswordChar, InputRules.NormalizePassword);
        AddLabeledControl(layout, "Password", _settingsTransferPasswordBox, 1);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            BackColor = AppTheme.Surface
        };
        var importButton = new RoundedButton { Text = "Import", Width = 88 };
        var exportButton = new RoundedButton { Text = "Export", Width = 88 };
        importButton.Click += async (_, _) => await ImportSettingsAsync();
        exportButton.Click += (_, _) => ExportSettings();
        buttons.Controls.Add(importButton);
        buttons.Controls.Add(exportButton);
        SetSettingsRowHeight(layout, 2, ButtonRowHeight(exportButton));
        layout.Controls.Add(buttons, 0, 2);
        layout.SetColumnSpan(buttons, 2);

        var note = CreateNoteLabel("Exports use AES-256-GCM with a password so host credentials can move safely between PCs.");
        SetSettingsRowAutoSize(layout, 3);
        layout.Controls.Add(note, 0, 3);
        layout.SetColumnSpan(note, 2);

        panel.Controls.Add(layout);
        return panel;
    }

    private Control BuildThemeSettings()
    {
        var panel = new RoundedPanel
        {
            Dock = DockStyle.Top,
            Margin = new Padding(12, 12, 0, 0),
            BackColor = AppTheme.Surface,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(0, 0)
        };

        var layout = CreateSettingsLayout();
        AddSectionTitle(layout, "Theme colors", 0);

        _themeSwatchesPanel.Dock = DockStyle.Top;
        _themeSwatchesPanel.AutoSize = true;
        _themeSwatchesPanel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _themeSwatchesPanel.FlowDirection = FlowDirection.LeftToRight;
        _themeSwatchesPanel.WrapContents = true;
        _themeSwatchesPanel.AutoScroll = false;
        _themeSwatchesPanel.MinimumSize = new Size(0, 48);
        _themeSwatchesPanel.Padding = new Padding(0, 4, 0, 0);
        _themeSwatchesPanel.BackColor = AppTheme.Surface;

        SetSettingsRowAutoSize(layout, 1);
        layout.Controls.Add(_themeSwatchesPanel, 0, 1);
        layout.SetColumnSpan(_themeSwatchesPanel, 2);

        panel.Controls.Add(layout);
        RefreshThemeSwatches();
        return panel;
    }

    private Control BuildNetworkSettings()
    {
        var panel = new RoundedPanel
        {
            Dock = DockStyle.Top,
            Margin = new Padding(12, 12, 0, 0),
            BackColor = AppTheme.Surface,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(0, 0)
        };

        var layout = CreateSettingsLayout();
        AddSectionTitle(layout, "Network activity", 0);

        _networkAutoBox.Text = "Auto detect active NICs";
        _networkAutoBox.ForeColor = AppTheme.Text;
        _networkAutoBox.AutoSize = true;
        SetSettingsRowHeight(layout, 1, CheckBoxRowHeight(_networkAutoBox));
        layout.Controls.Add(_networkAutoBox, 0, 1);
        layout.SetColumnSpan(_networkAutoBox, 2);

        ConfigureSettingsComboBox(_networkUnitBox);
        _networkUnitBox.Items.Clear();
        foreach (var unit in Enum.GetValues<NetworkRateUnit>())
        {
            _networkUnitBox.Items.Add(new NetworkRateUnitItem(unit));
        }

        AddLabeledControl(layout, "Rate units", _networkUnitBox, 2);

        _averageWindowBox.Minimum = 1;
        _averageWindowBox.Maximum = MaxAverageWindowMinutes;
        _averageWindowBox.Increment = 5;
        _averageWindowBox.BackColor = AppTheme.SurfaceRaised;
        _averageWindowBox.ForeColor = AppTheme.Text;
        _averageWindowBox.BorderStyle = BorderStyle.FixedSingle;
        _averageWindowBox.TextAlign = HorizontalAlignment.Left;
        AddLabeledControl(layout, "Average window", CreateAverageWindowEditor(), 3);

        for (var index = 0; index < _networkInterfaceBoxes.Length; index++)
        {
            var comboBox = _networkInterfaceBoxes[index];
            ConfigureSettingsComboBox(comboBox);
            AddLabeledControl(layout, $"NIC{index + 1}", comboBox, 4 + index);
        }

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            BackColor = AppTheme.Surface
        };
        var refreshButton = new RoundedButton { Text = "Refresh NICs", Width = 122 };
        refreshButton.Click += (_, _) => PopulateNetworkInterfaceSelectors();
        buttons.Controls.Add(refreshButton);
        SetSettingsRowHeight(layout, 8, ButtonRowHeight(refreshButton));
        layout.Controls.Add(buttons, 0, 8);
        layout.SetColumnSpan(buttons, 2);

        panel.Controls.Add(layout);
        PopulateNetworkInterfaceSelectors();
        return panel;
    }

    private Control CreateIntervalEditor()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.None,
            ColumnCount = 2,
            RowCount = 1,
            Height = SettingsInputHeight(_intervalBox),
            BackColor = AppTheme.Surface
        };
        var digitWidth = Math.Max(76, TextRenderer.MeasureText("9999", _intervalBox.Font).Width + 34);
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, digitWidth));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Math.Max(42, TextRenderer.MeasureText("ms", Font).Width + 20)));

        _intervalBox.Minimum = 250;
        _intervalBox.Maximum = 9999;
        _intervalBox.Increment = 50;
        _intervalBox.DecimalPlaces = 0;
        _intervalBox.ThousandsSeparator = false;
        _intervalBox.BackColor = AppTheme.SurfaceRaised;
        _intervalBox.ForeColor = AppTheme.Text;
        _intervalBox.BorderStyle = BorderStyle.FixedSingle;
        _intervalBox.TextAlign = HorizontalAlignment.Left;
        _intervalBox.Width = digitWidth - 10;
        _intervalBox.Dock = DockStyle.Left;
        _intervalBox.Margin = new Padding(0, 2, 0, 2);

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
        panel.Width = digitWidth + (int)panel.ColumnStyles[1].Width;
        return panel;
    }

    private Control CreateBarSmoothingEditor()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.None,
            ColumnCount = 2,
            RowCount = 1,
            Height = SettingsInputHeight(_barSmoothingBox),
            BackColor = AppTheme.Surface
        };
        var digitWidth = Math.Max(76, TextRenderer.MeasureText("6000", _barSmoothingBox.Font).Width + 34);
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, digitWidth));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Math.Max(42, TextRenderer.MeasureText("ms", Font).Width + 20)));

        _barSmoothingBox.BorderStyle = BorderStyle.FixedSingle;
        _barSmoothingBox.TextAlign = HorizontalAlignment.Left;
        _barSmoothingBox.Width = digitWidth - 10;
        _barSmoothingBox.Dock = DockStyle.Left;
        _barSmoothingBox.Margin = new Padding(0, 2, 0, 2);

        var unitLabel = new Label
        {
            Text = "ms",
            Dock = DockStyle.Fill,
            ForeColor = AppTheme.MutedText,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(8, 0, 0, 0)
        };

        panel.Controls.Add(_barSmoothingBox, 0, 0);
        panel.Controls.Add(unitLabel, 1, 0);
        panel.Width = digitWidth + (int)panel.ColumnStyles[1].Width;
        return panel;
    }

    private Control CreateMonitorOpacityEditor()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.None,
            ColumnCount = 2,
            RowCount = 1,
            Height = SettingsInputHeight(_monitorOpacityBox),
            BackColor = AppTheme.Surface
        };
        var digitWidth = Math.Max(72, TextRenderer.MeasureText("100", _monitorOpacityBox.Font).Width + 34);
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, digitWidth));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Math.Max(40, TextRenderer.MeasureText("%", Font).Width + 20)));

        _monitorOpacityBox.BorderStyle = BorderStyle.FixedSingle;
        _monitorOpacityBox.TextAlign = HorizontalAlignment.Left;
        _monitorOpacityBox.Width = digitWidth - 10;
        _monitorOpacityBox.Dock = DockStyle.Left;
        _monitorOpacityBox.Margin = new Padding(0, 2, 0, 2);

        var unitLabel = new Label
        {
            Text = "%",
            Dock = DockStyle.Fill,
            ForeColor = AppTheme.MutedText,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(8, 0, 0, 0)
        };

        panel.Controls.Add(_monitorOpacityBox, 0, 0);
        panel.Controls.Add(unitLabel, 1, 0);
        panel.Width = digitWidth + (int)panel.ColumnStyles[1].Width;
        return panel;
    }

    private Control CreateAverageWindowEditor()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.None,
            ColumnCount = 2,
            RowCount = 1,
            Height = SettingsInputHeight(_averageWindowBox),
            BackColor = AppTheme.Surface
        };
        var digitWidth = Math.Max(88, TextRenderer.MeasureText("4320", _averageWindowBox.Font).Width + 36);
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, digitWidth));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Math.Max(48, TextRenderer.MeasureText("min", Font).Width + 22)));

        _averageWindowBox.Width = digitWidth - 10;
        _averageWindowBox.Dock = DockStyle.Left;
        _averageWindowBox.Margin = new Padding(0, 2, 0, 2);

        var unitLabel = new Label
        {
            Text = "min",
            Dock = DockStyle.Fill,
            ForeColor = AppTheme.MutedText,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(8, 0, 0, 0)
        };

        panel.Controls.Add(_averageWindowBox, 0, 0);
        panel.Controls.Add(unitLabel, 1, 0);
        panel.Width = digitWidth + (int)panel.ColumnStyles[1].Width;
        return panel;
    }

    private Control BuildRemoteSettings()
    {
        var panel = new RoundedPanel
        {
            Dock = DockStyle.Top,
            Margin = new Padding(12, 0, 0, 0),
            BackColor = AppTheme.Surface,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(0, 0)
        };

        var layout = CreateSettingsLayout();
        AddSectionTitle(layout, "Remote Hosts", 0);

        _remotePillsPanel.Dock = DockStyle.Top;
        _remotePillsPanel.AutoSize = true;
        _remotePillsPanel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _remotePillsPanel.FlowDirection = FlowDirection.LeftToRight;
        _remotePillsPanel.WrapContents = true;
        _remotePillsPanel.AutoScroll = false;
        _remotePillsPanel.MinimumSize = new Size(0, 42);
        _remotePillsPanel.Padding = new Padding(0, 4, 0, 0);
        _remotePillsPanel.BackColor = AppTheme.Surface;
        SetSettingsRowAutoSize(layout, 1);
        layout.Controls.Add(_remotePillsPanel, 0, 1);
        layout.SetColumnSpan(_remotePillsPanel, 2);

        AddLabeledControl(layout, "Name", _remoteNameBox, 2);
        AddLabeledControl(layout, "Host/IP", _remoteHostBox, 3);
        AddLabeledControl(layout, "Port", _remotePortBox, 4);
        AddLabeledControl(layout, "Username", _remoteUserBox, 5);
        AddLabeledControl(layout, "Password", _remotePasswordBox, 6);
        AddLabeledControl(layout, "Pinned cert", _remoteThumbprintBox, 7);

        _remotePortBox.Minimum = 1024;
        _remotePortBox.Maximum = 65535;
        _remotePortBox.Value = 54545;
        _remotePortBox.BackColor = AppTheme.SurfaceRaised;
        _remotePortBox.ForeColor = AppTheme.Text;
        _remotePortBox.BorderStyle = BorderStyle.FixedSingle;
        _remotePortBox.TextAlign = HorizontalAlignment.Left;
        _remotePasswordBox.UseSystemPasswordChar = true;
        foreach (var textBox in new[] { _remoteNameBox, _remoteHostBox, _remoteUserBox, _remotePasswordBox, _remoteThumbprintBox })
        {
            textBox.BackColor = AppTheme.SurfaceRaised;
            textBox.ForeColor = AppTheme.Text;
            textBox.BorderStyle = BorderStyle.FixedSingle;
            textBox.TextAlign = HorizontalAlignment.Left;
        }
        _remoteNameBox.PlaceholderText = " Required name";
        _remoteHostBox.PlaceholderText = " Required host/IP";
        _remoteUserBox.PlaceholderText = " Required username";
        _remotePasswordBox.PlaceholderText = " Required password";
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
        var newRemoteButton = new RoundedButton { Text = "New", Width = 70 };
        saveRemoteButton.Click += (_, _) => SaveRemoteHost();
        removeRemoteButton.Click += (_, _) => RemoveSelectedRemoteHost();
        clearPinButton.Click += (_, _) => _remoteThumbprintBox.Text = string.Empty;
        newRemoteButton.Click += (_, _) => ClearRemoteEditor();
        buttons.Controls.Add(saveRemoteButton);
        buttons.Controls.Add(removeRemoteButton);
        buttons.Controls.Add(clearPinButton);
        buttons.Controls.Add(newRemoteButton);
        SetSettingsRowHeight(layout, 8, ButtonRowHeight(saveRemoteButton));
        layout.Controls.Add(buttons, 0, 8);
        layout.SetColumnSpan(buttons, 2);

        var note = CreateNoteLabel("First successful connection pins the server certificate SHA-256 hash here. Clear the pin only when you intentionally replaced that host certificate.");
        SetSettingsRowAutoSize(layout, 9);
        layout.Controls.Add(note, 0, 9);
        layout.SetColumnSpan(note, 2);

        panel.Controls.Add(layout);
        return panel;
    }

    private TableLayoutPanel CreateSettingsLayout()
    {
        var labelWidth = Math.Max(176, TextRenderer.MeasureText("Update every", Font).Width + 36);
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            BackColor = AppTheme.Surface
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, labelWidth));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        return layout;
    }

    private static void ConfigureSettingsComboBox(ComboBox comboBox)
    {
        comboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        comboBox.BackColor = AppTheme.SurfaceRaised;
        comboBox.ForeColor = AppTheme.Text;
        comboBox.FlatStyle = FlatStyle.Flat;
        comboBox.IntegralHeight = false;
        comboBox.MaxDropDownItems = 12;
    }

    private void AddSectionTitle(TableLayoutPanel layout, string text, int row)
    {
        var title = new Label
        {
            Text = text,
            Dock = DockStyle.Fill,
            ForeColor = AppTheme.Text,
            Font = new Font("Segoe UI", 13.5F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft
        };
        SetSettingsRowHeight(layout, row, Math.Max(54, title.Font.Height + 20));
        layout.Controls.Add(title, 0, row);
        layout.SetColumnSpan(title, 2);
    }

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
            AutoEllipsis = true
        };
        var inputHeight = SettingsInputHeight(control);
        var rowHeight = Math.Max(50, Math.Max(labelControl.Font.Height + 24, inputHeight + 16));
        var verticalMargin = Math.Max(0, (rowHeight - inputHeight) / 2);
        control.Dock = DockStyle.None;
        control.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        control.Margin = new Padding(0, verticalMargin, 0, verticalMargin);
        control.MinimumSize = new Size(0, Math.Max(control.MinimumSize.Height, inputHeight));
        control.Height = Math.Max(control.Height, inputHeight);
        if (control is TextBox textBox)
        {
            textBox.TextAlign = HorizontalAlignment.Left;
        }

        if (control is NumericUpDown numeric)
        {
            numeric.TextAlign = HorizontalAlignment.Left;
        }

        SetSettingsRowHeight(layout, row, rowHeight);
        layout.Controls.Add(labelControl, 0, row);
        layout.Controls.Add(control, 1, row);
    }

    private static int SettingsInputHeight(Control control) =>
        control switch
        {
            TextBox textBox => Math.Max(textBox.Font.Height + 16, 34),
            NumericUpDown numeric => Math.Max(numeric.Font.Height + 18, 36),
            ComboBox comboBox => Math.Max(comboBox.Font.Height + 18, 36),
            _ => Math.Max(control.MinimumSize.Height, Math.Max(control.Font.Height + 16, 36))
        };

    private static int CheckBoxRowHeight(CheckBox checkBox) =>
        Math.Max(34, checkBox.Font.Height + 14);

    private static int ButtonRowHeight(Control control) =>
        Math.Max(54, control.Font.Height + 28);

    private static void SetSettingsRowHeight(TableLayoutPanel layout, int row, int height)
    {
        EnsureSettingsRow(layout, row);
        layout.RowStyles[row].SizeType = SizeType.Absolute;
        layout.RowStyles[row].Height = height;
    }

    private static void SetSettingsRowAutoSize(TableLayoutPanel layout, int row)
    {
        EnsureSettingsRow(layout, row);
        layout.RowStyles[row].SizeType = SizeType.AutoSize;
    }

    private static void EnsureSettingsRow(TableLayoutPanel layout, int row)
    {
        layout.RowCount = Math.Max(layout.RowCount, row + 1);
        while (layout.RowStyles.Count <= row)
        {
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }
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
            StartPolling();
        };

        Resize += (_, _) =>
        {
            if (WindowState == FormWindowState.Minimized)
            {
                HideToTray(showTip: true);
                return;
            }

            ApplyResponsiveLayout();
        };

        FormClosing += OnFormClosing;
        _intervalBox.ValueChanged += (_, _) =>
        {
            if (!_loadingSettingsControls)
            {
                ApplyUpdateIntervalFromBox(save: true);
            }
        };
        _intervalBox.Leave += (_, _) => ApplyUpdateIntervalFromBox(save: true);
        _intervalBox.KeyDown += (_, args) =>
        {
            if (args.KeyCode != Keys.Enter)
            {
                return;
            }

            ApplyUpdateIntervalFromBox(save: true);
            args.SuppressKeyPress = true;
        };
        _intervalBox.Enter += (_, _) => _intervalBox.Select(0, _intervalBox.Text.Length);
        _barSmoothingBox.ValueChanged += (_, _) =>
        {
            if (!_loadingSettingsControls)
            {
                ApplyBarSmoothingFromBox(save: true);
            }
        };
        _barSmoothingBox.Leave += (_, _) => ApplyBarSmoothingFromBox(save: true);
        _monitorTopMostBox.CheckedChanged += (_, _) => ApplyMonitorWindowOptions(save: true);
        _monitorOpacityBox.ValueChanged += (_, _) => ApplyMonitorWindowOptions(save: true);
        _networkAutoBox.CheckedChanged += (_, _) =>
        {
            if (!_loadingSettingsControls)
            {
                ApplyNetworkSettings(save: true);
            }
        };
        _networkUnitBox.SelectedIndexChanged += (_, _) =>
        {
            if (!_loadingSettingsControls)
            {
                ApplyNetworkSettings(save: true);
            }
        };
        _averageWindowBox.ValueChanged += (_, _) =>
        {
            if (!_loadingSettingsControls)
            {
                ApplyNetworkSettings(save: true);
            }
        };
        foreach (var comboBox in _networkInterfaceBoxes)
        {
            comboBox.SelectedIndexChanged += (_, _) =>
            {
                if (!_loadingSettingsControls)
                {
                    ApplyNetworkSettings(save: true);
                }
            };
        }

        _killButton.Click += async (_, _) => await KillSelectedProcessAsync();
        _killParentButton.Click += async (_, _) => await KillSelectedParentProcessAsync();
        _stopServiceButton.Click += async (_, _) => await ControlSelectedServiceAsync(ServiceControlAction.Stop);
        _startServiceButton.Click += async (_, _) => await ControlSelectedServiceAsync(ServiceControlAction.Start);
        _disableServiceButton.Click += async (_, _) => await ControlSelectedServiceAsync(ServiceControlAction.Disable);
        _enableServiceButton.Click += async (_, _) => await ControlSelectedServiceAsync(ServiceControlAction.Enable);
        _dashboardButton.Click += (_, _) => ShowDashboardPage();
        _settingsButton.Click += (_, _) => ShowSettingsPage();
        _processGrid.SelectionChanged += (_, _) =>
        {
            if (!_updatingProcessRows)
            {
                UpdateActionButtons();
            }
        };
    }

    private void LoadSettingsIntoControls()
    {
        _loadingSettingsControls = true;
        try
        {
            _settings.UpdateIntervalMs = Math.Clamp(_settings.UpdateIntervalMs, 250, 9999);
            _settings.BarSmoothingMs = Math.Clamp(_settings.BarSmoothingMs, 0, MaxBarSmoothingMs);
            _settings.MonitorWindowOpacityPercent = Math.Clamp(_settings.MonitorWindowOpacityPercent, 30, 100);
            _settings.AverageWindowMinutes = Math.Clamp(_settings.AverageWindowMinutes, 1, MaxAverageWindowMinutes);
            _intervalBox.Value = _settings.UpdateIntervalMs;
            _barSmoothingBox.Value = _settings.BarSmoothingMs;
            _monitorTopMostBox.Checked = _settings.MonitorWindowsStayOnTop;
            _monitorOpacityBox.Value = _settings.MonitorWindowOpacityPercent;
            _networkAutoBox.Checked = _settings.NetworkSelectionMode == NetworkSelectionMode.Auto;
            _averageWindowBox.Value = _settings.AverageWindowMinutes;
            SelectNetworkRateUnit(_settings.NetworkRateUnit);
            PopulateNetworkInterfaceSelectors();

            _listenerEnabledBox.Checked = _settings.ListenerEnabled;
            _confirmKillsBox.Checked = _settings.ConfirmTaskKills;
            _listenerPortBox.Value = Math.Clamp(_settings.ListenerPort, 1024, 65535);
            _listenerUserBox.Text = _settings.Username;
            _listenerPasswordBox.Text = _settings.GetPassword();
        }
        finally
        {
            _loadingSettingsControls = false;
        }

        ApplyBarSmoothingToControls();
        ApplyMonitorWindowOptionsToOpenWindows();
        ApplyNetworkSettings(save: false);
        RefreshRemoteList();
        RefreshThemeSwatches();
    }

    private void ApplyUpdateIntervalFromBox(bool save)
    {
        var interval = Math.Clamp((int)_intervalBox.Value, 250, 9999);
        var changed = _settings.UpdateIntervalMs != interval;
        if (_settings.UpdateIntervalMs != interval)
        {
            _settings.UpdateIntervalMs = interval;
            RestartPolling();
            SetStatusText($"Live telemetry - {_settings.UpdateIntervalMs:N0} ms updates");
        }

        if (_intervalBox.Value != interval)
        {
            _intervalBox.Value = interval;
        }

        if (save && changed)
        {
            SettingsStore.Save(_settings);
        }
    }

    private void ApplyBarSmoothingFromBox(bool save)
    {
        var smoothingMs = Math.Clamp((int)_barSmoothingBox.Value, 0, MaxBarSmoothingMs);
        var changed = _settings.BarSmoothingMs != smoothingMs;
        if (changed)
        {
            _settings.BarSmoothingMs = smoothingMs;
            ApplyBarSmoothingToControls();
        }

        if (_barSmoothingBox.Value != smoothingMs)
        {
            _barSmoothingBox.Value = smoothingMs;
        }

        if (save && changed)
        {
            SettingsStore.Save(_settings);
        }
    }

    private void ApplyBarSmoothingToControls()
    {
        foreach (var card in new[] { _cpuCard, _ramCard, _gpuCard, _vramCard })
        {
            card.SmoothingDurationMs = _settings.BarSmoothingMs;
        }

        foreach (var card in _hostCards.Values)
        {
            card.SmoothingDurationMs = _settings.BarSmoothingMs;
        }

        foreach (var window in _monitorWindows.Values)
        {
            if (_hostSnapshots.TryGetValue(window.HostId, out var snapshot))
            {
                window.UpdateSnapshot(snapshot, _settings.BarSmoothingMs, _settings.NetworkRateUnit);
            }
        }
    }

    private void ApplyMonitorWindowOptions(bool save)
    {
        var opacityPercent = Math.Clamp((int)_monitorOpacityBox.Value, 30, 100);
        var changed = _settings.MonitorWindowsStayOnTop != _monitorTopMostBox.Checked
            || _settings.MonitorWindowOpacityPercent != opacityPercent;

        _settings.MonitorWindowsStayOnTop = _monitorTopMostBox.Checked;
        _settings.MonitorWindowOpacityPercent = opacityPercent;
        ApplyMonitorWindowOptionsToOpenWindows();

        if (save && changed)
        {
            SettingsStore.Save(_settings);
        }

        if (_monitorOpacityBox.Value != opacityPercent)
        {
            _monitorOpacityBox.Value = opacityPercent;
        }
    }

    private void ApplyMonitorWindowOptionsToOpenWindows()
    {
        foreach (var window in _monitorWindows.Values)
        {
            window.ApplyOptions(_settings.MonitorWindowsStayOnTop, _settings.MonitorWindowOpacityPercent);
        }
    }

    private void PopulateNetworkInterfaceSelectors()
    {
        var selectedIds = _settings.TrackedNetworkInterfaceIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Take(MaxNetworkInterfaces)
            .ToArray();
        var options = NetworkUsageReader.GetAvailableInterfaces();

        for (var index = 0; index < _networkInterfaceBoxes.Length; index++)
        {
            var comboBox = _networkInterfaceBoxes[index];
            comboBox.BeginUpdate();
            comboBox.Items.Clear();
            comboBox.Items.Add(NetworkInterfaceSelectionItem.None);
            foreach (var option in options)
            {
                comboBox.Items.Add(new NetworkInterfaceSelectionItem(option));
            }

            var desiredId = index < selectedIds.Length ? selectedIds[index] : string.Empty;
            comboBox.SelectedItem = comboBox.Items
                .Cast<NetworkInterfaceSelectionItem>()
                .FirstOrDefault(item => string.Equals(item.Id, desiredId, StringComparison.OrdinalIgnoreCase))
                ?? NetworkInterfaceSelectionItem.None;
            comboBox.Enabled = _settings.NetworkSelectionMode == NetworkSelectionMode.Manual;
            comboBox.EndUpdate();
        }
    }

    private void SelectNetworkRateUnit(NetworkRateUnit unit)
    {
        foreach (var item in _networkUnitBox.Items.OfType<NetworkRateUnitItem>())
        {
            if (item.Unit == unit)
            {
                _networkUnitBox.SelectedItem = item;
                return;
            }
        }

        if (_networkUnitBox.Items.Count > 0)
        {
            _networkUnitBox.SelectedIndex = 0;
        }
    }

    private void ApplyNetworkSettings(bool save)
    {
        var selectedIds = _networkInterfaceBoxes
            .Select(comboBox => comboBox.SelectedItem)
            .OfType<NetworkInterfaceSelectionItem>()
            .Select(item => item.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxNetworkInterfaces)
            .ToList();
        var selectedUnit = (_networkUnitBox.SelectedItem as NetworkRateUnitItem)?.Unit ?? _settings.NetworkRateUnit;
        var averageWindowMinutes = Math.Clamp((int)_averageWindowBox.Value, 1, MaxAverageWindowMinutes);
        var mode = _networkAutoBox.Checked ? NetworkSelectionMode.Auto : NetworkSelectionMode.Manual;
        var changed = _settings.NetworkSelectionMode != mode
            || _settings.NetworkRateUnit != selectedUnit
            || _settings.AverageWindowMinutes != averageWindowMinutes
            || !_settings.TrackedNetworkInterfaceIds.SequenceEqual(selectedIds, StringComparer.OrdinalIgnoreCase);

        _settings.NetworkSelectionMode = mode;
        _settings.NetworkRateUnit = selectedUnit;
        _settings.AverageWindowMinutes = averageWindowMinutes;
        _settings.TrackedNetworkInterfaceIds = selectedIds;
        _collector.ApplySettings(_settings);

        foreach (var comboBox in _networkInterfaceBoxes)
        {
            comboBox.Enabled = mode == NetworkSelectionMode.Manual;
        }

        if (_averageWindowBox.Value != averageWindowMinutes)
        {
            _averageWindowBox.Value = averageWindowMinutes;
        }

        PruneHostMetricHistories();
        UpdateHostCards();
        UpdateMonitorWindows();
        RefreshSelectedHostView(forceProcessRefresh: false);

        if (save && changed)
        {
            SettingsStore.Save(_settings);
        }
    }

    private void ExportSettings()
    {
        var password = InputRules.NormalizePassword(_settingsTransferPasswordBox.Text);
        if (string.IsNullOrEmpty(password))
        {
            MessageBox.Show(this, "Enter a password before exporting settings.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dialog = new SaveFileDialog
        {
            AddExtension = true,
            DefaultExt = "vramvue",
            FileName = "vram-vue-settings.vramvue",
            Filter = "VRAM Vue settings (*.vramvue)|*.vramvue|All files (*.*)|*.*",
            OverwritePrompt = true,
            Title = "Export VRAM Vue settings"
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            SettingsPackage.Export(_settings, password, dialog.FileName);
            MessageBox.Show(this, "Settings exported.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or CryptographicException)
        {
            MessageBox.Show(this, $"Settings export failed: {ex.Message}", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async Task ImportSettingsAsync()
    {
        var password = InputRules.NormalizePassword(_settingsTransferPasswordBox.Text);
        if (string.IsNullOrEmpty(password))
        {
            MessageBox.Show(this, "Enter the export password before importing settings.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dialog = new OpenFileDialog
        {
            CheckFileExists = true,
            Filter = "VRAM Vue settings (*.vramvue)|*.vramvue|All files (*.*)|*.*",
            Title = "Import VRAM Vue settings"
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            var imported = SettingsPackage.Import(dialog.FileName, password);
            ApplyImportedSettings(imported);
            AppTheme.Apply(_settings.ThemeColors);
            SettingsStore.Save(_settings);
            ApplyThemePaletteToStaticControls();
            LoadSettingsIntoControls();
            ApplyThemeToOpenSurfaces();
            await RestartServerAsync();
            _hostListDirty = true;
            UpdateHostCards();
            UpdateMonitorWindows();
            RefreshSelectedHostView(forceProcessRefresh: true);
            await RefreshAllHostsAsync();
            MessageBox.Show(this, "Settings imported.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or CryptographicException or ArgumentException or FormatException)
        {
            MessageBox.Show(this, $"Settings import failed: {ex.Message}", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void ApplyImportedSettings(AppSettings imported)
    {
        _settings.ListenerEnabled = imported.ListenerEnabled;
        _settings.ListenerPort = imported.ListenerPort;
        _settings.Username = imported.Username;
        _settings.ProtectedPassword = imported.ProtectedPassword;
        _settings.UpdateIntervalMs = imported.UpdateIntervalMs;
        _settings.BarSmoothingMs = imported.BarSmoothingMs;
        _settings.MonitorWindowsStayOnTop = imported.MonitorWindowsStayOnTop;
        _settings.MonitorWindowOpacityPercent = imported.MonitorWindowOpacityPercent;
        _settings.ConfirmTaskKills = imported.ConfirmTaskKills;
        _settings.NetworkSelectionMode = imported.NetworkSelectionMode;
        _settings.TrackedNetworkInterfaceIds = imported.TrackedNetworkInterfaceIds ?? [];
        _settings.NetworkRateUnit = imported.NetworkRateUnit;
        _settings.AverageWindowMinutes = Math.Clamp(imported.AverageWindowMinutes, 1, MaxAverageWindowMinutes);
        _settings.ThemeColors = new Dictionary<string, string>(imported.ThemeColors, StringComparer.OrdinalIgnoreCase);
        _settings.RemoteHosts = imported.RemoteHosts;
        _selectedRemoteHostId = null;
    }

    private void RefreshThemeSwatches()
    {
        if (_themeSwatchesPanel.IsDisposed)
        {
            return;
        }

        _themeSwatchesPanel.SuspendLayout();
        _themeSwatchesPanel.Controls.Clear();
        foreach (var slot in AppTheme.ColorSlots)
        {
            var swatch = new ColorSwatchButton
            {
                SwatchColor = AppTheme.GetColor(slot.Key),
                BorderColor = AppTheme.Border,
                BackdropColor = AppTheme.Surface,
                RingColor = AppTheme.Text,
                AccessibleName = slot.Label,
                Tag = slot.Key
            };
            _toolTip.SetToolTip(swatch, $"{slot.Label}: {AppTheme.ToHex(AppTheme.GetColor(slot.Key))}");
            swatch.Click += (_, _) => ChooseThemeColor(slot);
            _themeSwatchesPanel.Controls.Add(swatch);
        }

        _themeSwatchesPanel.ResumeLayout();
    }

    private void ChooseThemeColor(ThemeColorSlot slot)
    {
        using var dialog = new ColorDialog
        {
            Color = AppTheme.GetColor(slot.Key),
            FullOpen = true
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        AppTheme.SetColor(slot.Key, dialog.Color);
        _settings.ThemeColors[slot.Key] = AppTheme.ToHex(dialog.Color);
        SettingsStore.Save(_settings);
        ApplyThemePaletteToStaticControls();
        ApplyThemeToOpenSurfaces();
        RefreshThemeSwatches();
    }

    private void ApplyThemePaletteToStaticControls()
    {
        _cpuCard.AccentColor = AppTheme.Accent;
        _ramCard.AccentColor = AppTheme.Good;
        _gpuCard.AccentColor = AppTheme.Warning;
        _vramCard.AccentColor = AppTheme.Danger;

        foreach (var button in new[]
        {
            _killButton,
            _killParentButton,
            _stopServiceButton,
            _startServiceButton,
            _disableServiceButton,
            _enableServiceButton,
            _dashboardButton,
            _settingsButton,
            _refreshButton,
            _hideButton
        })
        {
            button.ForeColor = AppTheme.Text;
            button.FillColor = AppTheme.SurfaceRaised;
            button.BorderColor = AppTheme.Border;
        }
    }

    private void ApplyThemeToOpenSurfaces()
    {
        BackColor = AppTheme.Background;
        ForeColor = AppTheme.Text;
        _rootLayout.BackColor = AppTheme.Background;
        _headerLayout.BackColor = AppTheme.Background;
        _dashboardLayout.BackColor = AppTheme.Background;
        _settingsContent.BackColor = AppTheme.Background;
        _dashboardPage.BackColor = AppTheme.Background;
        _settingsPage.BackColor = AppTheme.Background;
        _hostCardsPanel.BackColor = AppTheme.Background;
        _actionsPanel.BackColor = AppTheme.Background;
        _titleLabel.ForeColor = AppTheme.Text;
        _listenerStatusLabel.ForeColor = AppTheme.MutedText;
        _statusLabel.ForeColor = AppTheme.MutedText;

        _processGrid.BackgroundColor = AppTheme.Surface;
        _processGrid.GridColor = AppTheme.Border;
        _processGrid.ColumnHeadersDefaultCellStyle.BackColor = AppTheme.SurfaceRaised;
        _processGrid.ColumnHeadersDefaultCellStyle.ForeColor = AppTheme.Text;
        _processGrid.DefaultCellStyle.BackColor = AppTheme.Surface;
        _processGrid.DefaultCellStyle.ForeColor = AppTheme.Text;
        _processGrid.DefaultCellStyle.SelectionForeColor = AppTheme.Text;
        _processGrid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(
            Math.Min(255, AppTheme.Surface.R + 3),
            Math.Min(255, AppTheme.Surface.G + 4),
            Math.Min(255, AppTheme.Surface.B + 6));

        if (_settingsPage.Visible)
        {
            ShowSettingsPage();
        }
        else
        {
            ShowDashboardPage();
        }

        foreach (var card in _hostCards.Values)
        {
            card.Invalidate();
        }

        foreach (var window in _monitorWindows.Values)
        {
            window.BackColor = AppTheme.Surface;
            window.ForeColor = AppTheme.Text;
            window.Invalidate(true);
        }

        NativeWindowStyler.ApplyDarkTitleBar(this);
        Invalidate(true);
    }

    private bool IsUiInteractionPaused => _isMovingOrSizing || _openContextMenuCount > 0;

    private void BeginContextMenuInteraction()
    {
        _openContextMenuCount++;
    }

    private void EndContextMenuInteraction()
    {
        if (_openContextMenuCount > 0)
        {
            _openContextMenuCount--;
        }

        ApplyPendingRefreshIfReady();
    }

    private void ApplyPendingRefreshIfReady()
    {
        if (IsUiInteractionPaused || _pendingRefreshResults is null)
        {
            return;
        }

        var pending = _pendingRefreshResults;
        _pendingRefreshResults = null;
        ApplyRefreshResultsCore(pending);
    }

    private void ConfigureTrayIcon()
    {
        var trayMenu = new ContextMenuStrip();
        trayMenu.Opening += (_, _) => BeginContextMenuInteraction();
        trayMenu.Closed += (_, _) => EndContextMenuInteraction();
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

    private void StartPolling()
    {
        StopPolling();
        _pollLoopCts = new CancellationTokenSource();
        var token = _pollLoopCts.Token;
        _pollLoopTask = Task.Run(() => PollLoopAsync(token), token);
    }

    private void RestartPolling()
    {
        if (_pollLoopCts is not null)
        {
            StartPolling();
        }
    }

    private void StopPolling()
    {
        var cts = _pollLoopCts;
        _pollLoopCts = null;
        _pollLoopTask = null;

        if (cts is null)
        {
            return;
        }

        cts.Cancel();
        cts.Dispose();
    }

    private async Task PollLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(_settings.UpdateIntervalMs));
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                await RefreshAllHostsAsync();
            }
        }
        catch (OperationCanceledException)
        {
            // Polling was intentionally stopped or restarted.
        }
    }

    private async Task RefreshAllHostsAsync()
    {
        if (Interlocked.Exchange(ref _refreshInProgress, 1) == 1)
        {
            return;
        }

        _refreshCts?.Cancel();
        _refreshCts?.Dispose();
        _refreshCts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var refreshCts = _refreshCts;
        var token = refreshCts.Token;

        try
        {
            var remoteHosts = await RunOnUiThreadAsync(() => _settings.RemoteHosts.ToArray());
            var localTelemetryTask = Task.Run(() => _collector.Read(), token);
            var remoteResultsTask = Task.WhenAll(remoteHosts.Select(remote => RefreshRemoteHostAsync(remote, token)));

            var localTelemetry = await localTelemetryTask;
            var remoteResults = await remoteResultsTask;
            await RunOnUiThreadAsync(() => ApplyRefreshResults(new RefreshResults(localTelemetry, remoteResults)));
        }
        catch (OperationCanceledException)
        {
            // A newer refresh or shutdown superseded this one.
        }
        finally
        {
            if (ReferenceEquals(_refreshCts, refreshCts))
            {
                _refreshCts?.Dispose();
                _refreshCts = null;
            }

            Interlocked.Exchange(ref _refreshInProgress, 0);
        }
    }

    private async Task<RemoteRefreshResult> RefreshRemoteHostAsync(RemoteHostConfig remote, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(remote.Host))
        {
            return RemoteRefreshResult.Failed(remote, "No host configured");
        }

        try
        {
            var result = await _remoteClient.ReadTelemetryAsync(remote, cancellationToken);
            return result.Success && result.Telemetry is not null
                ? RemoteRefreshResult.Succeeded(remote, result.Telemetry, result.CertificateThumbprint)
                : RemoteRefreshResult.Failed(remote, result.ErrorMessage ?? "Unavailable");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException or AuthenticationException)
        {
            return RemoteRefreshResult.Failed(remote, ex.Message);
        }
    }

    private void ApplyRefreshResults(RefreshResults results)
    {
        if (IsUiInteractionPaused)
        {
            _pendingRefreshResults = results;
            return;
        }

        ApplyRefreshResultsCore(results);
    }

    private void ApplyRefreshResultsCore(RefreshResults results)
    {
        RefreshLocalHost(results.LocalTelemetry);

        var shouldSaveSettings = false;
        var shouldRefreshRemoteList = false;
        var activeRemoteIds = _settings.RemoteHosts.Select(remote => remote.Id).ToHashSet();

        foreach (var staleId in _hostSnapshots.Keys
            .Where(id => id != _localHostId && !activeRemoteIds.Contains(id))
            .ToArray())
        {
            _hostSnapshots.Remove(staleId);
            _hostMetricHistories.Remove(staleId);
            _hostListDirty = true;
        }

        foreach (var result in results.RemoteResults)
        {
            var remote = _settings.RemoteHosts.FirstOrDefault(item => item.Id == result.Remote.Id);
            if (remote is null)
            {
                continue;
            }

            if (result.Success && result.Telemetry is not null)
            {
                if (string.IsNullOrWhiteSpace(remote.TrustedCertificateThumbprint)
                    && !string.IsNullOrWhiteSpace(result.CertificateThumbprint))
                {
                    remote.TrustedCertificateThumbprint = result.CertificateThumbprint;
                    shouldSaveSettings = true;
                    shouldRefreshRemoteList = true;
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
                MarkRemoteOffline(remote, result.Status);
            }
        }

        if (shouldSaveSettings)
        {
            SettingsStore.Save(_settings);
        }

        if (shouldRefreshRemoteList)
        {
            RefreshRemoteList();
        }

        RecordHostMetricHistories();
        UpdateHostCards();
        UpdateMonitorWindows();
        RefreshSelectedHostView();
        UpdateListenerStatus();
        SetStatusText($"Live telemetry - {_settings.UpdateIntervalMs:N0} ms updates");
    }

    private void RefreshLocalHost(HostTelemetry telemetry)
    {
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

    private void RecordHostMetricHistories()
    {
        var window = AverageWindow;
        var activeIds = _hostSnapshots.Keys.ToHashSet();
        foreach (var staleId in _hostMetricHistories.Keys.Where(id => !activeIds.Contains(id)).ToArray())
        {
            _hostMetricHistories.Remove(staleId);
        }

        foreach (var snapshot in _hostSnapshots.Values)
        {
            if (snapshot.Telemetry is null)
            {
                continue;
            }

            if (!_hostMetricHistories.TryGetValue(snapshot.Id, out var history))
            {
                history = new RollingHostMetricHistory();
                _hostMetricHistories[snapshot.Id] = history;
            }

            history.Add(snapshot, window);
        }
    }

    private void PruneHostMetricHistories()
    {
        var window = AverageWindow;
        foreach (var history in _hostMetricHistories.Values)
        {
            history.Prune(DateTimeOffset.Now, window);
        }
    }

    private HostMetricAverage? GetHostMetricAverage(Guid id) =>
        _hostMetricHistories.TryGetValue(id, out var history)
            ? history.ReadAverage(AverageWindow)
            : null;

    private TimeSpan AverageWindow =>
        TimeSpan.FromMinutes(Math.Clamp(_settings.AverageWindowMinutes, 1, MaxAverageWindowMinutes));

    private string AverageWindowLabel()
    {
        var minutes = Math.Clamp(_settings.AverageWindowMinutes, 1, MaxAverageWindowMinutes);
        return minutes < 60
            ? $"{minutes}m"
            : minutes % 60 == 0
                ? $"{minutes / 60}h"
                : $"{minutes / 60D:N1}h";
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
        _hostCardsPanel.AutoScrollMargin = Size.Empty;
        _hostCardsPanel.AutoScrollMinSize = Size.Empty;
        _hostCardsPanel.HorizontalScroll.Enabled = false;
        _hostCardsPanel.HorizontalScroll.Visible = false;

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
                card = new HostCard
                {
                    UseCompactMemoryValues = true
                };
                var hostId = snapshot.Id;
                card.Click += (_, _) =>
                {
                    _selectedHostId = hostId;
                    UpdateHostCards();
                    RefreshSelectedHostView(forceProcessRefresh: true);
                };
                card.DoubleClick += (_, _) => OpenHostMonitor(hostId);
                card.MouseUp += (_, args) =>
                {
                    if (args.Button == MouseButtons.Right)
                    {
                        _selectedHostId = hostId;
                        UpdateHostCards();
                        RefreshSelectedHostView(forceProcessRefresh: true);
                        OpenHostMonitor(hostId);
                    }
                };
                _hostCards[snapshot.Id] = card;
            }

            card.SmoothingDurationMs = _settings.BarSmoothingMs;
            card.NetworkUnit = _settings.NetworkRateUnit;
            card.Snapshot = snapshot;
            card.IsSelected = snapshot.Id == _selectedHostId;
            var scrollReserve = orderedSnapshots.Length > 1 ? SystemInformation.VerticalScrollBarWidth : 0;
            card.Width = Math.Max(220, _hostCardsPanel.ClientSize.Width - scrollReserve - 8);
            var networkRows = Math.Min(MaxNetworkInterfaces, snapshot.NetworkInterfaces.Count);
            var preferredHeight = Math.Max(216, card.Font.Height * (7 + networkRows) + 104 + (networkRows * 18));
            if (orderedSnapshots.Length == 1 && _hostCardsPanel.ClientSize.Height > 234)
            {
                preferredHeight = Math.Min(preferredHeight, _hostCardsPanel.ClientSize.Height - 28);
            }

            card.Height = Math.Max(210, preferredHeight);
            card.Invalidate();

            if (orderChanged || _hostListDirty)
            {
                _hostCardsPanel.Controls.Add(card);
            }
        }

        _hostListDirty = false;
        _hostCardsPanel.ResumeLayout();
        _hostCardsPanel.HorizontalScroll.Enabled = false;
        _hostCardsPanel.HorizontalScroll.Visible = false;
    }

    private void OpenHostMonitor(Guid hostId)
    {
        if (!_hostSnapshots.TryGetValue(hostId, out var snapshot))
        {
            return;
        }

        if (_monitorWindows.TryGetValue(hostId, out var existing))
        {
            if (existing.IsDisposed)
            {
                _monitorWindows.Remove(hostId);
            }
            else
            {
                existing.SnapBoundsProvider = GetSnapTargetsForMonitorWindow;
                existing.UpdateSnapshot(snapshot, _settings.BarSmoothingMs, _settings.NetworkRateUnit);
                existing.ApplyOptions(_settings.MonitorWindowsStayOnTop, _settings.MonitorWindowOpacityPercent);
                if (existing.WindowState == FormWindowState.Minimized)
                {
                    existing.WindowState = FormWindowState.Normal;
                }

                existing.Show();
                existing.Activate();
                return;
            }
        }

        var window = new HostMonitorForm(hostId, _appIcon);
        window.SnapBoundsProvider = GetSnapTargetsForMonitorWindow;
        window.ContextMenuOpened += (_, _) => BeginContextMenuInteraction();
        window.ContextMenuClosed += (_, _) => EndContextMenuInteraction();
        window.FormClosed += (_, _) => _monitorWindows.Remove(hostId);
        window.UpdateSnapshot(snapshot, _settings.BarSmoothingMs, _settings.NetworkRateUnit);
        window.ApplyOptions(_settings.MonitorWindowsStayOnTop, _settings.MonitorWindowOpacityPercent);
        PositionHostMonitorWindow(window);
        _monitorWindows[hostId] = window;
        window.Show();
        window.Activate();
    }

    private void UpdateMonitorWindows()
    {
        foreach (var (hostId, window) in _monitorWindows.ToArray())
        {
            if (window.IsDisposed)
            {
                _monitorWindows.Remove(hostId);
                continue;
            }

            if (_hostSnapshots.TryGetValue(hostId, out var snapshot))
            {
                window.UpdateSnapshot(snapshot, _settings.BarSmoothingMs, _settings.NetworkRateUnit);
                window.ApplyOptions(_settings.MonitorWindowsStayOnTop, _settings.MonitorWindowOpacityPercent);
            }
            else
            {
                window.Close();
                _monitorWindows.Remove(hostId);
            }
        }
    }

    private void PositionHostMonitorWindow(Form window)
    {
        var screen = Screen.FromPoint(Cursor.Position).WorkingArea;
        var x = Math.Clamp(Cursor.Position.X + 18, screen.Left, Math.Max(screen.Left, screen.Right - window.Width));
        var y = Math.Clamp(Cursor.Position.Y + 18, screen.Top, Math.Max(screen.Top, screen.Bottom - window.Height));
        window.Location = new Point(x, y);
    }

    private IEnumerable<Rectangle> GetSnapTargetsForMonitorWindow(HostMonitorForm source)
    {
        if (WindowState == FormWindowState.Normal && Visible)
        {
            yield return Bounds;
        }

        foreach (var window in _monitorWindows.Values)
        {
            if (ReferenceEquals(window, source) || window.IsDisposed || window.WindowState != FormWindowState.Normal)
            {
                continue;
            }

            yield return window.Bounds;
        }
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
            $"{row.ProcessId}:{row.ProcessName}:{row.LocalVramBytes}:{row.SharedBytes}:{row.NonLocalBytes}:{row.SystemGpuMemoryBytes}:{row.SpilloverStatus}:{row.RestartBehavior}:{row.ServiceName}:{row.ServiceState}:{row.ServiceStartMode}:{row.ServiceCount}:{row.ParentProcessId}:{row.ParentProcessName}:{row.WindowTitle}:{row.Notes}:{row.CanKill}"));

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

        _updatingProcessRows = true;
        _processGrid.SuspendLayout();
        try
        {
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
        }
        finally
        {
            _processGrid.ResumeLayout();
            _updatingProcessRows = false;
        }

        UpdateActionButtons();
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

        var average = GetHostMetricAverage(host.Id);
        var hasAverage = average is not null;
        var averageValue = average.GetValueOrDefault();
        var averageLabel = AverageWindowLabel();
        SetMetric(
            _cpuCard,
            "CPU",
            Formatters.Percent(host.CpuPercent),
            BuildAverageDetail(averageLabel, hasAverage ? Formatters.Percent(averageValue.CpuPercent) : string.Empty, host.DisplayName),
            host.CpuPercent / 100);
        SetMemoryMetric(
            _ramCard,
            "RAM",
            host.RamUsedBytes,
            host.RamTotalBytes,
            AppTheme.Good,
            hasAverage ? $"{averageLabel} avg {Formatters.BytesPrecise((long)averageValue.RamUsedBytes)}" : string.Empty);
        SetMetric(
            _gpuCard,
            "GPU",
            Formatters.Percent(host.GpuPercent),
            BuildAverageDetail(averageLabel, hasAverage ? Formatters.Percent(averageValue.GpuPercent) : string.Empty, "Engine utilization"),
            host.GpuPercent / 100);
        SetMemoryMetric(
            _vramCard,
            "VRAM",
            host.VramUsedBytes,
            host.VramTotalBytes,
            AppTheme.Danger,
            hasAverage ? $"{averageLabel} avg {Formatters.BytesPrecise((long)averageValue.VramUsedBytes)}" : string.Empty);
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

    private static string BuildAverageDetail(string averageLabel, string averageValue, string fallbackDetail) =>
        string.IsNullOrWhiteSpace(averageValue)
            ? fallbackDetail
            : $"{averageLabel} avg {averageValue} | {fallbackDetail}";

    private static void SetMemoryMetric(MetricCard card, string title, long usedBytes, long totalBytes, Color accentColor, string averageDetail)
    {
        var freeBytes = Math.Max(0, totalBytes - usedBytes);
        var overBytes = Math.Max(0, usedBytes - totalBytes);
        var capacityDetail = totalBytes <= 0
            ? "Total unknown"
            : overBytes > 0
                ? $"{Formatters.BytesPrecise(overBytes)} over / {Formatters.BytesPrecise(totalBytes)} total"
                : $"{Formatters.BytesPrecise(freeBytes)} free / {Formatters.BytesPrecise(totalBytes)} total";
        var detail = string.IsNullOrWhiteSpace(averageDetail)
            ? capacityDetail
            : $"{averageDetail} | {capacityDetail}";
        var value = totalBytes <= 0
            ? Formatters.BytesPrecise(usedBytes)
            : $"{BytesAsGb(usedBytes)}/{BytesAsGb(totalBytes)}GB";

        var previousAccent = card.AccentColor;
        card.AccentColor = overBytes > 0 ? AppTheme.Warning : accentColor;
        SetMetric(card, title, value, detail, Formatters.Ratio(usedBytes, totalBytes));
        if (previousAccent != card.AccentColor)
        {
            card.Invalidate();
        }
    }

    private static string BytesAsGb(long bytes) => $"{bytes / 1024D / 1024D / 1024D:N2}";

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
            $"Terminate {row.ProcessName} ({row.ProcessId}) on {host.DisplayName}?{Environment.NewLine}{Environment.NewLine}VRAM reported: {Formatters.BytesPrecise(row.LocalVramBytes)}",
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
        ApplyUpdateIntervalFromBox(save: false);
        ApplyBarSmoothingFromBox(save: false);
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

        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show(this, "Enter a display name for the remote host.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

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

        var selected = _selectedRemoteHostId is null
            ? null
            : _settings.RemoteHosts.FirstOrDefault(item => item.Id == _selectedRemoteHostId.Value);
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

        _selectedRemoteHostId = remote.Id;
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
        if (_selectedRemoteHostId is null)
        {
            return;
        }

        var remote = _settings.RemoteHosts.FirstOrDefault(item => item.Id == _selectedRemoteHostId.Value);
        if (remote is null)
        {
            return;
        }

        _settings.RemoteHosts.RemoveAll(item => item.Id == remote.Id);
        _hostSnapshots.Remove(remote.Id);
        _hostMetricHistories.Remove(remote.Id);
        _selectedRemoteHostId = null;
        SettingsStore.Save(_settings);
        RefreshRemoteList();
        _hostListDirty = true;
        UpdateHostCards();
        UpdateMonitorWindows();
        RefreshSelectedHostView(forceProcessRefresh: true);
    }

    private void RefreshRemoteList()
    {
        if (_selectedRemoteHostId is not null && _settings.RemoteHosts.All(item => item.Id != _selectedRemoteHostId.Value))
        {
            _selectedRemoteHostId = null;
        }

        _remotePillsPanel.SuspendLayout();
        _remotePillsPanel.Controls.Clear();
        foreach (var remote in _settings.RemoteHosts.OrderBy(item => item.DisplayName))
        {
            var isSelected = remote.Id == _selectedRemoteHostId;
            var button = new RoundedButton
            {
                Text = remote.DisplayName,
                Width = Math.Clamp(TextRenderer.MeasureText(remote.DisplayName, Font).Width + 34, 88, 220),
                Height = Math.Max(34, Font.Height + 16),
                CornerRadius = 18,
                FillColor = isSelected ? Color.FromArgb(37, 74, 119) : AppTheme.SurfaceRaised,
                HoverColor = isSelected ? Color.FromArgb(48, 91, 144) : Color.FromArgb(39, 47, 61),
                PressedColor = isSelected ? Color.FromArgb(29, 59, 96) : Color.FromArgb(48, 57, 73),
                BorderColor = isSelected ? AppTheme.Accent : AppTheme.Border,
                Margin = new Padding(0, 0, 8, 8),
                Tag = remote.Id
            };
            button.Click += (_, _) =>
            {
                _selectedRemoteHostId = remote.Id;
                RefreshRemoteList();
            };
            _remotePillsPanel.Controls.Add(button);
        }

        _remotePillsPanel.ResumeLayout();
        PopulateRemoteEditorFromSelection();
    }

    private void PopulateRemoteEditorFromSelection()
    {
        if (_selectedRemoteHostId is null
            || _settings.RemoteHosts.FirstOrDefault(item => item.Id == _selectedRemoteHostId.Value) is not { } remote)
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

    private void ClearRemoteEditor()
    {
        _selectedRemoteHostId = null;
        RefreshRemoteList();
    }

    private void UpdateActionButtons()
    {
        var row = _processGrid.SelectedRows
            .Cast<DataGridViewRow>()
            .Select(item => item.DataBoundItem)
            .OfType<GpuProcessInfo>()
            .FirstOrDefault();

        var layoutChanged = false;
        SetEnabledIfChanged(_killButton, row?.CanKill == true);
        var showParent = row?.ParentProcessId is not null;
        layoutChanged |= SetVisibleIfChanged(_killParentButton, showParent);
        SetEnabledIfChanged(_killParentButton, showParent);

        var hasService = !string.IsNullOrWhiteSpace(row?.ServiceName);
        var serviceStopped = string.Equals(row?.ServiceState, "Stopped", StringComparison.OrdinalIgnoreCase);
        var serviceDisabled = string.Equals(row?.ServiceStartMode, "Disabled", StringComparison.OrdinalIgnoreCase);

        layoutChanged |= SetVisibleIfChanged(_stopServiceButton, hasService);
        layoutChanged |= SetVisibleIfChanged(_startServiceButton, hasService);
        layoutChanged |= SetVisibleIfChanged(_disableServiceButton, hasService);
        layoutChanged |= SetVisibleIfChanged(_enableServiceButton, hasService);
        SetEnabledIfChanged(_stopServiceButton, hasService && !serviceStopped);
        SetEnabledIfChanged(_startServiceButton, hasService && serviceStopped && !serviceDisabled);
        SetEnabledIfChanged(_disableServiceButton, hasService && !serviceDisabled);
        SetEnabledIfChanged(_enableServiceButton, hasService && serviceDisabled);

        if (layoutChanged)
        {
            _actionsPanel.PerformLayout();
            ApplyResponsiveLayout();
        }
    }

    private static bool SetVisibleIfChanged(Control control, bool visible)
    {
        if (control.Visible == visible)
        {
            return false;
        }

        control.Visible = visible;
        return true;
    }

    private static void SetEnabledIfChanged(Control control, bool enabled)
    {
        if (control.Enabled != enabled)
        {
            control.Enabled = enabled;
        }
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

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmEnterSizeMove)
        {
            _isMovingOrSizing = true;
        }

        base.WndProc(ref m);

        if (m.Msg == WmExitSizeMove)
        {
            _isMovingOrSizing = false;
            ApplyPendingRefreshIfReady();
        }
    }

    private Task RunOnUiThreadAsync(Action action) =>
        RunOnUiThreadAsync(() =>
        {
            action();
            return true;
        });

    private Task<T> RunOnUiThreadAsync<T>(Func<T> action)
    {
        if (IsDisposed || !IsHandleCreated)
        {
            return Task.FromCanceled<T>(new CancellationToken(canceled: true));
        }

        if (!InvokeRequired)
        {
            try
            {
                return Task.FromResult(action());
            }
            catch (Exception ex)
            {
                return Task.FromException<T>(ex);
            }
        }

        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            BeginInvoke(new MethodInvoker(() =>
            {
                if (IsDisposed)
                {
                    completion.TrySetCanceled();
                    return;
                }

                try
                {
                    completion.TrySetResult(action());
                }
                catch (Exception ex)
                {
                    completion.TrySetException(ex);
                }
            }));
        }
        catch (InvalidOperationException)
        {
            completion.TrySetCanceled();
        }

        return completion.Task;
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

    private sealed record RefreshResults(
        HostTelemetry LocalTelemetry,
        IReadOnlyList<RemoteRefreshResult> RemoteResults);

    private sealed record RemoteRefreshResult(
        RemoteHostConfig Remote,
        HostTelemetry? Telemetry,
        string? CertificateThumbprint,
        string Status,
        bool Success)
    {
        public static RemoteRefreshResult Succeeded(RemoteHostConfig remote, HostTelemetry telemetry, string? certificateThumbprint) =>
            new(remote, telemetry, certificateThumbprint, "Online", true);

        public static RemoteRefreshResult Failed(RemoteHostConfig remote, string status) =>
            new(remote, null, null, status, false);
    }

    private sealed class RollingHostMetricHistory
    {
        private static readonly TimeSpan MinimumSampleSpacing = TimeSpan.FromSeconds(1);
        private readonly Queue<HostMetricSample> _samples = [];
        private DateTimeOffset _lastSampleAt = DateTimeOffset.MinValue;

        public void Add(HostSnapshot snapshot, TimeSpan window)
        {
            var now = DateTimeOffset.Now;
            if (now - _lastSampleAt < MinimumSampleSpacing)
            {
                Prune(now, window);
                return;
            }

            _lastSampleAt = now;
            _samples.Enqueue(new HostMetricSample(
                now,
                snapshot.CpuPercent,
                snapshot.GpuPercent,
                snapshot.RamUsedBytes,
                snapshot.VramUsedBytes));
            Prune(now, window);
        }

        public HostMetricAverage? ReadAverage(TimeSpan window)
        {
            var now = DateTimeOffset.Now;
            Prune(now, window);
            if (_samples.Count == 0)
            {
                return null;
            }

            return new HostMetricAverage(
                _samples.Average(sample => sample.CpuPercent),
                _samples.Average(sample => sample.GpuPercent),
                _samples.Average(sample => sample.RamUsedBytes),
                _samples.Average(sample => sample.VramUsedBytes));
        }

        public void Prune(DateTimeOffset now, TimeSpan window)
        {
            var cutoff = now - window;
            while (_samples.Count > 0 && _samples.Peek().CapturedAt < cutoff)
            {
                _samples.Dequeue();
            }
        }
    }

    private readonly record struct HostMetricSample(
        DateTimeOffset CapturedAt,
        double CpuPercent,
        double GpuPercent,
        long RamUsedBytes,
        long VramUsedBytes);

    private readonly record struct HostMetricAverage(
        double CpuPercent,
        double GpuPercent,
        double RamUsedBytes,
        double VramUsedBytes);

    private sealed record NetworkRateUnitItem(NetworkRateUnit Unit)
    {
        public override string ToString() => NetworkRateFormatter.DisplayName(Unit);
    }

    private sealed class NetworkInterfaceSelectionItem
    {
        public static NetworkInterfaceSelectionItem None { get; } = new(string.Empty, "(None)");

        private readonly string _displayName;

        private NetworkInterfaceSelectionItem(string id, string displayName)
        {
            Id = id;
            _displayName = displayName;
        }

        public NetworkInterfaceSelectionItem(NetworkInterfaceOption option)
            : this(option.Id, option.DisplayName)
        {
        }

        public string Id { get; }

        public override string ToString() => _displayName;
    }
}
