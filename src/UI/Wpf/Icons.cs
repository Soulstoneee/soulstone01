using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Path = System.Windows.Shapes.Path;

namespace RestMode.UI.Wpf;

/// <summary>
/// Simple flat line icons (24 x 24 grid, 1.5 px strokes) - only where needed:
/// settings, back, info, close, minimise, and the moon used for the app and
/// tray icons.
/// </summary>
internal static class Icons
{
    public static readonly Geometry Settings = Build(c =>
    {
        // gear: circle + 8 short teeth + centre
        Circle(c, new Point(12, 12), 6.5);
        Circle(c, new Point(12, 12), 2.6);
        for (int i = 0; i < 8; i++)
        {
            double a = i * Math.PI / 4;
            c.BeginFigure(new Point(12 + 6.5 * Math.Cos(a), 12 + 6.5 * Math.Sin(a)), false, false);
            c.LineTo(new Point(12 + 9.2 * Math.Cos(a), 12 + 9.2 * Math.Sin(a)), true, false);
        }
    });

    public static readonly Geometry Back = Geometry.Parse("M14.5,5.5 L8,12 L14.5,18.5");
    public static readonly Geometry Close = Geometry.Parse("M7,7 L17,17 M17,7 L7,17");
    public static readonly Geometry Minimize = Geometry.Parse("M7,12.5 L17,12.5");

    public static readonly Geometry Info = Build(c =>
    {
        Circle(c, new Point(12, 12), 9);
        c.BeginFigure(new Point(12, 10.8), false, false);
        c.LineTo(new Point(12, 16.5), true, false);
        c.BeginFigure(new Point(12, 7.6), false, false);
        c.LineTo(new Point(12, 7.7), true, false);
    });

    /// <summary>A crescent moon (outline).</summary>
    public static readonly Geometry Moon = Geometry.Parse("M15.2,3.6 A8.8,8.8 0 1 0 20.4,15.6 A7,7 0 1 1 15.2,3.6 Z");

    public static Path Make(Geometry g, double size = 18, string brush = "SecondaryBrush") => new()
    {
        Data = g,
        Stroke = ThemeManager.Brush(brush),
        StrokeThickness = 1.5,
        StrokeStartLineCap = PenLineCap.Round,
        StrokeEndLineCap = PenLineCap.Round,
        StrokeLineJoin = PenLineJoin.Round,
        Width = size,
        Height = size,
        Stretch = Stretch.Uniform,
    };

    /// <summary>Renders the moon on a dark rounded tile (app icon) or alone (tray), outline or filled.</summary>
    public static BitmapSource RenderMoon(int size, bool tile, bool filled)
    {
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            if (tile)
            {
                double r = size * 0.2;
                dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(0x0E, 0x0E, 0x0E)),
                    new Pen(new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3A)), Math.Max(1, size / 48.0)),
                    new Rect(0.5, 0.5, size - 1, size - 1), r, r);
            }
            double inset = tile ? size * 0.2 : size * 0.06;
            double scale = (size - 2 * inset) / 24.0;
            dc.PushTransform(new TranslateTransform(inset, inset));
            dc.PushTransform(new ScaleTransform(scale, scale));
            var pen = new Pen(Brushes.White, Math.Max(1.6, (size <= 32 ? 2.6 : 1.6) * (24.0 / size) * (size / 24.0) / scale * (size <= 32 ? 0.9 : 1.2) * scale / scale))
            { LineJoin = PenLineJoin.Round };
            pen.Thickness = size <= 20 ? 2.4 : size <= 32 ? 2.0 : 1.5;
            dc.DrawGeometry(filled ? Brushes.White : null, pen, Moon);
        }
        var bmp = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(dv);
        bmp.Freeze();
        return bmp;
    }

    private static Geometry Build(Action<StreamGeometryContext> draw)
    {
        var g = new StreamGeometry();
        using (var c = g.Open()) draw(c);
        g.Freeze();
        return g;
    }

    private static void Circle(StreamGeometryContext c, Point center, double r)
    {
        c.BeginFigure(new Point(center.X + r, center.Y), false, true);
        c.ArcTo(new Point(center.X - r, center.Y), new Size(r, r), 0, false, SweepDirection.Clockwise, true, false);
        c.ArcTo(new Point(center.X + r, center.Y), new Size(r, r), 0, false, SweepDirection.Clockwise, true, false);
    }
}
