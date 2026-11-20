using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace AutoAugury;

/// <summary>
/// The boss panel's reward summary as text: one entry per reward, "3x &lt;reward&gt;", which the panel builds in the
/// hidden element of the summary strip. It says both the multiplier and which reward it belongs to, so a legend can
/// name what the strip's icons only repeat; its markup is the game's, and anything without a multiplier (a copies
/// note) is dropped rather than guessed at.
/// </summary>
internal static class FateSummaryText
{
    /// <summary>One entry's body inside its braces; a reward with two lines keeps both inside them.</summary>
    private static readonly Regex Entry = new(@"\{([^}]*)\}", RegexOptions.CultureInvariant);

    private static readonly Regex Multiplied = new(@"^(\d+)x\s+([\s\S]+)$", RegexOptions.CultureInvariant);

    private static readonly Regex Tag = new("<[^>]*>", RegexOptions.CultureInvariant);

    /// <summary>The entries in the order the panel lists them: how many times each reward counts, and its text.</summary>
    public static List<(int Multiplier, string Text)> Parse(string summary)
    {
        var entries = new List<(int Multiplier, string Text)>();

        if (string.IsNullOrWhiteSpace(summary))
            return entries;

        foreach (Match match in Entry.Matches(summary))
        {
            // The dump writes a line break as " | "; the live element uses a newline. Both become one text again.
            var body = Clean(match.Groups[1].Value).Replace(" | ", "\n").Trim();
            var multiplied = Multiplied.Match(body);

            if (!multiplied.Success || !int.TryParse(multiplied.Groups[1].Value, out var multiplier))
                continue;

            entries.Add((multiplier, multiplied.Groups[2].Value.Trim()));
        }

        return entries;
    }

    private static string Clean(string part) => Tag.Replace(part ?? "", "");
}
