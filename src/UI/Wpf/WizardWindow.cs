using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RestMode.Core;
using RestMode.Core.Fans;
using RestMode.Elevation;

namespace RestMode.UI.Wpf;

/// <summary>
/// First-start guide: detects the device, explains what RestMode changes (and
/// that everything is put back), asks about the lid and Fan Control, offers the
/// no-prompt helper, and creates a backup of the current settings.
/// </summary>
internal sealed class WizardWindow : OrnateWindow
{
    private readonly IAppActions _app;
    private readonly AppSettings _s = SettingsStore.Current.Clone();
    private readonly DeviceProfile _dev;
    private readonly List<Func<FrameworkElement>> _pages = new();
    private readonly OrnateButton _next, _back;
    private int _index;
    private bool _startWithWindows = true;

    public WizardWindow(IAppActions app) : base("RestMode · Welcome", 1040, 780)
    {
        _app = app;
        _dev = app.Controller.Device;
        _pages.Add(Welcome);
        _pages.Add(WhatChanges);
        if (_dev.Caps.LidPresent) _pages.Add(Lid);
        _pages.Add(Fans);
        _pages.Add(Finish);

        _next = new OrnateButton("Next", 30, 80);
        _back = new OrnateButton("Back", 30, 80);
        _next.Click += (_, _) => Go(+1);
        _back.Click += (_, _) => Go(-1);
        var nav = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 16) };
        nav.Children.Add(_back);
        nav.Children.Add(_next);

        var dock = new DockPanel();
        DockPanel.SetDock(nav, Dock.Bottom);
        dock.Children.Add(nav);
        dock.Children.Add(_host);
        Body.Content = dock;
        Show(0);
    }

    private readonly ContentControl _host = new() { Margin = new Thickness(60, 0, 60, 0) };

    protected override void OnBack()
    {
        if (_index > 0) Go(-1); else Close();
    }

    private void Go(int delta)
    {
        if (_index + delta >= _pages.Count) { Finish_Click(); return; }
        Show(Math.Clamp(_index + delta, 0, _pages.Count - 1));
    }

    private void Show(int i)
    {
        _index = i;
        _host.Content = new ScrollViewer { Content = _pages[i](), VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };
        _back.Visibility = i == 0 ? Visibility.Hidden : Visibility.Visible;
        _next.Text = i == _pages.Count - 1 ? "Begin" : "Next";
        _next.Focus();
    }

    private StackPanel Page(string title)
    {
        var p = new StackPanel();
        p.Children.Add(Heading(title, 52));
        return p;
    }

    private FrameworkElement Welcome()
    {
        var p = Page("Welcome to RestMode");
        p.Children.Add(Label("Press Rest and your PC looks off - dark screen, no sound, quiet fans - but it keeps running, so downloads and updates carry on. Touch the mouse, a key or a controller and you're back exactly where you were."));
        p.Children.Add(SubHeading("Your PC"));
        p.Children.Add(Label(_dev.Summary() + "."));
        var fan = FanPlanner.Resolve(_s, _dev);
        p.Children.Add(Hint("Fans: " + fan.Describe() + ". " + string.Join(" ", fan.Notes)));
        if (_dev.Caps.ModernStandby)
            p.Children.Add(Hint("This PC uses Modern Standby. RestMode turns the screen off with a short timer so Windows stays fully on and the network stays connected."));
        return p;
    }

    private FrameworkElement WhatChanges()
    {
        var p = Page("What RestMode changes");
        p.Children.Add(Label("Only while resting, and always put back afterwards:"));
        foreach (var line in new[]
                 {
                     "· a temporary COPY of your power plan with a low CPU limit (your plan is never edited)",
                     "· the screen turns off and the sound is muted",
                     "· the fans: your PC's Quiet mode, a Fan Control profile or Windows' \"Best power efficiency\" - never 0 %",
                     "· the network adapter you use stays fully powered",
                     "· optional: chosen apps slowed down or paused",
                 })
            p.Children.Add(Label(line));
        p.Children.Add(SubHeading("Safety"));
        foreach (var line in new[]
                 {
                     "· overheat protection: fans back to normal, then sleep if it stays hot",
                     "· battery limit, and a warning when resting on battery",
                     "· a watchdog every 30 s: the PC stays awake, online and on the right plan",
                     "· after a crash, log-off or uninstall everything is restored; RestMode-Reset.cmd does it by hand",
                 })
            p.Children.Add(Label(line));
        return p;
    }

    private FrameworkElement Lid()
    {
        var p = Page("When you close the lid");
        RadioButton R(string text, LidBehavior b)
        {
            var r = new RadioButton { GroupName = "lid", Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap }, IsChecked = _s.LidAction == b };
            r.Checked += (_, _) => _s.LidAction = b;
            return r;
        }
        p.Children.Add(R("Rest - closing the lid enters Rest Mode, opening it wakes", LidBehavior.Rest));
        p.Children.Add(R("Sleep - Windows default", LidBehavior.Sleep));
        p.Children.Add(R("Do nothing", LidBehavior.DoNothing));
        p.Children.Add(Hint("For Rest and Do nothing, RestMode sets Windows' lid action to \"Do nothing\" while it runs and puts your original setting back exactly when you change this, exit RestMode or uninstall it. With the lid closed the overheat limit is stricter, and on battery the laptop sleeps at the battery limit. Never put a running laptop in a bag."));
        return p;
    }

    private FrameworkElement Fans()
    {
        var p = Page("Fans");
        var (prov, msg) = BuiltInFanModes.Detect();
        p.Children.Add(Label(msg));
        string? fc = FanControlSwitcher.NormalizeExe(_s.FanControlExe) ?? FanControlSwitcher.Detect();
        p.Children.Add(SubHeading("Fan Control"));
        if (fc != null)
        {
            p.Children.Add(Label("Found Fan Control: " + fc));
            if (string.IsNullOrWhiteSpace(_s.FanControlExe)) _s.FanControlExe = Path.GetDirectoryName(fc) ?? "";
            if (FanControlSwitcher.LocationWarning(fc) is string w) p.Children.Add(Warn(w));
            if (FanControlSwitcher.CheckRuntime(fc) is string rt) p.Children.Add(Warn(rt));
            p.Children.Add(Hint("Choose its Quiet and Normal configs later in Settings › Fans (and use the Test button)."));
        }
        else p.Children.Add(Hint("Not found - that's fine, it's optional. You can point RestMode to it later in Settings › Fans."));

        if (_dev.Account == AccountKind.Admin && !TaskInstaller.IsInstalled() && (prov is { Implemented: true } || fc != null || _s.KeepNetworkAdaptersPowered))
        {
            p.Children.Add(SubHeading("No prompts at night"));
            p.Children.Add(Hint("Fan modes and the network option need admin rights. Install the small helper task once and RestMode never has to ask again (it still never runs as admin all the time)."));
            p.Children.Add(Small("Install the helper task (one prompt)", async (sender, _) =>
            {
                bool ok = await HelperClient.InstallTaskAsync(_s);
                ((System.Windows.Controls.Button)sender).Content = ok ? "Helper task installed" : "Not installed - you can do it later in Settings › Safety";
            }));
        }
        return p;
    }

    private FrameworkElement Finish()
    {
        var p = Page("All set");
        var startup = new System.Windows.Controls.CheckBox { Content = "Start RestMode with Windows", IsChecked = _startWithWindows };
        startup.Checked += (_, _) => _startWithWindows = true;
        startup.Unchecked += (_, _) => _startWithWindows = false;
        p.Children.Add(startup);
        p.Children.Add(Label($"Rest any time with the big Rest button, {_s.Hotkey}, the tray icon or Start › \"RestMode – Rest now\"."));
        p.Children.Add(Hint("A backup of your current settings is saved when you press Begin (Settings › Backup & Reset)."));
        return p;
    }

    private void Finish_Click()
    {
        _s.FirstRunDone = true;
        SettingsStore.Save(_s);
        Util.StartupManager.SetEnabled(_startWithWindows, Util.Installer.RunningInstalledCopy ? Util.Installer.InstalledExe : null);
        BackupManager.Create(_dev, _s);
        Close();
    }
}
