using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using ExileCore;
using ExileCore.PoEMemory;
using ExileCore.PoEMemory.Elements;
using RectangleF = SharpDX.RectangleF;

namespace AutoAugury;

/// <summary>
/// A reward icon on a card: the text the panel holds for it, the catalogue reward it is, its copies, where it is.
/// </summary>
/// <param name="Text">The reward's text as the panel holds it, markup included.</param>
/// <param name="Reward">The catalogue reward the text is, or null when no catalogue reward has that text.</param>
/// <param name="CopiesNeeded">
/// How many copies of the Fate must be invoked for the reward: 1; 2 when the panel adds "To claim this reward, invoke
/// two copies of it"; 3 for "three copies".
/// </param>
/// <param name="Rect">
/// The icon's rectangle in client pixels where it sits while its card is not hovered (51x48 since 3.30.0c made the
/// panel larger, 40x37 before): the hovered card's content, icons included, is lifted (FatesPanelState.HoverLift), and
/// the lift read with the icon is taken out, so the rectangle stays put between reads. Empty when not read.
/// </param>
internal sealed record FateCardReward(string Text, AuguryReward Reward, int CopiesNeeded, RectangleF Rect = default);

/// <summary>A card in the Fates panel.</summary>
/// <param name="Name">The name the card shows.</param>
/// <param name="Fate">The catalogue Fate of that name, or null when none has it.</param>
/// <param name="Tier">
/// The tier, from the card's full text, which differs by tier for 18 of the 25 Fates; for the others, and for a text
/// a patch changed, the area's tier read off another card (FatesPanel.WithAreaTier); null when neither is known. The
/// panel's own tier number is a hidden element that read "1" on tier 2 and 3 cards.
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
/// The confirm button's rectangle ([2,10], below the top row in both views; 221x52 while choosing since 3.30.0c, 193x45
/// before); empty when it is not visible.
/// </param>
/// <param name="CloseButton">
/// The close button's rectangle ([3], 37x37 at the panel's top right in both views); empty when it is not visible.
/// </param>
/// <param name="HoverLift">
/// How far, in pixels, the game lifts the content of the card under the mouse (its name, text and reward icons; the
/// card's own rectangle stays): the most read so far; 0 until a hovered card has been read (the overlay then takes
/// FatesPanel.UnmeasuredHoverLift).
/// </param>
/// <param name="Counter">The Fate counter the panel shows ([2,5,0]); null when it shows none, or not "N/M".</param>
internal sealed record FatesPanelState(bool Visible, string Title, IReadOnlyList<FateCard> Offered,
    IReadOnlyList<FateCard> Collection, IReadOnlyList<RectangleF> TopRowSlots, RectangleF ConfirmButton,
    RectangleF CloseButton, float HoverLift = 0f, FateCounter Counter = null)
{
    public const string ChoosingTitle = "Fates";
    public const string InvokingTitle = "Invoke Fates";

    public static readonly FatesPanelState Closed = new(false, "", [], [], [], default, default);

    /// <summary>
    /// The boss panel's reward summary as text ("3x &lt;reward&gt;" per reward, FateSummaryText.Parse): what the summary
    /// strip's icons stand for and the multiplier the game applies. Empty while choosing.
    /// </summary>
    public IReadOnlyList<(int Multiplier, string Text)> SummaryLines { get; init; } = [];

    /// <summary>The rectangle the summary strip's visible icons span; empty when the strip is not shown.</summary>
    public RectangleF SummaryRect { get; init; }

    /// <summary>Whether the panel offers a Cauldron's Fates to choose from.</summary>
    public bool ChoosingFate => Visible && Title == ChoosingTitle;

    /// <summary>Whether the panel is the boss's invocation view.</summary>
    public bool InvokingFates => Visible && Title == InvokingTitle;
}

/// <summary>
/// The Fate counter the game shows since patch 3.30.0.4 (2026-11-19), "collected/available", in the Fates panel and on
/// the Show collected Fates button: the Fates collected in the area, and the Fates it holds, one per Cauldron. Read
/// from the client's code: the first number is the collection's length, the second a byte the server sends in a
/// message that carries nothing else (ClientAugury's sub-message 4). Seen in game: it counts the map's Cauldrons used
/// and in all (the user, 2026-11-19: "1/4" after the first choice).
/// </summary>
internal sealed record FateCounter(int Collected, int InArea)
{
    private static readonly Regex CounterText = new(@"^\s*(\d+)\s*/\s*(\d+)\s*$", RegexOptions.CultureInvariant);

    /// <summary>
    /// The counter a text shows, or null when the text is not "N/M" with M at least 1. The button's copy read empty
    /// before the server had sent the count (dump of 2026-11-19 22:25:49, no Cauldron opened yet in that visit).
    /// </summary>
    public static FateCounter Parse(string text)
    {
        var match = CounterText.Match(text ?? "");

        if (!match.Success || !int.TryParse(match.Groups[1].Value, out var collected) ||
            !int.TryParse(match.Groups[2].Value, out var inArea) || inArea < 1)
            return null;

        return new FateCounter(collected, inArea);
    }
}

/// <summary>
/// Reads the Fates panel, the Augury window in which a Cauldron's Fates are chosen and, at the area's boss, the
/// collection's Fates are invoked, as the game shows them. Only visible elements count: the panel reuses its elements,
/// and hidden ones keep earlier cards' names and rewards (dumps of 2026-11-14).
///
/// The layout, read off dumps of 2026-11-14 (the panel while choosing, and at the boss): the panel is a
/// child of IngameUi whose index moves as other panels open, recognised by its title, child 2's child 3: "Fates" while
/// choosing, "Invoke Fates" at the boss. Below child 2: [5,0] the Fate counter's text, "collected/available" ("0/4"),
/// [6,0] the top row (the offer, or the invocation slots), [7,0] the collection, [8] a reward summary (hidden while
/// choosing, visible at the boss), [10] the confirm button. Patch 3.30.0.4 (2026-11-19) added the counter at [5] and
/// moved every later child up one (child 2 had 11 children before, 12 since; dumps of 22:30 to 22:36 choosing, with
/// the counter at "0/4" to "2/4", and 22:37:09 at the boss, "4/4"). A card k's content is [k,0,0]: [4] its name,
/// [8,0] its reward icons, each icon's child [3] the reward's text; the card element's Tooltip holds its full text.
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

    /// <summary>
    /// The hover lift taken until one is read: 13 px, measured before 3.30.0c (2026-11-14); the larger panel may lift
    /// by another amount, which the first read of a hovered card replaces.
    /// </summary>
    public const float UnmeasuredHoverLift = 13f;

    /// <summary>
    /// The largest lift taken as one: a card's content read further above its rectangle than this is taken to be in
    /// some other state, not hovered. Chosen, about three times the lift measured.
    /// </summary>
    private const float MostHoverLift = 40f;

    private Element _panel;

    /// <summary>The largest hover lift read since the panel was found; 0 until one is read.</summary>
    private float _hoverLift;

    /// <summary>Not started, so the first read looks at once; restarted by every look after.</summary>
    private readonly Stopwatch _sinceLook = new();

    /// <summary>
    /// Forgets the panel, so the next read looks for it again. For an area change. The hover lift is kept: it belongs
    /// to the interface's size, not to the area.
    /// </summary>
    public void Forget() => _panel = null;

    public FatesPanelState Read(GameController gc, bool forceElementTree = false)
    {
        var panel = PanelOf(gc);

        if (panel == null || !Safe.Read(panel, static p => p.IsVisible, false))
            return FatesPanelState.Closed;

        var body = ChildAt(panel, 2);

        var title = TitleOf(panel);

        if (!forceElementTree && ReadFromWindow(gc, panel, body, title) is { } typed)
            return typed;

        var rows = CardRowsOf(body);
        var top = CardsOf(rows.Count > 0 ? rows[0] : null);
        var bottom = CardsOf(rows.Count > 1 ? rows[1] : null);
        var (offered, collection) = WithAreaTier(top.Cards, bottom.Cards, title == FatesPanelState.ChoosingTitle);

        _hoverLift = Math.Max(_hoverLift, Math.Max(top.Lift, bottom.Lift));

        var summary = SummaryOf(ChildAt(body, 8));

        return new FatesPanelState(true, title, offered, collection, top.Slots,
            VisibleRectOf(ConfirmButtonOf(body)), VisibleRectOf(ChildAt(panel, 3)), _hoverLift, CounterOf(body))
        {
            SummaryLines = summary.Lines,
            SummaryRect = summary.Rect,
        };
    }

    /// <summary>
    /// The panel read through the HUD's typed window (IngameUi.AuguryFateWindow): the offer and the collection as cards
    /// with their table Ids and outcome Ids, so nothing the plugin scores comes from a child index or matched text.
    /// Null when the window is missing or reads empty - the element tree answers then. The title, the rectangles, the
    /// buttons and the summary still come from the elements: the typed window has none of those.
    /// </summary>
    private static FatesPanelState ReadFromWindow(GameController gc, Element panel, Element body, string title)
    {
        var window = Safe.Read(gc, static g => g.IngameState?.IngameUi?.AuguryFateWindow, null);

        if (window == null || !Safe.Read(window, static w => w.IsVisible, false))
            return null;

        var choices = Safe.Read(window, static w => w.CardChoices, null);
        var owned = Safe.Read(window, static w => w.OwnedCards, null);
        var offered = CardsOf(choices);
        var collection = CardsOf(owned);

        // An empty typed read on a visibly open panel means the type went stale: let the element tree answer.
        if (offered.Count == 0 && collection.Count == 0)
            return null;

        var slots = new List<RectangleF>();

        foreach (var choice in choices ?? [])
        {
            var rect = Safe.Read(choice, static c => c.GetClientRectCache, default(RectangleF));

            if (rect.Width > 0f && rect.Height > 0f)
                slots.Add(rect);
        }

        var summary = SummaryOf(ChildAt(body, 8));

        return new FatesPanelState(true, title, offered, collection, slots, VisibleRectOf(ConfirmButtonOf(body)),
            VisibleRectOf(ChildAt(panel, 3)))
        {
            SummaryLines = summary.Lines,
            SummaryRect = summary.Rect,
            Counter = CounterOf(body),
        };
    }

    /// <summary>
    /// The cards of a typed row (CardChoices or OwnedCards): each card's table Id resolved in the catalogue, its
    /// outcomes by outcome Id with the copies they need (the outcome's tier), and whether the window says it is
    /// selected. The card's own tier is not exposed by the typed window, so it reads null here.
    /// </summary>
    private static List<FateCard> CardsOf(IEnumerable<AuguryCardElement> cards)
    {
        var list = new List<FateCard>();

        foreach (var card in cards ?? [])
        {
            var id = Safe.Read(card, static c => c.Card?.Id, null);

            if (string.IsNullOrWhiteSpace(id))
                continue;

            var fate = AuguryCatalogueLookup.FateNamed(id);
            var rewards = new List<FateCardReward>();

            foreach (var outcome in Safe.Read(card, static c => c.Rewards, null) ?? [])
            {
                var outcomeId = Safe.Read(outcome, static o => o.Id, null);

                if (string.IsNullOrWhiteSpace(outcomeId))
                    continue;

                var text = Safe.Read(outcome, static o => o.Description, "") ?? "";
                var copies = Safe.Read(outcome, static o => o.Tier, 1);

                rewards.Add(new FateCardReward(text, AuguryCatalogueLookup.RewardById(outcomeId), copies));
            }

            list.Add(new FateCard(fate?.Name ?? id, fate, null,
                Safe.Read(card, static c => c.GetClientRectCache, default(RectangleF)), rewards,
                Safe.Read(card, static c => c.IsSelected, false)));
        }

        return list;
    }

    /// <summary>
    /// The panel's card rows, top down: a body child holding a row of cards, the offer (or the invocation slots at the
    /// boss) first, the collection second. Found by the cards' shape rather than by index, so a patch that inserts an
    /// element above them - as 3.30.0d did with the Fate counter - moves nothing the reader depends on. The fallback
    /// when the typed window is unavailable.
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
    /// Whether an element holds cards: one child whose child 0,0 has a card's content - 9 or more children, a name at
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

    /// <summary>
    /// The Fate counter the panel shows ("3/3"): the body child whose own child reads two numbers around a slash.
    /// Null when none does (an older client, or a moved element).
    /// </summary>
    private static FateCounter CounterOf(Element body)
    {
        foreach (var holder in Safe.ChildrenOf(body) ?? [])
        {
            var text = Safe.Read(ChildAt(holder, 0), static e => e?.Text, null);

            if (text != null && FateCounter.Parse(text) is { } counter)
                return counter;
        }

        return null;
    }

    /// <summary>
    /// The summary strip's text entries and the rectangle its visible icons span. The strip's own element holds no
    /// text: the long line is one of its children (read past Element.Text's 256-character stop), the icons sit in two
    /// groups of children below it.
    /// </summary>
    private static (List<(int Multiplier, string Text)> Lines, RectangleF Rect) SummaryOf(Element summary)
    {
        var lines = new List<(int Multiplier, string Text)>();
        var rect = default(RectangleF);
        var any = false;

        if (summary == null)
            return (lines, rect);

        foreach (var child in Safe.ChildrenOf(summary) ?? [])
        {
            var raw = Safe.Read(child, static c => c.Text, null);

            if (!string.IsNullOrWhiteSpace(raw) && raw.Contains('x'))
                lines.AddRange(FateSummaryText.Parse(Safe.Read(child, static c => c.GetText(MostTextLength), null)));

            foreach (var icon in Safe.ChildrenOf(child) ?? [])
            {
                if (!Safe.Read(icon, static i => i.IsVisible, false))
                    continue;

                var iconRect = Safe.Read(icon, static i => i.GetClientRectCache, default(RectangleF));

                if (iconRect.Width <= 0f || iconRect.Height <= 0f)
                    continue;

                rect = any
                    ? new RectangleF(Math.Min(rect.X, iconRect.X), Math.Min(rect.Y, iconRect.Y),
                        Math.Max(rect.Right, iconRect.Right) - Math.Min(rect.X, iconRect.X),
                        Math.Max(rect.Bottom, iconRect.Bottom) - Math.Min(rect.Y, iconRect.Y))
                    : iconRect;
                any = true;
            }
        }

        return (lines, rect);
    }

    /// <summary>
    /// The cards, those whose text gave no tier given the area's: every card in an area shows one tier, the one the
    /// server sends on entering it (the client looks up a card's tier text by that tier and the card, read from its
    /// code on 2026-11-18). The area's tier is the one tier read off the collection and, while choosing, the offer; not
    /// off the boss's slots, whose text shows a pair's raised tier (tier 3 for two copies of a tier 2 Fate, 2026-11-14
    /// 05:35). Unchanged when no tier was read, or two different ones were.
    /// </summary>
    internal static (List<FateCard> Top, List<FateCard> Bottom) WithAreaTier(List<FateCard> top, List<FateCard> bottom,
        bool choosing)
    {
        var tiers = (choosing ? top.Concat(bottom) : bottom).Select(static c => c.Tier).OfType<int>().Distinct()
            .ToList();

        if (tiers.Count != 1)
            return (top, bottom);

        List<FateCard> Filled(List<FateCard> cards) =>
            cards.Select(c => c.Tier == null ? c with { Tier = tiers[0] } : c).ToList();

        return (Filled(top), Filled(bottom));
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
    /// cards means the client's layout moved and the finders no longer land. The Tick warns on repeats of this.
    /// </summary>
    public static bool ReadsEmpty(FatesPanelState state) =>
        state != null && state.Visible && state.TopRowSlots.Count == 0 && state.Offered.Count == 0 &&
        state.Collection.Count == 0;

    /// <summary>
    /// Whether the panel offers Fates to choose from but no offered card read: the offer row was not found, which is
    /// what a layout change does (2026-11-19: a counter inserted above the rows left the cards reading as the
    /// collection, so ReadsEmpty stayed quiet while the overlay drew nothing).
    /// </summary>
    public static bool ReadsNoOffers(FatesPanelState state) =>
        state != null && state.ChoosingFate && state.Offered.Count == 0;

    /// <summary>The text of child 2's child 3, where the Fates panel has its title; null when there is none.</summary>
    private static string TitleOf(Element panel) => Safe.Read(ChildAt(panel, 2, 3), static t => t?.Text, null);

    /// <summary>
    /// A row's cards: every visible card element's rectangle (slots), and a FateCard for each whose name element is
    /// visible and named, with the largest hover lift read among them (0 when no card was hovered). An empty
    /// invocation slot is a visible card element with its name element hidden.
    /// </summary>
    private static (List<FateCard> Cards, List<RectangleF> Slots, float Lift) CardsOf(Element row)
    {
        var cards = new List<FateCard>();
        var slots = new List<RectangleF>();
        var mostLift = 0f;

        if (!Safe.Read(row, static r => r?.IsVisible ?? false, false))
            return (cards, slots, mostLift);

        foreach (var card in Safe.ChildrenOf(row) ?? [])
        {
            if (!Safe.Read(card, static c => c.IsVisible, false))
                continue;

            var rect = Safe.Read(card, static c => c.GetClientRectCache, default(RectangleF));

            slots.Add(rect);

            var content = ChildAt(card, 0, 0);
            var lift = LiftOf(rect, Safe.Read(content, static c => c?.GetClientRectCache ?? default,
                default(RectangleF)));
            var nameElement = ChildAt(content, 4);

            mostLift = Math.Max(mostLift, lift);
            var name = Safe.Read(nameElement, static n => n?.IsVisible ?? false, false) ? TextOf(nameElement) : null;

            if (string.IsNullOrWhiteSpace(name))
                continue;

            var fate = AuguryCatalogueLookup.FateNamed(name);
            var fullText = TextOf(Safe.Read(card, static c => c.Tooltip, null));

            cards.Add(new FateCard(name.Trim(), fate, AuguryCatalogueLookup.TierOf(fate, fullText), rect,
                RewardsOf(ChildAt(content, 8, 0), lift), !Safe.Read(card, static c => c.IsActive, true)));
        }

        return (cards, slots, mostLift);
    }

    /// <summary>
    /// How far a card's content sits above the card's own rectangle: the hover lift while the card is hovered, 0
    /// otherwise; 0 too when the content was not read or sits further up than MostHoverLift.
    /// </summary>
    private static float LiftOf(RectangleF card, RectangleF content)
    {
        var lift = card.Top - content.Top;

        return content.Height > 0f && lift > 0f && lift <= MostHoverLift ? lift : 0f;
    }

    /// <summary>A card's visible reward icons, each moved down by the card's lift, to where it rests.</summary>
    private static List<FateCardReward> RewardsOf(Element icons, float lift)
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

            var rect = Safe.Read(icon, static i => i.GetClientRectCache, default(RectangleF));

            if (rect.Width > 0f)
                rect.Y += lift;

            rewards.Add(new FateCardReward(text, AuguryCatalogueLookup.RewardShown(text, copies), copies, rect));
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
