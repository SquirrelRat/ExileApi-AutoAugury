using System;
using System.Collections.Generic;
using System.Linq;

namespace AutoAugury;

/// <summary>
/// What a collection is still worth for the further copies of its Fates that the offers to come could bring: the
/// expected value of completing a pair or a triple, by the player's weights. FateScoring adds the change in it to an
/// offered card's immediate gain, so that pairing a Fate now gives up the chance of pairing it later, and a new Fate
/// brings its own chance, rewards that need two or three copies included (a pair claims both, FateValues).
///
/// For each Fate held once or twice: the chance the offers to come bring one more copy, or two, k offers each with a
/// chance q = 3/N of showing the Fate (N the Fates of its faction, 7, 10 or 8; an offer shows a Fate at most once), k
/// the offers to come from Cauldrons of its faction (FateScoring.OffersToComeFor); and the value of those copies, what
/// they add to the best invocation (InvocationAdvisor, within the slots). A second copy pays nothing of its own, but a
/// third counts its own rewards three times (3.30.0b Hotfix 3), so a copy to come needs rewards: it is taken to carry
/// those of the weakest held copy of its Fate (the least as an empowered copy). Copies of one Fate have shared a
/// reward (three Incandescent, each with three-copy Valuable Currency, 2026-11-14 05:35; two Houndmaster, each with
/// Currency Items, 06:14), but the catalogue ties no reward to a Fate, so the held copies are only a sample; of two,
/// the weaker is taken, so as not to count on the luckier.
///
/// The Fates compete for the same offers and slots, so their expected values are not added: the potential is the
/// expected best, the Fates ordered by their value when they do get a copy, E1 + E2 (1 - P1) + E3 (1 - P1)(1 - P2) ...,
/// with Ei a Fate's expected value and Pi its chance of another copy, taken as independent.
///
/// Assumed, not measured: offers are three Fates of the Cauldron's faction drawn evenly. Not counted: a later card of
/// another Fate carrying the same reward, which adds to its count (counts pool across Fates, FateValues), and a later
/// copy's own rewards, better or worse than the weakest held copy's. With no offers to come the potential is 0 and a
/// card is scored on its immediate gain alone.
/// </summary>
internal static class CopyPotential
{
    /// <summary>Fates per offer: every offer seen showed three (2026-11-14), of five card slots.</summary>
    public const int FatesPerOffer = 3;

    /// <summary>The most copies of one Fate counted: three, the most that can be invoked (3.30.0b Hotfix 3).</summary>
    public const int MostCopies = 3;

    /// <summary>One way a held Fate could gain copies: how many more, what they would add, and the chance.</summary>
    public sealed record CopyOutcome(int More, int Value, double Chance);

    /// <summary>One held Fate's part in the potential, for the dump.</summary>
    public sealed record CopyChance(FateCard Card, int Held, int OffersToCome, IReadOnlyList<CopyOutcome> Outcomes)
    {
        public double Expected => Outcomes.Sum(static o => o.Value * o.Chance);
        public double AnyChance => Outcomes.Sum(static o => o.Chance);
    }

    /// <summary>The chance one offer of its faction shows a given Fate: 3 in the faction's number of Fates.</summary>
    public static double PerOfferChance(AuguryFate fate)
    {
        var factionSize = fate == null ? 0 : AuguryCatalogue.Fates.Count(f => f.FactionId == fate.FactionId);

        return factionSize <= 0 ? 0d : Math.Min(1d, FatesPerOffer / (double)factionSize);
    }

    /// <summary>Each Fate held fewer than MostCopies times, with the copies it could gain and their chances.</summary>
    public static List<CopyChance> ChancesOf(IReadOnlyList<FateCard> collection, int slots, AutoAugurySettings settings,
        Func<string, int> offersToCome)
    {
        var chances = new List<CopyChance>();
        var baseline = InvocationAdvisor.Best(collection, slots, settings).Total;

        foreach (var group in GroupsOf(collection))
        {
            var held = group.Count;
            var card = group.MinBy(c => FateValues.RewardsValue(c, FateValues.EmpoweredMultiple, settings));
            var offers = offersToCome(card.Fate?.FactionId);

            if (held >= MostCopies || offers <= 0)
                continue;

            var perOffer = PerOfferChance(card.Fate);
            var most = Math.Min(MostCopies - held, offers);
            var outcomes = new List<CopyOutcome>();

            for (var more = 1; more <= most; more++)
            {
                // Copies to come carry the weakest held copy's rewards; a second pays nothing, a third its own.
                var copies = Enumerable.Repeat(card with { Rect = default, Placed = false }, more);
                var value = InvocationAdvisor.Best(collection.Concat(copies).ToList(), slots, settings).Total -
                            baseline;
                var chance = more < most
                    ? Exactly(more, offers, perOffer)
                    : AtLeast(more, offers, perOffer);

                outcomes.Add(new CopyOutcome(more, Math.Max(0, value), chance));
            }

            var copyChance = new CopyChance(card, held, offers, outcomes);

            if (copyChance.Expected > 0d)
                chances.Add(copyChance);
        }

        return chances;
    }

    /// <summary>The expected value of the best copies the collection could still gain.</summary>
    public static double Of(IReadOnlyList<FateCard> collection, int slots, AutoAugurySettings settings,
        Func<string, int> offersToCome)
    {
        var expected = 0d;
        var noneBefore = 1d;
        var ordered = ChancesOf(collection, slots, settings, offersToCome)
            .OrderByDescending(static c => c.AnyChance > 0d ? c.Expected / c.AnyChance : 0d);

        foreach (var chance in ordered)
        {
            expected += chance.Expected * noneBefore;
            noneBefore *= 1d - chance.AnyChance;
        }

        return expected;
    }

    private static List<List<FateCard>> GroupsOf(IReadOnlyList<FateCard> cards)
    {
        var groups = new List<List<FateCard>>();

        foreach (var card in cards)
        {
            var group = groups.FirstOrDefault(g => FateValues.SameFate(g[0], card));

            if (group == null)
                groups.Add([card]);
            else
                group.Add(card);
        }

        return groups;
    }

    /// <summary>The chance of exactly j successes in n tries of chance p.</summary>
    private static double Exactly(int j, int n, double p) =>
        Choose(n, j) * Math.Pow(p, j) * Math.Pow(1d - p, n - j);

    /// <summary>The chance of at least j successes in n tries of chance p.</summary>
    private static double AtLeast(int j, int n, double p)
    {
        var sum = 0d;

        for (var i = j; i <= n; i++)
            sum += Exactly(i, n, p);

        return sum;
    }

    private static double Choose(int n, int k)
    {
        var result = 1d;

        for (var i = 1; i <= k; i++)
            result = result * (n - k + i) / i;

        return result;
    }
}
