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
/// Base for every RestMode window: near-black background, a thin border, a
/// minimal title bar (drag to move, minimise, close), Esc to go back / close,
/// arrow-key and game-controller navigation (D-pad moves, A presses, B goes back).
/// </summary>
internal class AppWindow : Window
{
    protected readonly ContentControl Body = new();
    private readonly DispatcherTimer _pad = new() { Interval = TimeSpan.FromMilliseconds(90) };
    private ushort _lastButtons;

    public AppWindow(string title, double width, double height, bool showTitle = true)
    {
        Title = title;
        Width = width;
        Height = height;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.CanMinimize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = ThemeManager.Brush("BackgroundBrush");
        BorderBrush = ThemeManager.Brush("BorderBrush");
        BorderThickness = new Thickness(1);
        FontFamily = ThemeManager.Font;
        Foreground = ThemeManager.Brush("TextBrush");
        UseLayoutRounding = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
        try { Icon = AppIcon; } catch { /* no icon */ }

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var bar = new DockPanel { Margin = new Thickness(20, 10, 10, 0), Background = Brushes.Transparent, Height = 36 };
        var close = IconButton(Icons.Close, "Close", (_, _) => Close());
        close.IsTabStop = false;
        var min = IconButton(Icons.Minimize, "Minimise", (_, _) => WindowState = WindowState.Minimized);
        min.IsTabStop = false;
        DockPanel.SetDock(close, Dock.Right);
        DockPanel.SetDock(min, Dock.Right);
        bar.Children.Add(close);
        bar.Children.Add(min);
        TopBarRight = new StackPanel { Orientation = Orientation.Horizontal };
        DockPanel.SetDock(TopBarRight, Dock.Right);
        bar.Children.Add(TopBarRight);
        if (showTitle)
            bar.Children.Add(new TextBlock { Text = title, Style = ThemeManager.Style("Secondary"), FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0) });
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
        Closed += (_, _) => _pad.Stop();
        _pad.Tick += (_, _) => PollPad();
        Activated += (_, _) => _pad.Start();
        Deactivated += (_, _) => _pad.Stop();
    }

    /// <summary>Extra icon buttons in the title bar (left of minimise / close).</summary>
    protected readonly StackPanel TopBarRight;

    public static ImageSource AppIcon { get; } = BitmapFrame.Create(new Uri("pack://application:,,,/RestMode;component/RestMode.ico"));

    /// <summary>Esc / controller B. Default: close the window.</summary>
    protected virtual void OnBack() => Close();

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

    // ------------------------------------------------------------------ building blocks

    protected static TextBlock Heading(string t, double size = 26) => new() { Text = t, Style = ThemeManager.Style("Title"), FontSize = size };
    protected static TextBlock Section(string t) => new() { Text = t.ToUpperInvariant(), Style = ThemeManager.Style("Section") };
    protected static TextBlock Text(string t) => new() { Text = t, Style = ThemeManager.Style("Text") };
    protected static TextBlock Secondary(string t) => new() { Text = t, Style = ThemeManager.Style("Secondary") };
    protected static TextBlock Warn(string t) => new() { Text = t, Style = ThemeManager.Style("Warning") };

    public static System.Windows.Controls.Button Flat(string text, RoutedEventHandler onClick, string style = "FlatButton")
    {
        var b = new System.Windows.Controls.Button { Content = text, Style = ThemeManager.Style(style) };
        b.Click += onClick;
        return b;
    }

    public static System.Windows.Controls.Button IconButton(Geometry icon, string tip, RoutedEventHandler onClick)
    {
        var b = new System.Windows.Controls.Button { Content = Icons.Make(icon, 16), ToolTip = tip, Style = ThemeManager.Style("IconButton") };
        b.Click += onClick;
        return b;
    }
}
