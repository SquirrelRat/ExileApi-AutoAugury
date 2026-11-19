using System.Numerics;
using ImGuiNET;

namespace AutoAugury;

/// <summary>
/// The notice at the top of the plugin's settings: that the plugin is in development, then its known limits, each a
/// thing assumed or not counted, so a player can judge the scores. Drawn by AutoAugurySettings.DevelopmentNoticeUi.
/// </summary>
internal static class DevelopmentNotice
{
    /// <summary>Yellow, from AutoExpedition's palette, for the in-development line.</summary>
    private static readonly Vector4 NoticeColour = new(1f, 210f / 255f, 80f / 255f, 1f);

    private const string InDevelopment =
        "This plugin is in development: there may be bugs, and details may be inaccurate.";

    /// <summary>The known limits, one bullet each.</summary>
    private static readonly string[] KnownLimits =
    [
        MapCauldrons.Disclaimer,
        "Potential weight ignores later cards of other Fates that carry a reward you hold part of, and takes a " +
        "later copy to carry the rewards of the weakest copy of its Fate you hold.",
        "Three copies of a Fate are counted as 3.30.0b Hotfix 3 describes (the first and third copies' rewards " +
        "tripled), not yet checked in game; their tier is taken to be the Fate's own, as it was before the hotfix.",
        "Offers are taken to be drawn evenly from the Fates of the Cauldron's faction.",
        "The tier of 7 Fates cannot be read off their card (Dryad, Gravewalker, Pestilent, Sadist, Savage, Tempest, " +
        "Wildfire); it is taken from another card in the area, as every card in an area shows the same tier. It " +
        "only counts when Score per tier is not 0.",
        "Must take rewards (by default Hinekora's Lock, Mirror Shard, Reflecting Mist, Fracturing Orb and Divine " +
        "Orbs) decide the pick before the weights: with " +
        "none held, the card with the weightiest one; with one held, the card that adds most to them, a lone copy " +
        "of a three-copy reward counting a third. The default weights were set for 3.30.0c on 2026-11-19, the " +
        "chase rewards starting from poe.ninja prices at 100 per Divine Orb.",
        "Rewards a patch adds or rewords are learned the first time a card shows them and listed under Reward " +
        "weights as new rewards. Each counts nothing until you set its weight; a message under the cursor says so.",
        "Hard mode rewards are in the game's data but have not been seen offered; they are taken to be Ruthless " +
        "only, and are weighted by their family's weight.",
    ];

    public static void Draw()
    {
        ImGui.PushStyleColor(ImGuiCol.Text, NoticeColour);
        ImGui.TextWrapped(InDevelopment);
        ImGui.PopStyleColor();

        ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetColorU32(ImGuiCol.TextDisabled));
        ImGui.TextWrapped("Known limits:");

        foreach (var limit in KnownLimits)
        {
            ImGui.Bullet();
            ImGui.SameLine();
            ImGui.TextWrapped(limit);
        }

        ImGui.PopStyleColor();
    }
}
