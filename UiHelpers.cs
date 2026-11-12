using System.Drawing.Drawing2D;

namespace ExpansorTexto;

internal static class GraphicsExtensions
{
    public static void FillRoundedRectangle(this Graphics graphics, Brush brush, Rectangle bounds, int radius)
    {
        using var path = RoundedPath(bounds, radius);
        graphics.FillPath(brush, path);
    }

    public static void DrawRoundedRectangle(this Graphics graphics, Pen pen, Rectangle bounds, int radius)
    {
        using var path = RoundedPath(bounds, radius);
        graphics.DrawPath(pen, path);
    }

    private static GraphicsPath RoundedPath(Rectangle bounds, int radius)
    {
        int d = Math.Max(1, Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height)));
        var path = new GraphicsPath();
        path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}

internal enum IconKind { Plus, Check, Trash, Pencil, Search, Keyboard, Close }

/// <summary>Ícones simples desenhados em tempo de execução (não dependem de fontes de símbolos).</summary>
internal static class VectorIcons
{
    public static Bitmap Create(IconKind kind, Color color, int size)
    {
        var bitmap = new Bitmap(size, size);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.ScaleTransform(size / 16F, size / 16F);
        using var pen = new Pen(color, 1.5F) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        using var brush = new SolidBrush(color);

        switch (kind)
        {
            case IconKind.Plus:
                g.DrawLine(pen, 8F, 3F, 8F, 13F);
                g.DrawLine(pen, 3F, 8F, 13F, 8F);
                break;
            case IconKind.Close:
                g.DrawLine(pen, 4F, 4F, 12F, 12F);
                g.DrawLine(pen, 12F, 4F, 4F, 12F);
                break;
            case IconKind.Check:
                g.DrawLines(pen, new[] { new PointF(3F, 8.5F), new PointF(6.5F, 12F), new PointF(13F, 4.5F) });
                break;
            case IconKind.Trash:
                g.DrawLine(pen, 3F, 4.5F, 13F, 4.5F);
                g.DrawLines(pen, new[] { new PointF(6F, 4.5F), new PointF(6F, 2.8F), new PointF(10F, 2.8F), new PointF(10F, 4.5F) });
                g.DrawLines(pen, new[] { new PointF(4.5F, 5F), new PointF(5.2F, 13.2F), new PointF(10.8F, 13.2F), new PointF(11.5F, 5F) });
                g.DrawLine(pen, 7F, 7.2F, 7F, 11.2F);
                g.DrawLine(pen, 9F, 7.2F, 9F, 11.2F);
                break;
            case IconKind.Pencil:
                g.DrawPolygon(pen, new[]
                {
                    new PointF(2.8F, 13.2F), new PointF(3.4F, 10.2F), new PointF(10.6F, 3F),
                    new PointF(13F, 5.4F), new PointF(5.8F, 12.6F)
                });
                g.DrawLine(pen, 9F, 4.6F, 11.4F, 7F);
                break;
            case IconKind.Search:
                g.DrawEllipse(pen, 2.5F, 2.5F, 7F, 7F);
                g.DrawLine(pen, 9.3F, 9.3F, 13.2F, 13.2F);
                break;
            case IconKind.Keyboard:
                g.DrawRoundedRectangle(pen, new Rectangle(1, 4, 14, 9), 2);
                g.FillEllipse(brush, 3.2F, 6.2F, 1.4F, 1.4F);
                g.FillEllipse(brush, 5.8F, 6.2F, 1.4F, 1.4F);
                g.FillEllipse(brush, 8.4F, 6.2F, 1.4F, 1.4F);
                g.FillEllipse(brush, 11F, 6.2F, 1.4F, 1.4F);
                g.DrawLine(pen, 5F, 10.6F, 11F, 10.6F);
                break;
        }
        return bitmap;
    }
}

internal sealed class LogoMark : Control
{
    public LogoMark()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        TabStop = false;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float s = Math.Min(Width, Height) / 48F; // desenho projetado em 48x48
        g.ScaleTransform(s, s);
        using var bg = new SolidBrush(Color.FromArgb(225, 242, 245));
        using var accent = new Pen(Color.FromArgb(40, 132, 150), 2.4F) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var white = new SolidBrush(Color.White);
        using var sparkle = new Pen(Color.FromArgb(40, 132, 150), 1.8F) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.FillRoundedRectangle(bg, new Rectangle(1, 1, 45, 45), 12);
        g.DrawRoundedRectangle(accent, new Rectangle(11, 12, 25, 23), 5);
        g.DrawLine(accent, 16, 19, 30, 19);
        g.DrawLine(accent, 16, 24, 26, 24);
        g.FillEllipse(white, 29, 8, 10, 10);
        g.DrawLine(sparkle, 34, 9, 34, 17);
        g.DrawLine(sparkle, 30, 13, 38, 13);
    }
}
