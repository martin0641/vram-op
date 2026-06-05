using System.Runtime.InteropServices;

namespace VramOp;

internal sealed class HostMonitorForm : Form
{
    private const int WmNchitTest = 0x0084;
    private const int WmNclButtonDown = 0x00A1;
    private const int HtLeft = 10;
    private const int HtRight = 11;
    private const int HtTop = 12;
    private const int HtTopLeft = 13;
    private const int HtTopRight = 14;
    private const int HtBottom = 15;
    private const int HtBottomLeft = 16;
    private const int HtBottomRight = 17;
    private const int HtCaption = 2;

    private readonly ContextMenuStrip _menu = new();
    private readonly HostCard _card = new()
    {
        Dock = DockStyle.Fill,
        IsSelected = true,
        Margin = Padding.Empty,
        UseCompactMemoryValues = true
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
        FormBorderStyle = FormBorderStyle.None;
        MinimizeBox = true;
        MaximizeBox = true;
        ShowInTaskbar = true;
        StartPosition = FormStartPosition.Manual;
        MinimumSize = new Size(280, 190);
        ClientSize = new Size(380, 238);
        Padding = new Padding(6);
        KeyPreview = true;

        _menu.Items.Add("Close", null, (_, _) => Close());
        ContextMenuStrip = _menu;
        _card.ContextMenuStrip = _menu;
        MouseDown += (_, args) => BeginMove(args);
        _card.MouseDown += (_, args) => BeginMove(args);
        Controls.Add(_card);
        UpdateCardScale();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _menu.Dispose();
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

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape)
        {
            Close();
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmNchitTest)
        {
            var hit = HitTestResizeBorder(m.LParam);
            if (hit is not null)
            {
                m.Result = hit.Value;
                return;
            }
        }

        base.WndProc(ref m);
    }

    private void BeginMove(MouseEventArgs args)
    {
        if (args.Button != MouseButtons.Left || WindowState == FormWindowState.Maximized)
        {
            return;
        }

        ReleaseCapture();
        SendMessage(Handle, WmNclButtonDown, HtCaption, 0);
    }

    private nint? HitTestResizeBorder(IntPtr lParam)
    {
        var point = PointToClient(GetPointFromLParam(lParam));
        var grip = Math.Max(6, (int)Math.Round(8 * DeviceDpi / 96D));
        var left = point.X <= grip;
        var right = point.X >= ClientSize.Width - grip;
        var top = point.Y <= grip;
        var bottom = point.Y >= ClientSize.Height - grip;

        if (top && left)
        {
            return HtTopLeft;
        }

        if (top && right)
        {
            return HtTopRight;
        }

        if (bottom && left)
        {
            return HtBottomLeft;
        }

        if (bottom && right)
        {
            return HtBottomRight;
        }

        if (left)
        {
            return HtLeft;
        }

        if (right)
        {
            return HtRight;
        }

        if (top)
        {
            return HtTop;
        }

        return bottom ? HtBottom : null;
    }

    private static Point GetPointFromLParam(IntPtr lParam)
    {
        var value = lParam.ToInt64();
        return new Point((short)(value & 0xFFFF), (short)((value >> 16) & 0xFFFF));
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

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern nint SendMessage(nint hWnd, int msg, int wParam, int lParam);
}
