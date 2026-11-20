using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace AutoAugury;

/// <summary>
/// Finds what the Fates panel shows in AuguryCatalogue: a Fate by the name on its card, its tier by its full text, and
/// a reward by the text on its icon. Texts are compared with their whitespace collapsed, since the panel's text and
/// the table's carry the same words and markup but not always the same line breaks.
/// </summary>
internal static class AuguryCatalogueLookup
{
    private static readonly Dictionary<string, AuguryFate> FatesByName = BuildFatesByName();

    /// <summary>The catalogue's reward outcomes with the ones read from a later client (AuguryCatalogueSupplement).</summary>
    private static readonly AuguryReward[] Rewards =
        [.. AuguryCatalogue.Rewards, .. AuguryCatalogueSupplement.Rewards];

    /// <summary>
    /// Each reward by its text without the copies note and its tier, the first row of each (a family's level bands
    /// share a text). No key is shared by two families in the export of 2026-11-14. A text alone is not enough: "will
    /// drop additional Currency Items" is CurrencyLow at tier 1 and CurrencyHigh at tier 2.
    /// </summary>
    private static readonly Dictionary<(string Text, int Tier), AuguryReward> RewardsByTextAndTier =
        BuildByTextAndTier();

    private static Dictionary<(string Text, int Tier), AuguryReward> BuildByTextAndTier()
    {
        var byTextAndTier = new Dictionary<(string Text, int Tier), AuguryReward>();

        // First wins: the export's rows before the supplement's, and a supplement row never overrides one.
        foreach (var reward in Rewards)
            byTextAndTier.TryAdd((RewardText(reward.Description), reward.Tier), reward);

        return byTextAndTier;
    }

    private static readonly Dictionary<string, AuguryReward> RewardsByText = Rewards
        .GroupBy(static r => RewardText(r.Description))
        .ToDictionary(static g => g.Key, static g => g.OrderBy(static r => r.Tier).First());

    private static Dictionary<string, AuguryFate> BuildFatesByName()
    {
        var byName = new Dictionary<string, AuguryFate>(StringComparer.OrdinalIgnoreCase);

        foreach (var fate in AuguryCatalogue.Fates)
        {
            byName.TryAdd(fate.Name, fate);
            byName.TryAdd(fate.Id, fate);
        }

        return byName;
    }

    /// <summary>The Fate whose name (or table Id) the card shows, or null.</summary>
    public static AuguryFate FateNamed(string name) =>
        name == null ? null : FatesByName.GetValueOrDefault(name.Trim());

    /// <summary>
    /// The tier whose full text equals the card's, or null when none does or more than one does: 7 of the 25 Fates
    /// (Dryad, Wildfire, Gravewalker, Tempest, Savage, Pestilent, Sadist) read the same at every tier.
    /// </summary>
    public static int? TierOf(AuguryFate fate, string fullText)
    {
        if (fate == null || string.IsNullOrWhiteSpace(fullText))
            return null;

        var text = Collapsed(fullText);
        var matches = fate.Tiers.Where(t => Collapsed(t.Body) == text).ToList();

        return matches.Count == 1 ? matches[0].Tier : null;
    }

    /// <summary>
    /// Whether the Fate's tiers read differently: the tier is found by matching the card's full text, so a Fate
    /// whose tiers all read the same (Dryad and six more) never has a known tier. Used for the overlay's tier badge.
    /// </summary>
    public static bool HasDistinctTiers(AuguryFate fate)
    {
        if (fate == null)
            return false;

        return fate.Tiers.Select(t => Collapsed(t.Body)).Distinct().Count() > 1;
    }

    /// <summary>
    /// How many copies a reward needs, from the note the panel adds to it: "invoke two copies of it" is 2, "three
    /// copies" 3, no note 1. Seen: the two-copy note on rewards of tier 2 in the table, the three-copy note on
    /// CurrencyOpulent1, of tier 3 (2026-11-14).
    /// </summary>
    public static int CopiesNeededOf(string shownText)
    {
        if (shownText == null)
            return 1;

        if (shownText.Contains("three copies", StringComparison.OrdinalIgnoreCase))
            return 3;

        return shownText.Contains("two copies", StringComparison.OrdinalIgnoreCase) ? 2 : 1;
    }

    /// <summary>
    /// The catalogue reward a shown text is: by its text and, taking the copies needed as the tier, by tier; failing
    /// that by text alone. Null when no reward has the text.
    /// </summary>
    public static AuguryReward RewardShown(string shownText, int copiesNeeded)
    {
        if (string.IsNullOrWhiteSpace(shownText))
            return null;

        var text = RewardText(shownText);

        return RewardsByTextAndTier.GetValueOrDefault((text, copiesNeeded)) ?? RewardsByText.GetValueOrDefault(text);
    }

    /// <summary>A reward's text without the panel's copies note ("&lt;rgb(122,122,122)&gt;{To claim ...}").</summary>
    private static string RewardText(string text)
    {
        var note = text.IndexOf("<rgb(", StringComparison.Ordinal);

        return Collapsed(note >= 0 ? text[..note] : text);
    }

    private static string Collapsed(string text) => Regex.Replace(text ?? "", @"\s+", " ").Trim();
}
