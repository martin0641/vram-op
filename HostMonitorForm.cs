using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace VramOp;

internal sealed class HostMonitorForm : Form
{
    private const int WmNchitTest = 0x0084;
    private const int WmNclButtonDown = 0x00A1;
    private const int WmExitSizeMove = 0x0232;
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
        ShowResizeGrip = true,
        UseCompactMemoryValues = true,
        UseCompactNetworkValues = true
    };

    private Font? _cardFont;
    private float _lastFontSize;

    public Guid HostId { get; }
    public Func<HostMonitorForm, IEnumerable<Rectangle>>? SnapBoundsProvider { get; set; }
    public event EventHandler? ContextMenuOpened;
    public event EventHandler? ContextMenuClosed;

    public HostMonitorForm(Guid hostId, Icon icon)
    {
        HostId = hostId;
        Icon = icon;
        Text = "VRAM Vue monitor";
        BackColor = AppTheme.Surface;
        ForeColor = AppTheme.Text;
        Font = new Font("Segoe UI", 9F);
        AutoScaleMode = AutoScaleMode.Font;
        FormBorderStyle = FormBorderStyle.None;
        MinimizeBox = true;
        MaximizeBox = true;
        ShowInTaskbar = true;
        StartPosition = FormStartPosition.Manual;
        MinimumSize = new Size(320, 260);
        ClientSize = new Size(430, 340);
        Padding = Padding.Empty;
        KeyPreview = true;

        _menu.Items.Add("Close", null, (_, _) => Close());
        _menu.Opening += (_, _) => ContextMenuOpened?.Invoke(this, EventArgs.Empty);
        _menu.Closed += (_, _) => ContextMenuClosed?.Invoke(this, EventArgs.Empty);
        ContextMenuStrip = _menu;
        _card.ContextMenuStrip = _menu;
        MouseDown += (sender, args) => BeginMove(sender, args);
        _card.MouseDown += (sender, args) => BeginMove(sender, args);
        Controls.Add(_card);
        UpdateCardScale();
        UpdateRoundedRegion();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _menu.Dispose();
            _cardFont?.Dispose();
            var region = Region;
            Region = null;
            region?.Dispose();
        }

        base.Dispose(disposing);
    }

    public void UpdateSnapshot(HostSnapshot snapshot, int smoothingDurationMs, NetworkRateUnit networkUnit)
    {
        Text = $"{snapshot.DisplayName} - VRAM Vue";
        _card.SmoothingDurationMs = smoothingDurationMs;
        _card.NetworkUnit = networkUnit;
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
        UpdateRoundedRegion();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        UpdateRoundedRegion();
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

        if (m.Msg == WmExitSizeMove)
        {
            SnapToNearbyEdges();
        }
    }

    private void BeginMove(object? sender, MouseEventArgs args)
    {
        if (args.Button != MouseButtons.Left || WindowState == FormWindowState.Maximized)
        {
            return;
        }

        var clientPoint = sender is Control control
            ? PointToClient(control.PointToScreen(args.Location))
            : args.Location;
        if (IsInResizeGrip(clientPoint))
        {
            return;
        }

        ReleaseCapture();
        SendMessage(Handle, WmNclButtonDown, HtCaption, 0);
    }

    private nint? HitTestResizeBorder(IntPtr lParam)
    {
        var point = PointToClient(GetPointFromLParam(lParam));
        if (IsInResizeGrip(point))
        {
            return HtBottomRight;
        }

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

    private bool IsInResizeGrip(Point point)
    {
        var size = ScaleForDpi(34);
        return point.X >= ClientSize.Width - size
            && point.Y >= ClientSize.Height - size
            && point.X <= ClientSize.Width
            && point.Y <= ClientSize.Height;
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
        var sizeFromHeight = contentHeight / 24F;
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

    private void UpdateRoundedRegion()
    {
        if (!IsHandleCreated || ClientSize.Width <= 0 || ClientSize.Height <= 0)
        {
            return;
        }

        if (WindowState == FormWindowState.Maximized)
        {
            var oldMaximizedRegion = Region;
            Region = null;
            oldMaximizedRegion?.Dispose();
            return;
        }

        using var path = RoundedPath(new Rectangle(Point.Empty, ClientSize), ScaleForDpi(16));
        var oldRegion = Region;
        Region = new Region(path);
        oldRegion?.Dispose();
    }

    private void SnapToNearbyEdges()
    {
        if (WindowState != FormWindowState.Normal)
        {
            return;
        }

        var bounds = Bounds;
        var snapped = bounds.Location;
        var threshold = ScaleForDpi(16);
        var screen = Screen.FromRectangle(bounds);

        snapped = SnapToRectangle(snapped, bounds.Size, screen.WorkingArea, threshold, adjacent: false);
        snapped = SnapToRectangle(snapped, bounds.Size, screen.Bounds, threshold, adjacent: false);

        if (SnapBoundsProvider is not null)
        {
            foreach (var target in SnapBoundsProvider(this))
            {
                if (target.Width <= 0 || target.Height <= 0 || target == bounds)
                {
                    continue;
                }

                snapped = SnapToRectangle(snapped, bounds.Size, target, threshold, adjacent: true);
            }
        }

        if (snapped != bounds.Location)
        {
            Location = snapped;
        }
    }

    private static Point SnapToRectangle(Point location, Size size, Rectangle target, int threshold, bool adjacent)
    {
        var left = location.X;
        var top = location.Y;
        var right = left + size.Width;
        var bottom = top + size.Height;

        if (Near(left, target.Left, threshold))
        {
            location.X = target.Left;
        }
        else if (Near(right, target.Right, threshold))
        {
            location.X = target.Right - size.Width;
        }
        else if (adjacent && RangesOverlap(top, bottom, target.Top, target.Bottom))
        {
            if (Near(left, target.Right, threshold))
            {
                location.X = target.Right;
            }
            else if (Near(right, target.Left, threshold))
            {
                location.X = target.Left - size.Width;
            }
        }

        left = location.X;
        top = location.Y;
        right = left + size.Width;
        bottom = top + size.Height;

        if (Near(top, target.Top, threshold))
        {
            location.Y = target.Top;
        }
        else if (Near(bottom, target.Bottom, threshold))
        {
            location.Y = target.Bottom - size.Height;
        }
        else if (adjacent && RangesOverlap(left, right, target.Left, target.Right))
        {
            if (Near(top, target.Bottom, threshold))
            {
                location.Y = target.Bottom;
            }
            else if (Near(bottom, target.Top, threshold))
            {
                location.Y = target.Top - size.Height;
            }
        }

        return location;
    }

    private static bool Near(int first, int second, int threshold) => Math.Abs(first - second) <= threshold;

    private static bool RangesOverlap(int firstStart, int firstEnd, int secondStart, int secondEnd) =>
        Math.Max(firstStart, secondStart) < Math.Min(firstEnd, secondEnd);

    private int ScaleForDpi(int pixels) => Math.Max(1, (int)Math.Round(pixels * DeviceDpi / 96D));

    private static GraphicsPath RoundedPath(Rectangle bounds, int radius)
    {
        var diameter = Math.Max(2, radius * 2);
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern nint SendMessage(nint hWnd, int msg, int wParam, int lParam);
}
