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
/// </summary>
internal static class FateOverlay
{
    /// <summary>The colours, from AutoExpedition's palette: green for the best pick and the advised cards.</summary>
    private static readonly Color BestColour = Colours.FromArgb(255, 90, 255, 120);

    /// <summary>Yellow for the other picks.</summary>
    private static readonly Color OtherColour = Colours.FromArgb(255, 255, 210, 80);

    /// <summary>Red for a pick that scores below zero.</summary>
    private static readonly Color NegativeColour = Colours.FromArgb(255, 235, 90, 90);

    /// <summary>Purple for a Fate's second or third copy, AutoExpedition's reward colour.</summary>
    private static readonly Color EmpowerColour = Colours.FromArgb(255, 190, 120, 255);

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
    /// exact tie), the best by the unrounded value (FateScore.Value), not by the Total written: weak cards often all
    /// round to the same Total. While the mouse is over a label, an ImGui tooltip names the parts (ScoreWithPartsHelp)
    /// and gives that card's weight without rounding, to two places ("Weight without rounding: 0.46").
    /// </summary>
    public static void DrawScores(Graphics graphics, IReadOnlyList<FateScore> scores, DisplaySettings display)
    {
        if (scores.Count == 0)
            return;

        var best = scores.Max(static s => s.Value);
        var lineHeight = LineHeightOf(graphics);
        var mouse = ImGui.GetMousePos();
        FateScore hovered = null;

        foreach (var score in scores)
        {
            var rect = score.Card.Rect;

            if (rect.Width <= 0f)
                continue;

            var isBest = score.Value >= best - SameValue;
            var colour = isBest ? BestColour : score.Total < 0 ? NegativeColour : OtherColour;

            if (isBest)
                graphics.DrawFrame(rect, BestColour, 3);

            var label = DrawAbove(graphics, rect, 1, lineHeight, ScoreLabelOf(score, display.ShowScoreBreakdown),
                colour);

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
    /// Frames only the card to place next (InvocationGuide.Next: the remaining card that adds the most while the order
    /// is free, the advice's next while two copies of one Fate are still to place), green or, for a Fate's second or
    /// third copy, purple, with what placing it adds above it ("+15", "+40 pair", "+45 triple"). Framing every card
    /// while the order was free was tried and dropped as more confusing. Writes one line to the left of the confirm
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
            var colour = copyMark.Length == 0 ? BestColour : EmpowerColour;
            var label = Signed(guide.GainOf(pick)) + copyMark;

            graphics.DrawFrame(pick.Card.Rect, colour, 3);
            DrawAbove(graphics, pick.Card.Rect, 1, lineHeight, label, colour);
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
    /// the parts add up to the rounded total. " ?" follows when a reward on the card matched no catalogue reward, or
    /// its name no Fate.
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
    /// "Estimated: 1/3" to the left of the panel's close button while choosing: the Cauldron being chosen at, of the
    /// map's estimated Cauldrons: MapCauldrons' assumption, or more when more are known
    /// (FateScoring.CauldronProgressOf).
    /// </summary>
    public static void DrawCauldronProgress(Graphics graphics, FatesPanelState state, (int Current, int Total) progress)
    {
        var close = state.CloseButton;

        if (close.Width <= 0f || progress.Total <= 0)
            return;

        var lineHeight = LineHeightOf(graphics);

        graphics.DrawTextWithBackground($"Estimated: {progress.Current}/{progress.Total}",
            new Vector2(close.X - 8f, close.Y + close.Height / 2f - lineHeight / 2f), TextColour, FontAlign.Right,
            Background);
    }

    private static float LineHeightOf(Graphics graphics) => graphics.MeasureText("Ag").Y + 2f;

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
}
