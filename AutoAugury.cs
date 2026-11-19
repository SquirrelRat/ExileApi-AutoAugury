using System.Collections.Generic;
using System.Diagnostics;
using System.Windows.Forms;
using ExileCore;
using ExileCore.Shared.Nodes;

namespace AutoAugury;

/// <summary>
/// A plugin for the 3.30 league mechanic, Augury (Covens of Umbra), in which each completed Cauldron encounter offers a
/// choice of Fates for the area's collection, and Fates from the collection are invoked on the area's boss.
///
/// While the Fates panel offers a choice, it scores each offered Fate by the player's reward and Fate weights, taking
/// the collection into account (FateScoring), and draws the scores over the cards (FateOverlay). The panel is read by
/// FatesPanel and matched against the game's own tables, held as static data in AuguryCatalogue.
///
/// A Cauldron's offer is not on the client before it is opened (searched for with memory scans during development,
/// 2026-11-14), so the plugin can only weigh the offer on screen. The Debug section holds the dump.
/// </summary>
public class AutoAugury : BaseSettingsPlugin<AutoAugurySettings>
{
    /// <summary>How often the Fates panel is read, in ms. Chosen: the choice is made at human speed.</summary>
    private const int PanelReadIntervalMs = 100;

    /// <summary>
    /// The plugin's place in ExileCore's plugin order (BaseSettingsPlugin.Order): 5001, the user's choice, so it runs
    /// after MinimapIcons and the larger Cauldron icon is drawn over the icons that plugin draws. It orders the whole
    /// plugin, the Fates panel overlay included. Set in the constructor and again in Initialise, as
    /// DeepwaterEngagementSuite sets its own.
    /// </summary>
    private const int PluginOrder = 5001;

    /// <summary>How long a message from SettingsChanges stays under the cursor: 8 s, the user's figure.</summary>
    private const double SettingsChangeMessageSeconds = 8d;

    /// <summary>
    /// A message from SettingsChanges, held until the player is in game, so it is not shown at the HUD's start where
    /// nobody would read it; null when none waits.
    /// </summary>
    private string _messageOnceInGame;

    private readonly FatesPanel _fatesPanel = new();
    private readonly Stopwatch _sincePanelRead = Stopwatch.StartNew();

    /// <summary>The area's Cauldrons, for the offers still to come (the copy potential). Read by Dump.</summary>
    internal CauldronTracker Cauldrons { get; } = new();

    // Published whole by the Tick, drawn by Render.
    private FatesPanelState _panelState = FatesPanelState.Closed;
    private IReadOnlyList<FateScore> _scores = [];
    private InvocationGuide _guide = InvocationGuide.None;
    private (int Current, double Total) _cauldronProgress;

    public AutoAugury() => Order = PluginOrder;

    public override bool Initialise()
    {
        Order = PluginOrder;
        DumpFolder.ConfigDirectory = ConfigDirectory;
        RegisterHotkey(Settings.Debug.DumpHotkey);
        ApplySettingsChanges();
        NewRewardRegistry.Tidy(Settings.Rewards);
        Settings.Rewards.ForgetWeightsAtDefault();
        CauldronMapIcons.LoadIcons(Graphics, DirectoryFullName);

        return true;
    }

    /// <summary>
    /// Brings a saved settings file up to date with changed defaults, once per change (SettingsChanges), keeping any
    /// message for the player until they are in game.
    /// </summary>
    private void ApplySettingsChanges() => _messageOnceInGame = SettingsChanges.Apply(Settings);

    /// <summary>
    /// Tells Input about a hotkey so PressedOnce answers, and again whenever it is rebound in the menu. A key left
    /// unbound is not registered, since registering None would claim a key nobody pressed.
    /// </summary>
    private static void RegisterHotkey(HotkeyNodeV2 hotkey)
    {
        RegisterIfBound(hotkey);
        hotkey.OnValueChanged += () => RegisterIfBound(hotkey);
    }

    private static void RegisterIfBound(HotkeyNodeV2 hotkey)
    {
        if (IsBound(hotkey))
            Input.RegisterKey(hotkey.Value);
    }

    private static bool IsBound(HotkeyNodeV2 hotkey) =>
        Safe.Read(hotkey, static h => h.Value.Key, Keys.None) != Keys.None;

    /// <summary>ExileApi's Tick returns a job to run in the background; this plugin runs none.</summary>
    public override Job Tick()
    {
        // Before the Enable check: a disabled plugin's player is told too.
        if (_messageOnceInGame != null && Safe.Read(GameController, static g => g.InGame, false))
        {
            CursorMessage.Show(CursorMessage.Kind.Error, _messageOnceInGame, SettingsChangeMessageSeconds);
            _messageOnceInGame = null;
        }

        AnswerDebugTools();

        if (!Settings.Enable || !Safe.Read(GameController, static g => g.InGame, false))
        {
            ForgetPanel();
            return null;
        }

        Cauldrons.Update(GameController);

        if (_sincePanelRead.ElapsedMilliseconds >= PanelReadIntervalMs)
        {
            _sincePanelRead.Restart();

            var areaLevel = Safe.Read(GameController, static g => g.Area.CurrentArea.RealLevel, 0);
            var resolution = NewRewardRegistry.Resolve(_fatesPanel.Read(GameController), Settings.Rewards, areaLevel,
                true);
            var state = resolution.State;

            CatalogueWarnings.Update(resolution, Settings.Rewards);
            _panelState = state;

            _scores = state.ChoosingFate
                ? FateScoring.Score(state, Settings,
                    FateScoring.OffersToComeFor(state, areaLevel, Cauldrons))
                : [];
            // The game's Fate counter, when the panel shows it, says what "Estimated: 1/3" would.
            _cauldronProgress = state.ChoosingFate && state.Counter == null
                ? FateScoring.CauldronProgressOf(state, areaLevel, Cauldrons)
                : default;
            _guide = state.InvokingFates ? InvocationAdvisor.GuideFor(state, Settings) : InvocationGuide.None;
        }

        return null;
    }

    private void ForgetPanel()
    {
        _panelState = FatesPanelState.Closed;
        _scores = [];
        _guide = InvocationGuide.None;
    }

    /// <summary>
    /// The dump answers with the plugin disabled too: what it is for is reading the client before the plugin does
    /// anything with it.
    /// </summary>
    private void AnswerDebugTools()
    {
        var dumpHotkey = Settings.Debug.DumpHotkey;

        if (IsBound(dumpHotkey) && dumpHotkey.PressedOnce() || Dump.AskedForInSettings)
        {
            Dump.AskedForInSettings = false;
            Dump.Write(this);
        }
    }

    public override void AreaChange(AreaInstance area)
    {
        _fatesPanel.Forget();
        Cauldrons.EnterArea(Safe.Read(area, static a => a?.Hash ?? 0u, 0u),
            Safe.Read(area, static a => a != null && (a.IsTown || a.IsHideout), false));
        ForgetPanel();
    }

    public override void Render()
    {
        // Before any Enable check, so a debug tool's message shows with the plugin switched off.
        CursorMessage.Draw(Graphics, GameController);

        if (!Settings.Enable)
            return;

        var display = Settings.Display;

        // First, so the panel's marks go over the large map's icons.
        if (display.DrawLargerMinimapIcon)
            CauldronMapIcons.Draw(Graphics, GameController, Cauldrons.Cauldrons, display.MinimapIconSize.Value,
                _panelState.Visible);

        if (display.ShowScores)
        {
            FateOverlay.DrawScores(Graphics, _scores, display);

            if (_panelState.ChoosingFate)
                FateOverlay.DrawCauldronProgress(Graphics, _panelState, _cauldronProgress);
        }

        if (display.ShowInvocationAdvice && _panelState.InvokingFates)
            FateOverlay.DrawGuide(Graphics, _guide, _panelState);

        if (_panelState.Visible)
        {
            FateOverlay.DrawMustTakeRewards(Graphics, _panelState, Settings.Rewards);
            FateOverlay.DrawRewardsWithoutWeight(Graphics, _panelState, Settings.Rewards);
        }
    }
}
