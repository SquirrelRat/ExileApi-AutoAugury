using System;
using System.Collections.Generic;
using System.Numerics;
using ExileCore;
using ExileCore.Shared.Enums;
using Color = SharpDX.Color;

namespace AutoAugury;

/// <summary>
/// Marks in the world the Cauldrons whose choice waits, the same ones CauldronMapIcons frames on the large map: a
/// yellow ring on the ground at the Cauldron's base, a fading pillar above it and "READY" at its top, so the one you
/// can click is visible across the arena rather than only on the map. The position is the Cauldron's last grid
/// position with the terrain's height under it (IngameState.Data.ToWorldWithTerrainHeight), so the ring lies on the
/// ground where it slopes, and it draws as long as the area is loaded even if the entity has left the entity list.
/// Hidden while the Fates panel is open: the player is standing at it then.
/// </summary>
internal static class CauldronWorldMarker
{
    /// <summary>The ring's radius in world units, at 100% (Display > World marker size).</summary>
    private const float RingRadius = 4f;

    /// <summary>Segments: enough to read as a circle at the distances the marker is useful from.</summary>
    private const int RingSegments = 64;

    private const float RingThickness = 4f;

    /// <summary>How tall the pillar of fading rings stands, in world units at 100%.</summary>
    private const float PillarHeight = 6f;

    private const int PillarRings = 6;

    /// <summary>How far above the pillar's top the label sits, in pixels.</summary>
    private const float LabelGap = 12f;

    /// <summary>The owed Cauldron's colour: the map frame's yellow (CauldronMapIcons).</summary>
    private static readonly Color OwedColour = Colours.FromArgb(255, 255, 210, 80);

    private static readonly Color LabelBackground = Colours.FromArgb(210, 0, 0, 0);

    /// <param name="fatesPanelOpen">Whether the last read found the Fates panel open (FatesPanelState.Visible).</param>
    /// <param name="scale">The marker's size, 1 at 100% (Display > World marker size).</param>
    public static void Draw(Graphics graphics, GameController gc, IReadOnlyList<TrackedCauldron> cauldrons,
        bool fatesPanelOpen, float scale)
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

        scale = Math.Clamp(scale, 0.5f, 3f);

        var radius = RingRadius * scale;
        var height = PillarHeight * scale;
        var thickness = RingThickness * MathF.Sqrt(scale);
        var up = new Vector3(0f, 0f, 1f);

        // A gentle pulse, so the marker reads in a busy fight without flashing.
        var pulse = 0.75f + 0.25f * MathF.Sin((float)DateTime.Now.TimeOfDay.TotalSeconds * 4f);

        foreach (var cauldron in owed)
        {
            if (cauldron.GridPos is not { } grid)
                continue;

            var world = Safe.Read(data, d => d.ToWorldWithTerrainHeight(grid), default(Vector3));

            // A soft disc under the ring, so the base reads even where the ground is dark.
            graphics.DrawFilledCircleInWorld(world, radius, Colours.FromArgb(45, OwedColour), RingSegments, true);
            graphics.DrawCircleInWorld(world, radius, Colours.FromArgb((int)(255 * pulse), OwedColour), thickness,
                RingSegments, true);

            // The pillar: rings stacked up, fading and shrinking, which reads as a beam from across the arena.
            for (var i = 1; i <= PillarRings; i++)
            {
                var faded = Colours.FromArgb((int)(170f * (1f - (float)i / (PillarRings + 1)) * pulse), OwedColour);

                graphics.DrawCircleInWorld(world + up * (height * i / PillarRings), radius * (1f - 0.08f * i),
                    faded, thickness * 0.7f, RingSegments, false);
            }

            var top = Safe.Read(camera, c => c.WorldToScreen(world + up * height), default(Vector2));

            graphics.DrawTextWithBackground("READY", new Vector2(top.X, top.Y - LabelGap), OwedColour,
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
