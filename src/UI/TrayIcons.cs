using System.Drawing.Drawing2D;
using static RestMode.Native.NativeMethods;

namespace RestMode.UI;

/// <summary>
/// Draws the tray icons at runtime (no image files needed): a power symbol,
/// blue while awake and orange while resting - like the PS4's rest-mode light.
/// </summary>
internal static class TrayIcons
{
    public static readonly Color AwakeColor = Color.FromArgb(0x2D, 0x7F, 0xF9);
    public static readonly Color RestColor = Color.FromArgb(0xFF, 0x8C, 0x1A);

    public static Icon Create(Color color, int size = 32)
    {
        using var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            float w = size / 8f;
            using var pen = new Pen(color, w) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            float pad = w + size * 0.06f;
            var rect = new RectangleF(pad, pad + size * 0.04f, size - 2 * pad, size - 2 * pad);
            g.DrawArc(pen, rect, -55, 290);             // ring with a gap at the top
            g.DrawLine(pen, size / 2f, w * 0.8f, size / 2f, size * 0.45f); // the "I" of the power symbol
        }
        IntPtr h = bmp.GetHicon();
        try { return (Icon)Icon.FromHandle(h).Clone(); }
        finally { DestroyIcon(h); }
    }
}
