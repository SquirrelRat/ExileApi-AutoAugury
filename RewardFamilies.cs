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

    /// <summary>
    /// The categories in the order the settings list them: currency first, where the most valuable rewards are (the
    /// user, 2026-11-18; uniques came first before).
    /// </summary>
    public static readonly string[] Categories =
        [CurrencyAndLoot, UniqueItems, Gems, RareItemsWithModifier, OtherItems];

    public static readonly IReadOnlyList<RewardFamily> All = Build();

    private static readonly Dictionary<string, RewardFamily> ById = All.ToDictionary(static f => f.Id);

    public static RewardFamily Of(string familyId) => familyId == null ? null : ById.GetValueOrDefault(familyId);

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
    /// Whether a family is Must take until the player says otherwise (RewardSettings.IsMustTake), on the user's
    /// request (2026-11-18): Hinekora's Lock and the Mirror Shard, worth tens of divines, then Reflecting Mist, the
    /// Fracturing Orb and both Divine Orb rewards.
    /// </summary>
    public static bool MustTakeByDefault(string family) =>
        family is "CurrencyOpulentHinekorasLock" or "CurrencyOpulentMirrorShard" or "CurrencyOpulentReflectingMist"
            or "CurrencyOpulentFractureOrb" or "CurrencyOpulentDivineOrb1" or "CurrencyOpulentDivineOrb2";

    /// <summary>
    /// A short name from a reward's text: the first line less "Empowered Monster will drop" and the like, and the
    /// modifier from the second line in brackets: "additional Rare Boots (a Speed modifier)".
    /// </summary>
    internal static string LabelOf(string description)
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
    /// as saved at 2026-11-19 00:08:38, after the user's pass over the 3.30.0c rewards, taken as the defaults, every
    /// family the user had not set keeping the default it had (as on 2026-11-14, when the user's weights of 04:42 and
    /// 06:50 became the defaults). Every family not named gets 1, which most rare items with a given modifier have,
    /// so a family a later patch adds starts at 1 until the catalogue is regenerated and it is weighted.
    ///
    /// The chase rewards 3.30.0c added for high tier maps (all needing three copies then; since 3.30.0.4 a single
    /// Divine Orb and a single Orb of Annulment need two, weights unchanged) started from poe.ninja's Covens
    /// currency prices of 2026-11-18 at 100 per Divine Orb (a Divine Orb 219 chaos): Hinekora's Lock 63.71 divines,
    /// 6371; Mirror Shard 26.78, 2678; Reflecting Mist 8.66, 866; Fracturing Orb 5.73, 573; a Divine Orb 100. The user
    /// then set Divine Orbs (how many is not said) 300, a Sacred Orb 66, Orbs of Annulment 47 (the Id misspelt in the
    /// game's data, CurrencyOpulemtAnnulemntOrb), an Orb of Annulment 21, Rare Amulets with a Gem modifier 5.
    /// Saved settings from before this table are overwritten once (AutoAugury.ApplySettingsChanges, ConfigVersion 1).
    /// </summary>
    private static int DefaultWeightOf(string family) => family switch
    {
        "CurrencyOpulentHinekorasLock" => 6371,
        "CurrencyOpulentMirrorShard" => 2678,
        "CurrencyOpulentReflectingMist" => 866,
        "CurrencyOpulentFractureOrb" => 573,
        "CurrencyOpulentDivineOrb2" => 300,
        "CurrencyOpulentDivineOrb1" => 100,
        "CurrencyOpulentSacred" => 66,
        "CurrencyOpulent" => 65,
        "StackedDecksHigh" => 48,
        "CurrencyOpulemtAnnulemntOrb" => 47,
        "StackedDecksLow" => 46,
        "UniqueJewelleryHigh" => 45,
        "UniqueJewel" => 44,
        "UniqueJewelleryLow" => 43,
        "CurrencyLow" => 25,
        "GoldLow" => 24,
        "CurrencyOpulentAnnulmentOrb" => 21,
        "CurrencyHigh" => 20,
        "GoldHigh" => 19,
        "ThreadsHigh" => 17,
        "UniqueArmourHigh" => 16,
        "RarityHigh" or "UniqueWeaponHigh" => 15,
        "ExceptionalGem" or "ForceViscera" or "RarityLow" or "ThreadsLow" => 10,
        "AmuletTransformGems" or "JewelleryLow" or "UniqueArmourLow" or "UniqueFlask" or "UniqueWeaponLow" => 5,
        "BootsTransformSpeed" => 3,
        "AmuletTransformAttribute" or "BodyTransformDefence" or "BootsTransformNoDefence"
            or "BootsTransformResistance" or "BowTransformCrit" or "BowTransformSpeed"
            or "EquipmentDexArmourLow" or "EquipmentIntArmourLow" or "EquipmentMeleeWeaponLow"
            or "EquipmentRangedWeaponLow" or "EquipmentStrArmourLow" or "QualityGemHigh" or "RareJewelleryLow"
            or "SceptreTransformGems" or "StaffTransformGems" or "WandTransformGems"
            or "WeaponTransformPhysical" => 2,
        "MapsHigh" or "MapsLow" or "QualityGemLow" or "SkillGem" or "SupportGem" or "UniqueCorpseRose"
            or "UniqueDoedresTongue" or "UniqueOrderedFate" or "UniqueThornbrew" or "UniqueVendigoPelt" => 0,
        _ => 1,
    };
}
