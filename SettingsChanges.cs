namespace AutoAugury;

/// <summary>
/// The changes a saved settings file gets once, when a new build changes what it must hold: each step raises
/// AutoAugurySettings.ConfigVersion, so it runs once per file. A saved file holds a value for every setting it saved,
/// so a changed default never reaches it on its own. The same pattern as AutoExpedition's ConfigVersion.
/// </summary>
internal static class SettingsChanges
{
    /// <summary>The ConfigVersion a saved file reaches once every step has run.</summary>
    public const int CurrentConfigVersion = 1;

    /// <summary>The message step 1 leaves for the player, in the user's words (2026-11-19).</summary>
    public const string UpdatedFor3300c =
        "AutoAugury: Updated for 3.30.0c. (Lock, Mirror Shards, Divines, etc). Weights overwritten.";

    /// <summary>
    /// Runs the steps the file has not had and returns the message for the player, or null when there is none.
    ///
    /// Step 1 (3.30.0c, 2026-11-19): the default reward weights became the user's, with the patch's new rewards, and
    /// Must take marks arrived. Every saved reward weight and Must take mark is dropped, so the new defaults reach
    /// every install, and the player is told (UpdatedFor3300c). A file that was never used (the plugin off, no weight
    /// or mark saved) gets no message, as nothing in it was overwritten. Learned rewards stay; their weights, kept with
    /// the others, go, so their warnings come back until they are weighted again.
    /// </summary>
    public static string Apply(AutoAugurySettings settings)
    {
        string message = null;

        if (settings.ConfigVersion < 1)
        {
            var used = settings.Enable || settings.Rewards.Weights.Count > 0 || settings.Rewards.MustTake.Count > 0;

            settings.Rewards.Weights.Clear();
            settings.Rewards.MustTake.Clear();

            if (used)
                message = UpdatedFor3300c;
        }

        if (settings.ConfigVersion < CurrentConfigVersion)
            settings.ConfigVersion = CurrentConfigVersion;

        return message;
    }
}
