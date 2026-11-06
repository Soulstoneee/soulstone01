using System.Diagnostics;
using RestMode.Core;
using RestMode.Core.Fans;
using RestMode.Elevation;
using RestMode.Util;

namespace RestMode.UI;

/// <summary>
/// The settings window. Built in code (no designer file) so the whole UI is
/// readable in one place. Changes apply when you press Save; values take
/// effect the next time you enter Rest Mode (the hotkey changes immediately).
/// </summary>
internal sealed class SettingsForm : Form
{
    private readonly Action _onSaved;
    private readonly Func<Task> _restoreNow;
    private readonly AppSettings _s = SettingsStore.Current.Clone();
    private readonly DeviceProfile _dev = DeviceProfile.Detect();

    // General
    private readonly TextBox _hotkey = new() { ReadOnly = true, Width = 200, BackColor = SystemColors.Window };
    private readonly CheckBox _startWithWindows = new() { Text = "Start RestMode with Windows", AutoSize = true };
    private readonly CheckBox _mute = new() { Text = "Mute sound while resting", AutoSize = true };
    private readonly CheckBox _notifications = new() { Text = "Show tray notifications", AutoSize = true };
    private readonly CheckBox _controller = new() { Text = "A controller button press (Xbox / DS4Windows / Steam Input) wakes the PC", AutoSize = true };
    private readonly NumericUpDown _mousePx = Num(0, 200);

    // Power
    private readonly NumericUpDown _cpuMax = Num(5, 100);
    private readonly NumericUpDown _cpuMin = Num(0, 100);
    private readonly NumericUpDown _diskIdle = Num(0, 3600);
    private readonly CheckBox _lid = new() { Text = "Laptops: keep resting (and downloading) with the lid closed", AutoSize = true };
    private readonly NumericUpDown _battery = Num(0, 90);

    // Downloads
    private readonly NumericUpDown _netThreshold = Num(1, 1_000_000);
    private readonly NumericUpDown _diskThreshold = Num(0, 10_000_000);
    private readonly NumericUpDown _idleMinutes = Num(1, 1440);
    private readonly ComboBox _endAction = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };

    // Apps
    private readonly CheckBox _throttle = new() { Text = "Slow down or pause these apps while resting", AutoSize = true };
    private readonly DataGridView _apps = new()
    {
        Dock = DockStyle.Fill,
        AllowUserToAddRows = true,
        AllowUserToDeleteRows = true,
        RowHeadersVisible = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        BackgroundColor = SystemColors.Window,
    };

    // Fans
    private readonly ComboBox _fanMode = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 360, DropDownWidth = 460 };
    private readonly CheckBox _builtIn = new() { Text = "Use the PC's built-in fan modes (Quiet / Balanced / Performance) if it has them", AutoSize = true };
    private readonly Label _builtInStatus = Hint();
    private readonly TextBox _fanExe = new() { Width = 400, PlaceholderText = "auto-detect" };
    private readonly Label _fanStatus = Hint();
    private readonly Label _fanWarning = new() { AutoSize = true, MaximumSize = new Size(620, 0), ForeColor = Color.DarkOrange };
    private readonly ComboBox _fanQuiet = new() { Width = 200, DropDownStyle = ComboBoxStyle.DropDown };
    private readonly ComboBox _fanNormal = new() { Width = 200, DropDownStyle = ComboBoxStyle.DropDown };
    private readonly Label _fanPlan = new() { AutoSize = true, MaximumSize = new Size(600, 0), Font = new Font(SystemFonts.MessageBoxFont ?? DefaultFont, FontStyle.Bold) };

    // Admin
    private readonly CheckBox _adapters = new() { Text = "Keep the network adapter in use fully powered", AutoSize = true };
    private readonly Label _helperStatus = new() { AutoSize = true, MaximumSize = new Size(600, 0) };

    public SettingsForm(Action onSaved, Func<Task> restoreNow)
    {
        _onSaved = onSaved;
        _restoreNow = restoreNow;

        Text = "RestMode Settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(700, 600);
        Font = SystemFonts.MessageBoxFont ?? Font;
        Icon = TrayIcons.Create(TrayIcons.AwakeColor);

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(BuildGeneralTab());
        tabs.TabPages.Add(BuildPowerTab());
        tabs.TabPages.Add(BuildDownloadsTab());
        tabs.TabPages.Add(BuildAppsTab());
        tabs.TabPages.Add(BuildFansTab());
        tabs.TabPages.Add(BuildAdminTab());

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Padding = new Padding(8) };
        var save = new Button { Text = "Save", AutoSize = true, DialogResult = DialogResult.None };
        var cancel = new Button { Text = "Cancel", AutoSize = true };
        var log = new Button { Text = "Open log", AutoSize = true };
        var restore = new Button { Text = "Restore everything now", AutoSize = true };
        save.Click += (_, _) => SaveAndClose();
        cancel.Click += (_, _) => Close();
        log.Click += (_, _) => OpenFile(AppPaths.LogFile);
        restore.Click += async (_, _) => await _restoreNow();
        buttons.Controls.AddRange(new Control[] { save, cancel, log, restore });
        AcceptButton = save;
        CancelButton = cancel;

        Controls.Add(tabs);
        Controls.Add(buttons);
        LoadValues();
    }

    // ------------------------------------------------------------------ tabs

    private TabPage BuildGeneralTab()
    {
        var t = Table();
        _hotkey.KeyDown += (_, e) =>
        {
            e.SuppressKeyPress = true;
            if (e.KeyCode is Keys.Back or Keys.Delete) { _hotkey.Text = ""; return; }
            var text = HotkeyParser.Format(e.KeyData);
            if (text != null && HotkeyParser.TryParse(text, out uint _, out Keys _)) _hotkey.Text = text;
        };
        AddRow(t, "Rest Mode hotkey:", _hotkey, "Click the box and press the keys (needs Ctrl/Alt/Shift, or an F-key). Backspace = no hotkey.");
        AddRow(t, null, _startWithWindows);
        AddRow(t, null, _mute);
        AddRow(t, null, _notifications);
        AddRow(t, null, _controller);
        AddRow(t, "Mouse movement to wake (pixels):", _mousePx, "0 = the slightest movement wakes the PC. A few pixels ignores desk vibrations.");

        var shortcut = new Button { Text = "Create \"Enter Rest Mode\" desktop shortcut", AutoSize = true };
        shortcut.Click += (_, _) =>
        {
            bool ok = ShortcutHelper.CreateDesktopShortcut();
            MessageBox.Show(this, ok ? "Shortcut created on your desktop." : "Could not create the shortcut (see log).", "RestMode");
        };
        AddRow(t, null, shortcut);

        if (Installer.RunningInstalledCopy)
        {
            AddRow(t, null, Hint($"Installed for this user in {Installer.InstallDir}. Uninstall from Windows Settings > Apps."));
        }
        else
        {
            var install = new Button { Text = "Install for this user (Start menu + Apps list)", AutoSize = true };
            install.Click += (_, _) =>
            {
                try { Process.Start(new ProcessStartInfo(AppPaths.ExePath, "--install") { UseShellExecute = false }); }
                catch (Exception ex) { MessageBox.Show(this, "Install failed: " + ex.Message, "RestMode"); }
            };
            AddRow(t, null, install, "Optional. RestMode.exe is portable and works from any folder. Installing copies it to your user profile (no admin needed).");
        }
        return Page("General", t);
    }

    private TabPage BuildPowerTab()
    {
        var t = Table();
        AddRow(t, "Maximum processor state (%):", _cpuMax, "PS4 Rest Mode runs the chip at a crawl. 20-30% keeps fans quiet but downloads fast.");
        AddRow(t, "Minimum processor state (%):", _cpuMin);
        AddRow(t, "Turn off hard disks after (seconds):", _diskIdle, "Disks only spin down when idle; a running transfer keeps them on. 0 = never.");

        _lid.Enabled = _dev.Caps.LidPresent;
        AddRow(t, null, _lid, _dev.Caps.LidPresent ? "Warning: do not put a running laptop in a bag." : "This PC has no lid.");
        _battery.Enabled = _dev.UsesBattery;
        AddRow(t, "On battery, end Rest Mode at (%):", _battery,
            _dev.UsesBattery ? "At or below this charge, the PC hibernates (or shuts down). 0 = off." : "This PC has no battery (a UPS doesn't count).");

        AddRow(t, null, Hint(
            "This PC: " + _dev.Summary() + ".\n" +
            (_dev.UsesBattery
                ? "The low-power plan is a temporary COPY of your plan with plugged-in AND battery values."
                : "Desktop: the low-power plan is a temporary COPY of your plan with plugged-in values only.") +
            " Your own plan is never edited." +
            (_dev.Caps.ModernStandby
                ? $"\nModern Standby PC: the screen turns off {PowerPlanManager.ModernStandbyScreenOffSeconds} s after entering (an instant \"monitor off\" would make Windows go to standby and cut the network)."
                : "")));
        return Page("Power", t);
    }

    private TabPage BuildDownloadsTab()
    {
        var t = Table();
        AddRow(t, "Downloads are finished when network is below (KB/s):", _netThreshold, "Download + upload, averaged over 30 seconds.");
        AddRow(t, "...and disk activity is below (KB/s):", _diskThreshold, "Game updates unpack after downloading. 0 = ignore disk.");
        AddRow(t, "...for this many minutes:", _idleMinutes);
        _endAction.Items.AddRange(new object[] { "Sleep", "Hibernate", "Shut down", "Stay in Rest Mode" });
        AddRow(t, "Then:", _endAction, "Sleep and hibernate restore all settings first, so the PC wakes up normally.");
        return Page("Downloads", t);
    }

    private TabPage BuildAppsTab()
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(10) };
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _apps.Columns.Add(new DataGridViewTextBoxColumn { Name = "Name", HeaderText = "Program name (e.g. Spotify or Spotify.exe)", FillWeight = 65 });
        var action = new DataGridViewComboBoxColumn { Name = "Action", HeaderText = "While resting", FillWeight = 35, FlatStyle = FlatStyle.Flat };
        action.Items.AddRange("Low priority", "Pause");
        _apps.Columns.Add(action);
        _apps.DefaultValuesNeeded += (_, e) => e.Row.Cells["Action"].Value = "Low priority";

        var note = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(640, 0),
            ForeColor = SystemColors.GrayText,
            Text = "Low priority = idle CPU priority + Windows Efficiency mode. Pause = frozen until you wake the PC " +
                   "(skipped automatically if the app is busy transferring data).\n" +
                   "Never touched, even if listed: Windows system processes, Steam / Epic / EA / Ubisoft / GOG / Battle.net / Xbox, " +
                   "torrent and download managers, OneDrive / Dropbox / Google Drive, and all web browsers.",
        };
        panel.Controls.Add(_throttle, 0, 0);
        panel.Controls.Add(_apps, 0, 1);
        panel.Controls.Add(note, 0, 2);
        return new TabPage("Apps") { Controls = { panel } };
    }

    private TabPage BuildFansTab()
    {
        var t = Table();
        _fanMode.Items.AddRange(new object[]
        {
            "Auto (built-in > Fan Control > Windows)",
            "Built-in fan modes only",
            "Fan Control only",
            "Windows power mode only",
            "Off (don't change fans)",
        });
        AddRow(t, "In Rest Mode, quieten fans with:", _fanMode,
            "Auto = the PC's built-in modes if found, else Fan Control (if set up and running), else the Windows power mode \"Best power efficiency\". " +
            "Always on top of this: the CPU limit and passive cooling of the Rest Mode plan, which slow the CPU fan on every PC.");

        // Built-in
        AddRow(t, null, _builtIn);
        var detectBuiltIn = new Button { Text = "Detect built-in fan modes", AutoSize = true };
        detectBuiltIn.Click += (_, _) => { _builtInStatus.Text = BuiltInFanModes.Detect().message; UpdateFanPlan(); };
        AddRow(t, null, detectBuiltIn);
        AddRow(t, null, _builtInStatus);

        // Fan Control
        AddRow(t, null, new Label { Text = "Fan Control (Rem0o, optional - getfancontrol.com)", AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(3, 12, 3, 0) });
        var fanRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        var browse = new Button { Text = "Browse...", AutoSize = true };
        var folder = new Button { Text = "Folder...", AutoSize = true };
        var detect = new Button { Text = "Detect", AutoSize = true };
        browse.Click += (_, _) =>
        {
            using var dlg = new OpenFileDialog { Filter = "FanControl.exe|FanControl.exe|Programs|*.exe", Title = "Find FanControl.exe" };
            if (dlg.ShowDialog(this) == DialogResult.OK) { _fanExe.Text = dlg.FileName; RefreshFanControl(); }
        };
        folder.Click += (_, _) =>
        {
            using var dlg = new FolderBrowserDialog { Description = "Choose the Fan Control folder (the one with FanControl.exe)", UseDescriptionForTitle = true };
            if (dlg.ShowDialog(this) == DialogResult.OK) { _fanExe.Text = dlg.SelectedPath; RefreshFanControl(); }
        };
        detect.Click += (_, _) =>
        {
            var p = FanControlSwitcher.Detect();
            if (p != null) { _fanExe.Text = p; RefreshFanControl(); }
            else MessageBox.Show(this, "Fan Control was not found in Program Files, the Desktop, Downloads or C:\\Tools. Use Browse or Folder.", "RestMode");
        };
        _fanExe.Leave += (_, _) => RefreshFanControl();
        fanRow.Controls.AddRange(new Control[] { browse, folder, detect });
        AddRow(t, "FanControl.exe or folder:", _fanExe);
        AddRow(t, null, fanRow);
        AddRow(t, null, _fanStatus);
        AddRow(t, null, _fanWarning);
        AddRow(t, "Quiet config (Rest Mode):", _fanQuiet);
        AddRow(t, "Normal config (on wake):", _fanNormal,
            "Configs you saved in Fan Control (its Configurations folder). Make the Quiet one safe: never 0% / fan stop, " +
            "a minimum of 20-30%, and a curve that reaches 100% by about 70-75 °C (CPU and GPU).");
        var test = new Button { Text = "Test: Quiet for 10 s, then back", AutoSize = true };
        test.Click += async (_, _) =>
        {
            ApplyValues();
            string? exe = FanControlSwitcher.NormalizeExe(_s.FanControlExe) ?? FanControlSwitcher.Detect();
            if (exe == null) { MessageBox.Show(this, "Choose FanControl.exe first.", "RestMode"); return; }
            test.Enabled = false;
            test.Text = "Testing (about 15 s)...";
            string result = await HelperClient.TestFanControlAsync(_s, exe);
            test.Text = "Test: Quiet for 10 s, then back";
            test.Enabled = true;
            MessageBox.Show(this, result, "Fan Control test");
        };
        AddRow(t, null, test, "Needs admin approval (like Rest Mode itself). Fan Control must be running.");

        AddRow(t, null, new Label { Text = "On this PC, Rest Mode will use:", AutoSize = true, Margin = new Padding(3, 12, 3, 0) });
        AddRow(t, null, _fanPlan);

        _fanMode.SelectedIndexChanged += (_, _) => UpdateFanPlan();
        _builtIn.CheckedChanged += (_, _) => UpdateFanPlan();
        _fanQuiet.TextChanged += (_, _) => UpdateFanPlan();
        return Page("Fans", t);
    }

    private TabPage BuildAdminTab()
    {
        var t = Table();
        AddRow(t, null, _adapters, "Unticks \"Allow the computer to turn off this device to save power\" while resting. Needs admin rights. " +
                                   "Only the adapter in use (wired or Wi-Fi) is changed, and only if it has that option; otherwise nothing is asked.");

        var install = new Button { Text = "Allow without UAC prompts (install helper task)", AutoSize = true };
        var remove = new Button { Text = "Remove helper task", AutoSize = true };
        install.Click += async (_, _) =>
        {
            ApplyValues(); // so the chosen FanControl.exe path is baked in
            bool ok = await HelperClient.InstallTaskAsync(_s);
            UpdateHelperStatus();
            MessageBox.Show(this, ok ? "Done. RestMode will no longer ask for admin approval." : "The helper task was not installed.", "RestMode");
        };
        remove.Click += async (_, _) =>
        {
            await HelperClient.RemoveTaskAsync();
            UpdateHelperStatus();
        };
        if (_dev.Account == AccountKind.Standard && !_dev.Elevated) install.Enabled = false;
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        row.Controls.AddRange(new Control[] { install, remove });
        AddRow(t, null, row);
        AddRow(t, null, _helperStatus);
        return Page("Admin", t);
    }

    // ------------------------------------------------------------------ fan helpers

    private void RefreshFanControl()
    {
        string? exe = FanControlSwitcher.NormalizeExe(_fanExe.Text) ?? (string.IsNullOrWhiteSpace(_fanExe.Text) ? FanControlSwitcher.Detect() : null);
        var msgs = new List<string>();
        var warnings = new List<string>();
        if (exe == null)
        {
            if (string.IsNullOrWhiteSpace(_fanExe.Text)) msgs.Add("Fan Control not found (that's fine - it's optional).");
            else warnings.Add("No FanControl.exe at this path. Choose the Fan Control folder or FanControl.exe.");
        }
        else
        {
            msgs.Add((string.IsNullOrWhiteSpace(_fanExe.Text) ? "Found: " : "OK: ") + exe);
            if (FanControlSwitcher.CheckRuntime(exe) is string rt) warnings.Add(rt);
            msgs.Add(FanControlSwitcher.IsRunning() ? "Fan Control is running." : "Fan Control is not running (RestMode only switches it while it runs; it never starts it).");
            var configs = FanControlSwitcher.ListConfigs(exe);
            if (configs.Count == 0) msgs.Add("No configs saved yet: in Fan Control, save a \"Quiet\" and a \"Normal\" config.");
            if (FanControlSwitcher.LocationWarning(exe) is string w) warnings.Add("Warning: " + w);
            FillConfigs(_fanQuiet, configs);
            FillConfigs(_fanNormal, configs);
        }
        _fanStatus.Text = string.Join("\n", msgs);
        _fanWarning.Text = string.Join("\n", warnings);
        _fanWarning.Visible = warnings.Count > 0;
        UpdateFanPlan();
    }

    private static void FillConfigs(ComboBox box, List<string> configs)
    {
        string current = box.Text;
        box.Items.Clear();
        foreach (var c in configs) box.Items.Add(c);
        box.Text = current;
    }

    private void UpdateFanPlan()
    {
        try
        {
            var tmp = _s.Clone();
            tmp.FanMode = (FanStrategy)Math.Max(0, _fanMode.SelectedIndex);
            tmp.UseBuiltInFanModes = _builtIn.Checked;
            tmp.FanControlExe = _fanExe.Text.Trim();
            tmp.FanQuietConfig = _fanQuiet.Text.Trim();
            var plan = FanPlanner.Resolve(tmp, _dev);
            _fanPlan.Text = plan.Describe() + (plan.NeedsAdmin ? " (needs admin approval)" : "");
        }
        catch (Exception ex) { _fanPlan.Text = "?"; Logger.Info($"Fan plan preview failed: {ex.Message}"); }
    }

    // ------------------------------------------------------------------ values

    private void LoadValues()
    {
        _hotkey.Text = _s.Hotkey;
        _startWithWindows.Checked = StartupManager.IsEnabled();
        _mute.Checked = _s.MuteAudio;
        _notifications.Checked = _s.ShowNotifications;
        _controller.Checked = _s.WakeWithController;
        _mousePx.Value = _s.MouseMoveThresholdPx;

        _cpuMax.Value = _s.CpuMaxPercent;
        _cpuMin.Value = _s.CpuMinPercent;
        _diskIdle.Value = _s.DiskIdleSeconds;
        _lid.Checked = _s.AllowLidClosed;
        _battery.Value = _s.LowBatteryPercent;

        _netThreshold.Value = _s.IdleThresholdKBps;
        _diskThreshold.Value = _s.DiskThresholdKBps;
        _idleMinutes.Value = _s.IdleMinutes;
        _endAction.SelectedIndex = (int)_s.EndAction;

        _throttle.Checked = _s.ThrottleApps;
        foreach (var a in _s.Apps)
            _apps.Rows.Add(a.Name, a.Action == AppAction.Pause ? "Pause" : "Low priority");

        _fanMode.SelectedIndex = (int)_s.FanMode;
        _builtIn.Checked = _s.UseBuiltInFanModes;
        _builtInStatus.Text = BuiltInFanModes.Detect().message;
        _fanExe.Text = _s.FanControlExe;
        _fanQuiet.Text = _s.FanQuietConfig;
        _fanNormal.Text = _s.FanNormalConfig;
        RefreshFanControl();

        _adapters.Checked = _s.KeepNetworkAdaptersPowered;
        UpdateHelperStatus();
    }

    private void ApplyValues()
    {
        _s.Hotkey = _hotkey.Text;
        _s.MuteAudio = _mute.Checked;
        _s.ShowNotifications = _notifications.Checked;
        _s.WakeWithController = _controller.Checked;
        _s.MouseMoveThresholdPx = (int)_mousePx.Value;

        _s.CpuMaxPercent = (int)_cpuMax.Value;
        _s.CpuMinPercent = (int)_cpuMin.Value;
        _s.DiskIdleSeconds = (int)_diskIdle.Value;
        _s.AllowLidClosed = _lid.Checked;
        _s.LowBatteryPercent = (int)_battery.Value;

        _s.IdleThresholdKBps = (int)_netThreshold.Value;
        _s.DiskThresholdKBps = (int)_diskThreshold.Value;
        _s.IdleMinutes = (int)_idleMinutes.Value;
        _s.EndAction = (EndAction)Math.Max(0, _endAction.SelectedIndex);

        _s.ThrottleApps = _throttle.Checked;
        _s.Apps = _apps.Rows.Cast<DataGridViewRow>()
            .Where(r => !r.IsNewRow && r.Cells["Name"].Value is string n && n.Trim().Length > 0)
            .Select(r => new AppRule
            {
                Name = ((string)r.Cells["Name"].Value).Trim(),
                Action = (r.Cells["Action"].Value as string) == "Pause" ? AppAction.Pause : AppAction.LowPriority,
            })
            .ToList();

        _s.FanMode = (FanStrategy)Math.Max(0, _fanMode.SelectedIndex);
        _s.UseBuiltInFanModes = _builtIn.Checked;
        _s.FanControlExe = _fanExe.Text.Trim();
        _s.FanQuietConfig = _fanQuiet.Text.Trim();
        _s.FanNormalConfig = _fanNormal.Text.Trim();

        _s.KeepNetworkAdaptersPowered = _adapters.Checked;
    }

    private void SaveAndClose()
    {
        if (!string.IsNullOrEmpty(_fanQuiet.Text.Trim()) && !AppSettings.IsSafeConfigName(_fanQuiet.Text.Trim()) ||
            !string.IsNullOrEmpty(_fanNormal.Text.Trim()) && !AppSettings.IsSafeConfigName(_fanNormal.Text.Trim()))
        {
            MessageBox.Show(this, "Fan Control configs must be plain file names ending in .json, e.g. Quiet.json", "RestMode");
            return;
        }

        ApplyValues();
        var protectedNames = _s.Apps.Where(a => ProcessThrottler.IsProtected(a.Name)).Select(a => a.Name).ToList();
        if (protectedNames.Count > 0)
            MessageBox.Show(this, "These apps are protected and will never be slowed down or paused:\n" + string.Join(", ", protectedNames), "RestMode");

        StartupManager.SetEnabled(_startWithWindows.Checked);
        SettingsStore.Save(_s);
        Logger.Info("Settings saved.");
        _onSaved();
        Close();
    }

    private void UpdateHelperStatus()
    {
        bool installed = TaskInstaller.IsInstalled();
        _helperStatus.Text =
            HelperClient.IsElevated ? "RestMode is running as administrator: no prompts needed."
            : _dev.Account == AccountKind.Standard
                ? "This is a standard (non-administrator) account. Admin-only steps (network adapter option, built-in fan modes, Fan Control) are skipped; everything else works. To use them, sign in with an administrator account, or run RestMode as administrator."
            : installed
                ? "Helper task installed: admin features run without UAC prompts. Reinstall it after updating RestMode.exe or changing the FanControl.exe path."
                : "Helper task not installed: you'll see a UAC prompt when entering/leaving Rest Mode if an admin feature is needed.";
    }

    // ------------------------------------------------------------------ layout helpers

    private static NumericUpDown Num(int min, int max) => new() { Minimum = min, Maximum = max, Width = 100, ThousandsSeparator = true };

    private static Label Hint(string text = "") => new()
    {
        Text = text,
        AutoSize = true,
        MaximumSize = new Size(620, 0),
        ForeColor = SystemColors.GrayText,
    };

    private static TableLayoutPanel Table() => new()
    {
        Dock = DockStyle.Top,
        AutoSize = true,
        ColumnCount = 2,
        Padding = new Padding(10),
        ColumnStyles = { new ColumnStyle(SizeType.AutoSize), new ColumnStyle(SizeType.Percent, 100) },
    };

    private static TabPage Page(string title, Control content) =>
        new(title) { AutoScroll = true, Controls = { content } };

    private static void AddRow(TableLayoutPanel t, string? label, Control control, string? hint = null)
    {
        int row = t.RowCount++;
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        control.Margin = new Padding(3, 6, 3, hint == null ? 6 : 0);
        if (label != null)
        {
            t.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 9, 12, 3) }, 0, row);
            t.Controls.Add(control, 1, row);
        }
        else
        {
            t.Controls.Add(control, 0, row);
            t.SetColumnSpan(control, 2);
        }

        if (hint != null)
        {
            int hr = t.RowCount++;
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var h = new Label
            {
                Text = hint,
                AutoSize = true,
                MaximumSize = new Size(600, 0),
                ForeColor = SystemColors.GrayText,
                Margin = new Padding(label == null ? 20 : 3, 0, 3, 6),
            };
            t.Controls.Add(h, label == null ? 0 : 1, hr);
            if (label == null) t.SetColumnSpan(h, 2);
        }
    }

    private static void OpenFile(string path)
    {
        try
        {
            if (!File.Exists(path)) File.WriteAllText(path, "");
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) { Logger.Error("Opening file failed", ex); }
    }
}
