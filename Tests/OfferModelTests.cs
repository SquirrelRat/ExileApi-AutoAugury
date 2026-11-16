using System.Linq;
using Xunit;

namespace AutoAugury.Tests;

/// <summary>
/// The offer model: one offer shows FatesPerOffer Fates of the Cauldron's faction (default 3, as every 2026-11-14
/// offer did), drawn evenly. The count is a debug override, not an assumption.
/// </summary>
public class OfferModelTests
{
    private static AuguryFate SavageFate() => AuguryCatalogue.Fates.First(f => f.FactionId == "Savage");

    [Fact]
    public void PerOfferChance_ScalesWithOfferSize()
    {
        // Savage has 7 Fates: 3 of 7 per offer by default.
        Assert.Equal(3d / 7d, CopyPotential.PerOfferChance(SavageFate(), 3), precision: 12);
    }

    [Fact]
    public void PerOfferChance_ClampsAtOne()
    {
        Assert.Equal(1d, CopyPotential.PerOfferChance(SavageFate(), 99), precision: 12);
    }

    [Fact]
    public void PerOfferChance_IsZeroWithoutFate()
    {
        Assert.Equal(0d, CopyPotential.PerOfferChance(null, 3), precision: 12);
    }

    [Fact]
    public void FatesPerOffer_DefaultsToThree()
    {
        Assert.Equal(3, new AutoAugurySettings().Debug.FatesPerOffer.Value);
    }
}
