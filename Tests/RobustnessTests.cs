using Xunit;

namespace AutoAugury.Tests;

/// <summary>
/// The panel reader's self-check: an open Fates panel always shows card frames, so repeated empty reads mean the
/// client's layout moved and the indices no longer land. The catalogue carries its export stamp for the same reason.
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
    public void Catalogue_CarriesExportStamp()
    {
        Assert.NotEqual("", AuguryCatalogue.ExportedAt);
        Assert.NotEqual("", AuguryCatalogue.Schema);
    }
}
