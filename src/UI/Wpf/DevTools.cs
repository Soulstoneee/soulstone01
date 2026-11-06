using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RestMode.Core;

namespace RestMode.UI.Wpf;

/// <summary>
/// Developer / test helpers (only with the test-mode flag file):
///   --render-ui DIR     renders every window and settings page to PNG (works while the PC is locked)
///   --render-icon FILE  writes the multi-size app icon (.ico)
/// </summary>
internal static class DevTools
{
    private sealed class StubApp : IAppActions
    {
        public StubApp(RestModeController c) => Controller = c;
        public Task EnterFromUi(string trigger) => Task.CompletedTask;
        public void ShowSettings(string? page = null) { }
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
        _ = app.Controller.Device;


        var settings = new SettingsWindow(app);
        var show = typeof(SettingsWindow).GetMethod("Show", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance, new[] { typeof(string) })!;
        foreach (var page in new[] { "General", "Rest & lid", "Power", "Downloads", "Fans", "Safety", "Schedule & apps", "Look", "Backup & reset", "About" })
        {
            show.Invoke(settings, new object[] { page });
            Save(settings, Path.Combine(dir, "settings-" + new string(page.Where(char.IsLetter).ToArray()) + ".png"), keepOpen: true);
        }
        settings.Close();
        Save(new StatsWindow(), Path.Combine(dir, "stats.png"));
        Save(new WizardWindow(app), Path.Combine(dir, "wizard.png"));
        Save(new SetupWindow(), Path.Combine(dir, "setup.png"));

        var overlay = new Grid { Width = 1600, Height = 900, Background = Brushes.Black };
        overlay.Children.Add(new TextBlock
        {
            Text = SettingsStore.Current.OverlayText, FontFamily = ThemeManager.DisplayFont, FontSize = 30, FontWeight = FontWeights.Light,
            Foreground = ThemeManager.Brush("TextBrush"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        });
        SaveElement(overlay, 1600, 900, Path.Combine(dir, "overlay.png"));
        Save(new MainWindow(app), Path.Combine(dir, "main.png"));
        hidden.Dispose();
        return 0;
    }

    private static void Save(Window w, string file, bool keepOpen = false)
    {
        Util.Logger.Info($"[render] {Path.GetFileName(file)}");
        w.WindowStartupLocation = WindowStartupLocation.Manual;
        w.Left = -10000;
        w.Top = -10000;
        w.ShowActivated = false;
        w.Show();
        Flush();
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
        using (var dc = bg.RenderOpen()) dc.DrawRectangle(ThemeManager.Brush("BackgroundBrush"), null, new Rect(0, 0, w, h));
        bmp.Render(bg);
        bmp.Render(e);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(file);
        enc.Save(fs);
    }

    /// <summary>Writes a multi-size .ico: a white line moon on a near-black rounded tile.</summary>
    public static int RenderIcon(string file)
    {
        var sizes = new[] { 16, 24, 32, 48, 64, 128, 256 };
        var frames = new List<byte[]>();
        foreach (int size in sizes)
        {
            var bmp = Icons.RenderMoon(size, tile: true, filled: false);
            if (size >= 256)
            {
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(bmp));
                using var ms = new MemoryStream();
                enc.Save(ms);
                frames.Add(ms.ToArray());
            }
            else frames.Add(Dib(bmp, size));
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
            w.Write(frames[i].Length);
            w.Write(offset);
            offset += frames[i].Length;
        }
        foreach (var f in frames) w.Write(f);
        return 0;
    }

    /// <summary>Classic icon frame: BITMAPINFOHEADER + 32-bit BGRA (bottom-up) + 1-bit AND mask.</summary>
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
        w.Write(new byte[maskStride * size]);
        return ms.ToArray();
    }
}
