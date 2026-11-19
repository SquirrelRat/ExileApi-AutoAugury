using System;
using System.Collections.Generic;
using System.Linq;

namespace AutoAugury;

/// <summary>One offered card's score and its parts, for the overlay.</summary>
/// <param name="Card">The offered card.</param>
/// <param name="Total">Gain plus Potential, rounded.</param>
/// <param name="Gain">What adding the card adds to the best invocation now (InvocationAdvisor).</param>
/// <param name="Potential">
/// The change in the collection's copy potential (CopyPotential) from adding the card: below zero for a card that
/// pairs a held Fate now (the chance of pairing it later is used up), above zero for a new Fate that could be paired.
/// </param>
/// <param name="Invoked">Whether the best invocation with the card in the collection uses it.</param>
/// <param name="PairedWith">The collected copy it would be invoked with, or null.</param>
/// <param name="OffersToCome">
/// The offers still to come from Cauldrons of its faction (OffersToComeFor), a fraction when the map's Cauldrons are.
/// </param>
/// <param name="UnknownRewards">
/// Rewards shown on the card that count nothing for want of a weight (NewRewardRegistry.LacksWeight): matched no
/// catalogue reward and not learned, or learned and the weight not set.
/// </param>
/// <param name="MustTakeRank">
/// What the card is ranked by under MustTakeRule: with no Must take reward in the collection, the weight of the Must
/// take rewards it carries; with one, what it adds to the Must take value of the best invocation
/// (InvocationAdvisor.BestMustTakeValue). 0 when it does nothing for them.
/// </param>
internal sealed record FateScore(FateCard Card, int Total, int Gain, double Potential, bool Invoked,
    FateCard PairedWith, double OffersToCome, int UnknownRewards, double MustTakeRank = 0d)
{
    /// <summary>
    /// Gain plus Potential, unrounded: what the cards are ranked and the best framed by. Total, its rounding, is what
    /// is written; weak cards often all round to the same Total (0 at the Armoury, 2026-11-14 17:40:47, where their
    /// values were 0.46, 0.46, 0.31 and 0.15).
    /// </summary>
    public double Value => Gain + Potential;
}

/// <summary>
/// Scores each offered Fate by the player's weights, so the overlay can mark the best pick.
///
/// A pick is worth what it adds to the collection's value, which has two parts:
///
/// - Now: the best invocation at the area's boss (InvocationAdvisor; three slots while choosing, as seen at the boss).
///   A second copy of a held Fate pairs with it (FateValues); a third counts its own rewards three times too
///   (3.30.0b Hotfix 3); once the collection fills the slots, a card only counts by how much it beats the Fates it
///   would push out.
/// - Later: CopyPotential, the expected value of the best pair or triple the collection could still complete from
///   the offers still to come (OffersToComeFor). Pairing a Fate now uses up the chance of pairing it later, so its
///   potential is lost; a card of a new Fate brings the chance of its own pair, two- and three-copy rewards included.
///
/// So the score is the change in the first plus the change in the second. With no offers known to come, the second is
/// 0 and a card is scored on what it adds now. This replaced a flat "Two-copy reward potential" share (25% of a card's
/// two-copy rewards), which had no basis and leaned towards completing a pair now: it counted nothing for a held Fate
/// that could still pair later, nor for pairing the stronger card later.
///
/// Must take rewards come first (BestOf): the score is still written, but the card framed as best is chosen by the
/// Must take rule whenever an offered card touches a Must take reward.
/// </summary>
internal static class FateScoring
{
    /// <summary>
    /// Which Cauldron of the map the player is choosing at and of how many, for "Estimated: 1/3" by the close button:
    /// the Fates collected plus one, of the larger of the map's Cauldrons (MapCauldrons.InArea: the Fate counter's, or
    /// the fixed count, maybe a fraction) and this one plus those the tracker knows still to come.
    /// </summary>
    public static (int Current, double Total) CauldronProgressOf(FatesPanelState state, int areaLevel,
        CauldronTracker tracker)
    {
        var current = CollectedOf(state) + 1;
        var mapFaction = state.Offered.FirstOrDefault(static c => c.Fate != null)?.Fate.FactionId;
        var known = tracker?.OffersToCome(mapFaction, true) ?? 0;

        return (current, Math.Max(MapCauldrons.InArea(state.Counter, areaLevel), current + known));
    }

    /// <summary>
    /// The offers still to come for a Fate's faction while choosing: the larger of the count from the map's size
    /// (MapCauldrons.InArea: the Fate counter's Cauldrons, or the fixed count for the area level, maybe a fraction;
    /// less the Fates collected, less the Cauldron being chosen at) and the Cauldrons the tracker knows
    /// not done. The count from the map's size is what matters in play: Cauldrons are met one at a time and picked from
    /// at once, so when choosing the tracker has seen only the Cauldron in front of the player. Future Cauldrons are
    /// taken to be of the offer's faction (every map seen held one faction), so a Fate of another faction gets the
    /// tracker's count alone.
    /// </summary>
    public static Func<string, double> OffersToComeFor(FatesPanelState state, int areaLevel, CauldronTracker tracker)
    {
        var mapFaction = state.Offered.FirstOrDefault(static c => c.Fate != null)?.Fate.FactionId;
        var fromMapSize = Math.Max(0d, MapCauldrons.InArea(state.Counter, areaLevel) - CollectedOf(state) - 1);

        return faction =>
        {
            var known = tracker?.OffersToCome(faction, state.ChoosingFate) ?? 0;
            var sameFaction = faction == null || mapFaction == null || faction == mapFaction;

            return Math.Max(known, sameFaction ? fromMapSize : 0d);
        };
    }

    /// <summary>
    /// The Fates collected in the area: the Fate counter's count when the panel shows one (the client's own, the
    /// collection's length), else the collection row's visible cards.
    /// </summary>
    private static int CollectedOf(FatesPanelState state) => state.Counter?.Collected ?? state.Collection.Count;

    /// <summary>
    /// The best offered cards, by the Must take rule when it applies, else by value (FateScore.Value): when any card
    /// has a MustTakeRank above 0, those with the highest rank, ties by value; otherwise those with the highest value.
    /// Several only on an exact tie. The rule, the user's (2026-11-18): with no Must take reward held, take the card
    /// with the weightiest Must take reward; with one held, the card that adds most to the Must take rewards (so with
    /// a Hinekora's Lock held, a second copy of its Fate, which triples the Lock, beats a Mirror Shard on another
    /// Fate); when no card touches a Must take reward, the weights decide as before.
    /// </summary>
    public static List<FateScore> BestOf(IReadOnlyList<FateScore> scores)
    {
        if (scores.Count == 0)
            return [];

        var byRule = scores.Any(static s => s.MustTakeRank > SameValue);
        var pool = scores;

        if (byRule)
        {
            var topRank = scores.Max(static s => s.MustTakeRank);
            pool = scores.Where(s => s.MustTakeRank >= topRank - SameValue).ToList();
        }

        var topValue = pool.Max(static s => s.Value);

        return pool.Where(s => s.Value >= topValue - SameValue).ToList();
    }

    /// <summary>Two values closer than this are equal: the same card's value worked out twice.</summary>
    private const double SameValue = 1e-6;

    public static List<FateScore> Score(FatesPanelState state, AutoAugurySettings settings,
        Func<string, double> offersToCome)
    {
        var scores = new List<FateScore>();
        var slots = InvocationAdvisor.DefaultSlots;
        var bestWithout = InvocationAdvisor.Best(state.Collection, slots, settings).Total;
        var potentialWithout = CopyPotential.Of(state.Collection, slots, settings, offersToCome);
        var mustTakeHeld = state.Collection.Any(c => c.Rewards.Any(r => r.Reward != null &&
                                                                       settings.Rewards.IsMustTake(r.Reward.Family)));
        var mustTakeWithout = mustTakeHeld
            ? InvocationAdvisor.BestMustTakeValue(state.Collection, slots, settings)
            : 0d;

        foreach (var card in state.Offered)
        {
            var with = state.Collection.Append(card).ToList();
            var advice = InvocationAdvisor.Best(with, slots, settings);
            var pick = advice.PickOf(card);
            var gain = advice.Total - bestWithout;
            var potential = CopyPotential.Of(with, slots, settings, offersToCome) - potentialWithout;
            var pairedWith = pick == null
                ? null
                : pick.Empowers ?? pick.ThirdCopyOf ??
                  advice.Picks.FirstOrDefault(p => ReferenceEquals(p.Empowers, card))?.Card;

            var mustTakeRank = mustTakeHeld
                ? InvocationAdvisor.BestMustTakeValue(with, slots, settings) - mustTakeWithout
                : card.Rewards.Where(r => r.Reward != null && settings.Rewards.IsMustTake(r.Reward.Family))
                    .Sum(r => (double)settings.Rewards.WeightOf(r.Reward.Family));

            scores.Add(new FateScore(card, (int)Math.Round(gain + potential), gain, potential, pick != null,
                pairedWith, offersToCome(card.Fate?.FactionId),
                card.Rewards.Count(r => NewRewardRegistry.LacksWeight(r, settings.Rewards)), mustTakeRank));
        }

        return scores;
    }
}
