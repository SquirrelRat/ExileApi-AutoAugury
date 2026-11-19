using System.Numerics;
using ExileCore.Shared.Nodes;
using ImGuiNET;

namespace AutoAugury;

/// <summary>
/// The notice at the top of the plugin's settings: that the plugin is in development, then its known limits, each a
/// thing assumed or not counted, so a player can judge the scores. The last limit, the Fates per Cauldron, has its
/// setting drawn under it, so the player can answer it where it is read. Drawn by
/// AutoAugurySettings.DevelopmentNoticeUi.
/// </summary>
internal static class DevelopmentNotice
{
    /// <summary>Yellow, from AutoExpedition's palette, for the in-development line.</summary>
    private static readonly Vector4 NoticeColour = new(1f, 210f / 255f, 80f / 255f, 1f);

    private const string InDevelopment =
        "This plugin is still in development. Expect bugs and some inaccurate details.";

    /// <summary>
    /// The known limits, one bullet each, in plain words. Cut on 2026-11-19 to what still affects a score with the
    /// default settings: the Cauldron count is read off the game's Fate counter (MapCauldrons), three copies take a
    /// pair's tier (FateValues.TierInvokedAt), a Fate's tier read off another card is exact (every card in an area
    /// shows one tier, FatesPanel.WithAreaTier), and Hard mode rewards are not offered outside Ruthless, so none of
    /// them is a limit.
    /// </summary>
    private static readonly string[] KnownLimits =
    [
        "Must take rewards are always picked first, except against other Must take rewards. By default these are " +
        "Hinekora's Lock, Mirror Shard, Reflecting Mist, Fracturing Orb and Divine Orbs.",
        "The default weights are based on poe.ninja prices from 2026-11-19, with a Divine Orb worth 100.",
        "Scores include the chance of getting another copy of a Fate later. This assumes every Fate of the " +
        "Cauldron's faction is equally likely to be offered, and that a later copy has the same rewards as your " +
        "weakest copy. Other Fates that could add to a reward you have started don't count towards this potential; " +
        "if one is offered, it is still scored and picked as normal.",
        "Rewards added in a later patch are marked with a red border, and will have no weight until you set one in " +
        "the Rewards section.",
    ];

    /// <summary>The last known limit, with Fates per Cauldron drawn under it; the user's words (2026-11-19).</summary>
    private const string FatesPerCauldronLimit =
        "A Cauldron seems to offer a random number of Fate cards to choose from. I've guessed an average of 3.5, " +
        "feel free to adjust it below. This impacts the potential weight for later picks.";

    public static void Draw(AutoAugurySettings settings)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, NoticeColour);
        ImGui.TextWrapped(InDevelopment);
        ImGui.PopStyleColor();

        ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetColorU32(ImGuiCol.TextDisabled));
        ImGui.TextWrapped("Known limits:");

        foreach (var limit in KnownLimits)
            DrawBullet(limit);

        DrawBullet(FatesPerCauldronLimit);
        ImGui.PopStyleColor();

        DrawFatesPerCauldron(settings.FatesPerCauldron);
    }

    private static void DrawBullet(string text)
    {
        ImGui.Bullet();
        ImGui.SameLine();
        ImGui.TextWrapped(text);
    }

    /// <summary>
    /// The Fates per Cauldron slider, indented under its bullet, one decimal (ImGui rounds the value to the format).
    /// </summary>
    private static void DrawFatesPerCauldron(RangeNode<float> fatesPerCauldron)
    {
        ImGui.Indent();

        var value = fatesPerCauldron.Value;

        if (ImGui.SliderFloat("Fates per Cauldron###autoauguryfatespercauldron", ref value, fatesPerCauldron.Min,
                fatesPerCauldron.Max, "%.1f"))
            fatesPerCauldron.Value = value;

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("A fraction such as 3.5 is an average: 3 or 4, equally likely.\nCtrl+click to type.");

        ImGui.Unindent();
    }
}
