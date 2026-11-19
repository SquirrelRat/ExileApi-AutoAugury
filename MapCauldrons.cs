namespace AutoAugury;

/// <summary>
/// How many Cauldrons an area holds: the game's Fate counter while the Fates panel shows it (FateCounter.InArea, since
/// patch 3.30.0.4), else a fixed count by area level: 3 in white maps, 3.5 in yellow, 4 in red (the user, 2026-11-19).
/// FateScoring uses it to count the offers still to come (this less the Fates collected less the Cauldron being chosen
/// at), which sets how much a Fate's chance of being paired later is worth, and for "Estimated: 1/3" when the counter
/// is not read. A fraction is an expectation: 3.5 is 3 or 4 Cauldrons, equally likely, and the offers to come carry
/// the fraction into CopyPotential, which mixes the two counts.
///
/// The fixed counts are only for a panel whose counter cannot be read, which no dump has shown (the counter read in
/// every panel dumped since 3.30.0.4). They replaced the Maps settings (white, yellow, red; 3, 4, 5 until set, from
/// 2026-11-19 until the counter). Measured before the counter: 3 in white maps, every map seen (Silo 68, Malformation
/// 68, Geode 70 and a Savage map, 2026-11-14); in yellow maps 3 or 4 (Belfry 73 four, City Square 74 three, Cemetery
/// 75 four); 4 in Jungle Valley, level 83 (the counter, 2026-11-19). Campaign areas take the white maps' count.
/// </summary>
internal static class MapCauldrons
{
    /// <summary>The highest area level of a white map (tier 5).</summary>
    private const int HighestWhiteLevel = 72;

    /// <summary>The highest area level of a yellow map (tier 10).</summary>
    private const int HighestYellowLevel = 77;

    private const double WhiteMapCauldrons = 3d;
    private const double YellowMapCauldrons = 3.5d;
    private const double RedMapCauldrons = 4d;

    /// <summary>
    /// The Cauldrons the area holds: the Fate counter's count when the panel shows one, else the fixed count (PerMap).
    /// </summary>
    public static double InArea(FateCounter counter, int areaLevel) => counter?.InArea ?? PerMap(areaLevel);

    /// <summary>The Cauldrons an area of this level is taken to hold when the Fate counter cannot be read.</summary>
    public static double PerMap(int areaLevel) => areaLevel switch
    {
        <= HighestWhiteLevel => WhiteMapCauldrons,
        <= HighestYellowLevel => YellowMapCauldrons,
        _ => RedMapCauldrons,
    };
}
