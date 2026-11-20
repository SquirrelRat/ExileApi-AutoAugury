using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Xunit;
using RectangleF = SharpDX.RectangleF;

namespace AutoAugury.Tests;

/// <summary>
/// The panel reader's self-checks: an open Fates panel always shows card frames, so repeated empty reads mean the
/// client's layout moved, and a choosing panel always offers Fates, so a choosing read with no offers means the offer
/// row was not found. The world marker's selection, and the catalogue's export stamp, for the same reason.
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
        var slots = new[] { default(RectangleF) };
        var withFrames = new FatesPanelState(true, FatesPanelState.ChoosingTitle, [], [], slots, default, default);

        Assert.False(FatesPanel.ReadsEmpty(withFrames));
        Assert.False(FatesPanel.ReadsEmpty(FatesPanelState.Closed));
    }

    [Fact]
    public void ReadsNoOffers_FlagsChoosingWithNoOfferRows()
    {
        var collectionOnly = new FatesPanelState(true, FatesPanelState.ChoosingTitle, [],
            [new FateCard("Tempest", null, null, default, [])], [default(RectangleF)], default, default);

        Assert.True(FatesPanel.ReadsNoOffers(collectionOnly));
        Assert.False(FatesPanel.ReadsEmpty(collectionOnly));

        var offered = new FatesPanelState(true, FatesPanelState.ChoosingTitle,
            [new FateCard("Tempest", null, null, default, [])], [], [default(RectangleF)], default, default);

        Assert.False(FatesPanel.ReadsNoOffers(offered));
        Assert.False(FatesPanel.ReadsNoOffers(FatesPanelState.Closed));
        Assert.False(FatesPanel.ReadsNoOffers(
            new FatesPanelState(true, FatesPanelState.InvokingTitle, [], [], [], default, default)));
    }

    [Fact]
    public void WorldMarker_DrawsOnlyOwedCauldronsWithAPosition()
    {
        TrackedCauldron At(uint id, CauldronStage stage, Vector2? grid) =>
            new(id, "Savage", stage, 10f, grid, true, true, []);

        var owed = CauldronWorldMarker.OwedOf(
        [
            At(1, CauldronStage.Owed, new Vector2(10, 20)),
            At(2, CauldronStage.NotOwed, new Vector2(30, 40)),
            At(3, CauldronStage.Done, new Vector2(50, 60)),
            At(4, CauldronStage.Owed, null),
        ]);

        Assert.Single(owed);
        Assert.Equal(1u, owed[0].Id);
    }

    [Fact]
    public void Catalogue_CarriesExportStamp()
    {
        Assert.NotEqual("", AuguryCatalogue.ExportedAt);
        Assert.NotEqual("", AuguryCatalogue.Schema);
    }
}
