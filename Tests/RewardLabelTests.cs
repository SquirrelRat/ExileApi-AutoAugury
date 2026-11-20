using System.Linq;
using Xunit;

namespace AutoAugury.Tests;

/// <summary>
/// The reward legends: what each reward is called beside its icon, and the boss summary's own "3x &lt;reward&gt;"
/// entries, both read from the texts the panel shows (2026-11-19).
/// </summary>
public class RewardLabelTests
{
    [Theory]
    [InlineData("Empowered Monster will drop additional Threads", "Threads")]
    [InlineData("Empowered Monster will drop additional Gold", "Gold")]
    [InlineData("Empowered Monster will drop many additional Threads", "Threads")]
    [InlineData("Empowered Monster will drop an additional Mirror Shard", "Mirror")]
    [InlineData("Empowered Monster will drop additional Rare Belts\n" +
                "Belts dropped by Empowered Monster will have an Elemental modifier", "Belt:Elem")]
    [InlineData("Empowered Monster will drop additional Rare Helmets\n" +
                "Helmets dropped by Empowered Monster will have a Defence modifier", "Helm:Def")]
    [InlineData("Empowered Monster will drop additional Rare Amulets\n" +
                "Amulets dropped by Empowered Monster will have a Gem modifier", "Amulet:Gem")]
    [InlineData("Empowered Monster will drop additional Rare Swords\n" +
                "Swords dropped by Empowered Monster will have no Physical modifiers", "Sword:noPhys")]
    [InlineData("Empowered Monster will drop additional Rare Melee Weapons\n" +
                "Melee Weapons dropped by Empowered Monster will have a Critical modifier", "Melee:Crit")]
    [InlineData("Empowered Monster has a chance to drop additional Rare Armour items with Intelligence requirements",
        "Arm:Int")]
    [InlineData("Empowered Monster will drop additional Rare Body Armours\n" +
                "Body Armours dropped by Empowered Monster will have a Chaos modifier", "Body:Chaos")]
    [InlineData("Empowered Monster will drop additional Valuable Currency Items", "Curr")]
    public void Of_NamesTheDropAndItsModifier(string description, string expected)
    {
        Assert.Equal(expected, RewardLabels.Of(description));
    }

    [Fact]
    public void Join_KeepsIconOrderAndDropsRepeats()
    {
        Assert.Equal("Threads / Belt:Elem",
            RewardLabels.Join(["Threads", "Belt:Elem", "Threads", ""]));
    }

    [Fact]
    public void Summary_ParsesTheMultipliersAndKeepsTwoLineRewards()
    {
        const string summary =
            "| <valuedefault>{3x Empowered Monster will drop additional Threads} | " +
            "<valuedefault>{3x Empowered Monster will drop additional Gold} | " +
            "<valuedefault>{3x Empowered Monster will drop additional Rare Helmets | Helmets dropped by Empowered " +
            "Monster will have a Defence modifier} | " +
            "<valuedefault>{3x Empowered Monster will drop additional Rare Belts | Belts dropped by Empowered " +
            "Monster will have an Elemental modifier} |";

        var entries = FateSummaryText.Parse(summary);

        Assert.Equal(4, entries.Count);
        Assert.All(entries, e => Assert.Equal(3, e.Multiplier));
        Assert.Equal("Threads", RewardLabels.Of(entries[0].Text));
        Assert.Equal("Helm:Def", RewardLabels.Of(entries[2].Text));
        Assert.Equal("Belt:Elem", RewardLabels.Of(entries[3].Text));
    }

    [Fact]
    public void Summary_DropsNotesWithoutAMultiplier()
    {
        var entries = FateSummaryText.Parse(
            "<valuedefault>{1x Empowered Monster will drop hoards of Gold} | " +
            "<rgb(122,122,122)>{To claim this reward, invoke two copies of it}");

        Assert.Single(entries);
        Assert.Equal("Empowered Monster will drop hoards of Gold", entries[0].Text);
        Assert.Equal("Gold", RewardLabels.Of(entries[0].Text));
    }
}
