# AutoAugury against the 3.30 patches

- Checked 2026-11-19 against the official patch notes for 3.30.0b (Nov 14) with hotfixes 1–4
  (Nov 15–17), 3.30.0c (Nov 18) and 3.30.0d (Nov 19), plus live reads of the client's data tables
  and the Covens panel through the ExileApi MCP (poe1 HUD, in game).
- Plugin first commit: 2026-11-14 (`a06bb9c`), catalogue export: 2026-11-14 03:20.

**Answer up front.** One patch broke the plugin's drawing: 3.30.0d's new "Fates available in an
area" counter, inserted into the panel body as child `[5]`, shifted every row down one. That is
fixed (rows are now found by their cards, not their index). Three more changes affect what the
plugin *believes* rather than whether it draws: 3.30.0b Hotfix 3 changed the rule for three copies
of one Fate, 3.30.0c added chase reward outcomes and swapped several armour reward rows, and
3.30.0d changed how many copies a Divine Orb or Orb of Annulment outcome needs. The first two leave
the reward catalogue stale; the third is read live and needs no change.

## Patch by patch

| Patch | Line that matters | Effect on the plugin |
|---|---|---|
| 3.30.0b Hotfix 2 (Nov 15) | "Tier 3 Crowkeeper Fate no longer grants the Empowered monster Culling Strike." | None expected: Culling Strike was a monster-mod stat, not part of the card's body text, so `TierOf` still matches. Balance only. |
| 3.30.0b Hotfix 2 | "Fixed a bug where some Fate-spawned Bosses could not drop Viscera." | None (drops, not the panel). |
| 3.30.0b Hotfix 3 (Nov 16) | "When Invoking Fates, you may now Invoke three copies of the same Fate. The first and third Fates will have their rewards tripled, while the second is ignored." | **Model change.** `FateValues` still says a third copy adds nothing and takes the invoked tier back down (measured 2026-11-14). Scoring, copy potential and the invocation guide now under-state a three-copy line. |
| 3.30.0c (Nov 18) | "Increased the size of the Covens UI, as well as scaling up further the size of reward icons." | None: cards are read from their live rectangles. Height of the offer row went from the 2026-11-14 dump; the reader does not assume it. |
| 3.30.0c | "Added several new chase reward outcomes for invoking Fates in high tier Maps." (additional Divine Orbs, a Mirror Shard, a Fracturing Orb, a Hinekora's Lock, Amulets with a Gem modifier) | **Catalogue stale.** Those icons match no `AuguryCatalogue` row, so `RewardShown` returns null and the card counts nothing for them (`UnknownRewards`). `CurrencyOpulent` weight is 65, the second highest. |
| 3.30.0c | "Replaced Covens modifiers for Shields and Body Armours … with will have a Defence modifier. Replaced … Helmets and Gloves … with will have a Life modifier." | **Catalogue stale.** The rows were replaced by new Ids (see below), so those icons are unknown too. |
| 3.30.0c | "Pressing V now also closes the Covens league UI." | None: the reader sees the panel closed and keeps its state until it opens again. |
| 3.30.0c | "The majority of Fate Empowerment effects have had their damage reduced." | None: weights are reward-based, not damage-based. |
| 3.30.0c | Shrine boss / Depraved Trinity / extra Bosses dropping Viscera fixes | None (drops). |
| 3.30.0d (Nov 19) | "Added a counter that shows how many Fates are available in an area." | **Broke the reader.** The counter landed at body `[5]`; the old code read `[5,0]` as the offer row and `[6,0]` as the collection, so the offers read as the collection and nothing scored. Fixed by shape-based row lookup; the counter is also an opportunity (see below). |
| 3.30.0d | "The Covens reward outcomes which guarantee a single Divine Orb or single Orb of Annulment now only require two copies to complete, rather than three." | None needed: `CopiesNeededOf` reads the panel's own "invoke two copies" note. Only the catalogue's tier-by-copies disambiguation can pick the other tier of the same text; the weight is per family, so the value is unchanged. |
| 3.30.0d | "Added line breaks to a number of Fate descriptions which were missing them." | None: `TierOf` collapses whitespace before comparing (`AuguryCatalogueLookup.Collapsed`). |

## Live evidence (2026-11-19, MCP)

- Panel while choosing, before the fix: `body[5]` = "0/3" (108x27), `body[6][0]` = 5 cards
  (3 visible: Tempest, Stormcaller, Avalanche), `body[7][0]` = 6 hidden cards, `[8]` = reward
  summary, `[10]` = confirm button ("confirm", 166x38). The plugin read `Offered=0`, the offers as
  `Collection`, and drew nothing.
- After the fix: `Visible=True, Title=Fates, Offered=3`, scores 18 / 51 / 70, confirm button found
  at 877,684 166x38. 48/48 tests pass.
- `Data/AuguryRewardOutcomes.dat`: 370 rows. New Ids seen live and absent from the 2026-11-14
  catalogue: `CurrencyOpulentDivineOrb1/2`, `CurrencyOpulentMirrorShard1`,
  `CurrencyOpulentFractureOrb1`, `CurrencyOpulentHinekorasLock1`, `CurrencyOpulentAnnulmentOrb1`,
  `CurrencyOpulentReflectingMist1`, `CurrencyOpulentSacred1`; the catalogue holds only
  `CurrencyOpulent1` of that family.
- Armour rows: the live table has `BodyTransformDefence1/2`, `ShieldTransformDefence1/2`,
  `HelmetTransformLife1/2`, `GlovesTransformLife1/2` and no `*TransformNoLife` row at all. The
  catalogue still carries `BodyTransformNoLife1/2`, `ShieldTransformNoLife1/2`,
  `HelmetTransformNoDefence1/2`, `GlovesTransformNoDefence1/2`, and lacks the `*TransformLife`
  helmet and glove rows. HardMode mirrors the same swap at tiers 1–3.
- Row layout of the reward table, for a reconstruction: `+0` Id, `+16` the panel's text, `+64` icon,
  `+8` tier, `+28` a 0–100 band (area-level weight, as exported). Drop mods and achievement are not
  in this table.

## Done (2026-11-19)

1. **Reader: rows by shape.** `FatesPanel` finds the offer/slot row and the collection row by the cards they hold,
   and the confirm button by its "confirm" label, so 3.30.0d's counter moving every index changed nothing. A
   "choosing but no offers read" canary logs after about five seconds if a layout change lands the cards elsewhere.
2. **Three-copy rule.** `FateValues` now counts the first copy at 3x and, with three or more copies, the third copy
   at 3x, the second nullified, and `PlacingOrder` puts the nullified copy in the second slot and the best remaining
   copy third. Measured on the live panel (dump `autoaugury_20261119_192157.txt`): three Murderous copies paid four
   rewards at 3x — the first and third cards' — and never the third card's own. Tests cover the value and the order.
3. **Reward catalogue supplement.** `AuguryCatalogueSupplement` adds the 29 rows the 2026-11-14 export lacks: the
   eight `CurrencyOpulent` chase outcomes 3.30.0c added, the Shield/Body "Defence modifier" and Helmet/Glove "Life
   modifier" swaps plus `AmuletTransformGems3`, regular and HardMode. `AuguryCatalogueLookup` matches against both
   sets and a test keeps the Ids unique. Only Id, text, tier, levels and icon were read; `DropMods` and
   `Achievement` are left empty because the reward table does not carry them and the plugin does not read them.
4. **Fates counter.** The "0/3" element 3.30.0d added is read as `FatesPanelState.FatesCounter` and
   `CauldronProgressOf` prefers it ("game") over the tracker's estimate.

## Open

- **Tier with three copies.** `TierInvokedAt` still says a third copy drops the invoked tier back, from a 2026-11-14
  dump that predates Hotfix 3. All three Murderous cards read tier unknown (their tier text does not differ), so it
  needs a Fate whose tiers read apart, three copies placed, and a dump.
- **The composed texts.** The armour and amulet rows' panel text is copied from the same slot's sibling rows (the
  patch notes quote the same wording); none has been offered yet. A dump with one on screen settles it - and if it
  differs, that row simply stays unknown until the catalogue is regenerated.
- **HardMode chase outcomes.** The eight new chase rows exist only in the regular table (no `HardModeCurrencyOpulent*`
  variants except the old `HardModeCurrencyOpulent1`); whether Ruthless rolls them at all is unknown.
- **The dead rows.** The export's `*TransformNoLife` and `*TransformNoDefence` rows no longer exist in the client.
  They are harmless (nothing reads their text any more) and go with the next regeneration.
- The new counter reads as Fates collected of the area's total (0/3 at the first Cauldron, 3/3 at the boss); that is
  how `CauldronProgressOf` uses it, and the panel shows `+1` as the Cauldron being chosen.

## Open questions

- Whether a third copy also changes the invoked tier under the new rule (the old measurement had it
  lower the tier; Hotfix 3 may have fixed that too). Settles with the measurement in step 1.
- Whether HardMode (Ruthless) offers the new chase outcomes; the plugin weights them by family
  either way, but the catalogue should say.
- The reward summary (body `[8]`, 3.30.0d) is icon-only, and **repetition is the multiplier**: a
  reward counted three times shows as three identical icons in the left group, and a reward short of
  its copies shows one claim there plus the icon in the right group with its "invoke two copies"
  note. Confirmed by screenshot with a Stormcaller pair placed: 3 identical icons for its 1-copy
  reward, 1 gold claim and the gold leftover on the right for its 2-copy reward. So the pair still
  triples, and the third copy can be judged by counting identical icons before and after placing it.
  The left group has 9 slots, so three tripled copies of one Fate (up to 9 claims) can reach the cap.
- What the new counter counts exactly — Fates left to collect, or Cauldrons left to open. Observed
  "0/3" at a choosing view with nothing collected, and "3/3" at the area's boss with three Fates
  collected and two invoked (2026-11-19); that reads as Fates collected of the area's total.
