using System;
using System.Collections.Generic;
using System.Linq;

namespace AutoAugury;

/// <summary>
/// One Fate of an invocation: the card, its place in the order, the earlier copy it empowers or joins as a third copy.
/// </summary>
/// <param name="Card">The card to place.</param>
/// <param name="Order">Its place in the order to place the cards in, from 1.</param>
/// <param name="Empowers">
/// For a Fate's second copy, which pays nothing of its own, the first copy it empowers; null otherwise.
/// </param>
/// <param name="Value">
/// What its Fate adds to the invocation (the invocation's value less its value without the Fate), on the Fate's first
/// copy; 0 on its other copies.
/// </param>
/// <param name="ThirdCopyOf">
/// For a Fate's third copy, whose own rewards are tripled too (FateValues), the Fate's first copy; null otherwise.
/// </param>
internal sealed record InvocationPick(FateCard Card, int Order, FateCard Empowers, int Value,
    FateCard ThirdCopyOf = null);

/// <summary>
/// The cards that could be invoked at the boss, those of them already placed (in slot order), and whether which
/// collected copy a slot holds had to be guessed.
/// </summary>
internal sealed record InvocationCandidates(IReadOnlyList<FateCard> Cards, IReadOnlyList<FateCard> Placed,
    bool PlacedCopyUncertain);

/// <summary>What the overlay shows at the boss, worked out from the advice and what is placed.</summary>
/// <param name="Advice">The best invocation around the placed cards.</param>
/// <param name="Next">
/// The pick to place now: while the remaining order is free, the one that adds the most; otherwise the advice's next.
/// Null when all are placed.
/// </param>
/// <param name="Gains">What each pick adds when placed in the advice's order (placed ones first, as placed).</param>
/// <param name="Cost">How much less the placed cards allow than the best invocation without them; 0 when none.</param>
/// <param name="WrongOrder">
/// Whether the cost comes from order alone: every placed card is in the best invocation, but copies of one Fate went
/// in the wrong order. When false and Cost is above 0, a placed card is not among the best Fates.
/// </param>
/// <param name="AnyOrder">Whether the order does not matter: no two picks are copies of one Fate.</param>
/// <param name="Remaining">The picks not yet placed, in the advice's order.</param>
/// <param name="AnyOrderLeft">
/// Whether the remaining picks can go in any order: no two of them are copies of one Fate, whose order changes what
/// each copy pays.
/// </param>
internal sealed record InvocationGuide(InvocationAdvice Advice, InvocationPick Next, IReadOnlyList<int> Gains, int Cost,
    bool WrongOrder, bool AnyOrder, IReadOnlyList<InvocationPick> Remaining, bool AnyOrderLeft)
{
    public static readonly InvocationGuide None = new(InvocationAdvice.None, null, [], 0, false, true, [], true);

    /// <summary>What the pick adds when placed in the advice's order; 0 for a card not in it.</summary>
    public int GainOf(InvocationPick pick) =>
        pick == null || pick.Order < 1 || pick.Order > Gains.Count ? 0 : Gains[pick.Order - 1];

    /// <summary>Whether the pick is placed already.</summary>
    public bool IsPlaced(InvocationPick pick) => !Remaining.Any(r => ReferenceEquals(r, pick));
}

/// <summary>The best invocation found: its picks in placing order, its value, the slots it was chosen for.</summary>
internal sealed record InvocationAdvice(IReadOnlyList<InvocationPick> Picks, int Total, int Slots)
{
    public static readonly InvocationAdvice None = new([], 0, 0);

    /// <summary>The pick for a card, or null when the card is not in the invocation.</summary>
    public InvocationPick PickOf(FateCard card) => Picks.FirstOrDefault(p => ReferenceEquals(p.Card, card));
}

/// <summary>
/// Chooses the Fates to invoke on the area's boss: the set of at most Slots cards, from the collection, worth the most
/// by the player's weights, and the order to place them in.
///
/// The value of an invocation is FateValues.InvocationValue: each Fate counts its first copy's rewards, three times
/// over when a second copy empowers it, the second copy paying nothing (the panel: "This Fate slot is being used to
/// empower a previously played Fate, tripling the rewards granted by it and upgrading its tier, but nullifying the
/// rewards of the Fate in this slot"), and a third copy counting its own rewards three times too (3.30.0b Hotfix 3); a
/// reward is claimed once per the copies it needs. So of a Fate's copies the better go first and third and the weakest
/// second, which is why the order matters; the order between different Fates does not, so they are placed by what
/// they add. A Fate whose weight makes it worth less than nothing is left out, leaving a slot empty.
///
/// Cards already placed are fixed: they are in every set tried, they come first in the order, in slot order, and a
/// Fate's placed copies keep their places among its copies (the first placed is the one the second empowers), whatever
/// their values, as in the game. So the advice fills the remaining slots around what is there, on plan or not.
///
/// Every subset is tried: a collection holds a few Fates per Cauldron of the area (five Cauldrons at most, per the
/// guides), so at most a few hundred subsets.
/// </summary>
internal static class InvocationAdvisor
{
    /// <summary>
    /// The invocation slots when the panel does not show them (while choosing): three, as the boss's view showed at
    /// 2026-11-14 03:38. Whether atlas passives or anything else add slots is not known.
    /// </summary>
    public const int DefaultSlots = 3;

    /// <summary>The most cards tried, so the subsets stay few; a collection has never come near it.</summary>
    private const int MostCards = 16;

    /// <summary>
    /// The cards that could be invoked at the boss, and which of them are placed, in slot order: each card in a slot
    /// taken to be a collected copy of its Fate.
    ///
    /// A placed card stays in the collection row, greyed (FateCard.Placed), and in its slot it shows its name but not
    /// its rewards (dump of 2026-11-14 04:26: Crowkeeper placed, its slot's reward row hidden, Crowkeeper still listed
    /// below). So a slot is matched to a collected copy of the same Fate not already taken by an earlier slot: the one
    /// with the same reward texts if the slot shows any, else a greyed one. Matching on reward texts alone took the
    /// slot for a second Crowkeeper, and advised a pair that does not exist ("wrong order: -40"); guessing between
    /// copies without the grey took the wrong Houndmaster at 06:14 and framed the copy already in a slot. When two
    /// greyed copies of one Fate have different rewards, which went in first cannot be told; PlacedCopyUncertain says
    /// so, and each slot takes the copy the advice with nothing placed puts earliest in its order (failing that, the
    /// most rewards as an empowered first copy), on the guess that the advice was followed. Without greyed copies (the
    /// flag unread) every copy is a candidate. A slot that shows rewards matching no collected copy is kept as a card
    /// of its own.
    /// </summary>
    public static InvocationCandidates CandidatesOf(FatesPanelState state, AutoAugurySettings settings = null)
    {
        var cards = state.Collection.ToList();
        var placed = new List<FateCard>();
        var uncertain = false;

        // The advice with nothing placed, for guessing which copy a slot holds: the one it put earliest.
        var planned = settings == null
            ? InvocationAdvice.None
            : Best(cards, state.TopRowSlots.Count > 0 ? state.TopRowSlots.Count : DefaultSlots, settings);

        foreach (var card in state.Offered)
        {
            var free = cards
                .Where(c => !placed.Any(p => ReferenceEquals(p, c)) && FateValues.SameFate(c, card))
                .ToList();
            var collected = free.FirstOrDefault(c => SameCard(c, card));

            if (collected == null && card.Rewards.Count == 0 && free.Count > 0)
            {
                var greyed = free.Where(static c => c.Placed).ToList();
                var candidates = greyed.Count > 0 ? greyed : free;

                collected = settings == null
                    ? candidates[0]
                    : candidates.Where(c => planned.PickOf(c) != null).MinBy(c => planned.PickOf(c).Order) ??
                      candidates.MaxBy(c => FateValues.RewardsValue(c, FateValues.EmpoweredMultiple, settings));
                uncertain |= candidates.Any(c => !SameRewards(c, collected));
            }

            // A slot not matched to the collection: kept as a card of its own.
            if (collected == null)
            {
                cards.Add(card);
                collected = card;
            }

            placed.Add(collected);
        }

        return new InvocationCandidates(cards, placed, uncertain);
    }

    /// <summary>
    /// The guide for the boss's view: the advice around what is placed, for as many slots as the panel shows; the pick
    /// to place now; what each pick adds in order; and what the placed cards cost against the best invocation.
    /// </summary>
    public static InvocationGuide GuideFor(FatesPanelState state, AutoAugurySettings settings)
    {
        var slots = state.TopRowSlots.Count > 0 ? state.TopRowSlots.Count : DefaultSlots;
        var candidates = CandidatesOf(state, settings);
        var advice = Best(candidates.Cards, slots, settings, candidates.Placed);
        var cost = 0;
        var wrongOrder = false;

        // No warning when which copy went in cannot be told: it could be a false one.
        if (candidates.Placed.Count > 0 && !candidates.PlacedCopyUncertain)
        {
            var unconstrained = Best(candidates.Cards, slots, settings);

            cost = Math.Max(0, unconstrained.Total - advice.Total);
            wrongOrder = cost > 0 &&
                         candidates.Placed.All(p => unconstrained.Picks.Any(u => ReferenceEquals(u.Card, p)));
        }

        var remaining = advice.Picks.Where(p => !candidates.Placed.Any(c => ReferenceEquals(c, p.Card))).ToList();
        var anyOrderLeft = !remaining.Any(p =>
            remaining.Any(r => !ReferenceEquals(r, p) && FateValues.SameFate(r.Card, p.Card)));
        var gains = GainsOf(advice.Picks.Select(static p => p.Card).ToList(), settings);

        // While the order is free, the next card is the one that adds the most (the first on a tie); while two copies
        // of one Fate are still to place, the advice's order decides.
        var next = anyOrderLeft
            ? remaining.MaxBy(p => p.Order >= 1 && p.Order <= gains.Count ? gains[p.Order - 1] : 0)
            : remaining.FirstOrDefault();

        return new InvocationGuide(advice, next, gains, cost, wrongOrder,
            advice.Picks.All(static p => p.Empowers == null), remaining, anyOrderLeft);
    }

    /// <summary>
    /// What each card adds when placed in the given order: the value of the cards up to it less the value of those
    /// before it, a Fate's copies counting in the order given (FateGroup), as in the game.
    /// </summary>
    public static List<int> GainsOf(IReadOnlyList<FateCard> order, AutoAugurySettings settings)
    {
        var gains = new List<int>();
        var before = 0;

        for (var i = 1; i <= order.Count; i++)
        {
            var upTo = FateValues.InvocationValue(
                GroupsOf(order.Take(i).ToList()).Select(static g => new FateGroup(g)), settings);

            gains.Add(upTo - before);
            before = upTo;
        }

        return gains;
    }

    /// <summary>
    /// The best invocation of at most slots cards from cards, keeping the placed cards (which must be among cards).
    /// </summary>
    public static InvocationAdvice Best(IReadOnlyList<FateCard> cards, int slots, AutoAugurySettings settings,
        IReadOnlyList<FateCard> placed = null)
    {
        placed ??= [];

        if (cards.Count == 0 || slots <= 0)
            return new InvocationAdvice([], 0, slots);

        // The placed cards first, so they are always among those tried and their bits are the low ones.
        var tried = placed
            .Concat(cards.Where(c => !placed.Any(p => ReferenceEquals(p, c)))
                .OrderByDescending(c => FateValues.RewardsValue(c, 1, settings)))
            .Take(MostCards)
            .ToList();
        var placedMask = (1 << Math.Min(placed.Count, tried.Count)) - 1;
        var best = ValueOf(Subset(tried, placedMask), settings, placed);

        for (var mask = placedMask + 1; mask < 1 << tried.Count; mask++)
        {
            if ((mask & placedMask) != placedMask || int.PopCount(mask) > slots)
                continue;

            var candidate = ValueOf(Subset(tried, mask), settings, placed);

            // Strictly better only, so of two equal sets the one found first stays: a subset comes before its
            // supersets, so a copy that adds nothing is left out.
            if (candidate.Value > best.Value)
                best = candidate;
        }

        return new InvocationAdvice(PicksOf(best.Groups, settings, placed), best.Value, slots);
    }

    /// <summary>
    /// The chosen cards' value and their Fates, each with its copies in placing order: the placed copies first, in slot
    /// order, as in the game; the rest in whichever order makes the invocation worth the most, every combination tried
    /// (with three slots, one Fate at most has more than one copy, so six orders at most).
    /// </summary>
    private static (int Value, List<FateGroup> Groups) ValueOf(List<FateCard> chosen, AutoAugurySettings settings,
        IReadOnlyList<FateCard> placed)
    {
        var orders = GroupsOf(chosen).Select(g => PlacingOrdersOf(g, placed)).ToList();
        var choice = new int[orders.Count];
        (int Value, List<FateGroup> Groups) best = (int.MinValue, null);

        while (true)
        {
            var groups = orders.Select((o, i) => new FateGroup(o[choice[i]])).ToList();
            var value = FateValues.InvocationValue(groups, settings);

            if (value > best.Value)
                best = (value, groups);

            // The next combination of first copies, as an odometer; done when it wraps.
            var digit = 0;

            while (digit < choice.Length && ++choice[digit] == orders[digit].Count)
                choice[digit++] = 0;

            if (digit == choice.Length)
                return best;
        }
    }

    /// <summary>
    /// The orders a Fate's copies could be placed in: its placed copies first, in slot order, then every order of the
    /// rest.
    /// </summary>
    private static List<List<FateCard>> PlacingOrdersOf(List<FateCard> group, IReadOnlyList<FateCard> placed)
    {
        var placedCopies = placed.Where(p => group.Any(c => ReferenceEquals(c, p))).ToList();
        var rest = group.Where(c => !placedCopies.Any(p => ReferenceEquals(p, c))).ToList();

        return PermutationsOf(rest).Select(p => placedCopies.Concat(p).ToList()).ToList();
    }

    /// <summary>Every order of the cards; one empty order for no cards.</summary>
    private static IEnumerable<List<FateCard>> PermutationsOf(List<FateCard> cards)
    {
        if (cards.Count <= 1)
        {
            yield return cards.ToList();
            yield break;
        }

        for (var i = 0; i < cards.Count; i++)
        {
            var others = cards.Where((_, j) => j != i).ToList();

            foreach (var tail in PermutationsOf(others))
                yield return [cards[i], .. tail];
        }
    }

    /// <summary>
    /// The chosen cards in placing order: the placed ones first, in slot order, then the other Fates by value, each
    /// Fate's copies in its group's order (first, the second that empowers it, the third).
    /// </summary>
    private static List<InvocationPick> PicksOf(List<FateGroup> groups, AutoAugurySettings settings,
        IReadOnlyList<FateCard> placed)
    {
        var picks = new List<InvocationPick>();
        var total = FateValues.InvocationValue(groups, settings);
        var added = groups
            .Select(g => (Group: g, Adds: total - FateValues.InvocationValue(groups.Where(o => o != g), settings)))
            .ToList();

        void Add(FateCard card)
        {
            var (group, adds) = added.First(a => a.Group.Copies.Any(c => ReferenceEquals(c, card)));
            var isFirst = ReferenceEquals(card, group.First);
            var isThird = ReferenceEquals(card, group.Third);

            picks.Add(new InvocationPick(card, picks.Count + 1, isFirst || isThird ? null : group.First,
                isFirst ? adds : 0, isThird ? group.First : null));
        }

        foreach (var card in placed.Where(p => groups.Any(g => g.Copies.Any(c => ReferenceEquals(c, p)))))
            Add(card);

        foreach (var (group, _) in added.OrderByDescending(static a => a.Adds))
        {
            foreach (var card in group.Copies)
            {
                if (!picks.Any(p => ReferenceEquals(p.Card, card)))
                    Add(card);
            }
        }

        return picks;
    }

    private static List<List<FateCard>> GroupsOf(List<FateCard> cards)
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

    private static List<FateCard> Subset(List<FateCard> cards, int mask)
    {
        var subset = new List<FateCard>();

        for (var i = 0; i < cards.Count; i++)
        {
            if ((mask & 1 << i) != 0)
                subset.Add(cards[i]);
        }

        return subset;
    }

    private static bool SameCard(FateCard a, FateCard b) =>
        string.Equals(a.Name, b.Name, StringComparison.OrdinalIgnoreCase) && SameRewards(a, b);

    private static bool SameRewards(FateCard a, FateCard b) =>
        a.Rewards.Select(static r => r.Text).SequenceEqual(b.Rewards.Select(static r => r.Text));
}
