using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using RestMode.Core;
using RestMode.Core.Fans;

namespace RestMode.UI.Wpf;

/// <summary>What the windows need from the running app.</summary>
internal interface IAppActions
{
    Task EnterFromUi(string trigger);
    void ShowSettings();
    void ShowStats();
    Task RestoreEverything();
    void ExitApp();
    RestModeController Controller { get; }
}

/// <summary>
/// The main screen: the big ornate "Rest" button (the most prominent element),
/// a glowing emblem above it, and a short menu below. Arrow keys / controller
/// move between the buttons; Enter / A presses.
/// </summary>
internal sealed class MainWindow : OrnateWindow
{
    private readonly IAppActions _app;
    private readonly TextBlock _status = new() { Style = ThemeManager.Style("Hint"), TextAlignment = TextAlignment.Center, Margin = new Thickness(40, 4, 40, 0) };
    private readonly TextBlock _status2 = new() { Style = ThemeManager.Style("Hint"), TextAlignment = TextAlignment.Center, Margin = new Thickness(40, 0, 40, 18) };
    private readonly OrnateButton _rest;
    private readonly System.Windows.Controls.Image _emblem;

    public MainWindow(IAppActions app) : base("RestMode", 1080, 920, showTitle: false)
    {
        _app = app;
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        KeyboardNavigation.SetDirectionalNavigation(stack, KeyboardNavigationMode.Cycle);

        stack.Children.Add(new TextBlock
        {
            Text = "RestMode",
            FontFamily = ThemeManager.ScriptAltFont,
            FontSize = 76,
            Foreground = (Brush)FindResource("TextBrush"),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, -6),
        });
        stack.Children.Add(new TextBlock
        {
            Text = "the quiet rest of a console, for your PC",
            Style = ThemeManager.Style("Hint"),
            FontStyle = FontStyles.Italic,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 10),
        });

        _emblem = new System.Windows.Controls.Image
        {
            Source = new DrawingImage(Ornaments.Emblem(ThemeManager.Accent)),
            Width = 128,
            Height = 134,
            Margin = new Thickness(0, 0, 0, 6),
            Effect = new DropShadowEffect { Color = ThemeManager.Glow, BlurRadius = 22, ShadowDepth = 0, Opacity = 0.75 },
        };
        stack.Children.Add(_emblem);

        _rest = new OrnateButton("Rest", 92, 250, big: true) { ToolTip = "Enter Rest Mode (Ctrl+Alt+Shift+R)" };
        _rest.Click += async (_, _) => await _app.EnterFromUi("Rest button");
        stack.Children.Add(_rest);

        stack.Children.Add(new FrameworkElement { Height = 6 });
        foreach (var (text, act) in new (string, Action)[]
                 {
                     ("Settings", () => _app.ShowSettings()),
                     ("Statistics", () => _app.ShowStats()),
                     ("Restore everything", () => _ = _app.RestoreEverything()),
                     ("Hide", Hide),
                 })
        {
            var b = new OrnateButton(text, 29, 110);
            b.Click += (_, _) => act();
            stack.Children.Add(b);
        }

        var dock = new DockPanel();
        var footer = new StackPanel();
        footer.Children.Add(_status);
        footer.Children.Add(_status2);
        var exit = Small("Exit RestMode", (_, _) => _app.ExitApp());
        exit.HorizontalAlignment = HorizontalAlignment.Center;
        exit.Margin = new Thickness(0, 0, 0, 14);
        footer.Children.Add(exit);
        DockPanel.SetDock(footer, Dock.Bottom);
        dock.Children.Add(footer);
        dock.Children.Add(stack);
        Body.Content = dock;

        Loaded += (_, _) => { _rest.Focus(); Refresh(); };
        Activated += (_, _) => Refresh();
        ThemeManager.Changed += OnTheme;
        Closed += (_, _) => ThemeManager.Changed -= OnTheme;
    }

    private void OnTheme()
    {
        _emblem.Source = new DrawingImage(Ornaments.Emblem(ThemeManager.Accent));
        if (_emblem.Effect is DropShadowEffect e) e.Color = ThemeManager.Glow;
    }

    /// <summary>The close button hides the window; RestMode keeps running in the tray.</summary>
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_reallyClose)
        {
            e.Cancel = true;
            Hide();
        }
        base.OnClosing(e);
    }

    private bool _reallyClose;
    public void CloseForGood()
    {
        _reallyClose = true;
        Close();
    }

    protected override void OnBack() => Hide();

    public void Refresh()
    {
        var s = SettingsStore.Current;
        var c = _app.Controller;
        var dev = c.Device;
        string fan;
        try { fan = FanPlanner.Resolve(s, dev).Describe(); } catch { fan = "?"; }
        _status.Text = $"This PC: {dev.ClassText}, {dev.CpuText} CPU, {dev.SleepText}{(dev.MonitorCount > 1 ? $", {dev.MonitorCount} monitors" : "")}.  Fans: {fan}.";
        var r = c.LastReading;
        string temps = r is { HasTemperature: true } ? $"Last temperatures: {r}.  " : "";
        _status2.Text = $"{temps}Hotkey: {(string.IsNullOrEmpty(s.Hotkey) ? "none" : s.Hotkey)}.  Wake with any key, mouse move{(s.WakeWithController ? " or controller button" : "")}" +
                        (dev.Caps.LidPresent && s.LidAction == LidBehavior.Rest ? ", or by opening the lid." : ".");
    }
}
