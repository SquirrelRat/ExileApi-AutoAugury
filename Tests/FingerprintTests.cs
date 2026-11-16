using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace AutoAugury.Tests;

/// <summary>
/// The panel and settings fingerprints: the scores are recomputed only when the cards, the weights or the
/// tracker's generation changed, not on every 100 ms panel read.
/// </summary>
public class FingerprintTests
{
    private static FateCard Card(string name, int? tier = null, bool placed = false, params string[] rewards)
    {
        var fate = AuguryCatalogue.Fates.FirstOrDefault(f => f.Id == name);
        var shown = rewards.Select(r => new FateCardReward(r, null, 1)).ToList();

        return new FateCard(name, fate, tier, default, shown, placed);
    }

    private static FatesPanelState Choosing(params FateCard[] offered)
    {
        return new FatesPanelState(true, FatesPanelState.ChoosingTitle, offered, [], [], default, default);
    }

    [Fact]
    public void FingerprintOf_IsStableForEqualStates()
    {
        var a = Choosing(Card("Crowkeeper", 1), Card("Dryad"));
        var b = Choosing(Card("Dryad"), Card("Crowkeeper", 1));

        Assert.Equal(FateScoring.FingerprintOf(a), FateScoring.FingerprintOf(b));
    }

    [Fact]
    public void FingerprintOf_MarksTierRewardAndPlacedChanges()
    {
        var baseline = FateScoring.FingerprintOf(Choosing(Card("Crowkeeper", 1)));

        Assert.NotEqual(baseline, FateScoring.FingerprintOf(Choosing(Card("Crowkeeper", 2))));
        Assert.NotEqual(baseline, FateScoring.FingerprintOf(Choosing(Card("Crowkeeper", 1, false, "extra"))));
        Assert.NotEqual(baseline, FateScoring.FingerprintOf(Choosing(Card("Crowkeeper", 1, true))));
        Assert.NotEqual(baseline, FateScoring.FingerprintOf(Choosing(Card("Dryad", 1))));
    }

    [Fact]
    public void SettingsFingerprint_MarksWeightChanges()
    {
        var settings = new AutoAugurySettings();
        var baseline = FateScoring.SettingsFingerprint(settings);

        Assert.Equal(baseline, FateScoring.SettingsFingerprint(settings));

        settings.Rewards.Weights["CurrencyHigh"] = 60;

        Assert.NotEqual(baseline, FateScoring.SettingsFingerprint(settings));
    }
}
