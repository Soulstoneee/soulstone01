using System.Diagnostics;
using RestMode.Core;
using RestMode.Util;
using static RestMode.Native.NativeMethods;

namespace RestMode.UI;

/// <summary>
/// The application: a tray icon + menu, the global hotkey, single-instance
/// signals (desktop shortcut "--enter"), and the Rest Mode controller.
/// </summary>
internal sealed class TrayApp : ApplicationContext
{
    private const int HotkeyId = 1;

    private readonly HiddenWindow _window = new();
    private readonly RestModeController _controller;
    private readonly NotifyIcon _tray;
    private readonly Icon _iconAwake = TrayIcons.Create(TrayIcons.AwakeColor);
    private readonly Icon _iconRest = TrayIcons.Create(TrayIcons.RestColor);
    private readonly ToolStripMenuItem _enterItem;
    private readonly ToolStripMenuItem _statusItem;
    private readonly SingleInstance _signals = new();
    private SettingsForm? _settingsForm;
    private bool _hotkeyRegistered;

    public TrayApp(CommandLine cmd)
    {
        _window.Init();
        _controller = new RestModeController(_window);

        var menu = new ContextMenuStrip();
        _statusItem = new ToolStripMenuItem("Awake") { Enabled = false };
        _enterItem = new ToolStripMenuItem("Enter Rest Mode", null, async (_, _) => await EnterFromUi("tray menu"))
        { Font = new Font(menu.Font, FontStyle.Bold) };
        menu.Items.Add(_statusItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_enterItem);
        menu.Items.Add("Settings...", null, (_, _) => ShowSettings());
        menu.Items.Add("View log", null, (_, _) => OpenLog());
        menu.Items.Add("Restore everything now", null, async (_, _) => await _controller.RestoreEverythingAsync());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, async (_, _) => await ExitAppAsync());
        menu.Opening += (_, _) => _statusItem.Text = _controller.StatusText;

        _tray = new NotifyIcon
        {
            Icon = _iconAwake,
            Text = "RestMode",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _tray.DoubleClick += (_, _) => ShowSettings();

        _controller.PhaseChanged += UpdateUi;
        _controller.Notify += ShowBalloon;
        _window.HotkeyPressed += id => { if (id == HotkeyId) _ = OnHotkey(); };

        _signals.Listen(SingleInstance.EnterEvent, () => _ = EnterFromUi("desktop shortcut"), _window);
        _signals.Listen(SingleInstance.RestoreEvent, () => _ = _controller.RestoreEverythingAsync(), _window);
        _signals.Listen(SingleInstance.ShowEvent, ShowSettings, _window);
        _signals.Listen(SingleInstance.ExitEvent, () => _ = ExitAppAsync(), _window);

        RegisterHotkey();
        _window.BeginInvoke(() => _ = StartAsync(cmd));
    }

    private async Task StartAsync(CommandLine cmd)
    {
        bool firstRun = !File.Exists(AppPaths.SettingsFile);
        _controller.Startup();
        if (firstRun)
        {
            SettingsStore.Save(SettingsStore.Current);
            ShowBalloon("RestMode is ready", $"Press {SettingsStore.Current.Hotkey} or use this tray icon to enter Rest Mode.");
            ShowSettings();
        }
        if (cmd.Enter) await EnterFromUi("command line");
        else if (cmd.ShowSettings) ShowSettings();
    }

    private async Task OnHotkey()
    {
        // The key-down that wakes the PC also fires the hotkey; don't re-enter right away.
        if (_controller.RecentlyWoken) return;
        await EnterFromUi("hotkey");
    }

    private async Task EnterFromUi(string trigger)
    {
        if (_controller.Current != RestModeController.Phase.Awake) return;
        _settingsForm?.Close();
        await _controller.EnterAsync(trigger);
    }

    public void RegisterHotkey()
    {
        if (_hotkeyRegistered) { UnregisterHotKey(_window.Handle, HotkeyId); _hotkeyRegistered = false; }
        string text = SettingsStore.Current.Hotkey;
        if (string.IsNullOrWhiteSpace(text)) return;
        if (!HotkeyParser.TryParse(text, out uint mods, out Keys key))
        {
            Logger.Warn($"Invalid hotkey '{text}'.");
            return;
        }
        _hotkeyRegistered = RegisterHotKey(_window.Handle, HotkeyId, mods | MOD_NOREPEAT, (uint)key);
        if (_hotkeyRegistered) Logger.Info($"Hotkey {text} registered.");
        else
        {
            Logger.Warn($"Hotkey {text} is already used by another program.");
            ShowBalloon("RestMode", $"The hotkey {text} is already used by another program. Pick another one in Settings.");
        }
    }

    private void UpdateUi()
    {
        bool resting = _controller.Current is RestModeController.Phase.Resting or RestModeController.Phase.Entering;
        _tray.Icon = resting ? _iconRest : _iconAwake;
        _tray.Text = resting ? "RestMode - resting" : "RestMode - awake";
        _enterItem.Enabled = _controller.Current == RestModeController.Phase.Awake;
    }

    private void ShowSettings()
    {
        if (_settingsForm is { IsDisposed: false })
        {
            _settingsForm.Activate();
            return;
        }
        _settingsForm = new SettingsForm(onSaved: RegisterHotkey, restoreNow: () => _controller.RestoreEverythingAsync());
        _settingsForm.FormClosed += (_, _) => _settingsForm = null;
        _settingsForm.Show();
        _settingsForm.Activate();
    }

    private static void OpenLog()
    {
        try
        {
            if (!File.Exists(AppPaths.LogFile)) File.WriteAllText(AppPaths.LogFile, "");
            Process.Start(new ProcessStartInfo(AppPaths.LogFile) { UseShellExecute = true });
        }
        catch (Exception ex) { Logger.Error("Opening log failed", ex); }
    }

    private void ShowBalloon(string title, string text)
    {
        if (!SettingsStore.Current.ShowNotifications) return;
        try { _tray.ShowBalloonTip(5000, title, text, ToolTipIcon.Info); } catch { /* ignore */ }
    }

    /// <summary>Unexpected UI-thread exception: log it, put the PC back to normal, keep running.</summary>
    public void OnFatal(Exception ex)
    {
        Logger.Error("Unexpected error", ex);
        try { _controller.RestoreNowSync("unexpected error"); } catch { /* ignore */ }
        ShowBalloon("RestMode", "Something went wrong; your settings were restored. See the log for details.");
    }

    /// <summary>Called on a non-UI thread when the process is about to die.</summary>
    public static void OnProcessCrash(Exception? ex)
    {
        Logger.Error("Fatal crash", ex);
        try { Restorer.RestoreLocal(StateStore.Load()); } catch { /* next launch will retry */ }
    }

    private async Task ExitAppAsync()
    {
        if (_controller.Current == RestModeController.Phase.Resting)
            await _controller.WakeAsync("RestMode is closing");
        else if (_controller.Current != RestModeController.Phase.Awake)
            _controller.RestoreNowSync("RestMode is closing");
        Logger.Info("RestMode exiting.");
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (_hotkeyRegistered) UnregisterHotKey(_window.Handle, HotkeyId);
            _tray.Visible = false;
            _tray.Dispose();
            _signals.Dispose();
            _controller.Dispose();
            _window.Dispose();
            _iconAwake.Dispose();
            _iconRest.Dispose();
        }
        base.Dispose(disposing);
    }
}
