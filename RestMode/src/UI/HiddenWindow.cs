using System.Runtime.InteropServices;
using RestMode.Core;
using RestMode.Util;
using static RestMode.Native.NativeMethods;

namespace RestMode.UI;

/// <summary>
/// An invisible top-level window. It is never shown, but it receives the
/// system-wide messages RestMode needs: the global hotkey (WM_HOTKEY), sleep /
/// resume (WM_POWERBROADCAST), display on/off notifications and log-off /
/// shutdown (WM_QUERYENDSESSION). Message-only windows do not get these
/// broadcasts, so a real (hidden) top-level window is used.
/// </summary>
internal sealed class HiddenWindow : Form
{
    private IntPtr _displayNotify;

    public event Action<int>? HotkeyPressed;
    public event Action<int>? PowerBroadcast;
    public event Action<int>? DisplayStateChanged;
    public event Action? SessionEnding;

    public HiddenWindow()
    {
        Text = "RestMode";
        // Note: ShowInTaskbar is deliberately left at its default. Setting it to
        // false makes WinForms give the form an owner window, and Windows does
        // not reliably deliver broadcasts (WM_POWERBROADCAST) to invisible owned
        // windows. The window is never shown, so it never gets a taskbar button.
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Location = new Point(-32000, -32000);
        Size = new Size(1, 1);
    }

    /// <summary>Creates the window handle without showing anything.</summary>
    public void Init()
    {
        CreateHandle();
        try
        {
            var g = PowerGuids.ConsoleDisplayState;
            _displayNotify = RegisterPowerSettingNotification(Handle, ref g, 0 /* DEVICE_NOTIFY_WINDOW_HANDLE */);
        }
        catch (Exception ex)
        {
            Logger.Warn($"Display-state notifications unavailable: {ex.Message}");
        }
    }

    protected override void SetVisibleCore(bool value) => base.SetVisibleCore(false);

    protected override void WndProc(ref Message m)
    {
        switch (m.Msg)
        {
            case WM_HOTKEY:
                HotkeyPressed?.Invoke((int)m.WParam);
                return;

            case WM_POWERBROADCAST:
                int ev = (int)m.WParam;
                if (ev == PBT_POWERSETTINGCHANGE && m.LParam != IntPtr.Zero)
                {
                    var setting = Marshal.PtrToStructure<POWERBROADCAST_SETTING>(m.LParam);
                    if (setting.PowerSetting == PowerGuids.ConsoleDisplayState)
                        DisplayStateChanged?.Invoke(setting.Data);
                }
                else
                {
                    PowerBroadcast?.Invoke(ev);
                }
                m.Result = (IntPtr)1;
                return;

            case WM_QUERYENDSESSION:
                SessionEnding?.Invoke();
                m.Result = (IntPtr)1; // allow log-off / shutdown
                return;

            case WM_ENDSESSION:
                if (m.WParam != IntPtr.Zero) SessionEnding?.Invoke();
                break;
        }
        base.WndProc(ref m);
    }

    protected override void Dispose(bool disposing)
    {
        if (_displayNotify != IntPtr.Zero)
        {
            try { UnregisterPowerSettingNotification(_displayNotify); } catch { /* ignore */ }
            _displayNotify = IntPtr.Zero;
        }
        base.Dispose(disposing);
    }
}
