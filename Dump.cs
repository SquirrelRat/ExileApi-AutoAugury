using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using ExileCore;
using ExileCore.PoEMemory;
using ExileCore.PoEMemory.Components;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore.Shared.Enums;
using SharpDX;
using RectangleF = SharpDX.RectangleF;

namespace AutoAugury;

/// <summary>
/// Writes what the plugin can read about the Augury league mechanic to a text file.
///
/// ExileCore 3.30 has no Augury class of its own: the only Augury names in it are enum values (GameStat, QuestFlag,
/// MapIconsIndex). So the dump reads everything generic that could hold a card choice, and the question it is for -
/// whether the choices still to come are readable before the game shows them - is answered by comparing dumps taken
/// before, during and after a choice:
///
/// 1. The server's mechanic handlers by ruleset name: whether there is an Augury one. ExileCore exposes only a
///    handler's address and id, so this says where card state would be, not what it is.
/// 2. The game files whose names match the entity pattern: which Augury tables exist.
/// 3. The player's server inventories: whether the Deck of Fates, or the cards on offer, are items in an inventory.
/// 4. The entities whose metadata path matches the entity pattern (the Cauldrons and whatever else the mechanic
///    places), and every entity carrying an Augury game stat: components, state machine states, stats, buffs, mods.
/// 5. The element tree under the interface root, with hidden elements inside open panels, and the texts of the tooltips
///    the walked elements hold: whether the choice panel holds more than the choice it shows.
/// 6. What changed since the previous dump, in the entities and in the element tree.
///
/// Every read goes through Safe, and the dump catches its own throw, so a section that cannot be read says so and the
/// rest is still written. Only ExileCore's own properties are read.
/// </summary>
internal static class Dump
{
    /// <summary>
    /// Set by the settings button, answered by the tick that answers the dump key. A CustomNode is handed neither the
    /// plugin nor the GameController, so the button and the key share one path.
    /// </summary>
    public static bool AskedForInSettings;

    /// <summary>The last file written, for the row beside the button. Empty until one is taken.</summary>
    public static string LastWritten = "";

    /// <summary>
    /// Most lines the element tree may write for one top-level panel, so one large panel cannot crowd out the rest.
    /// </summary>
    private const int MostTreeLinesPerPanel = 4000;

    /// <summary>Most lines the whole element tree may write.</summary>
    private const int MostTreeLines = 30000;

    /// <summary>Most lines the changes section may write.</summary>
    private const int MostChangeLines = 800;

    /// <summary>Most distinct tooltips whose texts are written.</summary>
    private const int MostTooltips = 100;

    /// <summary>Most text lines written for one tooltip or the hovered tooltip.</summary>
    private const int MostTooltipTexts = 60;

    /// <summary>Most items listed for one server inventory.</summary>
    private const int MostItemsPerInventory = 60;

    private const RegexOptions PatternOptions = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    /// <summary>
    /// The GameStat values whose names contain Augury, read off ExileCore's enum: the stats an entity carries when the
    /// mechanic has touched it (MonsterIsAuguryAffectedBoss, the card and daemon tier stats, the monster reward stats).
    /// </summary>
    private static readonly HashSet<GameStat> AuguryStats = Enum.GetValues<GameStat>()
        .Where(static s => s.ToString().Contains("Augury", StringComparison.OrdinalIgnoreCase))
        .ToHashSet();

    /// <summary>
    /// Each element's description from the previous dump's tree, by its path of child indices from the interface root.
    /// Held in memory only, so the first dump after a reload has nothing to compare against.
    /// </summary>
    private static Dictionary<string, string> _previousTree;

    /// <summary>Each written entity's state from the previous dump, by entity id, without its position.</summary>
    private static Dictionary<uint, string> _previousEntities;

    /// <summary>When the previous dump was taken, for the changes section's heading.</summary>
    private static DateTime _previousAt;

    public static void Write(BaseSettingsPlugin<AutoAugurySettings> plugin)
    {
        var gc = plugin.GameController;
        var settings = plugin.Settings;
        var b = new StringBuilder();
        var timer = Stopwatch.StartNew();

        try
        {
            Describe(b, gc, settings, (plugin as AutoAugury)?.Cauldrons);
        }
        catch (Exception ex)
        {
            b.AppendLine();
            b.AppendLine($"!! the dump itself threw: {ex}");
        }

        b.AppendLine();
        b.AppendLine($"== Dump took {timer.ElapsedMilliseconds} ms");

        try
        {
            var directory = DumpFolder.Path;
            Directory.CreateDirectory(directory);

            var path = Path.Combine(directory, $"autoaugury_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
            File.WriteAllText(path, b.ToString());

            LastWritten = Path.GetFileName(path);
            DebugWindow.LogMsg($"[AutoAugury] Wrote {path}", 10f);
            CursorMessage.Show(CursorMessage.Kind.Success, "Dump written");

            Prune(directory, settings.Debug.MaxDumps.Value);
        }
        catch (Exception ex)
        {
            DebugWindow.LogError($"[AutoAugury] Could not write the dump: {ex.Message}", 10f);
            CursorMessage.Show(CursorMessage.Kind.Error, "Dump failed");
        }
    }

    /// <summary>Keeps the newest few dumps and deletes the rest. Failures are ignored: the dump is written.</summary>
    private static void Prune(string directory, int keep)
    {
        try
        {
            var files = new DirectoryInfo(directory).GetFiles("autoaugury_*.txt")
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .ToList();

            for (var i = Math.Max(1, keep); i < files.Count; i++)
                files[i].Delete();
        }
        catch
        {
            // Nothing depends on it.
        }
    }

    private static void Describe(StringBuilder b, GameController gc, AutoAugurySettings settings,
        CauldronTracker cauldrons)
    {
        b.AppendLine($"AutoAugury (PoE1) dump - {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        b.AppendLine($"plugin enabled: {settings.Enable.Value}");

        var pattern = EntityPatternOf(settings, out var patternNote);

        b.AppendLine($"entity pattern: {pattern}{patternNote}");

        DescribeFatesPanel(b, gc, settings, cauldrons);
        DescribeArea(b, gc);
        DescribeMechanicHandlers(b, gc);
        DescribeGameFiles(b, gc, pattern);
        DescribeHover(b, gc);
        DescribeInventories(b, gc, pattern);

        var debug = settings.Debug;
        var entities = DescribeEntities(b, gc, pattern);
        var tree = DescribeInterface(b, gc, pattern, debug.DumpElementDepth.Value, debug.DumpHiddenElements.Value);

        DescribeChanges(b, entities, tree);

        _previousEntities = entities;
        _previousTree = tree;
        _previousAt = DateTime.Now;
    }

    /// <summary>
    /// The entity pattern as a regular expression, or the default with a note when the setting is unusable.
    /// </summary>
    private static Regex EntityPatternOf(AutoAugurySettings settings, out string note)
    {
        var text = Safe.Read(settings, static s => s.Debug.DumpEntityPattern.Value, "");
        note = "";

        if (string.IsNullOrWhiteSpace(text))
        {
            note = " (the setting is empty: using the default)";
            text = DebugSettings.DefaultDumpEntityPattern;
        }

        try
        {
            return new Regex(text, PatternOptions, TimeSpan.FromMilliseconds(50));
        }
        catch (ArgumentException ex)
        {
            note = $" (the setting \"{text}\" is not a valid expression, {ex.Message}: using the default)";

            return new Regex(DebugSettings.DefaultDumpEntityPattern, PatternOptions);
        }
    }

    /// <summary>
    /// The Fates panel as FatesPanel reads it and FateScoring scores it, first in the file because it is what the
    /// plugin acts on: each card's name, catalogue Fate, tier and rewards with their families, copies and weights.
    /// </summary>
    private static void DescribeFatesPanel(StringBuilder b, GameController gc, AutoAugurySettings settings,
        CauldronTracker cauldrons)
    {
        b.AppendLine();
        b.AppendLine("== Fates panel, as the reader sees it");

        var areaLevel = Safe.Read(gc, static g => g.Area.CurrentArea.RealLevel, 0);
        var state = NewRewardRegistry.Resolve(new FatesPanel().Read(gc), settings.Rewards, areaLevel, false).State;

        b.AppendLine($"  visible {state.Visible}, title \"{state.Title}\", choosing a Fate {state.ChoosingFate}, " +
                     $"invoking Fates {state.InvokingFates}, {state.Offered.Count} named in the top row of " +
                     $"{state.TopRowSlots.Count} visible, {state.Collection.Count} collected, hover lift " +
                     (state.HoverLift > 0f ? $"{state.HoverLift:0} px" : "not read (no card hovered)") +
                     ", Fate counter " + (state.Counter is { } counter
                         ? $"{counter.Collected}/{counter.InArea}"
                         : "not read"));

        if (state.SummaryLines.Count > 0)
            b.AppendLine("  reward summary: " + string.Join(" | ", state.SummaryLines.Select(static l =>
                $"{l.Multiplier}x {l.Text.Replace("\r", "").Replace("\n", " ").Trim()}")));

        DescribeLearnedRewards(b, settings.Rewards);
        DescribeCauldrons(b, cauldrons);
        DescribeServerMinimapIcons(b, gc);

        var offersToCome = FateScoring.OffersToComeFor(state, areaLevel, cauldrons);
        var mapFaction = state.Offered.FirstOrDefault(static c => c.Fate != null)?.Fate.FactionId;

        if (state.ChoosingFate)
        {
            var known = cauldrons?.OffersToCome(mapFaction, true) ?? 0;

            var inArea = MapCauldrons.InArea(state.Counter, areaLevel);
            var whence = state.Counter != null ? "by the Fate counter" : "assumed for the area level";

            b.AppendLine($"  offers to come for {mapFaction ?? "?"}: {offersToCome(mapFaction):0.##} (area level " +
                         $"{areaLevel}, {inArea:0.##} Cauldrons {whence}, " +
                         $"{state.Counter?.Collected ?? state.Collection.Count} collected, this one; the tracker " +
                         $"knows {known} not done besides this one), {settings.FatesPerCauldron.Value:0.#} Fates " +
                         "per Cauldron as set");
        }

        if (state.Visible)
        {
            foreach (var chance in CopyPotential.ChancesOf(state.Collection, InvocationAdvisor.DefaultSlots, settings,
                         offersToCome))
            {
                var outcomes = string.Join(", ", chance.Outcomes.Select(o =>
                    $"{o.More} more adds {o.Value} at {o.Chance:P0}"));

                b.AppendLine($"  copy potential: {chance.Card.Name} (held {chance.Held}, " +
                             $"{chance.OffersToCome:0.##} offer(s) to come): {outcomes}; " +
                             $"expected {chance.Expected:0.#}");
            }
        }

        var scores = state.ChoosingFate ? FateScoring.Score(state, settings, offersToCome) : [];
        var topRow = state.InvokingFates ? "placed" : "offered";

        foreach (var (label, cards) in new[] { (topRow, state.Offered), ("collected", state.Collection) })
        {
            foreach (var card in cards)
            {
                var score = scores.FirstOrDefault(s => ReferenceEquals(s.Card, card));

                b.AppendLine($"  {label}: {card.Name} - Fate {card.Fate?.Id ?? "not in the catalogue"} " +
                             $"({card.Fate?.FactionId ?? "?"}), tier {card.Tier?.ToString() ?? "unknown"}, " +
                             $"rect {Rect(card.Rect)}" + (card.Placed ? ", greyed (in a slot)" : "") +
                             (score == null
                                 ? ""
                                 : $"; score {FateOverlay.ScoreLabelOf(score, true)}, unrounded {score.Value:0.##}" +
                                   $", must take rank {score.MustTakeRank:0.##}" +
                                   (FateScoring.BestOf(scores).Contains(score) ? ", BEST" : "")));

                foreach (var reward in card.Rewards)
                {
                    var family = reward.Reward?.Family;
                    var learned = (!NewRewardRegistry.IsLearnedFamily(family)
                                      ? ""
                                      : NewRewardRegistry.LacksWeight(reward, settings.Rewards)
                                          ? ", learned, no weight set"
                                          : ", learned") +
                                  (settings.Rewards.IsMustTake(family) ? ", must take" : "");

                    b.AppendLine($"      reward {reward.Reward?.Id ?? "UNKNOWN"} (family {family ?? "-"}{learned}, " +
                                 $"{reward.CopiesNeeded} copies, weight {settings.Rewards.WeightOf(family)}, " +
                                 $"icon {Rect(reward.Rect)}): {OneLine(reward.Text)}");
                }
            }
        }

        if (!state.InvokingFates)
            return;

        var guide = InvocationAdvisor.GuideFor(state, settings);
        var advice = guide.Advice;

        var why = guide.WrongOrder ? "wrong order" : "not the best Fates";

        var uncertain = InvocationAdvisor.CandidatesOf(state, settings).PlacedCopyUncertain;

        b.AppendLine($"  invocation advice for {advice.Slots} slots, value {advice.Total}" +
                     $"{(guide.AnyOrder ? ", any order" : "")}" +
                     $"{(guide.AnyOrderLeft ? ", the rest in any order" : "")}" +
                     (guide.Cost > 0 ? $", placed cards cost {guide.Cost} ({why})" : "") +
                     (uncertain ? ", which collected copy a slot holds is a guess" : "") +
                     (advice.Picks.Count == 0 ? ": invoke nothing" : ":"));

        foreach (var pick in advice.Picks)
        {
            b.AppendLine($"    {pick.Order}. {pick.Card.Name}, adds {guide.GainOf(pick)}" +
                         (pick.Empowers == null ? "" : $", empowers {pick.Empowers.Name}") +
                         (pick.ThirdCopyOf == null ? "" : $", third copy of {pick.ThirdCopyOf.Name}, tripled") +
                         (guide.IsPlaced(pick) ? ", placed" : "") +
                         (ReferenceEquals(pick, guide.Next) ? ", NEXT" : ""));
        }
    }

    /// <summary>The Cauldrons CauldronTracker knows in this area, and the offers it counts as still to come.</summary>
    private static void DescribeCauldrons(StringBuilder b, CauldronTracker cauldrons)
    {
        if (cauldrons == null)
        {
            b.AppendLine("  Cauldrons: no tracker");
            return;
        }

        var known = cauldrons.Cauldrons;

        b.AppendLine($"  Cauldrons known this area: {known.Count}" +
                     (known.Count == 0 ? "" : ": " + string.Join(", ", known.Select(c =>
                         $"id {c.Id} {c.FactionId ?? "faction ?"} {c.Stage}" +
                         (c.Distance < float.MaxValue ? $" at {c.Distance:0}" : " out of range") +
                         (c.GridPos is { } grid ? $" grid <{grid.X:0}, {grid.Y:0}>" : "") +
                         $" targetable {c.Targetable?.ToString() ?? "?"}" +
                         (c.IconListed ? " icon listed" : "")))));
        foreach (var cauldron in known.Where(static c => c.History.Count > 0))
            b.AppendLine($"    id {cauldron.Id} history: {string.Join("; ", cauldron.History)}");

        var notTracked = cauldrons.IconsNotTracked;

        b.AppendLine($"  Cauldron icons of the server list not tracked since entering: {notTracked.Count}" +
                     (notTracked.Count == 0 ? "" : ": " + string.Join("; ", notTracked)));
        b.AppendLine($"  larger map icons, last frame: {CauldronMapIcons.LastFrameOf()}");
    }

    /// <summary>
    /// The server's minimap icon list (ServerData.MinimapIcons): how many, by icon, and each Cauldron's in full, to
    /// learn how far it reaches and when a Cauldron's icon leaves it.
    /// </summary>
    private static void DescribeServerMinimapIcons(StringBuilder b, GameController gc)
    {
        var icons = Safe.Read(gc, static g => g.IngameState.ServerData.MinimapIcons, null);

        if (icons == null)
        {
            b.AppendLine("  server minimap icons: unreadable");
            return;
        }

        var player = Safe.Read(gc, static g => (System.Numerics.Vector2?)g.Player.GridPosNum, null);
        var byIcon = icons
            .GroupBy(static i => Safe.Read(i, static x => x.MinimapIcon?.Id, null) ?? "?")
            .OrderByDescending(static g => g.Count())
            .Select(static g => $"{g.Key} {g.Count()}");

        b.AppendLine($"  server minimap icons: {icons.Count}: {string.Join(", ", byIcon)}");

        foreach (var icon in icons.Where(CauldronTracker.IsCauldronIcon))
        {
            var grid = Safe.Read(icon, static i => i.GridPosition, default);
            var distance = player is { } p
                ? $", {System.Numerics.Vector2.Distance(p, new System.Numerics.Vector2(grid.X, grid.Y)):0} away"
                : "";

            b.AppendLine($"    Cauldron icon: entity {Safe.Read(icon, static i => i.EntityId, 0u)}, " +
                         $"grid <{grid.X}, {grid.Y}>{distance}");
        }
    }

    private static bool Matches(Regex pattern, string text)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        try
        {
            return pattern.IsMatch(text);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    private static void DescribeArea(StringBuilder b, GameController gc)
    {
        b.AppendLine();
        b.AppendLine("== Area");

        var area = Safe.Read(gc, static g => g.Area.CurrentArea, null);
        var displayName = Safe.Read(area, static a => a.DisplayName, "?");
        var name = Safe.Read(area, static a => a.Name, "?");

        b.AppendLine($"  {displayName} (\"{name}\"), hash {Safe.Read(area, static a => a.Hash, 0u)}, " +
                     $"level {Safe.Read(area, static a => a.RealLevel, -1)}, " +
                     $"town {Safe.Read(area, static a => a.IsTown, false)}, " +
                     $"hideout {Safe.Read(area, static a => a.IsHideout, false)}");

        // The area's own stats: MapAuguryLeague, MapHasAuguryPinnacleCard and the like, when the area has them.
        StatLines(b, "map stats with Augury in the name",
            Safe.Read(gc, static g => g.IngameState.Data.MapStats, null));
        StatLines(b, "visible map stats with Augury in the name",
            Safe.Read(gc, static g => g.IngameState.Data.MapStatsVisible, null));
    }

    private static void StatLines(StringBuilder b, string name, Dictionary<GameStat, int> stats)
    {
        if (stats == null)
        {
            b.AppendLine($"  {name}: unreadable");
            return;
        }

        var kept = stats.Where(static s => AuguryStats.Contains(s.Key)).ToList();

        b.AppendLine($"  {name}: {kept.Count} of {stats.Count}");

        foreach (var (stat, value) in kept)
            b.AppendLine($"    {stat} = {value}");
    }

    /// <summary>
    /// ServerData's mechanic handlers, by the ruleset name the client gives each. ExileCore reads two of them itself
    /// (DeepwaterHandler, UnstableBreachHandler); for the rest it exposes only the address and the id at +8.
    /// </summary>
    private static void DescribeMechanicHandlers(StringBuilder b, GameController gc)
    {
        b.AppendLine();
        b.AppendLine("== Server mechanic handlers (ServerData.MechanicHandlersByRulesetName)");

        var handlers = Safe.Read(gc, static g => g.IngameState.ServerData.MechanicHandlersByRulesetName, null);

        if (handlers == null)
        {
            b.AppendLine("  unreadable");
            return;
        }

        var listed = Safe.Read(gc, static g => g.IngameState.ServerData.MechanicHandlers.Count, -1);

        b.AppendLine($"  {handlers.Count} handler(s) by name; {listed} in MechanicHandlers");

        foreach (var (name, handler) in handlers.OrderBy(static h => h.Key, StringComparer.OrdinalIgnoreCase))
        {
            b.AppendLine($"  \"{name}\": id {Safe.Read(handler, static h => h.Id, -1)}, " +
                         $"address 0x{Safe.Read(handler, static h => h.Address, 0L):X}");
        }
    }

    /// <summary>The game's file names that match the entity pattern: which Augury tables the client has.</summary>
    private static void DescribeGameFiles(StringBuilder b, GameController gc, Regex pattern)
    {
        b.AppendLine();
        b.AppendLine("== Game files matching the entity pattern (Files.AllFiles)");

        var names = Safe.Read(gc, static g => g.Files.AllFiles.Keys.ToList(), null);

        if (names == null)
        {
            b.AppendLine("  unreadable");
            return;
        }

        var matching = names.Where(n => Matches(pattern, n))
            .OrderBy(static n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();

        b.AppendLine($"  {matching.Count} of {names.Count} file(s)");

        foreach (var name in matching)
            b.AppendLine($"  {name}");
    }

    /// <summary>What the game reports under the cursor, and the hovered tooltip's texts (a card, say).</summary>
    private static void DescribeHover(StringBuilder b, GameController gc)
    {
        b.AppendLine();
        b.AppendLine("== Hover");

        var root = Safe.Read(gc, static g => g.IngameState.UIRoot, null);
        var hover = Safe.Read(gc, static g => g.IngameState.UIHover, null);
        var hoverElement = Safe.Read(gc, static g => g.IngameState.UIHoverElement, null);
        var tooltip = Safe.Read(gc, static g => g.IngameState.UIHoverTooltip, null);

        b.AppendLine($"  mouse ({Safe.Read(gc, static g => g.IngameState.MousePosX, -1)}," +
                     $"{Safe.Read(gc, static g => g.IngameState.MousePosY, -1)})");
        b.AppendLine($"  UIHover:        {DescribeElement(hover)}");
        b.AppendLine($"    path below UIRoot: {DescribePath(PathFrom(root, hover))}");
        b.AppendLine($"  UIHoverElement: {DescribeElement(hoverElement)}");
        b.AppendLine($"    path below UIRoot: {DescribePath(PathFrom(root, hoverElement))}");
        b.AppendLine($"  UIHoverTooltip: {DescribeElement(tooltip)}");
        TooltipTexts(b, tooltip, "    ");
    }

    /// <summary>
    /// The texts under an element, one per line, a hidden element's marked "(hidden)". The mark matters in the Fates
    /// panel, whose hidden elements keep text from earlier cards.
    /// </summary>
    private static void TooltipTexts(StringBuilder b, Element element, string indent)
    {
        var texts = new List<string>();
        TextsOf(element, 0, texts);

        foreach (var line in texts)
            b.AppendLine($"{indent}{line}");
    }

    private static void TextsOf(Element element, int depth, List<string> into)
    {
        if (element == null || depth > 12 || into.Count >= MostTooltipTexts)
            return;

        if (Safe.Read(element, static e => e.Text, null) is { Length: > 0 } text && !string.IsNullOrWhiteSpace(text))
        {
            var hidden = Safe.Read(element, static e => e.IsVisible, false) ? "" : "(hidden) ";

            into.Add($"{hidden}\"{OneLine(text)}\"");
        }

        foreach (var child in Safe.ChildrenOf(element) ?? [])
            TextsOf(child, depth + 1, into);
    }

    /// <summary>
    /// Every server inventory the player has, with its items. Items whose metadata path matches the entity pattern are
    /// marked: if the Deck of Fates or the cards on offer are items, they show here.
    /// </summary>
    private static void DescribeInventories(StringBuilder b, GameController gc, Regex pattern)
    {
        b.AppendLine();
        b.AppendLine("== Player server inventories (ServerData.PlayerInventories)");

        var holders = Safe.Read(gc, static g => g.IngameState.ServerData.PlayerInventories.ToList(), null);

        if (holders == null)
        {
            b.AppendLine("  unreadable");
            return;
        }

        b.AppendLine($"  {holders.Count} inventory holder(s)");

        foreach (var holder in holders)
        {
            var inventory = Safe.Read(holder, static h => h.Inventory, null);
            var items = Safe.Read(inventory, static i => i.Items.ToList(), null);
            var columns = Safe.Read(inventory, static i => i.Columns, -1);
            var rows = Safe.Read(inventory, static i => i.Rows, -1);

            b.AppendLine($"  holder id {Safe.Read(holder, static h => h.Id, -1)}, " +
                         $"name {Safe.Read(holder, static h => h.TypeId.ToString(), "?")}: " +
                         $"type {Safe.Read(inventory, static i => i.InventType.ToString(), "?")}, " +
                         $"slot {Safe.Read(inventory, static i => i.InventSlot.ToString(), "?")}, {columns}x{rows}, " +
                         $"item count {Safe.Read(inventory, static i => i.ItemCount, -1L)}, " +
                         $"items listed {(items == null ? "unreadable" : items.Count.ToString())}");

            foreach (var item in items?.Take(MostItemsPerInventory) ?? [])
            {
                var path = Safe.Read(item, static e => e.Path, "?");
                var name = Safe.Read(item, static e => e.GetComponent<Base>()?.Name, "?");
                var mark = Matches(pattern, path) ? "  <== matches the entity pattern" : "";

                b.AppendLine($"    {name} - {path}{StackOf(item)}{mark}");
            }

            if (items?.Count > MostItemsPerInventory)
                b.AppendLine($"    ... {items.Count - MostItemsPerInventory} more");
        }
    }

    private static string StackOf(Entity item)
    {
        var size = Safe.Read(item, static e => e.GetComponent<Stack>()?.Size ?? 0, 0);

        return size > 1 ? $" x{size}" : "";
    }

    /// <summary>
    /// The entities whose metadata path matches the entity pattern, then the other entities carrying an Augury stat,
    /// each in full, then every metadata path in the area with its count, for paths the pattern misses. Returns each
    /// written entity's state by id for the changes section.
    /// </summary>
    private static Dictionary<uint, string> DescribeEntities(StringBuilder b, GameController gc, Regex pattern)
    {
        var states = new Dictionary<uint, string>();
        var entities = Safe.Read(gc, static g => g.EntityListWrapper.Entities.ToList(), null);

        b.AppendLine();
        b.AppendLine("== Entities whose metadata path matches the entity pattern");

        if (entities == null)
        {
            b.AppendLine("  unreadable");
            return states;
        }

        var labels = GroundLabelsByEntityId(gc);
        var matching = entities.Where(e => Matches(pattern, Safe.Read(e, static x => x.Path, null)))
            .OrderBy(static e => Safe.Read(e, static x => x.DistancePlayer, float.MaxValue))
            .ToList();

        b.AppendLine($"  {matching.Count} of {entities.Count} entities, nearest first");

        foreach (var entity in matching)
            states[Safe.Read(entity, static e => e.Id, 0u)] = DescribeEntity(b, entity, labels);

        b.AppendLine();
        b.AppendLine("== Other entities with an Augury stat");

        var withAuguryStat = entities
            .Where(e => !matching.Contains(e))
            .Where(static e => Safe.Read(e, static x => x.Stats?.Keys.Any(AuguryStats.Contains) ?? false, false))
            .ToList();

        b.AppendLine($"  {withAuguryStat.Count}");

        foreach (var entity in withAuguryStat)
            states[Safe.Read(entity, static e => e.Id, 0u)] = DescribeEntity(b, entity, labels);

        b.AppendLine();
        b.AppendLine("== Every metadata path in the area, with its count");

        foreach (var group in entities.GroupBy(static e => Safe.Read(e, static x => x.Path, "?") ?? "?")
                     .OrderBy(static g => g.Key, StringComparer.OrdinalIgnoreCase))
            b.AppendLine($"  {group.Count(),4}  {group.Key}");

        return states;
    }

    /// <summary>The visible ground labels' texts by entity id; an object the player can click may carry one.</summary>
    private static Dictionary<uint, string> GroundLabelsByEntityId(GameController gc)
    {
        var labels = new Dictionary<uint, string>();
        var visible = Safe.Read(gc, static g => g.IngameState.IngameUi.ItemsOnGroundLabelsVisible.ToList(), null);

        foreach (var label in visible ?? [])
        {
            var id = Safe.Read(label, static l => l.ItemOnGround?.Id ?? 0u, 0u);
            var text = Safe.Read(label, static l => l.Label?.Text, null);

            if (id != 0u && !string.IsNullOrWhiteSpace(text))
                labels[id] = OneLine(text);
        }

        return labels;
    }

    /// <summary>
    /// The rewards learned from the panel that the catalogue does not have (NewRewardRegistry), each with its family
    /// Id, copies, weight or "no weight", and when and at what area level it was first seen.
    /// </summary>
    private static void DescribeLearnedRewards(StringBuilder b, RewardSettings rewards)
    {
        var learned = rewards.NewRewards.Select(r =>
        {
            var id = NewRewardRegistry.IdOf(r);
            var weight = rewards.Weights.TryGetValue(id, out var set) ? $"weight {set}" : "no weight";

            return $"{id} \"{OneLine(r.Text)}\" ({r.CopiesNeeded} copies, {weight}, first seen {r.FirstSeen} " +
                   $"at area level {r.FirstSeenAreaLevel})";
        }).ToList();

        b.AppendLine($"  learned rewards, not in the catalogue: {learned.Count}");

        foreach (var line in learned)
            b.AppendLine($"    {line}");
    }

    /// <summary>
    /// One entity in full. Returns its state without its position and distance, so the changes section shows what the
    /// mechanic changed rather than where the player walked.
    ///
    /// The id-keyed components (Entity.HashComponents) are listed by key only: components the client keeps under a
    /// 16-bit id instead of a name, as PoE2's Expedition remnants keep their runes (component 0x87B2 in
    /// ExileCore2-AutoExpedition). What sits behind a key is not read, as that would be a raw read.
    /// </summary>
    private static string DescribeEntity(StringBuilder b, Entity entity, Dictionary<uint, string> labels)
    {
        var id = Safe.Read(entity, static e => e.Id, 0u);
        var state = new StringBuilder();

        void Line(string text)
        {
            b.AppendLine($"      {text}");
            state.AppendLine(text);
        }

        b.AppendLine();
        b.AppendLine($"  [id {id}] {Safe.Read(entity, static e => e.Path, "?")}");
        b.AppendLine($"      grid {Safe.Read(entity, static e => e.GridPosNum.ToString("0", null), "?")}, " +
                     $"distance {Safe.Read(entity, static e => e.DistancePlayer, -1f):0}");

        var renderName = Safe.Read(entity, static e => e.RenderName, "");
        var type = Safe.Read(entity, static e => e.Type.ToString(), "?");
        var league = Safe.Read(entity, static e => e.League.ToString(), "?");
        var rarity = Safe.Read(entity, static e => e.Rarity.ToString(), "?");

        Line($"render name \"{renderName}\", type {type}, league {league}, rarity {rarity}");
        Line($"valid {Safe.Read(entity, static e => e.IsValid, false)}, " +
             $"alive {Safe.Read(entity, static e => e.IsAlive, false)}, " +
             $"targetable {Safe.Read(entity, static e => e.IsTargetable, false)}, " +
             $"opened {Safe.Read(entity, static e => e.IsOpened, false)}, " +
             $"hidden {Safe.Read(entity, static e => e.IsHidden, false)}, " +
             $"hostile {Safe.Read(entity, static e => e.IsHostile, false)}");

        if (labels.TryGetValue(id, out var label))
            Line($"ground label \"{label}\"");

        var components = Safe.Read(entity, static e => e.CacheComp?.Keys.OrderBy(static k => k).ToList(), null);

        Line($"components ({components?.Count.ToString() ?? "unreadable"}): {string.Join(", ", components ?? [])}");

        var idKeyed = Safe.Read(entity, static e => e.HashComponents?.Keys.OrderBy(static k => k).ToList(), null);
        var idKeyedWritten = idKeyed?.Select(static k => $"0x{k:X4}");

        Line($"id-keyed components ({idKeyed?.Count.ToString() ?? "unreadable"}): " +
             string.Join(", ", idKeyedWritten ?? []));

        if (Safe.Read(entity, static e => e.GetComponent<StateMachine>(), null) is { } stateMachine)
        {
            var machineStates = Safe.Read(stateMachine, static s => s.States?.ToList(), null);
            var written = machineStates?.Select(static s =>
                $"{Safe.Read(s, static x => x.Name, "?")}={Safe.Read(s, static x => x.Value, 0L)}");

            var count = machineStates?.Count.ToString() ?? "unreadable";

            Line($"StateMachine ({count}): {string.Join(", ", written ?? [])}");
        }

        var stats = Safe.Read(entity, static e => e.Stats, null);
        var statsWritten = stats?.OrderBy(static s => s.Key.ToString()).Select(static s => $"{s.Key}={s.Value}");

        Line($"stats ({stats?.Count.ToString() ?? "unreadable"}): {string.Join(", ", statsWritten ?? [])}");

        var buffs = Safe.Read(entity, static e => e.Buffs?.ToList(), null);

        if (buffs is { Count: > 0 })
        {
            var buffsWritten = buffs.Select(static x =>
                $"{Safe.Read(x, static y => y.Name, "?")} \"{Safe.Read(x, static y => y.DisplayName, "")}\" " +
                $"charges {Safe.Read(x, static y => y.BuffCharges, (ushort)0)} " +
                $"stacks {Safe.Read(x, static y => y.BuffStacks, (ushort)0)}");

            Line($"buffs ({buffs.Count}): {string.Join(", ", buffsWritten)}");
        }

        if (Safe.Read(entity, static e => e.GetComponent<ObjectMagicProperties>(), null) is { } magic)
            Line($"ObjectMagicProperties mods: {string.Join(", ", Safe.Read(magic, static m => m.Mods, null) ?? [])}");

        if (Safe.Read(entity, static e => e.GetComponent<MinimapIcon>(), null) is { } icon)
        {
            Line($"MinimapIcon \"{Safe.Read(icon, static i => i.Name, "?")}\", " +
                 $"visible {Safe.Read(icon, static i => i.IsVisible, false)}, " +
                 $"hide {Safe.Read(icon, static i => i.IsHide, false)}");
        }

        if (Safe.Read(entity, static e => e.GetComponent<Animated>(), null) is { } animated)
        {
            var basePath = Safe.Read(animated, static a => a.BaseAnimatedObjectEntity?.Path, null);

            Line($"Animated base object: {basePath ?? "none"}");
        }

        if (Safe.Read(entity, static e => e.GetComponent<Render>(), null) is { } render)
            Line($"Render name \"{Safe.Read(render, static r => r.Name, "")}\"");

        return state.ToString();
    }

    /// <summary>
    /// The element tree under the interface root (UIRoot), one element per line with its path of child indices. Each
    /// top-level panel - every child of UIRoot, and every child of IngameUi, which is one of them - is walked on its
    /// own line budget. A hidden panel gets one line, except the Fates panel, which is walked in full, hidden elements
    /// included, open or not. Inside an open panel, hidden elements are walked too when includeHidden is set. Then the
    /// texts of every distinct tooltip the walked elements hold. Returns each element's description by path, for the
    /// changes section.
    /// </summary>
    private static Dictionary<string, string> DescribeInterface(StringBuilder b, GameController gc, Regex pattern,
        int mostDepth, bool includeHidden)
    {
        var tree = new Dictionary<string, string>();

        b.AppendLine();
        b.AppendLine($"== Interface element tree (paths are child indices from UIRoot, {mostDepth} levels below each " +
                     $"panel at most, hidden elements inside open panels {(includeHidden ? "included" : "left out")})");

        var root = Safe.Read(gc, static g => g.IngameState.UIRoot, null);
        var ingameUi = Safe.Read(gc, static g => (Element)g.IngameState.IngameUi, null);
        var ingameUiAddress = Safe.Read(ingameUi, static e => e.Address, 0L);

        if (root == null)
        {
            b.AppendLine("  UIRoot unreadable");
            return tree;
        }

        b.AppendLine($"  IngameUi is at {DescribePath(PathFrom(root, ingameUi))}");

        // Addresses worth marking where they turn up in the tree.
        var marks = new Dictionary<long, string>();

        void Mark(Element element, string what)
        {
            var address = Safe.Read(element, static e => e.Address, 0L);

            if (address != 0L)
                marks[address] = marks.TryGetValue(address, out var had) ? $"{had}, {what}" : what;
        }

        Mark(Safe.Read(gc, static g => g.IngameState.UIHover, null), "UIHover");
        Mark(Safe.Read(gc, static g => g.IngameState.UIHoverElement, null), "UIHoverElement");
        Mark(Safe.Read(gc, static g => g.IngameState.UIHoverTooltip, null), "UIHoverTooltip");
        Mark(Safe.Read(gc, static g => g.IngameState.FocusedInputElement, null), "focused input");

        var tooltips = new List<(string Path, Element Tooltip)>();
        var tooltipAddresses = new HashSet<long>();
        var totalLines = 0;

        void Walk(Element element, string path, int depth, bool walkHidden, ref int panelLines)
        {
            if (element == null || panelLines >= MostTreeLinesPerPanel || totalLines >= MostTreeLines)
                return;

            var description = DescribeElement(element);
            var address = Safe.Read(element, static e => e.Address, 0L);
            var entityPath = Safe.Read(element, static e => e.Entity?.Path, null);
            var mark = marks.TryGetValue(address, out var what) ? $"  <== {what}" : "";

            if (Matches(pattern, entityPath))
                mark += "  <== entity matches the entity pattern";

            var tooltip = Safe.Read(element, static e => e.Tooltip, null);
            var tooltipAddress = Safe.Read(tooltip, static t => t.Address, 0L);

            if (tooltipAddress != 0L && tooltipAddress != address)
            {
                mark += "  <== holds a tooltip";

                if (tooltipAddresses.Add(tooltipAddress) && tooltips.Count < MostTooltips)
                    tooltips.Add((path, tooltip));
            }

            tree[path] = description;
            b.AppendLine($"  {new string(' ', Math.Min(depth, 40))}[{path}] {description}{mark}");
            panelLines++;
            totalLines++;

            if (depth >= mostDepth || !walkHidden && !Safe.Read(element, static e => e.IsVisible, false))
                return;

            var children = Safe.ChildrenOf(element) ?? [];

            for (var i = 0; i < children.Count; i++)
                Walk(children[i], $"{path},{i}", depth + 1, walkHidden, ref panelLines);
        }

        void WalkPanel(Element panel, string path, int depth)
        {
            var panelLines = 0;
            var fatesPanel = FatesPanel.IsFatesPanel(panel);

            if (fatesPanel)
                Mark(panel, "Fates panel");

            // The Fates panel is walked in full whether open or not: whether it holds choices before it is opened, or
            // more than the set it shows, is what the dump is for.
            if (fatesPanel || Safe.Read(panel, static e => e.IsVisible, false))
            {
                Walk(panel, path, depth, includeHidden || fatesPanel, ref panelLines);

                if (panelLines >= MostTreeLinesPerPanel)
                    b.AppendLine($"  ... panel [{path}] stopped at {MostTreeLinesPerPanel} lines");

                return;
            }

            // A closed panel: one line, not descended into.
            var description = DescribeElement(panel);

            tree[path] = description;
            b.AppendLine($"  {new string(' ', depth)}[{path}] {description}");
            totalLines++;
        }

        var top = Safe.ChildrenOf(root) ?? [];

        b.AppendLine($"  [] UIRoot {DescribeElement(root)}");

        for (var i = 0; i < top.Count; i++)
        {
            var child = top[i];

            if (ingameUiAddress == 0L || Safe.Read(child, static e => e.Address, 0L) != ingameUiAddress)
            {
                WalkPanel(child, $"{i}", 1);
                continue;
            }

            var description = DescribeElement(child);

            tree[$"{i}"] = description;
            b.AppendLine($"   [{i}] {description}  <== IngameUi");
            totalLines++;

            var panels = Safe.ChildrenOf(child) ?? [];

            for (var j = 0; j < panels.Count; j++)
                WalkPanel(panels[j], $"{i},{j}", 2);
        }

        if (totalLines >= MostTreeLines)
            b.AppendLine($"  ... stopped at {MostTreeLines} lines");

        b.AppendLine();
        b.AppendLine($"== Tooltips held by walked elements ({tooltips.Count} distinct, {MostTooltips} at most)");

        foreach (var (path, tooltip) in tooltips)
        {
            b.AppendLine($"  held by [{path}]: {DescribeElement(tooltip)}");
            TooltipTexts(b, tooltip, "    ");
        }

        return tree;
    }

    private static void DescribeChanges(StringBuilder b, Dictionary<uint, string> entities,
        Dictionary<string, string> tree)
    {
        b.AppendLine();

        if (_previousTree == null || _previousEntities == null)
        {
            b.AppendLine("== Changes since the previous dump: none to compare - " +
                         "this is the first dump since the plugin loaded");
            return;
        }

        b.AppendLine($"== Changes since the previous dump at {_previousAt:HH:mm:ss}");

        var written = 0;

        void Line(string text)
        {
            if (written++ < MostChangeLines)
                b.AppendLine(text);
        }

        Line("  -- entities (by id, position left out)");

        foreach (var (id, now) in entities)
        {
            if (!_previousEntities.TryGetValue(id, out var before))
                Line($"  appeared [id {id}]");
            else if (before != now)
            {
                Line($"  changed  [id {id}]");

                foreach (var (was, @is) in ChangedLines(before, now))
                {
                    Line($"    before: {was}");
                    Line($"    now:    {@is}");
                }
            }
        }

        foreach (var id in _previousEntities.Keys.Where(id => !entities.ContainsKey(id)))
            Line($"  gone     [id {id}]");

        Line("  -- element tree (by path; a shifted child index shows as many changes)");

        foreach (var (path, now) in tree)
        {
            if (!_previousTree.TryGetValue(path, out var before))
                Line($"  appeared [{path}] {now}");
            else if (before != now)
            {
                Line($"  changed  [{path}]");
                Line($"    before: {before}");
                Line($"    now:    {now}");
            }
        }

        foreach (var (path, before) in _previousTree)
        {
            if (!tree.ContainsKey(path))
                Line($"  gone     [{path}] {before}");
        }

        if (written > MostChangeLines)
            b.AppendLine($"  ... {written - MostChangeLines} more lines not written");
    }

    /// <summary>The lines that differ between two entity states, paired by position; a missing line is empty.</summary>
    private static IEnumerable<(string Before, string Now)> ChangedLines(string before, string now)
    {
        var was = before.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var @is = now.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        for (var i = 0; i < Math.Max(was.Length, @is.Length); i++)
        {
            var a = i < was.Length ? was[i].TrimEnd('\r') : "";
            var c = i < @is.Length ? @is[i].TrimEnd('\r') : "";

            if (a != c)
                yield return (a, c);
        }
    }

    /// <summary>
    /// The child-index path from root down to element, walking up element's Parent chain and finding each step's index
    /// among its parent's children by address. Null when element is not below root within 64 levels, or a step cannot
    /// be read.
    /// </summary>
    private static int[] PathFrom(Element root, Element element)
    {
        var rootAddress = Safe.Read(root, static r => r.Address, 0L);

        if (rootAddress == 0L || element == null)
            return null;

        var indices = new List<int>();
        var at = element;

        for (var depth = 0; depth < 64 && at != null; depth++)
        {
            var address = Safe.Read(at, static e => e.Address, 0L);

            if (address == 0L)
                return null;

            if (address == rootAddress)
            {
                indices.Reverse();
                return indices.ToArray();
            }

            var parent = Safe.Read(at, static e => e.Parent, null);
            var siblings = Safe.ChildrenOf(parent);

            if (siblings == null)
                return null;

            var index = -1;

            for (var i = 0; i < siblings.Count && index < 0; i++)
            {
                if (Safe.Read(siblings[i], static s => s.Address, 0L) == address)
                    index = i;
            }

            if (index < 0)
                return null;

            indices.Add(index);
            at = parent;
        }

        return null;
    }

    private static string DescribePath(int[] path) => path == null ? "unknown" : $"[{string.Join(",", path)}]";

    /// <summary>
    /// One element on one line: enough to recognise it and to see a choice change. Rectangles are in client pixels.
    /// Only ExileCore's own properties are read. Deliberately has no address in it, because the changes section
    /// compares these strings and an address that changes when a panel is rebuilt would mark everything changed.
    /// </summary>
    private static string DescribeElement(Element element)
    {
        if (element == null || Safe.Read(element, static e => e.Address, 0L) == 0L)
            return "none";

        var text = Safe.Read(element, static e => e.Text, null);
        var texture = Safe.Read(element, static e => e.TextureName, null);
        var entityPath = Safe.Read(element, static e => e.Entity?.Path, null);

        var parts = new StringBuilder();

        parts.Append(Rect(Safe.Read(element, static e => e.GetClientRectCache, default(RectangleF))));
        parts.Append(Safe.Read(element, static e => e.IsVisible, false) ? " visible" : " hidden");
        parts.Append($" kids {Safe.Read(element, static e => e.ChildCount, -1L)}");

        if (Safe.Read(element, static e => e.isHighlighted, false))
            parts.Append(" highlighted");

        if (Safe.Read(element, static e => e.IsActive, false))
            parts.Append(" active");

        if (Safe.Read(element, static e => e.IsSaturated, false))
            parts.Append(" saturated");

        parts.Append($" bg {Colour(Safe.Read(element, static e => e.BgColor, default(ColorBGRA)))}");
        parts.Append($" border {Colour(Safe.Read(element, static e => e.BordColor, default(ColorBGRA)))}");

        if (!string.IsNullOrWhiteSpace(text))
            parts.Append($" text \"{OneLine(text)}\"");

        if (!string.IsNullOrEmpty(texture))
            parts.Append($" texture {texture}");

        if (!string.IsNullOrEmpty(entityPath))
            parts.Append($" entity {entityPath}");

        return parts.ToString();
    }

    private static string Rect(RectangleF r) => $"({r.X:0},{r.Y:0} {r.Width:0}x{r.Height:0})";

    private static string Colour(ColorBGRA c) => $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";

    private static string OneLine(string text) => (text ?? "").Trim().Replace("\r", "").Replace("\n", " | ");
}
