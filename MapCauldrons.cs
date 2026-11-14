namespace AutoAugury;

/// <summary>
/// How many Cauldrons an area is taken to hold, by its level: an assumption, baked in for now. FateScoring uses it to
/// count the offers still to come (this less the Fates collected less the Cauldron being chosen at), which sets how
/// much a Fate's chance of being paired later is worth.
///
/// Measured: 3 in white maps, every map seen (Silo 68, Malformation 68, Geode 70 and a Savage map, 2026-11-14); in
/// yellow maps 3 or 4 (Belfry 73 four, City Square 74 three, Cemetery 75 four). Assumed: 4 in yellow maps (area level
/// 73 to 77) and 5 in red (78 and up), the user's choice, in line with the guides' "up to 4-5 related encounters in the
/// final map". The client's data has no stat for the count (none of
/// the Augury stats or tables holds one, where other leagues have "additional Breaches" and the like), so it is
/// likely decided by the server, by tier, atlas progression or both. Campaign areas are taken as 3.
/// </summary>
internal static class MapCauldrons
{
    /// <summary>The highest area level of a white map (tier 5).</summary>
    private const int HighestWhiteLevel = 72;

    /// <summary>The highest area level of a yellow map (tier 10).</summary>
    private const int HighestYellowLevel = 77;

    /// <summary>What is assumed, as one of the known limits at the top of the settings (DevelopmentNotice).</summary>
    public const string Disclaimer =
        "Cauldrons per map are assumed: 3 in white maps, 4 in yellow (seen 3 or 4), 5 in red (not yet seen). They " +
        "set how many offers are still to come, and so the potential weight.";

    public static int Assumed(int areaLevel) => areaLevel switch
    {
        <= HighestWhiteLevel => 3,
        <= HighestYellowLevel => 4,
        _ => 5,
    };
}
