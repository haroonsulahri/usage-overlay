using System.Drawing;
using System.Windows.Forms;

namespace UsageOverlay.Infrastructure;

public sealed class OverlayMenuColorTable : ProfessionalColorTable
{
    private readonly Color _surface;
    private readonly Color _raisedSurface;
    private readonly Color _border;

    public OverlayMenuColorTable(bool isDark = true)
    {
        UseSystemColors = false;
        if (isDark)
        {
            _surface = Color.FromArgb(36, 36, 36);
            _raisedSurface = Color.FromArgb(51, 51, 51);
            _border = Color.FromArgb(59, 59, 59);
        }
        else
        {
            _surface = Color.FromArgb(255, 255, 255);
            _raisedSurface = Color.FromArgb(243, 244, 246);
            _border = Color.FromArgb(226, 232, 240);
        }
    }

    public override Color ToolStripDropDownBackground => _surface;

    public override Color ToolStripBorder => _border;

    public override Color MenuBorder => _border;

    public override Color MenuItemBorder => _raisedSurface;

    public override Color MenuItemSelected => _raisedSurface;

    public override Color MenuItemSelectedGradientBegin => _raisedSurface;

    public override Color MenuItemSelectedGradientEnd => _raisedSurface;

    public override Color MenuItemPressedGradientBegin => _raisedSurface;

    public override Color MenuItemPressedGradientMiddle => _raisedSurface;

    public override Color MenuItemPressedGradientEnd => _raisedSurface;

    public override Color ImageMarginGradientBegin => _surface;

    public override Color ImageMarginGradientMiddle => _surface;

    public override Color ImageMarginGradientEnd => _surface;

    public override Color SeparatorDark => _border;

    public override Color SeparatorLight => _border;
}
