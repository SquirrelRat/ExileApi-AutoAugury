using System;
using System.Numerics;
using ExileCore;
using Color = SharpDX.Color;
using Graphics = ExileCore.Graphics;

namespace AutoAugury;

/// <summary>
/// The last message, written under the mouse cursor for a few seconds and faded out over the last part of that. One at
/// a time: a new message replaces the one showing. MonsterModAlerter's, which is CraftUntilHighlighted's with a fixed
/// duration; both take it from AutoExpedition's CursorWarning, which moved its warnings beside the cursor because
/// players did not notice them anywhere else. Messages are a few words, not sentences; a warning the player must act
/// on (CatalogueWarnings) may say where, and is shown for longer.
/// </summary>
internal static class CursorMessage
{
    public enum Kind
    {
        Error,
        Success,
        Info,
    }

    /// <summary>A message's time unless Show says otherwise. AutoExpedition's warnings default to 4 s.</summary>
    public const double DefaultShownSeconds = 4d;

    /// <summary>The share of a message's time spent fading out, at least a second. Chosen.</summary>
    private const double FadingShare = 0.3d;

    /// <summary>AutoExpedition's warning red.</summary>
    private static readonly Color ErrorColour = Colours.FromArgb(255, 235, 90, 90);

    /// <summary>AutoExpedition's purple.</summary>
    private static readonly Color SuccessColour = Colours.FromArgb(255, 190, 120, 255);

    /// <summary>AutoExpedition's white (its common chest colour).</summary>
    private static readonly Color InfoColour = Colours.FromArgb(255, 245, 245, 245);

    private static (string Text, Kind Kind, DateTime At, double Seconds) _shown =
        ("", Kind.Info, DateTime.MinValue, DefaultShownSeconds);

    /// <summary>Shows a message for the given number of seconds, replacing any before it.</summary>
    public static void Show(Kind kind, string text, double seconds = DefaultShownSeconds) =>
        _shown = (text ?? "", kind, DateTime.UtcNow, seconds);

    /// <summary>Draws the message showing, if any.</summary>
    public static void Draw(Graphics graphics, GameController gameController)
    {
        if (_shown.Text.Length == 0)
            return;

        var age = (DateTime.UtcNow - _shown.At).TotalSeconds;

        if (age >= _shown.Seconds)
            return;

        var cursor = Safe.Read(gameController,
            static g => new Vector2(g.IngameState.MousePosX, g.IngameState.MousePosY), new Vector2(-1f, -1f));
        var window = Safe.Read(gameController, static g => g.Window.GetWindowRectangle(), default);

        if (cursor.X < 0f || window.Width <= 0f)
            return;

        var size = graphics.MeasureText(_shown.Text);

        // Below and right of the pointer, kept inside the window so a message raised near an edge is not cut off.
        var at = new Vector2(
            Math.Clamp(cursor.X + 16f, 0f, Math.Max(0f, window.Width - size.X - 4f)),
            Math.Clamp(cursor.Y + 44f, 0f, Math.Max(0f, window.Height - size.Y - 4f)));

        // Fades over the last part of its time rather than vanishing mid-read.
        var fading = Math.Max(1d, _shown.Seconds * FadingShare);
        var alpha = (int)(255 * Math.Clamp((_shown.Seconds - age) / fading, 0d, 1d));

        var colour = _shown.Kind switch
        {
            Kind.Error => ErrorColour,
            Kind.Success => SuccessColour,
            _ => InfoColour,
        };

        graphics.DrawTextWithBackground(_shown.Text, at, Colours.FromArgb(alpha, colour),
            Colours.FromArgb(alpha * 200 / 255, Color.Black));
    }
}
