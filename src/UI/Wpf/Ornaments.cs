using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace RestMode.UI.Wpf;

/// <summary>
/// RestMode's ornaments: ORIGINAL vector art, drawn procedurally (no copied
/// game art or logos). Symmetrical silver baroque filigree: scrollwork, vines
/// with leaves and curls, a crown, beaded swags, hanging charms (hearts, stars,
/// keyholes), small chandeliers, old keys and scattered stars.
/// Everything is built once, frozen and cached, so it costs nothing to show.
/// </summary>
internal static class Ornaments
{
    public const double W = 1600, H = 1000;
    private static readonly Dictionary<string, Drawing> Cache = new();

    // ------------------------------------------------------------------ pens

    private static Pen MakePen(double thickness, byte alpha)
    {
        var brush = new LinearGradientBrush(
            new GradientStopCollection
            {
                new(Color.FromArgb(alpha, 0x7E, 0x84, 0x8D), 0),
                new(Color.FromArgb(alpha, 0xE2, 0xE6, 0xEC), 0.5),
                new(Color.FromArgb(alpha, 0x7E, 0x84, 0x8D), 1),
            }, new Point(0, 0), new Point(1, 1));
        var p = new Pen(brush, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        p.Freeze();
        return p;
    }

    // ------------------------------------------------------------------ primitives

    private static Point P(double x, double y) => new(x, y);

    /// <summary>A spiral curl (logarithmic-ish), from radius r0 down to r1 over the given turns.</summary>
    public static void Spiral(StreamGeometryContext c, Point center, double r0, double r1, double startDeg, double turns, bool clockwise, bool begin = true)
    {
        int n = Math.Max(12, (int)(turns * 48));
        double dir = clockwise ? 1 : -1;
        for (int i = 0; i <= n; i++)
        {
            double t = i / (double)n;
            double a = (startDeg + dir * t * turns * 360) * Math.PI / 180;
            double r = r0 * Math.Pow(r1 / r0, t);
            var pt = P(center.X + r * Math.Cos(a), center.Y + r * Math.Sin(a));
            if (i == 0 && begin) c.BeginFigure(pt, false, false);
            else c.LineTo(pt, true, true);
        }
    }

    /// <summary>Smooth curve through points (Catmull-Rom converted to cubic Béziers).</summary>
    public static void Smooth(StreamGeometryContext c, IReadOnlyList<Point> pts, bool begin = true)
    {
        if (begin) c.BeginFigure(pts[0], false, false);
        for (int i = 0; i < pts.Count - 1; i++)
        {
            var p0 = i == 0 ? pts[0] : pts[i - 1];
            var p1 = pts[i];
            var p2 = pts[i + 1];
            var p3 = i + 2 < pts.Count ? pts[i + 2] : pts[i + 1];
            var c1 = P(p1.X + (p2.X - p0.X) / 6, p1.Y + (p2.Y - p0.Y) / 6);
            var c2 = P(p2.X - (p3.X - p1.X) / 6, p2.Y - (p3.Y - p1.Y) / 6);
            c.BezierTo(c1, c2, p2, true, true);
        }
    }

    public static void Leaf(StreamGeometryContext c, Point at, double angleDeg, double len, double width)
    {
        double a = angleDeg * Math.PI / 180;
        var tip = P(at.X + len * Math.Cos(a), at.Y + len * Math.Sin(a));
        var nx = -Math.Sin(a) * width; var ny = Math.Cos(a) * width;
        var mid = P((at.X + tip.X) / 2, (at.Y + tip.Y) / 2);
        c.BeginFigure(at, false, true);
        c.QuadraticBezierTo(P(mid.X + nx, mid.Y + ny), tip, true, true);
        c.QuadraticBezierTo(P(mid.X - nx, mid.Y - ny), at, true, true);
        c.BeginFigure(at, false, false);
        c.LineTo(P(at.X + (tip.X - at.X) * 0.8, at.Y + (tip.Y - at.Y) * 0.8), true, false);
    }

    public static void Heart(StreamGeometryContext c, Point center, double s, bool filled = false)
    {
        var top = P(center.X, center.Y - s * 0.25);
        var bottom = P(center.X, center.Y + s * 0.6);
        c.BeginFigure(top, filled, true);
        c.BezierTo(P(center.X - s * 0.1, center.Y - s * 0.75), P(center.X - s * 0.95, center.Y - s * 0.45), bottom, true, true);
        c.BeginFigure(top, filled, true);
        c.BezierTo(P(center.X + s * 0.1, center.Y - s * 0.75), P(center.X + s * 0.95, center.Y - s * 0.45), bottom, true, true);
    }

    public static void Sparkle(StreamGeometryContext c, Point p, double r)
    {
        c.BeginFigure(P(p.X, p.Y - r), true, true);
        c.QuadraticBezierTo(p, P(p.X + r, p.Y), true, true);
        c.QuadraticBezierTo(p, P(p.X, p.Y + r), true, true);
        c.QuadraticBezierTo(p, P(p.X - r, p.Y), true, true);
        c.QuadraticBezierTo(p, P(p.X, p.Y - r), true, true);
    }

    public static void Keyhole(StreamGeometryContext c, Point center, double s, bool filled = false)
    {
        double r = s * 0.28;
        var cy = center.Y - s * 0.18;
        c.BeginFigure(P(center.X - r * 0.55, cy + r * 0.8), filled, true);
        c.ArcTo(P(center.X + r * 0.55, cy + r * 0.8), new Size(r, r), 0, true, SweepDirection.Clockwise, true, true);
        c.LineTo(P(center.X + r * 0.75, center.Y + s * 0.45), true, true);
        c.LineTo(P(center.X - r * 0.75, center.Y + s * 0.45), true, true);
    }

    public static void Circle(StreamGeometryContext c, Point center, double r, bool filled = false)
    {
        c.BeginFigure(P(center.X + r, center.Y), filled, true);
        c.ArcTo(P(center.X - r, center.Y), new Size(r, r), 0, false, SweepDirection.Clockwise, true, true);
        c.ArcTo(P(center.X + r, center.Y), new Size(r, r), 0, false, SweepDirection.Clockwise, true, true);
    }

    public static void Diamond(StreamGeometryContext c, Point p, double r, bool filled = true)
    {
        c.BeginFigure(P(p.X, p.Y - r), filled, true);
        c.LineTo(P(p.X + r * 0.6, p.Y), true, true);
        c.LineTo(P(p.X, p.Y + r), true, true);
        c.LineTo(P(p.X - r * 0.6, p.Y), true, true);
    }

    /// <summary>An old key: ornate bow (ring with three lobes), shaft and bit.</summary>
    public static void Key(StreamGeometryContext c, Point bow, double angleDeg, double s)
    {
        double a = angleDeg * Math.PI / 180;
        Point R(double x, double y) => P(bow.X + x * Math.Cos(a) - y * Math.Sin(a), bow.Y + x * Math.Sin(a) + y * Math.Cos(a));
        Circle(c, bow, s * 0.18);
        for (int i = 0; i < 3; i++)
        {
            double la = a + (i - 1) * 1.1 + Math.PI;
            Circle(c, P(bow.X + s * 0.24 * Math.Cos(la), bow.Y + s * 0.24 * Math.Sin(la)), s * 0.09);
        }
        c.BeginFigure(R(s * 0.18, 0), false, false);
        c.LineTo(R(s * 1.0, 0), true, false);
        c.BeginFigure(R(s * 0.32, -s * 0.05), false, false);
        c.LineTo(R(s * 0.32, s * 0.05), true, false);
        c.BeginFigure(R(s * 0.82, 0), false, false);
        c.LineTo(R(s * 0.82, s * 0.16), true, false);
        c.LineTo(R(s * 0.9, s * 0.16), true, false);
        c.LineTo(R(s * 0.9, s * 0.08), true, false);
        c.LineTo(R(s * 1.0, s * 0.08), true, false);
        c.LineTo(R(s * 1.0, 0), true, false);
    }

    public static void Crown(StreamGeometryContext c, Point center, double s)
    {
        double w = s, h = s * 0.62;
        var bl = P(center.X - w / 2, center.Y + h / 2);
        var br = P(center.X + w / 2, center.Y + h / 2);
        c.BeginFigure(bl, false, true);
        c.LineTo(br, true, true);
        c.LineTo(P(center.X + w * 0.56, center.Y - h * 0.25), true, true);
        c.LineTo(P(center.X + w * 0.25, center.Y + h * 0.05), true, true);
        c.LineTo(P(center.X, center.Y - h * 0.55), true, true);
        c.LineTo(P(center.X - w * 0.25, center.Y + h * 0.05), true, true);
        c.LineTo(P(center.X - w * 0.56, center.Y - h * 0.25), true, true);
        c.BeginFigure(P(bl.X + w * 0.05, bl.Y - h * 0.18), false, false);
        c.LineTo(P(br.X - w * 0.05, br.Y - h * 0.18), true, false);
        foreach (var x in new[] { -0.56, 0.0, 0.56 })
            Circle(c, P(center.X + w * x, center.Y + (x == 0 ? -h * 0.55 : -h * 0.25) - s * 0.07), s * 0.05);
        Diamond(c, P(center.X, center.Y + h * 0.25), s * 0.07, false);
    }

    /// <summary>A small chandelier hanging from <paramref name="top"/>.</summary>
    public static void Chandelier(StreamGeometryContext c, Point top, double s)
    {
        c.BeginFigure(top, false, false);
        c.LineTo(P(top.X, top.Y + s * 0.5), true, false);
        Circle(c, P(top.X, top.Y + s * 0.58), s * 0.06);
        c.BeginFigure(P(top.X, top.Y + s * 0.64), false, false);
        c.LineTo(P(top.X, top.Y + s * 1.05), true, false);
        foreach (int side in new[] { -1, 1 })
        {
            // S-shaped arm with a candle and a flame
            var start = P(top.X, top.Y + s * 0.85);
            var end = P(top.X + side * s * 0.62, top.Y + s * 0.78);
            c.BeginFigure(start, false, false);
            c.BezierTo(P(top.X + side * s * 0.15, top.Y + s * 1.15), P(top.X + side * s * 0.5, top.Y + s * 1.12), end, true, true);
            Spiral(c, P(top.X + side * s * 0.16, top.Y + s * 1.02), s * 0.08, s * 0.02, side > 0 ? 180 : 0, 1.2, side > 0);
            c.BeginFigure(P(end.X - s * 0.05, end.Y), false, true);
            c.LineTo(P(end.X + s * 0.05, end.Y), true, true);
            c.LineTo(P(end.X + s * 0.04, end.Y - s * 0.22), true, true);
            c.LineTo(P(end.X - s * 0.04, end.Y - s * 0.22), true, true);
            var f = P(end.X, end.Y - s * 0.26);
            c.BeginFigure(P(f.X, f.Y - s * 0.12), false, true);
            c.QuadraticBezierTo(P(f.X + s * 0.06, f.Y - s * 0.02), f, true, true);
            c.QuadraticBezierTo(P(f.X - s * 0.06, f.Y - s * 0.02), P(f.X, f.Y - s * 0.12), true, true);
            Diamond(c, P(end.X, end.Y + s * 0.12), s * 0.05, false);
        }
        Diamond(c, P(top.X, top.Y + s * 1.18), s * 0.08, false);
    }

    /// <summary>A short bead chain ending in a charm (0 heart, 1 star, 2 keyhole).</summary>
    public static void Charm(StreamGeometryContext c, Point top, double len, int kind, double s)
    {
        int beads = Math.Max(2, (int)(len / (s * 0.35)));
        for (int i = 0; i < beads; i++) Circle(c, P(top.X, top.Y + i * len / beads), s * 0.06);
        var at = P(top.X, top.Y + len + s * 0.3);
        switch (kind % 3)
        {
            case 0: Heart(c, at, s * 0.7); break;
            case 1: Sparkle(c, at, s * 0.35); break;
            default: Keyhole(c, at, s * 0.7); break;
        }
    }

    /// <summary>A baroque C-scroll: a curve from <paramref name="a"/> to <paramref name="b"/> with curls at both ends.</summary>
    public static void Scroll(StreamGeometryContext c, Point a, Point b, double bulge, double curl, bool flip)
    {
        var mid = P((a.X + b.X) / 2, (a.Y + b.Y) / 2);
        double dx = b.X - a.X, dy = b.Y - a.Y, len = Math.Sqrt(dx * dx + dy * dy);
        double nx = -dy / len * bulge * (flip ? -1 : 1), ny = dx / len * bulge * (flip ? -1 : 1);
        c.BeginFigure(a, false, false);
        c.BezierTo(P(a.X + nx, a.Y + ny), P(b.X + nx, b.Y + ny), b, true, true);
        double angA = Math.Atan2(ny, nx) * 180 / Math.PI;
        Spiral(c, P(a.X - nx * 0.15 + dx / len * curl, a.Y - ny * 0.15 + dy / len * curl), curl, curl * 0.15, angA + 180, 1.4, !flip);
        Spiral(c, P(b.X - nx * 0.15 - dx / len * curl, b.Y - ny * 0.15 - dy / len * curl), curl, curl * 0.15, angA + 180, 1.4, flip);
        _ = mid;
    }

    // ------------------------------------------------------------------ compositions

    /// <summary>The full-window background (1600 x 1000, symmetrical).</summary>
    public static Drawing Background()
    {
        lock (Cache)
        {
            if (Cache.TryGetValue("bg", out var d)) return d;
            d = BuildBackground();
            Cache["bg"] = d;
            return d;
        }
    }

    private static Drawing BuildBackground()
    {
        var main = MakePen(1.25, 255);
        var fine = MakePen(0.75, 200);
        var faint = MakePen(0.6, 120);

        // Left half (mirrored to the right), plus the centre pieces.
        var half = new StreamGeometry();
        var halfFine = new StreamGeometry();
        var center = new StreamGeometry();
        var centerFine = new StreamGeometry();
        var stars = new StreamGeometry();

        using (var c = half.Open())
        {
            // frame: outer + inner line with a stepped corner
            c.BeginFigure(P(W / 2, 22), false, false);
            c.PolyLineTo(new[] { P(60, 22), P(22, 60), P(22, H - 60), P(60, H - 22), P(W / 2, H - 22) }, true, true);

            // top-left corner: big double scroll + curls
            Scroll(c, P(40, 120), P(170, 40), 45, 18, false);
            Scroll(c, P(70, 170), P(230, 70), 60, 14, true);
            Spiral(c, P(95, 95), 34, 4, 225, 2.2, true);
            // bottom-left corner
            Scroll(c, P(40, H - 120), P(170, H - 40), 45, 18, true);
            Scroll(c, P(70, H - 170), P(230, H - 70), 60, 14, false);
            Spiral(c, P(95, H - 95), 34, 4, 135, 2.2, false);

            // left border vine running down with curls
            var vine = new List<Point>();
            for (int i = 0; i <= 16; i++)
            {
                double y = 210 + i * (H - 420) / 16.0;
                vine.Add(P(62 + 16 * Math.Sin(i * 0.9), y));
            }
            Smooth(c, vine);

            // swag from the crown to the left chandelier, with beads
            Smooth(c, new[] { P(W / 2 - 60, 92), P(W / 2 - 220, 150), P(W / 2 - 400, 120), P(W / 2 - 520, 70) });

            // lower-centre cartouche, left half
            Scroll(c, P(W / 2 - 40, H - 150), P(W / 2 - 230, H - 90), 55, 16, false);
            Scroll(c, P(W / 2 - 60, H - 110), P(W / 2 - 300, H - 160), 40, 12, true);
        }
        using (var c = halfFine.Open())
        {
            c.BeginFigure(P(W / 2, 34), false, false);
            c.PolyLineTo(new[] { P(68, 34), P(34, 68), P(34, H - 68), P(68, H - 34), P(W / 2, H - 34) }, true, true);
            // leaves along the vine
            for (int i = 0; i < 16; i++)
            {
                double y = 225 + i * (H - 420) / 16.0;
                double x = 62 + 16 * Math.Sin(i * 0.9 + 0.3);
                Leaf(c, P(x, y), i % 2 == 0 ? -25 : 25 + 180, 26, 7);
                if (i % 4 == 2) Spiral(c, P(x + 20, y + 8), 9, 1.5, 0, 1.5, true);
            }
            // beads on the swag
            for (int i = 0; i < 9; i++)
            {
                double t = i / 8.0;
                var p = P(W / 2 - 60 - t * 460, 92 + Math.Sin(t * Math.PI) * 48 - t * 22);
                Circle(c, p, 2.2);
            }
            // charms hanging from the swag
            for (int i = 0; i < 3; i++)
            {
                double t = (i + 1) / 4.0;
                var p = P(W / 2 - 60 - t * 460, 92 + Math.Sin(t * Math.PI) * 48 - t * 22);
                Charm(c, p, 40 + i * 14, i, 26);
            }
            // chandelier hanging from the top frame
            c.BeginFigure(P(W / 2 - 520, 22), false, false);
            c.LineTo(P(W / 2 - 520, 46), true, false);
            Chandelier(c, P(W / 2 - 520, 46), 90);
            // corner leaves
            Leaf(c, P(150, 110), -40, 40, 10);
            Leaf(c, P(110, 150), 130, 40, 10);
            Leaf(c, P(150, H - 110), 40, 40, 10);
            Leaf(c, P(110, H - 150), 230, 40, 10);
            // keys hanging in the lower corners
            Key(c, P(120, H - 230), 60, 70);
            // small hearts in the corner scrolls
            Heart(c, P(205, 120), 16);
            Heart(c, P(205, H - 128), 16);
        }
        using (var c = center.Open())
        {
            Crown(c, P(W / 2, 62), 64);
            // lower cartouche: keyhole medallion
            Circle(c, P(W / 2, H - 120), 46);
            Circle(c, P(W / 2, H - 120), 54);
            Keyhole(c, P(W / 2, H - 116), 46);
            // top-centre finial
            Spiral(c, P(W / 2 - 22, 108), 14, 2, 0, 1.6, false);
            Spiral(c, P(W / 2 + 22, 108), 14, 2, 180, 1.6, true);
        }
        using (var c = centerFine.Open())
        {
            // radiating lines around the medallion
            for (int i = 0; i < 24; i++)
            {
                double a = i * Math.PI / 12;
                c.BeginFigure(P(W / 2 + 62 * Math.Cos(a), H - 120 + 62 * Math.Sin(a)), false, false);
                c.LineTo(P(W / 2 + (i % 2 == 0 ? 82 : 72) * Math.Cos(a), H - 120 + (i % 2 == 0 ? 82 : 72) * Math.Sin(a)), true, false);
            }
            // crossed keys under the medallion
            Key(c, P(W / 2 - 110, H - 60), -12, 100);
            Key(c, P(W / 2 + 110, H - 60), 192, 100);
            Heart(c, P(W / 2, 138), 18);
        }
        using (var c = stars.Open())
        {
            var rnd = new Random(7);
            for (int i = 0; i < 26; i++)
            {
                var p = P(260 + rnd.NextDouble() * 420, 200 + rnd.NextDouble() * 600);
                Sparkle(c, p, 2 + rnd.NextDouble() * 4);
                Sparkle(c, P(W - p.X, p.Y), 2 + rnd.NextDouble() * 4);
            }
        }

        var mirror = new ScaleTransform(-1, 1, W / 2, 0);
        mirror.Freeze();
        var g = new DrawingGroup();
        using (var dc = g.Open())
        {
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, W, H));
            foreach (var (geo, pen) in new[] { (half, main), (halfFine, fine) })
            {
                geo.Freeze();
                dc.DrawGeometry(null, pen, geo);
                dc.PushTransform(mirror);
                dc.DrawGeometry(null, pen, geo);
                dc.Pop();
            }
            center.Freeze();
            centerFine.Freeze();
            dc.DrawGeometry(null, main, center);
            dc.DrawGeometry(null, fine, centerFine);
            stars.Freeze();
            var starBrush = new SolidColorBrush(Color.FromArgb(150, 0xDD, 0xE1, 0xE8));
            starBrush.Freeze();
            dc.DrawGeometry(starBrush, faint, stars);
        }
        g.Freeze();
        return g;
    }

    /// <summary>The RestMode emblem: a heart with a keyhole, a small crown and rays (original design).</summary>
    public static Drawing Emblem(Color line, double strokeScale = 1)
    {
        var pen = new Pen(new SolidColorBrush(line), 3.2 * strokeScale) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        var thin = new Pen(new SolidColorBrush(Color.FromArgb(170, line.R, line.G, line.B)), 1.6 * strokeScale) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        pen.Freeze(); thin.Freeze();
        var heart = new StreamGeometry();
        var detail = new StreamGeometry();
        using (var c = heart.Open())
        {
            Heart(c, P(100, 112), 150);
            Keyhole(c, P(100, 110), 62);
        }
        using (var c = detail.Open())
        {
            Crown(c, P(100, 26), 46);
            Spiral(c, P(48, 150), 18, 3, 300, 1.5, false);
            Spiral(c, P(152, 150), 18, 3, 240, 1.5, true);
            for (int i = 0; i < 9; i++)
            {
                double a = Math.PI + i * Math.PI / 8;
                c.BeginFigure(P(100 + 92 * Math.Cos(a), 105 + 92 * Math.Sin(a)), false, false);
                c.LineTo(P(100 + (i % 2 == 0 ? 104 : 98) * Math.Cos(a), 105 + (i % 2 == 0 ? 104 : 98) * Math.Sin(a)), true, false);
            }
            Sparkle(c, P(100, 196), 6);
        }
        heart.Freeze(); detail.Freeze();
        var g = new DrawingGroup();
        using (var dc = g.Open())
        {
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, 200, 210));
            dc.DrawGeometry(null, pen, heart);
            dc.DrawGeometry(null, thin, detail);
        }
        g.Freeze();
        return g;
    }

    /// <summary>
    /// A calligraphy flourish for one side of a button: starts at the button edge (x=0),
    /// waves outward, makes a loop and ends in a long tapered tail with a small curl
    /// (in the spirit of handwritten "eternal"). Mirror it for the left side.
    /// </summary>
    public static Geometry Flourish(double length, double height)
    {
        double h = height;
        // Drawn around y = h (the vertical centre of a 2h-high box), starting at x = 0.
        Point Q(double x, double y) => P(x, y + h);
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(Q(0, 0), false, false);
            c.BezierTo(Q(length * 0.12, -h * 0.05), Q(length * 0.18, h * 0.55), Q(length * 0.3, h * 0.42), true, true);
            // the loop
            c.BezierTo(Q(length * 0.44, h * 0.3), Q(length * 0.42, -h * 0.75), Q(length * 0.33, -h * 0.55), true, true);
            c.BezierTo(Q(length * 0.24, -h * 0.35), Q(length * 0.36, h * 0.25), Q(length * 0.55, h * 0.12), true, true);
            // long tail
            c.BezierTo(Q(length * 0.72, 0), Q(length * 0.86, -h * 0.28), Q(length * 0.95, -h * 0.12), true, true);
            c.BezierTo(Q(length * 1.0, -h * 0.02), Q(length * 0.97, h * 0.12), Q(length * 0.92, h * 0.07), true, true);
        }
        g.Freeze();
        return g;
    }

    /// <summary>Approximate length of a geometry (for the "draw itself" dash animation).</summary>
    public static double Length(Geometry g)
    {
        var flat = g.GetFlattenedPathGeometry(0.5, ToleranceType.Absolute);
        double len = 0;
        foreach (var fig in flat.Figures)
        {
            var last = fig.StartPoint;
            foreach (var seg in fig.Segments)
            {
                if (seg is PolyLineSegment pl)
                    foreach (var p in pl.Points) { len += (p - last).Length; last = p; }
                else if (seg is LineSegment ls) { len += (ls.Point - last).Length; last = ls.Point; }
            }
        }
        return len;
    }

    /// <summary>A small key used as the "selected" cursor next to buttons.</summary>
    public static Geometry KeyCursor()
    {
        var g = new StreamGeometry();
        using (var c = g.Open()) Key(c, P(8, 10), 0, 30);
        g.Freeze();
        return g;
    }

    /// <summary>Ornate corner piece (top-left orientation, ~18 px).</summary>
    public static Geometry Corner(double s)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(P(0, s), false, false);
            c.LineTo(P(0, 0), true, false);
            c.LineTo(P(s, 0), true, false);
            Spiral(c, P(s * 0.42, s * 0.42), s * 0.3, s * 0.05, 200, 1.3, true);
            Diamond(c, P(-s * 0.18, -s * 0.18), s * 0.18, false);
        }
        g.Freeze();
        return g;
    }

    /// <summary>Renders a drawing to a bitmap (used for the app icon and tray icons).</summary>
    public static BitmapSource Render(Drawing d, int size, Brush? background = null)
    {
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            if (background != null) dc.DrawRectangle(background, null, new Rect(0, 0, size, size));
            var b = d.Bounds;
            double scale = Math.Min(size / b.Width, size / b.Height) * 0.92;
            dc.PushTransform(new TranslateTransform((size - b.Width * scale) / 2 - b.X * scale, (size - b.Height * scale) / 2 - b.Y * scale));
            dc.PushTransform(new ScaleTransform(scale, scale));
            dc.DrawDrawing(d);
        }
        var bmp = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(dv);
        bmp.Freeze();
        return bmp;
    }
}
