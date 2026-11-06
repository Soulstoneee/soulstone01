using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using RestMode.Core;

namespace RestMode.UI.Wpf;

/// <summary>Statistics: time in Rest Mode, data downloaded, average temperatures, recent sessions.</summary>
internal sealed class StatsWindow : OrnateWindow
{
    public StatsWindow() : base("RestMode · Statistics", 900, 760)
    {
        var data = StatsStore.Load();
        var s = data.Sessions;
        var p = new StackPanel { Margin = new Thickness(50, 0, 50, 20) };
        p.Children.Add(Heading("Statistics", 52));

        double minutes = s.Sum(x => x.Minutes);
        long down = s.Sum(x => x.Downloaded);
        var cpu = s.Where(x => x.AvgCpuC != null).Select(x => x.AvgCpuC!.Value).ToList();
        var gpu = s.Where(x => x.AvgGpuC != null).Select(x => x.AvgGpuC!.Value).ToList();
        double? peak = s.Where(x => x.PeakC != null).Select(x => x.PeakC!.Value).DefaultIfEmpty().Max();

        var grid = new UniformGrid { Columns = 2, Margin = new Thickness(0, 10, 0, 10) };
        void Stat(string label, string value)
        {
            var sp = new StackPanel { Margin = new Thickness(0, 8, 20, 8) };
            var tb = new TextBlock { Text = value, FontFamily = ThemeManager.SerifFont, FontSize = 40, Foreground = (Brush)FindResource("AccentBrush") };
            System.Windows.Documents.Typography.SetNumeralStyle(tb, FontNumeralStyle.Lining);
            sp.Children.Add(tb);
            sp.Children.Add(Hint(label));
            grid.Children.Add(sp);
        }
        Stat("Rest Mode sessions", s.Count.ToString());
        Stat("time in Rest Mode", minutes >= 60 ? $"{minutes / 60:0.#} h" : $"{minutes:0} min");
        Stat("downloaded while resting", ActivityMonitor.FormatBytes(down));
        Stat("average CPU temperature", cpu.Count > 0 ? $"{cpu.Average():0} °C" : "–");
        Stat("average GPU temperature", gpu.Count > 0 ? $"{gpu.Average():0} °C" : "–");
        Stat("highest temperature seen", peak is > 0 ? $"{peak:0} °C" : "–");
        p.Children.Add(grid);

        p.Children.Add(SubHeading("Recent sessions"));
        if (s.Count == 0) p.Children.Add(Hint("None yet. Press Rest to start your first one."));
        foreach (var x in s.AsEnumerable().Reverse().Take(12))
            p.Children.Add(Label($"{x.StartUtc.ToLocalTime():ddd d MMM, HH:mm}  ·  {(x.Minutes >= 60 ? $"{x.Minutes / 60:0.#} h" : $"{x.Minutes:0} min")}  ·  {ActivityMonitor.FormatBytes(x.Downloaded)}" +
                                 $"{(x.AvgCpuC is double c ? $"  ·  {c:0} °C" : "")}  ·  {x.End}"));
        var row = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
        row.Children.Add(Small("Clear statistics", (_, _) =>
        {
            if (System.Windows.MessageBox.Show(this, "Delete all statistics?", "RestMode", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            {
                StatsStore.Clear();
                Close();
            }
        }));
        p.Children.Add(row);
        Body.Content = new ScrollViewer { Content = p, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };
    }
}
