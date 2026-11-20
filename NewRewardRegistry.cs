using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace AutoAugury;

/// <summary>
/// A reward a Fate card showed that AuguryCatalogue does not have: one a patch added or reworded after the catalogue
/// was made. Kept in the saved settings (RewardSettings.NewRewards) from the first time a card shows it, so it has a
/// row in the reward weight table like the catalogue's rewards. Public, as a type the settings save must be.
/// </summary>
public class NewReward
{
    /// <summary>The reward's text as the panel shows it, without the copies note, line breaks kept.</summary>
    public string Text { get; set; } = "";

    /// <summary>The copies of its Fate the panel says it needs: 1, 2 or 3.</summary>
    public int CopiesNeeded { get; set; } = 1;

    /// <summary>The area level of the area it was first seen in.</summary>
    public int FirstSeenAreaLevel { get; set; }

    /// <summary>The day it was first seen, yyyy-MM-dd.</summary>
    public string FirstSeen { get; set; } = "";
}

/// <summary>
/// Keeps the scores working when a patch adds or rewords Fate rewards before AuguryCatalogue is regenerated.
///
/// A reward whose text matches no catalogue reward is learned the first time a card shows it: saved as a NewReward,
/// given a family of its own (Id "New" and a hash of its copies and text, the same on every run), and scored by that
/// family's weight like any other reward. Until the player sets that weight it counts nothing: the score carries " ?"
/// (FateScore.UnknownRewards) and CatalogueWarnings names it under the cursor.
///
/// A reward the game stops offering needs nothing: no card shows it, so its weight never counts. After a catalogue
/// regeneration RewardSettings.ForgetWeightsAtDefault drops the weights of families gone from the catalogue, and Tidy
/// drops each learned reward the catalogue now has, carrying its weight across.
///
/// A panel read in which no reward matches the catalogue learns nothing: that every reward on screen is new is less
/// likely than the panel's text having changed form (markup, line breaks) so that none matches, and learning them all
/// would fill the table with copies of rewards the catalogue has. Resolution.TextsUnrecognised says so instead.
/// </summary>
internal static class NewRewardRegistry
{
    /// <summary>The start of every learned family's Id; no catalogue family Id starts with it (2026-11-18).</summary>
    public const string IdPrefix = "New";

    /// <summary>The category a learned reward's family is given (RewardFamily.Category).</summary>
    public const string Category = "New rewards";

    /// <summary>A learned family's Id: IdPrefix and eight hex digits.</summary>
    private static readonly int IdLength = IdPrefix.Length + 8;

    /// <summary>What one panel read became once its unmatched rewards were looked up among the learned ones.</summary>
    /// <param name="State">The panel as read, each reward that is a learned one given its learned reward.</param>
    /// <param name="Learned">The rewards this read learned, in the order the cards show them.</param>
    /// <param name="TextsUnrecognised">
    /// Whether rewards were left unmatched because no reward on the panel matched the catalogue (nothing learned).
    /// </param>
    internal sealed record Resolution(FatesPanelState State, IReadOnlyList<NewReward> Learned, bool TextsUnrecognised);

    /// <summary>A learned reward's family Id: "New" and the FNV-1a hash of its copies and collapsed text.</summary>
    public static string IdOf(NewReward reward) => IdOf(reward.Text, reward.CopiesNeeded);

    /// <summary>Whether a family Id is a learned reward's rather than a catalogue family's.</summary>
    public static bool IsLearnedFamily(string familyId) =>
        familyId != null && familyId.Length == IdLength && familyId.StartsWith(IdPrefix, StringComparison.Ordinal);

    /// <summary>
    /// Whether a shown reward counts nothing for want of a weight: it matched no catalogue reward and is not learned,
    /// or it is learned and the player has not set its weight (a weight set to 0 counts as set).
    /// </summary>
    public static bool LacksWeight(FateCardReward shown, RewardSettings settings) =>
        shown.Reward == null ||
        IsLearnedFamily(shown.Reward.Family) && !settings.Weights.ContainsKey(shown.Reward.Family);

    /// <summary>A short name for a learned reward, made from its text as a catalogue family's label is.</summary>
    public static string LabelOf(NewReward reward) => RewardFamilies.LabelOf(reward.Text);

    /// <summary>
    /// The panel read with each unmatched reward looked up among the learned rewards; with learn, one not learned yet
    /// is learned (added to settings.NewRewards), unless no reward on the panel matched the catalogue.
    /// </summary>
    public static Resolution Resolve(FatesPanelState state, RewardSettings settings, int areaLevel, bool learn)
    {
        var shown = state.Offered.Concat(state.Collection).SelectMany(static c => c.Rewards).ToList();

        if (shown.All(static r => r.Reward != null))
            return new Resolution(state, [], false);

        var anyMatched = shown.Any(static r => r.Reward != null && !IsLearnedFamily(r.Reward.Family));
        var learnedNow = new List<NewReward>();
        var byId = new Dictionary<string, NewReward>();

        foreach (var reward in settings.NewRewards)
            byId.TryAdd(IdOf(reward), reward);

        FateCardReward ResolveReward(FateCardReward shownReward)
        {
            if (shownReward.Reward != null)
                return shownReward;

            var text = AuguryCatalogueLookup.WithoutCopiesNote(shownReward.Text).Trim();
            var id = IdOf(text, shownReward.CopiesNeeded);

            if (!byId.TryGetValue(id, out var known))
            {
                if (!learn || !anyMatched || text.Length == 0)
                    return shownReward;

                known = new NewReward
                {
                    Text = text,
                    CopiesNeeded = shownReward.CopiesNeeded,
                    FirstSeenAreaLevel = areaLevel,
                    FirstSeen = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                };

                settings.NewRewards.Add(known);
                byId[id] = known;
                learnedNow.Add(known);
            }

            return shownReward with { Reward = RewardOf(known) };
        }

        List<FateCard> ResolveCards(IReadOnlyList<FateCard> cards) => cards
            .Select(c => c.Rewards.All(static r => r.Reward != null)
                ? c
                : c with { Rewards = c.Rewards.Select(ResolveReward).ToList() })
            .ToList();

        var resolved = state with
        {
            Offered = ResolveCards(state.Offered),
            Collection = ResolveCards(state.Collection),
        };
        var leftUnmatched = resolved.Offered.Concat(resolved.Collection)
            .Any(static c => c.Rewards.Any(static r => r.Reward == null));

        return new Resolution(resolved, learnedNow, leftUnmatched && !anyMatched);
    }

    /// <summary>
    /// Tidies the learned rewards on start: drops repeats of one family Id, and drops each whose text the catalogue
    /// now has (after a regeneration), carrying its weight, if set, to the catalogue family when the player has not
    /// set that family's weight. Run before RewardSettings.ForgetWeightsAtDefault, which keeps learned families'
    /// weights and drops those of families no longer anywhere.
    /// </summary>
    public static void Tidy(RewardSettings settings)
    {
        var kept = new HashSet<string>();

        foreach (var reward in settings.NewRewards.ToList())
        {
            var id = IdOf(reward);
            var catalogued = AuguryCatalogueLookup.RewardShown(reward.Text, reward.CopiesNeeded);

            if (catalogued == null && kept.Add(id))
                continue;

            settings.NewRewards.Remove(reward);

            if (catalogued != null && settings.Weights.Remove(id, out var weight))
                settings.Weights.TryAdd(catalogued.Family, weight);
        }
    }

    /// <summary>
    /// The catalogue-shaped reward a learned one is scored as: its own family, its copies as its tier (as the catalogue
    /// lookup takes them), the area level it was first seen at as its lowest.
    /// </summary>
    private static AuguryReward RewardOf(NewReward reward)
    {
        var id = IdOf(reward);

        return new AuguryReward(id, id, reward.CopiesNeeded, false, reward.Text, reward.FirstSeenAreaLevel, 100, "",
            [], "");
    }

    /// <summary>
    /// "New" and the 32-bit FNV-1a hash of "copies|text", the text without the copies note and with its whitespace
    /// collapsed, as the catalogue lookup compares texts. string.GetHashCode is not used: it differs between runs.
    /// </summary>
    private static string IdOf(string text, int copies)
    {
        var hash = 2166136261u;

        foreach (var b in Encoding.UTF8.GetBytes($"{copies}|{AuguryCatalogueLookup.RewardText(text)}"))
        {
            hash ^= b;
            hash *= 16777619u;
        }

        return $"{IdPrefix}{hash:X8}";
    }
}
