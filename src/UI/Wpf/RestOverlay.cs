using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using RestMode.Core;
using RestMode.Native;
using RestMode.Util;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace RestMode.UI.Wpf;

/// <summary>
/// The two full-screen moments:
///  * Entering Rest Mode - every monitor goes black; on the main one a light
///    travels clockwise along the very edges of the screen (a bright head with a
///    fading tail) while "Entering Rest Mode" fades in. After the set length
///    (6 s by default) light and text fade out together and every frame callback
///    is removed, so the screen is a still black image when it turns off.
///    Any key press or mouse move during the animation cancels Rest Mode entry.
///  * Welcome back - "Welcome back", a thin line and "Rested … · … downloaded",
///    for about 1.5 s. It is fire-and-forget: it never delays or blocks the wake,
///    and any key or click closes it.
/// </summary>
internal static class RestOverlay
{
    private static readonly List<Window> Windows = new();
    private static Grid? _root;
    private static EdgeLightScene? _scene;

    public static bool IsShown => Windows.Count > 0;

    public static readonly Color Orange = Color.FromRgb(0xFF, 0x7A, 0x1A);

    public static Color FrameColor(AppSettings s) => s.FrameColor switch
    {
        FrameColorChoice.White => Colors.White,
        FrameColorChoice.Accent => WindowsAccent() ?? Orange,
        FrameColorChoice.Custom when AppSettings.IsValidColor(s.CustomFrameColor) => (Color)ColorConverter.ConvertFromString(s.CustomFrameColor),
        _ => Orange,
    };

    /// <summary>The Windows accent color (Settings › Personalization › Colors), or null if it can't be read.</summary>
    public static Color? WindowsAccent()
    {
        try
        {
            using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM");
            if (k?.GetValue("AccentColor") is int abgr)
                return Color.FromRgb((byte)(abgr & 0xFF), (byte)((abgr >> 8) & 0xFF), (byte)((abgr >> 16) & 0xFF));
        }
        catch { /* fall through */ }
        return null;
    }

    // ------------------------------------------------------------------ entering

    /// <returns>true when the animation ran to the end (or was skipped after an error);
    /// false when a key press or mouse move cancelled it.</returns>
    public static async Task<bool> ShowEnterAsync(AppSettings s, int mouseThresholdPx = 5)
    {
        CloseAll();
        Build();
        if (_root == null) return true;
        try
        {
            var size = await WaitForSizeAsync(_root);
            _scene = new EdgeLightScene(size, FrameColor(s), s.OverlayText, s.ScreenCornerRadius, s.LapSeconds, s.AnimationSeconds);
            _root.Children.Add(_scene.Root);
            Sounds.Enter();
            var cancel = new InputCancel(Math.Max(3, mouseThresholdPx));
            return await _scene.PlayAsync(cancel.Check);
        }
        catch (Exception ex) { Logger.Error("Rest animation failed (skipped)", ex); return true; }
        finally
        {
            // Nothing may keep running while resting: stop the frame callback and remove the scene.
            _scene?.Stop();
            _root?.Children.Clear();
            _scene = null;
        }
    }

    // ------------------------------------------------------------------ welcome back

    /// <summary>Fire-and-forget. Never throws, never blocks the caller.</summary>
    public static async void ShowWelcome(AppSettings s, TimeSpan rested, long downloaded)
    {
        try
        {
            if (!IsShown) Build(primaryOnly: true);
            if (_root == null) { CloseAll(); return; }
            _scene?.Stop();
            _root.Children.Clear();
            var done = new TaskCompletionSource();
            foreach (var w in Windows)
            {
                w.PreviewKeyDown += (_, _) => done.TrySetResult();
                w.PreviewMouseDown += (_, _) => done.TrySetResult();
            }
            var welcome = BuildWelcome(FrameColor(s), WelcomeLine(rested, downloaded), out var line);
            welcome.Opacity = 0;
            _root.Children.Add(welcome);
            Sounds.Wake();
            // About 1.5 s in all: 0.3 s fade in, 0.9 s shown, 0.3 s fade out.
            welcome.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, ThemeManager.Dur(0.3)));
            line.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0, 1, ThemeManager.Dur(0.4)) { BeginTime = ThemeManager.Dur(0.1), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            await Task.WhenAny(done.Task, Task.Delay(ThemeManager.Dur(1.2)));
            if (!done.Task.IsCompleted)
            {
                foreach (var w in Windows) w.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, ThemeManager.Dur(0.3)));
                await Task.WhenAny(done.Task, Task.Delay(ThemeManager.Dur(0.32)));
            }
        }
        catch (Exception ex) { Logger.Info($"Welcome screen skipped: {ex.Message}"); }
        CloseAll();
    }

    public static string WelcomeLine(TimeSpan rested, long downloaded)
    {
        string t = rested.TotalHours >= 1 ? $"{(int)rested.TotalHours}h {rested.Minutes}m"
            : rested.TotalMinutes >= 1 ? $"{(int)rested.TotalMinutes} min"
            : $"{Math.Max(1, (int)rested.TotalSeconds)} s";
        return $"Rested {t} · {ActivityMonitor.FormatBytes(downloaded)} downloaded";
    }

    internal static FrameworkElement BuildWelcome(Color frame, string detail, out ScaleTransform line)
    {
        var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        panel.Children.Add(new TextBlock
        {
            Text = "Welcome back",
            FontFamily = ThemeManager.DisplayFont,
            FontSize = 34,
            FontWeight = FontWeights.Light,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        line = new ScaleTransform(0, 1);
        panel.Children.Add(new Rectangle
        {
            Height = 1.5,
            Width = 180,
            Fill = new SolidColorBrush(frame),
            Margin = new Thickness(0, 14, 0, 12),
            RenderTransformOrigin = new Point(0, 0.5),
            RenderTransform = line,
            Effect = new DropShadowEffect { Color = frame, BlurRadius = 8, ShadowDepth = 0, Opacity = 0.8 },
        });
        panel.Children.Add(new TextBlock
        {
            Text = detail,
            FontFamily = ThemeManager.Font,
            FontSize = 13.5,
            Foreground = ThemeManager.Brush("SecondaryBrush"),
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        return panel;
    }

    // ------------------------------------------------------------------ windows

    public static void CloseAll()
    {
        _scene?.Stop();
        _scene = null;
        foreach (var w in Windows.ToList())
        {
            try { w.Close(); } catch { }
        }
        Windows.Clear();
        _root = null;
    }

    private static async Task<Size> WaitForSizeAsync(FrameworkElement e)
    {
        for (int i = 0; i < 60 && (e.ActualWidth < 50 || e.ActualHeight < 50); i++) await Task.Delay(16);
        return new Size(e.ActualWidth, e.ActualHeight);
    }

    private static void Build(bool primaryOnly = false)
    {
        foreach (var screen in System.Windows.Forms.Screen.AllScreens)
        {
            if (primaryOnly && !screen.Primary) continue;
            var w = new Window
            {
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                Background = Brushes.Black,
                Topmost = true,
                ShowInTaskbar = false,
                ShowActivated = screen.Primary,
                Cursor = Cursors.None,
                Left = -32000, Top = -32000, Width = 10, Height = 10,
                Title = "RestMode",
            };
            if (screen.Primary)
            {
                _root = new Grid { Background = Brushes.Black, ClipToBounds = true };
                w.Content = _root;
            }
            var b = screen.Bounds;
            w.SourceInitialized += (_, _) =>
            {
                var h = new WindowInteropHelper(w).Handle;
                NativeMethods.SetWindowPos(h, NativeMethods.HWND_TOPMOST, b.Left, b.Top, b.Width, b.Height, NativeMethods.SWP_SHOWWINDOW);
            };
            w.Show();
            Windows.Add(w);
        }
    }
}

/// <summary>
/// "The user changed their mind": a new key or mouse-button press, or the mouse
/// moving more than a few pixels, while the Rest animation plays. Keys that were
/// already held when it started (the hotkey) don't count until released, so
/// letting go of the hotkey doesn't cancel. Polled once per frame; no hooks.
/// </summary>
internal sealed class InputCancel
{
    private readonly bool[] _held = new bool[256];
    private readonly NativeMethods.POINT _origin;
    private readonly bool _haveOrigin;
    private readonly int _threshold;
    private readonly Stopwatch _sw = Stopwatch.StartNew();

    public InputCancel(int thresholdPx)
    {
        _threshold = thresholdPx;
        for (int vk = 1; vk < 256; vk++) _held[vk] = (NativeMethods.GetAsyncKeyState(vk) & 0x8000) != 0;
        _haveOrigin = NativeMethods.GetCursorPos(out _origin);
    }

    /// <returns>A description of the input, or null if there was none.</returns>
    public string? Check()
    {
        for (int vk = 1; vk < 256; vk++)
        {
            bool down = (NativeMethods.GetAsyncKeyState(vk) & 0x8000) != 0;
            if (_held[vk]) { if (!down) _held[vk] = false; continue; }
            if (down) return vk is 1 or 2 or 4 or 5 or 6 ? "mouse click" : "key press";
        }
        // A short grace period lets the hand settle after clicking Rest.
        if (_haveOrigin && _sw.ElapsedMilliseconds > 300 && NativeMethods.GetCursorPos(out var p))
        {
            int dx = p.X - _origin.X, dy = p.Y - _origin.Y;
            if (dx * dx + dy * dy > _threshold * _threshold) return "mouse movement";
        }
        return null;
    }
}


/// <summary>
/// The Rest animation: a light that travels clockwise along the very edges of the
/// screen - a solid line (3 px at 100 %) with a soft glow fading inward, a bright
/// head and a tail fading over about 22 % of the screen's outline. It follows the
/// screen's rounded corners when a corner radius is set, otherwise square corners,
/// and the glow brightens a little as the head passes each corner.
/// Each frame is drawn from the elapsed time (constant speed even if a frame is
/// late): every straight edge the tail covers is one line with a smooth gradient,
/// so there are no seams; the glow is one GPU blur on its own layer.
/// </summary>
internal sealed class EdgeLightScene
{
    private const double TailShare = 0.22;  // tail length as a share of the outline
    private const double FadeOut = 0.5;     // seconds
    private const double Thickness = 3;     // DIPs: 3 px at 100 %, scaled like the rest of Windows
    private const double GlowThickness = 12;
    private const int Stops = 8;            // gradient stops per edge

    public Grid Root { get; } = new() { IsHitTestVisible = false };
    private readonly VisualHost _core = new(), _glow = new();
    private readonly TextBlock _text;
    private readonly Color _color, _hot;
    private readonly List<Piece> _pieces;
    private readonly double[] _corners;     // arc length of each corner's middle
    private readonly double _perimeter, _tail, _lap, _length;
    private Stopwatch? _clock;
    private Func<string?>? _cancel;
    private TaskCompletionSource<bool>? _done;

    public string? CancelledBy { get; private set; }

    /// <summary>One straight edge or one rounded corner of the outline.</summary>
    private sealed class Piece
    {
        public double Start, Length;
        public Point A, B;                  // straight: ends
        public Point[]? Arc;                // rounded corner: points every ~2 DIPs
        public double[]? ArcCum;
        public bool SquareCornerAtEnd;
        public double End => Start + Length;

        public Point At(double s)           // s: 0..Length along this piece
        {
            if (Arc == null) return Length > 0 ? A + (B - A) * (s / Length) : A;
            int i = 0;
            while (i < ArcCum!.Length - 2 && ArcCum[i + 1] < s) i++;
            double seg = ArcCum[i + 1] - ArcCum[i];
            return Arc[i] + (Arc[i + 1] - Arc[i]) * (seg > 0 ? (s - ArcCum[i]) / seg : 0);
        }
    }

    public EdgeLightScene(Size size, Color color, string text, int cornerRadius, double lapSeconds, double lengthSeconds)
    {
        _lap = Math.Clamp(lapSeconds, 0.5, 10);
        _length = Math.Max(FadeOut + 0.5, lengthSeconds);
        _color = color;
        _hot = Lerp(color, Colors.White, 0.2);
        var line = new Rect(size).Inflate2(-Thickness / 2);   // stroke centre: the line's outer edge is the screen edge
        double r = Math.Max(0, Math.Min(cornerRadius - Thickness / 2, Math.Min(line.Width, line.Height) / 2));
        (_pieces, _corners) = Outline(line, r);
        _perimeter = _pieces[^1].End;
        _tail = _perimeter * TailShare;

        _glow.Effect = new BlurEffect { Radius = 16, RenderingBias = RenderingBias.Performance, KernelType = KernelType.Gaussian };
        _glow.Opacity = 0.5;
        _text = new TextBlock
        {
            Text = text,
            FontFamily = ThemeManager.DisplayFont,
            FontSize = 20,
            FontWeight = FontWeights.Light,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0,
        };
        TextOptions.SetTextFormattingMode(_text, TextFormattingMode.Ideal);
        Root.Children.Add(_glow);
        Root.Children.Add(_core);
        Root.Children.Add(_text);
    }

    /// <summary>Plays the animation. Returns false if <paramref name="cancel"/> reported input.</summary>
    public Task<bool> PlayAsync(Func<string?>? cancel = null)
    {
        _cancel = cancel;
        _done = new TaskCompletionSource<bool>();
        _clock = Stopwatch.StartNew();
        DrawAt(0);
        CompositionTarget.Rendering += OnFrame;
        return _done.Task;
    }

    /// <summary>Stops the frame callback; the scene disappears. Safe to call twice.</summary>
    public void Stop()
    {
        CompositionTarget.Rendering -= OnFrame;
        _clock?.Stop();
        Root.Visibility = Visibility.Collapsed;
        _done?.TrySetResult(CancelledBy == null);
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        if (_clock == null) return;
        if (_cancel?.Invoke() is string why)
        {
            CancelledBy = why;
            Stop();
            return;
        }
        double t = _clock.Elapsed.TotalSeconds;
        if (t >= _length) { Stop(); return; }
        long t0 = Stopwatch.GetTimestamp();
        DrawAt(t);
        double ms = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
        _drawN++; _drawSum += ms; if (ms > DrawMsMax) DrawMsMax = ms;
    }

    // Diagnostics for the frame-rate check (dev tools only).
    private static int _drawN;
    private static double _drawSum;
    public static double DrawMsAvg => _drawN > 0 ? _drawSum / _drawN : 0;
    public static double DrawMsMax { get; private set; }

    /// <summary>Draws the frame at <paramref name="t"/> seconds (also used for still screenshots).</summary>
    public void DrawAt(double t)
    {
        double head = t / _lap * _perimeter;          // distance travelled (unwrapped)
        double tail = Math.Min(_tail, head);          // the tail grows from nothing at the start

        // Text fades in after a short delay; light and text fade out together at the end.
        Root.Opacity = Ease(Math.Clamp((_length - t) / FadeOut, 0, 1));
        _text.Opacity = Ease(Math.Clamp((t - 0.25) / 0.8, 0, 1)) * 0.92;

        // Corner glow: a little brighter while the head passes a corner.
        double boost = 0, h = Mod(head, _perimeter);
        foreach (double c in _corners)
        {
            double d = Math.Abs(h - c) % _perimeter;
            d = Math.Min(d, _perimeter - d);
            boost = Math.Max(boost, Math.Exp(-(d * d) / (2 * 45.0 * 45.0)));
        }
        _glow.Opacity = 0.5 + 0.45 * boost;

        using var dc = _core.Visual.RenderOpen();
        using var gc = _glow.Visual.RenderOpen();
        if (tail < 0.5) return;

        double from = head - tail;
        // Walk the outline from the tail end to the head, one piece at a time.
        double s = from;
        int guard = 0;
        while (s < head - 1e-6 && guard++ < 16)
        {
            double ws = Mod(s, _perimeter);
            var p = _pieces.First(x => ws < x.End - 1e-9 || ReferenceEquals(x, _pieces[^1]));
            double u0 = ws - p.Start;
            double take = Math.Min(p.Length - u0, head - s);
            if (take <= 1e-6) { s += 1e-3; continue; }
            double x0 = (s - from) / tail, x1 = (s + take - from) / tail;   // 0 = tail end, 1 = head
            bool continuesPastCorner = p.SquareCornerAtEnd && u0 + take >= p.Length - 1e-6 && s + take < head - 1e-6;

            if (p.Arc == null)
            {
                Point a = p.At(u0), b = p.At(u0 + take);
                dc.DrawLine(Pen(Gradient(a, b, x0, x1, 1.0), Thickness, continuesPastCorner), a, b);
                gc.DrawLine(Pen(Gradient(a, b, x0, x1, 0.55), GlowThickness, false), a, b);
            }
            else
            {
                // Rounded corner: short, so one alpha (its middle) is smooth enough.
                var g = new StreamGeometry();
                using (var c = g.Open())
                {
                    c.BeginFigure(p.At(u0), false, false);
                    for (int i = 0; i < p.ArcCum!.Length; i++)
                        if (p.ArcCum[i] > u0 && p.ArcCum[i] < u0 + take) c.LineTo(p.Arc![i], true, true);
                    c.LineTo(p.At(u0 + take), true, true);
                }
                g.Freeze();
                double xm = (x0 + x1) / 2;
                dc.DrawGeometry(null, Pen(Solid(ColorAt(xm, 1.0)), Thickness, false), g);
                gc.DrawGeometry(null, Pen(Solid(ColorAt(xm, 0.55)), GlowThickness, false), g);
            }
            s += take;
        }
    }

    // ------------------------------------------------------------------ drawing helpers

    /// <summary>Colour along the tail: x = 0 at the tail end (transparent) to 1 at the head (full, slightly lighter).</summary>
    private Color ColorAt(double x, double alphaScale)
    {
        x = Math.Clamp(x, 0, 1);
        var c = Lerp(_color, _hot, Math.Pow(x, 10));
        return Color.FromArgb((byte)Math.Round(255 * Math.Pow(x, 1.7) * alphaScale), c.R, c.G, c.B);
    }

    private LinearGradientBrush Gradient(Point a, Point b, double x0, double x1, double alphaScale)
    {
        var stops = new GradientStopCollection(Stops);
        for (int i = 0; i < Stops; i++)
        {
            double f = i / (double)(Stops - 1);
            stops.Add(new GradientStop(ColorAt(x0 + (x1 - x0) * f, alphaScale), f));
        }
        var br = new LinearGradientBrush(stops, a, b) { MappingMode = BrushMappingMode.Absolute };
        br.Freeze();
        return br;
    }

    private static SolidColorBrush Solid(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    /// <param name="squareEnd">Fills a square corner where the tail goes on around it.</param>
    private static Pen Pen(Brush b, double thickness, bool squareEnd)
    {
        var p = new Pen(b, thickness) { StartLineCap = PenLineCap.Flat, EndLineCap = squareEnd ? PenLineCap.Square : PenLineCap.Flat };
        p.Freeze();
        return p;
    }

    /// <summary>Clockwise from the top-left: top edge, top-right corner, right edge, … , top-left corner.</summary>
    private static (List<Piece> pieces, double[] corners) Outline(Rect r, double radius)
    {
        var pieces = new List<Piece>();
        var corners = new List<double>();
        double len = 0;
        void Straight(Point a, Point b, bool squareCorner)
        {
            var p = new Piece { Start = len, Length = (b - a).Length, A = a, B = b, SquareCornerAtEnd = squareCorner };
            pieces.Add(p);
            len += p.Length;
            if (squareCorner) corners.Add(len);
        }
        void Corner(Point centre, double startDeg)
        {
            if (radius < 0.5) return;
            int n = Math.Max(6, (int)Math.Ceiling(Math.PI / 2 * radius / 2));
            var pts = new Point[n + 1];
            var cum = new double[n + 1];
            for (int i = 0; i <= n; i++)
            {
                double ang = (startDeg + 90.0 * i / n) * Math.PI / 180;
                pts[i] = new Point(centre.X + radius * Math.Cos(ang), centre.Y + radius * Math.Sin(ang));
                if (i > 0) cum[i] = cum[i - 1] + (pts[i] - pts[i - 1]).Length;
            }
            var p = new Piece { Start = len, Length = cum[^1], Arc = pts, ArcCum = cum, A = pts[0], B = pts[^1] };
            pieces.Add(p);
            corners.Add(len + p.Length / 2);
            len += p.Length;
        }
        bool sq = radius < 0.5;
        double L = r.Left, T = r.Top, R = r.Right, B = r.Bottom, k = radius;
        Straight(new Point(L + k, T), new Point(R - k, T), sq);
        Corner(new Point(R - k, T + k), -90);
        Straight(new Point(R, T + k), new Point(R, B - k), sq);
        Corner(new Point(R - k, B - k), 0);
        Straight(new Point(R - k, B), new Point(L + k, B), sq);
        Corner(new Point(L + k, B - k), 90);
        Straight(new Point(L, B - k), new Point(L, T + k), sq);
        Corner(new Point(L + k, T + k), 180);
        return (pieces, corners.ToArray());
    }

    private static double Mod(double x, double m) => ((x % m) + m) % m;
    private static double Ease(double x) => x * x * (3 - 2 * x);   // smoothstep
    private static Color Lerp(Color a, Color b, double t) =>
        Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

    /// <summary>Hosts one DrawingVisual so it can be redrawn every frame without a layout pass.</summary>
    private sealed class VisualHost : FrameworkElement
    {
        public DrawingVisual Visual { get; } = new();
        public VisualHost() { AddVisualChild(Visual); IsHitTestVisible = false; }
        protected override int VisualChildrenCount => 1;
        protected override Visual GetVisualChild(int index) => Visual;
    }
}

internal static class RectExtensions
{
    public static Rect Inflate2(this Rect r, double by)
    {
        r.Inflate(by, by);
        return r;
    }
}
