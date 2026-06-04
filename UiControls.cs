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
    public BufferedFlowLayoutPanel()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        ResizeRedraw = true;
    }
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
    public string Title { get; set; } = string.Empty;
    public string ValueText { get; set; } = string.Empty;
    public string DetailText { get; set; } = string.Empty;
    public double Ratio { get; set; }
    public Color AccentColor { get; set; } = AppTheme.Accent;

    public MetricCard()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        MinimumSize = new Size(180, 132);
        Margin = new Padding(0, 0, 10, 10);
        Font = new Font("Segoe UI", 9F);
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

        var pad = Math.Max(12, Font.Height);
        var inner = Rectangle.Inflate(rect, -pad, -pad);
        var titleHeight = TextRenderer.MeasureText(e.Graphics, Title, Font, Size.Empty, TextFormatFlags.NoPadding).Height + 2;
        var detailHeight = TextRenderer.MeasureText(e.Graphics, "Hg", Font, Size.Empty, TextFormatFlags.NoPadding).Height + 2;
        var barHeight = Math.Max(8, Font.Height / 2);
        var barGap = Math.Max(10, Font.Height / 2);

        using var valueFont = new Font("Segoe UI", Font.Size + 5F, FontStyle.Bold);
        var valueHeight = TextRenderer.MeasureText(e.Graphics, ValueText, valueFont, Size.Empty, TextFormatFlags.NoPadding).Height + 4;

        var y = inner.Top;
        var titleRect = new Rectangle(inner.Left, y, inner.Width, titleHeight);
        TextRenderer.DrawText(e.Graphics, Title, Font, titleRect, AppTheme.MutedText, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

        y += titleHeight + Math.Max(4, Font.Height / 4);
        var valueRect = new Rectangle(inner.Left, y, inner.Width, valueHeight);
        TextRenderer.DrawText(e.Graphics, ValueText, valueFont, valueRect, AppTheme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

        y += valueHeight + Math.Max(2, Font.Height / 5);
        var detailRect = new Rectangle(inner.Left, y, inner.Width, detailHeight);
        TextRenderer.DrawText(e.Graphics, DetailText, Font, detailRect, AppTheme.MutedText, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

        var barRect = new Rectangle(inner.Left, Math.Max(y + detailHeight + barGap, inner.Bottom - barHeight), inner.Width, barHeight);
        DrawProgress(e.Graphics, barRect, Ratio, AccentColor);
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
    public HostSnapshot? Snapshot { get; set; }
    public bool IsSelected { get; set; }

    public HostCard()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Cursor = Cursors.Hand;
        Width = 330;
        Height = 208;
        Margin = new Padding(0, 0, 0, 12);
        Font = new Font("Segoe UI", 9F);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (Snapshot is null)
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
        var titleHeight = TextRenderer.MeasureText(e.Graphics, Snapshot.DisplayName, titleFont, Size.Empty, TextFormatFlags.NoPadding).Height + 2;
        var statusHeight = TextRenderer.MeasureText(e.Graphics, "Hg", Font, Size.Empty, TextFormatFlags.NoPadding).Height + 2;
        TextRenderer.DrawText(e.Graphics, Snapshot.DisplayName, titleFont, new Rectangle(inner.Left, inner.Top, inner.Width, titleHeight), AppTheme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        TextRenderer.DrawText(e.Graphics, Snapshot.Status, Font, new Rectangle(inner.Left, inner.Top + titleHeight, inner.Width, statusHeight), AppTheme.MutedText, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

        var lineHeight = compact ? Math.Max(Font.Height + 3, 22) : Math.Max(Font.Height + 8, 26);
        var y = inner.Top + titleHeight + statusHeight + (compact ? 4 : Math.Max(10, Font.Height / 2));
        DrawMetricLine(e.Graphics, "CPU", Snapshot.CpuPercent / 100, Formatters.Percent(Snapshot.CpuPercent), y, lineHeight, AppTheme.Accent);
        DrawMetricLine(e.Graphics, "RAM", Formatters.Ratio(Snapshot.RamUsedBytes, Snapshot.RamTotalBytes), $"{Formatters.Bytes(Snapshot.RamUsedBytes)} / {Formatters.Bytes(Snapshot.RamTotalBytes)}", y + lineHeight, lineHeight, AppTheme.Good);
        DrawMetricLine(e.Graphics, "GPU", Snapshot.GpuPercent / 100, Formatters.Percent(Snapshot.GpuPercent), y + lineHeight * 2, lineHeight, AppTheme.Warning);
        DrawMetricLine(e.Graphics, "VRAM", Formatters.Ratio(Snapshot.VramUsedBytes, Snapshot.VramTotalBytes), $"{Formatters.Bytes(Snapshot.VramUsedBytes)} / {Formatters.Bytes(Snapshot.VramTotalBytes)}", y + lineHeight * 3, lineHeight, AppTheme.Danger);
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
