namespace AutoAugury;

/// <summary>
/// Reward outcomes the client carried on 2026-11-19 that the catalogue, exported 2026-11-14 03:20:11, does not have:
/// the chase outcomes 3.30.0c added for invoking Fates in high tier Maps (Divine Orbs, Mirror Shard, Fracturing Orb,
/// Hinekora's Lock, Reflecting Mist, Sacred Orb, Orb of Annulment) and the armour and amulet rows the same patch
/// swapped in (Shield and Body Armour "will have a Defence modifier", Helmet and Glove "will have a Life modifier",
/// Amulet "will have a Gem modifier"). Read from the live tables through ExileApi on 2026-11-19: Id, the panel's text,
/// tier, area levels and icon. The armour and amulet texts are composed by the game from the monster mod and copied
/// from the catalogue's sibling rows for the same slot, as the patch notes quote them; nothing has offered one yet,
/// so they are unverified until one shows.
///
/// Family follows the Id without the HardMode prefix or the band digit, as the export does, except the chase outcomes:
/// they keep it (CurrencyOpulent) only where one claim is worth about one valuable currency drop (a Divine Orb, an
/// Orb of Annulment), and get a family of their own where it is not (RewardFamilies.DefaultWeightOf prices them
/// against the Covens prices of 2026-11-19), so the weight table can rank them apart. DropMods and Achievement are not
/// in the table this was read from and are left empty: the plugin matches rewards by Id and text only.
///
/// Regenerating the catalogue takes these in: delete this file and point AuguryRewards at the catalogue alone.
/// </summary>
internal static class AuguryCatalogueSupplement
{
    /// <summary>When the rows below were read from the running client.</summary>
    public const string ReadAt = "2026-11-19";

    public static readonly AuguryReward[] Rewards =
    [
        new("CurrencyOpulentMirrorShard1", "ChaseMirrorShard", 3, false,
            "Empowered Monster will drop an additional Mirror Shard",
            81, 100, "Art/2DArt/UIImages/InGame/Augury/AuguryRewardIconMirrorShard", [], null),
        new("CurrencyOpulentFractureOrb1", "ChaseFracturingOrb", 3, false,
            "Empowered Monster will drop an additional Fracturing Orb",
            68, 100, "Art/2DArt/UIImages/InGame/Augury/AuguryRewardIconFracturingOrb", [], null),
        new("CurrencyOpulentDivineOrb1", "CurrencyOpulent", 2, false,
            "Empowered Monster will drop an additional Divine Orb",
            68, 100, "Art/2DArt/UIImages/InGame/Augury/AuguryRewardIconDivineOrb", [], null),
        new("CurrencyOpulentDivineOrb2", "CurrencyOpulent", 3, false,
            "Empowered Monster will drop additional Divine Orbs",
            68, 100, "Art/2DArt/UIImages/InGame/Augury/AuguryRewardIconDivineOrb", [], null),
        new("CurrencyOpulentAnnulmentOrb1", "ChaseAnnulmentOrb", 2, false,
            "Empowered Monster will drop an additional Orb of Annulment",
            68, 100, "Art/2DArt/UIImages/InGame/Augury/AuguryRewardIconAnnulmentOrb", [], null),
        new("CurrencyOpulentReflectingMist1", "ChaseReflectingMist", 3, false,
            "Empowered Monster will drop an additional Reflecting Mist",
            81, 100, "Art/2DArt/UIImages/InGame/Augury/AuguryRewardIconReflectiveMist", [], null),
        new("CurrencyOpulentHinekorasLock1", "ChaseHinekorasLock", 3, false,
            "Empowered Monster will drop an additional Hinekora's Lock",
            81, 100, "Art/2DArt/UIImages/InGame/Augury/AuguryRewardIconHinekorasLock", [], null),
        new("CurrencyOpulentSacred1", "ChaseSacredOrb", 3, false,
            "Empowered Monster will drop an additional Sacred Orb",
            81, 100, "Art/2DArt/UIImages/InGame/Augury/AuguryRewardIconSacredOrb", [], null),

        new("BodyTransformDefence1", "BodyTransformDefence", 1, false,
            "Empowered Monster will drop additional Rare Body Armours\nBody Armours dropped by Empowered Monster "
            + "will have a Defence modifier",
            1, 45, "Art/2DArt/UIImages/InGame/Augury/AuguryRewardIconBodyArmour", [],
            "AuguryLeagueChallenge5_10"),
        new("BodyTransformDefence2", "BodyTransformDefence", 1, false,
            "Empowered Monster will drop additional Rare Body Armours\nBody Armours dropped by Empowered Monster "
            + "will have a Defence modifier",
            46, 100, "Art/2DArt/UIImages/InGame/Augury/AuguryRewardIconBodyArmour", [],
            "AuguryLeagueChallenge5_10"),
        new("ShieldTransformDefence1", "ShieldTransformDefence", 1, false,
            "Empowered Monster will drop additional Rare Shields\nShields dropped by Empowered Monster will have "
            + "a Defence modifier",
            1, 45, "Art/2DArt/UIImages/InGame/Augury/AuguryRewardIconShield", [], "AuguryLeagueChallenge5_10"),
        new("ShieldTransformDefence2", "ShieldTransformDefence", 1, false,
            "Empowered Monster will drop additional Rare Shields\nShields dropped by Empowered Monster will have "
            + "a Defence modifier",
            46, 100, "Art/2DArt/UIImages/InGame/Augury/AuguryRewardIconShield", [], "AuguryLeagueChallenge5_10"),
        new("HelmetTransformLife1", "HelmetTransformLife", 1, false,
            "Empowered Monster will drop additional Rare Helmets\nHelmets dropped by Empowered Monster will have "
            + "a Life modifier",
            1, 45, "Art/2DArt/UIImages/InGame/Augury/AuguryRewardIconHelmet", [], "AuguryLeagueChallenge5_10"),
        new("HelmetTransformLife2", "HelmetTransformLife", 1, false,
            "Empowered Monster will drop additional Rare Helmets\nHelmets dropped by Empowered Monster will have "
            + "a Life modifier",
            46, 100, "Art/2DArt/UIImages/InGame/Augury/AuguryRewardIconHelmet", [], "AuguryLeagueChallenge5_10"),
        new("GlovesTransformLife1", "GlovesTransformLife", 1, false,
            "Empowered Monster will drop additional Rare Gloves\nGloves dropped by Empowered Monster will have "
            + "a Life modifier",
            1, 45, "Art/2DArt/UIImages/InGame/Augury/AuguryRewardIconGloves", [], "AuguryLeagueChallenge5_10"),
        new("GlovesTransformLife2", "GlovesTransformLife", 1, false,
            "Empowered Monster will drop additional Rare Gloves\nGloves dropped by Empowered Monster will have "
            + "a Life modifier",
            46, 100, "Art/2DArt/UIImages/InGame/Augury/AuguryRewardIconGloves", [], "AuguryLeagueChallenge5_10"),
        new("AmuletTransformGems3", "AmuletTransformGems", 3, false,
            "Empowered Monster will drop additional Rare Amulets\nAmulets dropped by Empowered Monster will have "
            + "a Gem modifier",
            68, 100, "Art/2DArt/UIImages/InGame/Augury/AuguryRewardIconAmulet", [], "AuguryLeagueChallenge5_10"),

        new("HardModeBodyTransformDefence1", "BodyTransformDefence", 2, true,
            "Empowered Monster has a chance to drop additional Rare Body Armours\nBody Armours dropped by "
            + "Empowered Monster will have a Defence modifier",
            1, 45, "Art/2DArt/UIImages/InGame/Augury/AuguryRewardIconBodyArmour", [], null),
        new("HardModeBodyTransformDefence2", "BodyTransformDefence", 2, true,
            "Empowered Monster has a chance to drop additional Rare Body Armours\nBody Armours dropped by "
            + "Empowered Monster will have a Defence modifier",
            46, 67, "Art/2DArt/UIImages/InGame/Augury/AuguryRewardIconBodyArmour", [], null),
        new("HardModeBodyTransformDefence3", "BodyTransformDefence", 2, true,
            "Empowered Monster has a chance to drop additional Rare Body Armours\nBody Armours dropped by "
            + "Empowered Monster will have a Defence modifier",
            68, 100, "Art/2DArt/UIImages/InGame/Augury/AuguryRewardIconBodyArmour", [], null),
        new("HardModeShieldTransformDefence1", "ShieldTransformDefence", 2, true,
            "Empowered Monster has a chance to drop additional Rare Shields\nShields dropped by Empowered Monster "
            + "will have a Defence modifier",
            1, 45, "Art/2DArt/UIImages/InGame/Augury/AuguryRewardIconShield", [], null),
        new("HardModeShieldTransformDefence2", "ShieldTransformDefence", 2, true,
            "Empowered Monster has a chance to drop additional Rare Shields\nShields dropped by Empowered Monster "
            + "will have a Defence modifier",
            46, 67, "Art/2DArt/UIImages/InGame/Augury/AuguryRewardIconShield", [], null),
        new("HardModeShieldTransformDefence3", "ShieldTransformDefence", 2, true,
            "Empowered Monster has a chance to drop additional Rare Shields\nShields dropped by Empowered Monster "
            + "will have a Defence modifier",
            68, 100, "Art/2DArt/UIImages/InGame/Augury/AuguryRewardIconShield", [], null),
        new("HardModeHelmetTransformLife1", "HelmetTransformLife", 2, true,
            "Empowered Monster has a chance to drop additional Rare Helmets\nHelmets dropped by Empowered Monster "
            + "will have a Life modifier",
            1, 45, "Art/2DArt/UIImages/InGame/Augury/AuguryRewardIconHelmet", [], null),
        new("HardModeHelmetTransformLife2", "HelmetTransformLife", 2, true,
            "Empowered Monster has a chance to drop additional Rare Helmets\nHelmets dropped by Empowered Monster "
            + "will have a Life modifier",
            46, 67, "Art/2DArt/UIImages/InGame/Augury/AuguryRewardIconHelmet", [], null),
        new("HardModeHelmetTransformLife3", "HelmetTransformLife", 2, true,
            "Empowered Monster has a chance to drop additional Rare Helmets\nHelmets dropped by Empowered Monster "
            + "will have a Life modifier",
            68, 100, "Art/2DArt/UIImages/InGame/Augury/AuguryRewardIconHelmet", [], null),
        new("HardModeGlovesTransformLife1", "GlovesTransformLife", 2, true,
            "Empowered Monster has a chance to drop additional Rare Gloves\nGloves dropped by Empowered Monster "
            + "will have a Life modifier",
            1, 45, "Art/2DArt/UIImages/InGame/Augury/AuguryRewardIconGloves", [], null),
        new("HardModeGlovesTransformLife2", "GlovesTransformLife", 2, true,
            "Empowered Monster has a chance to drop additional Rare Gloves\nGloves dropped by Empowered Monster "
            + "will have a Life modifier",
            46, 67, "Art/2DArt/UIImages/InGame/Augury/AuguryRewardIconGloves", [], null),
        new("HardModeGlovesTransformLife3", "GlovesTransformLife", 2, true,
            "Empowered Monster has a chance to drop additional Rare Gloves\nGloves dropped by Empowered Monster "
            + "will have a Life modifier",
            68, 100, "Art/2DArt/UIImages/InGame/Augury/AuguryRewardIconGloves", [], null),
    ];
}
