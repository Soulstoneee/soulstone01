using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;
using RestMode.Core;
using RestMode.Util;

namespace RestMode.UI.Wpf;

/// <summary>
/// Loads Theme.xaml (all colors / styles) and the bundled fonts into the WPF
/// application, and applies the user's look settings (accent color, ornament
/// brightness, animation speed, sounds).
/// </summary>
internal static class ThemeManager
{
    public static FontFamily ScriptFont { get; private set; } = new("Segoe Script");
    public static FontFamily ScriptAltFont { get; private set; } = new("Segoe Script");
    public static FontFamily SerifFont { get; private set; } = new("Georgia");
    /// <summary>Multiplier for animation durations (1 = normal; higher setting = faster).</summary>
    public static double Speed { get; private set; } = 1;
    public static bool SoundsOn { get; private set; } = true;
    public static double OrnamentOpacity { get; private set; } = 0.55;

    public static Color Accent => (Color)System.Windows.Application.Current.Resources["AccentColor"];
    public static Color Glow => (Color)System.Windows.Application.Current.Resources["GlowColor"];
    public static Color Text => (Color)System.Windows.Application.Current.Resources["TextColor"];
    public static Color Silver => (Color)System.Windows.Application.Current.Resources["SilverColor"];
    public static Color SilverDim => (Color)System.Windows.Application.Current.Resources["SilverDimColor"];

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

        try
        {
            var baseUri = new Uri("pack://application:,,,/RestMode;component/Assets/Fonts/");
            ScriptFont = new FontFamily(baseUri, "./#Pinyon Script");
            ScriptAltFont = new FontFamily(baseUri, "./#Great Vibes");
            SerifFont = new FontFamily(baseUri, "./#Cormorant Garamond");
        }
        catch (Exception ex) { Logger.Error("Loading the fonts failed (system fonts used)", ex); }
        app.Resources["ScriptFont"] = ScriptFont;
        app.Resources["ScriptAltFont"] = ScriptAltFont;
        app.Resources["SerifFont"] = SerifFont;
        Apply(SettingsStore.Current);
    }

    public static void Apply(AppSettings s)
    {
        var res = System.Windows.Application.Current.Resources;
        Color accent = s.Accent switch
        {
            AccentColor.Gold => Color.FromRgb(0xE6, 0xC6, 0x7E),
            AccentColor.Blue => Color.FromRgb(0xA9, 0xCC, 0xFF),
            _ => Color.FromRgb(0xDD, 0xE2, 0xEA),
        };
        Color glow = s.Accent switch
        {
            AccentColor.Gold => Color.FromRgb(0xFF, 0xE7, 0xB0),
            AccentColor.Blue => Color.FromRgb(0xD6, 0xE8, 0xFF),
            _ => Color.FromRgb(0xF4, 0xF6, 0xFA),
        };
        res["AccentColor"] = accent;
        res["GlowColor"] = glow;
        res["AccentBrush"] = Frozen(new SolidColorBrush(accent));
        res["GlowBrushSolid"] = Frozen(new SolidColorBrush(glow));
        OrnamentOpacity = s.OrnamentBrightness / 100.0;
        res["OrnamentOpacity"] = OrnamentOpacity;
        Speed = s.AnimationSpeed;
        SoundsOn = s.Sounds;
        Changed?.Invoke();
    }

    /// <summary>Raised after the look settings changed (windows refresh their ornaments).</summary>
    public static event Action? Changed;

    public static TimeSpan Dur(double seconds) => TimeSpan.FromSeconds(seconds / Math.Max(0.25, Speed));

    public static T Frozen<T>(T f) where T : Freezable
    {
        if (f.CanFreeze) f.Freeze();
        return f;
    }

    public static Style? Style(string key) => System.Windows.Application.Current.TryFindResource(key) as Style;
}
