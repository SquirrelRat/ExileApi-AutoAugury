using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using ExileCore;
using ExileCore.Shared.Enums;
using ExileCore.Shared.Helpers;
using Color = SharpDX.Color;
using RectangleF = SharpDX.RectangleF;

namespace AutoAugury;

/// <summary>
/// Draws a large icon on the large map over each Cauldron of the area not yet picked from, where CauldronTracker last
/// saw it: a Cauldron that has left the entity list keeps its icon where it was. A done Cauldron gets none, as the game
/// hides its own icon once it is picked from (MinimapIcon hide True, dumps of 2026-11-14).
///
/// The icon is the game's Cauldron minimap icon, Art/2DArt/minimap/player/AuguryEncounter (64 x 64), shipped in the
/// plugin as textures/AuguryEncounter.png (taken from poedb.tw's copy of the game's art, 2026-11-14). It is not drawn
/// from the HUD's Icons.png: MapIconsIndex.AuguryEncounter points into the last rows of that atlas (896 x 3328, 14 x 52
/// cells of 64 px), and those two rows are empty in the atlas shipped with the HUD of 2026-11-14 (0 visible pixels in
/// its cell, as in those of AuguryEffigy and AuguryBossObject), so it drew nothing. When the file cannot be loaded, a
/// purple star from the atlas (FallbackIcon) stands in.
///
/// The Cauldrons are the tracker's last read. The large map's visibility, the side panels open over it and the map
/// projection (Graphics.GridToMap, which follows the camera and the map's zoom and pan) are read each frame, as
/// MonsterModAlerter's MatchDrawing reads them. Nothing is drawn while the Fates panel is open or a panel in
/// ExileCore's FullscreenPanels or LargePanels is (as MonsterModAlerter hides its overlay), since the game draws its
/// panels over the large map; a side panel only cuts its part of the screen out. The corner map is not drawn on. What
/// the last frame saw is kept for the dump (LastFrameOf).
/// </summary>
internal static class CauldronMapIcons
{
    /// <summary>The HUD's shared icon atlas, the one MinimapIcons draws from.</summary>
    private const string IconAtlasTexture = "Icons.png";

    /// <summary>The icon file, relative to the plugin's folder, where the project copies it.</summary>
    private const string IconFile = "textures/AuguryEncounter.png";

    /// <summary>The name the icon file is loaded under; the HUD's images are shared by every plugin.</summary>
    private const string IconTexture = "AutoAugury.AuguryEncounter.png";

    /// <summary>
    /// The atlas icon drawn when the icon file did not load: purple, the plugin's colour, and present in the atlas
    /// (checked by its UV).
    /// </summary>
    private const MapIconsIndex FallbackIcon = MapIconsIndex.LootFilterLargePurpleStar;

    private static bool _iconLoaded;

    /// <summary>The owed Cauldron's frame: the overlay's yellow for the other picks.</summary>
    private static readonly Color OwedColour = Colours.FromArgb(255, 255, 210, 80);

    /// <summary>One Cauldron as the last frame placed it: its map point, or why it was not drawn.</summary>
    private readonly record struct Placement(uint Id, CauldronStage Stage, Vector2? Grid, Vector2? Point,
        string Outcome);

    private static readonly List<Placement> LastPlacements = new();
    private static DateTime _lastFrameAt;
    private static bool _lastLargeMapVisible;
    private static string _lastHiddenBy;
    private static RectangleF _lastRegion;
    private static int _lastCauldronCount;

    /// <param name="iconSize">The icon's side in pixels (Display > Minimap icon size).</param>
    /// <param name="fatesPanelOpen">Whether the last read found the Fates panel open (FatesPanelState.Visible).</param>
    /// <param name="markOwed">Whether to frame the icon of the Cauldron whose choice waits (Display > Mark owed Cauldron icon).</param>
    public static void Draw(Graphics graphics, GameController gc, IReadOnlyList<TrackedCauldron> cauldrons,
        int iconSize, bool fatesPanelOpen, bool markOwed)
    {
        _lastFrameAt = DateTime.Now;
        _lastCauldronCount = cauldrons.Count;
        _lastLargeMapVisible = Safe.Read(gc, static g => g.IngameState.IngameUi.Map.LargeMap.IsVisibleLocal, false);
        _lastHiddenBy = null;
        _lastRegion = default;
        LastPlacements.Clear();

        if (cauldrons.Count == 0 || !_lastLargeMapVisible)
            return;

        // Unreadable reads as open, which draws nothing rather than over a panel.
        _lastHiddenBy = fatesPanelOpen
            ? "the Fates panel"
            : Safe.Read(gc, static g => g.IngameState.IngameUi.FullscreenPanels.Any(static p => p.IsVisible) ||
                                        g.IngameState.IngameUi.LargePanels.Any(static p => p.IsVisible), true)
                ? "a full-screen or large panel"
                : null;

        if (_lastHiddenBy != null)
            return;

        var region = DrawRegionOf(gc);

        _lastRegion = region;

        if (region.Width <= 0f || region.Height <= 0f)
            return;

        var fallbackUv = SpriteHelper.GetUV(FallbackIcon);
        var half = iconSize / 2f;

        // Clipped to the region, so an icon under an open side panel is cut at its edge.
        using (graphics.BeginRectClip(region, true))
        {
            foreach (var cauldron in cauldrons)
            {
                if (cauldron.Stage == CauldronStage.Done || cauldron.GridPos is not { } grid)
                {
                    LastPlacements.Add(new Placement(cauldron.Id, cauldron.Stage, cauldron.GridPos, null,
                        cauldron.Stage == CauldronStage.Done ? "done" : "no position"));
                    continue;
                }

                var point = Safe.Read((Graphics: graphics, Grid: grid),
                    static s => (Vector2?)s.Graphics.GridToMap(s.Grid, s.Grid), null);

                if (point is not { } p)
                {
                    LastPlacements.Add(new Placement(cauldron.Id, cauldron.Stage, grid, null, "projection failed"));
                    continue;
                }

                if (p.X + half < region.Left || p.X - half > region.Right || p.Y + half < region.Top ||
                    p.Y - half > region.Bottom)
                {
                    LastPlacements.Add(new Placement(cauldron.Id, cauldron.Stage, grid, p, "outside the region"));
                    continue;
                }

                var rect = new RectangleF(p.X - half, p.Y - half, iconSize, iconSize);

                if (_iconLoaded)
                    graphics.DrawImage(IconTexture, rect, Color.White);
                else
                    graphics.DrawImage(IconAtlasTexture, rect, fallbackUv, Color.White);

                if (markOwed && cauldron.Stage == CauldronStage.Owed)
                    graphics.DrawFrame(rect, OwedColour, 2);

                LastPlacements.Add(new Placement(cauldron.Id, cauldron.Stage, grid, p, "drawn"));
            }
        }
    }

    /// <summary>
    /// Loads the icon file from the plugin's folder (BaseSettingsPlugin.DirectoryFullName, where the project copies
    /// textures/), and the HUD's icon atlas for the fallback, as ExpeditionIcons and DeepwaterEngagementSuite load it,
    /// so neither depends on another plugin. Called once, from Initialise.
    /// </summary>
    public static void LoadIcons(Graphics graphics, string pluginDirectory)
    {
        var path = pluginDirectory == null ? null : Path.Combine(pluginDirectory, IconFile);

        _iconLoaded = path != null && File.Exists(path) &&
                      Safe.Read(() => graphics.InitImage(IconTexture, path), false);
        Safe.Try(() => graphics.InitImage(IconAtlasTexture));
    }

    /// <summary>What the last frame that called Draw saw, for the dump; says so when Draw has not run.</summary>
    public static string LastFrameOf()
    {
        if (_lastFrameAt == default)
            return "not run since the plugin loaded (Draw larger minimap icon off, or the plugin disabled)";

        var cauldrons = LastPlacements.Count == 0
            ? ""
            : ": " + string.Join(", ", LastPlacements.Select(static p =>
                $"id {p.Id} {p.Stage}" + (p.Grid is { } g ? $" grid <{g.X:0}, {g.Y:0}>" : "") +
                (p.Point is { } m ? $" map ({m.X:0}, {m.Y:0})" : "") + $" {p.Outcome}"));

        var icon = _iconLoaded ? IconFile : "the atlas star (icon file not loaded)";

        return $"{_lastFrameAt:HH:mm:ss.fff}, icon {icon}, " +
               $"large map visible {_lastLargeMapVisible}, " +
               (_lastHiddenBy == null ? "" : $"hidden by {_lastHiddenBy}, ") +
               $"{_lastCauldronCount} Cauldron(s) known, region ({_lastRegion.X:0},{_lastRegion.Y:0} " +
               $"{_lastRegion.Width:0}x{_lastRegion.Height:0}){cauldrons}";
    }

    /// <summary>
    /// The window less the side panels open over it, which cover the large map; MonsterModAlerter's DrawRegionOf with
    /// Prevent drawing over panels on.
    /// </summary>
    private static RectangleF DrawRegionOf(GameController gc)
    {
        var region = Safe.Read(gc, static g => g.Window.GetWindowRectangleTimeCache, default(RectangleF)) with
        {
            Location = SharpDX.Vector2.Zero,
        };
        var rightPanelLeft = Safe.Read(gc, static g => g.IngameState.IngameUi.OpenRightPanel is { IsVisible: true } p
            ? p.GetClientRectCache.Left
            : (float?)null, null);
        var leftPanelRight = Safe.Read(gc, static g => g.IngameState.IngameUi.OpenLeftPanel is { IsVisible: true } p
            ? p.GetClientRectCache.Right
            : (float?)null, null);

        if (rightPanelLeft is { } right)
            region.Right = right;

        if (leftPanelRight is { } left)
            region.Left = left;

        return region;
    }
}
