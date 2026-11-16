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
/// <param name="OffersToCome">The offers still to come from Cauldrons of its faction (CauldronTracker).</param>
/// <param name="UnknownRewards">Rewards shown on the card that matched no catalogue reward, so count nothing.</param>
internal sealed record FateScore(FateCard Card, int Total, int Gain, double Potential, bool Invoked,
    FateCard PairedWith, int OffersToCome, int UnknownRewards)
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
/// </summary>
internal static class FateScoring
{
    /// <summary>
    /// Which Cauldron of the map the player is choosing at and of how many, for "Estimated: 1/3" by the close button:
    /// the Fates collected plus one, of the larger of the map's assumed Cauldrons (MapCauldrons) and this one plus
    /// those the tracker knows still to come.
    /// </summary>
    public static (int Current, int Total) CauldronProgressOf(FatesPanelState state, int areaLevel,
        CauldronTracker tracker)
    {
        var current = state.Collection.Count + 1;
        var mapFaction = state.Offered.FirstOrDefault(static c => c.Fate != null)?.Fate.FactionId;
        var known = tracker?.OffersToCome(mapFaction, true) ?? 0;

        return (current, Math.Max(MapCauldrons.Assumed(areaLevel), current + known));
    }

    /// <summary>
    /// The offers still to come for a Fate's faction while choosing: the larger of the count from the map's size
    /// (MapCauldrons' assumption for the area level, less the Fates collected, less the Cauldron being chosen at) and
    /// the Cauldrons the tracker knows not done. The count from the map's size is what matters in play: Cauldrons are
    /// met one at a time and picked from at once, so when choosing the tracker has seen only the Cauldron in front of
    /// the player. Future Cauldrons are taken to be of the offer's faction (every map seen held one faction), so a Fate
    /// of another faction gets the tracker's count alone.
    /// </summary>
    public static Func<string, int> OffersToComeFor(FatesPanelState state, int areaLevel, CauldronTracker tracker)
    {
        var mapFaction = state.Offered.FirstOrDefault(static c => c.Fate != null)?.Fate.FactionId;
        var fromMapSize = Math.Max(0, MapCauldrons.Assumed(areaLevel) - state.Collection.Count - 1);

        return faction =>
        {
            var known = tracker?.OffersToCome(faction, state.ChoosingFate) ?? 0;
            var sameFaction = faction == null || mapFaction == null || faction == mapFaction;

            return Math.Max(known, sameFaction ? fromMapSize : 0);
        };
    }

    public static List<FateScore> Score(FatesPanelState state, AutoAugurySettings settings,
        Func<string, int> offersToCome)
    {
        var scores = new List<FateScore>();
        var slots = InvocationAdvisor.DefaultSlots;
        var bestWithout = InvocationAdvisor.Best(state.Collection, slots, settings).Total;
        var potentialWithout = CopyPotential.Of(state.Collection, slots, settings, offersToCome);

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

            scores.Add(new FateScore(card, (int)Math.Round(gain + potential), gain, potential, pick != null,
                pairedWith, offersToCome(card.Fate?.FactionId), card.Rewards.Count(static r => r.Reward == null)));
        }

        return scores;
    }
}
