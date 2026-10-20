using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using CodexUsage.Core.Providers;

namespace UsageOverlay.Infrastructure;

/// <summary>
/// Small generic app badges drawn in each app's colour. They are deliberately not the
/// official logos, which are trademarks of their owners.
/// </summary>
public static class ProviderBadge
{
    private static readonly System.Windows.Media.Color CodexColor = System.Windows.Media.Color.FromRgb(0x10, 0xA3, 0x7F);
    private static readonly System.Windows.Media.Color ClaudeColor = System.Windows.Media.Color.FromRgb(0xD9, 0x77, 0x57);
    private static readonly System.Windows.Media.Color CursorColor = System.Windows.Media.Color.FromRgb(0x5B, 0x6B, 0xF0);

    // Terminal prompt, eight-point spark, and pointer arrow, on a 10x10 grid.
    private static readonly Geometry CodexGlyph = Geometry.Parse("M 1.5,2.5 L 4.5,5 L 1.5,7.5 M 5.5,8 L 9,8");
    private static readonly Geometry ClaudeGlyph = Geometry.Parse(
        "M 5,0.5 L 5,9.5 M 0.5,5 L 9.5,5 M 1.8,1.8 L 8.2,8.2 M 8.2,1.8 L 1.8,8.2");
    private static readonly Geometry CursorGlyph = Geometry.Parse("M 2,1 L 2,9 L 4.2,7 L 5.8,10 L 7.2,9.3 L 5.7,6.4 L 8.6,6.2 Z");

    public static Border Create(UsageProviderKind provider, double size)
    {
        var (color, glyph, filled) = provider switch
        {
            UsageProviderKind.Claude => (ClaudeColor, ClaudeGlyph, false),
            UsageProviderKind.Cursor => (CursorColor, CursorGlyph, true),
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
}
