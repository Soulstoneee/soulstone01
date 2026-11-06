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

        // Rest animation and welcome screen as a 1920 x 1080 screen at 100 / 125 / 150 % scaling.
        foreach (var (scale, dpi) in new[] { (100, 96.0), (125, 120.0), (150, 144.0) })
        {
            double w = 1920 * 96 / dpi, h = 1080 * 96 / dpi;
            var s = SettingsStore.Current;
            // 0.6 s: top edge, 0.8 s: passing the top-right corner, 3.0 s: second lap with the text, 5.8 s: fading out.
            foreach (var (name, t, radius) in new[] { ("top", 0.6, 0), ("corner", 0.8, 0), ("lap2", 3.0, 0), ("fade", 5.8, 0), ("rounded", 0.8, 12) })
            {
                if (radius > 0 && scale != 125) continue;
                var scene = new EdgeLightScene(new Size(w, h), RestOverlay.FrameColor(s), s.OverlayText, radius, s.LapSeconds, s.AnimationSeconds);
                scene.DrawAt(t);
                var host = new Grid { Background = Brushes.Black, Width = w, Height = h };
                host.Children.Add(scene.Root);
                SaveElementAtDpi(host, w, h, dpi, Path.Combine(dir, $"rest-{name}-{scale}.png"));
            }
            var welcome = RestOverlay.BuildWelcome(RestOverlay.FrameColor(SettingsStore.Current), RestOverlay.WelcomeLine(TimeSpan.FromMinutes(134), 3_435_973_837), out var line);
            line.ScaleX = 1;
            var wh = new Grid { Background = Brushes.Black, Width = w, Height = h };
            wh.Children.Add(welcome);
            SaveElementAtDpi(wh, w, h, dpi, Path.Combine(dir, $"welcome-{scale}.png"));
        }
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

    /// <summary>Renders at a given DPI (96 = 100 %, 120 = 125 %, 144 = 150 %), like a real screen with that scaling.</summary>
    private static void SaveElementAtDpi(FrameworkElement e, double w, double h, double dpi, string file)
    {
        e.Measure(new Size(w, h));
        e.Arrange(new Rect(0, 0, w, h));
        e.UpdateLayout();
        var bmp = new RenderTargetBitmap((int)Math.Round(w * dpi / 96), (int)Math.Round(h * dpi / 96), dpi, dpi, PixelFormats.Pbgra32);
        bmp.Render(e);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(file);
        enc.Save(fs);
    }

    /// <summary>
    /// Live preview on the real screens with a frame-rate measurement (test mode only):
    /// the Rest animation, then the welcome screen. Logs average fps and the longest frame.
    /// </summary>
    public static async Task PreviewAsync()
    {
        var times = new List<double>();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        double last = 0;
        EventHandler tick = (_, _) => { double now = sw.Elapsed.TotalMilliseconds; if (last > 0) times.Add(now - last); last = now; };
        CompositionTarget.Rendering += tick;
        await RestOverlay.ShowEnterAsync(SettingsStore.Current);
        CompositionTarget.Rendering -= tick;
        var enter = times.ToList();
        times.Clear(); last = 0;
        await Task.Delay(600);
        CompositionTarget.Rendering += tick;
        RestOverlay.ShowWelcome(SettingsStore.Current, TimeSpan.FromMinutes(134), 3_435_973_837);
        await Task.Delay(2600);
        CompositionTarget.Rendering -= tick;
        RestOverlay.CloseAll();
        string Stats(List<double> t) => t.Count == 0 ? "no frames" :
            $"{t.Count} frames, avg {1000 / t.Average():0} fps, slowest frame {t.Max():0} ms, frames over 25 ms: {t.Count(x => x > 25)}";
        var ps = System.Windows.Forms.SystemInformation.PowerStatus;
        Util.Logger.Info($"[preview] power={ps.PowerLineStatus}, render tier={RenderCapability.Tier >> 16}, draw time per frame avg {EdgeLightScene.DrawMsAvg:0.00} ms max {EdgeLightScene.DrawMsMax:0.0} ms; Rest animation: {Stats(enter)}; welcome: {Stats(times)}");
    }
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
