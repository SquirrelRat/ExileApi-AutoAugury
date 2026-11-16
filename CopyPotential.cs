using System;
using System.Collections.Generic;
using System.Linq;

namespace AutoAugury;

/// <summary>
/// What a collection is still worth for the second copies of its Fates that the offers to come could bring: the
/// expected value of completing a pair, by the player's weights. FateScoring adds the change in it to an offered card's
/// immediate gain, so that pairing a Fate now gives up the chance of pairing it later, and a new Fate brings its own
/// chance, rewards that need two or three copies included (a pair claims both, FateValues).
///
/// For each Fate held once: the chance the offers to come bring at least one more copy, k offers each with a chance
/// q = 3/N of showing the Fate (N the Fates of its faction, 7, 10 or 8; an offer shows a Fate at most once), k the
/// offers to come from Cauldrons of its faction (FateScoring.OffersToComeFor); and the value of that copy, what it adds
/// to the best invocation (InvocationAdvisor with a copy that has no rewards of its own, since a copy that empowers
/// pays nothing; so the first copy's rewards counted three times, within the slots). Any Fate copy counts, whatever its
/// rewards. A third copy adds nothing (the boss's summary did not change, 2026-11-14 05:35), so a Fate held twice has
/// no potential.
///
/// The Fates compete for the same offers and slots, so their expected values are not added: the potential is the
/// expected best, the Fates ordered by their value when they do get a copy, E1 + E2 (1 - P1) + E3 (1 - P1)(1 - P2) ...,
/// with Ei a Fate's expected value and Pi its chance of another copy, taken as independent.
///
/// Assumed, not measured: offers are three Fates of the Cauldron's faction drawn evenly. Not counted: a later card of
/// another Fate carrying the same reward, which adds to its count (counts pool across Fates, FateValues), and a later
/// copy with better rewards than the held one placed first instead. With no offers to come the potential is 0 and a
/// card is scored on its immediate gain alone.
/// </summary>
internal static class CopyPotential
{
    /// <summary>Fates per offer when nothing overrides it: every offer seen showed three (2026-11-14).</summary>
    public const int FatesPerOffer = 3;

    /// <summary>The most copies of one Fate counted: two, as a third copy adds nothing (FateValues).</summary>
    public const int MostCopies = 2;

    /// <summary>One way a held Fate could gain copies: how many more, what they would add, and the chance.</summary>
    public sealed record CopyOutcome(int More, int Value, double Chance);

    /// <summary>One held Fate's part in the potential, for the dump.</summary>
    public sealed record CopyChance(FateCard Card, int Held, int OffersToCome, IReadOnlyList<CopyOutcome> Outcomes)
    {
        public double Expected => Outcomes.Sum(static o => o.Value * o.Chance);
        public double AnyChance => Outcomes.Sum(static o => o.Chance);
    }

    /// <summary>The chance one offer of its faction shows a given Fate: the offer's Fates in the faction's number.</summary>
    public static double PerOfferChance(AuguryFate fate, int fatesPerOffer)
    {
        var factionSize = fate == null ? 0 : AuguryCatalogue.Fates.Count(f => f.FactionId == fate.FactionId);

        return factionSize <= 0 || fatesPerOffer <= 0
            ? 0d
            : Math.Min(1d, fatesPerOffer / (double)factionSize);
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
            var card = group[0];
            var offers = offersToCome(card.Fate?.FactionId);

            if (held >= MostCopies || offers <= 0)
                continue;

            var perOffer = PerOfferChance(card.Fate, settings.Debug.FatesPerOffer.Value);
            var most = Math.Min(MostCopies - held, offers);
            var outcomes = new List<CopyOutcome>();

            for (var more = 1; more <= most; more++)
            {
                // Further copies empower the first and pay nothing of their own, so their rewards do not matter.
                var copies = Enumerable.Repeat(card with { Rewards = [], Rect = default }, more);
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
