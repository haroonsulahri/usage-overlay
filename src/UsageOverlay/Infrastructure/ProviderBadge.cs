using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using CodexUsage.Core.Providers;

namespace UsageOverlay.Infrastructure;

/// <summary>
/// App badges for the detail card. When an official logo is bundled under
/// <c>Assets/Logos</c> (see the README there) it is used; otherwise a generic badge in
/// the app's colours is drawn. Logos are trademarks of their owners.
/// </summary>
public static class ProviderBadge
{
    private static readonly System.Windows.Media.Color CodexColor = System.Windows.Media.Color.FromRgb(0x0D, 0x0D, 0x0D);
    private static readonly System.Windows.Media.Color ClaudeColor = System.Windows.Media.Color.FromRgb(0xD9, 0x77, 0x57);
    private static readonly System.Windows.Media.Color CursorColor = System.Windows.Media.Color.FromRgb(0x14, 0x12, 0x0B);

    // Terminal prompt, eight-point spark, and isometric cube, on a 10x10 grid.
    private static readonly Geometry CodexGlyph = Geometry.Parse("M 1.5,2.5 L 4.5,5 L 1.5,7.5 M 5.5,8 L 9,8");
    private static readonly Geometry ClaudeGlyph = Geometry.Parse(
        "M 5,0.5 L 5,9.5 M 0.5,5 L 9.5,5 M 1.8,1.8 L 8.2,8.2 M 8.2,1.8 L 1.8,8.2");
    private static readonly Geometry CursorGlyph = Geometry.Parse(
        "M 5,0.8 L 9,3 L 9,7.5 L 5,9.7 L 1,7.5 L 1,3 Z M 1,3 L 5,5.2 L 9,3 M 5,5.2 L 5,9.7");

    private static readonly Dictionary<UsageProviderKind, ImageSource?> Logos = new();

    public static Border Create(UsageProviderKind provider, double size)
    {
        if (LoadLogo(provider) is { } logo)
        {
            var image = new System.Windows.Controls.Image
            {
                Source = logo,
                Width = size,
                Height = size,
                Stretch = Stretch.Uniform
            };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            var logoBadge = new Border
            {
                Width = size,
                Height = size,
                CornerRadius = new CornerRadius(size * 0.28),
                VerticalAlignment = VerticalAlignment.Center,
                Child = image
            };
            System.Windows.Automation.AutomationProperties.SetName(logoBadge, UsageProviderCatalog.DisplayName(provider));
            return logoBadge;
        }

        var (color, glyph, filled) = provider switch
        {
            UsageProviderKind.Claude => (ClaudeColor, ClaudeGlyph, false),
            UsageProviderKind.Cursor => (CursorColor, CursorGlyph, false),
            _ => (CodexColor, CodexGlyph, false)
        };

        var path = new System.Windows.Shapes.Path
        {
            Data = glyph,
            Stretch = Stretch.Uniform,
            Width = size * 0.56,
            Height = size * 0.56,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Stroke = System.Windows.Media.Brushes.White,
            StrokeThickness = filled ? 0 : Math.Max(1.2, size / 11),
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Fill = filled ? System.Windows.Media.Brushes.White : null
        };

        var badge = new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(size * 0.28),
            Background = new SolidColorBrush(color),
            VerticalAlignment = VerticalAlignment.Center,
            SnapsToDevicePixels = true,
            Child = path
        };
        System.Windows.Automation.AutomationProperties.SetName(badge, UsageProviderCatalog.DisplayName(provider));
        return badge;
    }

    private static ImageSource? LoadLogo(UsageProviderKind provider)
    {
        if (Logos.TryGetValue(provider, out var cached))
        {
            return cached;
        }

        ImageSource? logo = null;
        try
        {
            var uri = new Uri(
                $"pack://application:,,,/Assets/Logos/{provider.ToString().ToLowerInvariant()}.png",
                UriKind.Absolute);
            if (System.Windows.Application.GetResourceStream(uri) is { } resource)
            {
                using var stream = resource.Stream;
                var bitmap = new System.Windows.Media.Imaging.BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bitmap.StreamSource = stream;
                bitmap.EndInit();
                bitmap.Freeze();
                logo = bitmap;
            }
        }
        catch (Exception exception) when (exception is System.IO.IOException or NotSupportedException or UriFormatException)
        {
            // No bundled logo: use the generic badge.
        }

        Logos[provider] = logo;
        return logo;
    }
}
