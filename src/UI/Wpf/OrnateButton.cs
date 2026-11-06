using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using Path = System.Windows.Shapes.Path;

namespace RestMode.UI.Wpf;

/// <summary>
/// The signature button: a rectangle with a thin silver border and ornate
/// corners, a cursive label, and long calligraphy flourishes looping off both
/// sides. When hovered or selected (keyboard / controller focus) the flourishes
/// draw themselves out and glow soft silver-white, and a small key cursor
/// appears beside the button. It is a real Button: Enter / Space / A click it.
/// </summary>
internal sealed class OrnateButton : System.Windows.Controls.Button
{
    private static ControlTemplate? _bare;
    private readonly Path _left, _right, _key;
    private readonly Border _box;
    private readonly TextBlock _label;
    private readonly double _len;
    private readonly DropShadowEffect _glow;
    private bool _lit;

    public OrnateButton(string text, double fontSize = 34, double flourish = 150, bool big = false)
    {
        Template = _bare ??= (ControlTemplate)XamlReader.Parse(
            "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Button'>" +
            "<ContentPresenter /></ControlTemplate>");
        FocusVisualStyle = null;
        Cursor = System.Windows.Input.Cursors.Hand;
        Background = Brushes.Transparent;
        HorizontalAlignment = HorizontalAlignment.Center;
        Margin = new Thickness(0, big ? 10 : 8, 0, big ? 10 : 8);

        double flourishH = fontSize * (big ? 0.9 : 0.75);
        var geo = Ornaments.Flourish(flourish, flourishH);
        _len = Ornaments.Length(geo) + 2;
        var dash = new DoubleCollection { _len, _len };

        _glow = new DropShadowEffect { Color = ThemeManager.Glow, BlurRadius = big ? 18 : 12, ShadowDepth = 0, Opacity = 0 };

        Path MakeFlourish(bool mirror) => new()
        {
            Data = geo,
            Stroke = (Brush)FindRes("AccentBrush"),
            StrokeThickness = big ? 1.7 : 1.3,
            StrokeDashArray = dash,
            StrokeDashOffset = _len * 0.82,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Opacity = 0.55,
            VerticalAlignment = VerticalAlignment.Center,
            Width = flourish + 4,
            Height = flourishH * 2 + 4,
            Stretch = Stretch.None,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = mirror ? new ScaleTransform(-1, 1) : Transform.Identity,
            IsHitTestVisible = false,
        };
        _left = MakeFlourish(true);
        _right = MakeFlourish(false);

        _label = new TextBlock
        {
            Text = text,
            FontFamily = ThemeManager.ScriptFont,
            FontSize = fontSize,
            Foreground = (Brush)FindRes("TextBrush"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(big ? 46 : 26, big ? 2 : 0, big ? 46 : 26, big ? 10 : 6),
        };
        _box = new Border
        {
            BorderBrush = (Brush)FindRes("SilverLineBrush"),
            BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)),
            Child = _label,
            MinWidth = big ? 320 : 200,
        };
        var boxHost = new Grid();
        boxHost.Children.Add(_box);
        if (big)
        {
            boxHost.Children.Add(new Border
            {
                BorderBrush = (Brush)FindRes("SilverDimBrush"),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(-7),
                IsHitTestVisible = false,
            });
        }
        double cs = big ? 22 : 13;
        foreach (var (h, v, sx, sy) in new[] { (HorizontalAlignment.Left, VerticalAlignment.Top, 1, 1), (HorizontalAlignment.Right, VerticalAlignment.Top, -1, 1),
                                               (HorizontalAlignment.Left, VerticalAlignment.Bottom, 1, -1), (HorizontalAlignment.Right, VerticalAlignment.Bottom, -1, -1) })
        {
            boxHost.Children.Add(new Path
            {
                Data = Ornaments.Corner(cs),
                Stroke = (Brush)FindRes("AccentBrush"),
                StrokeThickness = 1,
                HorizontalAlignment = h,
                VerticalAlignment = v,
                Margin = new Thickness(big ? -12 : -6),
                Width = cs, Height = cs,
                Stretch = Stretch.None,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new ScaleTransform(sx, sy),
                IsHitTestVisible = false,
            });
        }

        _key = new Path
        {
            Data = Ornaments.KeyCursor(),
            Stroke = (Brush)FindRes("AccentBrush"),
            StrokeThickness = 1.6,
            Width = 40, Height = 22,
            Stretch = Stretch.None,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 0, 6, 0),
            Opacity = 0,
            IsHitTestVisible = false,
            RenderTransform = new TranslateTransform(-10, 0),
        };

        var grid = new Grid { Background = Brushes.Transparent };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(_left, 0);
        Grid.SetColumn(boxHost, 1);
        Grid.SetColumn(_right, 2);
        Grid.SetColumn(_key, 0);
        grid.Children.Add(_left);
        grid.Children.Add(_key);
        grid.Children.Add(boxHost);
        grid.Children.Add(_right);
        boxHost.Effect = _glow;
        Content = grid;

        MouseEnter += (_, _) => Light(true);
        MouseLeave += (_, _) => Light(IsKeyboardFocused);
        GotKeyboardFocus += (_, _) => Light(true);
        LostKeyboardFocus += (_, _) => Light(IsMouseOver);
        IsEnabledChanged += (_, _) => Opacity = IsEnabled ? 1 : 0.4;
        ThemeManager.Changed += OnThemeChanged;
        Unloaded += (_, _) => ThemeManager.Changed -= OnThemeChanged;
    }

    public string Text
    {
        get => _label.Text;
        set => _label.Text = value;
    }

    private void OnThemeChanged()
    {
        _glow.Color = ThemeManager.Glow;
        var accent = (Brush)FindRes("AccentBrush");
        _left.Stroke = _right.Stroke = _key.Stroke = accent;
    }

    private static object FindRes(string key) => System.Windows.Application.Current.FindResource(key);

    private void Light(bool on)
    {
        if (on == _lit) return;
        _lit = on;
        var d = ThemeManager.Dur(on ? 0.65 : 0.35);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        foreach (var p in new[] { _left, _right })
        {
            p.BeginAnimation(Shape.StrokeDashOffsetProperty, new DoubleAnimation(on ? 0 : _len * 0.82, d) { EasingFunction = ease });
            p.BeginAnimation(OpacityProperty, new DoubleAnimation(on ? 1 : 0.55, d));
            p.Effect = on ? new DropShadowEffect { Color = ThemeManager.Glow, BlurRadius = 8, ShadowDepth = 0, Opacity = 0.9 } : null;
        }
        _glow.BeginAnimation(DropShadowEffect.OpacityProperty, new DoubleAnimation(on ? 0.85 : 0, d));
        _key.BeginAnimation(OpacityProperty, new DoubleAnimation(on ? 1 : 0, d));
        ((TranslateTransform)_key.RenderTransform).BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(on ? 0 : -10, d) { EasingFunction = ease });
        _label.Foreground = on ? new SolidColorBrush(ThemeManager.Glow) : (Brush)FindRes("TextBrush");
        if (on) Sounds.Tick();
    }
}
