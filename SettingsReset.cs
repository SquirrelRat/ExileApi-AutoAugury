using System;
using System.Reflection;

namespace AutoAugury;

/// <summary>
/// Puts every setting back as shipped, except Enable. For the Reset all settings button at the top of the menu.
///
/// By reflection, after AutoExpedition's ResetAll and CraftUntilHighlighted's SettingsReset: a fresh settings object is
/// made and each node's value copied from it, section by section, so "default" means whatever the property initialisers
/// say now, and a setting added later is covered without anybody coming back here.
///
/// A node is anything with a readable and writable Value (ToggleNode, RangeNode, TextNode, HotkeyNodeV2), or a writable
/// Content (ContentNode). A dictionary (the weight tables) is emptied. A section is a property whose type is this
/// plugin's own; its nodes are copied the same way. Anything else - a CustomNode button or heading, a request flag, an
/// int - is left alone.
/// </summary>
internal static class SettingsReset
{
    /// <summary>
    /// The root properties a reset leaves alone. Enable would switch the plugin off. ConfigVersion is not a setting: it
    /// records which settings changes a saved file has had, and putting it back would run them all again on the next
    /// load.
    /// </summary>
    private static readonly string[] Untouched =
        [nameof(AutoAugurySettings.Enable), nameof(AutoAugurySettings.ConfigVersion)];

    public static void Apply(AutoAugurySettings settings)
    {
        if (settings != null)
            Copy(new AutoAugurySettings(), settings, true);
    }

    private static void Copy(object from, object to, bool root)
    {
        foreach (var property in to.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetIndexParameters().Length > 0 || root && Array.IndexOf(Untouched, property.Name) >= 0)
                continue;

            var source = Safe.Read(() => property.GetValue(from), null);
            var target = Safe.Read(() => property.GetValue(to), null);

            if (source == null || target == null || source.GetType() != target.GetType())
                continue;

            var type = target.GetType();

            if (type.GetProperty("Value") is { CanRead: true, CanWrite: true } value)
            {
                Safe.Try(() => value.SetValue(target, value.GetValue(source)));
                continue;
            }

            // The fresh object's own list, which nothing else holds: it is made here and dropped after.
            if (type.GetProperty("Content") is { CanRead: true, CanWrite: true } content)
            {
                Safe.Try(() => content.SetValue(target, content.GetValue(source)));
                continue;
            }

            // The weight tables (RewardSettings.Weights, FateSettings.Weights): the fresh object's are empty, which
            // means every weight at its default.
            if (target is System.Collections.IDictionary dictionary)
            {
                Safe.Try(dictionary.Clear);
                continue;
            }

            if (type.Namespace == typeof(SettingsReset).Namespace)
                Copy(source, target, false);
        }
    }
}
