using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace AutoAugury.Tests;

/// <summary>
/// Characterization of the invocation value model after 3.30.0b Hotfix 3: reward counts pool across Fates, empowering
/// triples the first copy, a third copy pays a tripled card of its own, and placing order is by marginal gain.
/// </summary>
public class ScoringTests
{
    private static AutoAugurySettings Settings() => new();

    private static FateCard Card(string fateId, int? tier, string rewardId)
    {
        var fate = AuguryCatalogue.Fates.First(f => f.Id == fateId);
        var reward = AuguryCatalogue.Rewards.First(r => r.Id == rewardId);
        var shown = new[] { new FateCardReward("reward", reward, 1) };

        return new FateCard(fateId, fate, tier, default, shown, false);
    }

    [Fact]
    public void RewardsValue_TriplesEmpoweredCopy()
    {
        var settings = Settings();
        var expected = settings.Rewards.WeightOf("CurrencyLow");
        var card = Card("Crowkeeper", 1, "CurrencyLow1");

        Assert.True(expected > 0);
        Assert.Equal(expected, FateValues.RewardsValue(card, 1, settings));
        Assert.Equal(3 * expected, FateValues.RewardsValue(card, FateValues.EmpoweredMultiple, settings));
    }

    [Fact]
    public void InvocationValue_TriplesTheThirdCopyToo()
    {
        var settings = Settings();
        var first = Card("Crowkeeper", 1, "CurrencyLow1");
        var second = Card("Crowkeeper", 1, "CurrencyLow1");
        var third = Card("Crowkeeper", 1, "CurrencyHigh1");

        var pair = FateValues.InvocationValue([new FateGroup([first, second])], settings);
        var triple = FateValues.InvocationValue([new FateGroup([first, second, third])], settings);

        // 3.30.0b Hotfix 3: the third copy pays three times over, the second nothing (measured 2026-11-19).
        Assert.Equal(3 * settings.Rewards.WeightOf("CurrencyHigh"), triple - pair);
    }

    [Fact]
    public void Best_PoolsRewardCountsAcrossFates()
    {
        var settings = Settings();
        var cards = new List<FateCard>
        {
            Card("Crowkeeper", 1, "CurrencyLow1"),
            Card("Dryad", 1, "CurrencyLow1"),
        };
        var expected = 2 * settings.Rewards.WeightOf("CurrencyLow");

        var advice = InvocationAdvisor.Best(cards, 3, settings);

        Assert.Equal(expected, advice.Total);
        Assert.Equal(2, advice.Picks.Count);
    }
}
