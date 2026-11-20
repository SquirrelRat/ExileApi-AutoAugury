using System.Linq;
using Xunit;

namespace AutoAugury.Tests;

/// <summary>
/// The panel reader's self-checks: an open Fates panel always shows card frames, so repeated empty reads mean the
/// client's layout moved, and a choosing panel always offers Fates, so a choosing read with no offers means the offer
/// row was not found. The catalogue carries its export stamp for the same reason.
/// </summary>
public class RobustnessTests
{
    [Fact]
    public void ReadsEmpty_FlagsOpenPanelWithNoCards()
    {
        var empty = new FatesPanelState(true, FatesPanelState.ChoosingTitle, [], [], [], default, default);

        Assert.True(FatesPanel.ReadsEmpty(empty));
    }

    [Fact]
    public void ReadsEmpty_PassesPanelsWithFramesAndClosedPanels()
    {
        var slots = new[] { default(SharpDX.RectangleF) };
        var withFrames = new FatesPanelState(true, FatesPanelState.ChoosingTitle, [], [], slots, default, default);

        Assert.False(FatesPanel.ReadsEmpty(withFrames));
        Assert.False(FatesPanel.ReadsEmpty(FatesPanelState.Closed));
    }

    [Fact]
    public void ReadsNoOffers_FlagsChoosingWithNoOfferRows()
    {
        var collectionOnly = new FatesPanelState(true, FatesPanelState.ChoosingTitle, [],
            [new FateCard("Tempest", null, null, default, [])], [default(SharpDX.RectangleF)], default, default);

        Assert.True(FatesPanel.ReadsNoOffers(collectionOnly));
        Assert.False(FatesPanel.ReadsEmpty(collectionOnly));

        var offered = new FatesPanelState(true, FatesPanelState.ChoosingTitle,
            [new FateCard("Tempest", null, null, default, [])], [], [default(SharpDX.RectangleF)], default, default);

        Assert.False(FatesPanel.ReadsNoOffers(offered));
        Assert.False(FatesPanel.ReadsNoOffers(FatesPanelState.Closed));
        Assert.False(FatesPanel.ReadsNoOffers(
            new FatesPanelState(true, FatesPanelState.InvokingTitle, [], [], [], default, default)));
    }

    [Fact]
    public void Supplement_AddsRowsTheCatalogueLacksAndNoIdTwice()
    {
        var ids = AuguryCatalogue.Rewards.Concat(AuguryCatalogueSupplement.Rewards).Select(static r => r.Id).ToList();

        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.DoesNotContain(AuguryCatalogue.Rewards, r => r.Id == "CurrencyOpulentDivineOrb1");

        var divine = AuguryCatalogueLookup.RewardShown("Empowered Monster will drop an additional Divine Orb", 2);
        var armour = AuguryCatalogueLookup.RewardShown(
            "Empowered Monster will drop additional Rare Body Armours\n" +
            "Body Armours dropped by Empowered Monster will have a Defence modifier", 1);

        Assert.Equal("CurrencyOpulentDivineOrb1", divine?.Id);
        Assert.Equal("BodyTransformDefence1", armour?.Id);
    }

    [Fact]
    public void Catalogue_CarriesExportStamp()
    {
        Assert.NotEqual("", AuguryCatalogue.ExportedAt);
        Assert.NotEqual("", AuguryCatalogue.Schema);
    }
}
