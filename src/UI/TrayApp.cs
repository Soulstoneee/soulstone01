using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using RestMode.Core;
using RestMode.UI.Wpf;
using RestMode.Util;
using static RestMode.Native.NativeMethods;

namespace RestMode.UI;

/// <summary>
/// The running app: tray icon + menu, the global hotkey, single-instance
/// signals (Start menu / desktop "Rest now", --wake, --simulate), the Rest
/// Mode controller, and the WPF windows (main, settings, statistics, wizard,
/// the Rest overlay).
/// </summary>
internal sealed class TrayApp : IAppActions, IDisposable
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
    private MainWindow? _main;
    private SettingsWindow? _settingsWindow;
    private StatsWindow? _statsWindow;
    private bool _hotkeyRegistered;
    private bool _exiting;

    public RestModeController Controller => _controller;

    public TrayApp(CommandLine cmd)
    {
        _window.Init();
        _controller = new RestModeController(_window)
        {
            PlayEnterAnimation = () => RestOverlay.ShowEnterAsync(SettingsStore.Current.OverlayText),
            PlayWakeAnimation = () =>
            {
                if (SettingsStore.Current.RestAnimation) RestOverlay.ShowWelcome(SettingsStore.Current.WelcomeText);
                else RestOverlay.CloseAll();
            },
            HideOverlay = RestOverlay.CloseAll,
        };

        var menu = new ContextMenuStrip();
        _statusItem = new ToolStripMenuItem("Awake") { Enabled = false };
        _enterItem = new ToolStripMenuItem("Rest", null, async (_, _) => await EnterFromUi("tray menu"))
        { Font = new Font(menu.Font, System.Drawing.FontStyle.Bold) };
        menu.Items.Add(_statusItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_enterItem);
        menu.Items.Add("Open RestMode", null, (_, _) => ShowMain());
        menu.Items.Add("Settings…", null, (_, _) => ShowSettings());
        menu.Items.Add("Statistics", null, (_, _) => ShowStats());
        menu.Items.Add("View log", null, (_, _) => OpenLog());
        menu.Items.Add("Restore everything now", null, async (_, _) => await RestoreEverything());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitApp());
        menu.Opening += (_, _) => _statusItem.Text = _controller.StatusText;

        _tray = new NotifyIcon
        {
            Icon = _iconAwake,
            Text = "RestMode",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _tray.DoubleClick += (_, _) => ShowMain();

        _controller.PhaseChanged += UpdateUi;
        _controller.Notify += ShowBalloon;
        _window.HotkeyPressed += id => { if (id == HotkeyId) _ = OnHotkey(); };

        _signals.Listen(SingleInstance.EnterEvent, () => _ = EnterFromUi("shortcut"), _window);
        _signals.Listen(SingleInstance.WakeEvent, () => { if (_controller.Current == RestModeController.Phase.Resting) _ = _controller.WakeAsync("command line"); }, _window);
        _signals.Listen(SingleInstance.RestoreEvent, () => _ = RestoreEverything(), _window);
        _signals.Listen(SingleInstance.ShowEvent, ShowMain, _window);
        _signals.Listen(SingleInstance.ExitEvent, ExitApp, _window);
        _signals.Listen(SingleInstance.SimulateEvent, () => _ = _controller.OnSimulateAsync(), _window);
        SettingsStore.Saved += RegisterHotkey;

        RegisterHotkey();
        _window.BeginInvoke(() => _ = StartAsync(cmd));
    }

    private async Task StartAsync(CommandLine cmd)
    {
        bool firstRun = !SettingsStore.Current.FirstRunDone && !File.Exists(AppPaths.SettingsFile);
        _controller.Startup();
        if (!File.Exists(AppPaths.SettingsFile)) SettingsStore.Save(SettingsStore.Current);
        if (firstRun || !SettingsStore.Current.FirstRunDone)
        {
            if (!cmd.Enter && !cmd.Startup) new WizardWindow(this).Show();
        }
        else if (!cmd.Startup && !cmd.Enter && !cmd.ShowSettings && !cmd.Wake)
        {
            ShowMain();
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

    public async Task EnterFromUi(string trigger)
    {
        if (_controller.Current != RestModeController.Phase.Awake) return;
        _settingsWindow?.Close();
        _statsWindow?.Close();
        _main?.Hide();
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
        if (!resting) _main?.Refresh();
    }

    public void ShowMain()
    {
        if (_exiting) return;
        _main ??= new MainWindow(this);
        _main.Show();
        if (_main.WindowState == System.Windows.WindowState.Minimized) _main.WindowState = System.Windows.WindowState.Normal;
        _main.Activate();
        _main.Refresh();
    }

    public void ShowSettings()
    {
        if (_settingsWindow is { IsLoaded: true }) { _settingsWindow.Activate(); return; }
        _settingsWindow = new SettingsWindow(this);
        _settingsWindow.Closed += (_, _) => { _settingsWindow = null; _main?.Refresh(); };
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    public void ShowStats()
    {
        if (_statsWindow is { IsLoaded: true }) { _statsWindow.Activate(); return; }
        _statsWindow = new StatsWindow();
        _statsWindow.Closed += (_, _) => _statsWindow = null;
        _statsWindow.Show();
        _statsWindow.Activate();
    }

    public Task RestoreEverything() => _controller.RestoreEverythingAsync();

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
        try { RestOverlay.CloseAll(); } catch { /* ignore */ }
        try { _controller.RestoreNowSync("unexpected error"); } catch { /* ignore */ }
        ShowBalloon("RestMode", "Something went wrong; your settings were restored. See the log for details.");
    }

    /// <summary>Called on a non-UI thread when the process is about to die.</summary>
    public static void OnProcessCrash(Exception? ex)
    {
        Logger.Error("Fatal crash", ex);
        try { Restorer.RestoreLocal(StateStore.Load()); } catch { /* next launch will retry */ }
        try { LidManager.RestoreAll(); } catch { /* next launch will retry */ }
    }

    public async void ExitApp()
    {
        if (_exiting) return;
        _exiting = true;
        if (_controller.Current == RestModeController.Phase.Resting)
            await _controller.WakeAsync("RestMode is closing");
        else if (_controller.Current != RestModeController.Phase.Awake)
            _controller.RestoreNowSync("RestMode is closing");
        _controller.OnAppExit();
        RestOverlay.CloseAll();
        _settingsWindow?.Close();
        _statsWindow?.Close();
        _main?.CloseForGood();
        Logger.Info("RestMode exiting.");
        Dispose();
        System.Windows.Application.Current.Shutdown();
    }

    public void Dispose()
    {
        SettingsStore.Saved -= RegisterHotkey;
        if (_hotkeyRegistered) { UnregisterHotKey(_window.Handle, HotkeyId); _hotkeyRegistered = false; }
        _tray.Visible = false;
        _tray.Dispose();
        _signals.Dispose();
        _controller.Dispose();
        _window.Dispose();
        _iconAwake.Dispose();
        _iconRest.Dispose();
    }
}
