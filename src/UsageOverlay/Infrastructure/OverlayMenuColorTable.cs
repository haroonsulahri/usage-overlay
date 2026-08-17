using System.Drawing;
using System.Windows.Forms;

namespace UsageOverlay.Infrastructure;

public sealed class OverlayMenuColorTable : ProfessionalColorTable
{
    private static readonly Color Surface = Color.FromArgb(36, 36, 36);
    private static readonly Color RaisedSurface = Color.FromArgb(51, 51, 51);
    private static readonly Color Border = Color.FromArgb(59, 59, 59);

    public OverlayMenuColorTable()
    {
        UseSystemColors = false;
    }

    public override Color ToolStripDropDownBackground => Surface;

    public override Color ToolStripBorder => Border;

    public override Color MenuBorder => Border;

    public override Color MenuItemBorder => RaisedSurface;

    public override Color MenuItemSelected => RaisedSurface;

    public override Color MenuItemSelectedGradientBegin => RaisedSurface;

    public override Color MenuItemSelectedGradientEnd => RaisedSurface;

    public override Color MenuItemPressedGradientBegin => RaisedSurface;

    public override Color MenuItemPressedGradientMiddle => RaisedSurface;

    public override Color MenuItemPressedGradientEnd => RaisedSurface;

    public override Color ImageMarginGradientBegin => Surface;

    public override Color ImageMarginGradientMiddle => Surface;

    public override Color ImageMarginGradientEnd => Surface;

    public override Color SeparatorDark => Border;

    public override Color SeparatorLight => Border;
}
