using System;
using System.Collections.Generic;
using System.Linq;

namespace AutoAugury;

/// <summary>One Fate of an invocation: its copies, and the first of them, the copy the others empower.</summary>
internal sealed record FateGroup(IReadOnlyList<FateCard> Copies, FateCard First);

/// <summary>
/// What an invocation is worth by the player's weights: the rules InvocationAdvisor, FateScoring and CopyPotential
/// share, as the boss panel's reward summary counts rewards ([2,7]; dumps of 2026-11-14 05:35:23 and 05:35:25).
///
/// Each Fate invoked counts its first copy's rewards, once each, or three times each when a second copy empowers it
/// (the panel: "tripling the rewards granted by it and upgrading its tier, but nullifying the rewards of the Fate in
/// this slot"). A third copy is tripled as well, its own rewards, with the second still the nullified empower slot
/// (3.30.0b Hotfix 3; measured 2026-11-19: three Murderous copies showed four rewards at 3x, the rewards of the cards
/// in the first and third slots, and never the third card's own). A reward that needs n copies is claimed once for
/// every n of its count. Measured with a pair of Incandescent whose first copy held greatly increased Item Rarity
/// (1 copy), additional Currency Items (2) and additional Valuable Currency Items (3): "3x" Item Rarity, "1x"
/// Currency Items with "1/2" left over, and "1x" Valuable Currency Items; so a pair claims two- and three-copy
/// rewards alike.
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
    /// What an invocation is worth: each counted copy's rewards (CountedCopies), claimed once per the copies it needs,
    /// times its family's weight; plus each Fate's Preference, once per Fate whatever its copies. A reward no
    /// catalogue reward matched counts nothing.
    /// </summary>
    public static int InvocationValue(IEnumerable<FateGroup> groups, AutoAugurySettings settings)
    {
        var counts = new Dictionary<string, (AuguryReward Reward, int Needed, int Count)>();
        var preference = 0;

        foreach (var group in groups)
        {
            preference += Preference(group.First, group.Copies.Count, settings);

            foreach (var (card, multiple) in CountedCopies(group, settings))
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
        }

        return preference +
               counts.Values.Sum(e => e.Count / e.Needed * settings.Rewards.WeightOf(e.Reward.Family));
    }

    /// <summary>
    /// The copies of a Fate that pay, with how many times each counts its rewards: the first, once alone or three
    /// times when a second copy empowers it; and, with three copies, the third as well, also three times, while the
    /// second pays nothing (3.30.0b Hotfix 3: "the first and third Fates will have their rewards tripled, while the
    /// second is ignored"; three Murderous copies measured 2026-11-19). Which card reaches the first and third slots
    /// is the player's choice, so the value takes the best copy left for the third: the group's copies are in
    /// collection order, not slot order.
    /// </summary>
    internal static IEnumerable<(FateCard Card, int Multiple)> CountedCopies(FateGroup group,
        AutoAugurySettings settings)
    {
        if (group.Copies.Count < 2)
        {
            yield return (group.First, 1);
            yield break;
        }

        yield return (group.First, EmpoweredMultiple);

        if (group.Copies.Count < 3)
            yield break;

        var third = group.Copies
            .Where(c => !ReferenceEquals(c, group.First))
            .MaxBy(c => RewardsValue(c, EmpoweredMultiple, settings));

        if (third != null)
            yield return (third, EmpoweredMultiple);
    }

    /// <summary>
    /// A Fate's copies in the order the game wants them placed: the paying first copy, then the nullified empower
    /// copy, then the third paying copy (all three with three or more copies), then any further copies, which pay
    /// nothing. The player's slot choice is what makes a copy the third, so this order is what the guide asks for.
    /// </summary>
    public static List<FateCard> PlacingOrder(FateGroup group, AutoAugurySettings settings)
    {
        var order = new List<FateCard> { group.First };

        if (group.Copies.Count < 2)
            return order;

        var counted = CountedCopies(group, settings).Select(static c => c.Card).ToList();
        var third = counted.Count > 1 ? counted[1] : null;

        // With two copies the other one is the empower; with three, the copy not chosen as the third.
        var nullified = group.Copies.FirstOrDefault(c =>
            !ReferenceEquals(c, group.First) && !ReferenceEquals(c, third));

        if (nullified != null)
            order.Add(nullified);

        if (third != null)
            order.Add(third);

        foreach (var card in group.Copies)
        {
            if (!order.Any(o => ReferenceEquals(o, card)))
                order.Add(card);
        }

        return order;
    }

    /// <summary>
    /// One card's rewards on their own, each counted the given number of times and claimed once per the copies it
    /// needs: with 1, what the card pays alone; with EmpoweredMultiple, what it pays as a pair's first copy.
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
    /// The tier a Fate is invoked at: its own, one higher for a pair ("upgrading its tier"), and its own again for
    /// three copies. Seen once: Incandescent of tier 2 read tier 3 with two copies placed and tier 2 with three
    /// (2026-11-14 05:35). That reading predates 3.30.0b Hotfix 3, which made a third copy pay, and has not been
    /// re-checked: no three-copy Fate whose tiers read apart has been seen since. Whether a tier 3 Fate's pair goes
    /// above 3 is not known either.
    /// </summary>
    public static int TierInvokedAt(int tier, int copies) => copies == 2 ? tier + 1 : tier;

    /// <summary>Two cards are copies of one Fate: the same catalogue Fate, or, unresolved, the same name.</summary>
    public static bool SameFate(FateCard a, FateCard b) =>
        a.Fate != null && b.Fate != null
            ? a.Fate.Id == b.Fate.Id
            : string.Equals(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
}
