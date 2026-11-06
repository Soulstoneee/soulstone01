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
using Path = System.Windows.Shapes.Path;
using Rectangle = System.Windows.Shapes.Rectangle;
using Shape = System.Windows.Shapes.Shape;

namespace RestMode.UI.Wpf;

/// <summary>
/// The two full-screen moments:
///  * Entering Rest Mode - every monitor goes black; on the main one a neon frame
///    draws itself clockwise around the screen, flares, then dims while
///    "Resting…" fades in and out (about 3.5 s). Then every animation is stopped
///    and removed, so the screen is a still black image when it turns off.
///  * Welcome back - "Welcome back", a thin line and "Rested … · … downloaded",
///    for about 1.5 s. It is fire-and-forget: it never delays or blocks the wake,
///    and any key or click closes it.
/// All animation runs on WPF's render thread (GPU), no timers or polling.
/// </summary>
internal static class RestOverlay
{
    private static readonly List<Window> Windows = new();
    private static Grid? _root;
    private static NeonScene? _scene;

    public static bool IsShown => Windows.Count > 0;

    public static Color FrameColor(AppSettings s) => s.FrameColor switch
    {
        FrameColorChoice.White => Colors.White,
        FrameColorChoice.Custom when AppSettings.IsValidColor(s.CustomFrameColor) => (Color)ColorConverter.ConvertFromString(s.CustomFrameColor),
        _ => Color.FromRgb(0xFF, 0x7A, 0x1A),
    };

    // ------------------------------------------------------------------ entering

    public static async Task ShowEnterAsync(AppSettings s)
    {
        CloseAll();
        Build();
        if (_root == null) return;
        try
        {
            var size = await WaitForSizeAsync(_root);
            _scene = new NeonScene(size, FrameColor(s), s.OverlayText);
            _root.Children.Add(_scene.Root);
            Sounds.Enter();
            await _scene.PlayAsync();
        }
        catch (Exception ex) { Logger.Error("Rest animation failed (skipped)", ex); }
        finally
        {
            // Nothing may keep running while resting: stop and remove every animation.
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
/// The neon frame: three strokes on the same rectangle (about 4.5 % inside the
/// screen edges) - a wide faint haze, a slightly jagged "flame" line with a
/// glow, and a thin hot core - drawn clockwise from the top-left corner with a
/// dash-offset animation (like a loading bar).
/// </summary>
internal sealed class NeonScene
{
    public Grid Root { get; } = new() { IsHitTestVisible = false };
    private readonly Path _core, _flame, _haze;
    private readonly TextBlock _text;
    private readonly DropShadowEffect _flameGlow;
    private readonly (Path path, double len)[] _strokes;

    public NeonScene(Size size, Color color, string text)
    {
        double mx = Math.Round(size.Width * 0.045), my = Math.Round(size.Height * 0.045);
        var rect = new Rect(mx, my, Math.Max(10, size.Width - 2 * mx), Math.Max(10, size.Height - 2 * my));
        var smooth = Clockwise(rect, 0, 0, out double perimeter);
        var jagged = Clockwise(rect, 4, 2.0, out double jaggedLength);

        Color hot = Lerp(color, Color.FromRgb(0xFF, 0xF4, 0xD2), 0.78);
        _haze = Stroke(smooth, Color.FromArgb(160, color.R, color.G, color.B), 16, perimeter);
        _haze.Opacity = 0.5;
        _haze.Effect = new BlurEffect { Radius = 26, RenderingBias = RenderingBias.Performance };
        _flame = Stroke(jagged, color, 5.2, jaggedLength);
        _flameGlow = new DropShadowEffect { Color = color, BlurRadius = 28, ShadowDepth = 0, Opacity = 1, RenderingBias = RenderingBias.Performance };
        _flame.Effect = _flameGlow;
        _core = Stroke(smooth, hot, 2.4, perimeter);
        _strokes = new[] { (_haze, perimeter), (_flame, jaggedLength), (_core, perimeter) };

        _text = new TextBlock
        {
            Text = text,
            FontFamily = ThemeManager.DisplayFont,
            FontSize = 26,
            FontWeight = FontWeights.Light,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0,
        };
        Root.Children.Add(_haze);
        Root.Children.Add(_flame);
        Root.Children.Add(_core);
        Root.Children.Add(_text);
    }

    /// <summary>Static frame for screenshots: 0 = nothing drawn, 1 = closed frame.</summary>
    public void SetProgress(double p, bool showText = false)
    {
        foreach (var (path, len) in _strokes) path.StrokeDashOffset = len / path.StrokeThickness * (1 - p);
        _text.Opacity = showText ? 1 : 0;
    }

    public async Task PlayAsync()
    {
        var draw = ThemeManager.Dur(1.7);
        var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
        foreach (var (path, len) in _strokes)
            path.BeginAnimation(Shape.StrokeDashOffsetProperty, new DoubleAnimation(len / path.StrokeThickness, 0, draw) { EasingFunction = ease });

        // Fiery flicker on the flame line while it draws and glows.
        var rnd = new Random();
        var flicker = new DoubleAnimationUsingKeyFrames { Duration = ThemeManager.Dur(3.4) };
        for (double t = 0; t < 3.4; t += 0.06)
            flicker.KeyFrames.Add(new LinearDoubleKeyFrame(0.72 + rnd.NextDouble() * 0.28, KeyTime.FromTimeSpan(ThemeManager.Dur(t))));
        _flame.BeginAnimation(UIElement.OpacityProperty, flicker);
        await Task.Delay(draw);

        // Closed: a short flare, then a slow dim while "Resting…" fades in and out.
        _flameGlow.BeginAnimation(DropShadowEffect.BlurRadiusProperty, new DoubleAnimation(28, 42, ThemeManager.Dur(0.25)) { AutoReverse = true });
        _haze.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.5, 0.8, ThemeManager.Dur(0.25)) { AutoReverse = true });
        await Task.Delay(ThemeManager.Dur(0.25));
        Root.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, 0.1, ThemeManager.Dur(1.45)) { EasingFunction = new SineEase() });
        var text = new DoubleAnimationUsingKeyFrames { Duration = ThemeManager.Dur(1.4) };
        text.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        text.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(ThemeManager.Dur(0.45)), new SineEase()));
        text.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(ThemeManager.Dur(0.9))));
        text.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(ThemeManager.Dur(1.4)), new SineEase()));
        _text.BeginAnimation(UIElement.OpacityProperty, text);
        await Task.Delay(ThemeManager.Dur(1.5));
    }

    /// <summary>Stops and removes every animation; the scene disappears.</summary>
    public void Stop()
    {
        foreach (var (path, _) in _strokes)
        {
            path.BeginAnimation(Shape.StrokeDashOffsetProperty, null);
            path.BeginAnimation(UIElement.OpacityProperty, null);
        }
        _flameGlow.BeginAnimation(DropShadowEffect.BlurRadiusProperty, null);
        _text.BeginAnimation(UIElement.OpacityProperty, null);
        Root.BeginAnimation(UIElement.OpacityProperty, null);
        Root.Visibility = Visibility.Collapsed;
    }

    private static Path Stroke(Geometry g, Color c, double thickness, double length)
    {
        double units = length / thickness + 2; // dash units are multiples of the stroke thickness
        return new Path
        {
            Data = g,
            Stroke = new SolidColorBrush(c),
            StrokeThickness = thickness,
            StrokeDashArray = new DoubleCollection { units, units },
            StrokeDashOffset = units,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            IsHitTestVisible = false,
        };
    }

    /// <summary>The rectangle as one clockwise figure from the top-left corner; optional small jitter for a flame-like edge.</summary>
    private static Geometry Clockwise(Rect r, double step, double jitter, out double length)
    {
        var g = new StreamGeometry();
        var rnd = new Random(42);
        length = 0;
        using (var c = g.Open())
        {
            c.BeginFigure(r.TopLeft, false, false);
            var corners = new[] { r.TopRight, r.BottomRight, r.BottomLeft, r.TopLeft };
            var from = r.TopLeft;
            var prev = r.TopLeft;
            foreach (var to in corners)
            {
                if (step > 0)
                {
                    double len = (to - from).Length;
                    var dir = (to - from) / len;
                    var normal = new Vector(-dir.Y, dir.X);
                    for (double d = step; d < len; d += step)
                    {
                        var p = from + dir * d + normal * ((rnd.NextDouble() * 2 - 1) * jitter);
                        c.LineTo(p, true, true);
                        length += (p - prev).Length;
                        prev = p;
                    }
                }
                c.LineTo(to, true, true);
                length += (to - prev).Length;
                prev = to;
                from = to;
            }
        }
        g.Freeze();
        return g;
    }

    private static Color Lerp(Color a, Color b, double t) =>
        Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
}
