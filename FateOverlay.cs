using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using ExileCore.Shared.Enums;
using ImGuiNET;
using Color = SharpDX.Color;
using Graphics = ExileCore.Graphics;
using RectangleF = SharpDX.RectangleF;

namespace AutoAugury;

/// <summary>
/// Draws the plugin's marks on the Fates panel, from the last read only (nothing of the game is read here):
///
/// - While choosing: each offered Fate's score above its card, "24 (5 + 19)" (its parts when Show score breakdown is
///   on, with a tooltip naming them), and the best card (every best card, on a tie) framed.
/// - At the boss: only the card to place now framed, with what it adds ("+15"), and under the invocation slots what
///   each pick adds in placing order, with a warning when the placed cards cost something.
/// - In both views: each reward icon that has no weight framed in red (DrawRewardsWithoutWeight), and each Must take
///   reward framed in purple (DrawMustTakeRewards).
/// </summary>
internal static class FateOverlay
{
    /// <summary>The colours, from AutoExpedition's palette: green for the best pick and the advised cards.</summary>
    private static readonly Color BestColour = Colours.FromArgb(255, 90, 255, 120);

    /// <summary>Yellow for the other picks.</summary>
    private static readonly Color OtherColour = Colours.FromArgb(255, 255, 210, 80);

    /// <summary>Red for a pick that scores below zero, and for a reward without a weight.</summary>
    private static readonly Color NegativeColour = Colours.FromArgb(255, 235, 90, 90);

    /// <summary>How far the red frame round a reward icon stands off the icon, in pixels. Chosen.</summary>
    private const float RewardFrameMargin = 3f;

    /// <summary>
    /// How far a card's frame stands outside its reward icons, in pixels: beyond a reward's red frame
    /// (RewardFrameMargin and its 3 px line), so the two never touch. Chosen.
    /// </summary>
    private const float CardFrameIconMargin = 8f;

    /// <summary>Purple, AutoExpedition's reward colour: the Must take rewards' frames and line only.</summary>
    private static readonly Color MustTakeColour = Colours.FromArgb(255, 190, 120, 255);

    private static readonly Color TextColour = Colours.FromArgb(255, 245, 245, 245);
    private static readonly Color Background = Colours.FromArgb(200, 0, 0, 0);

    /// <summary>The tooltip on a score label with its parts, naming them in order.</summary>
    private const string ScoreWithPartsHelp = "Total weight (Weight now + Potential weight)";

    /// <summary>The tooltip on a score label without its parts.</summary>
    private const string ScoreHelp = "Total weight";

    /// <summary>The tooltip's second line, before the hovered card's unrounded value (FateScore.Value).</summary>
    private const string UnroundedHelp = "Weight without rounding: ";

    /// <summary>Two values closer than this are equal: the same card's value worked out twice.</summary>
    private const double SameValue = 1e-6;

    /// <summary>
    /// Writes each offered card's score label above it (ScoreLabelOf) and frames the best card (every best card, on an
    /// exact tie): the best by FateScoring.BestOf, the Must take rule first and then the unrounded value
    /// (FateScore.Value), not the Total written, since weak cards often all round to the same Total. A card the Must
    /// take rule ranks gets a purple line above its score, "must take +6371". While the mouse is over a label, an ImGui
    /// tooltip names the parts (ScoreWithPartsHelp) and gives that card's weight without rounding, to two places
    /// ("Weight without rounding: 0.46").
    /// </summary>
    public static void DrawScores(Graphics graphics, IReadOnlyList<FateScore> scores, DisplaySettings display)
    {
        if (scores.Count == 0)
            return;

        var best = FateScoring.BestOf(scores);
        var lineHeight = LineHeightOf(graphics);
        var mouse = ImGui.GetMousePos();
        FateScore hovered = null;

        foreach (var score in scores)
        {
            var rect = score.Card.Rect;

            if (rect.Width <= 0f)
                continue;

            var isBest = best.Contains(score);
            var colour = isBest ? BestColour : score.Total < 0 ? NegativeColour : OtherColour;

            if (isBest)
                graphics.DrawFrame(FrameRectOf(score.Card), BestColour, 3);

            var label = DrawAbove(graphics, rect, 1, lineHeight, ScoreLabelOf(score, display.ShowScoreBreakdown),
                colour);

            if (score.MustTakeRank > 0d)
                DrawAbove(graphics, rect, 2, lineHeight, $"must take +{score.MustTakeRank:0}", MustTakeColour);

            if (mouse.X >= label.Left && mouse.X <= label.Right && mouse.Y >= label.Top && mouse.Y <= label.Bottom)
                hovered = score;
        }

        if (hovered != null)
        {
            ImGui.SetTooltip((display.ShowScoreBreakdown ? ScoreWithPartsHelp : ScoreHelp) + "\n" + UnroundedHelp +
                             hovered.Value.ToString("0.00", CultureInfo.InvariantCulture));
        }
    }

    /// <summary>
    /// Frames in purple each Must take reward icon (RewardSettings.IsMustTake) on a visible card, offered or collected,
    /// lifted with a hovered card's content as DrawRewardsWithoutWeight's frames are. Drawn whatever the Display
    /// settings.
    /// </summary>
    public static void DrawMustTakeRewards(Graphics graphics, FatesPanelState state, RewardSettings rewards) =>
        DrawRewardFrames(graphics, state, r => r.Reward != null && rewards.IsMustTake(r.Reward.Family),
            MustTakeColour);

    /// <summary>
    /// Frames in red each reward icon on a visible card, offered or collected, that counts nothing for want of a
    /// weight (NewRewardRegistry.LacksWeight): a reward the catalogue does not have, until the player sets its weight
    /// in the reward table. So a patch's new reward stands out on its card as well as in the message under the cursor
    /// (CatalogueWarnings). Drawn whatever the Display settings, as a warning.
    ///
    /// The icons are read where they rest (FateCardReward.Rect); while the mouse is over a card's rectangle the game
    /// lifts its content by FatesPanelState.HoverLift, and the frame is lifted with it in the same frame, from the
    /// mouse position alone. Following the reads instead, ten a second, made the frame trail the icon visibly.
    /// </summary>
    public static void DrawRewardsWithoutWeight(Graphics graphics, FatesPanelState state, RewardSettings rewards) =>
        DrawRewardFrames(graphics, state, r => NewRewardRegistry.LacksWeight(r, rewards), NegativeColour);

    /// <summary>
    /// Frames each reward icon the test picks, RewardFrameMargin off the icon, lifted by the hover lift while the
    /// mouse is over its card's rectangle.
    /// </summary>
    private static void DrawRewardFrames(Graphics graphics, FatesPanelState state, Func<FateCardReward, bool> framed,
        Color colour)
    {
        var mouse = ImGui.GetMousePos();

        foreach (var card in state.Offered.Concat(state.Collection))
        {
            var cardRect = card.Rect;
            var hovered = mouse.X >= cardRect.Left && mouse.X <= cardRect.Right && mouse.Y >= cardRect.Top &&
                          mouse.Y <= cardRect.Bottom;
            var lift = !hovered ? 0f : state.HoverLift > 0f ? state.HoverLift : FatesPanel.UnmeasuredHoverLift;

            foreach (var reward in card.Rewards)
            {
                if (reward.Rect.Width <= 0f || !framed(reward))
                    continue;

                var rect = reward.Rect;

                graphics.DrawFrame(new RectangleF(rect.X - RewardFrameMargin, rect.Y - lift - RewardFrameMargin,
                    rect.Width + 2f * RewardFrameMargin, rect.Height + 2f * RewardFrameMargin), colour, 3);
            }
        }
    }

    /// <summary>
    /// Frames only the card to place next (InvocationGuide.Next: the remaining card that adds the most while the order
    /// is free, the advice's next while two copies of one Fate are still to place), green, with what placing it adds
    /// above it ("+15", "+40 pair", "+45 triple"). A Fate's second or third copy was framed purple until 2026-11-19,
    /// when purple became the Must take rewards' alone (the user). Framing every card while the order was free was
    /// tried and dropped as more confusing. Writes one line to the left of the confirm
    /// button: what each pick adds in placing order, as placed so far ("weights: 15 + 15 + 5 = 35"), with "(any order)"
    /// when no two picks are copies of one Fate. When the placed cards allow less than the best invocation, a red line
    /// under it says how much and why: "wrong order" when copies of one Fate went in the wrong order, "not the best
    /// Fates" when a placed card is not among them.
    /// </summary>
    public static void DrawGuide(Graphics graphics, InvocationGuide guide, FatesPanelState state)
    {
        var lineHeight = LineHeightOf(graphics);
        var framed = guide.Next == null ? [] : new[] { guide.Next };

        foreach (var pick in framed)
        {
            if (pick.Card.Rect.Width <= 0f)
                continue;

            var copyMark = pick.Empowers != null ? " pair" : pick.ThirdCopyOf != null ? " triple" : "";
            var label = Signed(guide.GainOf(pick)) + copyMark;

            graphics.DrawFrame(FrameRectOf(pick.Card), BestColour, 3);
            DrawAbove(graphics, pick.Card.Rect, 1, lineHeight, label, BestColour);
        }

        var weights = guide.Gains.Count == 0
            ? "no Fate is worth a slot by your weights"
            : $"weights: {string.Join(" + ", guide.Gains)} = {guide.Advice.Total}" +
              (guide.AnyOrder && guide.Gains.Count > 1 ? " (any order)" : "");
        var warning = guide.Cost > 0
            ? $"{(guide.WrongOrder ? "wrong order" : "not the best Fates")}: -{guide.Cost}"
            : null;

        // Right-aligned to the left of the confirm button, centred on it; under the slots when it cannot be read.
        Vector2 at;
        FontAlign align;

        if (state.ConfirmButton.Width > 0f)
        {
            var button = state.ConfirmButton;

            at = new Vector2(button.X - 8f, button.Y + button.Height / 2f - lineHeight / 2f);
            align = FontAlign.Right;
        }
        else if (state.TopRowSlots.Count > 0)
        {
            var slots = state.TopRowSlots;

            at = new Vector2((slots.Min(static s => s.X) + slots.Max(static s => s.X + s.Width)) / 2f,
                slots.Max(static s => s.Y + s.Height) + 4f);
            align = FontAlign.Center;
        }
        else
            return;

        graphics.DrawTextWithBackground(weights, at, TextColour, align, Background);

        if (warning != null)
        {
            graphics.DrawTextWithBackground(warning, at with { Y = at.Y + lineHeight }, NegativeColour, align,
                Background);
        }
    }

    private static string Signed(int value) => value switch
    {
        < 0 => $"{value}",
        0 => "0",
        _ => $"+{value}",
    };

    /// <summary>
    /// A score as the overlay writes it, in the boss view's "15 + 15 + 5" form: "24 (5 + 19)", the total, then what
    /// the card adds to the best invocation now plus the change in copy potential (CopyPotential); "36 (58 - 22)" when
    /// the potential falls; the total alone without its parts. The potential written is the total less the gain, so
    /// the parts add up to the rounded total. " ?" follows when a reward on the card has no weight (a reward the
    /// catalogue does not have, until its weight is set: NewRewardRegistry), or its name matches no Fate.
    /// </summary>
    public static string ScoreLabelOf(FateScore score, bool withParts)
    {
        var uncertain = score.UnknownRewards > 0 || score.Card.Fate == null ? " ?" : "";

        if (!withParts)
            return $"{score.Total}{uncertain}";

        var potential = score.Total - score.Gain;

        return $"{score.Total} ({score.Gain} {(potential < 0 ? "-" : "+")} {Math.Abs(potential)}){uncertain}";
    }

    /// <summary>
    /// "Estimated: 1/3" to the left of the panel's close button while choosing and the panel shows no Fate counter:
    /// the Cauldron being chosen at, of the map's estimated Cauldrons: MapCauldrons' setting ("1/3.5" for a fraction),
    /// or more when more are known (FateScoring.CauldronProgressOf). Not drawn when progress is empty.
    /// </summary>
    public static void DrawCauldronProgress(Graphics graphics, FatesPanelState state,
        (int Current, double Total) progress)
    {
        var close = state.CloseButton;

        if (close.Width <= 0f || progress.Total <= 0)
            return;

        var lineHeight = LineHeightOf(graphics);

        graphics.DrawTextWithBackground(
            $"Estimated: {progress.Current}/{progress.Total.ToString("0.#", CultureInfo.InvariantCulture)}",
            new Vector2(close.X - 8f, close.Y + close.Height / 2f - lineHeight / 2f), TextColour, FontAlign.Right,
            Background);
    }

    private static float LineHeightOf(Graphics graphics) => graphics.MeasureText("Ag").Y + 2f;

    /// <summary>
    /// The rectangle a card's frame is drawn on: the card's own, grown to take in each reward icon with
    /// CardFrameIconMargin round it. Since 3.30.0c made the icons larger they hang below the card's rectangle (dump of
    /// 2026-11-18 23:15:18: a card at 763,161 345x476, bottom 637, its icons 51x48 at y 599, bottom 647; before, a
    /// card's 40x37 icons ended 7 px inside it), and a frame on the card's rectangle alone drew its bottom line through
    /// them. Taken from the rectangles read, so a later resize is followed without a new offset. The icons are taken
    /// where they rest (FateCardReward.Rect), so the frame stays put when the card is hovered and its content lifts;
    /// following the lift, read ten times a second, made the frame's bottom line jump behind the card.
    /// </summary>
    private static RectangleF FrameRectOf(FateCard card)
    {
        var (left, top, right, bottom) = (card.Rect.Left, card.Rect.Top, card.Rect.Right, card.Rect.Bottom);

        foreach (var reward in card.Rewards)
        {
            var icon = reward.Rect;

            if (icon.Width <= 0f)
                continue;

            left = Math.Min(left, icon.Left - CardFrameIconMargin);
            top = Math.Min(top, icon.Top - CardFrameIconMargin);
            right = Math.Max(right, icon.Right + CardFrameIconMargin);
            bottom = Math.Max(bottom, icon.Bottom + CardFrameIconMargin);
        }

        return new RectangleF(left, top, right - left, bottom - top);
    }

    /// <summary>
    /// A centred line of text above a card, line 1 just above it, line 2 above that. Returns the text's rectangle, by
    /// MeasureText, for hover tests.
    /// </summary>
    private static RectangleF DrawAbove(Graphics graphics, RectangleF rect, int line, float lineHeight, string text,
        Color colour)
    {
        var centre = new Vector2(rect.X + rect.Width / 2f, rect.Y - line * lineHeight - 2f);
        var size = graphics.MeasureText(text);

        graphics.DrawTextWithBackground(text, centre, colour, FontAlign.Center, Background);

        return new RectangleF(centre.X - size.X / 2f, centre.Y, size.X, size.Y);
    }

    /// <summary>
    /// The reward legend under each offered card's icons (Display > Label rewards): the drop's name, with the modifier
    /// a rare item will carry, in icon order. Wrapped to a second line when three rewards do not fit, and kept above
    /// the confirm button when a card sits over it.
    /// </summary>
    public static void DrawRewardLabels(Graphics graphics, FatesPanelState state)
    {
        if (!state.ChoosingFate || state.Offered.Count == 0)
            return;

        var lineHeight = LineHeightOf(graphics);

        foreach (var card in state.Offered)
        {
            var text = RewardLabels.Join(card.Rewards.Select(static r => RewardLabels.Of(r.Text)));

            if (text.Length == 0)
                continue;

            var lines = Wrap(graphics, text, card.Rect.Width - 16f, 2);
            var bottom = card.Rect.Bottom + 2f + lines.Count * lineHeight;

            if (state.ConfirmButton.Width > 0f &&
                card.Rect.X < state.ConfirmButton.Right && card.Rect.Right > state.ConfirmButton.X)
                bottom = Math.Min(bottom, state.ConfirmButton.Y - 2f);

            for (var i = 0; i < lines.Count; i++)
                graphics.DrawTextWithBackground(lines[i],
                    new Vector2(card.Rect.X + card.Rect.Width / 2f, bottom - (lines.Count - i) * lineHeight),
                    OtherColour, FontAlign.Center, Background);
        }
    }

    /// <summary>
    /// The boss summary's legend, under the strip (Display > Label rewards): one line per reward the panel lists,
    /// "Threads 3x", named by RewardLabels and counted by the panel's own multiplier. The strip repeats an icon per
    /// count, so this says in words what the icons only repeat.
    /// </summary>
    public static void DrawSummaryLegend(Graphics graphics, FatesPanelState state)
    {
        if (!state.InvokingFates || state.SummaryLines.Count == 0)
            return;

        var rect = state.SummaryRect;

        if (rect.Width <= 0f || rect.Height <= 0f)
            return;

        var lineHeight = LineHeightOf(graphics);

        for (var i = 0; i < state.SummaryLines.Count; i++)
        {
            var (multiplier, text) = state.SummaryLines[i];
            var label = RewardLabels.Of(text);

            if (label.Length == 0)
                continue;

            graphics.DrawTextWithBackground($"{label} {multiplier}x",
                new Vector2(rect.X, rect.Bottom + 4f + i * lineHeight), TextColour, FontAlign.Left, Background);
        }
    }

    /// <summary>
    /// The text split into at most <paramref name="mostLines"/> lines no wider than the card, on the " / " separator
    /// RewardLabels.Join writes. A piece longer than the card stays whole rather than being cut mid-word.
    /// </summary>
    private static List<string> Wrap(Graphics graphics, string text, float maxWidth, int mostLines)
    {
        var lines = new List<string>();
        var current = "";

        foreach (var part in text.Split(" / ", StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = current.Length == 0 ? part : $"{current} / {part}";

            if (current.Length == 0 || graphics.MeasureText(candidate).X <= maxWidth)
            {
                current = candidate;
                continue;
            }

            lines.Add(current);
            current = part;

            if (lines.Count == mostLines - 1)
                break;
        }

        if (current.Length > 0)
            lines.Add(current);

        return lines;
    }
}
