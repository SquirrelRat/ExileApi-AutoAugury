using System.Collections.Generic;
using System.Numerics;
using ExileCore;
using ExileCore.Shared.Enums;
using Color = SharpDX.Color;

namespace AutoAugury;

/// <summary>
/// Marks in the world the Cauldrons whose choice waits, the same ones CauldronMapIcons frames on the large map: a
/// yellow ring on the ground at the Cauldron's base and "READY" above it, so the one you can click is visible across
/// the arena rather than only on the map. The position is the Cauldron's last grid position with the terrain's height
/// under it (IngameState.Data.ToWorldWithTerrainHeight), so the ring lies on the ground where it slopes, and it draws
/// as long as the area is loaded even if the entity has left the entity list. Hidden while the Fates panel is open:
/// the player is standing at it then.
/// </summary>
internal static class CauldronWorldMarker
{
    /// <summary>The ring's radius in world units: a little wider than the Cauldron's base.</summary>
    private const float RingRadius = 2.5f;

    /// <summary>Segments: enough to read as a circle at the distances the marker is useful from.</summary>
    private const int RingSegments = 48;

    /// <summary>How far above the projected base the label sits, in pixels.</summary>
    private const float LabelHeight = 40f;

    /// <summary>The owed Cauldron's colour: the map frame's yellow (CauldronMapIcons).</summary>
    private static readonly Color OwedColour = Colours.FromArgb(255, 255, 210, 80);

    private static readonly Color LabelBackground = Colours.FromArgb(170, 0, 0, 0);

    /// <param name="fatesPanelOpen">Whether the last read found the Fates panel open (FatesPanelState.Visible).</param>
    public static void Draw(Graphics graphics, GameController gc, IReadOnlyList<TrackedCauldron> cauldrons,
        bool fatesPanelOpen)
    {
        if (cauldrons.Count == 0 || fatesPanelOpen)
            return;

        var owed = OwedOf(cauldrons);

        if (owed.Count == 0)
            return;

        var data = Safe.Read(gc, static g => g.IngameState?.Data, null);
        var camera = Safe.Read(gc, static g => g.IngameState?.Camera, null);

        if (data == null || camera == null)
            return;

        foreach (var cauldron in owed)
        {
            if (cauldron.GridPos is not { } grid)
                continue;

            var world = Safe.Read(data, d => d.ToWorldWithTerrainHeight(grid), default(Vector3));

            graphics.DrawCircleInWorld(world, RingRadius, OwedColour, 2f, RingSegments, true);

            var at = Safe.Read(camera, c => c.WorldToScreen(world), default(Vector2));

            graphics.DrawTextWithBackground("READY", new Vector2(at.X, at.Y - LabelHeight), OwedColour,
                FontAlign.Center, LabelBackground);
        }
    }

    /// <summary>
    /// The Cauldrons the marker is drawn for: those whose choice waits (CauldronStage.Owed, targetable or its
    /// "Select a Fate" label up) and whose last seen grid position is known. The same set CauldronMapIcons frames.
    /// </summary>
    public static List<TrackedCauldron> OwedOf(IReadOnlyList<TrackedCauldron> cauldrons)
    {
        var owed = new List<TrackedCauldron>();

        foreach (var cauldron in cauldrons ?? [])
        {
            if (cauldron.Stage == CauldronStage.Owed && cauldron.GridPos is not null)
                owed.Add(cauldron);
        }

        return owed;
    }
}
