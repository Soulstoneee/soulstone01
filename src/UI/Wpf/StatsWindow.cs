using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using RestMode.Core;

namespace RestMode.UI.Wpf;

/// <summary>Statistics: time in Rest Mode, data downloaded, average temperatures, recent sessions.</summary>
internal sealed class StatsWindow : AppWindow
{
    public StatsWindow() : base("Statistics", 760, 640)
    {
        var s = StatsStore.Load().Sessions;
        var p = new StackPanel { Margin = new Thickness(40, 10, 40, 24) };
        p.Children.Add(Heading("Statistics", 24));

        double minutes = s.Sum(x => x.Minutes);
        long down = s.Sum(x => x.Downloaded);
        var cpu = s.Where(x => x.AvgCpuC != null).Select(x => x.AvgCpuC!.Value).ToList();
        var gpu = s.Where(x => x.AvgGpuC != null).Select(x => x.AvgGpuC!.Value).ToList();
        double? peak = s.Where(x => x.PeakC != null).Select(x => x.PeakC!.Value).DefaultIfEmpty().Max();

        var grid = new UniformGrid { Columns = 3, Margin = new Thickness(0, 18, 0, 6) };
        void Stat(string label, string value)
        {
            var card = new Border
            {
                Background = ThemeManager.Brush("PanelBrush"),
                BorderBrush = ThemeManager.Brush("BorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(16, 12, 16, 12),
                Margin = new Thickness(0, 0, 10, 10),
            };
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = value, Style = ThemeManager.Style("Title"), FontSize = 24 });
            sp.Children.Add(Secondary(label));
            card.Child = sp;
            grid.Children.Add(card);
        }
        Stat("sessions", s.Count.ToString());
        Stat("time in Rest Mode", minutes >= 60 ? $"{minutes / 60:0.#} h" : $"{minutes:0} min");
        Stat("downloaded", ActivityMonitor.FormatBytes(down));
        Stat("average CPU", cpu.Count > 0 ? $"{cpu.Average():0} °C" : "–");
        Stat("average GPU", gpu.Count > 0 ? $"{gpu.Average():0} °C" : "–");
        Stat("highest seen", peak is > 0 ? $"{peak:0} °C" : "–");
        p.Children.Add(grid);

        p.Children.Add(Section("Recent sessions"));
        if (s.Count == 0) p.Children.Add(Secondary("None yet."));
        foreach (var x in s.AsEnumerable().Reverse().Take(12))
            p.Children.Add(new Border
            {
                BorderBrush = ThemeManager.Brush("BorderBrush"),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(0, 8, 0, 8),
                Child = Text($"{x.StartUtc.ToLocalTime():ddd d MMM, HH:mm}   ·   {(x.Minutes >= 60 ? $"{x.Minutes / 60:0.#} h" : $"{x.Minutes:0} min")}   ·   " +
                             $"{ActivityMonitor.FormatBytes(x.Downloaded)}{(x.AvgCpuC is double c ? $"   ·   {c:0} °C" : "")}   ·   {x.End}"),
            });
        var clear = Flat("Clear statistics", (_, _) =>
        {
            if (System.Windows.MessageBox.Show(this, "Delete all statistics?", "RestMode", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            {
                StatsStore.Clear();
                Close();
            }
        });
        clear.Margin = new Thickness(0, 16, 0, 0);
        p.Children.Add(clear);
        Body.Content = new ScrollViewer { Content = p, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };
    }
}
