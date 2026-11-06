using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using RestMode.Native;

namespace RestMode.UI.Wpf;

/// <summary>
/// The Rest overlay: every monitor goes pure black; on the main one an
/// original glowing emblem fades in at the lower right and a cursive
/// "Entering Rest Mode…" appears; after ~2.5 s it fades and the screen turns
/// off. The black windows then stay, completely still (nothing animates while
/// resting), until "Welcome back" fades in and out on wake.
/// </summary>
internal static class RestOverlay
{
    private static readonly List<Window> Windows = new();
    private static TextBlock? _text;
    private static System.Windows.Controls.Image? _emblem;

    public static bool IsShown => Windows.Count > 0;

    public static async Task ShowEnterAsync(string text)
    {
        CloseAll();
        Build();
        if (_text == null || _emblem == null) return;
        _text.Text = text;
        _text.Opacity = 0;
        _emblem.Opacity = 0;
        Sounds.Enter();
        Fade(_emblem, 1, 1.3);
        await Task.Delay(ThemeManager.Dur(0.45));
        Fade(_text, 1, 0.9);
        await Task.Delay(ThemeManager.Dur(1.9));
        Fade(_text, 0, 0.7);
        Fade(_emblem, 0, 0.9);
        await Task.Delay(ThemeManager.Dur(0.95));
        // From here on the overlay is a still black screen.
    }

    public static async void ShowWelcome(string text)
    {
        try
        {
            if (!IsShown) Build();
            if (_text == null) { CloseAll(); return; }
            _text.Text = text;
            _text.Opacity = 0;
            if (_emblem != null) _emblem.Opacity = 0;
            Sounds.Wake();
            Fade(_text, 1, 0.5);
            if (_emblem != null) Fade(_emblem, 0.7, 0.5);
            await Task.Delay(ThemeManager.Dur(1.3));
            foreach (var w in Windows) w.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, ThemeManager.Dur(0.6)));
            await Task.Delay(ThemeManager.Dur(0.65));
        }
        catch { /* cosmetic */ }
        CloseAll();
    }

    public static void CloseAll()
    {
        foreach (var w in Windows.ToList())
        {
            try { w.Close(); } catch { }
        }
        Windows.Clear();
        _text = null;
        _emblem = null;
    }

    private static void Fade(UIElement e, double to, double seconds) =>
        e.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(to, ThemeManager.Dur(seconds)) { EasingFunction = new SineEase() });

    private static void Build()
    {
        foreach (var screen in System.Windows.Forms.Screen.AllScreens)
        {
            var w = new Window
            {
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                Background = Brushes.Black,
                Topmost = true,
                ShowInTaskbar = false,
                ShowActivated = screen.Primary,
                Cursor = System.Windows.Input.Cursors.None,
                Left = -32000, Top = -32000, Width = 10, Height = 10,
                Title = "RestMode",
            };
            if (screen.Primary)
            {
                var grid = new Grid();
                _emblem = new System.Windows.Controls.Image
                {
                    Source = new DrawingImage(Ornaments.Emblem(ThemeManager.Accent, 0.9)),
                    Width = 230, Height = 240,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(0, 0, 90, 80),
                    Opacity = 0,
                    Effect = new DropShadowEffect { Color = ThemeManager.Glow, BlurRadius = 36, ShadowDepth = 0, Opacity = 0.95 },
                };
                _text = new TextBlock
                {
                    FontFamily = ThemeManager.ScriptFont,
                    FontSize = 58,
                    Foreground = new SolidColorBrush(ThemeManager.Text),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Opacity = 0,
                    Effect = new DropShadowEffect { Color = ThemeManager.Glow, BlurRadius = 14, ShadowDepth = 0, Opacity = 0.6 },
                };
                grid.Children.Add(_emblem);
                grid.Children.Add(_text);
                w.Content = grid;
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
