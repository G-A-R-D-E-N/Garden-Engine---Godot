using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace OpenCommonwealth.Launcher;

internal static class Ui
{
    public static readonly IBrush Bg = new SolidColorBrush(Color.FromRgb(13, 17, 23));
    public static readonly IBrush Surface = new SolidColorBrush(Color.FromRgb(22, 27, 34));
    public static readonly IBrush SurfaceAlt = new SolidColorBrush(Color.FromRgb(33, 38, 45));
    public static readonly IBrush SurfaceHover = new SolidColorBrush(Color.FromRgb(44, 50, 58));
    public static readonly IBrush Border = new SolidColorBrush(Color.FromRgb(48, 54, 61));
    public static readonly IBrush BorderHi = new SolidColorBrush(Color.FromRgb(88, 96, 108));
    public static readonly IBrush Text = new SolidColorBrush(Color.FromRgb(230, 237, 243));
    public static readonly IBrush Muted = new SolidColorBrush(Color.FromRgb(139, 148, 158));
    public static readonly IBrush Accent = new SolidColorBrush(Color.FromRgb(35, 134, 54));
    public static readonly IBrush AccentHi = new SolidColorBrush(Color.FromRgb(46, 160, 67));

    public static readonly IBrush Green = new SolidColorBrush(Color.FromRgb(63, 185, 80));
    public static readonly IBrush Amber = new SolidColorBrush(Color.FromRgb(210, 153, 34));
    public static readonly IBrush Red = new SolidColorBrush(Color.FromRgb(248, 81, 73));
    public static readonly IBrush Blue = new SolidColorBrush(Color.FromRgb(88, 166, 255));

    public static Bitmap LoadIcon() =>
        new(AssetLoader.Open(new Uri("avares://dev/Assets/AppIcon.png")));

    public static void ApplyChrome(Window w)
    {
        w.Background = Bg;
        try { w.Icon = new WindowIcon(LoadIcon()); } catch { }
    }

    public static Button PillButton(string text, bool primary, double width = double.NaN, double height = 42)
    {
        var b = new Button
        {
            Content = text,
            Width = width,
            Height = height,
            CornerRadius = new CornerRadius(8),
            Background = primary ? Accent : SurfaceAlt,
            Foreground = primary ? Brushes.White : Text,
            BorderBrush = primary ? Accent : Border,
            BorderThickness = new Thickness(primary ? 0 : 1),
            FontWeight = primary ? FontWeight.SemiBold : FontWeight.Normal,
            FontSize = primary ? 14 : 13,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Cursor = new Cursor(StandardCursorType.Hand),
            Margin = new Thickness(0, 0, 10, 0),
        };
        b.Classes.Add(primary ? "primary" : "secondary");
        return b;
    }
}
