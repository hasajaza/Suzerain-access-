using System;
using SuzerainAccess.Config;
using SuzerainAccess.Core;
using SuzerainAccess.Game;
using SuzerainAccess.Speech;
using SuzerainAccess.UI;
using TMPro;
using UnityEngine.EventSystems;

namespace SuzerainAccess.Features
{
    /// <summary>
    /// Text fields (save names, journal notepad, report search, account forms). The game reports edit
    /// mode through Panels.IsAnyInputFieldInFocus(); while editing, the mod stops using keys that would
    /// interfere with typing, and speaks typed/deleted characters.
    /// </summary>
    internal sealed class InputFieldAccessibility
    {
        private readonly SpeechManager _speech;
        private TMP_InputField _field;
        private string _lastText;
        private bool _wasEditing;

        public bool IsEditing { get; private set; }

        public InputFieldAccessibility(SpeechManager speech)
        {
            _speech = speech;
        }

        public void Tick()
        {
            try
            {
                var panels = ScreenTracker.GetPanels();
                bool editing = false;
                try { editing = panels != null && panels.IsAnyInputFieldInFocus(); } catch { }

                TMP_InputField field = null;
                var es = EventSystem.current;
                var selected = es != null ? es.currentSelectedGameObject : null;
                if (selected != null) field = selected.GetComponent<TMP_InputField>();
                if (field != null && field.isFocused) editing = true;

                IsEditing = editing;
                if (editing && !_wasEditing)
                    _speech.Say(ModConfig.AtLeast(Verbosity.Normal) ? "Editing. Type text, Enter to confirm, Tab to leave." : "Editing.");
                if (!editing && _wasEditing && _field != null)
                    _speech.Say("Done editing. " + (string.IsNullOrEmpty(_lastText) ? "blank" : _lastText));
                _wasEditing = editing;

                if (!editing || field == null)
                {
                    if (!editing) { _field = null; _lastText = null; }
                    return;
                }

                string text = field.text ?? string.Empty;
                if (_field == null || _field.Pointer != field.Pointer)
                {
                    _field = field;
                    _lastText = text;
                    return;
                }
                if (text == _lastText) return;

                if (ModConfig.EchoTyping.Value)
                {
                    string old = _lastText ?? string.Empty;
                    if (text.Length == old.Length + 1 && text.StartsWith(old, StringComparison.Ordinal))
                        _speech.Say(Speakable(text[text.Length - 1]));
                    else if (text.Length == old.Length - 1 && old.StartsWith(text, StringComparison.Ordinal))
                        _speech.Say("deleted " + Speakable(old[old.Length - 1]));
                    else
                        _speech.Say(string.IsNullOrEmpty(text) ? "blank" : text);
                }
                _lastText = text;
            }
            catch (Exception ex)
            {
                ModLog.Exception("input-field", ex);
            }
        }

        private static string Speakable(char c)
        {
            if (c == ' ') return "space";
            return c.ToString();
        }
    }
}
