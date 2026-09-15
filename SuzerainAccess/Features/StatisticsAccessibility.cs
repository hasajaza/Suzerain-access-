using System;
using System.Collections.Generic;
using System.Diagnostics;
using SuzerainAccess.Config;
using SuzerainAccess.Core;
using SuzerainAccess.Game;
using SuzerainAccess.Speech;
using SuzerainAccess.UI;

namespace SuzerainAccess.Features
{
    /// <summary>
    /// Exposes gameplay numbers that the game shows in its statistics bar (HUDPanel) and announces
    /// changes, plus the turn number. Verified sources:
    ///  HUDPanel.instantiatedHUDStats : List&lt;TemplateHUDStat&gt;  (currentValue is the real int value)
    ///  HUDPanel.instantiatedHUDTextStats : List&lt;TemplateHUDTextStat&gt; (text status coloured red/yellow/green)
    ///  Managers.Instance.GameFlowManager.currentTurnNo / currentTurn.TransitionTitle
    ///  Singleton&lt;GameManager&gt;.Instance.GetGameState(); Managers.Instance.MapManager.GetCurrentMapType()
    /// </summary>
    internal sealed class StatisticsAccessibility
    {
        private const float PollInterval = 0.5f;

        private readonly SpeechManager _speech;
        private readonly ScreenTracker _screen;
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private float _nextPoll;

        private readonly Dictionary<string, int> _statValues = new Dictionary<string, int>();
        private readonly Dictionary<string, string> _textStatValues = new Dictionary<string, string>();
        private IntPtr _hudPointer;
        private int _lastTurn = -1;

        public StatisticsAccessibility(SpeechManager speech, ScreenTracker screen)
        {
            _speech = speech;
            _screen = screen;
        }

        private float Now => (float)_clock.Elapsed.TotalSeconds;

        public void Tick()
        {
            if (Now < _nextPoll) return;
            _nextPoll = Now + PollInterval;
            try { PollStats(); } catch (Exception ex) { ModLog.Exception("stats", ex); }
            try { PollTurn(); } catch (Exception ex) { ModLog.Exception("turn", ex); }
        }

        private static HUDPanel GetHud()
        {
            var panels = ScreenTracker.GetPanels();
            if (panels == null) return null;
            var hud = panels.HUDPanel;
            return UiUtil.Alive(hud) ? hud : null;
        }

        private void PollStats()
        {
            var hud = GetHud();
            if (hud == null || !hud.IsShowing()) return;

            // New scene / new campaign: take a fresh baseline without announcing.
            bool baseline = hud.Pointer != _hudPointer;
            if (baseline)
            {
                _hudPointer = hud.Pointer;
                _statValues.Clear();
                _textStatValues.Clear();
            }

            var changes = new List<string>();
            var stats = hud.instantiatedHUDStats;
            for (int i = 0; stats != null && i < stats.Count; i++)
            {
                var s = stats[i];
                if (!UiUtil.Alive(s) || !s.gameObject.activeInHierarchy) continue;
                string id = StatId(s);
                int value = s.currentValue;
                if (_statValues.TryGetValue(id, out int old))
                {
                    if (old != value)
                        changes.Add(GameText.StatName(s) + " " + GameText.StatValue(s) + ", " + (value > old ? "up" : "down") + " from " + TextUtil.Signed(old));
                }
                _statValues[id] = value;
            }

            var textStats = hud.instantiatedHUDTextStats;
            for (int i = 0; textStats != null && i < textStats.Count; i++)
            {
                var s = textStats[i];
                if (!UiUtil.Alive(s) || !s.gameObject.activeInHierarchy) continue;
                string name = GameText.TextStatName(s);
                string value = GameText.TextStatValue(s);
                if (_textStatValues.TryGetValue(name, out var old) && old != value && !string.IsNullOrEmpty(value))
                    changes.Add(name + ": " + value);
                _textStatValues[name] = value;
            }

            if (!baseline && changes.Count > 0 && ModConfig.AnnounceStatChanges.Value)
                _speech.Say(TextUtil.JoinWith(". ", changes) + ".", interrupt: false, important: true, automatic: true);
        }

        private static string StatId(TemplateHUDStat s)
        {
            try
            {
                var d = s.currentHUDStatData;
                if (d != null && !string.IsNullOrEmpty(d.Id)) return d.Id;
            }
            catch { }
            return s.Pointer.ToInt64().ToString();
        }

        private void PollTurn()
        {
            var gfm = GetGameFlow();
            if (gfm == null) { _lastTurn = -1; return; }
            int turn = gfm.currentTurnNo;
            if (turn == _lastTurn) return;
            bool first = _lastTurn < 0;
            _lastTurn = turn;
            if (turn <= 0 || !ModConfig.AnnounceTurnChanges.Value) return;
            if (first && _screen.IsShowing("MainMenuPanel")) return;
            _speech.Say(TurnText(gfm), interrupt: false, important: true, automatic: true);
        }

        private static GameFlowManager GetGameFlow()
        {
            try
            {
                var managers = Managers.Instance;
                if (!UiUtil.Alive(managers)) return null;
                var gfm = managers.GameFlowManager;
                return UiUtil.Alive(gfm) ? gfm : null;
            }
            catch { return null; }
        }

        private static string TurnText(GameFlowManager gfm)
        {
            string text = "Turn " + gfm.currentTurnNo;
            try
            {
                var t = gfm.currentTurn;
                if (t != null && !string.IsNullOrWhiteSpace(t.TransitionTitle)) text += ": " + TextUtil.Clean(t.TransitionTitle);
            }
            catch { }
            return text + ".";
        }

        // ------------------------------------------------------------------ commands

        /// <summary>
        /// All statistics currently shown, in a fixed order: the numeric statistics in the order the game
        /// created them (HUDPanel.instantiatedHUDStats), then the text statistics (instantiatedHUDTextStats).
        /// Alt+1..Alt+9 and the numbers F4 speaks use this same order.
        /// </summary>
        private static List<string> StatEntries(HUDPanel hud, bool withModifiers)
        {
            var entries = new List<string>();
            var stats = hud.instantiatedHUDStats;
            for (int i = 0; stats != null && i < stats.Count; i++)
            {
                var s = stats[i];
                if (!UiUtil.Alive(s) || !s.gameObject.activeInHierarchy) continue;
                string name = GameText.StatName(s), value = GameText.StatValue(s);
                if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(value)) continue;
                string entry = string.IsNullOrWhiteSpace(name) ? value : name + ": " + value;
                if (withModifiers && ModConfig.DetailedNumbers.Value)
                {
                    string mods = GameText.StatModifiers(s);
                    if (!string.IsNullOrEmpty(mods)) entry += " (" + mods + ")";
                }
                entries.Add(entry);
            }
            var textStats = hud.instantiatedHUDTextStats;
            for (int i = 0; textStats != null && i < textStats.Count; i++)
            {
                var s = textStats[i];
                if (!UiUtil.Alive(s) || !s.gameObject.activeInHierarchy) continue;
                string name = GameText.TextStatName(s), value = GameText.TextStatValue(s);
                // Skip empty widgets and placeholders ("None") that the game shows before data is set.
                if (string.IsNullOrWhiteSpace(value) || value.Equals("None", StringComparison.OrdinalIgnoreCase)) continue;
                entries.Add(string.IsNullOrWhiteSpace(name) ? value : name + ": " + value);
            }
            return entries;
        }

        public void ReadAll()
        {
            var hud = GetHud();
            if (hud == null || !hud.IsShowing()) { _speech.Say("The statistics bar is not shown right now."); return; }
            var entries = StatEntries(hud, withModifiers: true);
            if (entries.Count == 0) { _speech.Say("No statistics shown."); return; }
            var parts = new List<string>();
            for (int i = 0; i < entries.Count; i++) parts.Add((i + 1) + ". " + entries[i]);
            _speech.Say(TextUtil.JoinWith(". ", parts) + ".");
        }

        /// <summary>Alt+number: one statistic (1-based), with its modifiers.</summary>
        public void ReadOne(int number)
        {
            var hud = GetHud();
            if (hud == null || !hud.IsShowing()) { _speech.Say("The statistics bar is not shown right now."); return; }
            var entries = StatEntries(hud, withModifiers: true);
            if (number < 1 || number > entries.Count)
            {
                _speech.Say(entries.Count == 0 ? "No statistics shown." : "There are only " + entries.Count + " statistics. F4 lists them.");
                return;
            }
            _speech.Say(entries[number - 1]);
        }

        /// <summary>Alt+0: turn number and the turn's title.</summary>
        public void ReadTurn()
        {
            var gfm = GetGameFlow();
            if (gfm == null || gfm.currentTurnNo <= 0) { _speech.Say("No turn information right now."); return; }
            _speech.Say(TurnText(gfm));
        }

        public void ReadStatus()
        {
            var parts = new List<string>();
            var gfm = GetGameFlow();
            if (gfm != null && gfm.currentTurnNo > 0) parts.Add(TurnText(gfm));

            try
            {
                var gm = Singleton<GameManager>.Instance;
                if (UiUtil.Alive(gm)) parts.Add("State: " + StateName(gm.GetGameState()) + ".");
            }
            catch { }

            try
            {
                var map = Managers.Instance != null ? Managers.Instance.MapManager : null;
                if (UiUtil.Alive(map)) parts.Add("Map: " + map.GetCurrentMapType().ToString() + ".");
            }
            catch { }

            var names = new List<string>();
            foreach (var r in _screen.Regions) names.Add(r.Name);
            parts.Add("Open: " + TextUtil.JoinWith(", ", names) + ".");
            parts.Add("Speech engine: " + _speech.QueryEngine() + ".");
            _speech.Say(TextUtil.JoinWith(" ", parts));
        }

        private static string StateName(GameState state)
        {
            switch (state)
            {
                case GameState.MainMenu: return "main menu";
                case GameState.PrologueEpilogue: return "prologue or epilogue";
                case GameState.FreeLook: return "free roam, map and menus available";
                case GameState.StoryFragment: return "story event in progress";
                case GameState.CharacterCustomization: return "character customization";
                default: return state.ToString();
            }
        }
    }
}
