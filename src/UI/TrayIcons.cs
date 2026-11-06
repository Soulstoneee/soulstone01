using System.Drawing;
using System.IO;
using System.Windows.Media.Imaging;
using static RestMode.Native.NativeMethods;

namespace RestMode.UI;

/// <summary>
/// Tray icons drawn at runtime from RestMode's emblem: silver while awake,
/// warm amber while resting - like a console's rest light.
/// </summary>
internal static class TrayIcons
{
    public static readonly System.Windows.Media.Color AwakeColor = System.Windows.Media.Color.FromRgb(0xE4, 0xE8, 0xEE);
    public static readonly System.Windows.Media.Color RestColor = System.Windows.Media.Color.FromRgb(0xFF, 0xA9, 0x3A);

    public static Icon Create(System.Windows.Media.Color color, int size = 32)
    {
        var src = Wpf.Ornaments.Render(Wpf.Ornaments.Emblem(color, 3.4), size);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(src));
        using var ms = new MemoryStream();
        enc.Save(ms);
        ms.Position = 0;
        using var bmp = new Bitmap(ms);
        IntPtr h = bmp.GetHicon();
        try { return (Icon)Icon.FromHandle(h).Clone(); }
        finally { DestroyIcon(h); }
    }
}
