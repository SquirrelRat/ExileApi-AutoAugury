using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;
using ImGuiNET;

namespace AutoAugury;

/// <summary>
/// The settings menu's two weight tables, drawn by CustomNodes. A weight equal to its default is not saved, so a
/// changed default reaches every install that never set it. Each table has a Ctrl-gated button that puts its weights
/// back to their defaults.
///
/// The reward table is a searchable, filterable list: a number box per reward family (0 to 100), a bar beside it sized
/// against the highest reward weight, so weights compare at a glance (on a slider from -100 to 100 a weight of 3 and
/// one of 8 sat in the same place), the copies the reward needs, the area level it starts at, and a reset for a weight
/// that differs from its default. The Fate table keeps a slider per Fate from -100 to 100, grouped by faction, since a
/// Fate can be worth avoiding.
/// </summary>
internal static class WeightTables
{
    public const int LowestRewardWeight = 0;
    public const int HighestRewardWeight = 100;

    private const int LowestFateWeight = -100;
    private const int HighestFateWeight = 100;
    private const float SliderWidth = 140f;
    private const float RewardTableHeight = 420f;

    /// <summary>A changed weight's label colour: AutoExpedition's yellow.</summary>
    private static readonly Vector4 ChangedColour = new(1f, 210f / 255f, 80f / 255f, 1f);

    /// <summary>A category heading's colour: AutoExpedition's purple.</summary>
    private static readonly Vector4 CategoryColour = new(190f / 255f, 120f / 255f, 1f, 1f);

    private static readonly string AllCategories = "All categories";

    // What the reward table shows, for this session only: not settings, so not saved.
    private static string _rewardSearch = "";
    private static int _rewardCategory;
    private static bool _changedRewardsOnly;
    private static bool _rewardsByCategory = true;
    private static int _bulkRewardWeight;

    public static void DrawRewardWeights(RewardSettings settings)
    {
        DrawResetButton("Reset reward weights###autoauguryResetRewardWeights", settings.Weights);

        ImGui.SetNextItemWidth(320f);
        ImGui.InputTextWithHint("###autoauguryRewardSearch", "search rewards, e.g. boots speed", ref _rewardSearch,
            128);

        if (_rewardSearch.Length > 0)
        {
            ImGui.SameLine();

            if (ImGui.SmallButton("clear###autoauguryRewardSearchClear"))
                _rewardSearch = "";
        }

        DrawCategoryFilter();
        ImGui.SameLine();
        ImGui.Checkbox("Changed only###autoauguryRewardChangedOnly", ref _changedRewardsOnly);
        ImGui.SameLine();
        ImGui.Checkbox("Group by category###autoauguryRewardGroups", ref _rewardsByCategory);

        var shown = ShownRewardFamilies(settings);

        ImGui.TextDisabled($"Showing {shown.Count} of {RewardFamilies.All.Count}");
        ImGui.SameLine();
        DrawBulkSet(settings, shown);
        DrawRewardTable(settings, shown);
    }

    private static void DrawCategoryFilter()
    {
        var names = new[] { AllCategories }.Concat(RewardFamilies.Categories).ToArray();

        ImGui.SetNextItemWidth(220f);

        if (!ImGui.BeginCombo("###autoauguryRewardCategory", names[Math.Clamp(_rewardCategory, 0, names.Length - 1)]))
            return;

        for (var i = 0; i < names.Length; i++)
        {
            if (ImGui.Selectable($"{names[i]}###autoauguryRewardCategory{i}", i == _rewardCategory))
                _rewardCategory = i;
        }

        ImGui.EndCombo();
    }

    /// <summary>
    /// The families the filters let through, in the order the table lists them: by category and then by weight when
    /// grouped, by weight alone otherwise; ties by label.
    /// </summary>
    private static List<RewardFamily> ShownRewardFamilies(RewardSettings settings)
    {
        var words = _rewardSearch.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var category = _rewardCategory > 0 && _rewardCategory <= RewardFamilies.Categories.Length
            ? RewardFamilies.Categories[_rewardCategory - 1]
            : null;

        var shown = RewardFamilies.All
            .Where(f => category == null || f.Category == category)
            .Where(f => !_changedRewardsOnly || settings.IsChanged(f.Id))
            .Where(f => words.All(w => Mentions(f, w)));

        var ordered = _rewardsByCategory
            ? shown.OrderBy(static f => Array.IndexOf(RewardFamilies.Categories, f.Category))
                .ThenByDescending(f => settings.WeightOf(f.Id))
            : shown.OrderByDescending(f => settings.WeightOf(f.Id));

        return ordered.ThenBy(static f => f.Label, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Whether a search word appears, ignoring case, in a family's label, text or Id. Not its category: "currency"
    /// would then find Stacked Decks and Rare Maps, which sit under Currency and loot; the category list filters by
    /// category.
    /// </summary>
    private static bool Mentions(RewardFamily family, string word) =>
        family.Label.Contains(word, StringComparison.OrdinalIgnoreCase) ||
        family.Description.Contains(word, StringComparison.OrdinalIgnoreCase) ||
        family.Id.Contains(word, StringComparison.OrdinalIgnoreCase);

    /// <summary>Sets every shown family to one weight; Ctrl-gated like the reset, since it changes many.</summary>
    private static void DrawBulkSet(RewardSettings settings, List<RewardFamily> shown)
    {
        ImGui.SetNextItemWidth(70f);
        ImGui.DragInt("###autoauguryRewardBulkWeight", ref _bulkRewardWeight, 0.25f, LowestRewardWeight,
            HighestRewardWeight, "%d", ImGuiSliderFlags.AlwaysClamp);
        ImGui.SameLine();

        var armed = ImGui.IsKeyDown(ImGuiKey.ModCtrl);

        ImGui.BeginDisabled(!armed || shown.Count == 0);
        var clicked = ImGui.Button($"Set all {shown.Count} shown###autoauguryRewardBulkSet");
        ImGui.EndDisabled();

        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip("Sets every reward listed below to the weight on the left.\nHold Ctrl to click it.");

        if (!clicked || !armed)
            return;

        foreach (var family in shown)
            Save(settings.Weights, family.Id, _bulkRewardWeight, family.DefaultWeight);
    }

    private static void DrawRewardTable(RewardSettings settings, List<RewardFamily> shown)
    {
        const ImGuiTableFlags flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV |
                                      ImGuiTableFlags.ScrollY | ImGuiTableFlags.SizingFixedFit;

        if (!ImGui.BeginTable("###autoauguryRewardTable", 6, flags, new Vector2(0f, RewardTableHeight)))
            return;

        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn("Weight", ImGuiTableColumnFlags.WidthFixed, 60f);
        ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, 120f);
        ImGui.TableSetupColumn("Reward", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Copies", ImGuiTableColumnFlags.WidthFixed, 50f);
        ImGui.TableSetupColumn("From level", ImGuiTableColumnFlags.WidthFixed, 70f);
        ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, 45f);
        ImGui.TableHeadersRow();

        // Bars are sized against the highest weight of all families, shown or not, so filtering does not rescale them.
        var highest = Math.Max(1, RewardFamilies.All.Max(f => settings.WeightOf(f.Id)));
        string category = null;

        foreach (var family in shown)
        {
            if (_rewardsByCategory && family.Category != category)
            {
                category = family.Category;
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.TableNextColumn();
                ImGui.TableNextColumn();
                ImGui.TextColored(CategoryColour, category);
            }

            DrawRewardRow(settings, family, highest);
        }

        ImGui.EndTable();
    }

    private static void DrawRewardRow(RewardSettings settings, RewardFamily family, int highest)
    {
        var weight = settings.WeightOf(family.Id);
        var changed = settings.IsChanged(family.Id);

        ImGui.TableNextRow();

        ImGui.TableNextColumn();
        ImGui.SetNextItemWidth(-1f);

        if (ImGui.DragInt($"###autoauguryReward{family.Id}", ref weight, 0.25f, LowestRewardWeight, HighestRewardWeight,
                "%d", ImGuiSliderFlags.AlwaysClamp))
            Save(settings.Weights, family.Id, weight, family.DefaultWeight);

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Drag, or double-click to type.");

        ImGui.TableNextColumn();
        ImGui.ProgressBar(weight / (float)highest, new Vector2(-1f, 0f), "");

        ImGui.TableNextColumn();

        if (changed)
            ImGui.TextColored(ChangedColour, family.Label);
        else
            ImGui.TextUnformatted(family.Label);

        if (ImGui.IsItemHovered())
        {
            var copies = family.CopiesNeeded > 1 ? $"Needs {family.CopiesNeeded} copies of the Fate.\n" : "";

            ImGui.SetTooltip($"{family.Description}\n\n{copies}{family.Category}. From area level {family.MinLevel}. " +
                             $"Default weight {family.DefaultWeight}.");
        }

        ImGui.TableNextColumn();
        ImGui.TextUnformatted(family.CopiesNeeded > 1 ? $"{family.CopiesNeeded}" : "");

        ImGui.TableNextColumn();
        ImGui.TextUnformatted($"{family.MinLevel}");

        ImGui.TableNextColumn();

        if (!changed)
            return;

        if (ImGui.SmallButton($"reset###autoauguryRewardReset{family.Id}"))
            settings.Weights.Remove(family.Id);

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip($"Back to the default, {family.DefaultWeight}.");
    }

    public static void DrawFateWeights(FateSettings settings)
    {
        DrawResetButton("Reset Fate weights###autoauguryResetFateWeights", settings.Weights);

        foreach (var faction in AuguryCatalogue.Factions)
        {
            if (!ImGui.TreeNode($"{faction.Name} ({faction.Id})###autoauguryFaction{faction.Id}"))
                continue;

            var fates = AuguryCatalogue.Fates.Where(f => f.FactionId == faction.Id).OrderBy(static f => f.Name);

            foreach (var fate in fates)
            {
                var weight = settings.WeightOf(fate.Id);

                ImGui.SetNextItemWidth(SliderWidth);

                if (ImGui.SliderInt($"##autoauguryFate{fate.Id}", ref weight, LowestFateWeight, HighestFateWeight))
                    Save(settings.Weights, fate.Id, weight, 0);

                ImGui.SameLine();
                ImGui.TextUnformatted(fate.Name);

                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip(TooltipOf(fate));
            }

            ImGui.TreePop();
        }
    }

    /// <summary>
    /// A Fate's ability at each tier, as its card's full text reads, without the game's markup or the title line (the
    /// title is not always the Fate's name: Rot's text is titled "Rotted").
    /// </summary>
    private static string TooltipOf(AuguryFate fate)
    {
        var lines = new List<string>();

        foreach (var tier in fate.Tiers)
        {
            var body = Regex.Replace(tier.Body ?? "", @"<[^>]*>\{", "").Replace("}", "").Replace("\r", "").Trim();
            var titleEnd = body.IndexOf("\n\n", StringComparison.Ordinal);
            var withoutTitle = titleEnd >= 0 ? body[(titleEnd + 2)..].Trim() : body;

            lines.Add($"Tier {tier.Tier}: {withoutTitle}");
        }

        return string.Join("\n\n", lines);
    }

    private static void Save(Dictionary<string, int> weights, string key, int weight, int defaultWeight)
    {
        if (weight == defaultWeight)
            weights.Remove(key);
        else
            weights[key] = weight;
    }

    private static void DrawResetButton(string label, Dictionary<string, int> weights)
    {
        var armed = ImGui.IsKeyDown(ImGuiKey.ModCtrl);

        ImGui.BeginDisabled(!armed);
        var clicked = ImGui.Button(label);
        ImGui.EndDisabled();

        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip("Puts every weight in this table back to its default.\nHold Ctrl to click it.");

        if (clicked && armed)
            weights.Clear();
    }
}
