using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using RestMode.Native;

namespace RestMode.UI.Wpf;

/// <summary>
/// Base for every RestMode window: pure black, the silver filigree background
/// (brightness from the settings), a minimal title bar (drag to move, minimise,
/// close), Esc to close, arrow-key and game-controller navigation.
/// </summary>
internal class OrnateWindow : Window
{
    private readonly System.Windows.Controls.Image _bg;
    protected readonly ContentControl Body = new();
    private readonly DispatcherTimer _pad = new() { Interval = TimeSpan.FromMilliseconds(90) };
    private ushort _lastButtons;

    public OrnateWindow(string title, double width, double height, bool showTitle = true)
    {
        Title = title;
        Width = width;
        Height = height;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.CanMinimize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Brushes.Black;
        BorderBrush = (Brush)FindResource("SilverDimBrush");
        BorderThickness = new Thickness(1);
        UseLayoutRounding = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
        try { Icon = AppIcon; } catch { /* no icon */ }

        _bg = new System.Windows.Controls.Image
        {
            Source = new DrawingImage(Ornaments.Background()),
            Stretch = Stretch.Fill,
            Opacity = ThemeManager.OrnamentOpacity,
            IsHitTestVisible = false,
        };
        RenderOptions.SetCachingHint(_bg, CachingHint.Cache);
        _bg.CacheMode = new BitmapCache { RenderAtScale = 1 };

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRowSpan(_bg, 2);
        root.Children.Add(_bg);

        var bar = new DockPanel { Margin = new Thickness(18, 10, 10, 0), Background = Brushes.Transparent };
        var close = GlyphButton("✕", "Close");
        close.Click += (_, _) => Close();
        var min = GlyphButton("–", "Minimise");
        min.Click += (_, _) => WindowState = WindowState.Minimized;
        DockPanel.SetDock(close, Dock.Right);
        DockPanel.SetDock(min, Dock.Right);
        bar.Children.Add(close);
        bar.Children.Add(min);
        if (showTitle)
            bar.Children.Add(new TextBlock { Text = title, FontFamily = ThemeManager.ScriptFont, FontSize = 24, Foreground = (Brush)FindResource("HintBrush"), VerticalAlignment = VerticalAlignment.Center });
        bar.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) try { DragMove(); } catch { } };
        root.Children.Add(bar);

        Grid.SetRow(Body, 1);
        root.Children.Add(Body);
        Content = root;

        KeyboardNavigation.SetDirectionalNavigation(this, KeyboardNavigationMode.Cycle);
        KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.Cycle);
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { OnBack(); e.Handled = true; }
        };
        ThemeManager.Changed += OnTheme;
        Closed += (_, _) => { ThemeManager.Changed -= OnTheme; _pad.Stop(); };
        _pad.Tick += (_, _) => PollPad();
        Activated += (_, _) => _pad.Start();
        Deactivated += (_, _) => _pad.Stop();
    }

    public static ImageSource AppIcon { get; } = LoadIcon();

    private static ImageSource LoadIcon()
    {
        var d = BitmapFrame.Create(new Uri("pack://application:,,,/RestMode;component/RestMode.ico"));
        return d;
    }

    /// <summary>Esc / controller B. Default: close the window.</summary>
    protected virtual void OnBack() => Close();

    private void OnTheme()
    {
        _bg.Opacity = ThemeManager.OrnamentOpacity;
    }

    private System.Windows.Controls.Button GlyphButton(string glyph, string tip)
    {
        var b = new System.Windows.Controls.Button
        {
            Content = glyph,
            ToolTip = tip,
            Style = (Style)FindResource("SmallButton"),
            FontSize = 15,
            Padding = new Thickness(9, 1, 9, 2),
            Margin = new Thickness(6, 0, 0, 0),
            IsTabStop = false,
            Focusable = false,
        };
        return b;
    }

    // ------------------------------------------------------------------ game controller

    private void PollPad()
    {
        if (!IsActive) return;
        ushort buttons = 0;
        try
        {
            for (uint i = 0; i < 4; i++)
                if (NativeMethods.XInputGetState(i, out var st) == 0) buttons |= st.Gamepad.wButtons;
        }
        catch { _pad.Stop(); return; }

        ushort pressed = (ushort)(buttons & ~_lastButtons);
        _lastButtons = buttons;
        if (pressed == 0) return;

        var focused = Keyboard.FocusedElement as UIElement;
        if (focused == null || !IsAncestorOf(focused as DependencyObject ?? this))
        {
            MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
            return;
        }
        if ((pressed & NativeMethods.XINPUT_DPAD_UP) != 0) focused.MoveFocus(new TraversalRequest(FocusNavigationDirection.Up));
        else if ((pressed & NativeMethods.XINPUT_DPAD_DOWN) != 0) focused.MoveFocus(new TraversalRequest(FocusNavigationDirection.Down));
        else if ((pressed & NativeMethods.XINPUT_DPAD_LEFT) != 0) focused.MoveFocus(new TraversalRequest(FocusNavigationDirection.Left));
        else if ((pressed & NativeMethods.XINPUT_DPAD_RIGHT) != 0) focused.MoveFocus(new TraversalRequest(FocusNavigationDirection.Right));
        else if ((pressed & NativeMethods.XINPUT_A) != 0) ActivateElement(focused);
        else if ((pressed & NativeMethods.XINPUT_B) != 0) OnBack();
    }

    private static void ActivateElement(UIElement e)
    {
        switch (e)
        {
            case System.Windows.Controls.ComboBox cb: cb.IsDropDownOpen = !cb.IsDropDownOpen; break;
            case ToggleButton t when t is System.Windows.Controls.RadioButton: t.IsChecked = true; break;
            case ToggleButton t: t.IsChecked = !(t.IsChecked ?? false); break;
            case ButtonBase b: b.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); break;
        }
    }

    // ------------------------------------------------------------------ helpers for page building

    protected static TextBlock Heading(string t, double size = 44) => new() { Text = t, Style = ThemeManager.Style("Heading"), FontSize = size };
    protected static TextBlock SubHeading(string t) => new() { Text = t, Style = ThemeManager.Style("SubHeading") };
    protected static TextBlock Label(string t) => new() { Text = t, Style = ThemeManager.Style("Label") };
    protected static TextBlock Hint(string t) => new() { Text = t, Style = ThemeManager.Style("Hint") };
    protected static TextBlock Warn(string t) => new() { Text = t, Style = ThemeManager.Style("Warning") };

    protected static System.Windows.Controls.Button Small(string text, RoutedEventHandler onClick)
    {
        var b = new System.Windows.Controls.Button { Content = text, Style = ThemeManager.Style("SmallButton"), HorizontalAlignment = HorizontalAlignment.Left };
        b.Click += onClick;
        return b;
    }
}
