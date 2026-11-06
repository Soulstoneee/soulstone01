using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using RestMode.Native;

namespace RestMode.UI.Wpf;

/// <summary>
/// The Rest overlay: every monitor goes black and a single line of text
/// ("Entering Rest Mode…") fades in and out on the main one; then the screen
/// turns off. The black windows stay, completely still (nothing animates
/// while resting), until "Welcome back" fades in and out on wake.
/// </summary>
internal static class RestOverlay
{
    private static readonly List<Window> Windows = new();
    private static TextBlock? _text;

    public static bool IsShown => Windows.Count > 0;

    public static async Task ShowEnterAsync(string text)
    {
        CloseAll();
        Build();
        if (_text == null) return;
        _text.Text = text;
        _text.Opacity = 0;
        Sounds.Enter();
        Fade(_text, 1, 0.6);
        await Task.Delay(ThemeManager.Dur(2.0));
        Fade(_text, 0, 0.6);
        await Task.Delay(ThemeManager.Dur(0.65));
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
            Sounds.Wake();
            Fade(_text, 1, 0.4);
            await Task.Delay(ThemeManager.Dur(1.2));
            foreach (var w in Windows) w.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, ThemeManager.Dur(0.5)));
            await Task.Delay(ThemeManager.Dur(0.55));
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
                _text = new TextBlock
                {
                    FontFamily = ThemeManager.DisplayFont,
                    FontSize = 30,
                    FontWeight = FontWeights.Light,
                    Foreground = ThemeManager.Brush("TextBrush"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Opacity = 0,
                };
                w.Content = new Grid { Children = { _text } };
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
