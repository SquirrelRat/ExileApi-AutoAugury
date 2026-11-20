using System;
using System.Collections.Generic;
using System.Linq;

namespace AutoAugury;

/// <summary>
/// One Fate of an invocation: its copies in placing order. The second copy empowers the first; a third counts its own
/// rewards three times too (FateValues).
/// </summary>
internal sealed record FateGroup(IReadOnlyList<FateCard> Copies)
{
    /// <summary>The copy placed first, whose rewards count, three times over when a second copy empowers it.</summary>
    public FateCard First => Copies[0];

    /// <summary>The copy placed third, whose own rewards count three times; null with fewer than three.</summary>
    public FateCard Third => Copies.Count >= 3 ? Copies[2] : null;
}

/// <summary>
/// What an invocation is worth by the player's weights: the rules InvocationAdvisor, FateScoring and CopyPotential
/// share, as the boss panel's reward summary counts rewards ([2,7]; dumps of 2026-11-14 05:35:23 and 05:35:25).
///
/// Each Fate invoked counts its first copy's rewards, once each, or three times each when a second copy empowers it
/// (the panel: "tripling the rewards granted by it and upgrading its tier, but nullifying the rewards of the Fate in
/// this slot"). A reward that needs n copies is claimed once for every n of its count. Measured: a pair of Incandescent
/// whose first copy held greatly increased Item Rarity (1 copy), additional Currency Items (2) and additional Valuable
/// Currency Items (3) showed "3x" Item Rarity, "1x" Currency Items with "1/2" left over, and "1x" Valuable Currency
/// Items; so a pair claims two- and three-copy rewards alike.
///
/// A third copy counts its own rewards three times each, added to the first's, by the patch notes of 3.30.0b Hotfix 3
/// (2026-11-16): "When Invoking Fates, you may now Invoke three copies of the same Fate. The first and third Fates will
/// have their rewards tripled, while the second is ignored." Not yet measured in the summary. Before the hotfix,
/// placing a third copy changed nothing in the summary and took the slots' tier back from 3 to 2 (2026-11-14 05:35).
///
/// Counts are added up across Fates, reward by reward (CurrencyLow1 and CurrencyHigh1, which share their text, are
/// listed apart). Measured: a Houndmaster pair whose first copy held massively increased Item Rarity (2 copies) and
/// Black Frost holding the same reward showed "2x" Item Rarity with nothing left over, 3 + 1 copies (Cemetery,
/// 2026-11-14 06:22:57), as the advice had counted.
/// </summary>
internal static class FateValues
{
    /// <summary>How many times an empowered Fate counts its rewards: three ("3x" in the boss's summary).</summary>
    public const int EmpoweredMultiple = 3;

    /// <summary>
    /// What an invocation is worth: each reward's count over the Fates' first copies (EmpoweredMultiple for a Fate
    /// with two or more copies, 1 for one) and third copies (EmpoweredMultiple), claimed once per the copies it needs,
    /// times its family's weight; plus each Fate's Preference. A reward no catalogue reward matched counts nothing.
    /// </summary>
    public static int InvocationValue(IEnumerable<FateGroup> groups, AutoAugurySettings settings)
    {
        var groupList = groups.ToList();
        var preference = groupList.Sum(g => Preference(g.First, g.Copies.Count, settings));

        return preference + CountsOf(groupList).Values
            .Sum(e => e.Count / e.Needed * settings.Rewards.WeightOf(e.Reward.Family));
    }

    /// <summary>
    /// What an invocation holds of the Must take rewards (RewardSettings.IsMustTake), for MustTakeRule: each one's
    /// count as InvocationValue counts it, divided by the copies it needs without rounding down, times its weight. So
    /// one copy of a three-copy reward holds a third of its weight and a pair all of it, and pairing a held Must take
    /// Fate raises it from a third to the whole. Not what the boss pays: a lone copy of a three-copy reward claims
    /// nothing there.
    /// </summary>
    public static double MustTakeValue(IEnumerable<FateGroup> groups, AutoAugurySettings settings) =>
        CountsOf(groups.ToList()).Values
            .Where(e => settings.Rewards.IsMustTake(e.Reward.Family))
            .Sum(e => (double)e.Count / e.Needed * settings.Rewards.WeightOf(e.Reward.Family));

    /// <summary>
    /// Each reward's count over the groups' first copies (EmpoweredMultiple for a Fate with two or more copies, 1 for
    /// one) and third copies (EmpoweredMultiple), by reward Id, with the most copies any card says it needs.
    /// </summary>
    private static Dictionary<string, (AuguryReward Reward, int Needed, int Count)> CountsOf(List<FateGroup> groups)
    {
        var counts = new Dictionary<string, (AuguryReward Reward, int Needed, int Count)>();

        void Count(FateCard card, int multiple)
        {
            foreach (var shown in card.Rewards)
            {
                if (shown.Reward == null)
                    continue;

                counts.TryGetValue(shown.Reward.Id, out var entry);
                counts[shown.Reward.Id] = (shown.Reward, Math.Max(Math.Max(1, shown.CopiesNeeded), entry.Needed),
                    entry.Count + multiple);
            }
        }

        foreach (var group in groups)
        {
            Count(group.First, group.Copies.Count >= 2 ? EmpoweredMultiple : 1);

            if (group.Third != null)
                Count(group.Third, EmpoweredMultiple);
        }

        return counts;
    }

    /// <summary>
    /// One card's rewards on their own, each counted the given number of times and claimed once per the copies it
    /// needs: with 1, what the card pays alone; with EmpoweredMultiple, what it pays as an empowered Fate's first copy
    /// or as a third copy.
    /// </summary>
    public static int RewardsValue(FateCard card, int multiple, AutoAugurySettings settings) =>
        card.Rewards.Where(static r => r.Reward != null)
            .Sum(r => multiple / Math.Max(1, r.CopiesNeeded) * settings.Rewards.WeightOf(r.Reward.Family));

    /// <summary>
    /// The player's liking for a Fate on the boss: its Fate weight, plus Score per tier times the tier it is invoked
    /// at (TierInvokedAt). No tier part when the card's tier cannot be read.
    /// </summary>
    public static int Preference(FateCard first, int copies, AutoAugurySettings settings) =>
        settings.Fates.WeightOf(first.Fate?.Id) +
        (first.Tier is { } tier ? settings.Fates.ScorePerTier.Value * TierInvokedAt(tier, copies) : 0);

    /// <summary>
    /// The tier a Fate is invoked at: its own, one higher for two or three copies. Read from the client's code (the
    /// boss's invocation count, 3.30.0c, 2026-11-18): the second copy looks up the tier row for tier + 1 and keeps it
    /// for the Fate, and a further copy changes no tier. Before 3.30.0b Hotfix 3 three copies read the Fate's own tier
    /// (Incandescent, 2026-11-14 05:35). A tier 3 Fate's pair finds no tier 4 row, and what the game shows then is not
    /// read; 4 is returned.
    /// </summary>
    public static int TierInvokedAt(int tier, int copies) => copies >= 2 ? tier + 1 : tier;

    /// <summary>Two cards are copies of one Fate: the same catalogue Fate, or, unresolved, the same name.</summary>
    public static bool SameFate(FateCard a, FateCard b) =>
        a.Fate != null && b.Fate != null
            ? a.Fate.Id == b.Fate.Id
            : string.Equals(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
}
