using System;
using System.Collections.Generic;
using System.Diagnostics;
using ExileCore;
using ExileCore.PoEMemory;
using RectangleF = SharpDX.RectangleF;

namespace AutoAugury;

/// <summary>A reward icon on a card: the text the panel holds for it, the catalogue reward it is, its copies.</summary>
/// <param name="Text">The reward's text as the panel holds it, markup included.</param>
/// <param name="Reward">The catalogue reward the text is, or null when no catalogue reward has that text.</param>
/// <param name="CopiesNeeded">
/// How many copies of the Fate must be invoked for the reward: 1; 2 when the panel adds "To claim this reward, invoke
/// two copies of it"; 3 for "three copies".
/// </param>
internal sealed record FateCardReward(string Text, AuguryReward Reward, int CopiesNeeded);

/// <summary>A card in the Fates panel.</summary>
/// <param name="Name">The name the card shows.</param>
/// <param name="Fate">The catalogue Fate of that name, or null when none has it.</param>
/// <param name="Tier">
/// The tier, from the card's full text, which differs by tier for 18 of the 25 Fates; null when the text is the same at
/// every tier or matches none. The panel's own tier number is a hidden element that read "1" on tier 2 and 3 cards.
/// </param>
/// <param name="Rect">
/// The card's rectangle in client pixels; the hovered card's content moves up 13 px, this does not.
/// </param>
/// <param name="Rewards">The card's visible reward icons, in order.</param>
/// <param name="Placed">
/// Whether the card's element reads not active (Element.IsActive false), which at the boss marks a collected copy that
/// is in an invocation slot: in every boss dump of 2026-11-14 the collection cards reading not active were exactly the
/// copies placed (one Crowkeeper of three cards at 04:26, one Incandescent of two at 04:36, two then all three
/// Incandescent at 05:35, the Houndmaster with Item Rarity of two at 06:14), and with nothing placed every card read
/// active. Every card read active while choosing, and every slot card at the boss.
/// </param>
internal sealed record FateCard(string Name, AuguryFate Fate, int? Tier, RectangleF Rect,
    IReadOnlyList<FateCardReward> Rewards, bool Placed = false);

/// <summary>What FatesPanel read: whether the panel is open, in which view, and the cards of its two rows.</summary>
/// <param name="Visible">Whether the panel is open.</param>
/// <param name="Title">The title: "Fates" while choosing, "Invoke Fates" at the boss; empty when closed.</param>
/// <param name="Offered">
/// The top row's visible cards: the Fates on offer while choosing; at the boss, the Fates placed in the invocation
/// slots (an empty slot has no name and is left out).
/// </param>
/// <param name="Collection">The bottom row's visible cards: the area's collection, in both views.</param>
/// <param name="TopRowSlots">
/// The rectangles of the top row's visible card elements, named or not: at the boss, the invocation slots, which an
/// empty slot shows as a card frame with its name hidden (three at the boss of 2026-11-14 03:38).
/// </param>
/// <param name="ConfirmButton">
/// The confirm button's rectangle (166x38 below the top row in both views on 2026-11-19), found by its "confirm"
/// label; empty when it is not visible.
/// </param>
/// <param name="CloseButton">
/// The close button's rectangle (the panel's child [3], 31x31 at its top right on 2026-11-19); empty when it is not
/// visible.
/// </param>
internal sealed record FatesPanelState(bool Visible, string Title, IReadOnlyList<FateCard> Offered,
    IReadOnlyList<FateCard> Collection, IReadOnlyList<RectangleF> TopRowSlots, RectangleF ConfirmButton,
    RectangleF CloseButton)
{
    public const string ChoosingTitle = "Fates";
    public const string InvokingTitle = "Invoke Fates";

    public static readonly FatesPanelState Closed = new(false, "", [], [], [], default, default);

    /// <summary>
    /// The game's own count of the area's Fates, from the "0/3" element 3.30.0d added to the panel body: how many are
    /// collected, and how many the area offers. Read as ground truth where the tracker would estimate the same
    /// (FateScoring.CauldronProgressOf). Null when the element is not there or does not read.
    /// </summary>
    public (int Collected, int Available)? FatesCounter { get; init; }

    /// <summary>Whether the panel offers a Cauldron's Fates to choose from.</summary>
    public bool ChoosingFate => Visible && Title == ChoosingTitle;

    /// <summary>Whether the panel is the boss's invocation view.</summary>
    public bool InvokingFates => Visible && Title == InvokingTitle;
}

/// <summary>
/// Reads the Fates panel, the Augury window in which a Cauldron's Fates are chosen and, at the area's boss, the
/// collection's Fates are invoked, as the game shows them. Only visible elements count: the panel reuses its elements,
/// and hidden ones keep earlier cards' names and rewards (dumps of 2026-11-14).
///
/// The layout, read off the dumps of 2026-11-14 (the panel while choosing, and at the boss) and 2026-11-19 (choosing,
/// after the 3.30.x patch): the panel is a child of IngameUi whose index moves as other panels open, recognised by its
/// title, child 2's child 3: "Fates" while choosing, "Invoke Fates" at the boss. Below child 2 the card rows are found
/// by the cards they hold, not by their index (CardRowsOf): the first is the top row (the offer, or the invocation
/// slots at the boss), the second the collection. The 2026-11-19 patch added a copies counter ("0/3") at [5] and moved
/// the rows from [5,0] and [6,0] to [6,0] and [7,0], the summary to [8] and the confirm button to [10]; finding them
/// by shape takes such an insertion without a change. A card k's content is [k,0,0]: [4] its name, [8,0] its reward
/// icons, each icon's child [3] the reward's text; the card element's Tooltip holds its full text.
/// </summary>
internal sealed class FatesPanel
{
    /// <summary>
    /// How often the panel is looked for among IngameUi's children while it has not been found, in ms. Chosen: the
    /// panel may not exist until a Cauldron is first opened, and looking means reading every child's title.
    /// </summary>
    private const int LookIntervalMs = 1000;

    /// <summary>The most text read; Element.Text stops at about 250 characters, short of a Fate's full text.</summary>
    private const int MostTextLength = 4096;

    private Element _panel;

    /// <summary>Not started, so the first read looks at once; restarted by every look after.</summary>
    private readonly Stopwatch _sinceLook = new();

    /// <summary>Forgets the panel, so the next read looks for it again. For an area change.</summary>
    public void Forget() => _panel = null;

    public FatesPanelState Read(GameController gc)
    {
        var panel = PanelOf(gc);

        if (panel == null || !Safe.Read(panel, static p => p.IsVisible, false))
            return FatesPanelState.Closed;

        var body = ChildAt(panel, 2);
        var rows = CardRowsOf(body);

        var offered = new List<FateCard>();
        var slots = new List<RectangleF>();
        var collection = new List<FateCard>();

        if (rows.Count > 0)
            (offered, slots) = CardsOf(rows[0]);

        if (rows.Count > 1)
            (collection, _) = CardsOf(rows[1]);

        return new FatesPanelState(true, TitleOf(panel), offered, collection, slots,
            VisibleRectOf(ConfirmButtonOf(body)), VisibleRectOf(ChildAt(panel, 3)))
        {
            FatesCounter = CounterOf(body),
        };
    }

    /// <summary>
    /// The game's own "collected/available" Fates count, the "0/3" element 3.30.0d added to the body: the body child
    /// whose own child reads two numbers around a slash. Null when none does (an older client, or a moved element).
    /// </summary>
    private static (int Collected, int Available)? CounterOf(Element body)
    {
        foreach (var holder in Safe.ChildrenOf(body) ?? [])
        {
            var text = Safe.Read(ChildAt(holder, 0), static e => e?.Text, null);

            if (text == null)
                continue;

            var parts = text.Trim().Split('/');

            if (parts.Length == 2 && int.TryParse(parts[0].Trim(), out var collected) &&
                int.TryParse(parts[1].Trim(), out var available))
                return (collected, available);
        }

        return null;
    }

    /// <summary>
    /// The panel's card rows, top down: a body child holding a row of cards, the offer (or the invocation slots at the
    /// boss) first, the collection second. Found by the cards' shape rather than by index, so a patch that inserts an
    /// element above them (as 2026-11-19 did with the copies counter) moves nothing the reader depends on. At most two
    /// are taken, which also bounds the scan to the rows in practice.
    /// </summary>
    private static List<Element> CardRowsOf(Element body)
    {
        var rows = new List<Element>();

        foreach (var holder in Safe.ChildrenOf(body) ?? [])
        {
            var row = ChildAt(holder, 0);

            if (row == null || !IsCardRow(row))
                continue;

            rows.Add(row);

            if (rows.Count == 2)
                break;
        }

        return rows;
    }

    /// <summary>
    /// Whether an element holds cards: one child whose child 0,0 has a card's content — 9 or more children, a name at
    /// [4] and the reward icons at [8]. The rows' leftover cards count, hidden or not, as the game keeps them.
    /// </summary>
    private static bool IsCardRow(Element row)
    {
        foreach (var card in Safe.ChildrenOf(row) ?? [])
        {
            var content = ChildAt(card, 0, 0);

            if (content == null || (Safe.ChildrenOf(content)?.Count ?? 0) < 9)
                continue;

            if (ChildAt(content, 4) != null && ChildAt(content, 8) != null)
                return true;
        }

        return false;
    }

    /// <summary>
    /// The confirm button: the body's visible child whose own child 0 reads "confirm", the button's label. Empty when
    /// none does, and the invocation guide's hint goes under the slots instead of beside the button.
    /// </summary>
    private static Element ConfirmButtonOf(Element body)
    {
        foreach (var child in Safe.ChildrenOf(body) ?? [])
        {
            if (!Safe.Read(child, static c => c.IsVisible, false))
                continue;

            var text = Safe.Read(ChildAt(child, 0), static l => l?.Text, null);

            if (string.Equals(text?.Trim(), "confirm", StringComparison.OrdinalIgnoreCase))
                return child;
        }

        return null;
    }

    /// <summary>An element's rectangle while it is visible; empty otherwise.</summary>
    private static RectangleF VisibleRectOf(Element element)
    {
        return Safe.Read(element, static e => e?.IsVisible ?? false, false)
            ? Safe.Read(element, static e => e.GetClientRectCache, default(RectangleF))
            : default;
    }

    /// <summary>The panel: the one found before while it still has the title, or a new look among IngameUi's.</summary>
    private Element PanelOf(GameController gc)
    {
        if (_panel != null && IsFatesPanel(_panel))
            return _panel;

        _panel = null;

        if (_sinceLook.IsRunning && _sinceLook.ElapsedMilliseconds < LookIntervalMs)
            return null;

        _sinceLook.Restart();

        var ingameUi = Safe.Read(gc, static g => (Element)g.IngameState.IngameUi, null);

        foreach (var child in Safe.ChildrenOf(ingameUi) ?? [])
        {
            if (!IsFatesPanel(child))
                continue;

            _panel = child;
            break;
        }

        return _panel;
    }

    /// <summary>Whether an element is the Fates panel: its title reads "Fates" or "Invoke Fates".</summary>
    public static bool IsFatesPanel(Element panel) =>
        TitleOf(panel) is FatesPanelState.ChoosingTitle or FatesPanelState.InvokingTitle;

    /// <summary>
    /// Whether an open panel read nothing: the panel always shows card frames, so an open read with no slots and no
    /// cards means the client's layout moved and the indices no longer land. The Tick warns on repeats of this.
    /// </summary>
    public static bool ReadsEmpty(FatesPanelState state) =>
        state.Visible && state.TopRowSlots.Count == 0 && state.Offered.Count == 0 &&
        state.Collection.Count == 0;

    /// <summary>
    /// Whether the panel offers Fates to choose from but no offered card read: the offer row was not found, which is
    /// what a layout change does (2026-11-19: a counter inserted above the rows left the cards reading as the
    /// collection, so ReadsEmpty stayed quiet while the overlay drew nothing).
    /// </summary>
    public static bool ReadsNoOffers(FatesPanelState state) =>
        state.ChoosingFate && state.Offered.Count == 0;

    /// <summary>The text of child 2's child 3, where the Fates panel has its title; null when there is none.</summary>
    private static string TitleOf(Element panel) => Safe.Read(ChildAt(panel, 2, 3), static t => t?.Text, null);

    /// <summary>
    /// A row's cards: every visible card element's rectangle (slots), and a FateCard for each whose name element is
    /// visible and named. An empty invocation slot is a visible card element with its name element hidden.
    /// </summary>
    private static (List<FateCard> Cards, List<RectangleF> Slots) CardsOf(Element row)
    {
        var cards = new List<FateCard>();
        var slots = new List<RectangleF>();

        if (!Safe.Read(row, static r => r?.IsVisible ?? false, false))
            return (cards, slots);

        foreach (var card in Safe.ChildrenOf(row) ?? [])
        {
            if (!Safe.Read(card, static c => c.IsVisible, false))
                continue;

            var rect = Safe.Read(card, static c => c.GetClientRectCache, default(RectangleF));

            slots.Add(rect);

            var content = ChildAt(card, 0, 0);
            var nameElement = ChildAt(content, 4);
            var name = Safe.Read(nameElement, static n => n?.IsVisible ?? false, false) ? TextOf(nameElement) : null;

            if (string.IsNullOrWhiteSpace(name))
                continue;

            var fate = AuguryCatalogueLookup.FateNamed(name);
            var fullText = TextOf(Safe.Read(card, static c => c.Tooltip, null));

            cards.Add(new FateCard(name.Trim(), fate, AuguryCatalogueLookup.TierOf(fate, fullText), rect,
                RewardsOf(ChildAt(content, 8, 0)), !Safe.Read(card, static c => c.IsActive, true)));
        }

        return (cards, slots);
    }

    private static List<FateCardReward> RewardsOf(Element icons)
    {
        var rewards = new List<FateCardReward>();

        foreach (var icon in Safe.ChildrenOf(icons) ?? [])
        {
            if (!Safe.Read(icon, static i => i.IsVisible, false))
                continue;

            var text = TextOf(ChildAt(icon, 3));

            if (string.IsNullOrWhiteSpace(text))
                continue;

            var copies = AuguryCatalogueLookup.CopiesNeededOf(text);

            rewards.Add(new FateCardReward(text, AuguryCatalogueLookup.RewardShown(text, copies), copies));
        }

        return rewards;
    }

    private static string TextOf(Element element) =>
        Safe.Read(element, static e => e?.GetText(MostTextLength), null);

    /// <summary>The element at a path of child indices below element, or null when a step is missing.</summary>
    private static Element ChildAt(Element element, params int[] path)
    {
        var at = element;

        foreach (var index in path)
        {
            var children = Safe.ChildrenOf(at);

            if (children == null || index >= children.Count)
                return null;

            at = children[index];
        }

        return at;
    }
}
