using System.Linq;
using Xunit;

namespace AutoAugury.Tests;

/// <summary>
/// The overlay marks every remaining invocation pick in placing order, uses a colourblind-safe palette on request,
/// and badges cards whose tier the panel cannot show.
/// </summary>
public class OverlayTests
{
    private static FateCard NamedCard(AuguryFate fate) => new("Card", fate, null, default, []);

    private static InvocationGuide PairGuide(out InvocationPick first, out InvocationPick empower)
    {
        var firstCard = new FateCard("A", null, null, default, []);
        var empowerCard = new FateCard("A", null, null, default, []);

        first = new InvocationPick(firstCard, 1, null, 15);
        empower = new InvocationPick(empowerCard, 2, firstCard, 0);
        var picks = new[] { first, empower };

        return new InvocationGuide(new InvocationAdvice(picks, 30, 3), empower, [15, 15], 0, false, false,
            picks, false);
    }

    [Fact]
    public void PickLabel_NumbersPicksAndMarksPairs()
    {
        var guide = PairGuide(out var first, out var empower);

        Assert.Equal("1. +15", FateOverlay.PickLabel(guide, first));
        Assert.Equal("2. +15 pair", FateOverlay.PickLabel(guide, empower));
    }

    [Fact]
    public void PaletteFor_OffersColourblindSafeSet()
    {
        var standard = FateOverlay.PaletteFor(false);
        var safe = FateOverlay.PaletteFor(true);

        Assert.Equal(Colours.FromArgb(255, 90, 255, 120), standard.Best);
        Assert.Equal(Colours.FromArgb(255, 0, 158, 115), safe.Best);
        Assert.NotEqual(standard.Best, safe.Best);
    }

    [Fact]
    public void ScoreLabelOf_BadgesUnknownTier()
    {
        var crowkeeper = AuguryCatalogue.Fates.First(f => f.Id == "Crowkeeper");
        var score = new FateScore(NamedCard(crowkeeper), 24, 5, 19d, true, null, 3, 0);

        Assert.Equal("24 (5 + 19) tier ?", FateOverlay.ScoreLabelOf(score, true));
        Assert.Equal("24 tier ?", FateOverlay.ScoreLabelOf(score, false));
    }

    [Fact]
    public void ScoreLabelOf_SkipsBadgeForTierlessFates()
    {
        var dryad = AuguryCatalogue.Fates.First(f => f.Id == "Dryad");

        Assert.False(AuguryCatalogueLookup.HasDistinctTiers(dryad));

        var score = new FateScore(NamedCard(dryad), 3, 3, 0d, true, null, 3, 0);

        Assert.Equal("3 (3 + 0)", FateOverlay.ScoreLabelOf(score, true));
    }

    [Fact]
    public void ScoreLabelOf_SkipsBadgeWhenTierKnown()
    {
        var crowkeeper = AuguryCatalogue.Fates.First(f => f.Id == "Crowkeeper");
        var card = new FateCard("Card", crowkeeper, 2, default, []);
        var score = new FateScore(card, 36, 58, -22d, true, null, 3, 0);

        Assert.Equal("36 (58 - 22)", FateOverlay.ScoreLabelOf(score, true));
    }
}
