using System.Windows;
using System.Windows.Controls;
using RestMode.Core;
using RestMode.Core.Fans;
using RestMode.Elevation;

namespace RestMode.UI.Wpf;

/// <summary>
/// First-start guide: detects the device, explains what RestMode changes (and
/// that everything is put back), asks about the lid and Fan Control, offers the
/// no-prompt helper, and creates a backup of the current settings.
/// </summary>
internal sealed class WizardWindow : AppWindow
{
    private readonly IAppActions _app;
    private readonly AppSettings _s = SettingsStore.Current.Clone();
    private readonly DeviceProfile _dev;
    private readonly List<Func<FrameworkElement>> _pages = new();
    private readonly System.Windows.Controls.Button _next, _back;
    private readonly ContentControl _host = new() { Margin = new Thickness(48, 6, 48, 0) };
    private int _index;
    private bool _startWithWindows = true;

    public WizardWindow(IAppActions app) : base("Welcome", 820, 620)
    {
        _app = app;
        _dev = app.Controller.Device;
        _pages.Add(Welcome);
        _pages.Add(WhatChanges);
        if (_dev.Caps.LidPresent) _pages.Add(Lid);
        _pages.Add(Fans);
        _pages.Add(Finish);

        _back = Flat("Back", (_, _) => Go(-1));
        _next = Flat("Next", (_, _) => Go(+1), "PrimaryButton");
        _next.FontSize = 13.5;
        _next.FontWeight = FontWeights.SemiBold;
        _next.Padding = new Thickness(24, 7, 24, 7);
        var nav = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 48, 24) };
        nav.Children.Add(_back);
        nav.Children.Add(_next);

        var dock = new DockPanel();
        DockPanel.SetDock(nav, Dock.Bottom);
        dock.Children.Add(nav);
        dock.Children.Add(_host);
        Body.Content = dock;
        Show(0);
    }

    protected override void OnBack()
    {
        if (_index > 0) Go(-1); else Close();
    }

    private void Go(int delta)
    {
        if (_index + delta >= _pages.Count) { Done(); return; }
        Show(Math.Clamp(_index + delta, 0, _pages.Count - 1));
    }

    private void Show(int i)
    {
        _index = i;
        _host.Content = new ScrollViewer { Content = _pages[i](), VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };
        _back.Visibility = i == 0 ? Visibility.Hidden : Visibility.Visible;
        _next.Content = i == _pages.Count - 1 ? "Done" : "Next";
        _next.Focus();
    }

    private static StackPanel Page(string title, int step, int of)
    {
        var p = new StackPanel();
        p.Children.Add(Secondary($"Step {step} of {of}"));
        p.Children.Add(Heading(title, 24));
        p.Children.Add(new FrameworkElement { Height = 12 });
        return p;
    }

    private StackPanel P(string title) => Page(title, _index + 1, _pages.Count);

    private static void Bullets(StackPanel p, IEnumerable<string> lines)
    {
        foreach (var l in lines)
        {
            var t = Text("–  " + l);
            t.Margin = new Thickness(0, 3, 0, 3);
            p.Children.Add(t);
        }
    }

    private FrameworkElement Welcome()
    {
        var p = P("Welcome to RestMode");
        p.Children.Add(Text("Press Rest and your PC looks off – dark screen, no sound, quiet fans – but it keeps running, so downloads and updates carry on. A key, the mouse or a controller brings you back exactly where you were."));
        p.Children.Add(Section("This PC"));
        p.Children.Add(Text(_dev.Summary() + "."));
        var fan = FanPlanner.Resolve(_s, _dev);
        p.Children.Add(Secondary("Fans: " + fan.Describe() + "."));
        if (_dev.Caps.ModernStandby)
            p.Children.Add(Secondary("This PC uses Modern Standby: RestMode turns the screen off with a short timer so Windows stays fully on and the network stays connected."));
        return p;
    }

    private FrameworkElement WhatChanges()
    {
        var p = P("What RestMode changes");
        p.Children.Add(Text("Only while resting – and always put back afterwards:"));
        Bullets(p, new[]
        {
            "a temporary copy of your power plan with a low CPU limit (your plan is never edited)",
            "the screen turns off and the sound is muted",
            "quieter fans: the PC's Quiet mode, a Fan Control profile or \"Best power efficiency\" – never 0 %",
            "the network adapter you use stays fully powered",
            "optional: chosen apps slowed down or paused",
        });
        p.Children.Add(Section("Safety"));
        Bullets(p, new[]
        {
            "overheat protection: fans back to normal, then sleep if it stays hot",
            "battery limit and a warning when resting on battery",
            "a watchdog every 30 s keeps the PC awake, online and on the right plan",
            "after a crash, sign-out or uninstall everything is restored; RestMode-Reset.cmd does it by hand",
        });
        return p;
    }

    private FrameworkElement Lid()
    {
        var p = P("When you close the lid");
        RadioButton R(string text, LidBehavior b)
        {
            var r = new RadioButton { GroupName = "lid", Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap }, IsChecked = _s.LidAction == b };
            r.Checked += (_, _) => _s.LidAction = b;
            return r;
        }
        p.Children.Add(R("Rest – closing the lid enters Rest Mode, opening it wakes", LidBehavior.Rest));
        p.Children.Add(R("Sleep – Windows default", LidBehavior.Sleep));
        p.Children.Add(R("Do nothing", LidBehavior.DoNothing));
        p.Children.Add(new FrameworkElement { Height = 8 });
        p.Children.Add(Secondary("For Rest and Do nothing, Windows' lid action is set to \"Do nothing\" while RestMode runs; your own setting is put back exactly when you change this, exit RestMode or uninstall it. With the lid closed the temperature limit is stricter, and on battery the laptop sleeps at the battery limit."));
        return p;
    }

    private FrameworkElement Fans()
    {
        var p = P("Fans");
        var (prov, msg) = BuiltInFanModes.Detect();
        p.Children.Add(Text(msg));
        string? fc = FanControlSwitcher.NormalizeExe(_s.FanControlExe) ?? FanControlSwitcher.Detect();
        p.Children.Add(Section("Fan Control"));
        if (fc != null)
        {
            p.Children.Add(Text("Found: " + fc));
            if (string.IsNullOrWhiteSpace(_s.FanControlExe)) _s.FanControlExe = Path.GetDirectoryName(fc) ?? "";
            if (FanControlSwitcher.LocationWarning(fc) is string w) p.Children.Add(Warn(w));
            if (FanControlSwitcher.CheckRuntime(fc) is string rt) p.Children.Add(Warn(rt));
            p.Children.Add(Secondary("Pick its Quiet and Normal configs later in Settings › Fans (with a Test button)."));
        }
        else p.Children.Add(Secondary("Not found – that's fine, it's optional. You can point RestMode to it later in Settings › Fans."));

        if (_dev.Account == AccountKind.Admin && !TaskInstaller.IsInstalled() && (prov is { Implemented: true } || fc != null || _s.KeepNetworkAdaptersPowered))
        {
            p.Children.Add(Section("No prompts at night"));
            p.Children.Add(Secondary("Fan modes and the network option need admin rights. Install the small helper task once and RestMode never asks again (it still never runs as admin all the time)."));
            var b = Flat("Install helper task (one prompt)", async (sender, _) =>
            {
                bool ok = await HelperClient.InstallTaskAsync(_s);
                ((System.Windows.Controls.Button)sender).Content = ok ? "Helper task installed" : "Not installed – you can do it later in Settings › Safety";
            });
            b.Margin = new Thickness(0, 10, 0, 0);
            p.Children.Add(b);
        }
        return p;
    }

    private FrameworkElement Finish()
    {
        var p = P("All set");
        var startup = new System.Windows.Controls.CheckBox { Content = "Start RestMode with Windows", IsChecked = _startWithWindows };
        startup.Checked += (_, _) => _startWithWindows = true;
        startup.Unchecked += (_, _) => _startWithWindows = false;
        p.Children.Add(startup);
        p.Children.Add(new FrameworkElement { Height = 14 });
        p.Children.Add(Text($"Rest any time with the Rest button, {_s.Hotkey}, the tray icon or Start › \"RestMode – Rest now\"."));
        p.Children.Add(Secondary("A backup of your current settings is saved when you press Done (Settings › Backup & reset)."));
        return p;
    }

    private void Done()
    {
        _s.FirstRunDone = true;
        SettingsStore.Save(_s);
        Util.StartupManager.SetEnabled(_startWithWindows, Util.Installer.RunningInstalledCopy ? Util.Installer.InstalledExe : null);
        BackupManager.Create(_dev, _s);
        Close();
    }
}
