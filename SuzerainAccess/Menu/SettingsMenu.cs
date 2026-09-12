using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using SuzerainAccess.Config;
using SuzerainAccess.Core;
using SuzerainAccess.Input;
using SuzerainAccess.Speech;
using UnityEngine.EventSystems;

namespace SuzerainAccess.Menu
{
    /// <summary>
    /// The accessibility settings menu (F9). It is a virtual, speech-only menu: Tab / Shift+Tab move,
    /// Left / Right change a value, Enter toggles or starts key rebinding, F9 or Backspace closes.
    /// Every change is written immediately to the BepInEx config file and persists between sessions.
    /// </summary>
    internal sealed class SettingsMenu
    {
        private sealed class Item
        {
            public Func<string> Text;
            public Action<int> Change;   // Left/Right (and Enter = +1 unless OnEnter is set)
            public Action OnEnter;
        }

        private readonly SpeechManager _speech;
        private readonly KeyboardManager _keys;
        private readonly List<Item> _items = new List<Item>();
        private int _index;
        private Command? _capturing;
        private int _captureArmFrames;

        public bool IsOpen { get; private set; }

        public SettingsMenu(SpeechManager speech, KeyboardManager keys)
        {
            _speech = speech;
            _keys = keys;
            Build();
        }

        private void Build()
        {
            _items.Add(new Item
            {
                Text = () => "Verbosity: " + ModConfig.Verbosity.Value,
                Change = d => ModConfig.Verbosity.Value = (Verbosity)Wrap((int)ModConfig.Verbosity.Value + d, 3)
            });
            _items.Add(new Item
            {
                Text = () =>
                {
                    int v = ModConfig.SpeechRatePercent.Value;
                    string value = v < 0 ? "unchanged" : v + " percent";
                    return "Speech rate: " + value + (_speech.IsRateSupported() ? "" : ", not supported by the current speech engine; your screen reader's own rate applies");
                },
                Change = d =>
                {
                    int v = ModConfig.SpeechRatePercent.Value;
                    v = v < 0 ? (d > 0 ? 50 : -1) : v + d * 10;
                    if (v > 100) v = 100;
                    if (v < 0) v = -1;
                    ModConfig.SpeechRatePercent.Value = v;
                    _speech.ApplyRate();
                }
            });
            _items.Add(new Item
            {
                Text = () => "Enter key activation: " + (ModConfig.EnterActivation.Value == EnterMode.Auto ? "automatic, avoids double activation" : "always handled by the mod"),
                Change = d => ModConfig.EnterActivation.Value = ModConfig.EnterActivation.Value == EnterMode.Auto ? EnterMode.AlwaysMod : EnterMode.Auto
            });

            _items.Add(new Item
            {
                Text = () => "JAWS connection: " + ModConfig.JawsRoute.Value + ". Takes effect with Control F11 or on restart",
                Change = d => ModConfig.JawsRoute.Value = (JawsRouteChoice)Wrap((int)ModConfig.JawsRoute.Value + d, 5)
            });
            AddBool("Read dialogue automatically", ModConfig.AutoReadDialogue);
            AddBool("Read the last line when a conversation reopens", ModConfig.ReadDialogueHistoryOnReturn);
            AddBool("Announce screens when they open", ModConfig.AutoReadScreens);
            AddBool("Read reports, decisions and pop-ups automatically", ModConfig.AutoReadContent);
            AddBool("Announce control types", ModConfig.AnnounceRoles);
            AddBool("Announce list positions", ModConfig.AnnouncePositions);
            AddBool("Detailed numbers", ModConfig.DetailedNumbers);
            AddBool("Ignore decorative text", ModConfig.IgnoreDecorativeText);
            AddBool("Announce statistic changes", ModConfig.AnnounceStatChanges);
            AddBool("Announce notifications", ModConfig.AnnounceNotifications);
            AddBool("Announce turn changes", ModConfig.AnnounceTurnChanges);
            AddBool("Announce new events and the Continue button", ModConfig.AnnounceNewEvents);
            AddBool("Read new text after activating something", ModConfig.AutoReadNewText);
            AddBool("Describe colour coding", ModConfig.DescribeColors);
            AddBool("Include disabled controls", ModConfig.IncludeDisabledControls);
            AddBool("Filter duplicate announcements", ModConfig.FilterDuplicates);
            AddBool("Braille output", ModConfig.BrailleOutput);
            AddBool("Braille output with JAWS (can cut off JAWS speech)", ModConfig.JawsBraille);
            AddBool("Reconnect to JAWS when returning to the game", ModConfig.JawsReconnectOnFocus);
            AddBool("Use Windows SAPI when no screen reader runs", ModConfig.UseSapiFallback, () => _speech.ApplyNativeSpeechSetting());
            AddBool("Move the game's selection with the focus", ModConfig.SyncGameSelection);
            AddBool("Echo typed characters", ModConfig.EchoTyping);
            AddBool("Highlight map locations while browsing", ModConfig.MapHoverTokens);
            AddBool("Debug logging", ModConfig.DebugLogging, () => ModLog.DebugEnabled = ModConfig.DebugLogging.Value);

            foreach (var command in _keys.AllCommands)
            {
                var c = command;
                _items.Add(new Item
                {
                    Text = () => "Key: " + KeyboardManager.Description(c) + ": " + _keys.Binding(c),
                    OnEnter = () => StartCapture(c),
                    Change = d => { _keys.ResetToDefault(c); _speech.Say("Reset to default: " + _keys.Binding(c)); }
                });
            }

            _items.Add(new Item { Text = () => "Close settings", OnEnter = () => Close() });
        }

        private void AddBool(string name, ConfigEntry<bool> entry, Action after = null)
        {
            _items.Add(new Item
            {
                Text = () => name + ": " + (entry.Value ? "on" : "off"),
                Change = d => { entry.Value = !entry.Value; after?.Invoke(); }
            });
        }

        private static int Wrap(int v, int count) => ((v % count) + count) % count;

        public void Open()
        {
            IsOpen = true;
            _capturing = null;
            _index = 0;
            // Clear the game's UI selection so Enter inside this menu cannot also press a game button.
            try { EventSystem.current?.SetSelectedGameObject(null); } catch { }
            _speech.Say("Accessibility settings. " + _items.Count + " items. Up and Down arrows or Tab to move, Left and Right to change, Enter to toggle or to change a key, F9 to close.");
            _speech.Say(_items[_index].Text(), interrupt: false);
        }

        public void Close()
        {
            IsOpen = false;
            _capturing = null;
            try { ModConfig.File.Save(); } catch (Exception ex) { ModLog.Exception("config-save", ex); }
            _speech.Say("Settings closed.");
        }

        /// <summary>Handles input while the menu is open. Called instead of the normal navigation.</summary>
        public void HandleInput()
        {
            if (_capturing.HasValue)
            {
                // Ignore the Enter press that started the capture.
                if (_captureArmFrames > 0) { _captureArmFrames--; return; }
                if (!_keys.TryCaptureChord(out var chord)) return;
                var command = _capturing.Value;
                _capturing = null;
                if (chord.Key == UnityEngine.InputSystem.Key.Backspace && !chord.Ctrl && !chord.Shift && !chord.Alt)
                {
                    _speech.Say("Cancelled.");
                    return;
                }
                var existing = _keys.CommandFor(chord);
                if (existing.HasValue && existing.Value != command)
                {
                    _speech.Say(chord + " is already used for " + KeyboardManager.Description(existing.Value) + ". Not changed.");
                    return;
                }
                _keys.Rebind(command, chord);
                _speech.Say(KeyboardManager.Description(command) + ": " + chord);
                return;
            }

            if (_keys.Triggered(Command.SettingsMenu) || _keys.Triggered(Command.Back)) { Close(); return; }
            if (_keys.Triggered(Command.NextElement) || _keys.Triggered(Command.NextElementArrow)) Move(1);
            else if (_keys.Triggered(Command.PreviousElement) || _keys.Triggered(Command.PreviousElementArrow)) Move(-1);
            else if (_keys.Triggered(Command.FirstElement)) { _index = 0; Speak(); }
            else if (_keys.Triggered(Command.LastElement)) { _index = _items.Count - 1; Speak(); }
            else if (_keys.Triggered(Command.IncreaseValue)) Change(1);
            else if (_keys.Triggered(Command.DecreaseValue)) Change(-1);
            else if (_keys.Triggered(Command.Activate) || _keys.Triggered(Command.ActivateAlternate))
            {
                var item = _items[_index];
                if (item.OnEnter != null) item.OnEnter();
                else Change(1);
            }
        }

        private void StartCapture(Command c)
        {
            _capturing = c;
            _captureArmFrames = 1;
            _speech.Say("Press the new key or key combination for " + KeyboardManager.Description(c) + ". Backspace cancels.");
        }

        private void Move(int d)
        {
            _index = Wrap(_index + d, _items.Count);
            Speak();
        }

        private void Change(int d)
        {
            var item = _items[_index];
            if (item.Change == null) { Speak(); return; }
            try { item.Change(d); }
            catch (Exception ex) { ModLog.Exception("settings-change", ex); }
            Speak();
        }

        private void Speak() => _speech.Say(_items[_index].Text() + ", " + (_index + 1) + " of " + _items.Count);
    }
}
