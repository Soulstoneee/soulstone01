using RestMode.Util;
using static RestMode.Native.NativeMethods;

namespace RestMode.Core;

/// <summary>Turns the monitor(s) off and back on.</summary>
internal static class DisplayController
{
    /// <summary>
    /// Classic (S3) PCs: SendMessage(HWND_BROADCAST, WM_SYSCOMMAND, SC_MONITORPOWER, 2)
    /// turns every monitor off at once while the PC keeps running.
    /// SendMessageTimeout + SMTO_ABORTIFHUNG is used so one frozen window cannot
    /// block RestMode. If the broadcast fails we send it to our own window
    /// (DefWindowProc handles SC_MONITORPOWER system-wide).
    ///
    /// Modern Standby (S0ix) PCs: SC_MONITORPOWER is NOT sent. Windows treats it
    /// like pressing the power button and enters Modern Standby, which freezes
    /// desktop apps and (on most PCs) disconnects the network - the opposite of
    /// Rest Mode. Instead the temporary power plan's short "turn off display
    /// after" timer switches every monitor off a few seconds after the last
    /// input, which leaves the PC fully running.
    /// </summary>
    public static void TurnOff(IntPtr ownWindow, bool modernStandby)
    {
        if (modernStandby)
        {
            Logger.Info($"Modern Standby PC: the screen turns off by the idle timer in ~{PowerPlanManager.ModernStandbyScreenOffSeconds} s " +
                        "(\"monitor off\" would make Windows enter standby and cut the network).");
            return;
        }
        try
        {
            var r = SendMessageTimeout(HWND_BROADCAST, WM_SYSCOMMAND, (IntPtr)SC_MONITORPOWER, (IntPtr)2,
                SMTO_ABORTIFHUNG, 2000, out _);
            if (r == IntPtr.Zero && ownWindow != IntPtr.Zero)
                SendMessage(ownWindow, WM_SYSCOMMAND, (IntPtr)SC_MONITORPOWER, (IntPtr)2);
            Logger.Info("Display turned off.");
        }
        catch (Exception ex)
        {
            Logger.Error("Turning the display off failed", ex);
        }
    }

    /// <summary>
    /// Wakes the display. Keyboard/mouse input already does this by itself; this
    /// is needed when we wake for another reason (controller button, tray menu).
    /// Uses three methods because each one is ignored by some drivers.
    /// </summary>
    public static void TurnOn(IntPtr ownWindow)
    {
        try
        {
            SetThreadExecutionState(EXECUTION_STATE.ES_DISPLAY_REQUIRED);
            if (ownWindow != IntPtr.Zero)
                SendMessage(ownWindow, WM_SYSCOMMAND, (IntPtr)SC_MONITORPOWER, (IntPtr)(-1));
            // A 1-pixel there-and-back mouse nudge is the most reliable "wake screen" signal.
            var inputs = new[]
            {
                new INPUT { type = 0, mi = new MOUSEINPUT { dx = 1, dy = 0, dwFlags = MOUSEEVENTF_MOVE } },
                new INPUT { type = 0, mi = new MOUSEINPUT { dx = -1, dy = 0, dwFlags = MOUSEEVENTF_MOVE } },
            };
            SendInput((uint)inputs.Length, inputs, System.Runtime.InteropServices.Marshal.SizeOf<INPUT>());
        }
        catch (Exception ex)
        {
            Logger.Error("Turning the display on failed", ex);
        }
    }
}
