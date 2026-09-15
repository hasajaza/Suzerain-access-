using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx;
using SuzerainAccess.Config;
using SuzerainAccess.Features;
using SuzerainAccess.Game;
using SuzerainAccess.Input;
using SuzerainAccess.Menu;
using SuzerainAccess.Navigation;
using SuzerainAccess.Speech;
using SuzerainAccess.UI;
using UnityEngine;
using UnityEngine.EventSystems;

namespace SuzerainAccess.Core
{
    /// <summary>
    /// Owns every subsystem and runs them once per frame from <see cref="AccessibilityRunner"/>.
    /// Each subsystem runs inside its own try/catch: an unexpected UI state is logged (throttled) and
    /// the rest of the mod keeps working. Nothing here can throw into the game.
    /// </summary>
    internal sealed class AccessibilityManager
    {
        public static AccessibilityManager Instance { get; private set; }

        private readonly SpeechManager _speech;
        private readonly KeyboardManager _keys;
        private readonly ScreenTracker _screen;
        private readonly FocusManager _focus;
        private readonly TextReviewer _reviewer;
        private readonly DialogueAccessibility _dialogue;
        private readonly ContentAccessibility _content;
        private readonly StatisticsAccessibility _stats;
        private readonly NotificationAccessibility _notifications;
        private readonly MapAccessibility _map;
        private readonly GuidanceAccessibility _guidance;
        private readonly InputFieldAccessibility _inputField;
        private readonly SettingsMenu _settings;

        private bool _started;
        private bool _versionReported;

        private AccessibilityManager(string pluginDirectory)
        {
            _speech = new SpeechManager();
            _speech.Initialize(new[] { pluginDirectory, Paths.GameRootPath });

            _keys = new KeyboardManager();
            _keys.Initialize(ModConfig.File);

            _screen = new ScreenTracker();
            _focus = new FocusManager(_speech, _screen);
            _reviewer = new TextReviewer(_focus, _speech);
            _dialogue = new DialogueAccessibility(_speech, _focus);
            _content = new ContentAccessibility(_speech, _focus, _screen, _dialogue);
            _stats = new StatisticsAccessibility(_speech, _screen);
            _notifications = new NotificationAccessibility(_speech);
            _map = new MapAccessibility(_speech);
            _guidance = new GuidanceAccessibility(_speech, _dialogue, _screen);
            _inputField = new InputFieldAccessibility(_speech);
            _settings = new SettingsMenu(_speech, _keys);
        }

        public static void Create(string pluginDirectory)
        {
            Instance = new AccessibilityManager(pluginDirectory);
        }

        /// <summary>Called every frame by the injected MonoBehaviour.</summary>
        private bool _suspended;

        public void Tick()
        {
            Run("keyboard", _keys.Update);

            // Ctrl+F9: turn the whole mod off / on. While off, the mod does nothing at all except wait
            // for this key: no speech, no reading of the game, no game selection changes, no JAWS connection.
            if (_keys.Triggered(Command.ToggleMod)) { Run("toggle", ToggleSuspended); return; }
            if (_suspended) return;

            if (!_started) Run("startup", Startup);
            Run("version", ReportVersion);
            Run("screen", _screen.Tick);
            Run("input-field", _inputField.Tick);
            Run("commands", HandleCommands);
            Run("focus", _focus.Tick);
            Run("dialogue", _dialogue.Tick);
            Run("content", _content.Tick);
            Run("statistics", _stats.Tick);
            Run("notifications", _notifications.Tick);
            Run("guidance", _guidance.Tick);
            Run("map", _map.Tick);
            Run("focus-late", _focus.LateTick);
            Run("text-review", _reviewer.Tick);
            Run("engine-watch", WatchEngine);
            Run("jaws-watch", WatchJaws);
        }

        private void ToggleSuspended()
        {
            if (!_suspended)
            {
                _map.Close(null);
                if (_settings.IsOpen) _settings.Close();
                _speech.Say("Suzerain Access off. Control F9 turns it back on.");
                _speech.Suspend();
                _suspended = true;
                ModLog.Info("Mod turned OFF by the player: no speech, no key handling, JAWS connection released.");
            }
            else
            {
                _suspended = false;
                _speech.Resume();
                _screen.ForcePoll();
                ModLog.Info("Mod turned ON by the player.");
                _speech.Say("Suzerain Access on.");
                _focus.RepeatFocused();
            }
        }

        private static void Run(string name, Action action)
        {
            try { action(); }
            catch (Exception ex) { ModLog.Exception(name, ex); }
        }

        private readonly System.Diagnostics.Stopwatch _engineClock = System.Diagnostics.Stopwatch.StartNew();
        private double _nextEngineCheck = 2;

        /// <summary>Logs whenever Universal Speech switches engine (e.g. NVDA started after the game).</summary>
        private double _nextJawsCheck = 1;
        private bool _wasFocused = true;
        private double _lostFocusAt;
        private double _keyPauseUntil;

        /// <summary>Once per second: JAWS start/stop/restart. Immediately: game window focus regained.</summary>
        private void WatchJaws()
        {
            bool focused = true;
            try { focused = Application.isFocused; } catch { }
            double now = _engineClock.Elapsed.TotalSeconds;
            if (!focused && _wasFocused)
            {
                _lostFocusAt = now;
                ModLog.Debug("Game window lost focus. Modifier keys as seen by the game: " + _keys.DescribeModifiers());
                _focus.CancelPending();
                _dialogue.CancelPending();
            }
            if (focused && !_wasFocused)
            {
                ModLog.Debug("Game window regained focus after " + (now - _lostFocusAt).ToString("0.0") + " s. Modifier keys as seen by the game: " +
                            _keys.DescribeModifiers() + ". Keyboard hooks installed by this mod: none.");
                int pause = ModConfig.DiagnosticKeyPauseAfterFocusSeconds.Value;
                if (pause > 0)
                {
                    _keys.Paused = true;
                    _keyPauseUntil = now + pause;
                    ModLog.Info("Diagnostic: the mod ignores all keys for " + pause + " seconds.");
                }
            }
            if (_keys.Paused && now >= _keyPauseUntil)
            {
                _keys.Paused = false;
                ModLog.Info("Diagnostic: the mod reads keys again.");
            }
            // Ignore focus flickers (seen in the log: two reconnects within a moment).
            bool regained = focused && !_wasFocused && now - _lostFocusAt >= 1.0;
            _wasFocused = focused;
            if (!regained && _engineClock.Elapsed.TotalSeconds < _nextJawsCheck) return;
            _nextJawsCheck = _engineClock.Elapsed.TotalSeconds + 1;
            _speech.WatchJaws(forceReconnect: regained && ModConfig.JawsReconnectOnFocus.Value);
        }

        private void WatchEngine()
        {
            if (_engineClock.Elapsed.TotalSeconds < _nextEngineCheck) return;
            _nextEngineCheck = _engineClock.Elapsed.TotalSeconds + 10;
            _speech.QueryEngine();
        }

                private void Startup()
        {
            _started = true;
            string gameVersion = "unknown", unityVersion = "unknown";
            try { gameVersion = Application.version; } catch { }
            try { unityVersion = Application.unityVersion; } catch { }
            ModLog.Info($"Suzerain version (Application.version): {gameVersion}; Unity {unityVersion}.");

            try
            {
                var es = EventSystem.current;
                string module = es != null && es.currentInputModule != null ? es.currentInputModule.GetIl2CppType().FullName : "none";
                ModLog.Info($"EventSystem present: {es != null}; input module: {module}; sendNavigationEvents: {(es != null && es.sendNavigationEvents)}.");
            }
            catch (Exception ex) { ModLog.Exception("startup-eventsystem", ex); }

            ModLog.Info("Speech engine: " + _speech.QueryEngine() + ".");
            ModLog.Info("Keyboard: read by polling Unity's Input System each frame. No keyboard hook, no raw input registration, no focus changes are made by this mod.");
            ModLog.Info("Accessibility system initialized.");
            _speech.Say("Suzerain Access loaded. Press F1 for help, F9 for settings.", interrupt: false);
        }

        private void ReportVersion()
        {
            if (_versionReported) return;
            var status = VersionCheck.Status;
            if (status == VersionCheck.Result.Pending) return;
            _versionReported = true;
            switch (status)
            {
                case VersionCheck.Result.Match:
                    ModLog.Info("Game build verified: files match the build this mod was made for. " + VersionCheck.Details);
                    break;
                case VersionCheck.Result.Mismatch:
                    ModLog.Warn("WRONG GAME VERSION: this installation differs from the build SuzerainAccess was verified against. " +
                                "Some features may not work. " + VersionCheck.Details);
                    _speech.Say("Warning: your Suzerain version differs from the version this accessibility mod was made for. Some features may not work.",
                        interrupt: false, important: true);
                    break;
                default:
                    ModLog.Warn("Could not verify the game build: " + VersionCheck.Details);
                    break;
            }
        }

        // ------------------------------------------------------------------ commands

        private void HandleCommands()
        {
            if (!_keys.AnyTriggered && !_settings.IsOpen) return;

            if (_settings.IsOpen)
            {
                if (_keys.Triggered(Command.StopSpeech)) _speech.Stop();
                _settings.HandleInput();
                return;
            }

            // Always available.
            if (_keys.Triggered(Command.StopSpeech)) { _speech.Stop(); return; }
            if (_keys.Triggered(Command.ReconnectSpeech)) { _speech.Reconnect(); return; }
            if (_keys.Triggered(Command.NextSpeechEngine)) { _speech.CycleEngine(); return; }
            if (_keys.Triggered(Command.SettingsMenu)) { _map.Close(null); _settings.Open(); return; }
            if (_keys.Triggered(Command.Help)) { Help(); return; }
            if (_keys.Triggered(Command.RepeatLastSpeech)) { _speech.RepeatLast(); return; }
            if (_keys.Triggered(Command.ReadStatistics)) { _stats.ReadAll(); return; }
            if (_keys.Triggered(Command.ReadStatus)) { _stats.ReadStatus(); return; }
            if (_keys.Triggered(Command.TurnInfo)) { _stats.ReadTurn(); return; }
            for (int n = 1; n <= 9; n++)
                if (_keys.Triggered(Command.Stat1 + (n - 1))) { _stats.ReadOne(n); return; }
            if (_keys.Triggered(Command.RepeatDialogue)) { _dialogue.RepeatLine(); return; }
            if (_keys.Triggered(Command.ReadResponses)) { _dialogue.ReadResponses(); return; }
            if (_keys.Triggered(Command.ReadLatestNotification)) { _notifications.ReadLatest(); return; }
            if (_keys.Triggered(Command.ReadNotificationHistory)) { _notifications.ReadHistory(); return; }
            if (_keys.Triggered(Command.OpenLatestNotification)) { _notifications.OpenLatest(); return; }
            if (_keys.Triggered(Command.WhatNext)) { _guidance.WhatNext(); return; }
            if (_keys.Triggered(Command.SwitchMap)) { _map.SwitchMap(); return; }

            if (_keys.Triggered(Command.ToggleMap))
            {
                if (!_map.IsActive)
                {
                    // Nothing in the game UI should receive Enter while the map browser is open.
                    try { EventSystem.current?.SetSelectedGameObject(null); } catch { }
                }
                _map.Toggle();
                return;
            }

            if (_map.IsActive)
            {
                HandleMapCommands();
                return;
            }

            if (_inputField.IsEditing)
            {
                // While typing, only Tab (leave the field) and F-keys are used by the mod.
                if (_keys.Triggered(Command.NextElement)) _focus.Move(1);
                else if (_keys.Triggered(Command.PreviousElement)) _focus.Move(-1);
                else if (_keys.Triggered(Command.ReadScreen)) _reviewer.ReadAll();
                else if (_keys.Triggered(Command.DescribeFocused)) _focus.DescribeFocused();
                return;
            }

            if (_keys.Triggered(Command.NextElement)) _focus.Move(1);
            else if (_keys.Triggered(Command.PreviousElement)) _focus.Move(-1);
            else if (_keys.Triggered(Command.NextElementArrow)) _focus.QueueArrowMove(1);
            else if (_keys.Triggered(Command.PreviousElementArrow)) _focus.QueueArrowMove(-1);
            else if (_keys.Triggered(Command.FirstElement)) _focus.MoveToEdge(false);
            else if (_keys.Triggered(Command.LastElement)) _focus.MoveToEdge(true);
            else if (_keys.Triggered(Command.Activate)) ActivateOrContinue(alternate: false);
            else if (_keys.Triggered(Command.ActivateAlternate)) ActivateOrContinue(alternate: true);
            else if (_keys.Triggered(Command.ContinueDialogue) && _focus.InDialogueRegion) _dialogue.RequestContinue();
            else if (_keys.Triggered(Command.IncreaseValue)) _focus.Adjust(1);
            else if (_keys.Triggered(Command.DecreaseValue)) _focus.Adjust(-1);
            else if (_keys.Triggered(Command.Back)) _focus.Back();
            else if (_keys.Triggered(Command.PanelMain))
                _focus.JumpToRegion(r => r.Tier == PanelTier.Modal || r.Tier == PanelTier.Center || r.Tier == PanelTier.MainMenu, "No main content is open.");
            else if (_keys.Triggered(Command.PanelSide))
                _focus.JumpToRegion(r => r.Tier == PanelTier.Side, "No side panel is open.");
            else if (_keys.Triggered(Command.PanelNavigation))
                _focus.JumpToRegion(r => r.Id == "NavigationPanel" || r.Id == "WarPlayerActionsPanel", "The navigation bar is not available here.");
            else if (_keys.Triggered(Command.PanelStatistics))
                _focus.JumpToRegion(r => r.Id == "HUDPanel" || r.Id == "WarHUDPanel", "The statistics bar is not available here.");
            else if (_keys.Triggered(Command.PanelContinue))
                _focus.JumpToRegion(r => r.Id == "ContinueButtonPanel" || r.Id == "ContinueCenterPanel" || r.Id == "WarTurnPanel",
                    "The Continue button is not available right now.");
            else if (_keys.Triggered(Command.PanelExtra))
                _focus.JumpToRegion(r => r.Id == "TokenProgressPanel" || r.Id == "BottomLeftPanel" || r.Id == "TurnCostPanel" || r.Id == "SocialsButtonPanel",
                    "No extra panel is open.");
            else if (_keys.Triggered(Command.NextRegion)) _focus.CycleRegion(1);
            else if (_keys.Triggered(Command.PreviousRegion)) _focus.CycleRegion(-1);
            else if (_keys.Triggered(Command.NextTextLine)) _reviewer.Move(1);
            else if (_keys.Triggered(Command.PreviousTextLine)) _reviewer.Move(-1);
            else if (_keys.Triggered(Command.NextHeading)) _reviewer.MoveHeading(1);
            else if (_keys.Triggered(Command.PreviousHeading)) _reviewer.MoveHeading(-1);
            else if (_keys.Triggered(Command.FirstTextLine)) _reviewer.MoveToEdge(false);
            else if (_keys.Triggered(Command.LastTextLine)) _reviewer.MoveToEdge(true);
            else if (_keys.Triggered(Command.ReadScreen)) _reviewer.ReadAll();
            else if (_keys.Triggered(Command.ReadAllRegions)) ReadRegions();
            else if (_keys.Triggered(Command.ReadDocument)) _content.ReadDocument();
            else if (_keys.Triggered(Command.DescribeFocused)) _focus.DescribeFocused();
        }

        /// <summary>
        /// Enter in a conversation: a response is selected; any other control is pressed only if you moved to
        /// it yourself since the last line appeared. Otherwise Enter continues the dialogue, the way a
        /// sighted player clicks to continue. Ctrl+Enter always presses the focused control.
        /// </summary>
        private void ActivateOrContinue(bool alternate)
        {
            if (_focus.InDialogueRegion && !alternate)
            {
                var current = _focus.Current;
                bool onResponse = current != null && current.Role == UI.ElementRole.Choice;
                // A participant's portrait: Enter opens their Codex entry. Space still continues the
                // dialogue, and Enter continues everywhere else in the conversation.
                bool portrait = current != null && current.Role == UI.ElementRole.Character;
                // The Continue button goes through the dialogue continue path, which presses the button and,
                // if the line does not advance, calls the game's own continue method.
                bool continueButton = current != null && current.IsConversationContinue;
                bool chosenByUser = !continueButton && (portrait || (current != null && _focus.LastExplicitFocusTime > _dialogue.LastDialogueEventTime));
                ModLog.Debug($"Enter in conversation: item='{(current != null ? current.Role.ToString() : "none")}', " +
                             $"response={onResponse}, continueButton={continueButton}, chosenByUser={chosenByUser}");
                if (!onResponse && !chosenByUser)
                {
                    _dialogue.RequestContinue();
                    return;
                }
            }
            ModLog.Debug("Activate key: " + (alternate ? "Ctrl+Enter" : "Enter") + " on " +
                         (_focus.Current != null ? _focus.Current.Role + " in " + (_focus.CurrentRegion != null ? _focus.CurrentRegion.Name : "?") : "nothing"));
            _reviewer.NoteActivation();
            _focus.Activate(fromSharedEnterKey: !alternate);
        }

        private void HandleMapCommands()
        {
            if (_keys.Triggered(Command.NextElement) || _keys.Triggered(Command.NextElementArrow)) _map.Move(1);
            else if (_keys.Triggered(Command.PreviousElement) || _keys.Triggered(Command.PreviousElementArrow)) _map.Move(-1);
            else if (_keys.Triggered(Command.FirstElement)) _map.MoveToEdge(false);
            else if (_keys.Triggered(Command.LastElement)) _map.MoveToEdge(true);
            else if (_keys.Triggered(Command.Activate) || _keys.Triggered(Command.ActivateAlternate)) _map.SelectCurrent();
            else if (_keys.Triggered(Command.NextRegion)) _map.JumpToWaiting(1);
            else if (_keys.Triggered(Command.PreviousRegion)) _map.JumpToWaiting(-1);
            else if (_keys.Triggered(Command.NextTextLine)) _map.CycleFilter(1);
            else if (_keys.Triggered(Command.PreviousTextLine)) _map.CycleFilter(-1);
            else if (_keys.Triggered(Command.ReadDocument) || _keys.Triggered(Command.DescribeFocused)) _map.ReadDetails();
            else if (_keys.Triggered(Command.Back)) _map.Close("Map browser closed.");
        }

        private void ReadRegions()
        {
            var names = new List<string>();
            foreach (var r in _screen.Regions) names.Add(r.Name);
            string current = _focus.CurrentRegion != null ? _focus.CurrentRegion.Name : "none";
            _speech.Say("Open panels: " + TextUtil.JoinWith(", ", names) + ". Current: " + current + ". Control Tab switches panels.");
        }

        private void Help()
        {
            var sb = new StringBuilder("Suzerain Access keys. ");
            foreach (var c in _keys.AllCommands)
                sb.Append(_keys.Binding(c)).Append(": ").Append(KeyboardManager.Description(c)).Append(". ");
            sb.Append("The game's own keys still work: Space continues dialogue, Left and Right arrows switch reports, Escape opens the pause menu.");
            _speech.Say(sb.ToString());
        }
    }
}
