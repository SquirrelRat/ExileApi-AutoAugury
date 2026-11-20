using System;
using System.Collections.Generic;

namespace AutoAugury;

/// <summary>
/// Short names for a reward, for the overlay's legends: the item and the modifier its drop will carry, abbreviated to
/// what fits beside an icon ("Belt:Elem", "Wep:Phys", "Sword:noPhys"), or just the drop's name ("Threads", "Mirror").
/// The full text stays in the tooltip. The card and boss legends both read from here, so a reward is named the same
/// way in both.
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

    /// <summary>The item a drop is, by the catalogue's own nouns, short enough for a legend.</summary>
    private static readonly Dictionary<string, string> ItemShort = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Amulets"] = "Amulet",
        ["Axes"] = "Axe",
        ["Belts"] = "Belt",
        ["Body Armours"] = "Body",
        ["Boots"] = "Boots",
        ["Bows"] = "Bow",
        ["Claws"] = "Claw",
        ["Corpse Rose"] = "CorpseRose",
        ["Currency Items"] = "Curr",
        ["Valuable Currency Items"] = "Curr",
        ["Daggers"] = "Dagger",
        ["Divine Orb"] = "Divine",
        ["Divine Orbs"] = "Divine",
        ["Exceptional Gem"] = "Gem",
        ["Fracturing Orb"] = "Fract",
        ["Gem with Quality"] = "Gem",
        ["Gems with Quality"] = "Gem",
        ["Gems with maximum Quality"] = "Gem",
        ["Gloves"] = "Glove",
        ["Gold"] = "Gold",
        ["Helmets"] = "Helm",
        ["Hide of the Vendigo"] = "Vendigo",
        ["Jewellery items"] = "Jewel",
        ["Maces"] = "Mace",
        ["Map"] = "Map",
        ["Maps"] = "Map",
        ["Melee Weapons"] = "Melee",
        ["Mirror Shard"] = "Mirror",
        ["Orb of Annulment"] = "Annul",
        ["Ordered Fate"] = "Fate",
        ["Quivers"] = "Quiver",
        ["Ranged Weapons or Quivers"] = "Ranged",
        ["Rings"] = "Ring",
        ["Rune Daggers"] = "RuneDag",
        ["Sceptres"] = "Sceptre",
        ["Staves"] = "Staff",
        ["Swords"] = "Sword",
        ["Threads"] = "Threads",
        ["Viscera Item"] = "Viscera",
        ["Wands"] = "Wand",
        ["Warstaves"] = "Warstaff",
        ["Weapons"] = "Wep",
        ["Armour"] = "Arm",
        ["Fishing Rod"] = "Rod",
    };

    /// <summary>The modifier a drop will have, by the phrases the panel uses.</summary>
    private static readonly Dictionary<string, string> ModifierShort = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Chaos"] = "Chaos",
        ["Critical"] = "Crit",
        ["Damage"] = "Dmg",
        ["Defence"] = "Def",
        ["Drop"] = "Drop",
        ["Gem"] = "Gem",
        ["Life"] = "Life",
        ["Mana"] = "Mana",
        ["Physical"] = "Phys",
        ["Resistance"] = "Res",
        ["Speed"] = "Spd",
        ["Angling"] = "Angling",
        ["Attribute"] = "Attr",
        ["Elemental"] = "Elem",
        ["Attack"] = "Atk",
        ["Intelligence"] = "Int",
        ["Strength"] = "Str",
        ["Dexterity"] = "Dex",
    };

    /// <summary>
    /// The label of a reward from the text the panel shows: "Belt:Elem" for a rare item that will have a modifier,
    /// "Threads" for one that will not, and "" for nothing readable.
    /// </summary>
    public static string Of(string description)
    {
        var lines = (description ?? "").Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var first = Shorten(lines.Length > 0 ? lines[0] : "");
        var (item, inline) = ItemOf(first);
        var modifier = inline.Length > 0 ? inline : ModifierOf(lines);

        if (item.Length == 0)
            return "";

        return modifier.Length == 0 ? item : $"{item}:{modifier}";
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

    /// <summary>
    /// The item of a drop and, when the item phrase carries it, the modifier ("Armour items with Intelligence
    /// requirements" is Arm and Int). The name is shortened to the legend's form, or kept as is when it is unknown.
    /// </summary>
    private static (string Item, string Modifier) ItemOf(string line)
    {
        const string armourWith = "Armour items with ";

        var text = line.StartsWith("Rare ", StringComparison.Ordinal) ? line["Rare ".Length..] : line;

        if (text.StartsWith(armourWith, StringComparison.OrdinalIgnoreCase) &&
            text.EndsWith(" requirements", StringComparison.OrdinalIgnoreCase))
        {
            var attribute = text[armourWith.Length..^" requirements".Length].Trim();

            return ("Arm", Short(ModifierShort, attribute));
        }

        if (text.StartsWith("Armour items", StringComparison.OrdinalIgnoreCase))
            return ("Arm", "");

        return (ItemShort.TryGetValue(text, out var shortName) ? shortName : text, "");
    }

    /// <summary>The modifier the drop will have, from the text's second line: "…will have a Defence modifier" is "Def",
    /// "…will have no Physical modifiers" is "noPhys". Empty when the text names none.</summary>
    private static string ModifierOf(string[] lines)
    {
        if (lines.Length < 2)
            return "";

        var line = lines[1];
        var have = line.IndexOf("will have ", StringComparison.Ordinal);

        if (have < 0)
            return "";

        var modifier = line[(have + "will have ".Length)..].Trim();
        var negated = false;

        foreach (var plural in new[] { " modifiers", " modifier" })
        {
            if (modifier.EndsWith(plural, StringComparison.Ordinal))
            {
                modifier = modifier[..^plural.Length];
                break;
            }
        }

        if (modifier.StartsWith("no ", StringComparison.OrdinalIgnoreCase))
        {
            negated = true;
            modifier = modifier[3..];
        }

        foreach (var article in new[] { "an ", "a " })
        {
            if (modifier.StartsWith(article, StringComparison.OrdinalIgnoreCase))
            {
                modifier = modifier[article.Length..];
                break;
            }
        }

        var shortName = Short(ModifierShort, modifier.Trim());

        return negated ? $"no{shortName}" : shortName;
    }

    private static string Short(Dictionary<string, string> map, string text) =>
        map.TryGetValue(text, out var shortName) ? shortName : text;

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
            if (text.StartsWith(quantity, StringComparison.OrdinalIgnoreCase))
            {
                text = text[quantity.Length..];
                break;
            }
        }

        return text.Trim();
    }
}
