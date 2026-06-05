using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace VramOp;

internal static class AppTheme
{
    public static readonly Color Background = Color.FromArgb(13, 17, 23);
    public static readonly Color Surface = Color.FromArgb(22, 27, 34);
    public static readonly Color SurfaceRaised = Color.FromArgb(30, 36, 46);
    public static readonly Color Border = Color.FromArgb(48, 56, 70);
    public static readonly Color Text = Color.FromArgb(235, 241, 247);
    public static readonly Color MutedText = Color.FromArgb(145, 156, 172);
    public static readonly Color Accent = Color.FromArgb(88, 166, 255);
    public static readonly Color Good = Color.FromArgb(69, 214, 136);
    public static readonly Color Warning = Color.FromArgb(255, 197, 92);
    public static readonly Color Danger = Color.FromArgb(255, 107, 107);
}

internal sealed class BufferedFlowLayoutPanel : FlowLayoutPanel
{
    private const int SB_HORZ = 0;
    private const int WS_HSCROLL = 0x00100000;

    public BufferedFlowLayoutPanel()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        ResizeRedraw = true;
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var createParams = base.CreateParams;
            createParams.Style &= ~WS_HSCROLL;
            return createParams;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        HideHorizontalScrollBar();
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        HideHorizontalScrollBar();
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        HideHorizontalScrollBar();
    }

    private void HideHorizontalScrollBar()
    {
        if (!IsHandleCreated)
        {
            return;
        }

        AutoScrollMinSize = Size.Empty;
        HorizontalScroll.Enabled = false;
        HorizontalScroll.Visible = false;
        ShowScrollBar(Handle, SB_HORZ, false);
    }

    [DllImport("user32.dll")]
    private static extern bool ShowScrollBar(IntPtr hWnd, int wBar, bool bShow);
}

internal sealed class BufferedPanel : Panel
{
    public BufferedPanel()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        ResizeRedraw = true;
    }
}

internal sealed class BufferedTableLayoutPanel : TableLayoutPanel
{
    public BufferedTableLayoutPanel()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        ResizeRedraw = true;
    }
}

internal sealed class BufferedLabel : Label
{
    public BufferedLabel()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        ResizeRedraw = true;
    }
}

internal sealed class AnimatedRatio
{
    private const double Epsilon = 0.0005;
    private double _start;
    private long _startedAt = Stopwatch.GetTimestamp();

    public double Target { get; private set; }
    public double Display { get; private set; }
    public bool IsActive => Math.Abs(Display - Target) > Epsilon;

    public void SetTarget(double value, int durationMs)
    {
        value = Clamp(value);
        Update(durationMs);
        if (Math.Abs(Target - value) <= Epsilon)
        {
            return;
        }

        Target = value;
        if (durationMs <= 0)
        {
            SnapTo(value);
            return;
        }

        _start = Display;
        _startedAt = Stopwatch.GetTimestamp();
    }

    public bool Update(int durationMs)
    {
        if (!IsActive)
        {
            return false;
        }

        var previous = Display;
        if (durationMs <= 0)
        {
            Display = Target;
            _start = Target;
            _startedAt = Stopwatch.GetTimestamp();
            return HasChanged(previous);
        }

        var elapsedMs = (Stopwatch.GetTimestamp() - _startedAt) * 1000.0 / Stopwatch.Frequency;
        var progress = Math.Clamp(elapsedMs / durationMs, 0, 1);
        if (progress >= 1)
        {
            Display = Target;
        }
        else
        {
            var eased = 1 - Math.Pow(1 - progress, 3);
            Display = _start + (Target - _start) * eased;
        }

        return HasChanged(previous);
    }

    public void SnapTo(double value)
    {
        Target = Clamp(value);
        Display = Target;
        _start = Target;
        _startedAt = Stopwatch.GetTimestamp();
    }

    private static double Clamp(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return 0;
        }

        return Math.Clamp(value, 0, 1);
    }

    private bool HasChanged(double previous) => Math.Abs(Display - previous) > Epsilon;
}

internal sealed class BufferedDataGridView : DataGridView
{
    public BufferedDataGridView()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }
}

internal sealed class RoundedPanel : Panel
{
    public int CornerRadius { get; set; } = 14;
    public Color BorderColor { get; set; } = AppTheme.Border;

    public RoundedPanel()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = AppTheme.Surface;
        Padding = new Padding(14);
        ResizeRedraw = true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var rect = ClientRectangle;
        rect.Width -= 1;
        rect.Height -= 1;

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var backdrop = new SolidBrush(Parent?.BackColor ?? AppTheme.Background);
        e.Graphics.FillRectangle(backdrop, ClientRectangle);

        using var path = RoundedPath(rect, CornerRadius);
        using var fill = new SolidBrush(BackColor);
        using var border = new Pen(BorderColor);
        e.Graphics.FillPath(fill, path);
        e.Graphics.DrawPath(border, path);
    }

    private static GraphicsPath RoundedPath(Rectangle bounds, int radius)
    {
        var diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}

internal sealed class RoundedButton : Button
{
    public Color FillColor { get; set; } = AppTheme.SurfaceRaised;
    public Color HoverColor { get; set; } = Color.FromArgb(39, 47, 61);
    public Color PressedColor { get; set; } = Color.FromArgb(48, 57, 73);
    public Color BorderColor { get; set; } = AppTheme.Border;
    public int CornerRadius { get; set; } = 10;

    private bool _hovered;
    private bool _pressed;

    public RoundedButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        ForeColor = AppTheme.Text;
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
        Height = Math.Max(38, Font.Height + 18);
        Margin = new Padding(6, 0, 0, 0);
        Padding = new Padding(10, 0, 10, 0);
    }

    protected override void OnFontChanged(EventArgs e)
    {
        Height = Math.Max(38, Font.Height + 18);
        base.OnFontChanged(e);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hovered = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hovered = false;
        _pressed = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs mevent)
    {
        _pressed = true;
        Invalidate();
        base.OnMouseDown(mevent);
    }

    protected override void OnMouseUp(MouseEventArgs mevent)
    {
        _pressed = false;
        Invalidate();
        base.OnMouseUp(mevent);
    }

    protected override void OnPaint(PaintEventArgs pevent)
    {
        pevent.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = ClientRectangle;
        rect.Width -= 1;
        rect.Height -= 1;

        using var backdrop = new SolidBrush(Parent?.BackColor ?? AppTheme.Background);
        pevent.Graphics.FillRectangle(backdrop, ClientRectangle);

        var color = _pressed ? PressedColor : _hovered ? HoverColor : FillColor;
        if (!Enabled)
        {
            color = Color.FromArgb(26, 31, 40);
        }

        using var path = RoundedPath(rect, CornerRadius);
        using var fill = new SolidBrush(color);
        using var border = new Pen(BorderColor);
        pevent.Graphics.FillPath(fill, path);
        pevent.Graphics.DrawPath(border, path);

        var textColor = Enabled ? ForeColor : AppTheme.MutedText;
        TextRenderer.DrawText(
            pevent.Graphics,
            Text,
            Font,
            Rectangle.Inflate(rect, -Padding.Left, 0),
            textColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    private static GraphicsPath RoundedPath(Rectangle bounds, int radius)
    {
        var diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}

internal sealed class MetricCard : Control
{
    private readonly System.Windows.Forms.Timer _animationTimer;
    private readonly AnimatedRatio _ratio = new();
    private int _smoothingDurationMs = 500;

    public string Title { get; set; } = string.Empty;
    public string ValueText { get; set; } = string.Empty;
    public string DetailText { get; set; } = string.Empty;
    public double Ratio
    {
        get => _ratio.Target;
        set
        {
            _ratio.SetTarget(value, SmoothingDurationMs);
            if (_ratio.IsActive)
            {
                StartAnimation();
            }

            Invalidate();
        }
    }

    public int SmoothingDurationMs
    {
        get => _smoothingDurationMs;
        set
        {
            _smoothingDurationMs = Math.Clamp(value, 0, 3000);
            if (_smoothingDurationMs == 0)
            {
                _ratio.SnapTo(_ratio.Target);
                _animationTimer.Stop();
            }

            Invalidate();
        }
    }

    public Color AccentColor { get; set; } = AppTheme.Accent;

    public MetricCard()
    {
        _animationTimer = new System.Windows.Forms.Timer { Interval = 16 };
        _animationTimer.Tick += (_, _) => AdvanceAnimation();
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        MinimumSize = new Size(160, 104);
        Margin = new Padding(0, 0, 10, 10);
        Font = new Font("Segoe UI", 9F);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _animationTimer.Dispose();
        }

        base.Dispose(disposing);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var rect = ClientRectangle;
        rect.Width -= 1;
        rect.Height -= 1;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var backdrop = new SolidBrush(Parent?.BackColor ?? AppTheme.Background);
        e.Graphics.FillRectangle(backdrop, ClientRectangle);

        using var path = RoundedPath(rect, 16);
        using var fill = new SolidBrush(AppTheme.Surface);
        using var border = new Pen(AppTheme.Border);
        e.Graphics.FillPath(fill, path);
        e.Graphics.DrawPath(border, path);

        var highDpi = DeviceDpi >= 144 || e.Graphics.DpiY >= 144 || Font.Height >= 20;
        var compact = highDpi || Height < 128 || Width < 220;
        var pad = compact ? Math.Min(12, Math.Max(8, Font.Height / 2)) : Math.Max(12, Font.Height);
        var inner = Rectangle.Inflate(rect, -pad, -pad);
        var barHeight = compact ? Math.Max(6, Math.Min(10, Font.Height / 3)) : Math.Max(6, Font.Height / 2);
        var barGap = compact ? Math.Max(4, Math.Min(8, Font.Height / 4)) : Math.Max(10, Font.Height / 2);
        var barRect = new Rectangle(inner.Left, inner.Bottom - barHeight, inner.Width, barHeight);
        var textBottom = Math.Max(inner.Top, barRect.Top - barGap);
        var textHeight = textBottom - inner.Top;

        if (compact)
        {
            DrawCompactLine(e.Graphics, inner, textHeight);
            DrawProgress(e.Graphics, barRect, _ratio.Display, AccentColor);
            return;
        }

        var titleHeight = TextRenderer.MeasureText(e.Graphics, Title, Font, Size.Empty, TextFormatFlags.NoPadding).Height + 2;
        var detailHeight = TextRenderer.MeasureText(e.Graphics, "Hg", Font, Size.Empty, TextFormatFlags.NoPadding).Height + 2;
        var titleGap = compact ? 2 : Math.Max(4, Font.Height / 4);
        var detailGap = compact ? 1 : Math.Max(2, Font.Height / 5);
        var minimumValueHeight = TextRenderer.MeasureText(e.Graphics, "Hg", Font, Size.Empty, TextFormatFlags.NoPadding).Height + 2;

        var showDetail = !string.IsNullOrWhiteSpace(DetailText)
            && textBottom - inner.Top >= titleHeight + titleGap + minimumValueHeight + detailGap + detailHeight;

        var valueFontSize = compact ? Font.Size + 2F : Font.Size + 5F;
        var availableValueHeight = textBottom - inner.Top - titleHeight - titleGap;
        if (showDetail)
        {
            availableValueHeight -= detailGap + detailHeight;
        }

        availableValueHeight = Math.Max(minimumValueHeight, availableValueHeight);
        using var valueFont = CreateFittingValueFont(e.Graphics, ValueText, valueFontSize, availableValueHeight);
        var valueHeight = TextRenderer.MeasureText(e.Graphics, ValueText, valueFont, Size.Empty, TextFormatFlags.NoPadding).Height + 4;
        var effectiveValueHeight = Math.Min(valueHeight, availableValueHeight);

        var y = inner.Top;
        var titleRect = new Rectangle(inner.Left, y, inner.Width, titleHeight);
        TextRenderer.DrawText(e.Graphics, Title, Font, titleRect, AppTheme.MutedText, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

        y += titleHeight + titleGap;
        var valueRect = new Rectangle(inner.Left, y, inner.Width, effectiveValueHeight);
        TextRenderer.DrawText(e.Graphics, ValueText, valueFont, valueRect, AppTheme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

        if (showDetail)
        {
            y += effectiveValueHeight + detailGap;
            var detailRect = new Rectangle(inner.Left, y, inner.Width, detailHeight);
            TextRenderer.DrawText(e.Graphics, DetailText, Font, detailRect, AppTheme.MutedText, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        }

        DrawProgress(e.Graphics, barRect, _ratio.Display, AccentColor);
    }

    private void StartAnimation()
    {
        if (!_animationTimer.Enabled)
        {
            _animationTimer.Start();
        }
    }

    private void AdvanceAnimation()
    {
        if (!_ratio.Update(SmoothingDurationMs))
        {
            if (!_ratio.IsActive)
            {
                _animationTimer.Stop();
            }

            return;
        }

        if (!_ratio.IsActive)
        {
            _animationTimer.Stop();
        }

        Invalidate();
    }

    private Font CreateFittingValueFont(Graphics graphics, string text, float startingSize, int availableHeight)
    {
        const float minimumSize = 7F;
        for (var size = startingSize; size > minimumSize; size -= 0.5F)
        {
            var candidate = new Font("Segoe UI", size, FontStyle.Bold);
            var measured = TextRenderer.MeasureText(graphics, text, candidate, Size.Empty, TextFormatFlags.NoPadding).Height + 4;
            if (measured <= availableHeight)
            {
                return candidate;
            }

            candidate.Dispose();
        }

        return new Font("Segoe UI", minimumSize, FontStyle.Bold);
    }

    private void DrawCompactLine(Graphics graphics, Rectangle inner, int availableHeight)
    {
        var primaryHeight = Math.Max(1, Math.Min(availableHeight, Font.Height + 6));
        var detailHeight = TextRenderer.MeasureText(graphics, "Hg", Font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Height + 2;
        var showDetail = !string.IsNullOrWhiteSpace(DetailText)
            && availableHeight >= primaryHeight + detailHeight + 2;

        if (showDetail)
        {
            primaryHeight = Math.Max(1, Math.Min(Font.Height + 2, availableHeight - detailHeight - 2));
        }

        var gap = Math.Max(8, Math.Min(18, inner.Width / 16));
        var valueWidth = Math.Max(inner.Width / 3, Math.Min(inner.Width / 2, TextRenderer.MeasureText(graphics, ValueText, Font, Size.Empty, TextFormatFlags.NoPadding).Width + gap));
        var titleWidth = Math.Max(1, inner.Width - valueWidth - gap);
        var titleRect = new Rectangle(inner.Left, inner.Top, titleWidth, primaryHeight);
        var valueRect = new Rectangle(inner.Right - valueWidth, inner.Top, valueWidth, primaryHeight);
        using var titleFont = CreateFittingCompactFont(graphics, Title, Font.Size, FontStyle.Regular, primaryHeight);
        using var valueFont = CreateFittingCompactFont(graphics, ValueText, Font.Size + 1F, FontStyle.Bold, primaryHeight);

        const TextFormatFlags flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
        TextRenderer.DrawText(graphics, Title, titleFont, titleRect, AppTheme.MutedText, flags | TextFormatFlags.Left);
        TextRenderer.DrawText(graphics, ValueText, valueFont, valueRect, AppTheme.Text, flags | TextFormatFlags.Right);

        if (showDetail)
        {
            var detailRect = new Rectangle(inner.Left, inner.Top + primaryHeight + 2, inner.Width, detailHeight);
            TextRenderer.DrawText(graphics, DetailText, Font, detailRect, AppTheme.MutedText, flags | TextFormatFlags.Left);
        }
    }

    private static Font CreateFittingCompactFont(Graphics graphics, string text, float startingSize, FontStyle style, int availableHeight)
    {
        const float minimumSize = 6F;
        for (var size = startingSize; size > minimumSize; size -= 0.5F)
        {
            var candidate = new Font("Segoe UI", size, style);
            var measured = TextRenderer.MeasureText(graphics, text, candidate, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Height + 2;
            if (measured <= availableHeight)
            {
                return candidate;
            }

            candidate.Dispose();
        }

        return new Font("Segoe UI", minimumSize, style);
    }

    internal static void DrawProgress(Graphics graphics, Rectangle bounds, double ratio, Color color)
    {
        ratio = Math.Min(1, Math.Max(0, ratio));
        using var backgroundPath = RoundedPath(bounds, bounds.Height / 2);
        using var background = new SolidBrush(Color.FromArgb(44, 52, 66));
        graphics.FillPath(background, backgroundPath);

        var fillWidth = Math.Max(bounds.Height, (int)Math.Round(bounds.Width * ratio));
        var fillBounds = new Rectangle(bounds.Left, bounds.Top, Math.Min(bounds.Width, fillWidth), bounds.Height);
        using var fillPath = RoundedPath(fillBounds, bounds.Height / 2);
        using var fill = new SolidBrush(color);
        graphics.FillPath(fill, fillPath);
    }

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
}

internal sealed class HostCard : Control
{
    private readonly System.Windows.Forms.Timer _animationTimer;
    private readonly AnimatedRatio _cpuRatio = new();
    private readonly AnimatedRatio _ramRatio = new();
    private readonly AnimatedRatio _gpuRatio = new();
    private readonly AnimatedRatio _vramRatio = new();
    private HostSnapshot? _snapshot;
    private int _smoothingDurationMs = 500;

    public HostSnapshot? Snapshot
    {
        get => _snapshot;
        set
        {
            _snapshot = value;
            UpdateRatioTargets();
            Invalidate();
        }
    }

    public bool IsSelected { get; set; }
    public int SmoothingDurationMs
    {
        get => _smoothingDurationMs;
        set
        {
            _smoothingDurationMs = Math.Clamp(value, 0, 3000);
            if (_smoothingDurationMs == 0)
            {
                SnapRatiosToTargets();
                _animationTimer.Stop();
            }
            else
            {
                UpdateRatioTargets();
            }

            Invalidate();
        }
    }

    public HostCard()
    {
        _animationTimer = new System.Windows.Forms.Timer { Interval = 16 };
        _animationTimer.Tick += (_, _) => AdvanceAnimations();
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Cursor = Cursors.Hand;
        Width = 330;
        Height = 208;
        Margin = new Padding(0, 0, 0, 12);
        Font = new Font("Segoe UI", 9F);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _animationTimer.Dispose();
        }

        base.Dispose(disposing);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var snapshot = Snapshot;
        if (snapshot is null)
        {
            using var emptyBackdrop = new SolidBrush(Parent?.BackColor ?? AppTheme.Surface);
            e.Graphics.FillRectangle(emptyBackdrop, ClientRectangle);
            return;
        }

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = ClientRectangle;
        rect.Width -= 1;
        rect.Height -= 1;

        using var backdrop = new SolidBrush(Parent?.BackColor ?? AppTheme.Surface);
        e.Graphics.FillRectangle(backdrop, ClientRectangle);

        using var path = RoundedPath(rect, 16);
        using var fill = new SolidBrush(IsSelected ? Color.FromArgb(30, 42, 59) : AppTheme.Surface);
        using var border = new Pen(IsSelected ? AppTheme.Accent : AppTheme.Border, IsSelected ? 2 : 1);
        e.Graphics.FillPath(fill, path);
        e.Graphics.DrawPath(border, path);

        var compact = Height < 220 || Width < 240;
        var pad = compact ? Math.Max(10, Font.Height / 2) : Math.Max(12, Font.Height);
        var inner = Rectangle.Inflate(rect, -pad, -pad);
        using var titleFont = new Font("Segoe UI", compact ? 9F : 10F, FontStyle.Bold);
        var titleHeight = TextRenderer.MeasureText(e.Graphics, snapshot.DisplayName, titleFont, Size.Empty, TextFormatFlags.NoPadding).Height + 2;
        var statusHeight = TextRenderer.MeasureText(e.Graphics, "Hg", Font, Size.Empty, TextFormatFlags.NoPadding).Height + 2;
        TextRenderer.DrawText(e.Graphics, snapshot.DisplayName, titleFont, new Rectangle(inner.Left, inner.Top, inner.Width, titleHeight), AppTheme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        TextRenderer.DrawText(e.Graphics, snapshot.Status, Font, new Rectangle(inner.Left, inner.Top + titleHeight, inner.Width, statusHeight), AppTheme.MutedText, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

        var lineHeight = compact ? Math.Max(Font.Height + 3, 22) : Math.Max(Font.Height + 8, 26);
        var y = inner.Top + titleHeight + statusHeight + (compact ? 4 : Math.Max(10, Font.Height / 2));
        DrawMetricLine(e.Graphics, "CPU", _cpuRatio.Display, Formatters.Percent(snapshot.CpuPercent), y, lineHeight, AppTheme.Accent);
        DrawMetricLine(e.Graphics, "RAM", _ramRatio.Display, MemoryLine(snapshot.RamUsedBytes, snapshot.RamTotalBytes), y + lineHeight, lineHeight, AppTheme.Good);
        DrawMetricLine(e.Graphics, "GPU", _gpuRatio.Display, Formatters.Percent(snapshot.GpuPercent), y + lineHeight * 2, lineHeight, AppTheme.Warning);
        DrawMetricLine(e.Graphics, "VRAM", _vramRatio.Display, MemoryLine(snapshot.VramUsedBytes, snapshot.VramTotalBytes), y + lineHeight * 3, lineHeight, AppTheme.Danger);
    }

    private void UpdateRatioTargets()
    {
        if (_snapshot is null)
        {
            _cpuRatio.SnapTo(0);
            _ramRatio.SnapTo(0);
            _gpuRatio.SnapTo(0);
            _vramRatio.SnapTo(0);
            _animationTimer.Stop();
            return;
        }

        _cpuRatio.SetTarget(_snapshot.CpuPercent / 100, SmoothingDurationMs);
        _ramRatio.SetTarget(Formatters.Ratio(_snapshot.RamUsedBytes, _snapshot.RamTotalBytes), SmoothingDurationMs);
        _gpuRatio.SetTarget(_snapshot.GpuPercent / 100, SmoothingDurationMs);
        _vramRatio.SetTarget(Formatters.Ratio(_snapshot.VramUsedBytes, _snapshot.VramTotalBytes), SmoothingDurationMs);

        if (_cpuRatio.IsActive || _ramRatio.IsActive || _gpuRatio.IsActive || _vramRatio.IsActive)
        {
            StartAnimation();
        }
    }

    private void SnapRatiosToTargets()
    {
        _cpuRatio.SnapTo(_cpuRatio.Target);
        _ramRatio.SnapTo(_ramRatio.Target);
        _gpuRatio.SnapTo(_gpuRatio.Target);
        _vramRatio.SnapTo(_vramRatio.Target);
    }

    private void StartAnimation()
    {
        if (!_animationTimer.Enabled)
        {
            _animationTimer.Start();
        }
    }

    private void AdvanceAnimations()
    {
        var changed = _cpuRatio.Update(SmoothingDurationMs)
            | _ramRatio.Update(SmoothingDurationMs)
            | _gpuRatio.Update(SmoothingDurationMs)
            | _vramRatio.Update(SmoothingDurationMs);

        if (!_cpuRatio.IsActive && !_ramRatio.IsActive && !_gpuRatio.IsActive && !_vramRatio.IsActive)
        {
            _animationTimer.Stop();
        }

        if (changed)
        {
            Invalidate();
        }
    }

    private static string MemoryLine(long usedBytes, long totalBytes)
    {
        if (totalBytes <= 0)
        {
            return $"{Formatters.BytesPrecise(usedBytes)} used";
        }

        var overBytes = Math.Max(0, usedBytes - totalBytes);
        if (overBytes > 0)
        {
            return $"{Formatters.BytesPrecise(usedBytes)} used, {Formatters.BytesPrecise(overBytes)} over";
        }

        return $"{Formatters.BytesPrecise(usedBytes)} used, {Formatters.BytesPrecise(totalBytes - usedBytes)} free";
    }

    private void DrawMetricLine(Graphics graphics, string label, double ratio, string value, int y, int lineHeight, Color color)
    {
        var labelWidth = Math.Max(48, TextRenderer.MeasureText(label, Font).Width + 4);
        var valueWidth = Math.Min(Math.Max(86, Width / 3), Math.Max(90, Width - labelWidth - 110));
        var left = Math.Max(12, Font.Height);
        var barHeight = Math.Max(8, Font.Height / 2);
        var labelRect = new Rectangle(left, y, labelWidth, lineHeight);
        TextRenderer.DrawText(graphics, label, Font, labelRect, AppTheme.MutedText, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

        var barLeft = left + labelWidth + 8;
        var barRight = Width - valueWidth - left - 8;
        var barRect = new Rectangle(barLeft, y + (lineHeight - barHeight) / 2, Math.Max(36, barRight - barLeft), barHeight);
        MetricCard.DrawProgress(graphics, barRect, ratio, color);

        var valueRect = new Rectangle(barRect.Right + 8, y, Width - barRect.Right - left - 8, lineHeight);
        TextRenderer.DrawText(graphics, value, Font, valueRect, AppTheme.Text, TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
    }

    private static GraphicsPath RoundedPath(Rectangle bounds, int radius)
    {
        var diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}

internal static class AppIconFactory
{
    public static Icon CreateIcon()
    {
        using var bitmap = new Bitmap(64, 64);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);

        using var background = new LinearGradientBrush(
            new Rectangle(0, 0, 64, 64),
            Color.FromArgb(88, 166, 255),
            Color.FromArgb(69, 214, 136),
            LinearGradientMode.ForwardDiagonal);
        using var border = new Pen(Color.FromArgb(235, 241, 247), 3);
        using var path = RoundedPath(new Rectangle(4, 4, 56, 56), 14);
        graphics.FillPath(background, path);
        graphics.DrawPath(border, path);

        using var textFont = new Font("Segoe UI", 24F, FontStyle.Bold, GraphicsUnit.Pixel);
        TextRenderer.DrawText(
            graphics,
            "V",
            textFont,
            new Rectangle(0, 6, 64, 48),
            Color.FromArgb(13, 17, 23),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

        var handle = bitmap.GetHicon();
        try
        {
            return (Icon)Icon.FromHandle(handle).Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    private static GraphicsPath RoundedPath(Rectangle bounds, int radius)
    {
        var diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
