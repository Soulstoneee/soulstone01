using System.Runtime.InteropServices;
using RestMode.Util;
using static RestMode.Native.NativeMethods;

namespace RestMode.Core;

/// <summary>
/// Detects "the user is back" while resting - the PC equivalent of pressing
/// the PS button.
///
/// * Keyboard: any key press (low-level keyboard hook).
/// * Mouse: any click / wheel, or moving more than N pixels (low-level mouse
///   hook; the small threshold ignores optical-sensor jitter).
/// * Controller: any button on an XInput pad (Xbox, or PlayStation pads via
///   DS4Windows / Steam Input).
/// * Fallback: if the hooks cannot be installed, GetLastInputInfo polling.
///
/// Must be created and used on the UI thread (the hooks need its message loop).
/// </summary>
internal sealed class InputWatcher : IDisposable
{
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 250 };
    private readonly LowLevelProc _kbProc;
    private readonly LowLevelProc _mouseProc;
    private IntPtr _kbHook, _mouseHook;
    private bool _running;
    private bool _firePosted;
    private uint _baselineTick;
    private POINT? _mouseOrigin;
    private int _thresholdPx;
    private bool _useController;
    private int _tickCount;

    private readonly bool[] _padConnected = new bool[4];
    private readonly ushort[] _padBaselineButtons = new ushort[4];
    private bool _xinputAvailable = true;

    /// <summary>Raised once (on the UI thread) with a description of the input.</summary>
    public event Action<string>? Woken;

    public InputWatcher()
    {
        _kbProc = KeyboardHook;
        _mouseProc = MouseHook;
        _timer.Tick += (_, _) => Poll();
    }

    public void Start(int mouseThresholdPx, bool useController)
    {
        Stop();
        _thresholdPx = mouseThresholdPx;
        _useController = useController;
        _baselineTick = GetLastInputTick();
        _mouseOrigin = null;
        _firePosted = false;
        _running = true;

        try
        {
            IntPtr mod = GetModuleHandle(null);
            _kbHook = SetWindowsHookEx(WH_KEYBOARD_LL, _kbProc, mod, 0);
            _mouseHook = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, mod, 0);
            if (_kbHook == IntPtr.Zero || _mouseHook == IntPtr.Zero)
                Logger.Warn($"Low-level input hooks not installed (error {Marshal.GetLastWin32Error()}); using GetLastInputInfo polling.");
        }
        catch (Exception ex)
        {
            Logger.Warn($"Input hooks failed: {ex.Message}; using GetLastInputInfo polling.");
        }

        if (_useController) SnapshotPads();
        _timer.Start();
    }

    public void Stop()
    {
        _running = false;
        _timer.Stop();
        if (_kbHook != IntPtr.Zero) { UnhookWindowsHookEx(_kbHook); _kbHook = IntPtr.Zero; }
        if (_mouseHook != IntPtr.Zero) { UnhookWindowsHookEx(_mouseHook); _mouseHook = IntPtr.Zero; }
    }

    private bool HooksActive => _kbHook != IntPtr.Zero && _mouseHook != IntPtr.Zero;

    private void Fire(string what)
    {
        if (!_running) return;
        Stop();
        Logger.Info($"Wake input: {what}");
        Woken?.Invoke(what);
    }

    private IntPtr KeyboardHook(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && _running && !_firePosted)
        {
            int msg = (int)wParam;
            if (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN)
                BeginFire("key press");
        }
        return CallNextHookEx(_kbHook, nCode, wParam, lParam);
    }

    private IntPtr MouseHook(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && _running && !_firePosted)
        {
            int msg = (int)wParam;
            switch (msg)
            {
                case WM_LBUTTONDOWN or WM_RBUTTONDOWN or WM_MBUTTONDOWN or WM_XBUTTONDOWN or WM_MOUSEWHEEL or WM_MOUSEHWHEEL:
                    BeginFire("mouse button");
                    break;
                case WM_MOUSEMOVE:
                    var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                    if (_mouseOrigin is not POINT o)
                    {
                        _mouseOrigin = data.pt;
                        if (_thresholdPx == 0) BeginFire("mouse movement");
                    }
                    else
                    {
                        int dx = data.pt.X - o.X, dy = data.pt.Y - o.Y;
                        if (dx * dx + dy * dy >= _thresholdPx * _thresholdPx)
                            BeginFire("mouse movement");
                    }
                    break;
            }
        }
        return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    // Hooks must return quickly, so the actual work is queued to the message loop.
    private void BeginFire(string what)
    {
        _firePosted = true;
        var ctx = SynchronizationContext.Current;
        if (ctx != null) ctx.Post(_ => Fire(what), null);
        else Fire(what);
    }

    private void Poll()
    {
        if (!_running || _firePosted) return;

        if (!HooksActive)
        {
            uint t = GetLastInputTick();
            if (t != _baselineTick) { Fire("keyboard/mouse (GetLastInputInfo)"); return; }
        }

        if (_useController && _xinputAvailable && (++_tickCount % 2 == 0))
            PollPads();
    }

    private void SnapshotPads()
    {
        for (uint i = 0; i < 4; i++)
        {
            try
            {
                _padConnected[i] = XInputGetState(i, out var st) == 0;
                _padBaselineButtons[i] = _padConnected[i] ? st.Gamepad.wButtons : (ushort)0;
            }
            catch (Exception) // DllNotFoundException / EntryPointNotFoundException
            {
                _xinputAvailable = false;
                return;
            }
        }
    }

    private void PollPads()
    {
        // Calling XInputGetState on empty slots is slow, so re-scan them only every ~5 s.
        bool rescan = _tickCount % 20 == 0;
        for (uint i = 0; i < 4; i++)
        {
            if (!_padConnected[i] && !rescan) continue;
            try
            {
                bool connected = XInputGetState(i, out var st) == 0;
                if (!connected) { _padConnected[i] = false; continue; }
                if (!_padConnected[i]) { _padConnected[i] = true; _padBaselineButtons[i] = st.Gamepad.wButtons; continue; }

                ushort newlyPressed = (ushort)(st.Gamepad.wButtons & ~_padBaselineButtons[i]);
                _padBaselineButtons[i] = st.Gamepad.wButtons;
                if (newlyPressed != 0) { Fire($"controller {i + 1} button"); return; }
            }
            catch (Exception)
            {
                _xinputAvailable = false;
                return;
            }
        }
    }

    public void Dispose()
    {
        Stop();
        _timer.Dispose();
    }
}
