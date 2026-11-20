using System.Linq;
using Xunit;

namespace AutoAugury.Tests;

/// <summary>
/// Cauldron-count learning: finished areas raise the assumed floor, and the
/// progress line says whether its total was tracked, observed or assumed.
/// </summary>
public class CauldronCountsTests
{
    [Theory]
    [InlineData(70, 0, 3)]
    [InlineData(70, 2, 3)]
    [InlineData(70, 5, 5)]
    [InlineData(80, 4, 5)]
    public void Assumed_IsObservedFloor(int areaLevel, int observed, int expected)
    {
        Assert.Equal(expected, MapCauldrons.Assumed(areaLevel, observed));
    }

    [Theory]
    [InlineData(2, 3, 70, 0, "tracked")]
    [InlineData(2, 0, 70, 5, "observed")]
    [InlineData(2, 0, 70, 0, "assumed")]
    public void ProgressSource_NamesWinner(int current, int known, int areaLevel, int observed, string expected)
    {
        Assert.Equal(expected, FateScoring.ProgressSource(current, known, areaLevel, observed));
    }

    [Fact]
    public void ObservedTotal_KeepsMostPerLevel()
    {
        var tracker = new CauldronTracker();

        tracker.RecordAreaTotal(70, 3);
        tracker.RecordAreaTotal(70, 5);
        tracker.RecordAreaTotal(70, 2);
        tracker.RecordAreaTotal(0, 9);
        tracker.RecordAreaTotal(71, 0);

        Assert.Equal(5, tracker.ObservedTotal(70));
        Assert.Equal(0, tracker.ObservedTotal(71));
    }

    [Fact]
    public void ObservedSummary_ListsLevels()
    {
        var tracker = new CauldronTracker();

        Assert.Equal("", tracker.ObservedSummary());

        tracker.RecordAreaTotal(75, 4);
        tracker.RecordAreaTotal(70, 3);

        Assert.Equal("70: 3; 75: 4", tracker.ObservedSummary());
    }

    private static FatesPanelState ChoosingState()
    {
        var savage = AuguryCatalogue.Fates.First(f => f.FactionId == "Savage");
        var offered = new[] { new FateCard("Offer", savage, null, default, [], false) };
        var collected = new[] { new FateCard("Held", savage, null, default, [], false) };

        return new FatesPanelState(true, FatesPanelState.ChoosingTitle, offered, collected, [], default, default);
    }

    [Fact]
    public void OffersToComeFor_UsesObservedFloor()
    {
        var tracker = new CauldronTracker();
        tracker.RecordAreaTotal(70, 5);
        var offersToCome = FateScoring.OffersToComeFor(ChoosingState(), 70, tracker);

        Assert.Equal(3, offersToCome("Savage"));
        Assert.Equal(0, offersToCome("Filth"));
    }

    [Fact]
    public void OffersToComeFor_FallsBackToAssumed()
    {
        var offersToCome = FateScoring.OffersToComeFor(ChoosingState(), 70, new CauldronTracker());

        Assert.Equal(1, offersToCome("Savage"));
    }

    [Fact]
    public void CauldronProgressOf_UsesTheGamesOwnCount()
    {
        var state = ChoosingState() with { FatesCounter = (1, 4) };
        var progress = FateScoring.CauldronProgressOf(state, 70, new CauldronTracker());

        Assert.Equal((2, 4, "game"), progress);
    }

    [Fact]
    public void CauldronProgressOf_ReportsObservedSource()
    {
        var tracker = new CauldronTracker();
        tracker.RecordAreaTotal(70, 5);
        var progress = FateScoring.CauldronProgressOf(ChoosingState(), 70, tracker);

        Assert.Equal((2, 5, "observed"), progress);
    }
}
