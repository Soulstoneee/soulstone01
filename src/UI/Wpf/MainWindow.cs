using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using RestMode.Core;
using RestMode.Core.Fans;

namespace RestMode.UI.Wpf;

/// <summary>What the windows need from the running app.</summary>
internal interface IAppActions
{
    Task EnterFromUi(string trigger);
    void ShowSettings(string? page = null);
    void ShowStats();
    Task RestoreEverything();
    void ExitApp();
    RestModeController Controller { get; }
}

/// <summary>
/// The main screen: one large, simple "Rest" button in the centre and a few
/// lines of status below it (device, fan method, temperatures). Settings and
/// info are small icons in the title bar; arrow keys / controller move between
/// the buttons, Enter / A presses.
/// </summary>
internal sealed class MainWindow : AppWindow
{
    private readonly IAppActions _app;
    private readonly TextBlock _device = Status();
    private readonly TextBlock _fans = Status();
    private readonly TextBlock _temps = Status();
    private readonly TextBlock _hint = Status();
    private readonly System.Windows.Controls.Button _rest;

    public MainWindow(IAppActions app) : base("RestMode", 760, 560)
    {
        _app = app;
        TopBarRight.Children.Add(IconButton(Icons.Info, "About RestMode", (_, _) => _app.ShowSettings("About")));
        TopBarRight.Children.Add(IconButton(Icons.Settings, "Settings", (_, _) => _app.ShowSettings()));

        _rest = new System.Windows.Controls.Button
        {
            Content = "Rest",
            Style = ThemeManager.Style("PrimaryButton"),
            Width = 300,
            Height = 112,
            ToolTip = "Enter Rest Mode",
        };
        _rest.Click += async (_, _) => await _app.EnterFromUi("Rest button");

        var center = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        center.Children.Add(_rest);
        center.Children.Add(new FrameworkElement { Height = 28 });
        center.Children.Add(_device);
        center.Children.Add(_fans);
        center.Children.Add(_temps);
        center.Children.Add(_hint);

        var links = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 22) };
        links.Children.Add(Flat("Statistics", (_, _) => _app.ShowStats(), "LinkButton"));
        links.Children.Add(Flat("Restore everything", async (_, _) => await _app.RestoreEverything(), "LinkButton"));
        links.Children.Add(Flat("Exit RestMode", (_, _) => _app.ExitApp(), "LinkButton"));

        var dock = new DockPanel();
        DockPanel.SetDock(links, Dock.Bottom);
        dock.Children.Add(links);
        dock.Children.Add(center);
        KeyboardNavigation.SetDirectionalNavigation(dock, KeyboardNavigationMode.Cycle);
        Body.Content = dock;

        Loaded += (_, _) => { _rest.Focus(); Refresh(); };
        Activated += (_, _) => Refresh();
    }

    private static TextBlock Status() => new()
    {
        Style = ThemeManager.Style("Secondary"),
        TextAlignment = TextAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center,
        Margin = new Thickness(0, 3, 0, 0),
        MaxWidth = 640,
    };

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
        _device.Text = $"{Cap(dev.ClassText)} · {dev.CpuText} CPU · {dev.SleepText}{(dev.MonitorCount > 1 ? $" · {dev.MonitorCount} monitors" : "")}";
        _fans.Text = $"Fans: {fan}";
        var r = c.LastReading;
        _temps.Text = r is { HasTemperature: true }
            ? $"CPU {(r.CpuC is double cpu ? $"{cpu:0} °C" : "–")} · GPU {(r.GpuC is double gpu ? $"{gpu:0} °C" : "–")}" +
              (r.FanRpm is { Count: > 0 } f ? $" · fans {string.Join(" / ", f)} RPM" : "")
            : "Temperatures: shown after the first Rest";
        _hint.Text = $"{(string.IsNullOrEmpty(s.Hotkey) ? "No hotkey" : s.Hotkey)} · wake with any key or mouse move" +
                     (dev.Caps.LidPresent && s.LidAction == LidBehavior.Rest ? " · or open the lid" : "");
    }

    private static string Cap(string t) => t.Length > 0 ? char.ToUpper(t[0]) + t[1..] : t;
}
