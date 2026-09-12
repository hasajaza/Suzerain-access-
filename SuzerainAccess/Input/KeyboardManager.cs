using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using SuzerainAccess.Core;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace SuzerainAccess.Input
{
    public enum Command
    {
        NextElement, PreviousElement, NextElementArrow, PreviousElementArrow, FirstElement, LastElement,
        Activate, ActivateAlternate, ContinueDialogue, IncreaseValue, DecreaseValue, Back,
        NextRegion, PreviousRegion, NextTextLine, PreviousTextLine, NextHeading, PreviousHeading, FirstTextLine, LastTextLine,
        Help, ReadScreen, ReadAllRegions, RepeatDialogue, ReadResponses,
        ReadStatistics, ReadStatus, ReadLatestNotification, ReadNotificationHistory, OpenLatestNotification,
        ToggleMap, SwitchMap, WhatNext, ReadDocument,
        Stat1, Stat2, Stat3, Stat4, Stat5, Stat6, Stat7, Stat8, Stat9, TurnInfo,
        PanelMain, PanelSide, PanelNavigation, PanelStatistics, PanelContinue, RepeatLastSpeech, DescribeFocused, SettingsMenu, StopSpeech, ReconnectSpeech, NextSpeechEngine, ToggleMod
    }

    internal readonly struct KeyChord : IEquatable<KeyChord>
    {
        public readonly Key Key;
        public readonly bool Ctrl, Shift, Alt;

        public KeyChord(Key key, bool ctrl, bool shift, bool alt) { Key = key; Ctrl = ctrl; Shift = shift; Alt = alt; }

        public bool IsValid => Key != Key.None;

        public override string ToString()
        {
            if (!IsValid) return "Unassigned";
            var parts = new List<string>();
            if (Ctrl) parts.Add("Ctrl");
            if (Shift) parts.Add("Shift");
            if (Alt) parts.Add("Alt");
            parts.Add(Key.ToString());
            return string.Join("+", parts);
        }

        /// <summary>Parses "Ctrl+Shift+Tab" style text. Key names are UnityEngine.InputSystem.Key names.</summary>
        public static bool TryParse(string text, out KeyChord chord)
        {
            chord = default;
            if (string.IsNullOrWhiteSpace(text)) return false;
            bool ctrl = false, shift = false, alt = false;
            Key key = Key.None;
            foreach (var raw in text.Split('+'))
            {
                string p = raw.Trim();
                if (p.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) || p.Equals("Control", StringComparison.OrdinalIgnoreCase)) ctrl = true;
                else if (p.Equals("Shift", StringComparison.OrdinalIgnoreCase)) shift = true;
                else if (p.Equals("Alt", StringComparison.OrdinalIgnoreCase)) alt = true;
                else if (!Enum.TryParse(p, true, out key)) return false;
            }
            if (key == Key.None) return false;
            chord = new KeyChord(key, ctrl, shift, alt);
            return true;
        }

        public bool Equals(KeyChord o) => Key == o.Key && Ctrl == o.Ctrl && Shift == o.Shift && Alt == o.Alt;
        public override bool Equals(object obj) => obj is KeyChord k && Equals(k);
        public override int GetHashCode() => ((int)Key * 8) + (Ctrl ? 1 : 0) + (Shift ? 2 : 0) + (Alt ? 4 : 0);
    }

    /// <summary>
    /// Reads the keyboard through the Unity Input System (the game uses the Input System package;
    /// UnityEngine.InputSystem.Keyboard.current is verified in the supplied Unity.InputSystem interop).
    ///
    /// Default keys deliberately avoid every keyboard binding of the game's own "Suzerain Controls"
    /// input asset (extracted from global-metadata.dat): Enter (Submit/ContinueDialogue), Space
    /// (ContinueDialogue), Escape (EscapeMenu), Up/Down (ResponsesNavigate), Left/Right and A/D
    /// (PanelContentCycle), W/A/S/D (map move), Q/E (language switch).
    /// Enter is shared on purpose - see FocusManager for how double activation is prevented.
    /// Up/Down are shared on purpose too: the game only uses them to move between dialogue responses,
    /// so an arrow press is given to the game first and the mod only moves focus if the game did not
    /// move its selection (see FocusManager.QueueArrowMove).
    /// Left/Right are only consumed when the focused control is adjustable (slider, selector...).
    /// IMPORTANT - how keys are read: the mod installs NO keyboard hook, registers NO raw input, never
    /// changes window focus and never enables/disables the game's input. It only polls Unity's Input
    /// System state once per frame (Keyboard.current[key].wasPressedThisFrame), which is read-only:
    /// keys always still reach the game and any screen reader. Because the state is read fresh every
    /// frame, there is no internal key state that could get stuck when focus is lost.
    /// Only layout-independent keys are used (Tab, Home, End, Page keys, F-keys, arrows), so AZERTY and
    /// other layouts behave the same.
    /// </summary>
    internal sealed class KeyboardManager
    {
        private static readonly Dictionary<Command, (string key, string description)> Defaults = new Dictionary<Command, (string, string)>
        {
            { Command.NextElement, ("Tab", "Move to the next control") },
            { Command.PreviousElement, ("Shift+Tab", "Move to the previous control") },
            { Command.NextElementArrow, ("DownArrow", "Move to the next control (arrow key; yields to the game's own response navigation)") },
            { Command.PreviousElementArrow, ("UpArrow", "Move to the previous control (arrow key; yields to the game's own response navigation)") },
            { Command.FirstElement, ("Home", "Move to the first control") },
            { Command.LastElement, ("End", "Move to the last control") },
            { Command.Activate, ("Enter", "Activate the focused control") },
            { Command.ActivateAlternate, ("NumpadEnter", "Activate the focused control (always handled by the mod)") },
            { Command.ContinueDialogue, ("Space", "Continue the dialogue (only in conversations; if the game does not continue by itself, the mod does)") },
            { Command.IncreaseValue, ("RightArrow", "Increase or change the value of a slider, selector or combo box") },
            { Command.DecreaseValue, ("LeftArrow", "Decrease or change the value of a slider, selector or combo box") },
            { Command.Back, ("Backspace", "Go back or close the current panel") },
            { Command.NextRegion, ("Ctrl+Tab", "Move to the next panel on screen") },
            { Command.PreviousRegion, ("Ctrl+Shift+Tab", "Move to the previous panel on screen") },
            { Command.NextTextLine, ("PageDown", "Read the next line of text on the current panel") },
            { Command.PreviousTextLine, ("PageUp", "Read the previous line of text on the current panel") },
            { Command.NextHeading, ("Ctrl+PageDown", "Jump to the next heading in the panel text") },
            { Command.PreviousHeading, ("Ctrl+PageUp", "Jump to the previous heading in the panel text") },
            { Command.FirstTextLine, ("Ctrl+Home", "Go to the first line of the panel text") },
            { Command.LastTextLine, ("Ctrl+End", "Go to the last line of the panel text") },
            { Command.Help, ("F1", "Keyboard help") },
            { Command.ReadScreen, ("F2", "Read all text of the current panel") },
            { Command.ReadAllRegions, ("Shift+F2", "List every open panel") },
            { Command.RepeatDialogue, ("F3", "Repeat the current dialogue line") },
            { Command.ReadResponses, ("Shift+F3", "Read all available responses") },
            { Command.ReadStatistics, ("F4", "Read the statistics bar (numbered, so you learn which Alt+number reads which)") },
            { Command.Stat1, ("Alt+Digit1", "Read statistic 1 of the statistics bar") },
            { Command.Stat2, ("Alt+Digit2", "Read statistic 2 of the statistics bar") },
            { Command.Stat3, ("Alt+Digit3", "Read statistic 3 of the statistics bar") },
            { Command.Stat4, ("Alt+Digit4", "Read statistic 4 of the statistics bar") },
            { Command.Stat5, ("Alt+Digit5", "Read statistic 5 of the statistics bar") },
            { Command.Stat6, ("Alt+Digit6", "Read statistic 6 of the statistics bar") },
            { Command.Stat7, ("Alt+Digit7", "Read statistic 7 of the statistics bar") },
            { Command.Stat8, ("Alt+Digit8", "Read statistic 8 of the statistics bar") },
            { Command.Stat9, ("Alt+Digit9", "Read statistic 9 of the statistics bar") },
            { Command.TurnInfo, ("Alt+Digit0", "Read the current turn and its title") },
            { Command.PanelMain, ("Ctrl+Digit1", "Go to the main content: conversation, decision, report, newspaper, pop-up, menu") },
            { Command.PanelSide, ("Ctrl+Digit2", "Go to the open side panel: journal, codex, overview, location, details") },
            { Command.PanelNavigation, ("Ctrl+Digit3", "Go to the navigation bar") },
            { Command.PanelStatistics, ("Ctrl+Digit4", "Go to the statistics bar") },
            { Command.PanelContinue, ("Ctrl+Digit5", "Go to the Continue button panel") },
            { Command.ReadStatus, ("Shift+F4", "Read turn, map and game state") },
            { Command.ReadLatestNotification, ("F5", "Read the latest notification") },
            { Command.ReadNotificationHistory, ("Shift+F5", "Read recent notifications") },
            { Command.OpenLatestNotification, ("Ctrl+F5", "Open the latest notification") },
            { Command.ToggleMap, ("F6", "Open or close the map location browser") },
            { Command.SwitchMap, ("Ctrl+F6", "Switch between the main map and the world map") },
            { Command.WhatNext, ("Shift+F6", "What can I do now: waiting events, new information and the Continue button") },
            { Command.ReadDocument, ("F7", "Read the current report, decision, article or location details") },
            { Command.RepeatLastSpeech, ("F8", "Repeat the last announcement") },
            { Command.DescribeFocused, ("Shift+F8", "Describe the focused control in detail, including tooltips") },
            { Command.SettingsMenu, ("F9", "Open or close the accessibility settings") },
            { Command.StopSpeech, ("F11", "Stop speech") },
            { Command.ToggleMod, ("Ctrl+F9", "Turn Suzerain Access off or on") },
            { Command.NextSpeechEngine, ("Shift+F11", "Switch speech engine: automatic, then each running screen reader or voice") },
            { Command.ReconnectSpeech, ("Ctrl+F11", "Reconnect speech and return to automatic engine choice (use if the screen reader went silent)") },
        };

        private readonly Dictionary<Command, ConfigEntry<string>> _entries = new Dictionary<Command, ConfigEntry<string>>();
        private readonly Dictionary<Command, KeyChord> _chords = new Dictionary<Command, KeyChord>();
        private readonly HashSet<Command> _triggered = new HashSet<Command>();
        private Key[] _allKeys;

        public bool Ctrl { get; private set; }
        public bool Shift { get; private set; }
        public bool Alt { get; private set; }
        /// <summary>Insert or Caps Lock held: the JAWS / NVDA key. Mod commands are ignored meanwhile.</summary>
        public bool ScreenReaderKey { get; private set; }

        /// <summary>When set, the mod reads no commands at all (diagnostic setting after focus regain).</summary>
        public bool Paused { get; set; }

        /// <summary>Snapshot of the modifier keys as Unity's Input System reports them (for the log).</summary>
        public string DescribeModifiers()
        {
            var kb = Keyboard.current;
            if (kb == null) return "no keyboard device";
            return $"Ctrl={IsDown(kb, Key.LeftCtrl) || IsDown(kb, Key.RightCtrl)}, Shift={IsDown(kb, Key.LeftShift) || IsDown(kb, Key.RightShift)}, " +
                   $"Alt={IsDown(kb, Key.LeftAlt) || IsDown(kb, Key.RightAlt)}, Insert={IsDown(kb, Key.Insert)}, CapsLock={IsDown(kb, Key.CapsLock)}";
        }

        public void Initialize(ConfigFile config)
        {
            foreach (var kv in Defaults)
            {
                var entry = config.Bind("Keys", kv.Key.ToString(), kv.Value.key,
                    kv.Value.description + ". Format: optional Ctrl+/Shift+/Alt+ followed by a UnityEngine.InputSystem.Key name, e.g. Ctrl+F2.");
                _entries[kv.Key] = entry;
                LoadChord(kv.Key);
            }
            _allKeys = Enum.GetValues(typeof(Key)).Cast<Key>()
                .Where(k => k != Key.None && k != Key.IMESelected &&
                            k != Key.LeftCtrl && k != Key.RightCtrl && k != Key.LeftShift && k != Key.RightShift &&
                            k != Key.LeftAlt && k != Key.RightAlt && k != Key.AltGr &&
                            k != Key.LeftMeta && k != Key.RightMeta)
                .Distinct().ToArray();
        }

        private void LoadChord(Command command)
        {
            string text = _entries[command].Value;
            if (!KeyChord.TryParse(text, out var chord))
            {
                ModLog.Warn($"Invalid key binding '{text}' for {command}; using default '{Defaults[command].key}'.");
                KeyChord.TryParse(Defaults[command].key, out chord);
            }
            _chords[command] = chord;
        }

        public static string Description(Command c) => Defaults[c].description;

        public KeyChord Binding(Command c) => _chords.TryGetValue(c, out var k) ? k : default;

        public IEnumerable<Command> AllCommands => Defaults.Keys;

        /// <summary>Returns the command that already uses this chord, if any.</summary>
        public Command? CommandFor(KeyChord chord)
        {
            foreach (var kv in _chords) if (kv.Value.Equals(chord)) return kv.Key;
            return null;
        }

        public void Rebind(Command command, KeyChord chord)
        {
            _entries[command].Value = chord.ToString();
            LoadChord(command);
            ModLog.Info($"Key for {command} set to {chord}.");
        }

        public void ResetToDefault(Command command)
        {
            _entries[command].Value = Defaults[command].key;
            LoadChord(command);
        }

        /// <summary>Samples the keyboard once per frame.</summary>
        public void Update()
        {
            _triggered.Clear();
            var kb = Keyboard.current;
            if (kb == null) return;

            Ctrl = IsDown(kb, Key.LeftCtrl) || IsDown(kb, Key.RightCtrl);
            Shift = IsDown(kb, Key.LeftShift) || IsDown(kb, Key.RightShift);
            Alt = IsDown(kb, Key.LeftAlt) || IsDown(kb, Key.RightAlt);
            ScreenReaderKey = IsDown(kb, Key.Insert) || IsDown(kb, Key.CapsLock);

            // Screen reader shortcuts (JAWS key / NVDA key + something) belong to the screen reader.
            // The mod only READS keys and cannot block them; it simply does not act on them.
            if (ScreenReaderKey) return;
            if (Paused)
            {
                // Even during the diagnostic pause, the on/off key keeps working.
                var toggle = _chords[Command.ToggleMod];
                if (toggle.IsValid && toggle.Ctrl == Ctrl && toggle.Shift == Shift && toggle.Alt == Alt && WasPressed(kb, toggle.Key))
                    _triggered.Add(Command.ToggleMod);
                return;
            }

            foreach (var kv in _chords)
            {
                var c = kv.Value;
                if (!c.IsValid) continue;
                if (c.Ctrl != Ctrl || c.Shift != Shift || c.Alt != Alt) continue;
                if (WasPressed(kb, c.Key)) _triggered.Add(kv.Key);
            }
        }

        public bool Triggered(Command c) => _triggered.Contains(c);

        public bool AnyTriggered => _triggered.Count > 0;

        /// <summary>For key rebinding: the first non-modifier key pressed this frame, with modifiers.</summary>
        public bool TryCaptureChord(out KeyChord chord)
        {
            chord = default;
            var kb = Keyboard.current;
            if (kb == null) return false;
            foreach (var key in _allKeys)
            {
                if (WasPressed(kb, key))
                {
                    chord = new KeyChord(key, Ctrl, Shift, Alt);
                    return true;
                }
            }
            return false;
        }

        private static bool WasPressed(Keyboard kb, Key key)
        {
            try
            {
                KeyControl control = kb[key];
                return control != null && control.wasPressedThisFrame;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsDown(Keyboard kb, Key key)
        {
            try
            {
                KeyControl control = kb[key];
                return control != null && control.isPressed;
            }
            catch
            {
                return false;
            }
        }
    }
}
