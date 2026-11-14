using Color = SharpDX.Color;

namespace AutoAugury;

/// <summary>
/// System.Drawing.Color's constructors for SharpDX.Color, which is the colour ExileApi draws and saves with, with its
/// argument order (alpha first). The same helper as CraftUntilHighlighted's and AutoExpedition's PoE1 port, so colours
/// copied from them read the same.
/// </summary>
internal static class Colours
{
    public static Color FromArgb(int alpha, int red, int green, int blue) =>
        new((byte)red, (byte)green, (byte)blue, (byte)alpha);

    /// <summary>The same colour with a different alpha.</summary>
    public static Color FromArgb(int alpha, Color colour) => new(colour.R, colour.G, colour.B, (byte)alpha);
}
