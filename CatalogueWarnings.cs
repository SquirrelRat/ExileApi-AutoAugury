using System.Collections.Generic;
using System.Linq;

namespace AutoAugury;

/// <summary>
/// A message under the cursor (CursorMessage) when the Fates panel shows something the plugin cannot weigh, so a
/// patch's changes do not go unnoticed: a reward just learned (NewRewardRegistry), a learned reward whose weight is not
/// set, reward texts none of which match the catalogue, or a Fate name the catalogue does not have.
///
/// One message per look at the panel: when it opens, when its cards change, and when a reward is learned; the first
/// of those cases that applies, in that order. So the player is told once per offer, not every read (100 ms). Each
/// names the reward in full and where to set its weight, and stays up for WarningSeconds: the first build cut labels
/// at 48 characters and showed them for 4 s, which the user found too short (2026-11-18). The rewards without a weight
/// are also framed in red on their cards (FateOverlay.DrawRewardsWithoutWeight).
/// </summary>
internal static class CatalogueWarnings
{
    /// <summary>The longest label written in a message, a longer one cut; above any catalogue label.</summary>
    private const int MostLabelLength = 90;

    /// <summary>
    /// How long a warning stays under the cursor: 8 s, the user's choice (2026-11-18), its last 2.4 s fading
    /// (CursorMessage's fading share).
    /// </summary>
    private const double WarningSeconds = 8d;

    /// <summary>Where the player sets a weight, as the messages say it.</summary>
    private const string WhereToSet = "Set it in the Rewards section.";

    /// <summary>The panel's title and card names at the last message check; empty while the panel is closed.</summary>
    private static string _cardsChecked = "";

    public static void Update(NewRewardRegistry.Resolution resolution, RewardSettings settings)
    {
        var state = resolution.State;

        if (!state.Visible)
        {
            _cardsChecked = "";
            return;
        }

        var cards = state.Offered.Concat(state.Collection).ToList();
        var checkedNow = $"{state.Title}|{string.Join("|", cards.Select(static c => c.Name))}";

        if (checkedNow == _cardsChecked && resolution.Learned.Count == 0)
            return;

        _cardsChecked = checkedNow;

        if (MessageFor(resolution, cards, settings) is { } message)
            CursorMessage.Show(CursorMessage.Kind.Error, message, WarningSeconds);
    }

    /// <summary>The message for what the panel shows, or null when the plugin can weigh all of it.</summary>
    private static string MessageFor(NewRewardRegistry.Resolution resolution, List<FateCard> cards,
        RewardSettings settings)
    {
        if (resolution.Learned.Count == 1)
            return "New Fate reward without a weight: " +
                   $"{Shortened(NewRewardRegistry.LabelOf(resolution.Learned[0]))}. {WhereToSet}";

        if (resolution.Learned.Count > 1)
            return $"{resolution.Learned.Count} new Fate rewards without a weight. Set them in the Rewards section.";

        if (resolution.TextsUnrecognised)
            return "Fate rewards not recognised: the plugin needs an update.";

        var unweighted = cards.SelectMany(static c => c.Rewards)
            .Where(r => r.Reward != null && NewRewardRegistry.LacksWeight(r, settings))
            .GroupBy(static r => r.Reward.Family)
            .Select(static g => g.First().Reward)
            .ToList();

        if (unweighted.Count == 1)
            return $"No weight for {Shortened(RewardFamilies.LabelOf(unweighted[0].Description))}. {WhereToSet}";

        if (unweighted.Count > 1)
            return $"{unweighted.Count} new Fate rewards have no weight. Set them in the Rewards section.";

        return cards.FirstOrDefault(static c => c.Fate == null) is { } unknown
            ? $"Fate not in the plugin's data: {Shortened(unknown.Name)}"
            : null;
    }

    private static string Shortened(string text) =>
        text.Length <= MostLabelLength ? text : text[..(MostLabelLength - 3)].TrimEnd() + "...";
}
