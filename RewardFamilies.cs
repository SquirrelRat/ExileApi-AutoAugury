using System;
using System.Collections.Generic;
using System.Linq;

namespace AutoAugury;

/// <summary>
/// A reward family: one kind of reward across the area levels it rolls at (WarstaffTransformSpeed1 and 2 are one
/// family), which is what a reward weight is set for.
/// </summary>
/// <param name="Id">The family Id from AuguryCatalogue, the reward Id less the HardMode prefix and level digit.</param>
/// <param name="Label">A short name, from the reward's text: "Rare Boots (a Speed modifier)".</param>
/// <param name="Description">The reward's full text, from its first row outside the HardMode set.</param>
/// <param name="Category">The settings group it is listed under.</param>
/// <param name="CopiesNeeded">Copies of a Fate needed to claim it, taken as its tier (observed for tier 2).</param>
/// <param name="MinLevel">The lowest area level any of its rows rolls at.</param>
/// <param name="DefaultWeight">Its weight until the player sets one; chosen (RewardFamilies.DefaultWeightOf).</param>
internal sealed record RewardFamily(string Id, string Label, string Description, string Category, int CopiesNeeded,
    int MinLevel, int DefaultWeight);

/// <summary>
/// The reward families of AuguryCatalogue, grouped for the settings, with default weights. The categories are the
/// plugin's own grouping, by the family Id; the game has none.
/// </summary>
internal static class RewardFamilies
{
    public const string UniqueItems = "Unique items";
    public const string CurrencyAndLoot = "Currency and loot";
    public const string Gems = "Gems";
    public const string RareItemsWithModifier = "Rare items with a modifier";
    public const string OtherItems = "Other items";

    /// <summary>The categories in the order the settings list them.</summary>
    public static readonly string[] Categories =
        [UniqueItems, CurrencyAndLoot, Gems, RareItemsWithModifier, OtherItems];

    public static readonly IReadOnlyList<RewardFamily> All = Build();

    private static readonly Dictionary<string, RewardFamily> ById = All.ToDictionary(static f => f.Id);

    public static RewardFamily Of(string familyId) => familyId == null ? null : ById.GetValueOrDefault(familyId);

    /// <summary>
    /// Whether every catalogue row of the family is of the HardMode set: taken to be Ruthless-only (none has been
    /// seen offered). The settings table can hide these on request; scoring is by family either way.
    /// </summary>
    public static bool IsHardModeOnly(string familyId)
    {
        if (familyId == null || ById.GetValueOrDefault(familyId) == null)
            return false;

        return AuguryCatalogue.Rewards.Where(r => r.Family == familyId).All(static r => r.HardMode);
    }

    private static List<RewardFamily> Build()
    {
        var families = new List<RewardFamily>();

        foreach (var group in AuguryCatalogue.Rewards.GroupBy(static r => r.Family))
        {
            // The HardMode rows read differently ("has a chance to drop"); the family is named by its other rows.
            var named = group.FirstOrDefault(static r => !r.HardMode) ?? group.First();
            var tiers = group.Where(r => r.HardMode == named.HardMode).Select(static r => r.Tier);

            families.Add(new RewardFamily(group.Key, LabelOf(named.Description), named.Description,
                CategoryOf(group.Key), tiers.Min(), group.Min(static r => r.MinLevel), DefaultWeightOf(group.Key)));
        }

        return families
            .OrderBy(static f => Array.IndexOf(Categories, f.Category))
            .ThenByDescending(static f => f.DefaultWeight)
            .ThenBy(static f => f.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// A short name from a reward's text: the first line less "Empowered Monster will drop" and the like, and the
    /// modifier from the second line in brackets: "additional Rare Boots (a Speed modifier)".
    /// </summary>
    private static string LabelOf(string description)
    {
        var lines = (description ?? "").Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var first = lines.Length > 0 ? lines[0].Trim() : "";

        foreach (var lead in new[]
                 {
                     "Empowered Monster has a chance to drop ", "Empowered Monster will drop ",
                     "Empowered Monster has ",
                 })
        {
            if (first.StartsWith(lead, StringComparison.Ordinal))
            {
                first = first[lead.Length..];
                break;
            }
        }

        if (first.Length > 0)
            first = char.ToUpperInvariant(first[0]) + first[1..];

        var modifier = lines.Length > 1 ? lines[1].Trim() : "";
        var have = modifier.IndexOf("will have ", StringComparison.Ordinal);

        return have >= 0 ? $"{first} ({modifier[(have + "will have ".Length)..]})" : first;
    }

    private static string CategoryOf(string family)
    {
        if (family.StartsWith("Unique", StringComparison.Ordinal))
            return UniqueItems;

        if (family.Contains("Gem", StringComparison.Ordinal))
            return Gems;

        if (family.Contains("Transform", StringComparison.Ordinal))
            return RareItemsWithModifier;

        if (family.StartsWith("Equipment", StringComparison.Ordinal) ||
            family.Contains("Jewellery", StringComparison.Ordinal))
            return OtherItems;

        return CurrencyAndLoot;
    }

    /// <summary>
    /// The default weights, on a scale where a weight is what one reward of the family is worth: the user's own weights
    /// as set in game on 2026-11-14, taken as the defaults (the saved settings at 04:42, then at 06:50: Valuable Unique
    /// Armour 16, Valuable Unique Weapon 15, and 2 for every rare item with a given modifier, plain equipment and rare
    /// jewellery). StackedDecksLow (20) and ThreadsLow (10) are the first build's choices, which the user left. Every
    /// family not named gets 2.
    /// </summary>
    private static int DefaultWeightOf(string family) => family switch
    {
        "ExceptionalGem" => 70,
        "CurrencyOpulent" => 65,
        "UniqueJewelleryHigh" => 50,
        "UniqueJewel" => 49,
        "StackedDecksHigh" => 48,
        "CurrencyHigh" => 45,
        "UniqueJewelleryLow" => 40,
        "StackedDecksLow" => 20,
        "CurrencyLow" => 19,
        "ThreadsHigh" => 18,
        "UniqueArmourHigh" => 16,
        "RarityHigh" or "UniqueWeaponHigh" => 15,
        "ForceViscera" or "RarityLow" or "GoldHigh" or "QualityGemHigh" or "ThreadsLow" => 10,
        "UniqueArmourLow" or "UniqueFlask" or "UniqueWeaponLow" or "JewelleryLow" => 5,
        "GoldLow" => 1,
        "UniqueDoedresTongue" or "UniqueCorpseRose" or "UniqueThornbrew" or "UniqueVendigoPelt"
            or "UniqueOrderedFate" or "MapsHigh" or "MapsLow" or "QualityGemLow" or "SkillGem" or "SupportGem" => 0,
        _ => 2,
    };
}
