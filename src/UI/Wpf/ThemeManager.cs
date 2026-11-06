using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;
using RestMode.Core;
using RestMode.Util;

namespace RestMode.UI.Wpf;

/// <summary>
/// Loads Theme.xaml (all colors / fonts / styles) into the WPF application and
/// applies the user's look settings that still exist in the minimal design:
/// animation speed and sounds.
/// </summary>
internal static class ThemeManager
{
    /// <summary>Multiplier for animation durations (1 = normal; higher setting = faster).</summary>
    public static double Speed { get; private set; } = 1;
    public static bool SoundsOn { get; private set; } = true;

    public static void Init()
    {
        var app = System.Windows.Application.Current;
        try
        {
            using var stream = typeof(ThemeManager).Assembly.GetManifestResourceStream("RestMode.Theme.xaml")
                               ?? throw new InvalidOperationException("Theme.xaml missing");
            var dict = (ResourceDictionary)XamlReader.Load(stream);
            app.Resources.MergedDictionaries.Add(dict);
        }
        catch (Exception ex)
        {
            Logger.Error("Loading the theme failed", ex);
        }
        Apply(SettingsStore.Current);
    }

    public static void Apply(AppSettings s)
    {
        Speed = s.AnimationSpeed;
        SoundsOn = s.Sounds;
        Changed?.Invoke();
    }

    /// <summary>Raised after the look settings changed.</summary>
    public static event Action? Changed;

    public static TimeSpan Dur(double seconds) => TimeSpan.FromSeconds(seconds / Math.Max(0.25, Speed));

    public static Style? Style(string key) => System.Windows.Application.Current.TryFindResource(key) as Style;

    public static Brush Brush(string key) => (Brush)System.Windows.Application.Current.FindResource(key);

    public static FontFamily Font => (FontFamily)System.Windows.Application.Current.FindResource("UiFont");
    public static FontFamily DisplayFont => (FontFamily)System.Windows.Application.Current.FindResource("UiFontDisplay");
}
