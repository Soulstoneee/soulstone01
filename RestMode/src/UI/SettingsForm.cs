using System.Diagnostics;
using RestMode.Core;
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

    // Admin
    private readonly CheckBox _adapters = new() { Text = "Keep network adapters fully powered (untick \"allow the computer to turn off this device\")", AutoSize = true };
    private readonly CheckBox _fan = new() { Text = "Switch Fan Control (Rem0o) to a quiet profile", AutoSize = true };
    private readonly TextBox _fanExe = new() { Width = 330, PlaceholderText = "auto-detect" };
    private readonly TextBox _fanQuiet = new() { Width = 200 };
    private readonly TextBox _fanNormal = new() { Width = 200 };
    private readonly Label _helperStatus = new() { AutoSize = true, MaximumSize = new Size(560, 0) };

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
        ClientSize = new Size(640, 520);
        Font = SystemFonts.MessageBoxFont ?? Font;
        Icon = TrayIcons.Create(TrayIcons.AwakeColor);

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(BuildGeneralTab());
        tabs.TabPages.Add(BuildPowerTab());
        tabs.TabPages.Add(BuildDownloadsTab());
        tabs.TabPages.Add(BuildAppsTab());
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
        return Page("General", t);
    }

    private TabPage BuildPowerTab()
    {
        var t = Table();
        AddRow(t, "Maximum processor state (%):", _cpuMax, "PS4 Rest Mode runs the chip at a crawl. 20-30% keeps fans quiet but downloads fast.");
        AddRow(t, "Minimum processor state (%):", _cpuMin);
        AddRow(t, "Turn off hard disks after (seconds):", _diskIdle, "Disks only spin down when idle; a running transfer keeps them on. 0 = never.");
        AddRow(t, null, _lid, "Warning: do not put a running laptop in a bag.");
        AddRow(t, "On battery, end Rest Mode at (%):", _battery, "At or below this charge, the PC hibernates (or shuts down). 0 = off.");

        var caps = PowerCapabilities.Read();
        var info = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(580, 0),
            ForeColor = SystemColors.GrayText,
            Text = "This PC: " + (caps.ModernStandby
                ? "Modern Standby (S0ix)" + (caps.ModernStandbyNetworkConnected ? " with network in standby" : "")
                : caps.S3Supported ? "classic S3 sleep" : "no sleep state reported") +
                   (caps.HibernateSupported ? ", hibernation available" : ", hibernation off") +
                   (caps.HasBattery ? ", battery" : ", no battery") + ".\n" +
                   "The low-power plan is a temporary COPY of your current plan (plugged-in and battery values). Your own plan is never edited.",
        };
        AddRow(t, null, info);
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
            MaximumSize = new Size(600, 0),
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

    private TabPage BuildAdminTab()
    {
        var t = Table();
        AddRow(t, null, _adapters, "Needs admin rights.");
        AddRow(t, null, _fan, "Needs Fan Control (getfancontrol.com). Without it, the CPU limit keeps fans quiet.");

        var fanRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        var browse = new Button { Text = "Browse...", AutoSize = true };
        var detect = new Button { Text = "Detect", AutoSize = true };
        browse.Click += (_, _) =>
        {
            using var dlg = new OpenFileDialog { Filter = "FanControl.exe|FanControl.exe|Programs|*.exe", Title = "Find FanControl.exe" };
            if (dlg.ShowDialog(this) == DialogResult.OK) _fanExe.Text = dlg.FileName;
        };
        detect.Click += (_, _) =>
        {
            var p = FanControlSwitcher.Detect();
            if (p != null) _fanExe.Text = p;
            else MessageBox.Show(this, "Fan Control was not found. Start it, or use Browse.", "RestMode");
        };
        fanRow.Controls.AddRange(new Control[] { _fanExe, browse, detect });
        AddRow(t, "FanControl.exe:", fanRow);
        AddRow(t, "Quiet profile (config file):", _fanQuiet, "A config you made in Fan Control, saved in its Configurations folder.");
        AddRow(t, "Normal profile (config file):", _fanNormal, "Switched back to when you wake the PC.");

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
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        row.Controls.AddRange(new Control[] { install, remove });
        AddRow(t, null, row);
        AddRow(t, null, _helperStatus);
        return Page("Admin / Fans", t);
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

        _adapters.Checked = _s.KeepNetworkAdaptersPowered;
        _fan.Checked = _s.FanControlEnabled;
        _fanExe.Text = _s.FanControlExe;
        _fanQuiet.Text = _s.FanQuietConfig;
        _fanNormal.Text = _s.FanNormalConfig;
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

        _s.KeepNetworkAdaptersPowered = _adapters.Checked;
        _s.FanControlEnabled = _fan.Checked;
        _s.FanControlExe = _fanExe.Text.Trim();
        _s.FanQuietConfig = _fanQuiet.Text.Trim();
        _s.FanNormalConfig = _fanNormal.Text.Trim();
    }

    private void SaveAndClose()
    {
        if (!string.IsNullOrEmpty(_fanQuiet.Text.Trim()) && !AppSettings.IsSafeConfigName(_fanQuiet.Text.Trim()) ||
            !string.IsNullOrEmpty(_fanNormal.Text.Trim()) && !AppSettings.IsSafeConfigName(_fanNormal.Text.Trim()))
        {
            MessageBox.Show(this, "Fan Control profiles must be plain file names ending in .json, e.g. Quiet.json", "RestMode");
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
        _helperStatus.Text = HelperClient.IsElevated
            ? "RestMode is running as administrator: no prompts needed."
            : installed
                ? "Helper task installed: admin features run without UAC prompts. Reinstall it after updating RestMode.exe or changing the FanControl.exe path."
                : "Helper task not installed: you'll see a UAC prompt when entering/leaving Rest Mode if an admin feature is on.";
    }

    // ------------------------------------------------------------------ layout helpers

    private static NumericUpDown Num(int min, int max) => new() { Minimum = min, Maximum = max, Width = 100, ThousandsSeparator = true };

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
                MaximumSize = new Size(560, 0),
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
