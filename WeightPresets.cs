using System;
using System.Collections.Generic;

namespace AutoAugury;

/// <summary>
/// One-click reward weight sets, drawn above the reward table. A preset raises its families and leaves the rest
/// alone; a family whose preset weight equals its default is removed rather than kept, like the table does.
/// </summary>
internal static class WeightPresets
{
    public static IReadOnlyList<(string Id, string Label)> All =>
    [
        ("currency", "Currency rush"),
        ("uniques", "Unique hunter"),
        ("viscera", "Boss progression"),
    ];

    public static IReadOnlyDictionary<string, int> For(string presetId) => presetId switch
    {
        "currency" => new Dictionary<string, int>
        {
            ["CurrencyOpulent"] = 80,
            ["CurrencyHigh"] = 60,
            ["StackedDecksHigh"] = 60,
            ["ThreadsHigh"] = 30,
            ["GoldHigh"] = 20,
        },
        "uniques" => new Dictionary<string, int>
        {
            ["UniqueJewelleryHigh"] = 70,
            ["UniqueJewel"] = 65,
            ["UniqueJewelleryLow"] = 55,
            ["UniqueArmourHigh"] = 30,
            ["UniqueWeaponHigh"] = 30,
        },
        "viscera" => new Dictionary<string, int>
        {
            ["CurrencyHigh"] = 45,
            ["RarityHigh"] = 40,
            ["ForceViscera"] = 30,
        },
        _ => new Dictionary<string, int>(),
    };

    public static void ApplyTo(Dictionary<string, int> weights, string presetId, Func<string, int> defaultOf)
    {
        foreach (var (family, weight) in For(presetId))
        {
            if (weight == defaultOf(family))
                weights.Remove(family);
            else
                weights[family] = weight;
        }
    }
}
