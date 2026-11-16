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

    /// <summary>Purple for a copy that empowers an earlier one, AutoExpedition's reward colour.</summary>
    private static readonly Color EmpowerColour = Colours.FromArgb(255, 190, 120, 255);

    private static readonly Color TextColour = Colours.FromArgb(255, 245, 245, 245);
    private static readonly Color Background = Colours.FromArgb(200, 0, 0, 0);

    /// <summary>One overlay colour set: the best pick, the other picks, picks scoring below zero, empowering copies.</summary>
    internal readonly record struct OverlayPalette(Color Best, Color Other, Color Negative, Color Empower);

    /// <summary>The standard set: the colours above.</summary>
    private static readonly OverlayPalette StandardPalette = new(BestColour, OtherColour, NegativeColour, EmpowerColour);

    /// <summary>
    /// Okabe-Ito's colourblind-safe set, for Display > High-contrast overlay palette: green, orange and vermillion
    /// stay apart for the common colour visions, with sky blue for empowering copies.
    /// </summary>
    private static readonly OverlayPalette SafePalette = new(
        Colours.FromArgb(255, 0, 158, 115),
        Colours.FromArgb(255, 230, 159, 0),
        Colours.FromArgb(255, 213, 94, 0),
        Colours.FromArgb(255, 86, 180, 233));

    /// <summary>The overlay's colour set: the colourblind-safe one on request, else the standard one.</summary>
    public static OverlayPalette PaletteFor(bool highContrast) => highContrast ? SafePalette : StandardPalette;

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

        var palette = PaletteFor(display.HighContrastPalette.Value);
        var thickness = display.FrameThickness.Value;
        var gap = display.ScoreLineGap.Value;
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
            var colour = isBest ? palette.Best : score.Total < 0 ? palette.Negative : palette.Other;

            if (isBest)
                graphics.DrawFrame(rect, palette.Best, thickness);

            var label = DrawAbove(graphics, rect, 1, lineHeight, ScoreLabelOf(score, display.ShowScoreBreakdown),
                colour, gap);

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
    /// Every pick still to place in placing order, the next brightest, each badged with its order: "1. +15",
    /// "2. +40 pair". Placed picks are not framed: their cards read greyed in the collection row already, and the
    /// weights line below counts them. One line is written to the left of the confirm button: what each pick adds
    /// in placing order, as placed so far ("weights: 15 + 15 + 5 = 35"), with "(any order)" when no two picks are
    /// copies of one Fate. When the placed cards allow less than the best invocation, a red line under it
    /// says how much and why: "wrong order" when a pair's copies went in the wrong way round, "not the best Fates"
    /// when a placed card is not among them.
    /// </summary>
    public static void DrawGuide(Graphics graphics, InvocationGuide guide, FatesPanelState state,
        DisplaySettings display)
    {
        var lineHeight = LineHeightOf(graphics);
        var palette = PaletteFor(display.HighContrastPalette.Value);
        var thickness = display.FrameThickness.Value;
        var gap = display.ScoreLineGap.Value;

        foreach (var pick in guide.Remaining)
        {
            if (pick.Card.Rect.Width <= 0f)
                continue;

            var isNext = ReferenceEquals(pick, guide.Next);
            var colour = isNext ? palette.Best : pick.Empowers == null ? palette.Other : palette.Empower;

            graphics.DrawFrame(pick.Card.Rect, colour, isNext ? thickness : Math.Max(1, thickness - 1));
            DrawAbove(graphics, pick.Card.Rect, 1, lineHeight, PickLabel(guide, pick), colour, gap);
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
            graphics.DrawTextWithBackground(warning, at with { Y = at.Y + lineHeight },
                PaletteFor(display.HighContrastPalette.Value).Negative, align, Background);
        }
    }

    /// <summary>One remaining pick's label: its placing order, what it adds, and whether it empowers an earlier copy.</summary>
    public static string PickLabel(InvocationGuide guide, InvocationPick pick) =>
        $"{pick.Order}. " + Signed(guide.GainOf(pick)) + (pick.Empowers == null ? "" : " pair");

    private static string Signed(int value) => value switch
    {
        < 0 => $"{value}",
        0 => "0",
        _ => $"+{value}",
    };

    /// <summary>
    /// A score as the overlay writes it, in the boss view's "15 + 15 + 5" form: "24 (5 + 19)", the total, then what
    /// the card adds to the best invocation now plus the change in copy potential (CopyPotential); "36 (58 - 22)" when
    /// the potential falls; the total alone without its parts. " ?" follows when a reward on the card matched no
    /// catalogue reward, or its name no Fate; "tier ?" when the card's tier cannot be read but the Fate has tiers.
    /// </summary>
    public static string ScoreLabelOf(FateScore score, bool withParts)
    {
        var uncertain = score.UnknownRewards > 0 || score.Card.Fate == null ? " ?" : "";
        var tierUnknown = score.Card is { Fate: not null, Tier: null } &&
            AuguryCatalogueLookup.HasDistinctTiers(score.Card.Fate) ? " tier ?" : "";

        if (!withParts)
            return $"{score.Total}{uncertain}{tierUnknown}";

        var potential = score.Total - score.Gain;

        return $"{score.Total} ({score.Gain} {(potential < 0 ? "-" : "+")} {Math.Abs(potential)})" +
            $"{uncertain}{tierUnknown}";
    }

    /// <summary>
    /// "Estimated: 1/3 (tracked)" to the left of the panel's close button while choosing: the Cauldron being chosen
    /// at, of the map's estimated Cauldrons (FateScoring.CauldronProgressOf), and where the total comes from.
    /// </summary>
    public static void DrawCauldronProgress(Graphics graphics, FatesPanelState state,
        (int Current, int Total, string Source) progress)
    {
        var close = state.CloseButton;

        if (close.Width <= 0f || progress.Total <= 0)
            return;

        var lineHeight = LineHeightOf(graphics);

        graphics.DrawTextWithBackground($"Estimated: {progress.Current}/{progress.Total} ({progress.Source})",
            new Vector2(close.X - 8f, close.Y + close.Height / 2f - lineHeight / 2f), TextColour, FontAlign.Right,
            Background);
    }

    private static float LineHeightOf(Graphics graphics) => graphics.MeasureText("Ag").Y + 2f;

    /// <summary>
    /// A centred line of text above a card, line 1 just above it, line 2 above that, with the display's gap between
    /// the card and the first line. Returns the text's rectangle, by MeasureText, for hover tests.
    /// </summary>
    private static RectangleF DrawAbove(Graphics graphics, RectangleF rect, int line, float lineHeight, string text,
        Color colour, float gap)
    {
        var centre = new Vector2(rect.X + rect.Width / 2f, rect.Y - line * lineHeight - gap);
        var size = graphics.MeasureText(text);

        graphics.DrawTextWithBackground(text, centre, colour, FontAlign.Center, Background);

        return new RectangleF(centre.X - size.X / 2f, centre.Y, size.X, size.Y);
    }
}
