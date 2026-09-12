using System;
using System.Collections.Generic;
using System.Diagnostics;
using SuzerainAccess.Config;
using SuzerainAccess.Core;
using SuzerainAccess.Game;
using SuzerainAccess.Navigation;
using SuzerainAccess.Speech;
using SuzerainAccess.UI;
using UnityEngine;
using Il2CppGeneric = Il2CppSystem.Collections.Generic;

namespace SuzerainAccess.Features
{
    /// <summary>
    /// Speaks dialogue and exposes responses. Suzerain runs its dialogue through the Pixel Crushers
    /// Dialogue System; the game's own handlers (verified in Assembly-CSharp) keep what is on screen:
    ///
    ///  ConversationHandler (Panels.ConversationPanel.conversationHandler)
    ///    instantiatedConversationTexts : List&lt;TemplateConversationText&gt;
    ///        TemplateConversationText.conversationText (TMP), .unformattedText, .speakerName
    ///    instantiatedResponses : List&lt;TemplateConversationResponse&gt; (.text, .responseButton)
    ///    responsesAreShown, continueButton
    ///  NarrationConversationHandler (child of NarrationPanel): titleText, subtitleText, narrationText, instantiatedResponses, responsesAreShown
    ///  PrologueEpilogueConversationHandler (child of PrologueEpiloguePanel): yearText, titleText, subtitleText, prologueEpilogueText, instantiatedResponses, responsesAreShown
    ///
    /// The mod reads these lists every frame instead of hooking methods: nothing is patched, so a game
    /// update cannot break dialogue flow, and lines that arrive while another is being spoken are queued.
    /// Responses are real UI buttons; they are activated through the game's own Button (see ElementFactory),
    /// and the game's own Up/Down response navigation keeps working.
    /// </summary>
    internal sealed class DialogueAccessibility
    {
        private sealed class PendingLine
        {
            public string Text;
            public float Since;
        }

        private sealed class TextBlockWatcher
        {
            private string _announced = "";
            private string _pending = "";
            private float _pendingSince;

            public void Reset() { _announced = ""; _pending = ""; }

            /// <summary>True while a changed text is waiting to become stable before being read.</summary>
            public bool HasPending => _pending.Length > 0;

            /// <summary>Returns text to announce once the text has been stable for a moment.</summary>
            public string Poll(string current, float now)
            {
                if (string.IsNullOrEmpty(current) || current == _announced) { _pending = ""; return null; }
                if (current != _pending) { _pending = current; _pendingSince = now; return null; }
                if (now - _pendingSince < 0.25f) return null;
                _announced = current;
                _pending = "";
                return current;
            }
        }

        private readonly SpeechManager _speech;
        private readonly FocusManager _focus;
        private readonly Stopwatch _clock = Stopwatch.StartNew();

        private readonly HashSet<string> _announcedLines = new HashSet<string>();
        private readonly Dictionary<string, PendingLine> _pendingLines = new Dictionary<string, PendingLine>();
        private bool _conversationVisible;
        private string _lastLine;
        private string _lastLineHintKey;
        private bool _continueHintGiven;

        private string _responseSignature = "";
        private readonly List<TemplateConversationResponse> _responses = new List<TemplateConversationResponse>();

        private NarrationConversationHandler _narration;
        private readonly TextBlockWatcher _narrationText = new TextBlockWatcher();
        private bool _narrationVisible;

        private PrologueEpilogueConversationHandler _prologue;
        private readonly TextBlockWatcher _prologueText = new TextBlockWatcher();
        private bool _prologueVisible;

        public DialogueAccessibility(SpeechManager speech, FocusManager focus)
        {
            _speech = speech;
            _focus = focus;
        }

        private float Now => (float)_clock.Elapsed.TotalSeconds;

        public bool IsDialogueActive => _conversationVisible || _narrationVisible || _prologueVisible;

        public void Tick()
        {
            var panels = ScreenTracker.GetPanels();
            if (panels == null) return;

            try { TickConversation(panels); }
            catch (Exception ex) { ModLog.Exception("dialogue-conversation", ex); }

            try { TickNarration(panels); }
            catch (Exception ex) { ModLog.Exception("dialogue-narration", ex); }

            try { TickPrologue(panels); }
            catch (Exception ex) { ModLog.Exception("dialogue-prologue", ex); }

            ProcessContinue();
        }

        // ------------------------------------------------------------------ conversation

        private void TickConversation(Panels panels)
        {
            var panel = panels.ConversationPanel;
            bool visible = UiUtil.Alive(panel) && panel.IsShowing();
            if (!visible)
            {
                if (_conversationVisible)
                {
                    _conversationVisible = false;
                    _announcedLines.Clear();
                    _pendingLines.Clear();
                    _responseSignature = "";
                }
                return;
            }

            var handler = panel.conversationHandler;
            if (!UiUtil.Alive(handler)) return;

            bool justOpened = !_conversationVisible;
            _conversationVisible = true;

            var texts = handler.instantiatedConversationTexts;
            int count = texts != null ? texts.Count : 0;
            for (int i = 0; i < count; i++)
            {
                var line = texts[i];
                if (!UiUtil.Alive(line)) continue;

                string unformatted = TextUtil.Clean(GameText.SafeText(() => line.unformattedText));
                string key = line.Pointer.ToInt64() + "|" + unformatted;
                if (_announcedLines.Contains(key)) continue;

                if (justOpened && (i < count - 1 || !ModConfig.ReadDialogueHistoryOnReturn.Value))
                {
                    // Conversation was already running (e.g. save loaded or panel re-shown): skip old lines.
                    _announcedLines.Add(key);
                    continue;
                }

                string display = UiUtil.TextOf(line.conversationText);
                if (!_pendingLines.TryGetValue(key, out var pending))
                {
                    pending = new PendingLine { Text = display, Since = Now };
                    _pendingLines[key] = pending;
                }
                else if (pending.Text != display)
                {
                    pending.Text = display;
                }

                float age = Now - pending.Since;
                // The typewriter may still be writing: wait until the visible text contains the whole line.
                bool complete = !string.IsNullOrEmpty(display) &&
                                (string.IsNullOrEmpty(unformatted) ? age > 0.5f : ContainsEnd(display, unformatted));
                if (!complete && age < 2.5f) continue;

                string spoken = complete ? display : SpeakerLine(line, unformatted);
                _announcedLines.Add(key);
                _pendingLines.Remove(key);
                if (string.IsNullOrEmpty(spoken)) continue;

                _lastLine = spoken;
                LastDialogueEventTime = ModClock.Now;
                _continueHintGiven = false;
                _lastLineHintKey = key;
                if (ModConfig.AutoReadDialogue.Value) _speech.Say(spoken, interrupt: false, important: true, automatic: true);
            }

            TickResponses(handler.responsesAreShown, handler.instantiatedResponses, _pendingLines.Count > 0);

            // Continue prompt (High verbosity): the game's Space/Enter "ContinueDialogue" keys advance the line.
            if (!handler.responsesAreShown && !_continueHintGiven && _lastLineHintKey != null && ModConfig.AtLeast(Verbosity.High))
            {
                var button = handler.continueButton;
                if (UiUtil.Alive(button) && button.IsInteractable() && UiUtil.IsGameObjectVisible(button.gameObject))
                {
                    _continueHintGiven = true;
                    _speech.Queue("Space to continue.");
                }
            }
        }

        private static bool ContainsEnd(string display, string unformatted)
        {
            string tail = unformatted.Length > 12 ? unformatted.Substring(unformatted.Length - 12) : unformatted;
            return display.IndexOf(tail, StringComparison.Ordinal) >= 0;
        }

        private static string SpeakerLine(TemplateConversationText line, string unformatted)
        {
            string speaker = TextUtil.Clean(GameText.SafeText(() => line.speakerName));
            if (string.IsNullOrEmpty(unformatted)) return UiUtil.TextOf(line.conversationText);
            return string.IsNullOrEmpty(speaker) ? unformatted : speaker + ": " + unformatted;
        }

        // ------------------------------------------------------------------ responses (all handlers)

        /// <summary>Time a new set of responses was first seen; used to read the story text before the options.</summary>
        private string _candidateSignature = "";
        private float _candidateSince;

        private void TickResponses(bool shown, Il2CppGeneric.List<TemplateConversationResponse> list, bool textPending)
        {
            int count = list != null ? list.Count : 0;
            if (!shown || count == 0)
            {
                if (_responseSignature.Length > 0) { _responseSignature = ""; _responses.Clear(); }
                return;
            }

            var visible = new List<TemplateConversationResponse>();
            var sig = new System.Text.StringBuilder();
            for (int i = 0; i < count; i++)
            {
                var r = list[i];
                if (!UiUtil.Alive(r) || !UiUtil.IsGameObjectVisible(r.gameObject)) continue;
                visible.Add(r);
                sig.Append(r.Pointer.ToInt64()).Append(':').Append(UiUtil.TextOf(r.text)).Append('|');
            }
            if (visible.Count == 0) return; // still fading in
            string signature = sig.ToString();
            if (signature == _responseSignature) return;

            // Read the text of the page/line before its options: wait while the text is still being
            // written or has not been read yet, and give text that changes together with the options a
            // moment to arrive. Never wait longer than 3 seconds.
            if (signature != _candidateSignature) { _candidateSignature = signature; _candidateSince = Now; }
            float waited = Now - _candidateSince;
            if (waited < 3f && (textPending || waited < 0.35f)) return;

            _responseSignature = signature;
            LastDialogueEventTime = ModClock.Now;
            _responses.Clear();
            _responses.AddRange(visible);

            // Put accessibility focus (and the game's selection) on the first response.
            _focus.SuppressAutoFocusAnnouncement(1.0f);
            var firstButton = visible[0].responseButton;
            _focus.FocusGameObject(UiUtil.Alive(firstButton) ? firstButton.gameObject : visible[0].gameObject, announce: false);

            if (!ModConfig.AutoReadDialogue.Value) return;
            string summary = visible.Count == 1 ? "1 response." : visible.Count + " responses.";
            _speech.Say(summary + " " + ResponseText(visible[0], 0, visible.Count), interrupt: false, important: true, automatic: true);
            if (ModConfig.AtLeast(Verbosity.High))
                _speech.Queue("Tab or the Up and Down arrows move between responses, Enter selects. Shift+F3 lists them all.");
        }

        private static string ResponseText(TemplateConversationResponse r, int index, int total)
        {
            string text = UiUtil.TextOf(r.text);
            bool available = true;
            try { available = r.responseButton == null || r.responseButton.IsInteractable(); } catch { }
            string prefix = ModConfig.AnnouncePositions.Value ? "Response " + (index + 1) + " of " + total + ": " : "";
            return prefix + text + (available ? "" : ", unavailable");
        }

        // ------------------------------------------------------------------ narration / prologue

        private void TickNarration(Panels panels)
        {
            var panel = panels.NarrationPanel;
            bool visible = UiUtil.Alive(panel) && panel.IsShowing();
            if (!visible)
            {
                if (_narrationVisible) { _narrationVisible = false; _narrationText.Reset(); _responseSignature = ""; }
                return;
            }
            _narrationVisible = true;
            if (!UiUtil.Alive(_narration)) _narration = panel.GetComponentInChildren<NarrationConversationHandler>(true);
            if (!UiUtil.Alive(_narration)) { ModLog.WarnOnce("no-narration-handler", "NarrationPanel is showing but no NarrationConversationHandler was found under it."); return; }

            string text = TextUtil.JoinWith(". ", new[]
            {
                UiUtil.VisibleTextOf(_narration.titleText),
                UiUtil.VisibleTextOf(_narration.subtitleText),
                UiUtil.VisibleTextOf(_narration.narrationText)
            });
            string announce = _narrationText.Poll(text, Now);
            if (announce != null)
            {
                _lastLine = announce;
                LastDialogueEventTime = ModClock.Now;
                if (ModConfig.AutoReadDialogue.Value) _speech.Say(announce, interrupt: false, important: true, automatic: true);
            }
            TickResponses(_narration.responsesAreShown, _narration.instantiatedResponses, _narrationText.HasPending);
        }

        private void TickPrologue(Panels panels)
        {
            var panel = panels.PrologueEpiloguePanel;
            bool visible = UiUtil.Alive(panel) && panel.IsShowing();
            if (!visible)
            {
                if (_prologueVisible) { _prologueVisible = false; _prologueText.Reset(); _responseSignature = ""; }
                return;
            }
            _prologueVisible = true;
            if (!UiUtil.Alive(_prologue)) _prologue = panel.GetComponentInChildren<PrologueEpilogueConversationHandler>(true);
            if (!UiUtil.Alive(_prologue)) { ModLog.WarnOnce("no-prologue-handler", "PrologueEpiloguePanel is showing but no PrologueEpilogueConversationHandler was found under it."); return; }

            string text = TextUtil.JoinWith(". ", new[]
            {
                UiUtil.VisibleTextOf(_prologue.yearText),
                UiUtil.VisibleTextOf(_prologue.titleText),
                UiUtil.VisibleTextOf(_prologue.subtitleText),
                UiUtil.VisibleTextOf(_prologue.prologueEpilogueText)
            });
            string announce = _prologueText.Poll(text, Now);
            if (announce != null)
            {
                _lastLine = announce;
                LastDialogueEventTime = ModClock.Now;
                if (ModConfig.AutoReadDialogue.Value) _speech.Say(announce, interrupt: false, important: true, automatic: true);
            }
            TickResponses(_prologue.responsesAreShown, _prologue.instantiatedResponses, _prologueText.HasPending);
        }

        // ------------------------------------------------------------------ commands

        public void RepeatLine()
        {
            _speech.Say(_lastLine ?? "No dialogue yet.");
        }

        public void ReadResponses()
        {
            var live = _responses.FindAll(r => UiUtil.Alive(r) && UiUtil.IsGameObjectVisible(r.gameObject));
            if (live.Count == 0) { _speech.Say("No responses available."); return; }
            var parts = new List<string>();
            for (int i = 0; i < live.Count; i++) parts.Add(ResponseText(live[i], i, live.Count));
            _speech.Say(TextUtil.JoinWith(". ", parts));
        }

        /// <summary>Participants of the current conversation (portrait tooltips).</summary>
        public string Participants()
        {
            var panels = ScreenTracker.GetPanels();
            if (panels == null) return "";
            var panel = panels.ConversationPanel;
            if (!UiUtil.Alive(panel) || !panel.IsShowing()) return "";
            var list = panel.instantiatedConversants;
            var names = new List<string>();
            for (int i = 0; list != null && i < list.Count; i++)
            {
                var c = list[i];
                if (!UiUtil.Alive(c)) continue;
                names.Add(TextUtil.Join(UiUtil.TextOf(c.tooltipTitle), UiUtil.TextOf(c.tooltipSubitle)));
            }
            return names.Count == 0 ? "" : "Participants: " + TextUtil.JoinWith("; ", names) + ".";
        }

        public string LastLine => _lastLine;

        /// <summary>ModClock.Now of the last dialogue line or response list that was presented.</summary>
        public float LastDialogueEventTime { get; private set; }

        // ------------------------------------------------------------------ continue with Enter

        private string _continueSnapshot;
        private int _continueFrames;

        /// <summary>
        /// Enter was pressed in a dialogue panel (not on a response). The game may continue by itself
        /// (its ContinueDialogue action); if after a few frames nothing changed, the mod continues through
        /// the game's own continue path (ConversationHandler.continueButton / OnContinue(),
        /// NarrationConversationHandler.OnContinue(), PrologueEpilogueConversationHandler.OnContinue()).
        /// </summary>
        /// <summary>Drops a queued "continue" (called when the game window loses focus).</summary>
        public void CancelPending() => _continueFrames = 0;

        public void RequestContinue()
        {
            _continueSnapshot = DialogueSnapshot();
            _continueFrames = 4;
        }

        private void ProcessContinue()
        {
            if (_continueFrames <= 0 || --_continueFrames > 0) return;
            if (DialogueSnapshot() != _continueSnapshot)
            {
                ModLog.Debug("Dialogue advanced by the game itself.");
                return;
            }
            var panels = ScreenTracker.GetPanels();
            if (panels == null) return;
            try
            {
                var conv = panels.ConversationPanel;
                if (UiUtil.Alive(conv) && conv.IsShowing())
                {
                    var h = conv.conversationHandler;
                    if (!UiUtil.Alive(h)) return;
                    if (h.responsesAreShown) { _speech.Say("Choose a response first. Shift F3 lists them."); return; }
                    var button = h.continueButton;
                    if (UiUtil.Alive(button) && button.IsInteractable() && button.gameObject.activeInHierarchy)
                    {
                        button.OnSubmit(new UnityEngine.EventSystems.BaseEventData(UnityEngine.EventSystems.EventSystem.current));
                        ModLog.Debug("Continued conversation via continueButton.");
                    }
                    else
                    {
                        h.OnContinue();
                        ModLog.Debug("Continued conversation via ConversationHandler.OnContinue().");
                    }
                    return;
                }
                var narration = panels.NarrationPanel;
                if (UiUtil.Alive(narration) && narration.IsShowing() && UiUtil.Alive(_narration))
                {
                    if (_narration.responsesAreShown) { _speech.Say("Choose a response first. Shift F3 lists them."); return; }
                    _narration.OnContinue();
                    ModLog.Debug("Continued narration via OnContinue().");
                    return;
                }
                var prologue = panels.PrologueEpiloguePanel;
                if (UiUtil.Alive(prologue) && prologue.IsShowing() && UiUtil.Alive(_prologue))
                {
                    if (_prologue.responsesAreShown) { _speech.Say("Choose a response first. Shift F3 lists them."); return; }
                    _prologue.OnContinue();
                    ModLog.Debug("Continued prologue/epilogue via OnContinue().");
                }
            }
            catch (Exception ex)
            {
                ModLog.Exception("dialogue-continue", ex);
            }
        }

        private string DialogueSnapshot()
        {
            var sb = new System.Text.StringBuilder();
            try
            {
                var panels = ScreenTracker.GetPanels();
                if (panels == null) return "";
                var conv = panels.ConversationPanel;
                if (UiUtil.Alive(conv) && conv.IsShowing() && UiUtil.Alive(conv.conversationHandler))
                {
                    var h = conv.conversationHandler;
                    var texts = h.instantiatedConversationTexts;
                    int n = texts != null ? texts.Count : 0;
                    sb.Append("c").Append(n).Append(h.responsesAreShown ? "R" : "-");
                    if (n > 0 && UiUtil.Alive(texts[n - 1])) sb.Append(texts[n - 1].Pointer.ToInt64()).Append(GameText.SafeText(() => texts[n - 1].unformattedText));
                }
                if (UiUtil.Alive(_narration)) sb.Append("|n").Append(UiUtil.TextOf(_narration.narrationText)).Append(_narration.responsesAreShown ? "R" : "-");
                if (UiUtil.Alive(_prologue)) sb.Append("|p").Append(UiUtil.TextOf(_prologue.prologueEpilogueText)).Append(_prologue.responsesAreShown ? "R" : "-");
            }
            catch { }
            return sb.ToString();
        }
    }
}
