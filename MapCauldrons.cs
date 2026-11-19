namespace AutoAugury;

/// <summary>
/// How many Cauldrons an area is taken to hold, by its level: the player's settings (MapSettings), 3 in white maps, 4
/// in yellow and 5 in red until set. FateScoring uses it to count the offers still to come (this less the Fates
/// collected less the Cauldron being chosen at), which sets how much a Fate's chance of being paired later is worth,
/// and for "Estimated: 1/3". A fraction is an expectation: 3.5 is 3 or 4 Cauldrons, equally likely, and the offers to
/// come carry the fraction into CopyPotential, which mixes the two counts.
///
/// Measured: 3 in white maps, every map seen (Silo 68, Malformation 68, Geode 70 and a Savage map, 2026-11-14); in
/// yellow maps 3 or 4 (Belfry 73 four, City Square 74 three, Cemetery 75 four). The defaults for yellow (area level
/// 73 to 77) and red (78 and up) were the user's choice, in line with the guides' "up to 4-5 related encounters in the
/// final map". The client's data has no stat for the count (none of the Augury stats or tables holds one, where other
/// leagues have "additional Breaches" and the like), so it is likely decided by the server, by tier, atlas progression
/// or both. Campaign areas take the white maps' setting.
/// </summary>
internal static class MapCauldrons
{
    /// <summary>The highest area level of a white map (tier 5).</summary>
    private const int HighestWhiteLevel = 72;

    /// <summary>The highest area level of a yellow map (tier 10).</summary>
    private const int HighestYellowLevel = 77;

    /// <summary>What is assumed, as one of the known limits at the top of the settings (DevelopmentNotice).</summary>
    public const string Disclaimer =
        "Cauldrons per map are set under Maps (3 in white maps, 4 in yellow, 5 in red until changed; seen 3 in " +
        "white and 3 or 4 in yellow, red not yet counted). They set how many offers are still to come, and so the " +
        "potential weight; a fraction such as 3.5 counts as 3 or 4, equally likely.";

    /// <summary>
    /// The Cauldrons an area of this level is taken to hold, from the settings (the defaults when there are none).
    /// </summary>
    public static double PerMap(int areaLevel, MapSettings maps)
    {
        maps ??= new MapSettings();

        return areaLevel switch
        {
            <= HighestWhiteLevel => maps.WhiteMapCauldrons.Value,
            <= HighestYellowLevel => maps.YellowMapCauldrons.Value,
            _ => maps.RedMapCauldrons.Value,
        };
    }
}
