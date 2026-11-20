using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using ExileCore;
using ExileCore.Shared.Attributes;
using ExileCore.Shared.Interfaces;
using ExileCore.Shared.Nodes;
using ImGuiNET;
using Newtonsoft.Json;

namespace AutoAugury;

/// <summary>
/// A heading inside a settings section, drawn as `--- Dump ------------`. Declared as a CustomNode property because
/// that is the only way to put something of a plugin's own into the attribute menu, and marked JsonIgnore because a
/// heading has nothing to save. The menu draws properties in declaration order, so a heading is the property above the
/// first setting it covers. The same pattern as AutoExpedition's and CraftUntilHighlighted's Section.
/// </summary>
internal static class Section
{
    public static CustomNode Of(string title) => new(() => ImGui.SeparatorText(title));
}

public class AutoAugurySettings : ISettings
{
    public AutoAugurySettings()
    {
        DevelopmentNoticeUi = new CustomNode(() => DevelopmentNotice.Draw(this));
        ResetAllSettingsUi = new CustomNode(DrawResetAllSettings);
    }

    /// <summary>
    /// First in the menu: the plugin is in development, its known limits, and Fates per Cauldron under the limit it
    /// answers (DevelopmentNotice). Made in the constructor because it draws a setting.
    /// </summary>
    [JsonIgnore]
    public CustomNode DevelopmentNoticeUi { get; set; }

    /// <summary>
    /// The Fates a Cauldron is taken to offer, on average, for the chance that a later offer shows a Fate again
    /// (CopyPotential.PerOfferChance). Drawn by DevelopmentNotice under its known limit, not by the menu. Offers of 3
    /// and of 4 were seen in one map after patch 3.30.0.4 (the user, 2026-11-19); what decides is not in the client's
    /// data (no stat for it among the patch's Augury stats), so it is the player's to set: 3 to 5 (the user's range),
    /// 3.5 until set, the middle of what was seen.
    /// </summary>
    [IgnoreMenu]
    public RangeNode<float> FatesPerCauldron { get; set; } = new(3.5f, 3f, 5f);

    /// <summary>
    /// The button at the top of the menu that puts every setting back to its default except Enable (SettingsReset).
    /// It is clickable only while Ctrl is held, so a stray click cannot undo the settings, as AutoExpedition's reset
    /// is; its tooltip says both, and shows while the button is disabled too. Made in the constructor because it needs
    /// the settings it resets.
    /// </summary>
    [JsonIgnore]
    public CustomNode ResetAllSettingsUi { get; set; }

    public ToggleNode Enable { get; set; } = new(false);

    /// <summary>
    /// Which built-in changes this saved file has had applied. A saved file holds a value for every setting, so a
    /// changed default never reaches it on its own; a change that must reach saved files raises this and adds a step
    /// to SettingsChanges. The same pattern as AutoExpedition's ConfigVersion.
    /// </summary>
    [IgnoreMenu]
    public int ConfigVersion { get; set; }

    [Submenu(CollapsedByDefault = true)]
    public RewardSettings Rewards { get; set; } = new();

    [Submenu(CollapsedByDefault = true)]
    public FateSettings Fates { get; set; } = new();

    [Submenu(CollapsedByDefault = true)]
    public DisplaySettings Display { get; set; } = new();

    [Submenu(CollapsedByDefault = true)]
    public DebugSettings Debug { get; set; } = new();

    private void DrawResetAllSettings()
    {
        var armed = ImGui.IsKeyDown(ImGuiKey.ModCtrl);

        ImGui.BeginDisabled(!armed);
        var clicked = ImGui.Button("Reset all settings to defaults###autoauguryResetAllSettings");
        ImGui.EndDisabled();

        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip("Resets all settings to their defaults, except Enable.\nHold Ctrl to click it.");

        if (!armed)
        {
            ImGui.SameLine();
            ImGui.TextDisabled("hold Ctrl");
        }

        if (!clicked || !armed)
            return;

        SettingsReset.Apply(this);
        DebugWindow.LogMsg("[AutoAugury] Settings reset to defaults", 5f);
        CursorMessage.Show(CursorMessage.Kind.Success, "Settings reset");
    }
}

/// <summary>
/// The reward weights: what one reward of each family is worth to the player, the scale FateScoring adds up. Kept in a
/// dictionary by family Id, holding only the weights the player changed; the rest are RewardFamilies' defaults. Also
/// the rewards learned from the panel that the catalogue does not have (NewRewardRegistry), whose weights share the
/// dictionary.
/// </summary>
public class RewardSettings
{
    public RewardSettings() => WeightsUi = new CustomNode(() => WeightTables.DrawRewardWeights(this));


    [JsonIgnore]
    public CustomNode WeightsUi { get; set; }

    /// <summary>
    /// The weights the player set, by reward family Id. A weight equal to its default is removed rather than kept, so
    /// a changed default reaches every install that never set that family; a learned reward's weight is kept whatever
    /// it is, since being here is what marks it set. ExileCore fills this on load without replacing it, which for a
    /// dictionary sets each saved entry.
    /// </summary>
    [IgnoreMenu]
    public Dictionary<string, int> Weights { get; set; } = new();

    /// <summary>
    /// The rewards cards have shown that the catalogue does not have, learned by NewRewardRegistry, in the order they
    /// were first seen. ExileCore fills this on load by adding the saved entries to the empty list;
    /// NewRewardRegistry.Tidy drops repeats. A settings reset leaves it (it is not a node), and empties the weights.
    /// </summary>
    [IgnoreMenu]
    public List<NewReward> NewRewards { get; set; } = new();

    /// <summary>
    /// The Must take marks the player changed, by family Id: true marks a family Must take, false clears a default
    /// mark; the rest are RewardFamilies.MustTakeByDefault. A Must take reward decides the pick before the weights do
    /// (MustTakeRule) and is framed purple on its card. A mark equal to its default is removed rather than kept.
    /// </summary>
    [IgnoreMenu]
    public Dictionary<string, bool> MustTake { get; set; } = new();

    /// <summary>Whether a family is Must take: the player's mark if set, else its default.</summary>
    public bool IsMustTake(string family) =>
        family != null &&
        (MustTake.TryGetValue(family, out var marked) ? marked : RewardFamilies.MustTakeByDefault(family));

    /// <summary>Marks a family Must take or not, keeping only a mark that differs from its default.</summary>
    public void SetMustTake(string family, bool mustTake)
    {
        if (mustTake == RewardFamilies.MustTakeByDefault(family))
            MustTake.Remove(family);
        else
            MustTake[family] = mustTake;
    }

    /// <summary>
    /// A family's weight: the player's if set, else its default, 0 for an unknown or learned family; held to the
    /// table's range, 0 to WeightTables.HighestRewardWeight (a reward cannot be worth less than nothing; the first
    /// build allowed -100, so a saved value may be).
    /// </summary>
    public int WeightOf(string family) =>
        Math.Clamp(family != null && Weights.TryGetValue(family, out var weight)
            ? weight
            : RewardFamilies.Of(family)?.DefaultWeight ?? 0, WeightTables.LowestRewardWeight,
            WeightTables.HighestRewardWeight);

    /// <summary>Whether a family's weight differs from its default.</summary>
    public bool IsChanged(string family) => WeightOf(family) != (RewardFamilies.Of(family)?.DefaultWeight ?? 0);

    /// <summary>
    /// Drops every saved weight equal to its family's default, so a weight saved before its default changed to the
    /// same value no longer counts as set (it would stay "changed" in the table, and keep a later default from it).
    /// Run on every start, after NewRewardRegistry.Tidy; a family neither in the catalogue nor learned is dropped too.
    /// The Must take marks are tidied the same way.
    /// </summary>
    public void ForgetWeightsAtDefault()
    {
        var learned = NewRewards.Select(NewRewardRegistry.IdOf).ToHashSet();

        foreach (var family in Weights.Keys.ToList())
        {
            if (learned.Contains(family))
                continue;

            if (RewardFamilies.Of(family) is not { } known || Weights[family] == known.DefaultWeight)
                Weights.Remove(family);
        }

        foreach (var family in MustTake.Keys.ToList())
        {
            if (!learned.Contains(family) && RewardFamilies.Of(family) == null ||
                MustTake[family] == RewardFamilies.MustTakeByDefault(family))
                MustTake.Remove(family);
        }
    }
}

/// <summary>
/// The Fate weights: how much the player wants (above zero) or wants to avoid (below zero) each Fate's ability on the
/// boss, added to an offered card's score. Kept in a dictionary by Fate Id, holding only those set; the rest are 0.
/// </summary>
public class FateSettings
{
    public FateSettings() => WeightsUi = new CustomNode(() => WeightTables.DrawFateWeights(this));

    /// <summary>
    /// Added to a card's score once per tier of its Fate: below zero to prefer lower tiers (milder abilities), above to
    /// prefer higher ones (for 21 of the 25 Fates each tier adds more item rarity, 15, 25 and 60% at tiers 1 to 3, per
    /// their boss mods; Butcher's tier 3 adds 40%, and the three companion-boss Fates add none). Counts nothing for a
    /// card whose tier cannot be read.
    /// </summary>
    [Menu("Score per tier")]
    public RangeNode<int> ScorePerTier { get; set; } = new(0, -50, 50);

    [JsonIgnore]
    public CustomNode WeightsUi { get; set; }

    /// <summary>The weights the player set, by Fate Id; one set back to 0 is removed.</summary>
    [IgnoreMenu]
    public Dictionary<string, int> Weights { get; set; } = new();

    public int WeightOf(string fateId) => fateId != null && Weights.TryGetValue(fateId, out var weight) ? weight : 0;
}

public class DisplaySettings
{
    /// <summary>
    /// Whether each offered Fate's score is drawn above its card, and the best card framed, while the Fates panel
    /// offers a choice. See FateOverlay.
    /// </summary>
    [Menu("Show scores")]
    public ToggleNode ShowScores { get; set; } = new(true);

    /// <summary>
    /// Whether the score is drawn with its parts, "24 (5 + 19)": what the card adds to the best invocation now and its
    /// change in copy potential. Off, the total alone.
    /// </summary>
    [Menu("Show score breakdown")]
    public ToggleNode ShowScoreBreakdown { get; set; } = new(true);

    /// <summary>
    /// Whether, at the boss's Cauldron, the Fates to invoke are framed in the collection with the order to place them
    /// in, and the advice's value is written under the invocation slots. See InvocationAdvisor.
    /// </summary>
    [Menu("Show invocation advice")]
    public ToggleNode ShowInvocationAdvice { get; set; } = new(true);

    /// <summary>
    /// Whether a large icon is drawn on the large map over each Cauldron not yet picked from, kept where the Cauldron
    /// was last seen once it is out of range: the game's Cauldron minimap icon, from the plugin's textures folder. See
    /// CauldronMapIcons.
    /// </summary>
    [Menu("Draw larger minimap icon")]
    public ToggleNode DrawLargerMinimapIcon { get; set; } = new(true);

    /// <summary>
    /// The side of the larger Cauldron icon on the large map, in pixels; the icon's art is 64 px. The default, 40, was
    /// chosen.
    /// </summary>
    [Menu("Minimap icon size")]
    public RangeNode<int> MinimapIconSize { get; set; } = new(40, 8, 128);

    /// <summary>
    /// Whether the Cauldron whose choice waits is framed on the large map in the overlay's yellow: the one you can
    /// pick from now, as opposed to the ones still to come.
    /// </summary>
    [Menu("Mark owed Cauldron icon")]
    public ToggleNode MarkOwedIcon { get; set; } = new(true);

    /// <summary>
    /// Whether the Cauldron whose choice waits is also marked in the world: a ring on the ground at its base, a beam
    /// above it and a frame with "READY" at its top. Hidden while the Fates panel is open, where the player is already
    /// at it. See CauldronWorldMarker.
    /// </summary>
    [Menu("Mark owed Cauldron in the world")]
    public ToggleNode MarkOwedInWorld { get; set; } = new(true);

    /// <summary>The heading the world marker's controls are drawn under.</summary>
    [JsonIgnore]
    public CustomNode WorldMarkerGroup { get; set; } = Section.Of("Owed Cauldron in the world");

    /// <summary>
    /// Whether the frame is drawn around the Cauldron. It is sized in pixels, so it stays the same size at any
    /// distance; the ring and the beam stay in the world.
    /// </summary>
    [Menu("Draw frame")]
    public ToggleNode WorldMarkerFrame { get; set; } = new(true);

    /// <summary>The frame's width in pixels at the Cauldron's base.</summary>
    [Menu("Frame width")]
    public RangeNode<int> WorldMarkerFrameWidth { get; set; } = new(110, 40, 400);

    /// <summary>The frame's height in pixels above the Cauldron's base.</summary>
    [Menu("Frame height")]
    public RangeNode<int> WorldMarkerFrameHeight { get; set; } = new(150, 60, 600);

    /// <summary>The frame's stroke width in pixels.</summary>
    [Menu("Frame thickness")]
    public RangeNode<int> WorldMarkerFrameThickness { get; set; } = new(3, 1, 8);

    /// <summary>Whether the ring on the ground is drawn, which shows the spot even where the terrain slopes.</summary>
    [Menu("Draw ground ring")]
    public ToggleNode WorldMarkerRing { get; set; } = new(true);

    /// <summary>The ground ring's radius in world units (the player is about one unit wide).</summary>
    [Menu("Ring radius")]
    public RangeNode<float> WorldMarkerRingRadius { get; set; } = new(6f, 1f, 20f);

    /// <summary>Whether the beam of stacked rings is drawn above the Cauldron.</summary>
    [Menu("Draw beam")]
    public ToggleNode WorldMarkerBeam { get; set; } = new(true);

    /// <summary>The beam's height in world units.</summary>
    [Menu("Beam height")]
    public RangeNode<float> WorldMarkerBeamHeight { get; set; } = new(10f, 1f, 40f);

    /// <summary>
    /// Whether rewards are named on the panel: a legend under each offered card's icons ("Belt:Elem"), and, at the
    /// boss, one line per reward under the summary strip with the panel's multiplier ("Threads 3x"). See
    /// FateOverlay.DrawRewardLabels and DrawSummaryLegend.
    /// </summary>
    [Menu("Label rewards")]
    public ToggleNode LabelRewards { get; set; } = new(true);
}

public class DebugSettings
{
    [JsonIgnore]
    public CustomNode DumpGroup { get; set; } = Section.Of("Dump");

    /// <summary>
    /// Writes what the plugin can read about the Augury league mechanic to a file: the server's mechanic handlers, the
    /// game's Augury files, the player's server inventories, the entities whose metadata path matches Dump entity
    /// pattern, and the interface's element tree, with what changed since the previous dump. Unbound by default. Works
    /// whether or not the plugin is enabled. See Dump.
    /// </summary>
    [Menu("Dump key",
        "Writes what the plugin can read about the league mechanic to a file,\n" +
        "with what changed since the last dump.")]
    public HotkeyNodeV2 DumpHotkey { get; set; } = new(Keys.None);

    /// <summary>
    /// Takes a dump now, for when the key is not bound. It raises a flag rather than writing anything, because a
    /// CustomNode is handed neither the plugin nor the GameController; the tick that answers the key answers this too.
    /// A dump taken from the button shows the HUD's menu open in the element tree.
    /// </summary>
    [JsonIgnore]
    public CustomNode DumpNowUi { get; set; } = new(() =>
    {
        if (ImGui.Button("Write a dump now###autoauguryDumpNow"))
            Dump.AskedForInSettings = true;

        if (Dump.LastWritten.Length == 0)
            return;

        ImGui.SameLine();
        ImGui.TextDisabled($"last: {Dump.LastWritten}");
    });

    [JsonIgnore]
    public CustomNode DumpFolderUi { get; set; } = new(() =>
    {
        if (ImGui.Button("Open dump folder###autoauguryOpenDumps"))
            DumpFolder.Open();
    });

    /// <summary>How many timestamped dumps to keep, oldest deleted first.</summary>
    [Menu("Max dump files")]
    public RangeNode<int> MaxDumps { get; set; } = new(20, 1, 200);

    /// <summary>
    /// A regular expression, ignoring case, matched against every entity's metadata path; each entity it matches is
    /// written out in full. The default covers the words the client's own Augury names use (GameStat, QuestFlag and
    /// MapIconsIndex: Augury, Cauldron, Bonfire, Effigy) and the league's published words (Coven, Fate, Viscera,
    /// Wickerman). Chosen, not read off any entity: the first dumps say which of these the client's paths use. An
    /// invalid expression falls back to the default, and the dump says so.
    /// </summary>
    [Menu("Dump entity pattern",
        "Entities whose metadata path matches this regular expression\n" +
        "(ignoring case) are written out in full.")]
    public TextNode DumpEntityPattern { get; set; } = new(DefaultDumpEntityPattern);

    public const string DefaultDumpEntityPattern = "Augury|Coven|Cauldron|Bonfire|Effigy|Fate|Viscera|Wicker";

    /// <summary>
    /// How many levels below each of the interface's top-level panels the element tree goes. Chosen:
    /// CraftUntilHighlighted found a stash item 10 levels below the stash element in PoE2, and a choice panel's card
    /// text could sit deeper.
    /// </summary>
    [Menu("Dump element depth")]
    public RangeNode<int> DumpElementDepth { get; set; } = new(24, 4, 40);

    /// <summary>
    /// Whether the element tree descends into hidden elements inside an open panel. On by default: a panel that shows
    /// one choice at a time may hold the others as hidden elements, and those are what the dump is for. Hidden
    /// top-level panels (the stash, the atlas and the rest that are closed) get one line each either way.
    /// </summary>
    [Menu("Dump hidden elements",
        "Also walks hidden elements inside open panels.\n" +
        "Closed panels get one line each either way.")]
    public ToggleNode DumpHiddenElements { get; set; } = new(true);

    /// <summary>
    /// Reads the Fates panel from the element tree even where the HUD's typed window (IngameUi.AuguryFateWindow) is
    /// available. Off by default; on, it keeps the fallback path in use so a stale typed window cannot go unnoticed,
    /// and the dump can compare the two.
    /// </summary>
    [Menu("Read the Fates panel from the element tree",
        "Uses the fallback reader even where the HUD's typed window works.")]
    public ToggleNode ElementTreePanelRead { get; set; } = new(false);
}
