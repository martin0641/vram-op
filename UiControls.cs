using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace VramOp;

internal static class AppTheme
{
    public static readonly IReadOnlyList<ThemeColorSlot> ColorSlots =
    [
        new("Background", "Background", Color.FromArgb(13, 17, 23)),
        new("Surface", "Surface", Color.FromArgb(22, 27, 34)),
        new("SurfaceRaised", "Raised", Color.FromArgb(30, 36, 46)),
        new("Text", "Text", Color.FromArgb(235, 241, 247)),
        new("Accent", "CPU/accent", Color.FromArgb(88, 166, 255)),
        new("Good", "RAM", Color.FromArgb(69, 214, 136)),
        new("Warning", "GPU", Color.FromArgb(255, 197, 92)),
        new("Danger", "VRAM", Color.FromArgb(255, 107, 107)),
        new("NetworkReceive", "Net receive", Color.FromArgb(94, 211, 243)),
        new("NetworkSend", "Net send", Color.FromArgb(199, 125, 255))
    ];

    public static Color Background { get; private set; } = GetDefaultColor("Background");
    public static Color Surface { get; private set; } = GetDefaultColor("Surface");
    public static Color SurfaceRaised { get; private set; } = GetDefaultColor("SurfaceRaised");
    public static Color Border { get; private set; } = Color.FromArgb(48, 56, 70);
    public static Color Text { get; private set; } = GetDefaultColor("Text");
    public static Color MutedText { get; private set; } = Color.FromArgb(145, 156, 172);
    public static Color Accent { get; private set; } = GetDefaultColor("Accent");
    public static Color Good { get; private set; } = GetDefaultColor("Good");
    public static Color Warning { get; private set; } = GetDefaultColor("Warning");
    public static Color Danger { get; private set; } = GetDefaultColor("Danger");
    public static Color NetworkReceive { get; private set; } = GetDefaultColor("NetworkReceive");
    public static Color NetworkSend { get; private set; } = GetDefaultColor("NetworkSend");

    public static void Apply(IReadOnlyDictionary<string, string>? colors)
    {
        Reset();
        if (colors is null)
        {
            return;
        }

        foreach (var (key, value) in colors)
        {
            if (TryParseHex(value, out var color))
            {
                SetColor(key, color);
            }
        }
    }

    public static void SetColor(string key, Color color)
    {
        key = ColorSlots.FirstOrDefault(item => string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase))?.Key ?? key;
        switch (key)
        {
            case "Background":
                Background = color;
                break;
            case "Surface":
                Surface = color;
                break;
            case "SurfaceRaised":
                SurfaceRaised = color;
                break;
            case "Text":
                Text = color;
                break;
            case "Accent":
                Accent = color;
                break;
            case "Good":
                Good = color;
                break;
            case "Warning":
                Warning = color;
                break;
            case "Danger":
                Danger = color;
                break;
            case "NetworkReceive":
                NetworkReceive = color;
                break;
            case "NetworkSend":
                NetworkSend = color;
                break;
        }

        Border = Mix(SurfaceRaised, Text, 0.16);
        MutedText = Mix(Text, Surface, 0.58);
    }

    public static Color GetColor(string key) =>
        key switch
        {
            "Background" => Background,
            "Surface" => Surface,
            "SurfaceRaised" => SurfaceRaised,
            "Text" => Text,
            "Accent" => Accent,
            "Good" => Good,
            "Warning" => Warning,
            "Danger" => Danger,
            "NetworkReceive" => NetworkReceive,
            "NetworkSend" => NetworkSend,
            _ => GetDefaultColor(key)
        };

    public static string ToHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    private static void Reset()
    {
        Background = GetDefaultColor("Background");
        Surface = GetDefaultColor("Surface");
        SurfaceRaised = GetDefaultColor("SurfaceRaised");
        Text = GetDefaultColor("Text");
        Accent = GetDefaultColor("Accent");
        Good = GetDefaultColor("Good");
        Warning = GetDefaultColor("Warning");
        Danger = GetDefaultColor("Danger");
        NetworkReceive = GetDefaultColor("NetworkReceive");
        NetworkSend = GetDefaultColor("NetworkSend");
        Border = Color.FromArgb(48, 56, 70);
        MutedText = Color.FromArgb(145, 156, 172);
    }

    private static Color GetDefaultColor(string key) =>
        ColorSlots.FirstOrDefault(item => string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase))?.DefaultColor
        ?? Color.FromArgb(22, 27, 34);

    private static bool TryParseHex(string value, out Color color)
    {
        value = value.Trim().TrimStart('#');
        if (value.Length != 6 || !int.TryParse(value, System.Globalization.NumberStyles.HexNumber, null, out var rgb))
        {
            color = Color.Empty;
            return false;
        }

        color = Color.FromArgb((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
        return true;
    }

    private static Color Mix(Color foreground, Color background, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        return Color.FromArgb(
            (int)Math.Round(foreground.R * amount + background.R * (1 - amount)),
            (int)Math.Round(foreground.G * amount + background.G * (1 - amount)),
            (int)Math.Round(foreground.B * amount + background.B * (1 - amount)));
    }
}

internal sealed record ThemeColorSlot(string Key, string Label, Color DefaultColor);

internal static class NativeWindowStyler
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaUseImmersiveDarkModeBefore20h1 = 19;
    private const int DwmwaCaptionColor = 35;
    private const int DwmwaTextColor = 36;

    public static void ApplyDarkTitleBar(Form form)
    {
        if (!OperatingSystem.IsWindows() || !form.IsHandleCreated)
        {
            return;
        }

        var enabled = 1;
        _ = DwmSetWindowAttribute(form.Handle, DwmwaUseImmersiveDarkMode, ref enabled, sizeof(int));
        _ = DwmSetWindowAttribute(form.Handle, DwmwaUseImmersiveDarkModeBefore20h1, ref enabled, sizeof(int));

        var captionColor = ToColorRef(AppTheme.Background);
        _ = DwmSetWindowAttribute(form.Handle, DwmwaCaptionColor, ref captionColor, sizeof(int));

        var textColor = ToColorRef(AppTheme.Text);
        _ = DwmSetWindowAttribute(form.Handle, DwmwaTextColor, ref textColor, sizeof(int));
    }

    public static void ApplyDarkScrollBars(Control control)
    {
        if (!OperatingSystem.IsWindows() || !control.IsHandleCreated)
        {
            return;
        }

        _ = SetWindowTheme(control.Handle, "DarkMode_Explorer", null);
    }

    private static int ToColorRef(Color color) => color.R | (color.G << 8) | (color.B << 16);

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int attributeValue, int attributeSize);

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SetWindowTheme(nint hWnd, string? pszSubAppName, string? pszSubIdList);
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
        NativeWindowStyler.ApplyDarkScrollBars(this);
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

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        NativeWindowStyler.ApplyDarkScrollBars(this);
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

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        NativeWindowStyler.ApplyDarkScrollBars(this);
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

internal sealed class ColorSwatchButton : Button
{
    private bool _hovered;

    public Color SwatchColor { get; set; } = AppTheme.Accent;

    public Color BorderColor { get; set; } = AppTheme.Border;

    public Color BackdropColor { get; set; } = AppTheme.Surface;

    public Color RingColor { get; set; } = AppTheme.Text;

    public ColorSwatchButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
        Size = new Size(36, 36);
        Margin = new Padding(0, 0, 10, 10);
        TabStop = true;
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
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnPaint(PaintEventArgs pevent)
    {
        pevent.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var backdrop = new SolidBrush(Parent?.BackColor ?? BackdropColor);
        pevent.Graphics.FillRectangle(backdrop, ClientRectangle);

        var circleSize = Math.Min(ClientSize.Width, ClientSize.Height) - 8;
        var rect = new Rectangle(
            (ClientSize.Width - circleSize) / 2,
            (ClientSize.Height - circleSize) / 2,
            circleSize,
            circleSize);

        using var fill = new SolidBrush(SwatchColor);
        using var border = new Pen(BorderColor, _hovered || Focused ? 2 : 1);
        pevent.Graphics.FillEllipse(fill, rect);
        pevent.Graphics.DrawEllipse(border, rect);

        if (_hovered || Focused)
        {
            var focusRect = Rectangle.Inflate(rect, 4, 4);
            using var ring = new Pen(Color.FromArgb(120, RingColor), 1);
            pevent.Graphics.DrawEllipse(ring, focusRect);
        }
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
            _smoothingDurationMs = Math.Clamp(value, 0, 6000);
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
    private readonly AnimatedRatio[] _networkReceiveRatios = [new(), new(), new(), new()];
    private readonly AnimatedRatio[] _networkSendRatios = [new(), new(), new(), new()];
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
    public bool UseCompactMemoryValues { get; set; }
    public bool ShowResizeGrip { get; set; }
    public bool ShowNetworkBars { get; set; } = true;
    public NetworkRateUnit NetworkUnit { get; set; } = NetworkRateUnit.Mbps;

    public int SmoothingDurationMs
    {
        get => _smoothingDurationMs;
        set
        {
            _smoothingDurationMs = Math.Clamp(value, 0, 6000);
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
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.StandardClick | ControlStyles.StandardDoubleClick | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
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
        var fillColor = IsSelected ? Color.FromArgb(30, 42, 59) : AppTheme.Surface;
        using var fill = new SolidBrush(fillColor);
        using var border = new Pen(IsSelected ? AppTheme.Accent : AppTheme.Border, IsSelected ? 2 : 1);
        e.Graphics.FillPath(fill, path);
        e.Graphics.DrawPath(border, path);

        var networkRows = snapshot.NetworkInterfaces.Take(4).ToArray();
        var compact = Height < 220 || Width < 240;
        var pad = compact ? Math.Max(10, Font.Height / 2) : Math.Max(12, Font.Height);
        var inner = Rectangle.Inflate(rect, -pad, -pad);
        using var titleFont = new Font(Font.FontFamily, Font.Size + (compact ? 0.5F : 1.5F), FontStyle.Bold);
        var titleHeight = TextRenderer.MeasureText(e.Graphics, snapshot.DisplayName, titleFont, Size.Empty, TextFormatFlags.NoPadding).Height + 2;
        var statusHeight = TextRenderer.MeasureText(e.Graphics, "Hg", Font, Size.Empty, TextFormatFlags.NoPadding).Height + 2;
        TextRenderer.DrawText(e.Graphics, snapshot.DisplayName, titleFont, new Rectangle(inner.Left, inner.Top, inner.Width, titleHeight), AppTheme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        TextRenderer.DrawText(e.Graphics, snapshot.Status, Font, new Rectangle(inner.Left, inner.Top + titleHeight, inner.Width, statusHeight), AppTheme.MutedText, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

        var y = inner.Top + titleHeight + statusHeight + (compact ? 4 : Math.Max(10, Font.Height / 2));
        var totalLines = 4 + networkRows.Length;
        var availableLineHeight = totalLines <= 0
            ? 0
            : Math.Max(Font.Height + 3, (inner.Bottom - y - (ShowResizeGrip ? Math.Max(12, Font.Height) : 0)) / totalLines);
        var desiredLineHeight = compact ? Math.Max(Font.Height + 3, 22) : Math.Max(Font.Height + 8, 26);
        var lineHeight = Math.Max(Font.Height + 3, Math.Min(desiredLineHeight, availableLineHeight));
        DrawMetricLine(e.Graphics, "CPU", _cpuRatio.Display, Formatters.Percent(snapshot.CpuPercent), y, lineHeight, AppTheme.Accent);
        DrawMetricLine(e.Graphics, "RAM", _ramRatio.Display, MemoryLine(snapshot.RamUsedBytes, snapshot.RamTotalBytes), y + lineHeight, lineHeight, AppTheme.Good);
        DrawMetricLine(e.Graphics, "GPU", _gpuRatio.Display, Formatters.Percent(snapshot.GpuPercent), y + lineHeight * 2, lineHeight, AppTheme.Warning);
        DrawMetricLine(e.Graphics, "VRAM", _vramRatio.Display, MemoryLine(snapshot.VramUsedBytes, snapshot.VramTotalBytes), y + lineHeight * 3, lineHeight, AppTheme.Danger);

        for (var index = 0; index < networkRows.Length; index++)
        {
            var network = networkRows[index];
            if (ShowNetworkBars)
            {
                DrawNetworkMetricLine(
                    e.Graphics,
                    network.Label,
                    _networkReceiveRatios[index].Display,
                    _networkSendRatios[index].Display,
                    NetworkRateFormatter.FormatPair(network.ReceiveBytesPerSecond, network.SendBytesPerSecond, NetworkUnit),
                    y + lineHeight * (4 + index),
                    lineHeight);
            }
            else
            {
                DrawNetworkTextLine(
                    e.Graphics,
                    network.Label,
                    NetworkRateFormatter.FormatWidgetPair(network.ReceiveBytesPerSecond, network.SendBytesPerSecond, NetworkUnit),
                    y + lineHeight * (4 + index),
                    lineHeight);
            }
        }

        if (ShowResizeGrip)
        {
            DrawResizeGrip(e.Graphics, rect, fillColor);
        }
    }

    private void UpdateRatioTargets()
    {
        if (_snapshot is null)
        {
            _cpuRatio.SnapTo(0);
            _ramRatio.SnapTo(0);
            _gpuRatio.SnapTo(0);
            _vramRatio.SnapTo(0);
            foreach (var ratio in _networkReceiveRatios.Concat(_networkSendRatios))
            {
                ratio.SnapTo(0);
            }

            _animationTimer.Stop();
            return;
        }

        _cpuRatio.SetTarget(_snapshot.CpuPercent / 100, SmoothingDurationMs);
        _ramRatio.SetTarget(Formatters.Ratio(_snapshot.RamUsedBytes, _snapshot.RamTotalBytes), SmoothingDurationMs);
        _gpuRatio.SetTarget(_snapshot.GpuPercent / 100, SmoothingDurationMs);
        _vramRatio.SetTarget(Formatters.Ratio(_snapshot.VramUsedBytes, _snapshot.VramTotalBytes), SmoothingDurationMs);
        var networkRows = _snapshot.NetworkInterfaces.Take(4).ToArray();
        for (var index = 0; index < _networkReceiveRatios.Length; index++)
        {
            if (index < networkRows.Length)
            {
                if (ShowNetworkBars)
                {
                    var network = networkRows[index];
                    _networkReceiveRatios[index].SetTarget(NetworkRateFormatter.RatioToLink(network.ReceiveBytesPerSecond, network.LinkSpeedBitsPerSecond), SmoothingDurationMs);
                    _networkSendRatios[index].SetTarget(NetworkRateFormatter.RatioToLink(network.SendBytesPerSecond, network.LinkSpeedBitsPerSecond), SmoothingDurationMs);
                }
                else
                {
                    _networkReceiveRatios[index].SnapTo(0);
                    _networkSendRatios[index].SnapTo(0);
                }
            }
            else
            {
                _networkReceiveRatios[index].SetTarget(0, SmoothingDurationMs);
                _networkSendRatios[index].SetTarget(0, SmoothingDurationMs);
            }
        }

        if (HasActiveAnimations())
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
        foreach (var ratio in _networkReceiveRatios.Concat(_networkSendRatios))
        {
            ratio.SnapTo(ratio.Target);
        }
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

        foreach (var ratio in _networkReceiveRatios.Concat(_networkSendRatios))
        {
            changed |= ratio.Update(SmoothingDurationMs);
        }

        if (!HasActiveAnimations())
        {
            _animationTimer.Stop();
        }

        if (changed)
        {
            Invalidate();
        }
    }

    private bool HasActiveAnimations() =>
        _cpuRatio.IsActive
        || _ramRatio.IsActive
        || _gpuRatio.IsActive
        || _vramRatio.IsActive
        || _networkReceiveRatios.Any(ratio => ratio.IsActive)
        || _networkSendRatios.Any(ratio => ratio.IsActive);

    private string MemoryLine(long usedBytes, long totalBytes)
    {
        if (UseCompactMemoryValues)
        {
            return CompactMemoryLine(usedBytes, totalBytes);
        }

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

    private static string CompactMemoryLine(long usedBytes, long totalBytes)
    {
        if (totalBytes <= 0)
        {
            return $"{BytesAsGb(usedBytes)}GB";
        }

        return $"{BytesAsGb(usedBytes)}/{BytesAsGb(totalBytes)}GB";
    }

    private static string BytesAsGb(long bytes) => $"{bytes / 1024D / 1024D / 1024D:N2}";

    private void DrawMetricLine(Graphics graphics, string label, double ratio, string value, int y, int lineHeight, Color color)
    {
        var labelWidth = Math.Max(52, TextRenderer.MeasureText("VRAM", Font).Width + 4);
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

    private void DrawNetworkMetricLine(Graphics graphics, string label, double receiveRatio, double sendRatio, string value, int y, int lineHeight)
    {
        var labelWidth = Math.Max(52, TextRenderer.MeasureText("NIC4", Font).Width + 4);
        var valueWidth = Math.Min(Math.Max(112, Width / 3), Math.Max(112, Width - labelWidth - 110));
        var left = Math.Max(12, Font.Height);
        var barHeight = Math.Max(8, Font.Height / 2);
        var labelRect = new Rectangle(left, y, labelWidth, lineHeight);
        TextRenderer.DrawText(graphics, label, Font, labelRect, AppTheme.MutedText, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

        var barLeft = left + labelWidth + 8;
        var barRight = Width - valueWidth - left - 8;
        var barRect = new Rectangle(barLeft, y + (lineHeight - barHeight) / 2, Math.Max(36, barRight - barLeft), barHeight);
        DrawDuplexProgress(graphics, barRect, receiveRatio, sendRatio);

        var valueRect = new Rectangle(barRect.Right + 8, y, Width - barRect.Right - left - 8, lineHeight);
        TextRenderer.DrawText(graphics, value, Font, valueRect, AppTheme.Text, TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
    }

    private void DrawNetworkTextLine(Graphics graphics, string label, string value, int y, int lineHeight)
    {
        var labelWidth = Math.Max(52, TextRenderer.MeasureText("NIC4", Font).Width + 4);
        var left = Math.Max(12, Font.Height);
        var labelRect = new Rectangle(left, y, labelWidth, lineHeight);
        TextRenderer.DrawText(graphics, label, Font, labelRect, AppTheme.MutedText, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

        var valueRect = new Rectangle(labelRect.Right + 8, y, Width - labelRect.Right - left - 8, lineHeight);
        TextRenderer.DrawText(graphics, value, Font, valueRect, AppTheme.Text, TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
    }

    private static void DrawDuplexProgress(Graphics graphics, Rectangle bounds, double receiveRatio, double sendRatio)
    {
        receiveRatio = Math.Min(1, Math.Max(0, receiveRatio));
        sendRatio = Math.Min(1, Math.Max(0, sendRatio));
        using var backgroundPath = RoundedPath(bounds, bounds.Height / 2);
        using var background = new SolidBrush(Color.FromArgb(44, 52, 66));
        graphics.FillPath(background, backgroundPath);

        if (receiveRatio > 0)
        {
            var receiveWidth = Math.Max(bounds.Height, (int)Math.Round(bounds.Width * receiveRatio));
            var receiveBounds = new Rectangle(bounds.Left, bounds.Top, Math.Min(bounds.Width, receiveWidth), bounds.Height);
            using var receivePath = RoundedPath(receiveBounds, bounds.Height / 2);
            using var receiveFill = new SolidBrush(Color.FromArgb(180, AppTheme.NetworkReceive));
            graphics.FillPath(receiveFill, receivePath);
        }

        if (sendRatio > 0)
        {
            var sendWidth = Math.Max(bounds.Height, (int)Math.Round(bounds.Width * sendRatio));
            var clampedWidth = Math.Min(bounds.Width, sendWidth);
            var sendBounds = new Rectangle(bounds.Right - clampedWidth, bounds.Top, clampedWidth, bounds.Height);
            using var sendPath = RoundedPath(sendBounds, bounds.Height / 2);
            using var sendFill = new SolidBrush(Color.FromArgb(180, AppTheme.NetworkSend));
            graphics.FillPath(sendFill, sendPath);
        }
    }

    private void DrawResizeGrip(Graphics graphics, Rectangle bounds, Color baseColor)
    {
        var size = Math.Max(18, Font.Height + 8);
        var inset = Math.Max(3, Font.Height / 4);
        var right = bounds.Right - inset;
        var bottom = bounds.Bottom - inset;
        var points = new[]
        {
            new Point(right - size, bottom),
            new Point(right, bottom - size),
            new Point(right, bottom)
        };

        using var fill = new SolidBrush(Darken(baseColor, 0.76));
        graphics.FillPolygon(fill, points);
    }

    private static Color Darken(Color color, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        return Color.FromArgb(
            color.A,
            (int)Math.Round(color.R * amount),
            (int)Math.Round(color.G * amount),
            (int)Math.Round(color.B * amount));
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
