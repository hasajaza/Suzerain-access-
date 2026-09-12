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
    /// Tells the player how to move the story forward. In free roam, sighted players see markers above
    /// map locations that have something waiting, and a Continue / end-turn button once the current
    /// step is done. Both are exposed here from the game's own data (verified members):
    ///
    ///  Panels.TokenIndicatorPanel.GetTokenIndicators() : List&lt;TokenIndicatorPanel.TokenIndicator&gt;
    ///      TokenIndicator.token (Token, with GetTokenData() : MapTokenData)
    ///      TokenIndicator.tokenIndicatorType : StoryFragment (an event/conversation is waiting there)
    ///                                          or TokenUpdate (new information, e.g. a report)
    ///  Panels.ContinueButtonPanel.IsShowing(), .button (Button)
    ///  TokenInteractionPanel.CreateStoryFragmentButtons / CreateReportButtons build the actions that
    ///      appear after selecting such a location.
    /// </summary>
    internal sealed class GuidanceAccessibility
    {
        public enum Marker { None, Event, Update }

        private const float PollInterval = 0.5f;

        private readonly SpeechManager _speech;
        private readonly DialogueAccessibility _dialogue;
        private readonly ScreenTracker _screen;
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private float _nextPoll;

        private static readonly Dictionary<string, Marker> Markers = new Dictionary<string, Marker>();
        /// <summary>Map (Token.GetMapConfiguration().mapType) and title of each marked location.</summary>
        private static readonly Dictionary<string, (MapConfiguration.MapType? map, string title)> MarkerInfo =
            new Dictionary<string, (MapConfiguration.MapType?, string)>();

        /// <summary>Titles of locations with an event waiting that are NOT on the given map.</summary>
        public static List<string> EventsOnOtherMap(MapConfiguration.MapType current)
        {
            var list = new List<string>();
            foreach (var kv in Markers)
            {
                if (kv.Value != Marker.Event) continue;
                if (MarkerInfo.TryGetValue(kv.Key, out var info) && info.map.HasValue && info.map.Value != current) list.Add(info.title);
            }
            return list;
        }
        private HashSet<string> _knownEvents;
        private bool _continueWasAvailable;

        public GuidanceAccessibility(SpeechManager speech, DialogueAccessibility dialogue, ScreenTracker screen)
        {
            _speech = speech;
            _dialogue = dialogue;
            _screen = screen;
        }

        private float Now => (float)_clock.Elapsed.TotalSeconds;

        /// <summary>Marker shown on a map location (by MapTokenData.NameInDatabase), from the last poll.</summary>
        public static Marker MarkerFor(MapTokenData data)
        {
            if (data == null) return Marker.None;
            try { return Markers.TryGetValue(data.NameInDatabase ?? "", out var m) ? m : Marker.None; }
            catch { return Marker.None; }
        }

        public static string MarkerText(Marker m) =>
            m == Marker.Event ? "event waiting" : m == Marker.Update ? "new information" : "";

        public void Tick()
        {
            if (Now < _nextPoll) return;
            _nextPoll = Now + PollInterval;
            try { Poll(announce: true); }
            catch (Exception ex) { ModLog.Exception("guidance", ex); }
        }

        /// <summary>Refreshes the marker table; optionally announces new events and the continue button.</summary>
        public void Poll(bool announce)
        {
            var panels = ScreenTracker.GetPanels();
            Markers.Clear();
            MarkerInfo.Clear();
            var eventTitles = new Dictionary<string, string>();
            if (panels == null) { _knownEvents = null; return; }

            var indicatorPanel = panels.TokenIndicatorPanel;
            if (UiUtil.Alive(indicatorPanel))
            {
                var list = indicatorPanel.GetTokenIndicators();
                for (int i = 0; list != null && i < list.Count; i++)
                {
                    var indicator = list[i];
                    if (indicator == null) continue;
                    var token = indicator.token;
                    if (!UiUtil.Alive(token)) continue;
                    var data = token.GetTokenData();
                    if (data == null) continue;
                    string key = data.NameInDatabase ?? "";
                    var marker = indicator.tokenIndicatorType == TokenIndicatorPanel.TokenIndicatorType.StoryFragment ? Marker.Event : Marker.Update;
                    // An event outranks an update on the same location.
                    if (!Markers.TryGetValue(key, out var existing) || existing == Marker.Update) Markers[key] = marker;
                    MapConfiguration.MapType? mapType = null;
                    try { var cfg = token.GetMapConfiguration(); if (cfg != null) mapType = cfg.mapType; } catch { }
                    MarkerInfo[key] = (mapType, TextUtil.Clean(data.Title));
                    if (marker == Marker.Event) eventTitles[key] = TextUtil.Clean(data.Title);
                }
            }

            bool busy = _dialogue.IsDialogueActive;

            // Announce locations that newly got an event marker (not during dialogue, not on first sight).
            var current = new HashSet<string>(eventTitles.Keys);
            if (announce && _knownEvents != null && !busy && ModConfig.AnnounceNewEvents.Value)
            {
                var added = new List<string>();
                foreach (var key in current) if (!_knownEvents.Contains(key)) added.Add(eventTitles[key]);
                if (added.Count > 0)
                    _speech.Say("Event waiting at " + TextUtil.JoinWith(", ", added) + ". F6 opens the map.", interrupt: false, important: true, automatic: true);
            }
            _knownEvents = current;

            // Announce when the Continue / end-turn button becomes available.
            bool continueAvailable = IsContinueAvailable(panels, out string label);
            if (announce && continueAvailable && !_continueWasAvailable && !busy && ModConfig.AnnounceNewEvents.Value)
                _speech.Say(TextUtil.Sentence(string.IsNullOrEmpty(label) ? "Continue button available" : label + " button available") +
                            " Shift+F6 explains how to reach it.", interrupt: false, important: true, automatic: true);
            _continueWasAvailable = continueAvailable;
        }

        private static bool IsContinueAvailable(Panels panels, out string label)
        {
            label = "";
            try
            {
                var p = panels.ContinueButtonPanel;
                if (!UiUtil.Alive(p) || !p.IsShowing()) return false;
                var b = p.button;
                if (!UiUtil.Alive(b) || !b.IsInteractable() || !UiUtil.IsGameObjectVisible(b.gameObject)) return false;
                label = ElementFactory.OwnText(b);
                return true;
            }
            catch { return false; }
        }

        /// <summary>Shift+F6: what can I do now?</summary>
        public void WhatNext()
        {
            var parts = new List<string>();
            var panels = ScreenTracker.GetPanels();
            if (panels == null) { _speech.Say("The game is loading."); return; }

            Poll(announce: false);
            var top = _screen.Top;

            if (_dialogue.IsDialogueActive)
            {
                parts.Add("A conversation is open. When responses are offered, choose one with the arrows and Enter; otherwise press Space to continue.");
            }
            else if (top != null && (top.Tier == PanelTier.Center || top.Tier == PanelTier.Modal) && top.Id != "MainMenuPanel")
            {
                parts.Add(top.Name + " is open. Finish it, or press Backspace to close it if it can be closed.");
            }

            var events = new List<string>();
            var updates = new List<string>();
            var list = panels.TokenIndicatorPanel != null ? panels.TokenIndicatorPanel.GetTokenIndicators() : null;
            var seen = new HashSet<string>();
            for (int i = 0; list != null && i < list.Count; i++)
            {
                var ind = list[i];
                if (ind == null || !UiUtil.Alive(ind.token)) continue;
                var data = ind.token.GetTokenData();
                if (data == null || !seen.Add((data.NameInDatabase ?? "") + ind.tokenIndicatorType)) continue;
                string where = TextUtil.Clean(data.Title);
                try
                {
                    var cfg = ind.token.GetMapConfiguration();
                    var current = Managers.Instance.MapManager.GetCurrentMapType();
                    if (cfg != null && cfg.mapType != current)
                        where += cfg.mapType == MapConfiguration.MapType.World ? " (on the world map)" : " (on the main map)";
                }
                catch { }
                if (ind.tokenIndicatorType == TokenIndicatorPanel.TokenIndicatorType.StoryFragment) events.Add(where);
                else updates.Add(where);
            }

            if (events.Count > 0)
                parts.Add("Events waiting at: " + TextUtil.JoinWith(", ", events) +
                          ". Press F6: the map browser starts on that location (Control F6 first if it is on the other map). Press Enter, then Tab to the action and press Enter.");
            if (updates.Count > 0)
                parts.Add("New information at: " + TextUtil.JoinWith(", ", updates) + ".");

            if (IsContinueAvailable(panels, out string label))
                parts.Add(TextUtil.Sentence((string.IsNullOrEmpty(label) ? "The Continue" : "The " + label) +
                          " button is available. It moves the story on. Press Control Tab until you hear 'Continue button', then Enter"));
            else if (events.Count == 0 && !_dialogue.IsDialogueActive)
                parts.Add("No event markers and no Continue button right now. Check the side panel and navigation bar with Control Tab, and the latest notification with F5.");

            _speech.Say(TextUtil.JoinWith(" ", parts));
        }
    }
}
