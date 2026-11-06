using System.Drawing;
using System.IO;
using System.Windows.Media.Imaging;
using static RestMode.Native.NativeMethods;

namespace RestMode.UI;

/// <summary>Tray icons: a white line moon while awake, a filled white moon while resting.</summary>
internal static class TrayIcons
{
    public static Icon Create(bool resting, int size = 32)
    {
        var src = Wpf.Icons.RenderMoon(size, tile: false, filled: resting);
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
