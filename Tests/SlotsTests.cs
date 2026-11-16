using System.Collections.Generic;
using RectangleF = SharpDX.RectangleF;
using Xunit;

namespace AutoAugury.Tests;

/// <summary>
/// The invocation slot count the advice is computed for: the boss panel's own
/// slots when it shows them, else the slots last seen at a boss, else the default.
/// </summary>
public class SlotsTests
{
    private static FatesPanelState StateWithSlots(int count)
    {
        var slots = new List<RectangleF>();

        for (var i = 0; i < count; i++)
            slots.Add(new RectangleF(i * 10f, 0f, 10f, 10f));

        return new FatesPanelState(true, FatesPanelState.InvokingTitle, [], [], slots, default, default);
    }

    private static readonly FatesPanelState NoSlots =
        new(true, FatesPanelState.ChoosingTitle, [], [], [], default, default);

    [Fact]
    public void SlotsFor_ReturnsPanelCount_WhenPanelShowsSlots()
    {
        Assert.Equal(4, InvocationAdvisor.SlotsFor(StateWithSlots(4), 0));
    }

    [Fact]
    public void SlotsFor_ReturnsObserved_WhenPanelShowsNoSlots()
    {
        Assert.Equal(4, InvocationAdvisor.SlotsFor(NoSlots, 4));
    }

    [Fact]
    public void SlotsFor_ReturnsDefault_WhenNothingKnown()
    {
        Assert.Equal(InvocationAdvisor.DefaultSlots, InvocationAdvisor.SlotsFor(NoSlots, 0));
    }
}
