using System.Collections.Generic;
using Xunit;

namespace AutoAugury.Tests;

/// <summary>
/// Settings helpers: Ruthless-only family detection, one-click weight presets, and the status line text.
/// </summary>
public class SettingsTests
{
    [Theory]
    [InlineData("SkillGem", true)]
    [InlineData("SupportGem", true)]
    [InlineData("JewelleryLow", true)]
    [InlineData("CurrencyHigh", false)]
    [InlineData("CurrencyOpulent", false)]
    [InlineData(null, false)]
    [InlineData("NoSuchFamily", false)]
    public void IsHardModeOnly_MarksRuthlessOnlyFamilies(string familyId, bool expected)
    {
        Assert.Equal(expected, RewardFamilies.IsHardModeOnly(familyId));
    }

    [Fact]
    public void ApplyPreset_SetsListedFamilies()
    {
        var weights = new Dictionary<string, int>();

        WeightPresets.ApplyTo(weights, "currency", _ => 2);

        Assert.Equal(80, weights["CurrencyOpulent"]);
        Assert.Equal(60, weights["CurrencyHigh"]);
        Assert.DoesNotContain("UniqueJewel", weights);
    }

    [Fact]
    public void ApplyPreset_DropsWeightsEqualToDefault()
    {
        var weights = new Dictionary<string, int> { ["CurrencyHigh"] = 1 };

        WeightPresets.ApplyTo(weights, "viscera", family => family == "CurrencyHigh" ? 45 : 2);

        Assert.DoesNotContain("CurrencyHigh", weights);
        Assert.Equal(30, weights["ForceViscera"]);
    }

    [Fact]
    public void ApplyPreset_IgnoresUnknownId()
    {
        var weights = new Dictionary<string, int>();

        WeightPresets.ApplyTo(weights, "no-such-preset", _ => 2);

        Assert.Empty(weights);
    }

    [Fact]
    public void StatusLineOf_NamesCounts()
    {
        Assert.Equal("Augury: 3 tracked | 3 slots | 3/offer",
            FateOverlay.StatusLineOf(3, 3, 3));
    }
}
