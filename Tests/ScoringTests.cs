using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace AutoAugury.Tests;

/// <summary>
/// Characterization of the invocation value model: reward counts pool across Fates, empowering triples the first
/// copy, and placing order is by marginal gain.
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
        var fate = AuguryCatalogue.Fates.First(f => f.Id == "Crowkeeper");
        var low = AuguryCatalogue.Rewards.First(r => r.Id == "CurrencyLow1");
        var high = AuguryCatalogue.Rewards.First(r => r.Id == "CurrencyHigh1");

        FateCard With(AuguryReward reward) =>
            new("Crowkeeper", fate, 1, default, [new FateCardReward("shown", reward, 1)], false);

        var first = With(low);
        var second = With(low);
        var third = With(high);

        var pair = FateValues.InvocationValue([new FateGroup([first, second], first)], settings);
        var triple = FateValues.InvocationValue([new FateGroup([first, second, third], first)], settings);

        // 3.30.0b Hotfix 3: the third copy pays three times over, the second nothing (measured 2026-11-19).
        Assert.Equal(3 * settings.Rewards.WeightOf("CurrencyHigh"), triple - pair);
    }

    [Fact]
    public void PlacingOrder_PutsTheNullifiedCopyInTheMiddle()
    {
        var settings = Settings();
        var fate = AuguryCatalogue.Fates.First(f => f.Id == "Crowkeeper");
        var low = AuguryCatalogue.Rewards.First(r => r.Id == "CurrencyLow1");
        var high = AuguryCatalogue.Rewards.First(r => r.Id == "CurrencyHigh1");

        FateCard With(AuguryReward reward) =>
            new("Crowkeeper", fate, 1, default, [new FateCardReward("shown", reward, 1)], false);

        var first = With(low);
        var second = With(low);
        var third = With(high);

        var order = FateValues.PlacingOrder(new FateGroup([first, second, third], first), settings);

        Assert.Same(first, order[0]);
        Assert.Same(second, order[1]);
        Assert.Same(third, order[2]);
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

    [Fact]
    public void GainsOf_SplitsValueInPlacingOrder()
    {
        var settings = Settings();
        var one = settings.Rewards.WeightOf("CurrencyLow");
        var cards = new List<FateCard>
        {
            Card("Crowkeeper", 1, "CurrencyLow1"),
            Card("Dryad", 1, "CurrencyLow1"),
        };

        Assert.Equal(new[] { one, one }, InvocationAdvisor.GainsOf(cards, settings));
    }
}
