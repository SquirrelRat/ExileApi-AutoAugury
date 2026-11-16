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
    public AutoAugurySettings() => ResetAllSettingsUi = new CustomNode(DrawResetAllSettings);

    /// <summary>First in the menu: the plugin is in development, and its known limits (DevelopmentNotice).</summary>
    [JsonIgnore]
    public CustomNode DevelopmentNoticeUi { get; set; } = new(DevelopmentNotice.Draw);

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
    /// to AutoAugury.ApplySettingsChanges. The same pattern as AutoExpedition's ConfigVersion.
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
/// dictionary by family Id, holding only the weights the player changed; the rest are RewardFamilies' defaults.
/// </summary>
public class RewardSettings
{
    public RewardSettings() => WeightsUi = new CustomNode(() => WeightTables.DrawRewardWeights(this));


    [JsonIgnore]
    public CustomNode WeightsUi { get; set; }

    /// <summary>
    /// Whether the table lists the Ruthless-only families (RewardFamilies.IsHardModeOnly): three rows of rewards
    /// never seen offered. Scoring is by family either way; this only cleans the table.
    /// </summary>
    [Menu("Show Ruthless-only families",
        "Lists the families whose rewards are taken to be Ruthless-only.\n" +
        "Scoring counts their families either way.")]
    public ToggleNode ShowHardModeFamilies { get; set; } = new(true);

    /// <summary>
    /// The weights the player set, by reward family Id. A weight equal to its default is removed rather than kept, so
    /// a changed default reaches every install that never set that family. ExileCore fills this on load without
    /// replacing it, which for a dictionary sets each saved entry.
    /// </summary>
    [IgnoreMenu]
    public Dictionary<string, int> Weights { get; set; } = new();

    /// <summary>
    /// A family's weight: the player's if set, else its default, 0 for an unknown family; held to the table's range,
    /// 0 to 100 (a reward cannot be worth less than nothing; the first build allowed -100, so a saved value may be).
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
    /// Run on every start; a family no longer in the catalogue is dropped too.
    /// </summary>
    public void ForgetWeightsAtDefault()
    {
        foreach (var family in Weights.Keys.ToList())
        {
            if (RewardFamilies.Of(family) is not { } known || Weights[family] == known.DefaultWeight)
                Weights.Remove(family);
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
    /// Whether the overlay uses the colourblind-safe set (Display > High-contrast overlay palette): Okabe-Ito green,
    /// orange and vermillion instead of green, yellow and red, with sky blue for empowering copies. The order badges
    /// say the picks apart either way.
    /// </summary>
    [Menu("High-contrast overlay palette")]
    public ToggleNode HighContrastPalette { get; set; } = new(false);

    /// <summary>
    /// The overlay frames' thickness, in pixels; the next pick to place gets the full thickness, the later picks one
    /// less (never below one).
    /// </summary>
    [Menu("Frame thickness")]
    public RangeNode<int> FrameThickness { get; set; } = new(3, 1, 6);

    /// <summary>The gap between a card and the score label above it, in pixels.</summary>
    [Menu("Score line gap")]
    public RangeNode<int> ScoreLineGap { get; set; } = new(2, 0, 30);

    /// <summary>
    /// Whether the large map's Cauldron icons mark the Cauldron whose choice waits: a frame around its icon, so the
    /// owed Cauldron stands out from the ones still to run.
    /// </summary>
    [Menu("Mark owed Cauldron icon")]
    public ToggleNode MarkOwedIcon { get; set; } = new(true);

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
    /// Whether a one-line summary is drawn at the screen's bottom left while playing: Cauldrons tracked, invocation
    /// slots and Fates modelled per offer. Off by default: it is for checking the tracker's state at a glance.
    /// </summary>
    [Menu("Show status line")]
    public ToggleNode ShowStatusLine { get; set; } = new(false);

    public DisplaySettings()
    {
        ChoosingNoteUi = new CustomNode(() => ImGui.TextDisabled(
            "The game hides each Cauldron's offer until the previous Cauldron is picked from: " +
            "scores cover this Cauldron only."));
        PreviewUi = new CustomNode(() =>
        {
            if (ImGui.Button("Preview overlay on screen###autoauguryPreviewScores"))
                PreviewScores = !PreviewScores;

            ImGui.SameLine();
            ImGui.TextDisabled(PreviewScores
                ? "preview on: close the menu to see it"
                : "draws three sample scores with your palette");
        });
    }

    /// <summary>Not saved: whether the sample scores show (Display > Preview overlay on screen).</summary>
    [JsonIgnore]
    public bool PreviewScores { get; set; }

    [JsonIgnore]
    public CustomNode ChoosingNoteUi { get; set; }

    [JsonIgnore]
    public CustomNode PreviewUi { get; set; }
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

    [JsonIgnore]
    public CustomNode OfferModelGroup { get; set; } = Section.Of("Offer model");

    /// <summary>
    /// How many Fates one Cauldron offer shows, for the copy potential's per-offer chance. Every offer seen showed
    /// three (2026-11-14); when offers show another number, set it here rather than re-scoring on a wrong model.
    /// </summary>
    [Menu("Fates per offer")]
    public RangeNode<int> FatesPerOffer { get; set; } = new(CopyPotential.FatesPerOffer, 1, 5);
}
