using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using ExileCore;
using ExileCore.Shared.Nodes;

[assembly: InternalsVisibleTo("AutoAugury.Tests")]

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
    /// <summary>The ConfigVersion a saved file reaches once every step in ApplySettingsChanges has run.</summary>
    private const int CurrentConfigVersion = 0;

    /// <summary>How often the Fates panel is read, in ms. Chosen: the choice is made at human speed.</summary>
    private const int PanelReadIntervalMs = 100;

    /// <summary>
    /// The plugin's place in ExileCore's plugin order (BaseSettingsPlugin.Order): 5001, the user's choice, so it runs
    /// after MinimapIcons and the larger Cauldron icon is drawn over the icons that plugin draws. It orders the whole
    /// plugin, the Fates panel overlay included. Set in the constructor and again in Initialise, as
    /// DeepwaterEngagementSuite sets its own.
    /// </summary>
    private const int PluginOrder = 5001;

    private readonly FatesPanel _fatesPanel = new();
    private readonly Stopwatch _sincePanelRead = Stopwatch.StartNew();

    /// <summary>The area's Cauldrons, for the offers still to come (the copy potential). Read by Dump.</summary>
    internal CauldronTracker Cauldrons { get; } = new();

    // The invocation slots last seen at a boss's panel: the choosing view comes before any boss view in an
    // area, so the count carries over from earlier areas until a boss view shows it again.
    internal int ObservedSlots { get; private set; }

    // What the scores were last computed for: the panel, the weights and the tracker's generation. Null until the
    // first read and after ForgetPanel, so the first read with the plugin enabled always scores.
    private (int Panel, int Settings, int Tracker)? _lastScoreFingerprint;

    // Whether the slot-count mismatch has been logged for this area (AreaChange resets it).
    private bool _slotMismatchLogged;

    // Empty open-panel reads in a row; the warning fires once the panel has read empty for about five seconds.
    private int _emptyPanelReads;

    /// <summary>100 ms reads after which an open-but-empty panel is reported (a patch may have moved it).</summary>
    private const int EmptyPanelWarningAfter = 50;

    // Whether an unexpected offer size has been logged for this area (AreaChange resets it).
    private bool _offerSizeLogged;

    // Published whole by the Tick, drawn by Render.
    private FatesPanelState _panelState = FatesPanelState.Closed;
    private IReadOnlyList<FateScore> _scores = [];
    private InvocationGuide _guide = InvocationGuide.None;
    private (int Current, int Total, string Source) _cauldronProgress;

    public AutoAugury() => Order = PluginOrder;

    public override bool Initialise()
    {
        Order = PluginOrder;
        DumpFolder.ConfigDirectory = ConfigDirectory;
        RegisterHotkey(Settings.Debug.DumpHotkey);
        ApplySettingsChanges();
        Settings.Rewards.ForgetWeightsAtDefault();
        CauldronMapIcons.LoadIcons(Graphics, DirectoryFullName);

        return true;
    }

    /// <summary>
    /// Brings a saved settings file up to date with changed defaults, once per change. No default has changed since the
    /// plugin was published, so there are no steps yet; the first adds one and raises CurrentConfigVersion to 1.
    /// </summary>
    private void ApplySettingsChanges()
    {
        if (Settings.ConfigVersion >= CurrentConfigVersion)
            return;

        Settings.ConfigVersion = CurrentConfigVersion;
    }

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

            var state = _fatesPanel.Read(GameController);

            _panelState = state;
            var areaLevel = Safe.Read(GameController, static g => g.Area.CurrentArea.RealLevel, 0);
            var slots = InvocationAdvisor.SlotsFor(state, ObservedSlots);
            NoteSlots(state);
            NoteOfferSize(state);
            NoteEmptyPanel(state);

            // The scores cost subsets and copy potentials: recompute only when the cards, the weights or the
            // tracker changed. The guide rides the same gate; its placed cards are in the fingerprint.
            var fingerprint = (FateScoring.FingerprintOf(state), FateScoring.SettingsFingerprint(Settings),
                Cauldrons.Version);
            var changed = fingerprint != _lastScoreFingerprint;

            if (changed)
                _lastScoreFingerprint = fingerprint;

            if (state.ChoosingFate)
            {
                if (changed)
                {
                    _scores = FateScoring.Score(state, Settings,
                        FateScoring.OffersToComeFor(state, areaLevel, Cauldrons), slots);
                    _cauldronProgress = FateScoring.CauldronProgressOf(state, areaLevel, Cauldrons);
                }
            }
            else
            {
                _scores = [];
                _cauldronProgress = default;
            }

            _guide = state.InvokingFates
                ? changed ? InvocationAdvisor.GuideFor(state, Settings, ObservedSlots) : _guide
                : InvocationGuide.None;
        }

        return null;
    }

    /// <summary>
    /// Remembers the boss view's slot count, and logs once per area when it is not the assumed one: atlas passives
    /// or anything else may add slots, and the advice is computed for the count seen.
    /// </summary>
    private void NoteSlots(FatesPanelState state)
    {
        if (!state.InvokingFates || state.TopRowSlots.Count <= 0)
            return;

        ObservedSlots = state.TopRowSlots.Count;

        if (ObservedSlots == InvocationAdvisor.DefaultSlots || _slotMismatchLogged)
            return;

        _slotMismatchLogged = true;
        DebugWindow.LogMsg("[AutoAugury] The boss panel shows " + ObservedSlots + " invocation slots, not the " +
            "assumed " + InvocationAdvisor.DefaultSlots + "; advising for " + ObservedSlots + ".", 10f);
    }

    /// <summary>
    /// Logs once per area when offers show another number of Fates than the copy potential models: the model is an
    /// override away (Debug > Fates per offer), and a wrong one quietly skews every potential weight.
    /// </summary>
    private void NoteOfferSize(FatesPanelState state)
    {
        if (!state.ChoosingFate || state.Offered.Count <= 0 || _offerSizeLogged)
            return;

        if (state.Offered.Count == Settings.Debug.FatesPerOffer.Value)
            return;

        _offerSizeLogged = true;
        DebugWindow.LogMsg("[AutoAugury] Offers show " + state.Offered.Count + " Fates, not the modelled " +
            Settings.Debug.FatesPerOffer.Value + "; set Debug > Fates per offer to match.", 10f);
    }

    /// <summary>
    /// Counts open-but-empty panel reads: the panel always shows card frames, so repeats mean the client's layout
    /// moved and the reader's indices no longer land. Warns once per stretch, pointing at the dump.
    /// </summary>
    private void NoteEmptyPanel(FatesPanelState state)
    {
        if (!FatesPanel.ReadsEmpty(state))
        {
            _emptyPanelReads = 0;
            return;
        }

        if (++_emptyPanelReads == EmptyPanelWarningAfter)
            DebugWindow.LogMsg("[AutoAugury] The Fates panel is open but no cards read. A patch may have moved " +
                "the panel; take Debug > Write a dump now and check the element tree.", 10f);
    }

    private void ForgetPanel()
    {
        _panelState = FatesPanelState.Closed;
        _scores = [];
        _guide = InvocationGuide.None;
        _lastScoreFingerprint = null;
        _emptyPanelReads = 0;
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
        _slotMismatchLogged = false;
        _offerSizeLogged = false;
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
                _panelState.Visible, display.MarkOwedIcon.Value);

        if (display.ShowScores)
        {
            FateOverlay.DrawScores(Graphics, _scores, display);

            if (_panelState.ChoosingFate)
                FateOverlay.DrawCauldronProgress(Graphics, _panelState, _cauldronProgress);
        }

        if (display.ShowInvocationAdvice && _panelState.InvokingFates)
            FateOverlay.DrawGuide(Graphics, _guide, _panelState, display);

        if (display.PreviewScores && !_panelState.Visible)
            DrawPreview(display);

        if (display.ShowStatusLine.Value)
            DrawStatusLine(display);
    }

    /// <summary>
    /// Three sample scores through the overlay's own drawing, with the display's palette, thickness and gap: what the
    /// choosing overlay looks like without waiting for a Cauldron. Hidden while the Fates panel is open.
    /// </summary>
    private void DrawPreview(DisplaySettings display)
    {
        var window = Safe.Read(GameController, static g => g.Window.GetWindowRectangleTimeCache,
            default(SharpDX.RectangleF));

        if (window.Width <= 0f)
            return;

        var fates = AuguryCatalogue.Fates;
        var crowkeeper = fates.FirstOrDefault(f => f.Id == "Crowkeeper");
        var dryad = fates.FirstOrDefault(f => f.Id == "Dryad");
        var y = window.Y + window.Height / 2f - 120f;
        var cards = new[]
        {
            new FateCard("Crowkeeper", crowkeeper, null, new SharpDX.RectangleF(window.X + window.Width / 4f - 90f, y, 180f, 240f), [], false),
            new FateCard("Dryad", dryad, 2, new SharpDX.RectangleF(window.X + window.Width / 2f - 90f, y, 180f, 240f), [], false),
            new FateCard("Unknown", null, null, new SharpDX.RectangleF(window.X + window.Width * 3f / 4f - 90f, y, 180f, 240f), [], false),
        };
        var scores = new FateScore[]
        {
            new(cards[0], 42, 20, 22d, true, null, 3, 0),
            new(cards[1], 36, 58, -22d, true, null, 3, 0),
            new(cards[2], 5, 5, 0d, false, null, 3, 1),
        };

        FateOverlay.DrawScores(Graphics, scores, display);
    }

    /// <summary>The tracker's state at a glance, bottom left: Cauldrons tracked, slots advised for, Fates per offer.</summary>
    private void DrawStatusLine(DisplaySettings display)
    {
        var window = Safe.Read(GameController, static g => g.Window.GetWindowRectangleTimeCache,
            default(SharpDX.RectangleF));

        if (window.Width <= 0f)
            return;

        var text = FateOverlay.StatusLineOf(Cauldrons.Cauldrons.Count,
            InvocationAdvisor.SlotsFor(_panelState, ObservedSlots), Settings.Debug.FatesPerOffer.Value);

        Graphics.DrawTextWithBackground(text,
            new System.Numerics.Vector2(window.X + 8f, window.Y + window.Height - 24f),
            Colours.FromArgb(255, 245, 245, 245), ExileCore.Shared.Enums.FontAlign.Left,
            Colours.FromArgb(200, 0, 0, 0));
    }
}
