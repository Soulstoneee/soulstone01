using System.Windows.Forms;
using static RestMode.Native.NativeMethods;

namespace RestMode.UI;

/// <summary>Converts between "Ctrl+Alt+Shift+R" text and RegisterHotKey modifier/key values.</summary>
internal static class HotkeyParser
{
    public static bool TryParse(string? text, out uint modifiers, out Keys key)
    {
        modifiers = 0;
        key = Keys.None;
        if (string.IsNullOrWhiteSpace(text)) return false;

        foreach (var raw in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl": case "control": modifiers |= MOD_CONTROL; break;
                case "alt": modifiers |= MOD_ALT; break;
                case "shift": modifiers |= MOD_SHIFT; break;
                case "win": case "windows": modifiers |= MOD_WIN; break;
                default:
                    if (key != Keys.None) return false; // two non-modifier keys
                    if (!Enum.TryParse(raw, ignoreCase: true, out key)) return false;
                    break;
            }
        }
        // A hotkey without modifiers would block that key everywhere; F-keys are the exception.
        bool isFunctionKey = key is >= Keys.F1 and <= Keys.F24 or Keys.Pause or Keys.Scroll;
        return key != Keys.None && (modifiers != 0 || isFunctionKey);
    }

    /// <summary>Formats a KeyDown event's KeyData, e.g. "Ctrl+Alt+R". Returns null for modifier-only presses.</summary>
    public static string? Format(Keys keyData)
    {
        Keys key = keyData & Keys.KeyCode;
        if (key is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin or Keys.None) return null;
        var parts = new List<string>();
        if (keyData.HasFlag(Keys.Control)) parts.Add("Ctrl");
        if (keyData.HasFlag(Keys.Alt)) parts.Add("Alt");
        if (keyData.HasFlag(Keys.Shift)) parts.Add("Shift");
        parts.Add(key.ToString());
        return string.Join("+", parts);
    }
}
