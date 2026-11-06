using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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
/// Settings: every option is here and can be overridden. Pages on the left
/// (ornate buttons, arrow keys / controller), content on the right. Changes
/// apply on Save (the Look page previews live and is reverted on Cancel).
/// </summary>
internal sealed class SettingsWindow : OrnateWindow
{
    private readonly IAppActions _app;
    private readonly AppSettings _s = SettingsStore.Current.Clone();
    private readonly AppSettings _original = SettingsStore.Current.Clone();
    private readonly DeviceProfile _dev;
    private readonly ContentControl _page = new();
    private readonly Dictionary<string, Func<FrameworkElement>> _pages = new();
    private readonly List<Action> _apply = new();
    private bool _saved;

    public SettingsWindow(IAppActions app, string? startPage = null) : base("RestMode · Settings", 1180, 840)
    {
        _app = app;
        _dev = app.Controller.Device;

        _pages["General"] = GeneralPage;
        _pages["Rest & Lid"] = RestPage;
        _pages["Power"] = PowerPage;
        _pages["Downloads"] = DownloadsPage;
        _pages["Fans"] = FansPage;
        _pages["Safety"] = SafetyPage;
        _pages["Schedule & Apps"] = SchedulePage;
        _pages["Look"] = LookPage;
        _pages["Backup & Reset"] = BackupPage;
        _pages["About"] = AboutPage;

        var nav = new StackPanel { Margin = new Thickness(10, 10, 0, 10) };
        KeyboardNavigation.SetDirectionalNavigation(nav, KeyboardNavigationMode.Cycle);
        foreach (var name in _pages.Keys)
        {
            var b = new OrnateButton(name, 24, 46) { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 3, 0, 3) };
            b.Click += (_, _) => Show(name);
            nav.Children.Add(b);
        }

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 24, 14) };
        var save = new OrnateButton("Save", 28, 70);
        save.Click += (_, _) => SaveAndClose();
        var cancel = new OrnateButton("Cancel", 28, 70);
        cancel.Click += (_, _) => Close();
        buttons.Children.Add(cancel);
        buttons.Children.Add(save);

        var scroll = new ScrollViewer
        {
            Content = _page,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Margin = new Thickness(10, 6, 30, 6),
            Focusable = false,
        };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetColumn(scroll, 1);
        Grid.SetRow(buttons, 1);
        Grid.SetColumnSpan(buttons, 2);
        grid.Children.Add(nav);
        grid.Children.Add(scroll);
        grid.Children.Add(buttons);
        Body.Content = grid;

        Show(startPage != null && _pages.ContainsKey(startPage) ? startPage : "General");
        Loaded += (_, _) => (nav.Children[0] as UIElement)?.Focus();
        Closed += (_, _) => { if (!_saved) ThemeManager.Apply(SettingsStore.Current); };
    }

    private void Show(string name)
    {
        var panel = _pages[name]();
        _page.Content = panel;
    }

    // ================================================================= pages

    private StackPanel Page(string title)
    {
        var p = new StackPanel { Margin = new Thickness(6, 0, 10, 20), MaxWidth = 800, HorizontalAlignment = HorizontalAlignment.Left };
        p.Children.Add(Heading(title, 48));
        return p;
    }

    private FrameworkElement GeneralPage()
    {
        var p = Page("General");
        var hotkey = new TextBox { Text = _s.Hotkey, Width = 280, HorizontalAlignment = HorizontalAlignment.Left, IsReadOnly = true };
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
        p.Children.Add(Label("Rest Mode hotkey"));
        p.Children.Add(hotkey);
        p.Children.Add(Hint("Click the box and press the keys (Ctrl / Alt / Shift + a key, or an F-key). Backspace = no hotkey."));

        bool startup = StartupManager.IsEnabled();
        p.Children.Add(Check("Start RestMode with Windows", startup, v => startup = v));
        _apply.Add(() => StartupManager.SetEnabled(startup, Installer.RunningInstalledCopy ? Installer.InstalledExe : null));
        p.Children.Add(Check("Mute the sound while resting", _s.MuteAudio, v => _s.MuteAudio = v));
        p.Children.Add(Check("Show notifications", _s.ShowNotifications, v => _s.ShowNotifications = v));
        p.Children.Add(Check("A controller button wakes the PC (Xbox / DS4Windows / Steam Input)", _s.WakeWithController, v => _s.WakeWithController = v));
        p.Children.Add(SliderRow("Mouse movement needed to wake", 0, 50, _s.MouseMoveThresholdPx, v => _s.MouseMoveThresholdPx = (int)v, v => v == 0 ? "any movement" : $"{v:0} pixels"));

        p.Children.Add(SubHeading("Shortcuts"));
        var row = new WrapPanel();
        row.Children.Add(Small("Desktop shortcut \"Rest now\"", (_, _) =>
            Info(ShortcutHelper.CreateDesktopShortcut() ? "Shortcut created on your desktop." : "Could not create the shortcut (see the log).")));
        row.Children.Add(Small("Start menu \"RestMode – Rest now\"", (_, _) =>
            Info(ShortcutHelper.CreateStartMenuRestShortcut() ? "Added to the Start menu." : "Could not create the shortcut (see the log).")));
        p.Children.Add(row);

        p.Children.Add(SubHeading("Install"));
        if (Installer.RunningInstalledCopy)
            p.Children.Add(Hint($"Installed for this user in {Installer.InstallDir}. Uninstall from Windows Settings › Apps."));
        else
        {
            p.Children.Add(Hint("Optional. RestMode.exe is portable and works from any folder. Installing copies it to your user profile, adds Start menu entries and an Apps-list entry (no admin needed)."));
            p.Children.Add(Small("Install for this user", (_, _) =>
            {
                try { Process.Start(new ProcessStartInfo(AppPaths.ExePath, "--install") { UseShellExecute = false }); }
                catch (Exception ex) { Info("Install failed: " + ex.Message); }
            }));
        }
        return p;
    }

    private FrameworkElement RestPage()
    {
        var p = Page("Rest & Lid");
        p.Children.Add(SubHeading("When I close the lid"));
        if (_dev.Caps.LidPresent)
        {
            p.Children.Add(Radio("lid", "Rest - closing the lid enters Rest Mode, opening it wakes", _s.LidAction == LidBehavior.Rest, () => _s.LidAction = LidBehavior.Rest));
            p.Children.Add(Radio("lid", "Sleep - Windows default (RestMode doesn't touch the lid)", _s.LidAction == LidBehavior.Sleep, () => _s.LidAction = LidBehavior.Sleep));
            p.Children.Add(Radio("lid", "Do nothing", _s.LidAction == LidBehavior.DoNothing, () => _s.LidAction = LidBehavior.DoNothing));
            p.Children.Add(Hint("For \"Rest\" and \"Do nothing\", Windows' lid action is set to \"Do nothing\" while RestMode runs. Your original setting is saved first and put back exactly when you pick \"Sleep\", exit RestMode or uninstall it. " +
                                "With the lid closed there is less airflow, so the overheat limit is 5 °C stricter and the laptop sleeps if it gets too hot. On battery it sleeps at the battery limit. Don't put a running laptop in a bag."));
        }
        else p.Children.Add(Hint("This PC has no lid."));

        p.Children.Add(SubHeading("Power button"));
        p.Children.Add(Hint("RestMode does not take over the power button. Windows doesn't tell programs when the power button is pressed if its action is \"Do nothing\" - so there is no safe way to turn it into a Rest button that can always be undone. Use the Rest button, the hotkey, the tray icon or the Start menu shortcut instead."));

        p.Children.Add(SubHeading("Rest animation"));
        p.Children.Add(Check("Show the Rest animation before the screen turns off", _s.RestAnimation, v => _s.RestAnimation = v));
        p.Children.Add(Label("Text when entering"));
        p.Children.Add(Text(_s.OverlayText, v => _s.OverlayText = v));
        p.Children.Add(Label("Text when waking"));
        p.Children.Add(Text(_s.WelcomeText, v => _s.WelcomeText = v));
        p.Children.Add(Small("Preview the animation", async (_, _) =>
        {
            await RestOverlay.ShowEnterAsync(_s.OverlayText);
            await Task.Delay(600);
            RestOverlay.ShowWelcome(_s.WelcomeText);
        }));
        return p;
    }

    private FrameworkElement PowerPage()
    {
        var p = Page("Power");
        p.Children.Add(SliderRow("Maximum processor speed while resting", 5, 100, _s.CpuMaxPercent, v => _s.CpuMaxPercent = (int)v, v => $"{v:0} %"));
        p.Children.Add(Hint("20-30 % keeps the PC cool and quiet and is plenty for downloads."));
        p.Children.Add(SliderRow("Minimum processor speed", 0, 50, _s.CpuMinPercent, v => _s.CpuMinPercent = (int)v, v => $"{v:0} %"));
        p.Children.Add(SliderRow("Turn off hard disks after", 0, 1800, _s.DiskIdleSeconds, v => _s.DiskIdleSeconds = (int)v, v => v == 0 ? "never" : $"{v:0} s"));
        p.Children.Add(Hint("Spinning hard drives stop when idle; a running transfer keeps them on. SSDs ignore this."));
        if (_dev.UsesBattery)
            p.Children.Add(Check("Stricter limits on battery (10 % lower processor speed)", _s.StricterOnBattery, v => _s.StricterOnBattery = v));
        p.Children.Add(SubHeading("This PC"));
        p.Children.Add(Label(_dev.Summary() + "."));
        p.Children.Add(Hint((_dev.UsesBattery
                                ? "The Rest Mode plan is a temporary COPY of your plan with plugged-in and battery values."
                                : "Desktop: the Rest Mode plan is a temporary COPY of your plan with plugged-in values only.") +
                            " Your own plan is never edited." +
                            (_dev.Caps.ModernStandby ? $" Modern Standby PC: the screen turns off {PowerPlanManager.ModernStandbyScreenOffSeconds} s after entering (an instant \"monitor off\" would make Windows go to standby and cut the network)." : "")));
        return p;
    }

    private FrameworkElement DownloadsPage()
    {
        var p = Page("Downloads");
        p.Children.Add(Label("Downloads are finished when the network is below (KB/s)"));
        p.Children.Add(Number(_s.IdleThresholdKBps, 1, 1_000_000, v => _s.IdleThresholdKBps = v));
        p.Children.Add(Label("…and the disk is below (KB/s, 0 = ignore the disk)"));
        p.Children.Add(Number(_s.DiskThresholdKBps, 0, 10_000_000, v => _s.DiskThresholdKBps = v));
        p.Children.Add(SliderRow("…for this many minutes", 1, 120, _s.IdleMinutes, v => _s.IdleMinutes = (int)v, v => $"{v:0} min"));
        p.Children.Add(Label("Then"));
        p.Children.Add(Combo(new[] { "Sleep", "Hibernate", "Shut down (60 s warning)", "Stay in Rest Mode" }, (int)_s.EndAction, i => _s.EndAction = (EndAction)i));
        p.Children.Add(SubHeading("Watch chosen apps"));
        p.Children.Add(Hint("List programs (one per line, e.g. steam, qbittorrent, EpicGamesLauncher). When any of them is running, Rest Mode ends when THEIR transfers stop, ignoring other background traffic."));
        p.Children.Add(Text(string.Join("\n", _s.WatchApps), v => _s.WatchApps = v.Split('\n', ',').Select(x => x.Trim()).Where(x => x.Length > 0).ToList(), multiline: true));
        return p;
    }

    private FrameworkElement FansPage()
    {
        var p = Page("Fans");
        p.Children.Add(Label("In Rest Mode, quieten the fans with"));
        var plan = Hint("");
        void UpdatePlan()
        {
            try
            {
                var r = FanPlanner.Resolve(_s, _dev);
                plan.Text = "On this PC Rest Mode will use: " + r.Describe() + (r.NeedsAdmin ? " (needs admin approval, or the helper task on the Safety page)." : ".");
            }
            catch { plan.Text = ""; }
        }
        p.Children.Add(Combo(new[] { "Auto: built-in modes › Fan Control › Windows power mode", "Built-in fan modes only", "Fan Control only", "Windows power mode only", "Off: don't change the fans" },
            (int)_s.FanMode, i => { _s.FanMode = (FanStrategy)i; UpdatePlan(); }));
        p.Children.Add(Hint("Always on top of this: the CPU limit and passive cooling of the Rest Mode plan. RestMode never sets a fan to 0 %."));

        p.Children.Add(SubHeading("Built-in fan modes"));
        var builtInStatus = Hint(BuiltInFanModes.Detect().message);
        p.Children.Add(Check("Use the PC's own Quiet / Balanced / Performance modes if it has them", _s.UseBuiltInFanModes, v => { _s.UseBuiltInFanModes = v; UpdatePlan(); }));
        p.Children.Add(Small("Detect built-in fan modes", (_, _) => { builtInStatus.Text = BuiltInFanModes.Detect().message; UpdatePlan(); }));
        p.Children.Add(builtInStatus);
        p.Children.Add(Hint("Supported: Lenovo Legion / LOQ (official GameZone WMI). Detected but not supported yet: ASUS, MSI, HP OMEN / Victus, Dell / Alienware, Acer, Razer."));

        p.Children.Add(SubHeading("Fan Control (optional)"));
        var path = new TextBox { Text = _s.FanControlExe, Width = 640, HorizontalAlignment = HorizontalAlignment.Left };
        var status = Hint("");
        var warn = Warn("");
        var quiet = new ComboBox { IsEditable = true, Text = _s.FanQuietConfig, Width = 300, HorizontalAlignment = HorizontalAlignment.Left };
        var normal = new ComboBox { IsEditable = true, Text = _s.FanNormalConfig, Width = 300, HorizontalAlignment = HorizontalAlignment.Left };
        void Refresh()
        {
            _s.FanControlExe = path.Text.Trim();
            string? exe = FanControlSwitcher.NormalizeExe(_s.FanControlExe) ?? (string.IsNullOrWhiteSpace(_s.FanControlExe) ? FanControlSwitcher.Detect() : null);
            var ok = new List<string>();
            var bad = new List<string>();
            if (exe == null)
            {
                if (string.IsNullOrWhiteSpace(_s.FanControlExe)) ok.Add("Fan Control not found (that's fine - it's optional).");
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
        p.Children.Add(Label("FanControl.exe or its folder (empty = find it automatically)"));
        p.Children.Add(path);
        var row = new WrapPanel();
        row.Children.Add(Small("Browse…", (_, _) =>
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "FanControl.exe|FanControl.exe|Programs|*.exe", Title = "Find FanControl.exe" };
            if (dlg.ShowDialog(this) == true) { path.Text = dlg.FileName; Refresh(); }
        }));
        row.Children.Add(Small("Folder…", (_, _) =>
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Choose the Fan Control folder (the one with FanControl.exe)" };
            if (dlg.ShowDialog(this) == true) { path.Text = dlg.FolderName; Refresh(); }
        }));
        row.Children.Add(Small("Detect", (_, _) =>
        {
            var found = FanControlSwitcher.Detect();
            if (found != null) { path.Text = found; Refresh(); }
            else Info("Fan Control was not found in Program Files, the Desktop, Downloads or C:\\Tools. Use Browse or Folder.");
        }));
        p.Children.Add(row);
        p.Children.Add(status);
        p.Children.Add(warn);
        p.Children.Add(Label("Quiet config (Rest Mode)"));
        p.Children.Add(quiet);
        p.Children.Add(Label("Normal config (on wake)"));
        p.Children.Add(normal);
        p.Children.Add(Hint("Configs you saved in Fan Control. Make the Quiet one safe: never 0 % or \"fan stop\", a minimum of 20-30 %, and a curve that reaches 100 % by about 70-75 °C on CPU and GPU."));
        _apply.Add(() => { _s.FanQuietConfig = quiet.Text.Trim(); _s.FanNormalConfig = normal.Text.Trim(); });
        p.Children.Add(Small("Test: Quiet for 10 s, then back", async (sender, _) =>
        {
            _s.FanQuietConfig = quiet.Text.Trim();
            _s.FanNormalConfig = normal.Text.Trim();
            string? exe = FanControlSwitcher.NormalizeExe(_s.FanControlExe) ?? FanControlSwitcher.Detect();
            if (exe == null) { Info("Choose FanControl.exe first."); return; }
            var b = (System.Windows.Controls.Button)sender;
            b.IsEnabled = false;
            b.Content = "Testing (about 15 s)…";
            string result = await HelperClient.TestFanControlAsync(_s, exe);
            b.Content = "Test: Quiet for 10 s, then back";
            b.IsEnabled = true;
            Info(result);
        }));
        p.Children.Add(SubHeading("Result"));
        p.Children.Add(plan);
        Refresh();
        return p;
    }

    private FrameworkElement SafetyPage()
    {
        var p = Page("Safety");
        p.Children.Add(Check("Overheat protection", _s.OverheatProtection, v => _s.OverheatProtection = v));
        int auto = _dev.DefaultTempLimitC;
        p.Children.Add(Hint($"Every 30 s RestMode reads the CPU / GPU temperature. At the limit it first puts the fans back to normal; if it stays hot it puts the PC to sleep. Automatic limit for this {_dev.ClassText}: {auto} °C (desktop 80, gaming laptop 75, thin laptop / handheld 70; 5 °C lower with the lid closed)."));
        bool automatic = _s.TempLimitC == 0;
        var slider = SliderRow("Temperature limit", 55, 95, automatic ? auto : _s.TempLimitC, v => { if (_s.TempLimitC != 0) _s.TempLimitC = (int)v; }, v => $"{v:0} °C");
        slider.IsEnabled = !automatic;
        p.Children.Add(Check("Automatic limit for this device", automatic, v =>
        {
            slider.IsEnabled = !v;
            _s.TempLimitC = v ? 0 : auto;
        }));
        p.Children.Add(slider);
        var reading = Hint("");
        p.Children.Add(Small("Read the sensors now", async (_, _) =>
        {
            reading.Text = "Reading…";
            var local = await Task.Run(() => Sensors.ReadLocal(_dev));
            SensorReading? vendor = null;
            var (prov, _) = BuiltInFanModes.Detect();
            if (prov is { Implemented: true }) vendor = await HelperClient.ReadVendorSensorsAsync(prov.Id, _s);
            reading.Text = $"Without admin: {local}." + (vendor != null ? $"\nManufacturer sensors: {vendor}." :
                prov is { Implemented: true } ? "\nManufacturer sensors need the helper task (below)." : "") +
                (local.HasTemperature || vendor?.HasTemperature == true ? "" : "\nNo sensor works: Rest Mode keeps the fans on their normal profile for safety.");
        }));
        p.Children.Add(reading);

        if (_dev.UsesBattery)
        {
            p.Children.Add(SubHeading("Battery"));
            p.Children.Add(SliderRow("On battery, end Rest Mode at", 0, 60, _s.LowBatteryPercent, v => _s.LowBatteryPercent = (int)v, v => v == 0 ? "off" : $"{v:0} %"));
            p.Children.Add(Hint("At or below this charge the end action runs (Sleep if the lid is closed or the end action is \"Stay\"). You get a warning when Rest Mode starts on battery or the charger is unplugged."));
        }

        p.Children.Add(SubHeading("Network"));
        p.Children.Add(Check("Keep the network adapter in use fully powered", _s.KeepNetworkAdaptersPowered, v => _s.KeepNetworkAdaptersPowered = v));
        p.Children.Add(Hint("Unticks \"Allow the computer to turn off this device to save power\" while resting - only for the adapter in use (wired or Wi-Fi) and only if it has that option. Needs admin. The watchdog also checks every 30 s that the network is up and reconnects Wi-Fi if it dropped."));

        p.Children.Add(SubHeading("Admin rights"));
        var helper = Hint("");
        void UpdateHelper() => helper.Text =
            HelperClient.IsElevated ? "RestMode is running as administrator."
            : _dev.Account == AccountKind.Standard ? "This is a standard account: admin-only steps (network adapter option, built-in fan modes, Fan Control, manufacturer sensors) are skipped; everything else works."
            : TaskInstaller.IsInstalled() ? "Helper task installed: admin steps run without prompts. Reinstall it after updating RestMode or moving Fan Control."
            : "Helper task not installed: Windows asks for approval (with a time limit) when an admin step is needed. RestMode never runs as admin all the time.";
        UpdateHelper();
        p.Children.Add(helper);
        var row = new WrapPanel();
        var install = Small("Allow without prompts (install helper task)", async (_, _) =>
        {
            bool ok = await HelperClient.InstallTaskAsync(_s);
            UpdateHelper();
            Info(ok ? "Done. Admin steps now run without prompts." : "The helper task was not installed.");
        });
        install.IsEnabled = _dev.Account == AccountKind.Admin;
        row.Children.Add(install);
        row.Children.Add(Small("Remove helper task", async (_, _) => { await HelperClient.RemoveTaskAsync(); UpdateHelper(); }));
        p.Children.Add(row);
        return p;
    }

    private FrameworkElement SchedulePage()
    {
        var p = Page("Schedule & Apps");
        p.Children.Add(SubHeading("Schedule"));
        p.Children.Add(Label("Rest automatically at (HH:mm, empty = off)"));
        p.Children.Add(Text(_s.RestAt, v => _s.RestAt = v.Trim(), width: 140));
        p.Children.Add(Label("Wake at (HH:mm, empty = off)"));
        p.Children.Add(Text(_s.WakeAt, v => _s.WakeAt = v.Trim(), width: 140));
        p.Children.Add(Hint("Every day. \"Wake at\" also wakes the PC from the end-action sleep if Windows allows wake timers (Power Options › Sleep › Allow wake timers)."));

        p.Children.Add(SubHeading("Slow down background apps"));
        p.Children.Add(Check("Slow down or pause these apps while resting", _s.ThrottleApps, v => _s.ThrottleApps = v));
        p.Children.Add(Hint("One per line. Plain name = low priority + Windows Efficiency mode. \"pause: name\" = frozen until you wake (skipped if the app is busy transferring). Never touched: Windows, game launchers, download managers, cloud sync and all browsers."));
        p.Children.Add(Text(string.Join("\n", _s.Apps.Select(a => a.Action == AppAction.Pause ? "pause: " + a.Name : a.Name)), v =>
            _s.Apps = v.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).Select(l => l.StartsWith("pause:", StringComparison.OrdinalIgnoreCase)
                ? new AppRule { Name = l[6..].Trim(), Action = AppAction.Pause }
                : new AppRule { Name = l, Action = AppAction.LowPriority }).Where(a => a.Name.Length > 0).ToList(), multiline: true));
        return p;
    }

    private FrameworkElement LookPage()
    {
        var p = Page("Look");
        p.Children.Add(Label("Accent color"));
        foreach (var (name, val) in new[] { ("Silver", AccentColor.Silver), ("Gold", AccentColor.Gold), ("Blue", AccentColor.Blue) })
            p.Children.Add(Radio("accent", name, _s.Accent == val, () => { _s.Accent = val; ThemeManager.Apply(_s); }));
        p.Children.Add(SliderRow("Ornament brightness", 10, 100, _s.OrnamentBrightness, v => { _s.OrnamentBrightness = (int)v; ThemeManager.Apply(_s); }, v => $"{v:0} %"));
        p.Children.Add(SliderRow("Animation speed", 0.5, 2, _s.AnimationSpeed, v => { _s.AnimationSpeed = Math.Round(v, 2); ThemeManager.Apply(_s); }, v => $"{v:0.0}×"));
        p.Children.Add(Check("Sounds", _s.Sounds, v => { _s.Sounds = v; ThemeManager.Apply(_s); }));
        p.Children.Add(Hint("Changes preview immediately; Cancel puts the old look back."));
        return p;
    }

    private FrameworkElement BackupPage()
    {
        var p = Page("Backup & Reset");
        p.Children.Add(SubHeading("Settings"));
        var row = new WrapPanel();
        row.Children.Add(Small("Export settings…", (_, _) =>
        {
            var dlg = new Microsoft.Win32.SaveFileDialog { FileName = "RestMode-settings.json", Filter = "RestMode settings|*.json" };
            if (dlg.ShowDialog(this) == true) { SettingsStore.Export(dlg.FileName); Info("Settings exported."); }
        }));
        row.Children.Add(Small("Import settings…", (_, _) =>
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "RestMode settings|*.json" };
            if (dlg.ShowDialog(this) == true)
            {
                bool ok = SettingsStore.Import(dlg.FileName);
                if (ok) { ThemeManager.Apply(SettingsStore.Current); _saved = true; Close(); Info("Settings imported."); }
                else Info("That file isn't a RestMode settings file.");
            }
        }));
        row.Children.Add(Small("Reset to defaults", (_, _) =>
        {
            if (System.Windows.MessageBox.Show(this, "Put every RestMode setting back to its default?", "RestMode", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
            SettingsStore.ResetToDefaults();
            ThemeManager.Apply(SettingsStore.Current);
            _saved = true;
            Close();
        }));
        p.Children.Add(row);

        p.Children.Add(SubHeading("Backups of your settings"));
        p.Children.Add(Hint("A copy of your power plan settings, lid and button settings, Windows power mode, network adapter options, Fan Control configs and RestMode settings - to compare or restore by hand."));
        var row2 = new WrapPanel();
        row2.Children.Add(Small("Create a backup now", (_, _) =>
        {
            var dir = BackupManager.Create(_dev, _s);
            Info(dir != null ? $"Backup saved in\n{dir}" : "The backup failed (see the log).");
        }));
        row2.Children.Add(Small("Open the backups folder", (_, _) => OpenPath(BackupManager.Root)));
        p.Children.Add(row2);

        p.Children.Add(SubHeading("Put everything back"));
        p.Children.Add(Hint("RestMode always restores what it changed on wake, after a crash, at log-off and on uninstall. These are for when something looks wrong:"));
        var row3 = new WrapPanel();
        row3.Children.Add(Small("Restore everything now", async (_, _) => await _app.RestoreEverything()));
        row3.Children.Add(Small("Show RestMode-Reset.cmd", (_, _) => OpenPath(AppPaths.DataDir, select: AppPaths.ResetScript)));
        row3.Children.Add(Small("Open the log", (_, _) => OpenPath(AppPaths.LogFile)));
        p.Children.Add(row3);
        p.Children.Add(Hint($"The log never contains your user name, PC name or Wi-Fi name, and lines older than {Logger.RetentionDays} days are deleted automatically."));
        return p;
    }

    private FrameworkElement AboutPage()
    {
        var p = Page("About");
        p.Children.Add(Label($"RestMode {typeof(SettingsWindow).Assembly.GetName().Version?.ToString(3)} - PS4-style Rest Mode for Windows 10 and 11. MIT License."));
        p.Children.Add(Hint("The screen turns off, the sound is muted, the processor is slowed and the fans quieten - but the PC stays on, so downloads and updates keep going. Touch the mouse, a key or a controller to come back."));
        p.Children.Add(SubHeading("What a PC can't copy from a console"));
        p.Children.Add(Hint("A console has a tiny separate chip for downloads while it sleeps; a PC doesn't, so RestMode keeps the PC on and makes it as quiet and low-power as possible. A capped desktop still uses about 25-80 W (a laptop 3-15 W). A running game keeps running slowly unless you pause it. Turning on from the network isn't part of RestMode."));
        p.Children.Add(SubHeading("Credits"));
        p.Children.Add(Hint("Fonts: Pinyon Script and Great Vibes (Robert Leuschke / TypeSETit), Cormorant Garamond (Christian Thalmann, Catharsis Fonts) - SIL Open Font License 1.1. All ornaments, the emblem and the sounds are original and made for RestMode."));
        p.Children.Add(Small("Run the self-check again", (_, _) =>
        {
            var msgs = SelfCheck.RunIfNeeded(SettingsStore.Current, _dev, force: true);
            Info(msgs.Count == 0 ? "Self-check passed: every feature works on this PC." : string.Join("\n", msgs));
        }));
        p.Children.Add(Small("Run the first-start guide again", (_, _) => { Close(); new WizardWindow(_app).Show(); }));
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
                Directory.CreateDirectory(Directory.Exists(path) || path.EndsWith(".log") ? (Path.GetDirectoryName(path) ?? path) : path);
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
        }
        catch (Exception ex) { Logger.Warn($"Opening {path} failed: {ex.Message}"); }
    }

    private static CheckBox Check(string text, bool value, Action<bool> set)
    {
        var c = new CheckBox { Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap }, IsChecked = value };
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
        var c = new ComboBox { Width = 560, HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var i in items) c.Items.Add(i);
        c.SelectedIndex = Math.Clamp(selected, 0, items.Length - 1);
        c.SelectionChanged += (_, _) => set(Math.Max(0, c.SelectedIndex));
        return c;
    }

    private static TextBox Text(string value, Action<string> set, bool multiline = false, double width = 560)
    {
        var t = new TextBox { Text = value, Width = width, HorizontalAlignment = HorizontalAlignment.Left };
        if (multiline)
        {
            t.AcceptsReturn = true;
            t.TextWrapping = TextWrapping.Wrap;
            t.MinHeight = 90;
            t.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        }
        t.TextChanged += (_, _) => set(t.Text);
        return t;
    }

    private static TextBox Number(int value, int min, int max, Action<int> set)
    {
        var t = new TextBox { Text = value.ToString(), Width = 160, HorizontalAlignment = HorizontalAlignment.Left };
        t.TextChanged += (_, _) => { if (int.TryParse(t.Text, out int v)) set(Math.Clamp(v, min, max)); };
        return t;
    }

    private static FrameworkElement SliderRow(string label, double min, double max, double value, Action<double> set, Func<double, string> format)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        var head = new DockPanel();
        var val = new TextBlock { Text = format(value), Style = ThemeManager.Style("Label"), Foreground = (Brush)System.Windows.Application.Current.FindResource("AccentBrush") };
        DockPanel.SetDock(val, Dock.Right);
        head.Children.Add(val);
        head.Children.Add(new TextBlock { Text = label, Style = ThemeManager.Style("Label") });
        var s = new Slider { Minimum = min, Maximum = max, Value = Math.Clamp(value, min, max), Width = 560, HorizontalAlignment = HorizontalAlignment.Left, SmallChange = 1, LargeChange = Math.Max(1, (max - min) / 10), IsSnapToTickEnabled = max - min > 5, TickFrequency = max - min > 5 ? 1 : 0.05 };
        s.ValueChanged += (_, e) => { val.Text = format(e.NewValue); set(e.NewValue); };
        head.Width = 560;
        head.HorizontalAlignment = HorizontalAlignment.Left;
        panel.Children.Add(head);
        panel.Children.Add(s);
        return panel;
    }
}
