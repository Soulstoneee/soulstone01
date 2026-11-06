using System.Diagnostics;
using RestMode.Core.Fans;
using RestMode.Elevation;
using RestMode.UI;
using RestMode.Util;
using static RestMode.Native.NativeMethods;

namespace RestMode.Core;

/// <summary>
/// Orchestrates Rest Mode:
///
///   Awake --Enter--> Entering --> Resting --(input / end action / resume / logoff)--> Exiting --> Awake
///
/// Lives on the UI thread. All waits are async so the message loop (hotkey,
/// power broadcasts, input hooks) keeps running.
/// </summary>
internal sealed class RestModeController : IDisposable
{
    public enum Phase { Awake, Entering, Resting, Exiting }

    private readonly HiddenWindow _window;
    private readonly InputWatcher _input = new();
    private readonly ActivityMonitor _activity = new();
    private readonly System.Windows.Forms.Timer _sampleTimer = new() { Interval = 5000 };
    private readonly System.Windows.Forms.Timer _pendingTimer = new() { Interval = 3000 };

    private AppSettings _settings = SettingsStore.Current;
    private RestState? _state;
    private DateTime? _idleSince;
    private DateTime _lastStatusLog;
    private DateTime _lastWakeUtc = DateTime.MinValue;
    private bool _stayLogged;
    private int _displayReoffCount;
    private bool _displayReoffBusy;
    private bool _shutdownPending;
    private bool _pendingBusy;
    private bool _pendingCanRunSilently;
    private PowerCapabilities _caps = PowerCapabilities.Read();
    /// <summary>Last console display state from Windows: 0 off, 1 on, 2 dimmed.</summary>
    private int _displayState = 1;
    /// <summary>When the 5-second sample timer last ran; a big gap means Windows froze RestMode (standby).</summary>
    private DateTime? _lastSampleUtc;
    private double _pausedSeconds;

    public Phase Current { get; private set; } = Phase.Awake;
    public event Action? PhaseChanged;
    /// <summary>(title, text) for a tray balloon.</summary>
    public event Action<string, string>? Notify;

    public RestModeController(HiddenWindow window)
    {
        _window = window;
        _input.Woken += OnInput;
        _sampleTimer.Tick += OnSample;
        _pendingTimer.Tick += OnPendingTick;
        _window.PowerBroadcast += OnPowerBroadcast;
        _window.DisplayStateChanged += OnDisplayStateChanged;
        _window.SessionEnding += OnSessionEnding;
    }

    public bool RecentlyWoken => DateTime.UtcNow - _lastWakeUtc < TimeSpan.FromSeconds(3);

    // =====================================================================
    // Startup / crash recovery
    // =====================================================================

    public void Startup()
    {
        var caps = PowerCapabilities.Read();
        Logger.Info($"RestMode started. {caps}");
        try { Logger.Info($"Device: {DeviceProfile.Detect().Summary()}"); } catch { /* logging only */ }

        var st = StateStore.Load();
        if (st?.Active == true)
        {
            Logger.Warn($"RestMode did not exit cleanly last time (rest started {st.StartedUtc.ToLocalTime():g}). Restoring saved settings.");
            Restorer.RestoreLocal(st);
            Logger.Session($"start={st.StartedUtc.ToLocalTime():yyyy-MM-dd HH:mm} end=recovered-on-next-launch");
            Notify?.Invoke("RestMode", "Your power plan, volume and apps were restored after an unexpected exit.");
        }
        PowerPlanManager.CleanupOrphans();
        StartPendingWatch();
    }

    // =====================================================================
    // Enter
    // =====================================================================

    public async Task EnterAsync(string trigger)
    {
        if (Current != Phase.Awake) return;
        if (_shutdownPending) CancelShutdown("Rest Mode entered");

        _settings = SettingsStore.Current.Clone();
        var s = _settings;
        var dev = DeviceProfile.Detect();
        _caps = dev.Caps;
        SetPhase(Phase.Entering);
        Logger.Info($"=== Entering Rest Mode ({trigger}) ===");
        Logger.Info($"Device: {dev.Summary()}");
        if (_caps.ModernStandby)
            Logger.Info("This PC uses Modern Standby: the screen will be turned off by a short idle timer so Windows stays fully on.");

        var fan = FanPlanner.Resolve(s, dev);
        foreach (var n in fan.Notes) Logger.Info("Fans: " + n);
        Logger.Info($"Fans: using {fan.Describe()} (plus the CPU limit / passive cooling of the Rest Mode plan).");

        // Laptops / handhelds: warn if resting on battery (the end action runs at the battery limit).
        if (dev.UsesBattery && SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Offline)
        {
            int pct = (int)Math.Round(SystemInformation.PowerStatus.BatteryLifePercent * 100);
            Logger.Warn($"Entering Rest Mode on battery ({pct}%).");
            Notify?.Invoke("RestMode - on battery",
                s.LowBatteryPercent > 0
                    ? $"Battery at {pct}%. Rest Mode ends at {s.LowBatteryPercent}%. Plug in for long downloads."
                    : $"Battery at {pct}%. Plug in for long downloads.");
        }

        var st = new RestState { Active = true, StartedUtc = DateTime.UtcNow, Trigger = trigger };
        _state = st;
        Save();

        try
        {
            // 1. Admin-only bits FIRST (may show ONE UAC prompt). The user's own plan is still active
            //    here, so the screen stays on while the prompt waits. (On Modern Standby PCs the Rest Mode
            //    plan turns the screen off after 15 s idle, which would hide the prompt.) The wait is
            //    time-limited and never blocks the UI thread; without an answer those steps are skipped.
            int adaptersToChange = s.KeepNetworkAdaptersPowered ? NetworkAdapterPower.CountActiveNeedingChange() : 0;
            if (s.KeepNetworkAdaptersPowered && adaptersToChange == 0)
                Logger.Info("Network: the connected adapter(s) already stay powered (no \"allow the computer to turn off this device\" option ticked) - nothing to change.");
            var work = new HelperWork
            {
                Adapters = s.KeepNetworkAdaptersPowered && adaptersToChange != 0,
                BuiltInId = fan.BuiltIn?.Id,
                FanExe = fan.FanControlExe,
                FanQuiet = s.FanQuietConfig,
                FanNormal = s.FanNormalConfig,
            };
            if (work.Any)
            {
                st.HelperRequested = true;
                Save();
                LogHelper("apply", await HelperClient.RunAsync(wantActive: true, work, s, allowPrompt: true));
            }

            // 2. Windows power mode slider -> Best power efficiency (universal fan fallback, no admin).
            if (fan.WindowsPowerMode)
            {
                Step("Windows power mode", () =>
                {
                    var before = WindowsPowerMode.Get();
                    if (before == null) { Logger.Info("Windows power mode is not available on this PC - skipped."); return; }
                    if (before == WindowsPowerMode.BestEfficiency) { Logger.Info("Windows power mode is already Best power efficiency."); return; }
                    st.PowerModeBefore = before;
                    st.PowerModeChanged = true;
                    Save();
                    if (WindowsPowerMode.Set(WindowsPowerMode.BestEfficiency))
                        Logger.Info($"Windows power mode: {WindowsPowerMode.Name(before)} -> Best power efficiency.");
                    else
                    {
                        st.PowerModeChanged = false;
                        Save();
                        Logger.Info("Windows power mode could not be changed on this PC - skipped.");
                    }
                });
            }

            // 3. Low-power plan (duplicate of the user's plan; theirs is never edited).
            Step("power plan", () =>
            {
                var original = PowerPlanManager.GetActiveScheme() ?? throw new InvalidOperationException("no active plan");
                st.OriginalScheme = original;
                Save();
                Logger.Info($"Saved current plan: {PowerPlanManager.GetName(original) ?? original.ToString()}");
                var temp = PowerPlanManager.CreateRestPlan(original, s, dev);
                st.TempScheme = temp;
                Save();
                if (!PowerPlanManager.Activate(temp)) throw new InvalidOperationException("could not activate the RestMode plan");
                Logger.Info("RestMode low-power plan is active.");
                if (!PowerPlanManager.CpuLimitApplied)
                    Notify?.Invoke("RestMode", "This device doesn't allow limiting the CPU speed. Rest Mode still works, but the PC may stay warmer.");
            });

            // Some manufacturer apps switch the power plan a moment after their fan mode changes. Keep ours.
            if (HelperClient.LoadHelperState()?.BuiltInId != null)
            {
                await Task.Delay(2000);
                if (st.TempScheme is Guid t && PowerPlanManager.GetActiveScheme() != t && PowerPlanManager.Exists(t))
                {
                    Logger.Warn("Another program switched the power plan after the fan mode changed - switching back to the Rest Mode plan.");
                    PowerPlanManager.Activate(t);
                }
            }

            // 4. Background apps (optional).
            if (s.ThrottleApps && s.Apps.Count > 0)
            {
                try
                {
                    await ProcessThrottler.ApplyAsync(s.Apps, snap => { st.Processes.Add(snap); Save(); });
                }
                catch (Exception ex) { Logger.Error("Throttling apps failed (skipped)", ex); }
            }

            // 5. Mute.
            if (s.MuteAudio)
            {
                Step("mute", () =>
                {
                    var snap = AudioController.Capture() ?? throw new InvalidOperationException("no playback device");
                    st.Audio = snap;
                    Save();
                    AudioController.Mute(snap.DeviceId);
                    Logger.Info($"Audio muted (was volume {snap.Volume:P0}, muted={snap.Muted}).");
                });
            }

            // 6. Keep the SYSTEM awake but let the DISPLAY sleep.
            Step("keep-awake request", () =>
            {
                var prev = SetThreadExecutionState(EXECUTION_STATE.ES_CONTINUOUS | EXECUTION_STATE.ES_SYSTEM_REQUIRED);
                if (prev == 0) throw new InvalidOperationException("SetThreadExecutionState failed");
                Logger.Info("System kept awake (ES_SYSTEM_REQUIRED), display allowed to turn off.");
            });

            // 7. Start measuring traffic, then switch the screen off once the keys are released.
            _activity.Start();
            await WaitForInputQuietAsync();
            DisplayController.TurnOff(_window.Handle, _caps.ModernStandby);

            // 8. Watch for the user and for downloads finishing.
            _idleSince = null;
            _stayLogged = false;
            _displayReoffCount = 0;
            _lastSampleUtc = DateTime.UtcNow;
            _pausedSeconds = 0;
            _lastStatusLog = DateTime.UtcNow;
            _input.Start(s.MouseMoveThresholdPx, s.WakeWithController);
            _sampleTimer.Start();
            SetPhase(Phase.Resting);
            Logger.Info("Resting. Waiting for input or for downloads to finish.");
            if (_caps.ModernStandby) _ = CheckScreenWentOffAsync(st);
        }
        catch (Exception ex)
        {
            Logger.Error("Entering Rest Mode failed - restoring everything", ex);
            await ExitAsync("error while entering", wakeDisplay: true, attended: true, outcome: "Error");
        }
    }

    /// <summary>
    /// Waits until no key/mouse input for ~1 s (max 5 s). Otherwise releasing
    /// the hotkey or the mouse button would wake the screen straight away.
    /// </summary>
    private static async Task WaitForInputQuietAsync()
    {
        var sw = Stopwatch.StartNew();
        while (GetIdleMilliseconds() < 1000 && sw.ElapsedMilliseconds < 5000)
            await Task.Delay(100);
    }

    /// <summary>
    /// Modern Standby PCs: the idle timer should have switched the screen off by
    /// now. If it is still on, an app is holding a "display required" request
    /// (video playback, a presentation, some games) - say so in the log.
    /// </summary>
    private async Task CheckScreenWentOffAsync(RestState session)
    {
        await Task.Delay(TimeSpan.FromSeconds(PowerPlanManager.ModernStandbyScreenOffSeconds + 25));
        if (Current != Phase.Resting || !ReferenceEquals(_state, session) || _displayState == 0) return;
        Logger.Warn("The screen is still on: an app is keeping it awake (video, presentation mode, a game). " +
                    "Rest Mode keeps running; run 'powercfg /requests' as administrator to see which app.");
        Notify?.Invoke("RestMode", "An app is keeping the screen on. Rest Mode is still active.");
    }

    // =====================================================================
    // Wake / exit
    // =====================================================================

    /// <summary>User is back: screen on, everything restored.</summary>
    public Task WakeAsync(string reason) => ExitAsync(reason, wakeDisplay: true, attended: true, outcome: "Woken");

    private void OnInput(string what)
    {
        if (_shutdownPending)
        {
            CancelShutdown($"user input ({what})");
            return;
        }
        if (Current == Phase.Resting)
            _ = WakeAsync(what);
    }

    private async Task ExitAsync(string reason, bool wakeDisplay, bool attended, string outcome)
    {
        if (Current is Phase.Awake or Phase.Exiting) return;
        SetPhase(Phase.Exiting);
        _lastWakeUtc = DateTime.UtcNow;
        Logger.Info($"=== Leaving Rest Mode: {reason} ===");

        _input.Stop();
        _sampleTimer.Stop();
        if (wakeDisplay) DisplayController.TurnOn(_window.Handle);

        var st = _state ?? StateStore.Load();
        long rx = _activity.TotalReceived, tx = _activity.TotalSent;
        _activity.Stop();

        Restorer.RestoreLocal(st);
        _state = null;

        if (HelperClient.HasPendingRestore())
        {
            var r = await HelperClient.SyncAsync(wantActive: false, _settings, allowPrompt: attended);
            LogHelper("restore", r);
            if (HelperClient.HasPendingRestore()) StartPendingWatch();
            // A manufacturer app may switch plans when its fan mode is restored: make sure the user's plan is active.
            if (st?.OriginalScheme is Guid o && PowerPlanManager.GetActiveScheme() != o && PowerPlanManager.Exists(o))
            {
                Logger.Warn("Another program changed the power plan while restoring the fan mode - switching back to your plan.");
                PowerPlanManager.Activate(o);
            }
        }

        if (st != null)
        {
            // Rest Mode must never let Windows really suspend the PC (network off, apps frozen). Verify it.
            var standby = StandbyEvents.Since(st.StartedUtc);
            if (_pausedSeconds > 0)
            {
                Logger.Warn($"PROBLEM: Windows froze apps for about {_pausedSeconds:0} s in total during this session. Windows' standby log:");
                foreach (var line in standby) Logger.Warn("  " + line);
            }
            else
            {
                Logger.Info("Checked: the PC stayed fully running for the whole session (no app freeze detected).");
                foreach (var line in standby) Logger.Info("  Windows standby log: " + line);
            }

            var dur = DateTime.UtcNow - st.StartedUtc;
            Logger.Session($"start={st.StartedUtc.ToLocalTime():yyyy-MM-dd HH:mm} duration={(int)dur.TotalHours}h{dur.Minutes:00}m " +
                           $"downloaded={ActivityMonitor.FormatBytes(rx)} uploaded={ActivityMonitor.FormatBytes(tx)} " +
                           $"end={outcome} ({reason})");
        }
        SetPhase(Phase.Awake);
    }

    /// <summary>
    /// Synchronous restore for log-off / shutdown / fatal errors, where we
    /// cannot await or show UAC prompts. Admin-only items are retried on the next launch.
    /// </summary>
    public void RestoreNowSync(string reason)
    {
        if (Current == Phase.Awake && StateStore.Load() == null) return;
        Logger.Info($"Emergency restore: {reason}");
        _input.Stop();
        _sampleTimer.Stop();
        try { _activity.Stop(); } catch { /* ignore */ }
        Restorer.RestoreLocal(_state ?? StateStore.Load());
        if (_state != null) Logger.Session($"start={_state.StartedUtc.ToLocalTime():yyyy-MM-dd HH:mm} end=Restored ({reason})");
        _state = null;
        SetPhase(Phase.Awake);
    }

    /// <summary>Tray "Restore everything now": works whether resting or not.</summary>
    public async Task RestoreEverythingAsync()
    {
        if (Current == Phase.Resting) { await WakeAsync("restore requested"); return; }
        if (Current != Phase.Awake) return;
        Restorer.RestoreLocal(StateStore.Load());
        PowerPlanManager.CleanupOrphans();
        if (HelperClient.HasPendingRestore())
            LogHelper("restore", await HelperClient.SyncAsync(false, SettingsStore.Current, allowPrompt: true));
        Notify?.Invoke("RestMode", HelperClient.HasPendingRestore()
            ? "Restored, except network adapter / Fan Control settings (admin approval needed)."
            : "Everything is back to your normal settings.");
    }

    // =====================================================================
    // While resting: downloads-finished detection
    // =====================================================================

    private async void OnSample(object? sender, EventArgs e)
    {
        if (Current != Phase.Resting) return;
        var s = _settings;
        try
        {
            // The timer fires every 5 s. A much longer gap means RestMode (and every
            // other desktop app) was frozen - i.e. Windows really went into standby.
            var now = DateTime.UtcNow;
            if (_lastSampleUtc is DateTime last)
            {
                double gap = (now - last).TotalSeconds - _sampleTimer.Interval / 1000.0;
                if (gap > 20)
                {
                    _pausedSeconds += gap;
                    Logger.Warn($"RestMode was frozen for about {gap:0} s: Windows suspended running apps (standby). Downloads were paused.");
                }
            }
            _lastSampleUtc = now;

            _activity.Sample();

            // Laptop safety: don't run the battery flat while "off".
            var ps = SystemInformation.PowerStatus;
            if (s.LowBatteryPercent > 0 && ps.PowerLineStatus == PowerLineStatus.Offline &&
                !ps.BatteryChargeStatus.HasFlag(BatteryChargeStatus.NoSystemBattery) &&
                ps.BatteryLifePercent >= 0 && ps.BatteryLifePercent * 100 <= s.LowBatteryPercent)
            {
                var action = s.EndAction is EndAction.Shutdown ? EndAction.Shutdown : EndAction.Hibernate;
                await EndAsync(action, $"battery at {ps.BatteryLifePercent:P0}");
                return;
            }

            bool netIdle = _activity.NetKBps < s.IdleThresholdKBps;
            bool diskIdle = s.DiskThresholdKBps == 0 || !_activity.DiskAvailable || _activity.DiskKBps < s.DiskThresholdKBps;

            if (netIdle && diskIdle)
            {
                _idleSince ??= DateTime.UtcNow;
            }
            else
            {
                if (_idleSince != null && _stayLogged) Logger.Info("Activity resumed.");
                _idleSince = null;
                _stayLogged = false;
            }

            if (DateTime.UtcNow - _lastStatusLog > TimeSpan.FromMinutes(5))
            {
                _lastStatusLog = DateTime.UtcNow;
                Logger.Info($"Resting: net {_activity.NetKBps:0.#} KB/s, disk {(_activity.DiskAvailable ? $"{_activity.DiskKBps:0.#} KB/s" : "n/a")}, " +
                            $"downloaded so far {ActivityMonitor.FormatBytes(_activity.TotalReceived)}" +
                            (_idleSince is DateTime t ? $", quiet for {(DateTime.UtcNow - t).TotalMinutes:0} min" : ""));
            }

            if (_idleSince is DateTime since && DateTime.UtcNow - since >= TimeSpan.FromMinutes(s.IdleMinutes))
                await EndAsync(s.EndAction, $"traffic below {s.IdleThresholdKBps} KB/s for {s.IdleMinutes} min");
        }
        catch (Exception ex)
        {
            Logger.Error("Activity sample failed", ex);
        }
    }

    private async Task EndAsync(EndAction action, string why)
    {
        if (action == EndAction.Stay)
        {
            if (!_stayLogged) Logger.Info($"Downloads look finished ({why}); staying in Rest Mode as configured.");
            _stayLogged = true;
            return;
        }

        Logger.Info($"Downloads look finished ({why}) -> {action}.");
        await ExitAsync($"downloads finished: {why}", wakeDisplay: false, attended: false, outcome: action.ToString());

        switch (action)
        {
            case EndAction.Sleep:
                Suspend(hibernate: false);
                break;
            case EndAction.Hibernate:
                var caps = PowerCapabilities.Read();
                if (!caps.HibernateSupported) Logger.Warn("Hibernation is not available on this PC - sleeping instead.");
                Suspend(caps.HibernateSupported);
                break;
            case EndAction.Shutdown:
                StartShutdown();
                break;
        }
    }

    private void Suspend(bool hibernate)
    {
        try
        {
            Logger.Info(hibernate ? "Hibernating." : "Going to sleep.");
            // Returns after the PC wakes up again.
            bool ok = Application.SetSuspendState(hibernate ? PowerState.Hibernate : PowerState.Suspend, false, false);
            if (!ok)
            {
                Logger.Warn("Windows refused to sleep/hibernate (blocked by policy or a driver). Turning the screen off instead.");
                DisplayController.TurnOff(_window.Handle, _caps.ModernStandby);
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Sleep/hibernate failed", ex);
        }
    }

    private void StartShutdown()
    {
        try
        {
            var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "shutdown.exe"))
            { UseShellExecute = false, CreateNoWindow = true };
            foreach (var a in new[] { "/s", "/t", "60", "/c", "RestMode: downloads finished. Move the mouse or press a key to cancel." })
                psi.ArgumentList.Add(a);
            using var p = Process.Start(psi);
            _shutdownPending = true;
            Logger.Info("Shutdown scheduled in 60 s (any input cancels it).");
            // Reuse the input watcher as a "cancel" detector.
            _input.Start(_settings.MouseMoveThresholdPx, _settings.WakeWithController);
        }
        catch (Exception ex)
        {
            Logger.Error("Starting shutdown failed", ex);
        }
    }

    private void CancelShutdown(string why)
    {
        _shutdownPending = false;
        _input.Stop();
        try
        {
            using var p = Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "shutdown.exe"), "/a")
            { UseShellExecute = false, CreateNoWindow = true });
            Logger.Info($"Shutdown cancelled: {why}.");
            Notify?.Invoke("RestMode", "Shutdown cancelled.");
        }
        catch (Exception ex)
        {
            Logger.Error("Cancelling shutdown failed", ex);
        }
    }

    // =====================================================================
    // Windows events
    // =====================================================================

    private async void OnPowerBroadcast(int ev)
    {
        switch (ev)
        {
            case PBT_APMSUSPEND:
                Logger.Info(Current == Phase.Resting ? "PC is going to sleep while in Rest Mode." : "PC is going to sleep.");
                break;
            case PBT_APMRESUMEAUTOMATIC:
            case PBT_APMRESUMESUSPEND:
                Logger.Info($"PC resumed from sleep ({(ev == PBT_APMRESUMESUSPEND ? "by user" : "automatic")}).");
                if (Current == Phase.Resting)
                    await ExitAsync("PC resumed from sleep", wakeDisplay: false, attended: ev == PBT_APMRESUMESUSPEND, outcome: "SleptExternally");
                StartPendingWatch();
                break;
        }
    }

    /// <summary>
    /// The screen came on but we did not wake (a notification, a driver, or a
    /// mouse nudge smaller than the wake threshold): once input has been quiet
    /// for a few seconds, turn it off again.
    /// </summary>
    private async void OnDisplayStateChanged(int state)
    {
        int previous = _displayState;
        _displayState = state;
        if (Current == Phase.Resting && state != previous)
            Logger.Info($"Display is now {(state == 0 ? "off" : state == 2 ? "dimmed" : "on")}.");

        // Modern Standby: never send "monitor off"; the short idle timer turns the screen off again by itself.
        if (_caps.ModernStandby) return;
        if (Current != Phase.Resting || state != 1 || _displayReoffBusy) return;
        _displayReoffBusy = true;
        try
        {
            do { await Task.Delay(1000); }
            while (Current == Phase.Resting && GetIdleMilliseconds() < 4000);

            if (Current != Phase.Resting || _displayReoffCount >= 500) return;
            if (_displayReoffCount++ < 5) Logger.Info("Display came on without a wake input - turning it off again.");
            DisplayController.TurnOff(_window.Handle, modernStandby: false);
        }
        finally { _displayReoffBusy = false; }
    }

    private void OnSessionEnding()
    {
        if (Current != Phase.Awake || StateStore.Load() != null)
            RestoreNowSync("Windows is logging off / shutting down");
    }

    // =====================================================================
    // Pending admin restore (adapters / Fan Control) after unattended exits
    // =====================================================================

    private void StartPendingWatch()
    {
        if (!HelperClient.HasPendingRestore()) return;
        _pendingCanRunSilently = HelperClient.IsElevated || TaskInstaller.IsInstalled();
        _pendingTimer.Start();
    }

    private async void OnPendingTick(object? sender, EventArgs e)
    {
        if (Current != Phase.Awake || _pendingBusy) return;
        if (!HelperClient.HasPendingRestore()) { _pendingTimer.Stop(); return; }

        // Only show a UAC prompt when someone is actually at the PC.
        bool userPresent = GetIdleMilliseconds() < 3000;
        if (!userPresent && !_pendingCanRunSilently) return;

        _pendingBusy = true;
        try
        {
            var r = await HelperClient.SyncAsync(false, SettingsStore.Current, allowPrompt: userPresent);
            LogHelper("restore (deferred)", r);
            if (!HelperClient.HasPendingRestore()) _pendingTimer.Stop();
            else if (r == null && userPresent)
            {
                _pendingTimer.Stop();
                Notify?.Invoke("RestMode", "Network adapter / Fan Control settings were not restored. Use \"Restore everything now\" in the tray menu.");
            }
        }
        finally { _pendingBusy = false; }
    }

    // =====================================================================
    // helpers
    // =====================================================================

    private void Save()
    {
        if (_state != null) StateStore.Save(_state);
    }

    private static void Step(string what, Action action)
    {
        try { action(); }
        catch (Exception ex) { Logger.Error($"Step '{what}' failed (skipped, continuing)", ex); }
    }

    private static void LogHelper(string what, HelperResult? r)
    {
        if (r == null) { Logger.Info($"Admin helper ({what}) did not run."); return; }
        Logger.Info($"Admin helper ({what}) {(r.Success ? "OK" : "finished with problems")}:");
        foreach (var m in r.Messages) Logger.Info("  " + m);
    }

    private void SetPhase(Phase p)
    {
        Current = p;
        PhaseChanged?.Invoke();
    }

    public string StatusText => Current switch
    {
        Phase.Resting => $"Resting - {_activity.NetKBps:0} KB/s, {ActivityMonitor.FormatBytes(_activity.TotalReceived)} downloaded",
        Phase.Entering => "Entering Rest Mode...",
        Phase.Exiting => "Waking up...",
        _ => "Awake",
    };

    public void Dispose()
    {
        _input.Dispose();
        _activity.Dispose();
        _sampleTimer.Dispose();
        _pendingTimer.Dispose();
    }
}
