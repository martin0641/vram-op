namespace VramOp;

internal sealed class HostMonitorForm : Form
{
    private readonly HostCard _card = new()
    {
        Dock = DockStyle.Fill,
        IsSelected = true,
        Margin = Padding.Empty
    };

    private Font? _cardFont;
    private float _lastFontSize;

    public Guid HostId { get; }

    public HostMonitorForm(Guid hostId, Icon icon)
    {
        HostId = hostId;
        Icon = icon;
        Text = "VRAM Vue monitor";
        BackColor = AppTheme.Background;
        ForeColor = AppTheme.Text;
        Font = new Font("Segoe UI", 9F);
        AutoScaleMode = AutoScaleMode.Font;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimizeBox = true;
        MaximizeBox = true;
        ShowInTaskbar = true;
        StartPosition = FormStartPosition.Manual;
        MinimumSize = new Size(280, 190);
        ClientSize = new Size(380, 238);
        Padding = new Padding(8);

        Controls.Add(_card);
        UpdateCardScale();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _cardFont?.Dispose();
        }

        base.Dispose(disposing);
    }

    public void UpdateSnapshot(HostSnapshot snapshot, int smoothingDurationMs)
    {
        Text = $"{snapshot.DisplayName} - VRAM Vue";
        _card.SmoothingDurationMs = smoothingDurationMs;
        _card.Snapshot = snapshot;
        _card.Invalidate();
    }

    public void ApplyOptions(bool stayOnTop, int opacityPercent)
    {
        TopMost = stayOnTop;
        Opacity = Math.Clamp(opacityPercent, 30, 100) / 100.0;
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        UpdateCardScale();
    }

    private void UpdateCardScale()
    {
        var contentWidth = Math.Max(1, ClientSize.Width - Padding.Horizontal);
        var contentHeight = Math.Max(1, ClientSize.Height - Padding.Vertical);
        var sizeFromWidth = contentWidth / 38F;
        var sizeFromHeight = contentHeight / 17F;
        var fontSize = Math.Clamp(Math.Min(sizeFromWidth, sizeFromHeight), 7.5F, 22F);

        if (Math.Abs(fontSize - _lastFontSize) < 0.25F)
        {
            return;
        }

        _lastFontSize = fontSize;
        var previousFont = _cardFont;
        _cardFont = new Font("Segoe UI", fontSize);
        _card.Font = _cardFont;
        previousFont?.Dispose();
    }
}
