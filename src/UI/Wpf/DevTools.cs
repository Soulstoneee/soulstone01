using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RestMode.Core;
using RestMode.Util;

namespace RestMode.UI.Wpf;

/// <summary>
/// Developer / test helpers (only with the test-mode flag file):
///   --render-ui DIR     renders every window and settings page to PNG (works while the PC is locked)
///   --render-icon FILE  writes the multi-size app icon (.ico) from the emblem
/// </summary>
internal static class DevTools
{
    private sealed class StubApp : IAppActions
    {
        public StubApp(RestModeController c) => Controller = c;
        public Task EnterFromUi(string trigger) => Task.CompletedTask;
        public void ShowSettings() { }
        public void ShowStats() { }
        public Task RestoreEverything() => Task.CompletedTask;
        public void ExitApp() { }
        public RestModeController Controller { get; }
    }

    public static int RenderUi(string dir)
    {
        Directory.CreateDirectory(dir);
        var hidden = new HiddenWindow();
        hidden.Init();
        var app = new StubApp(new RestModeController(hidden));

        Save(new MainWindow(app), Path.Combine(dir, "main.png"));
        var settings = new SettingsWindow(app);
        foreach (var page in new[] { "General", "Rest & Lid", "Power", "Downloads", "Fans", "Safety", "Schedule & Apps", "Look", "Backup & Reset", "About" })
        {
            var mi = typeof(SettingsWindow).GetMethod("Show", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance, new[] { typeof(string) });
            mi!.Invoke(settings, new object[] { page });
            Save(settings, Path.Combine(dir, "settings-" + new string(page.Where(char.IsLetter).ToArray()) + ".png"), keepOpen: true);
        }
        settings.Close();
        Save(new StatsWindow(), Path.Combine(dir, "stats.png"));
        Save(new WizardWindow(app), Path.Combine(dir, "wizard.png"));
        Save(new SetupWindow(), Path.Combine(dir, "setup.png"));

        // The overlay, mid-animation (emblem + text fully visible)
        var overlay = new Grid { Width = 1600, Height = 900, Background = Brushes.Black };
        overlay.Children.Add(new System.Windows.Controls.Image
        {
            Source = new DrawingImage(Ornaments.Emblem(ThemeManager.Accent, 0.9)),
            Width = 230, Height = 240, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 90, 80),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = ThemeManager.Glow, BlurRadius = 36, ShadowDepth = 0, Opacity = 0.95 },
        });
        overlay.Children.Add(new TextBlock { Text = SettingsStore.Current.OverlayText, FontFamily = ThemeManager.ScriptFont, FontSize = 58, Foreground = new SolidColorBrush(ThemeManager.Text), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        SaveElement(overlay, 1600, 900, Path.Combine(dir, "overlay.png"));
        hidden.Dispose();
        return 0;
    }

    private static void Save(Window w, string file, bool keepOpen = false)
    {
        w.WindowStartupLocation = WindowStartupLocation.Manual;
        w.Left = -10000;
        w.Top = -10000;
        w.ShowActivated = false;
        w.Show();
        Flush();
        // Show the "selected" state of the first focusable button for the screenshot.
        if (w.Content is FrameworkElement root)
        {
            Flush();
            SaveElement(root, w.ActualWidth, w.ActualHeight, file);
        }
        if (!keepOpen) w.Close();
    }

    private static void Flush()
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        System.Windows.Threading.Dispatcher.PushFrame(frame);
        Thread.Sleep(150);
    }

    private static void SaveElement(FrameworkElement e, double w, double h, string file)
    {
        e.Measure(new Size(w, h));
        e.Arrange(new Rect(0, 0, w, h));
        e.UpdateLayout();
        var bmp = new RenderTargetBitmap((int)w, (int)h, 96, 96, PixelFormats.Pbgra32);
        var bg = new DrawingVisual();
        using (var dc = bg.RenderOpen()) dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, w, h));
        bmp.Render(bg);
        bmp.Render(e);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(file);
        enc.Save(fs);
    }

    /// <summary>Writes a multi-size .ico (PNG frames) of the emblem on a black rounded tile.</summary>
    public static int RenderIcon(string file)
    {
        var sizes = new[] { 16, 24, 32, 48, 64, 128, 256 };
        var pngs = new List<byte[]>();
        foreach (int size in sizes)
        {
            double stroke = size <= 24 ? 3.6 : size <= 48 ? 2.4 : 1.4;
            var emblem = Ornaments.Emblem(Color.FromRgb(0xE6, 0xEA, 0xF0), stroke);
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                double r = size * 0.18;
                dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(6, 6, 8)),
                    new Pen(new SolidColorBrush(Color.FromRgb(0x8A, 0x90, 0x99)), Math.Max(1, size / 64.0)),
                    new Rect(0.5, 0.5, size - 1, size - 1), r, r);
                var b = emblem.Bounds;
                double scale = Math.Min(size / b.Width, size / b.Height) * 0.78;
                dc.PushTransform(new TranslateTransform((size - b.Width * scale) / 2 - b.X * scale, (size - b.Height * scale) / 2 - b.Y * scale));
                dc.PushTransform(new ScaleTransform(scale, scale));
                dc.DrawDrawing(emblem);
            }
            var bmp = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(dv);
            if (size >= 256)
            {
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(bmp));
                using var ms = new MemoryStream();
                enc.Save(ms);
                pngs.Add(ms.ToArray());
            }
            else pngs.Add(Dib(bmp, size));
        }
        using var fs = File.Create(file);
        using var w = new BinaryWriter(fs);
        w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
        int offset = 6 + 16 * sizes.Length;
        for (int i = 0; i < sizes.Length; i++)
        {
            w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
            w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
            w.Write((byte)0); w.Write((byte)0);
            w.Write((short)1); w.Write((short)32);
            w.Write(pngs[i].Length);
            w.Write(offset);
            offset += pngs[i].Length;
        }
        foreach (var p in pngs) w.Write(p);
        return 0;
    }

    /// <summary>Classic icon frame: BITMAPINFOHEADER + 32-bit BGRA (bottom-up, un-premultiplied) + 1-bit AND mask.</summary>
    private static byte[] Dib(BitmapSource src, int size)
    {
        var conv = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
        var px = new byte[size * size * 4];
        conv.CopyPixels(px, size * 4, 0);
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(40); w.Write(size); w.Write(size * 2); w.Write((short)1); w.Write((short)32);
        w.Write(0); w.Write(0); w.Write(0); w.Write(0); w.Write(0); w.Write(0);
        for (int y = size - 1; y >= 0; y--) w.Write(px, y * size * 4, size * 4);
        int maskStride = ((size + 31) / 32) * 4;
        w.Write(new byte[maskStride * size]); // all 0 = use the alpha channel
        return ms.ToArray();
    }
}
