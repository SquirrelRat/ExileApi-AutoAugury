using System;
using System.Collections.Generic;

namespace AutoAugury;

/// <summary>
/// Short labels for a reward, for the overlay's legends: the modifier a rare drop will carry ("Belts: Elemental"), or
/// the drop's own name ("Threads"). The card legend and the boss summary legend both read from here, so the same
/// reward is named the same way in both. The full text stays in the tooltip; this is what fits beside an icon.
/// </summary>
internal static class RewardLabels
{
    private static readonly string[] LeadingPhrases =
    [
        "Empowered Monster has a chance to drop ",
        "Empowered Monster will drop ",
        "Empowered Monster has ",
    ];

    private static readonly string[] QuantityPhrases =
    [
        "many additional ",
        "an additional ",
        "additional ",
        "hoards of ",
    ];

    /// <summary>
    /// The label of a reward from the text the panel shows: "Belts: Elemental" for a rare item that will have a
    /// modifier, "Threads" for one that will not, and "" for nothing readable.
    /// </summary>
    public static string Of(string description)
    {
        var lines = (description ?? "").Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var first = Shorten(lines.Length > 0 ? lines[0] : "");
        var modifier = ModifierOf(lines);

        if (first.Length == 0)
            return "";

        return modifier.Length == 0 ? first : $"{first}: {modifier}";
    }

    /// <summary>The labels for a card's rewards, in icon order, joined for one line.</summary>
    public static string Join(IEnumerable<string> labels)
    {
        var kept = new List<string>();

        foreach (var label in labels)
        {
            if (!string.IsNullOrWhiteSpace(label) && !kept.Contains(label))
                kept.Add(label);
        }

        return string.Join(" / ", kept);
    }

    /// <summary>The drop's name: "additional Rare Belts" is "Belts", "many additional Threads" is "Threads".</summary>
    private static string Shorten(string line)
    {
        var text = line.Trim();

        foreach (var lead in LeadingPhrases)
        {
            if (text.StartsWith(lead, StringComparison.Ordinal))
            {
                text = text[lead.Length..];
                break;
            }
        }

        foreach (var quantity in QuantityPhrases)
        {
            if (text.StartsWith(quantity, StringComparison.Ordinal))
            {
                text = text[quantity.Length..];
                break;
            }
        }

        text = text.Trim();

        return text.StartsWith("Rare ", StringComparison.Ordinal) ? text["Rare ".Length..] : text;
    }

    /// <summary>
    /// The modifier the drop will have, from the text's second line: "…will have a Defence modifier" is "Defence",
    /// "…will have no Physical modifiers" is "no Physical". Empty when the text names none.
    /// </summary>
    private static string ModifierOf(string[] lines)
    {
        if (lines.Length < 2)
            return "";

        var line = lines[1];
        var have = line.IndexOf("will have ", StringComparison.Ordinal);

        if (have < 0)
            return "";

        var modifier = line[(have + "will have ".Length)..].Trim();

        foreach (var plural in new[] { " modifiers", " modifier" })
        {
            if (modifier.EndsWith(plural, StringComparison.Ordinal))
            {
                modifier = modifier[..^plural.Length];
                break;
            }
        }

        foreach (var article in new[] { "an ", "a " })
        {
            if (modifier.StartsWith(article, StringComparison.Ordinal))
            {
                modifier = modifier[article.Length..];
                break;
            }
        }

        return modifier.Trim();
    }
}
