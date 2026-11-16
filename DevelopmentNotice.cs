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
        "Potential weight ignores later cards of other Fates that carry a reward you hold part of, and treats a " +
        "later copy as having no rewards of its own, so it errs low.",
        "Offers are taken to be drawn evenly from the Fates of the Cauldron's faction.",
        "The tier of 7 Fates cannot be read (Dryad, Gravewalker, Pestilent, Sadist, Savage, Tempest, Wildfire); it " +
        "only counts when Score per tier is not 0.",
        "Hard mode rewards are in the game's data but have not been seen offered; they are taken to be Ruthless " +
        "only, and are weighted by their family's weight. Rewards > Show Ruthless-only families hides them.",
        "Tables exported " + AuguryCatalogue.ExportedAt + " (" + AuguryCatalogue.Schema + "): regenerate the " +
        "catalogue after a patch that changes the Augury tables.",
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
