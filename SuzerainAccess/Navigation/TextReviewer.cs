using System;
using System.Collections.Generic;
using System.Diagnostics;
using SuzerainAccess.Config;
using SuzerainAccess.Core;
using SuzerainAccess.Game;
using SuzerainAccess.Speech;
using SuzerainAccess.UI;
using TMPro;
using UnityEngine;

namespace SuzerainAccess.Navigation
{
    /// <summary>
    /// Reads the text of the current panel line by line, without having to start at the top:
    ///
    ///  * Follows focus: after moving to a control with Tab/arrows, Page Down starts reading at that
    ///    control's text instead of the top of the panel.
    ///  * Headings: Ctrl+Page Down / Ctrl+Page Up jump between titles. A line counts as a heading when its
    ///    TextMeshPro font size is clearly larger than the panel's usual text, or it is short and bold
    ///    (real TMP_Text.fontSize / fontStyle values, no guessing from words).
    ///  * New text: after activating something (opening a codex entry, expanding an article or a journal
    ///    turn...), the lines that appeared are read automatically and the reading position is placed
    ///    there, so Page Down continues with the new information.
    ///  * Ctrl+Home / Ctrl+End go to the first / last line.
    /// </summary>
    internal sealed class TextReviewer
    {
        private sealed class Line
        {
            public string Text;
            public bool Heading;
            public int[] Path; // sibling indices from the region root; null = appended data (charts)
        }

        private const int MaxAutoReadLines = 12;

        private readonly FocusManager _focus;
        private readonly SpeechManager _speech;
        private readonly Stopwatch _clock = Stopwatch.StartNew();

        private List<Line> _lines = new List<Line>();
        private int _index = -1;
        private string _regionId;
        private string _signature;
        private IntPtr _focusAtLastReview;

        // New-text detection after an activation.
        private HashSet<string> _snapshot;
        private string _snapshotRegion;
        private float _watchUntil;
        private float _nextWatch;

        public TextReviewer(FocusManager focus, SpeechManager speech)
        {
            _focus = focus;
            _speech = speech;
        }

        private float Now => (float)_clock.Elapsed.TotalSeconds;

        // ------------------------------------------------------------------ commands

        public void Move(int delta)
        {
            Refresh();
            if (_lines.Count == 0) { _speech.Say("No text on this panel."); return; }
            StartAtFocusIfMoved(delta);

            int next = _index + delta;
            if (next < 0) { _index = 0; _speech.Say("Top. " + Speak(_lines[0])); return; }
            if (next >= _lines.Count) { _index = _lines.Count - 1; _speech.Say("Bottom. " + Speak(_lines[_index])); return; }
            _index = next;
            _speech.Say(Speak(_lines[_index]));
        }

        public void MoveHeading(int delta)
        {
            Refresh();
            if (_lines.Count == 0) { _speech.Say("No text on this panel."); return; }
            StartAtFocusIfMoved(delta);
            for (int i = _index + delta; i >= 0 && i < _lines.Count; i += delta)
            {
                if (!_lines[i].Heading) continue;
                _index = i;
                _speech.Say(Speak(_lines[i]));
                return;
            }
            _speech.Say(delta > 0 ? "No more headings below." : "No more headings above.");
        }

        public void MoveToEdge(bool last)
        {
            Refresh();
            if (_lines.Count == 0) { _speech.Say("No text on this panel."); return; }
            _index = last ? _lines.Count - 1 : 0;
            _focusAtLastReview = CurrentFocusKey();
            _speech.Say(Speak(_lines[_index]));
        }

        public void ReadAll()
        {
            Refresh();
            var region = _focus.CurrentRegion;
            string name = region != null ? region.Name : "Screen";
            if (_lines.Count == 0) { _speech.Say(name + ": no text."); return; }
            var texts = new List<string>();
            foreach (var l in _lines) texts.Add(l.Text);
            _speech.Say(TextUtil.Sentence(name) + " " + TextUtil.JoinWith(". ", texts));
            _index = -1;
        }

        private string Speak(Line line) =>
            line.Heading && ModConfig.AnnounceRoles.Value && ModConfig.AtLeast(Verbosity.Normal) ? line.Text + ", heading" : line.Text;

        /// <summary>
        /// If the user moved the control focus since the last reading command, begin reading at the text
        /// of the focused control (so they do not have to go through everything above it).
        /// </summary>
        private void StartAtFocusIfMoved(int delta)
        {
            IntPtr key = CurrentFocusKey();
            if (key == _focusAtLastReview) return;
            _focusAtLastReview = key;
            // In conversations the newest line matters most: reading starts from the end instead (see Refresh).
            if (_focus.InDialogueRegion) return;
            var current = _focus.Current;
            var region = _focus.CurrentRegion;
            if (current == null || !current.IsAlive || region?.Root == null) return;

            int[] focusPath = PathFrom(region.Root, current.GameObject.transform);
            if (focusPath == null) return;
            int first = _lines.FindIndex(l => l.Path != null && Compare(l.Path, focusPath) >= 0);
            if (first < 0) return;
            // Position so that the next move in this direction lands on the focused control's text.
            _index = delta > 0 ? first - 1 : first;
        }

        private IntPtr CurrentFocusKey()
        {
            var c = _focus.Current;
            return c != null ? c.Key : IntPtr.Zero;
        }

        // ------------------------------------------------------------------ new text after activation

        /// <summary>Call just before a control is activated.</summary>
        public void NoteActivation()
        {
            if (!ModConfig.AutoReadNewText.Value) return;
            // Conversations: the dialogue reader already speaks every new line; reading it here as well
            // made the chosen response be spoken twice.
            if (_focus.InDialogueRegion) { _snapshot = null; return; }
            Refresh();
            _snapshot = new HashSet<string>();
            foreach (var l in _lines) _snapshot.Add(l.Text);
            _snapshotRegion = _regionId;
            _watchUntil = Now + 2.5f;
            _nextWatch = Now + 0.3f;
        }

        public void Tick()
        {
            if (_snapshot == null || Now < _nextWatch) return;
            _nextWatch = Now + 0.25f;
            if (Now > _watchUntil) { _snapshot = null; return; }

            try
            {
                Refresh();
                // A different panel opened: the screen announcement and content readers handle that.
                if (_regionId != _snapshotRegion) { _snapshot = null; return; }

                int firstNew = -1;
                var added = new List<string>();
                for (int i = 0; i < _lines.Count; i++)
                {
                    if (_snapshot.Contains(_lines[i].Text)) continue;
                    if (firstNew < 0) firstNew = i;
                    added.Add(_lines[i].Text);
                }
                if (added.Count == 0) return;

                // Wait one more check in case the text is still being filled in.
                string joined = string.Join("\n", added);
                if (joined != _pendingNew) { _pendingNew = joined; return; }
                _pendingNew = null;
                _snapshot = null;

                if (added.Count <= MaxAutoReadLines)
                {
                    _speech.Say(TextUtil.JoinWith(". ", added), interrupt: false, important: true, automatic: true);
                    _index = firstNew + added.Count - 1;
                }
                else
                {
                    _speech.Say(added.Count + " new lines of text. Page Down reads them.", interrupt: false, automatic: true);
                    _index = firstNew - 1;
                }
                _focusAtLastReview = CurrentFocusKey();
            }
            catch (Exception ex)
            {
                ModLog.Exception("text-review-new", ex);
                _snapshot = null;
            }
        }

        private string _pendingNew;

        // ------------------------------------------------------------------ text collection

        private void Refresh()
        {
            var region = _focus.CurrentRegion;
            string id = region != null ? region.Id : null;
            var lines = Collect(region);

            var sb = new System.Text.StringBuilder();
            foreach (var l in lines) sb.Append(l.Text).Append('\n');
            string signature = sb.ToString();
            if (id == _regionId && signature == _signature) return;

            // Keep the reading position on the same text if it still exists.
            string currentText = _index >= 0 && _index < _lines.Count ? _lines[_index].Text : null;
            bool sameRegion = id == _regionId;
            _regionId = id;
            _signature = signature;
            _lines = lines;
            _index = sameRegion && currentText != null ? lines.FindIndex(l => l.Text == currentText) : -1;
            // Conversation: start after the last line, so Page Up reads the newest line first and further
            // Page Up presses go back through the history.
            if (_focus.InDialogueRegion && (_index < 0 || !sameRegion)) _index = lines.Count;
        }

        private List<Line> Collect(Region region)
        {
            var result = new List<Line>();
            if (region == null) return result;

            if (region.Root == null)
            {
                foreach (var t in _focus.RegionTexts()) result.Add(new Line { Text = t });
                return result;
            }

            var seen = new HashSet<string>();
            var sizes = new List<float>();
            var raw = new List<(Line line, float size, bool bold)>();
            var texts = region.Root.GetComponentsInChildren<TMP_Text>(false);
            for (int i = 0; i < texts.Length; i++)
            {
                var t = texts[i];
                try
                {
                    if (!UiUtil.IsTextVisible(t)) continue;
                    string s = UiUtil.TextOf(t);
                    if (string.IsNullOrEmpty(s)) continue;
                    if (ModConfig.IgnoreDecorativeText.Value && (TextUtil.IsDecorative(s) || !seen.Add(s))) continue;
                    float size = t.fontSize;
                    bool bold = (t.fontStyle & FontStyles.Bold) != 0;
                    sizes.Add(size);
                    raw.Add((new Line { Text = s, Path = PathFrom(region.Root, t.transform) }, size, bold));
                }
                catch { }
            }

            float median = 0f;
            if (sizes.Count > 0)
            {
                sizes.Sort();
                median = sizes[sizes.Count / 2];
            }
            foreach (var (line, size, bold) in raw)
            {
                line.Heading = median > 0f && line.Text.Length <= 100 &&
                               (size >= median * 1.25f || (bold && size >= median && line.Text.Length <= 60));
                result.Add(line);
            }

            // Chart data has no text on screen; append it (see Demographics).
            if (region.Id == "TokenInformationPanel")
            {
                var extra = new List<string>();
                AppendDemographics(extra);
                foreach (var e in extra) result.Add(new Line { Text = e });
            }
            return result;
        }

        private static void AppendDemographics(List<string> texts)
        {
            try
            {
                var panel = ScreenTracker.GetPanels()?.TokenInformationPanel;
                if (!UiUtil.Alive(panel)) return;
                string ethnicity = Demographics.FromChart(panel.ethnicityChart);
                if (string.IsNullOrEmpty(ethnicity)) ethnicity = Demographics.FromTokenData(panel.currentTokenData, ethnicity: true);
                string religion = Demographics.FromChart(panel.religionChart);
                if (string.IsNullOrEmpty(religion)) religion = Demographics.FromTokenData(panel.currentTokenData, ethnicity: false);
                if (!string.IsNullOrEmpty(ethnicity)) texts.Add("Ethnicity: " + ethnicity);
                if (!string.IsNullOrEmpty(religion)) texts.Add("Religion: " + religion);
            }
            catch { }
        }

        /// <summary>Sibling-index path from root to t (depth-first order key), or null if t is not under root.</summary>
        private static int[] PathFrom(Transform root, Transform t)
        {
            var path = new List<int>();
            for (var cur = t; cur != null; cur = cur.parent)
            {
                if (cur.Pointer == root.Pointer) { path.Reverse(); return path.ToArray(); }
                path.Add(cur.GetSiblingIndex());
            }
            return null;
        }

        /// <summary>Depth-first (reading) order comparison of two hierarchy paths.</summary>
        private static int Compare(int[] a, int[] b)
        {
            int n = Math.Min(a.Length, b.Length);
            for (int i = 0; i < n; i++)
                if (a[i] != b[i]) return a[i].CompareTo(b[i]);
            return a.Length.CompareTo(b.Length);
        }
    }
}
