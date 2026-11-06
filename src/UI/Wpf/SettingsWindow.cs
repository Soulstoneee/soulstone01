using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using RestMode.Core;
using RestMode.Core.Fans;
using RestMode.Core.Safety;
using RestMode.Elevation;
using RestMode.Util;
using CheckBox = System.Windows.Controls.CheckBox;
using ComboBox = System.Windows.Controls.ComboBox;
using RadioButton = System.Windows.Controls.RadioButton;
using TextBox = System.Windows.Controls.TextBox;

namespace RestMode.UI.Wpf;

/// <summary>
/// Settings: a clean list. Pages on the left, one row per setting on the right
/// (name + short explanation, control on the right). Every option can be
/// overridden. Changes apply on Save.
/// </summary>
internal sealed class SettingsWindow : AppWindow
{
    private readonly IAppActions _app;
    private readonly AppSettings _s = SettingsStore.Current.Clone();
    private readonly DeviceProfile _dev;
    private readonly ContentControl _page = new();
    private readonly Dictionary<string, Func<FrameworkElement>> _pages = new();
    private readonly Dictionary<string, System.Windows.Controls.Button> _nav = new();
    private readonly List<Action> _apply = new();
    private bool _saved;

    public SettingsWindow(IAppActions app, string? startPage = null) : base("Settings", 980, 720)
    {
        _app = app;
        _dev = app.Controller.Device;

        _pages["General"] = GeneralPage;
        _pages["Rest & lid"] = RestPage;
        _pages["Power"] = PowerPage;
        _pages["Downloads"] = DownloadsPage;
        _pages["Fans"] = FansPage;
        _pages["Safety"] = SafetyPage;
        _pages["Schedule & apps"] = SchedulePage;
        _pages["Look"] = LookPage;
        _pages["Backup & reset"] = BackupPage;
        _pages["About"] = AboutPage;

        var nav = new StackPanel { Margin = new Thickness(20, 18, 10, 10) };
        KeyboardNavigation.SetDirectionalNavigation(nav, KeyboardNavigationMode.Cycle);
        foreach (var name in _pages.Keys)
        {
            var b = new System.Windows.Controls.Button { Content = name, Style = ThemeManager.Style("NavButton") };
            b.Click += (_, _) => Show(name);
            _nav[name] = b;
            nav.Children.Add(b);
        }

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 24, 18) };
        buttons.Children.Add(Flat("Cancel", (_, _) => Close()));
        var save = Flat("Save", (_, _) => SaveAndClose(), "PrimaryButton");
        save.FontSize = 13.5;
        save.FontWeight = FontWeights.SemiBold;
        save.Padding = new Thickness(22, 7, 22, 7);
        save.Margin = new Thickness(0);
        buttons.Children.Add(save);

        var scroll = new ScrollViewer
        {
            Content = _page,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Margin = new Thickness(14, 10, 14, 0),
            Focusable = false,
        };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var divider = new Border { BorderBrush = ThemeManager.Brush("BorderBrush"), BorderThickness = new Thickness(0, 0, 1, 0), Margin = new Thickness(0, 14, 0, 0) };
        Grid.SetRowSpan(divider, 2);
        Grid.SetColumn(scroll, 1);
        Grid.SetRow(buttons, 1);
        Grid.SetColumn(buttons, 1);
        grid.Children.Add(divider);
        grid.Children.Add(nav);
        grid.Children.Add(scroll);
        grid.Children.Add(buttons);
        Body.Content = grid;

        string first = startPage != null && _pages.ContainsKey(startPage) ? startPage : "General";
        Show(first);
        Loaded += (_, _) => _nav[first].Focus();
        Closed += (_, _) => { if (!_saved) ThemeManager.Apply(SettingsStore.Current); };
    }

    private void Show(string name)
    {
        foreach (var (k, b) in _nav) b.Tag = k == name ? "selected" : null;
        _page.Content = _pages[name]();
    }

    // ================================================================= pages

    private StackPanel Page(string title)
    {
        var p = new StackPanel { Margin = new Thickness(16, 12, 16, 24), MaxWidth = 700, HorizontalAlignment = HorizontalAlignment.Left };
        p.Children.Add(Heading(title, 24));
        return p;
    }

    private FrameworkElement GeneralPage()
    {
        var p = Page("General");
        var hotkey = new TextBox { Text = _s.Hotkey, Width = 220, IsReadOnly = true };
        hotkey.PreviewKeyDown += (_, e) =>
        {
            e.Handled = true;
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key is Key.Back or Key.Delete) { hotkey.Text = ""; _s.Hotkey = ""; return; }
            if (key is Key.Tab or Key.Escape) { e.Handled = false; return; }
            var winKey = (System.Windows.Forms.Keys)KeyInterop.VirtualKeyFromKey(key);
            var mods = Keyboard.Modifiers;
            if (mods.HasFlag(ModifierKeys.Control)) winKey |= System.Windows.Forms.Keys.Control;
            if (mods.HasFlag(ModifierKeys.Alt)) winKey |= System.Windows.Forms.Keys.Alt;
            if (mods.HasFlag(ModifierKeys.Shift)) winKey |= System.Windows.Forms.Keys.Shift;
            var text = HotkeyParser.Format(winKey);
            if (text != null && HotkeyParser.TryParse(text, out uint _, out System.Windows.Forms.Keys _)) { hotkey.Text = text; _s.Hotkey = text; }
        };
        p.Children.Add(Section("Rest Mode"));
        p.Children.Add(Row("Hotkey", "Click the box and press the keys (Ctrl / Alt / Shift + a key, or an F-key). Backspace = none.", hotkey));
        bool startup = StartupManager.IsEnabled();
        p.Children.Add(Row("Start with Windows", null, Toggle(startup, v => startup = v)));
        _apply.Add(() => StartupManager.SetEnabled(startup, Installer.RunningInstalledCopy ? Installer.InstalledExe : null));
        p.Children.Add(Row("Mute the sound while resting", null, Toggle(_s.MuteAudio, v => _s.MuteAudio = v)));
        p.Children.Add(Row("Notifications", null, Toggle(_s.ShowNotifications, v => _s.ShowNotifications = v)));

        p.Children.Add(Section("Waking up"));
        p.Children.Add(Row("Controller button wakes the PC", "Xbox / XInput controllers (PlayStation pads via DS4Windows or Steam Input).", Toggle(_s.WakeWithController, v => _s.WakeWithController = v)));
        p.Children.Add(Row("Mouse movement needed", "0 = any movement. A few pixels ignores a shaky desk.", Slider(0, 50, _s.MouseMoveThresholdPx, v => _s.MouseMoveThresholdPx = (int)v, v => v == 0 ? "any" : $"{v:0} px")));

        p.Children.Add(Section("Shortcuts"));
        p.Children.Add(Row("Desktop shortcut", "\"RestMode – Rest now\" on the desktop.",
            Flat("Create", (_, _) => Info(ShortcutHelper.CreateDesktopShortcut() ? "Shortcut created on your desktop." : "Could not create the shortcut (see the log)."))));
        p.Children.Add(Row("Start menu shortcut", "\"RestMode – Rest now\" in the Start menu.",
            Flat("Create", (_, _) => Info(ShortcutHelper.CreateStartMenuRestShortcut() ? "Added to the Start menu." : "Could not create the shortcut (see the log)."))));

        p.Children.Add(Section("Install"));
        if (Installer.RunningInstalledCopy)
            p.Children.Add(Row("Installed for this user", $"{Installer.InstallDir}. Uninstall from Windows Settings › Apps.", null));
        else
            p.Children.Add(Row("Install for this user", "Optional - RestMode.exe is portable. Installing adds Start menu entries and an Apps-list entry (no admin needed).",
                Flat("Install", (_, _) =>
                {
                    try { Process.Start(new ProcessStartInfo(AppPaths.ExePath, "--install") { UseShellExecute = false }); }
                    catch (Exception ex) { Info("Install failed: " + ex.Message); }
                })));
        return p;
    }

    private FrameworkElement RestPage()
    {
        var p = Page("Rest & lid");
        p.Children.Add(Section("When I close the lid"));
        if (_dev.Caps.LidPresent)
        {
            var group = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
            group.Children.Add(Radio("lid", "Rest – closing the lid enters Rest Mode, opening it wakes", _s.LidAction == LidBehavior.Rest, () => _s.LidAction = LidBehavior.Rest));
            group.Children.Add(Radio("lid", "Sleep – Windows default (RestMode doesn't touch the lid)", _s.LidAction == LidBehavior.Sleep, () => _s.LidAction = LidBehavior.Sleep));
            group.Children.Add(Radio("lid", "Do nothing", _s.LidAction == LidBehavior.DoNothing, () => _s.LidAction = LidBehavior.DoNothing));
            p.Children.Add(group);
            p.Children.Add(Secondary("For Rest and Do nothing, Windows' lid action is set to \"Do nothing\" while RestMode runs. Your own setting is saved first and put back exactly when you choose Sleep, exit RestMode or uninstall it. " +
                                     "With the lid closed the temperature limit is 5 °C stricter and the laptop sleeps if it gets too hot; on battery it sleeps at the battery limit. Don't put a running laptop in a bag."));
        }
        else p.Children.Add(Secondary("This PC has no lid."));

        p.Children.Add(Section("Power button"));
        p.Children.Add(Secondary("RestMode doesn't use the power button: when its action is \"Do nothing\", Windows doesn't tell programs it was pressed, so there's no safe way to turn it into a Rest button that can always be undone. Use the Rest button, the hotkey, the tray icon or the Start menu shortcut."));

        p.Children.Add(Section("Screen"));
        p.Children.Add(Row("Rest animation", "A short \"Entering Rest Mode…\" fade before the screen turns off.", Toggle(_s.RestAnimation, v => _s.RestAnimation = v)));
        p.Children.Add(Row("Text when entering", null, Text(_s.OverlayText, v => _s.OverlayText = v, width: 260)));
        p.Children.Add(Row("Text when waking", null, Text(_s.WelcomeText, v => _s.WelcomeText = v, width: 260)));
        p.Children.Add(Row("Preview", null, Flat("Show", async (_, _) =>
        {
            await RestOverlay.ShowEnterAsync(_s.OverlayText);
            await Task.Delay(500);
            RestOverlay.ShowWelcome(_s.WelcomeText);
        })));
        return p;
    }

    private FrameworkElement PowerPage()
    {
        var p = Page("Power");
        p.Children.Add(Section("While resting"));
        p.Children.Add(Row("Maximum processor speed", "20–30 % keeps the PC cool and quiet and is plenty for downloads.", Slider(5, 100, _s.CpuMaxPercent, v => _s.CpuMaxPercent = (int)v, v => $"{v:0} %")));
        p.Children.Add(Row("Minimum processor speed", null, Slider(0, 50, _s.CpuMinPercent, v => _s.CpuMinPercent = (int)v, v => $"{v:0} %")));
        p.Children.Add(Row("Turn off hard disks after", "Spinning disks stop when idle; a running transfer keeps them on.", Slider(0, 1800, _s.DiskIdleSeconds, v => _s.DiskIdleSeconds = (int)v, v => v == 0 ? "never" : $"{v:0} s")));
        if (_dev.UsesBattery)
            p.Children.Add(Row("Stricter on battery", "10 % lower processor speed when not plugged in.", Toggle(_s.StricterOnBattery, v => _s.StricterOnBattery = v)));

        p.Children.Add(Section("This PC"));
        p.Children.Add(Text(_dev.Summary() + "."));
        p.Children.Add(Secondary((_dev.UsesBattery
                                     ? "The Rest Mode plan is a temporary copy of your plan with plugged-in and battery values."
                                     : "Desktop: the Rest Mode plan is a temporary copy of your plan with plugged-in values only.") +
                                 " Your own plan is never edited." +
                                 (_dev.Caps.ModernStandby ? $" Modern Standby PC: the screen turns off {PowerPlanManager.ModernStandbyScreenOffSeconds} s after entering (an instant \"monitor off\" would make Windows go to standby and cut the network)." : "")));
        return p;
    }

    private FrameworkElement DownloadsPage()
    {
        var p = Page("Downloads");
        p.Children.Add(Section("Downloads are finished when"));
        p.Children.Add(Row("Network is below", "Download + upload, KB/s.", Number(_s.IdleThresholdKBps, 1, 1_000_000, v => _s.IdleThresholdKBps = v)));
        p.Children.Add(Row("Disk is below", "KB/s (game updates unpack after downloading). 0 = ignore the disk.", Number(_s.DiskThresholdKBps, 0, 10_000_000, v => _s.DiskThresholdKBps = v)));
        p.Children.Add(Row("For", null, Slider(1, 120, _s.IdleMinutes, v => _s.IdleMinutes = (int)v, v => $"{v:0} min")));
        p.Children.Add(Row("Then", null, Combo(new[] { "Sleep", "Hibernate", "Shut down (60 s warning)", "Stay in Rest Mode" }, (int)_s.EndAction, i => _s.EndAction = (EndAction)i)));

        p.Children.Add(Section("Watch chosen apps"));
        p.Children.Add(Secondary("One program per line (e.g. steam, qbittorrent, EpicGamesLauncher). While any of them runs, Rest Mode ends when their transfers stop, ignoring other background traffic."));
        p.Children.Add(Text(string.Join("\n", _s.WatchApps), v => _s.WatchApps = v.Split('\n', ',').Select(x => x.Trim()).Where(x => x.Length > 0).ToList(), multiline: true));
        return p;
    }

    private FrameworkElement FansPage()
    {
        var p = Page("Fans");
        var plan = Secondary("");
        void UpdatePlan()
        {
            try
            {
                var r = FanPlanner.Resolve(_s, _dev);
                plan.Text = "On this PC Rest Mode will use: " + r.Describe() + (r.NeedsAdmin ? " (needs admin approval, or the helper task on the Safety page)." : ".");
            }
            catch { plan.Text = ""; }
        }
        p.Children.Add(Section("Method"));
        p.Children.Add(Row("Quieten the fans with", "Always on top: the CPU limit and passive cooling of the Rest Mode plan. Fans are never set to 0 %.",
            Combo(new[] { "Auto", "Built-in modes only", "Fan Control only", "Windows power mode only", "Off" }, (int)_s.FanMode, i => { _s.FanMode = (FanStrategy)i; UpdatePlan(); })));
        p.Children.Add(Secondary("Auto: the PC's built-in modes if found, else Fan Control (if set up and running), else Windows' \"Best power efficiency\"."));
        p.Children.Add(plan);

        p.Children.Add(Section("Built-in fan modes"));
        var builtInStatus = Secondary(BuiltInFanModes.Detect().message);
        p.Children.Add(Row("Use the PC's own Quiet mode", "Supported: Lenovo Legion / LOQ (official WMI). Detected but not supported yet: ASUS, MSI, HP, Dell / Alienware, Acer, Razer.",
            Toggle(_s.UseBuiltInFanModes, v => { _s.UseBuiltInFanModes = v; UpdatePlan(); })));
        p.Children.Add(Row("Detect", null, Flat("Detect now", (_, _) => { builtInStatus.Text = BuiltInFanModes.Detect(refresh: true).message; UpdatePlan(); })));
        p.Children.Add(builtInStatus);

        p.Children.Add(Section("Fan Control (optional)"));
        var path = new TextBox { Text = _s.FanControlExe, HorizontalAlignment = HorizontalAlignment.Stretch };
        var status = Secondary("");
        var warn = Warn("");
        var quiet = new ComboBox { IsEditable = true, Text = _s.FanQuietConfig, Width = 260 };
        var normal = new ComboBox { IsEditable = true, Text = _s.FanNormalConfig, Width = 260 };
        void Refresh()
        {
            _s.FanControlExe = path.Text.Trim();
            string? exe = FanControlSwitcher.NormalizeExe(_s.FanControlExe) ?? (string.IsNullOrWhiteSpace(_s.FanControlExe) ? FanControlSwitcher.Detect() : null);
            var ok = new List<string>();
            var bad = new List<string>();
            if (exe == null)
            {
                if (string.IsNullOrWhiteSpace(_s.FanControlExe)) ok.Add("Fan Control not found (that's fine – it's optional).");
                else bad.Add("No FanControl.exe at this path. Choose the Fan Control folder or FanControl.exe.");
            }
            else
            {
                ok.Add((string.IsNullOrWhiteSpace(_s.FanControlExe) ? "Found: " : "OK: ") + exe);
                if (FanControlSwitcher.CheckRuntime(exe) is string rt) bad.Add(rt); else ok.Add(".NET runtime: OK.");
                ok.Add(FanControlSwitcher.IsRunning() ? "Fan Control is running." : "Fan Control is not running (RestMode only switches it while it runs; it never starts it).");
                var configs = FanControlSwitcher.ListConfigs(exe);
                if (configs.Count == 0) ok.Add("No configs saved yet: in Fan Control, save a \"Quiet\" and a \"Normal\" config.");
                foreach (var cb in new[] { quiet, normal })
                {
                    string cur = cb.Text;
                    cb.Items.Clear();
                    foreach (var c in configs) cb.Items.Add(c);
                    cb.Text = cur;
                }
                if (FanControlSwitcher.LocationWarning(exe) is string w) bad.Add("Warning: " + w);
            }
            status.Text = string.Join("\n", ok);
            warn.Text = string.Join("\n", bad);
            warn.Visibility = bad.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            UpdatePlan();
        }
        path.LostFocus += (_, _) => Refresh();
        p.Children.Add(Text("FanControl.exe or its folder (empty = find it automatically)"));
        p.Children.Add(new FrameworkElement { Height = 6 });
        p.Children.Add(path);
        var row = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        row.Children.Add(Flat("Browse…", (_, _) =>
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "FanControl.exe|FanControl.exe|Programs|*.exe", Title = "Find FanControl.exe" };
            if (dlg.ShowDialog(this) == true) { path.Text = dlg.FileName; Refresh(); }
        }));
        row.Children.Add(Flat("Folder…", (_, _) =>
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Choose the Fan Control folder (the one with FanControl.exe)" };
            if (dlg.ShowDialog(this) == true) { path.Text = dlg.FolderName; Refresh(); }
        }));
        row.Children.Add(Flat("Detect", (_, _) =>
        {
            var found = FanControlSwitcher.Detect();
            if (found != null) { path.Text = found; Refresh(); }
            else Info("Fan Control was not found in Program Files, the Desktop, Downloads or C:\\Tools. Use Browse or Folder.");
        }));
        p.Children.Add(row);
        p.Children.Add(status);
        p.Children.Add(warn);
        p.Children.Add(Row("Quiet config", "Used in Rest Mode.", quiet));
        p.Children.Add(Row("Normal config", "Used again on wake.", normal));
        p.Children.Add(Secondary("Make the Quiet config safe in Fan Control: never 0 % or \"fan stop\", a minimum of 20–30 %, and a curve reaching 100 % by about 70–75 °C on CPU and GPU."));
        _apply.Add(() => { _s.FanQuietConfig = quiet.Text.Trim(); _s.FanNormalConfig = normal.Text.Trim(); });
        p.Children.Add(Row("Test", "Quiet for 10 seconds, then back to Normal.", Flat("Run test", async (sender, _) =>
        {
            _s.FanQuietConfig = quiet.Text.Trim();
            _s.FanNormalConfig = normal.Text.Trim();
            string? exe = FanControlSwitcher.NormalizeExe(_s.FanControlExe) ?? FanControlSwitcher.Detect();
            if (exe == null) { Info("Choose FanControl.exe first."); return; }
            var b = (System.Windows.Controls.Button)sender;
            b.IsEnabled = false;
            b.Content = "Testing…";
            string result = await HelperClient.TestFanControlAsync(_s, exe);
            b.Content = "Run test";
            b.IsEnabled = true;
            Info(result);
        })));
        Refresh();
        return p;
    }

    private FrameworkElement SafetyPage()
    {
        var p = Page("Safety");
        int auto = _dev.DefaultTempLimitC;
        p.Children.Add(Section("Overheat protection"));
        p.Children.Add(Row("Overheat protection", "Every 30 s: at the limit the fans go back to normal; if it stays hot the PC sleeps.", Toggle(_s.OverheatProtection, v => _s.OverheatProtection = v)));
        bool automatic = _s.TempLimitC == 0;
        var slider = Slider(55, 95, automatic ? auto : _s.TempLimitC, v => { if (_s.TempLimitC != 0) _s.TempLimitC = (int)v; }, v => $"{v:0} °C");
        slider.IsEnabled = !automatic;
        p.Children.Add(Row("Automatic limit", $"For this {_dev.ClassText}: {auto} °C (desktop 80, gaming laptop 75, thin laptop / handheld 70; 5 °C lower with the lid closed).", Toggle(automatic, v =>
        {
            slider.IsEnabled = !v;
            _s.TempLimitC = v ? 0 : auto;
        })));
        p.Children.Add(Row("Limit", null, slider));
        var reading = Secondary("");
        p.Children.Add(Row("Sensors", null, Flat("Read now", async (_, _) =>
        {
            reading.Text = "Reading…";
            var local = await Task.Run(() => Sensors.ReadLocal(_dev));
            SensorReading? vendor = null;
            var (prov, _) = BuiltInFanModes.Detect();
            if (prov is { Implemented: true }) vendor = await HelperClient.ReadVendorSensorsAsync(prov.Id, _s);
            reading.Text = $"Without admin: {local}." + (vendor != null ? $"\nManufacturer sensors: {vendor}." :
                prov is { Implemented: true } ? "\nManufacturer sensors need the helper task (below)." : "") +
                (local.HasTemperature || vendor?.HasTemperature == true ? "" : "\nNo sensor works: Rest Mode keeps the fans on their normal profile for safety.");
        })));
        p.Children.Add(reading);

        if (_dev.UsesBattery)
        {
            p.Children.Add(Section("Battery"));
            p.Children.Add(Row("End Rest Mode on battery at", "Then the end action runs (sleep if the lid is closed or the end action is \"Stay\").", Slider(0, 60, _s.LowBatteryPercent, v => _s.LowBatteryPercent = (int)v, v => v == 0 ? "off" : $"{v:0} %")));
        }

        p.Children.Add(Section("Network"));
        p.Children.Add(Row("Keep the network adapter powered", "Unticks \"Allow the computer to turn off this device\" for the adapter in use, if it has that option (admin). The watchdog also reconnects Wi-Fi if it drops.", Toggle(_s.KeepNetworkAdaptersPowered, v => _s.KeepNetworkAdaptersPowered = v)));

        p.Children.Add(Section("Admin rights"));
        var helper = Secondary("");
        void UpdateHelper() => helper.Text =
            HelperClient.IsElevated ? "RestMode is running as administrator."
            : _dev.Account == AccountKind.Standard ? "Standard account: admin-only steps (network option, built-in fan modes, Fan Control, manufacturer sensors) are skipped; everything else works."
            : TaskInstaller.IsInstalled() ? "Helper task installed: admin steps run without prompts. Reinstall it after updating RestMode or moving Fan Control."
            : "Windows asks for approval (with a time limit) when an admin step is needed. RestMode never runs as admin all the time.";
        UpdateHelper();
        var install = Flat("Install helper task", async (_, _) =>
        {
            bool ok = await HelperClient.InstallTaskAsync(_s);
            UpdateHelper();
            Info(ok ? "Done. Admin steps now run without prompts." : "The helper task was not installed.");
        });
        install.IsEnabled = _dev.Account == AccountKind.Admin;
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(install);
        row.Children.Add(Flat("Remove", async (_, _) => { await HelperClient.RemoveTaskAsync(); UpdateHelper(); }));
        p.Children.Add(Row("No prompts", "One approval now, none later.", row));
        p.Children.Add(helper);
        return p;
    }

    private FrameworkElement SchedulePage()
    {
        var p = Page("Schedule & apps");
        p.Children.Add(Section("Schedule"));
        p.Children.Add(Row("Rest automatically at", "Every day, HH:mm. Empty = off.", Text(_s.RestAt, v => _s.RestAt = v.Trim(), width: 100)));
        p.Children.Add(Row("Wake at", "Also wakes the PC from the end-action sleep if Windows allows wake timers.", Text(_s.WakeAt, v => _s.WakeAt = v.Trim(), width: 100)));

        p.Children.Add(Section("Slow down background apps"));
        p.Children.Add(Row("Slow down these apps while resting", "Never touched: Windows, game launchers, download managers, cloud sync and browsers.", Toggle(_s.ThrottleApps, v => _s.ThrottleApps = v)));
        p.Children.Add(Secondary("One per line. A plain name = low priority + Efficiency mode. \"pause: name\" = frozen until you wake (skipped while it transfers data)."));
        p.Children.Add(Text(string.Join("\n", _s.Apps.Select(a => a.Action == AppAction.Pause ? "pause: " + a.Name : a.Name)), v =>
            _s.Apps = v.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).Select(l => l.StartsWith("pause:", StringComparison.OrdinalIgnoreCase)
                ? new AppRule { Name = l[6..].Trim(), Action = AppAction.Pause }
                : new AppRule { Name = l, Action = AppAction.LowPriority }).Where(a => a.Name.Length > 0).ToList(), multiline: true));
        return p;
    }

    private FrameworkElement LookPage()
    {
        var p = Page("Look");
        p.Children.Add(Section("Animation & sound"));
        p.Children.Add(Row("Animation speed", "For the Rest / wake fades.", Slider(0.5, 2, _s.AnimationSpeed, v => { _s.AnimationSpeed = Math.Round(v, 2); ThemeManager.Apply(_s); }, v => $"{v:0.0}×")));
        p.Children.Add(Row("Sounds", "A soft chime when entering and leaving Rest Mode.", Toggle(_s.Sounds, v => { _s.Sounds = v; ThemeManager.Apply(_s); })));
        return p;
    }

    private FrameworkElement BackupPage()
    {
        var p = Page("Backup & reset");
        p.Children.Add(Section("Settings"));
        p.Children.Add(Row("Export", "Save RestMode's settings to a file.", Flat("Export…", (_, _) =>
        {
            var dlg = new Microsoft.Win32.SaveFileDialog { FileName = "RestMode-settings.json", Filter = "RestMode settings|*.json" };
            if (dlg.ShowDialog(this) == true) { SettingsStore.Export(dlg.FileName); Info("Settings exported."); }
        })));
        p.Children.Add(Row("Import", "Load settings from a file.", Flat("Import…", (_, _) =>
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "RestMode settings|*.json" };
            if (dlg.ShowDialog(this) == true)
            {
                if (SettingsStore.Import(dlg.FileName)) { ThemeManager.Apply(SettingsStore.Current); _saved = true; Close(); Info("Settings imported."); }
                else Info("That file isn't a RestMode settings file.");
            }
        })));
        p.Children.Add(Row("Reset to defaults", "Every RestMode setting back to its default.", Flat("Reset…", (_, _) =>
        {
            if (System.Windows.MessageBox.Show(this, "Put every RestMode setting back to its default?", "RestMode", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
            SettingsStore.ResetToDefaults();
            ThemeManager.Apply(SettingsStore.Current);
            _saved = true;
            Close();
        })));

        p.Children.Add(Section("Backups of your Windows settings"));
        p.Children.Add(Row("Create a backup", "Power plan, lid and buttons, power mode, network adapters, Fan Control configs and RestMode settings.", Flat("Create", (_, _) =>
        {
            var dir = BackupManager.Create(_dev, _s);
            Info(dir != null ? $"Backup saved in\n{dir}" : "The backup failed (see the log).");
        })));
        p.Children.Add(Row("Backups folder", null, Flat("Open", (_, _) => OpenPath(BackupManager.Root))));

        p.Children.Add(Section("Put everything back"));
        p.Children.Add(Row("Restore everything now", "RestMode always restores what it changed; use this if something looks wrong.", Flat("Restore", async (_, _) => await _app.RestoreEverything())));
        p.Children.Add(Row("RestMode-Reset.cmd", "Works even without RestMode.exe.", Flat("Show", (_, _) => OpenPath(AppPaths.DataDir, select: AppPaths.ResetScript))));
        p.Children.Add(Row("Log", $"No user, PC or Wi-Fi names; lines older than {Logger.RetentionDays} days are deleted.", Flat("Open", (_, _) => OpenPath(AppPaths.LogFile))));
        return p;
    }

    private FrameworkElement AboutPage()
    {
        var p = Page("About");
        p.Children.Add(Text($"RestMode {typeof(SettingsWindow).Assembly.GetName().Version?.ToString(3)} – PS4-style Rest Mode for Windows 10 and 11. MIT License."));
        p.Children.Add(Secondary("The screen turns off, the sound is muted, the processor slows down and the fans quieten – but the PC stays on, so downloads and updates keep going. A key, the mouse or a controller brings you back."));
        p.Children.Add(Section("What a PC can't copy from a console"));
        p.Children.Add(Secondary("A console has a small separate chip for downloads while it sleeps; a PC doesn't, so RestMode keeps the PC on and makes it as quiet and low-power as it can. A limited desktop still uses about 25–80 W (a laptop 3–15 W). A running game keeps running slowly unless you pause it. Turning on from the network isn't part of RestMode."));
        p.Children.Add(Section("Checks"));
        p.Children.Add(Row("Self-check", "Tests every feature on this PC again.", Flat("Run", (_, _) =>
        {
            var msgs = SelfCheck.RunIfNeeded(SettingsStore.Current, _dev, force: true);
            Info(msgs.Count == 0 ? "Self-check passed: every feature works on this PC." : string.Join("\n", msgs));
        })));
        p.Children.Add(Row("First-start guide", null, Flat("Open", (_, _) => { Close(); new WizardWindow(_app).Show(); })));
        return p;
    }

    // ================================================================= save

    private void SaveAndClose()
    {
        foreach (var a in _apply) a();
        if (!AppSettings.IsValidTime(_s.RestAt) || !AppSettings.IsValidTime(_s.WakeAt))
        {
            Info("Times must look like 23:30 (or be empty).");
            return;
        }
        if (!string.IsNullOrEmpty(_s.FanQuietConfig) && !AppSettings.IsSafeConfigName(_s.FanQuietConfig) ||
            !string.IsNullOrEmpty(_s.FanNormalConfig) && !AppSettings.IsSafeConfigName(_s.FanNormalConfig))
        {
            Info("Fan Control configs must be plain file names ending in .json, e.g. Quiet.json");
            return;
        }
        var protectedNames = _s.Apps.Where(a => ProcessThrottler.IsProtected(a.Name)).Select(a => a.Name).ToList();
        if (protectedNames.Count > 0)
            Info("These apps are protected and will never be slowed down or paused:\n" + string.Join(", ", protectedNames));
        _s.FirstRunDone = true;
        SettingsStore.Save(_s);
        ThemeManager.Apply(SettingsStore.Current);
        Logger.Info("Settings saved.");
        _saved = true;
        Close();
    }

    // ================================================================= control helpers

    private void Info(string text) => System.Windows.MessageBox.Show(this, text, "RestMode");

    private static void OpenPath(string path, string? select = null)
    {
        try
        {
            if (select != null && File.Exists(select)) Process.Start("explorer.exe", $"/select,\"{select}\"");
            else
            {
                if (path.EndsWith(".log") && !File.Exists(path)) File.WriteAllText(path, "");
                if (!path.EndsWith(".log")) Directory.CreateDirectory(path);
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
        }
        catch (Exception ex) { Logger.Warn($"Opening {path} failed: {ex.Message}"); }
    }

    /// <summary>One setting: name and short explanation on the left, the control on the right, a thin line below.</summary>
    private static FrameworkElement Row(string title, string? description, FrameworkElement? control)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 24, 0) };
        left.Children.Add(Text(title));
        if (!string.IsNullOrEmpty(description)) left.Children.Add(Secondary(description));
        grid.Children.Add(left);
        if (control != null)
        {
            control.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(control, 1);
            grid.Children.Add(control);
        }
        return new Border
        {
            Child = grid,
            Padding = new Thickness(0, 11, 0, 11),
            BorderBrush = ThemeManager.Brush("BorderBrush"),
            BorderThickness = new Thickness(0, 0, 0, 1),
        };
    }

    private static CheckBox Toggle(bool value, Action<bool> set)
    {
        var c = new CheckBox { IsChecked = value };
        c.Checked += (_, _) => set(true);
        c.Unchecked += (_, _) => set(false);
        return c;
    }

    private static RadioButton Radio(string group, string text, bool value, Action onChecked)
    {
        var r = new RadioButton { GroupName = group, Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap }, IsChecked = value };
        r.Checked += (_, _) => onChecked();
        return r;
    }

    private static ComboBox Combo(string[] items, int selected, Action<int> set)
    {
        var c = new ComboBox { Width = 230 };
        foreach (var i in items) c.Items.Add(i);
        c.SelectedIndex = Math.Clamp(selected, 0, items.Length - 1);
        c.SelectionChanged += (_, _) => set(Math.Max(0, c.SelectedIndex));
        return c;
    }

    private static TextBox Text(string value, Action<string> set, bool multiline = false, double width = double.NaN)
    {
        var t = new TextBox { Text = value, Width = width, HorizontalAlignment = double.IsNaN(width) ? HorizontalAlignment.Stretch : HorizontalAlignment.Right };
        if (multiline)
        {
            t.AcceptsReturn = true;
            t.TextWrapping = TextWrapping.Wrap;
            t.MinHeight = 90;
            t.Margin = new Thickness(0, 8, 0, 0);
            t.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        }
        t.TextChanged += (_, _) => set(t.Text);
        return t;
    }

    private static TextBox Number(int value, int min, int max, Action<int> set)
    {
        var t = new TextBox { Text = value.ToString(), Width = 110, TextAlignment = TextAlignment.Right };
        t.TextChanged += (_, _) => { if (int.TryParse(t.Text, out int v)) set(Math.Clamp(v, min, max)); };
        return t;
    }

    private static FrameworkElement Slider(double min, double max, double value, Action<double> set, Func<double, string> format)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        var val = new TextBlock { Text = format(value), Style = ThemeManager.Style("Text"), Width = 64, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        var s = new System.Windows.Controls.Slider
        {
            Minimum = min, Maximum = max, Value = Math.Clamp(value, min, max), Width = 220,
            SmallChange = 1, LargeChange = Math.Max(1, (max - min) / 10),
            IsSnapToTickEnabled = max - min > 5, TickFrequency = max - min > 5 ? 1 : 0.05,
            VerticalAlignment = VerticalAlignment.Center,
        };
        s.ValueChanged += (_, e) => { val.Text = format(e.NewValue); set(e.NewValue); };
        panel.Children.Add(val);
        panel.Children.Add(s);
        return panel;
    }
}
