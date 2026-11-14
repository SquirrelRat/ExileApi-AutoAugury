using System;
using System.Collections.Generic;
using ExileCore.PoEMemory;

namespace AutoAugury;

/// <summary>
/// One read of remote memory, or the fallback.
///
/// Every memory object in this HUD can throw at arbitrary times - zoning, a panel closing, a hot reload - and a throw
/// inside Render takes the whole overlay down, not just this plugin. So nothing reads the game without going through
/// here. CraftUntilHighlighted's Safe, which is AutoExpedition's less its read counters.
/// </summary>
internal static class Safe
{
    public static T Read<T>(Func<T> read, T fallback)
    {
        try
        {
            return read();
        }
        catch
        {
            return fallback;
        }
    }

    /// <summary>
    /// The same read, with the thing being read passed in rather than captured. Written as
    /// <c>Safe.Read(entity, static e =&gt; e.Path, null)</c> the lambda captures nothing and the call allocates
    /// nothing, which matters for reads made every frame.
    /// </summary>
    public static TValue Read<TState, TValue>(TState state, Func<TState, TValue> read, TValue fallback)
    {
        try
        {
            return read(state);
        }
        catch
        {
            return fallback;
        }
    }

    /// <summary>For a call whose only purpose is its side effect, such as a reset writing one setting.</summary>
    public static void Try(Action act)
    {
        try
        {
            act();
        }
        catch
        {
            // A setting that cannot be written keeps the value it had.
        }
    }

    /// <summary>
    /// An element's children through Element.Children, or null when there are none or they cannot be read.
    ///
    /// AutoExpedition reads the raw child pointer array instead, because it found Element.Children returning fewer
    /// children than the array holds on the passive tree's container. That is not done here: this plugin reads only
    /// what ExileCore exposes. The dump writes each element's ChildCount, so an element whose ChildCount is above the
    /// number of children listed under it shows where this falls short.
    /// </summary>
    public static IList<Element> ChildrenOf(Element element)
    {
        var children = Read(element, static e => e?.Children, null);

        return children is { Count: > 0 } ? children : null;
    }
}
