using System;
using System.Collections.Generic;
using System.Diagnostics;
using SuzerainAccess.Config;
using SuzerainAccess.Core;
using SuzerainAccess.Game;
using SuzerainAccess.Speech;
using SuzerainAccess.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SuzerainAccess.Navigation
{
    /// <summary>
    /// Knows which region (panel) is active, which real controls it contains, and which one has
    /// accessibility focus. It rebuilds the control list whenever the game changes the UI and keeps
    /// focus on the same control when possible.
    ///
    /// Focus and the game's own EventSystem selection are kept in sync in both directions:
    ///  * moving focus with Tab selects the control in the game (so its tooltip/hover logic runs);
    ///  * when the game itself moves the selection (e.g. its Up/Down response navigation), the newly
    ///    selected control is announced.
    /// </summary>
    internal sealed class FocusManager
    {
        private const float RebuildInterval = 0.35f;

        private readonly SpeechManager _speech;
        private readonly ScreenTracker _screen;
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly Dictionary<IntPtr, AccessibleElement> _cache = new Dictionary<IntPtr, AccessibleElement>();

        private List<AccessibleElement> _elements = new List<AccessibleElement>();
        private int _index = -1;
        private Region _region;
        private string _elementSignature = "";
        private string _subPage;
        private string _pendingSubPage;
        private float _nextRebuild;

        private IntPtr _lastSyncedSelection;
        private IntPtr _lastSeenSelection;

        private AccessibleElement _pendingAfterActivation;
        private int _pendingFrames;
        private float _suppressAutoFocusAnnouncementUntil;

        public FocusManager(SpeechManager speech, ScreenTracker screen)
        {
            _speech = speech;
            _screen = screen;
            _screen.TopChanged += OnTopChanged;
            _screen.RegionsChanged += OnRegionsChanged;
        }

        private float Now => (float)_clock.Elapsed.TotalSeconds;

        public Region CurrentRegion => _region;
        public IReadOnlyList<AccessibleElement> Elements => _elements;
        public AccessibleElement Current => _index >= 0 && _index < _elements.Count ? _elements[_index] : null;

        /// <summary>Features that announce content themselves (dialogue) can silence the automatic
        /// focus announcement that follows a screen change.</summary>
        public void SuppressAutoFocusAnnouncement(float seconds) => _suppressAutoFocusAnnouncementUntil = Now + seconds;

        // ------------------------------------------------------------------ screen changes

        private void OnTopChanged(Region top, Region previous)
        {
            SwitchTo(top, announce: ModConfig.AutoReadScreens.Value, userInitiated: false);
        }

        private void OnRegionsChanged()
        {
            if (_region == null) return;
            foreach (var r in _screen.Regions)
            {
                if (r.Id == _region.Id) { _region = r; return; }
            }
            // Our region disappeared (panel closed): follow the new top region.
            if (_screen.Top != null) SwitchTo(_screen.Top, announce: ModConfig.AutoReadScreens.Value, userInitiated: false);
        }

        /// <summary>Panel the focus came from, so closing a panel can return to it.</summary>
        private string _previousRegionId;

        private void SwitchTo(Region region, bool announce, bool userInitiated)
        {
            if (_region != null && !_region.IsFallback && _region.Id != region.Id) _previousRegionId = _region.Id;
            _region = region;
            _cache.Clear();
            _elementSignature = "";
            _subPage = null;
            _index = -1;
            Rebuild(initial: true);

            if (!announce) return;

            string header = region.Name;
            if (_subPage != null) header += ", " + _subPage;
            if (ModConfig.AtLeast(Verbosity.Normal) && _elements.Count > 0 && !region.IsFallback)
                header += ", " + _elements.Count + (_elements.Count == 1 ? " control" : " controls");

            // Dialogue panels: DialogueAccessibility reads the line and the responses itself.
            bool dialogueRegion = IsDialogueRegion(region);
            bool quietFocus = dialogueRegion || Now < _suppressAutoFocusAnnouncementUntil;
            _speech.Say(TextUtil.Sentence(header), interrupt: userInitiated || !quietFocus, automatic: !userInitiated);
            if (!quietFocus && Current != null) AnnounceElement(Current, automatic: !userInitiated, interrupt: false);
        }

        // ------------------------------------------------------------------ per frame

        public void Tick()
        {
            if (_region == null && _screen.Top != null) SwitchTo(_screen.Top, ModConfig.AutoReadScreens.Value, false);

            if (Now >= _nextRebuild) Rebuild(initial: false);
            WatchGameSelection();
            ProcessQueuedArrow();
            ProcessQueuedSlider();
            _selectionAtFrameEnd = CurrentGameSelection();
            _sliderValueAtFrameEnd = Current != null && Current.Role == ElementRole.Slider ? SliderValue(Current) : float.NaN;

            if (_pendingAfterActivation != null && --_pendingFrames <= 0)
            {
                var e = _pendingAfterActivation;
                _pendingAfterActivation = null;
                Rebuild(initial: false);
                // The element list is rebuilt, so compare by identity: e is the same logical control even
                // when its wrapper object was replaced (this made changed values go unspoken).
                // Choosing a category or a newspaper: go straight to its first entry, so the content of the
                // chosen tab follows the choice instead of being at the other end of the list.
                if (e.Role == ElementRole.Tab)
                {
                    // A top-level category (Economy, Law...) usually has a second row of tabs inside it
                    // (Policies / Situations), each with its own entries. Go to that row first, so the
                    // choice between them is not skipped; a second-level tab goes to the entries.
                    int target = -1, count = 0;
                    string what = null;
                    if (!e.IsSubTab)
                    {
                        var subTabs = _elements.FindAll(x => x.Role == ElementRole.Tab && x.IsSubTab);
                        if (subTabs.Count > 0)
                        {
                            target = _elements.FindIndex(x => x.Role == ElementRole.Tab && x.IsSubTab);
                            count = subTabs.Count;
                            what = count == 1 ? " tab" : " tabs";
                        }
                    }
                    if (target < 0)
                    {
                        target = _elements.FindIndex(IsTabContent);
                        count = _elements.FindAll(IsTabContent).Count;
                        what = count == 1 ? " item" : " items";
                    }
                    if (target >= 0)
                    {
                        _index = target;
                        SyncSelection(Current);
                        _speech.Say(TextUtil.Sentence(Safe(e.Label) + ", " + count + what) + " " + Describe(Current));
                        return;
                    }
                }

                bool stillThere = e.IsAlive && _elements.Exists(x => x.Identity == e.Identity);
                if (stillThere && HasToggleLikeState(e))
                {
                    // Include the name, so a toggle never answers with a bare "selected".
                    string spoken = TextUtil.Join(Safe(e.Label), Safe(e.Value), Safe(e.State));
                    _speech.Say(string.IsNullOrEmpty(spoken) ? "done" : spoken);
                }
            }
        }

        /// <summary>Items that belong to a tab's content (what choosing a tab should take you to).</summary>
        private static bool IsTabContent(AccessibleElement e) =>
            e.Role == ElementRole.Article || e.Role == ElementRole.Report || e.Role == ElementRole.RadioButton ||
            e.Role == ElementRole.ExpandableGroup || e.Role == ElementRole.DecisionOption || e.Role == ElementRole.Character;

        private static bool HasToggleLikeState(AccessibleElement e) =>
            e.Role == ElementRole.CheckBox || e.Role == ElementRole.RadioButton || e.Role == ElementRole.Tab ||
            e.Role == ElementRole.ToggleButton || e.Role == ElementRole.ExpandableGroup || e.Role == ElementRole.Article ||
            e.Role == ElementRole.Carousel || e.Role == ElementRole.ComboBox;

        /// <summary>Rebuilds the list of controls of the current region from the live Unity hierarchy.</summary>
        public void Rebuild(bool initial)
        {
            _nextRebuild = Now + RebuildInterval;
            if (_region == null) return;

            var previous = Current;
            var list = new List<AccessibleElement>();
            var identities = new HashSet<IntPtr>();

            try
            {
                foreach (var s in CollectSelectables(_region))
                {
                    AccessibleElement e;
                    IntPtr key = s.gameObject.Pointer;
                    if (!_cache.TryGetValue(key, out e) || !e.IsAlive)
                    {
                        e = ElementFactory.Create(s);
                        _cache[key] = e;
                    }
                    if (!identities.Add(e.Identity)) continue; // e.g. second arrow button of the same selector
                    list.Add(e);
                }
            }
            catch (Exception ex)
            {
                ModLog.Exception("focus-rebuild", ex);
            }

            // Newspaper: the paper you choose comes before its articles, so switching paper and reading it
            // follow one another instead of being at opposite ends of the list.
            if (_region.Id == "NewsPanel")
            {
                var tabs = list.FindAll(x => x.Role == ElementRole.Tab);
                if (tabs.Count > 0)
                {
                    var articles = list.FindAll(x => x.Role == ElementRole.Article);
                    var rest = list.FindAll(x => x.Role != ElementRole.Tab && x.Role != ElementRole.Article);
                    list = new List<AccessibleElement>(tabs);
                    list.AddRange(articles);
                    list.AddRange(rest);
                }
            }

            // In dialogue, put the responses first so Tab reaches the choices before the participants' portraits.
            if (IsDialogueRegion(_region))
            {
                var responses = list.FindAll(x => x.Role == ElementRole.Choice);
                if (responses.Count > 0)
                {
                    var rest = list.FindAll(x => x.Role != ElementRole.Choice);
                    list = new List<AccessibleElement>(responses);
                    list.AddRange(rest);
                }
            }

            var sig = new System.Text.StringBuilder();
            foreach (var e in list) sig.Append(e.Identity.ToInt64()).Append(',');
            string signature = sig.ToString();
            if (!initial && signature == _elementSignature && _pendingSubPage == null) return;
            bool listChanged = initial || signature != _elementSignature;
            _elementSignature = signature;
            _elements = list;

            if (ModLog.DebugEnabled && listChanged)
            {
                var dump = new System.Text.StringBuilder("Controls of " + _region.Name + ":");
                for (int i = 0; i < list.Count; i++)
                    dump.Append("\n  ").Append(i + 1).Append(". ").Append(Safe(list[i].Label)).Append(" [")
                        .Append(AccessibleElement.RoleName(list[i].Role)).Append("] ")
                        .Append(UiUtil.PathOf(list[i].GameObject.transform));
                ModLog.Debug(dump.ToString());
            }

            // Keep focus on the same logical control if it still exists.
            int newIndex = previous != null ? list.FindIndex(e => e.Identity == previous.Identity) : -1;
            if (initial || newIndex < 0) newIndex = DefaultIndex(list);
            bool lostFocus = !initial && previous != null && list.FindIndex(e => e.Identity == previous.Identity) < 0;
            _index = newIndex;

            // Sub-page changes are only accepted once seen on two consecutive checks (panel animations
            // can make a page appear and disappear for a moment).
            string page = SubPages.Detect(_region);
            bool pageChanged = false;
            if (initial) { _subPage = page; _pendingSubPage = null; }
            else if (page == _subPage) _pendingSubPage = null;
            else if (_pendingSubPage != (page ?? "\0")) _pendingSubPage = page ?? "\0";
            else { pageChanged = true; _subPage = page; _pendingSubPage = null; }

            if (initial)
            {
                SyncSelection(Current);
                return;
            }

            if (pageChanged && page != null && ModConfig.AutoReadScreens.Value)
            {
                _speech.Say(TextUtil.Sentence(page + " page"), automatic: true);
                if (Current != null) AnnounceElement(Current, automatic: true, interrupt: false);
                SyncSelection(Current);
            }
            else if (lostFocus && Current != null && Now >= _suppressAutoFocusAnnouncementUntil && !IsDialogueRegion(_region))
            {
                // The focused control vanished (list refreshed, item removed): say where focus went.
                AnnounceElement(Current, automatic: true);
                SyncSelection(Current);
            }
        }

        private static bool IsDialogueRegion(Region r) =>
            r != null && (r.Id == "ConversationPanel" || r.Id == "NarrationPanel" || r.Id == "PrologueEpiloguePanel");

        private IEnumerable<Selectable> CollectSelectables(Region region)
        {
            Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppArrayBase<Selectable> all;
            if (region.Root != null) all = region.Root.GetComponentsInChildren<Selectable>(false);
            else all = Selectable.allSelectablesArray;

            // Roots of the other visible regions: a control belongs to the nearest region root above it.
            var otherRoots = new HashSet<IntPtr>();
            if (region.Root != null)
                foreach (var r in _screen.Regions)
                    if (r.Root != null && r.Root.Pointer != region.Root.Pointer) otherRoots.Add(r.Root.Pointer);

            // Roots that never belong to the fallback region: the store/profile overlay of the mobile port
            // exists (with controls) while the game is still loading, which made it be read at startup.
            var excluded = new HashSet<IntPtr>();
            if (region.Root == null)
            {
                try
                {
                    var panels = ScreenTracker.GetPanels();
                    if (panels != null)
                    {
                        if (UiUtil.Alive(panels.OverlayPanel)) excluded.Add(panels.OverlayPanel.transform.Pointer);
                        if (UiUtil.Alive(panels.ProfileHUDPanel)) excluded.Add(panels.ProfileHUDPanel.transform.Pointer);
                    }
                }
                catch { }
            }

            for (int i = 0; i < all.Length; i++)
            {
                var s = all[i];
                if (!UiUtil.Alive(s)) continue;
                if (s.TryCast<Scrollbar>() != null) continue;
                var go = s.gameObject;
                if (!UiUtil.IsGameObjectVisible(go)) continue;
                // Controls on a tab's hidden page are skipped entirely: they are not part of this screen.
                if (UiUtil.IsOnInactivePage(s.transform)) continue;
                bool interactable = s.IsInteractable();
                if (region.Root == null)
                {
                    // No registered panel is open (loading, or an unknown screen): only offer controls that
                    // can actually be used, so leftovers of hidden panels are never announced.
                    if (!interactable) continue;
                    if (excluded.Count > 0 && BelongsToOtherRegion(s.transform, null, excluded)) continue;
                }
                else
                {
                    if (!interactable && !ModConfig.IncludeDisabledControls.Value) continue;
                    if (BelongsToOtherRegion(s.transform, region.Root, otherRoots)) continue;
                }
                yield return s;
            }
        }

        /// <summary>True if an ancestor of t is one of otherRoots, stopping at root (root may be null).</summary>
        private static bool BelongsToOtherRegion(Transform t, Transform root, HashSet<IntPtr> otherRoots)
        {
            if (otherRoots.Count == 0) return false;
            for (var cur = t; cur != null; cur = cur.parent)
            {
                IntPtr p = cur.Pointer;
                if (root != null && p == root.Pointer) return false;
                if (otherRoots.Contains(p)) return true;
            }
            return false;
        }

        private int DefaultIndex(List<AccessibleElement> list)
        {
            if (list.Count == 0) return -1;
            if (IsDialogueRegion(_region))
            {
                // In a conversation: the first response if there are any, otherwise anything except the
                // minimize button (Enter must never minimize the conversation by accident).
                int response = list.FindIndex(e => e.Role == ElementRole.Choice);
                if (response >= 0) return response;
                int other = list.FindIndex(e => !e.IsConversationMinimize && e.Role != ElementRole.Character && e.IsInteractable);
                if (other >= 0) return other;
                int notMinimize = list.FindIndex(e => !e.IsConversationMinimize);
                return notMinimize >= 0 ? notMinimize : 0;
            }
            // Respect the control the game itself selected (its FirstSelectable logic), if any.
            try
            {
                var es = EventSystem.current;
                var sel = es != null ? es.currentSelectedGameObject : null;
                if (sel != null)
                {
                    int i = list.FindIndex(e => e.Key == sel.Pointer);
                    if (i >= 0) return i;
                }
            }
            catch { }
            int firstInteractable = list.FindIndex(e => e.IsInteractable);
            return firstInteractable >= 0 ? firstInteractable : 0;
        }

        // ------------------------------------------------------------------ game selection sync

        private void SyncSelection(AccessibleElement e)
        {
            if (e == null || !ModConfig.SyncGameSelection.Value || e.SuppressSelectionSync) return;
            // In conversations only responses are given the game's selection. Otherwise Enter (which the
            // game also uses to continue the dialogue) would additionally press the selected button.
            if (IsDialogueRegion(_region) && e.Role != ElementRole.Choice) return;
            try
            {
                var es = EventSystem.current;
                if (es == null || !e.IsAlive || !e.IsInteractable) return;
                if (es.currentSelectedGameObject != null && es.currentSelectedGameObject.Pointer == e.Key) return;
                es.SetSelectedGameObject(e.GameObject);
                _lastSyncedSelection = e.Key;
                _lastSeenSelection = e.Key;
            }
            catch (Exception ex)
            {
                ModLog.Exception("selection-sync", ex);
            }
        }

        private void WatchGameSelection()
        {
            try
            {
                var es = EventSystem.current;
                if (es == null) return;
                var sel = es.currentSelectedGameObject;
                IntPtr ptr = sel != null ? sel.Pointer : IntPtr.Zero;
                if (ptr == _lastSeenSelection) return;
                _lastSeenSelection = ptr;
                if (ptr == IntPtr.Zero) return;

                // The game moved its selection (its own keyboard/gamepad navigation). Follow it.
                int i = IndexOfGameObject(sel);
                if (i < 0)
                {
                    Rebuild(initial: false);
                    i = IndexOfGameObject(sel);
                }
                if (i >= 0 && i != _index)
                {
                    _index = i;
                    if (Now - _lastUserNavigation < 1f) LastExplicitFocusTime = ModClock.Now;
                    // In dialogue the game pre-selects the first response by itself when options appear;
                    // DialogueAccessibility reads the text first and then the options, so only announce
                    // selection changes there when they follow a key press.
                    bool keyDriven = Now - _lastUserNavigation < 1f;
                    if (!IsDialogueRegion(_region) || keyDriven)
                        AnnounceElement(Current, automatic: !keyDriven);
                }
            }
            catch (Exception ex)
            {
                ModLog.Exception("selection-watch", ex);
            }
        }

        private int IndexOfGameObject(GameObject go)
        {
            var t = go.transform;
            for (int level = 0; level < 3 && t != null; level++, t = t.parent)
            {
                IntPtr p = t.gameObject.Pointer;
                int i = _elements.FindIndex(e => e.Key == p);
                if (i >= 0) return i;
            }
            return -1;
        }

        // ------------------------------------------------------------------ description

        public string Describe(AccessibleElement e, bool detailed = false)
        {
            if (e == null) return "Nothing focused.";
            string label = Safe(e.Label);
            string value = Safe(e.Value);
            string state = Safe(e.State);
            try
            {
                if (UiUtil.Alive(e.Notice) && e.Notice.IsShowing() && !state.Contains("new")) state = TextUtil.Join(state, "new");
            }
            catch { }
            bool normal = ModConfig.AtLeast(Verbosity.Normal);
            int position = _elements.IndexOf(e);
            int total = _elements.Count;

            string text;
            if (e.Role == ElementRole.Choice)
            {
                // Responses are numbered among the responses only.
                var responses = _elements.FindAll(x => x.Role == ElementRole.Choice);
                int r = responses.IndexOf(e);
                string prefix = normal && ModConfig.AnnouncePositions.Value && r >= 0
                    ? "Response " + (r + 1) + " of " + responses.Count + ": " : "";
                text = prefix + TextUtil.Join(string.IsNullOrEmpty(label) ? "unlabelled" : label, state);
            }
            else
            {
                string role = normal && ModConfig.AnnounceRoles.Value ? AccessibleElement.RoleName(e.Role) : "";
                if (e.Role == ElementRole.Item) role = "";
                // Avoid "Budget, statistic, plus 2" repeating the label inside the value.
                if (!string.IsNullOrEmpty(value) && label.Contains(value)) value = "";
                string pos = normal && ModConfig.AnnouncePositions.Value && position >= 0 && total > 1
                    ? (position + 1) + " of " + total : "";
                text = TextUtil.Join(string.IsNullOrEmpty(label) ? "unlabelled" : label, role, value, state, pos);
            }

            if (detailed)
            {
                string detail = Safe(e.Detail);
                if (string.IsNullOrEmpty(detail) && e.GameObject != null)
                    detail = TextUtil.JoinWith(". ", UiUtil.CollectTexts(e.GameObject.transform, includeHidden: true));
                if (!string.IsNullOrEmpty(detail) && detail != label) text = TextUtil.Sentence(text) + " " + detail;
                if (!string.IsNullOrEmpty(e.Hint)) text = TextUtil.Sentence(text) + " " + e.Hint;
            }
            else if (ModConfig.AtLeast(Verbosity.High) && !string.IsNullOrEmpty(e.Hint))
            {
                text = TextUtil.Sentence(text) + " " + e.Hint;
            }
            return text;
        }

        private IntPtr _lastAnnouncedIdentity;
        private string _lastAnnouncedText;
        private float _lastAnnouncedTime = -100f;

        /// <summary>
        /// Speaks an element. Automatic announcements of the element that was just announced with the same
        /// text are dropped, so a UI refresh or a panel animation cannot make the same control be read
        /// several times in a row. Key presses always speak.
        /// </summary>
        private void AnnounceElement(AccessibleElement e, bool automatic, bool interrupt = true)
        {
            if (e == null) return;
            string text = Describe(e);
            if (automatic && e.Identity == _lastAnnouncedIdentity && text == _lastAnnouncedText && Now - _lastAnnouncedTime < 3f)
            {
                ModLog.Debug("Skipped repeated announcement: " + text);
                return;
            }
            _lastAnnouncedIdentity = e.Identity;
            _lastAnnouncedText = text;
            _lastAnnouncedTime = Now;
            _speech.Say(text, interrupt: interrupt, automatic: automatic);
        }

        private static string Safe(Func<string> f)
        {
            try { return f?.Invoke() ?? string.Empty; }
            catch { return string.Empty; }
        }

        // ------------------------------------------------------------------ commands

        /// <summary>Drops queued arrow/slider actions (called when the game window loses focus).</summary>
        public void CancelPending()
        {
            _arrowDelta = 0;
            _sliderElement = null;
            _pendingAfterActivation = null;
            _delayedValueElement = null;
            _pendingRegionId = null;
        }

        /// <summary>ModClock.Now of the last focus move made with a navigation key.</summary>
        public float LastExplicitFocusTime { get; private set; } = -100f;

        public bool InDialogueRegion => IsDialogueRegion(_region);

        public void Move(int delta)
        {
            LastExplicitFocusTime = ModClock.Now;
            Rebuild(initial: false);
            if (_elements.Count == 0) { _speech.Say(EmptyMessage()); return; }
            int count = _elements.Count;
            int next = _index < 0 ? (delta > 0 ? 0 : count - 1) : (_index + delta + count) % count;
            bool wrapped = _index >= 0 && ((delta > 0 && next < _index) || (delta < 0 && next > _index));
            _index = next;
            SyncSelection(Current);
            string text = Describe(Current);
            if (wrapped && ModConfig.AtLeast(Verbosity.Normal)) text = (delta > 0 ? "Top. " : "Bottom. ") + text;
            _speech.Say(text);
        }

        // ------------------------------------------------------------------ arrow keys

        private int _arrowDelta;
        private int _arrowFrames;
        private IntPtr _arrowSelectionAtPress;
        private IntPtr _selectionAtFrameEnd;
        private float _lastUserNavigation = -100f;

        /// <summary>
        /// Up/Down arrow navigation. The game binds Up/Down to its own dialogue response navigation
        /// (ResponsesNavigate), which moves the game's EventSystem selection. To avoid moving twice, the
        /// arrow press is first left to the game: if after two frames the game's selection has changed,
        /// the game handled it (and <see cref="WatchGameSelection"/> announces the new item). Otherwise
        /// the mod moves the focus itself - which is what happens in menus, where the game ignores arrows.
        /// </summary>
        public void QueueArrowMove(int delta)
        {
            _arrowDelta = delta;
            _arrowFrames = 3;
            _lastUserNavigation = Now;
            // The game may already have reacted to the key earlier in this same frame (its input is
            // processed before this Update), so compare against the selection as it was last frame.
            _arrowSelectionAtPress = _selectionAtFrameEnd;
        }

        private void ProcessQueuedArrow()
        {
            if (_arrowDelta == 0 || --_arrowFrames > 0) return;
            int delta = _arrowDelta;
            _arrowDelta = 0;
            IntPtr now = CurrentGameSelection();
            if (now != IntPtr.Zero && now != _arrowSelectionAtPress)
            {
                var es = EventSystem.current;
                var sel = es != null ? es.currentSelectedGameObject : null;
                if (sel != null && IndexOfGameObject(sel) >= 0)
                {
                    // The game moved its own selection; WatchGameSelection has announced it.
                    ModLog.Debug("Arrow key handled by the game's own navigation.");
                    return;
                }
            }
            Move(delta);
        }

        private static IntPtr CurrentGameSelection()
        {
            try
            {
                var es = EventSystem.current;
                var sel = es != null ? es.currentSelectedGameObject : null;
                return sel != null ? sel.Pointer : IntPtr.Zero;
            }
            catch { return IntPtr.Zero; }
        }

        public void MoveToEdge(bool last)
        {
            LastExplicitFocusTime = ModClock.Now;
            Rebuild(initial: false);
            if (_elements.Count == 0) { _speech.Say(EmptyMessage()); return; }
            _index = last ? _elements.Count - 1 : 0;
            SyncSelection(Current);
            _speech.Say(Describe(Current));
        }

        /// <summary>Moves accessibility focus to a specific game object (used by features).</summary>
        public bool FocusGameObject(GameObject go, bool announce)
        {
            if (!UiUtil.Alive(go)) return false;
            Rebuild(initial: false);
            int i = IndexOfGameObject(go);
            if (i < 0) return false;
            _index = i;
            SyncSelection(Current);
            if (announce) _speech.Say(Describe(Current));
            return true;
        }

        public void Activate(bool fromSharedEnterKey)
        {
            OptionsChanged();
            Rebuild(initial: false);
            var e = Current;
            if (e == null || !e.IsAlive) { _speech.Say(EmptyMessage()); return; }
            if (!e.IsInteractable) { _speech.Say(TextUtil.Join(Safe(e.Label), "unavailable")); return; }

            if (fromSharedEnterKey && ModConfig.EnterActivation.Value == EnterMode.Auto && WillGameHandleEnter(e))
            {
                // Enter is also the game's own UI Submit key. The game's EventSystem will deliver Submit to
                // this exact object in this frame, so activating it here as well would press it twice.
                ModLog.Info("Enter left to the game's own Submit for " + UiUtil.PathOf(e.GameObject.transform) +
                            ". If nothing happens, set 'Enter key activation' to 'always handled by the mod' in F9.");
                AfterActivation(e);
                return;
            }

            if (e.Activate == null)
            {
                _speech.Say(e.Adjust != null ? "Use Left and Right arrows to change the value." : "This item cannot be activated.");
                return;
            }

            try
            {
                ElementFactory.LastCodexMessage = null;
                e.Activate();
                ModLog.Debug("Activated " + UiUtil.PathOf(e.GameObject.transform));
                if (ElementFactory.LastCodexMessage != null)
                {
                    _speech.Say(ElementFactory.LastCodexMessage, important: true);
                    ElementFactory.LastCodexMessage = null;
                }
                if (ElementFactory.RequestedRegionId != null)
                {
                    // The panel needs a moment to appear before the focus can move into it.
                    _pendingRegionId = ElementFactory.RequestedRegionId;
                    _pendingRegionFrames = 6;
                    ElementFactory.RequestedRegionId = null;
                }
            }
            catch (Exception ex)
            {
                ModLog.Exception("activate", ex);
                _speech.Say("Could not activate " + Safe(e.Label));
            }
            AfterActivation(e);
        }

        private static bool WillGameHandleEnter(AccessibleElement e)
        {
            try
            {
                var es = EventSystem.current;
                if (es == null || !es.sendNavigationEvents) return false;
                var selected = es.currentSelectedGameObject;
                if (selected == null || selected.Pointer != e.Key) return false;
                var handler = ExecuteEvents.GetEventHandler<ISubmitHandler>(e.GameObject);
                return handler != null && handler.Pointer == e.Key;
            }
            catch
            {
                return false;
            }
        }

        private void AfterActivation(AccessibleElement e)
        {
            _pendingAfterActivation = e;
            _pendingFrames = 3;
            _screen.ForcePoll();
        }

        /// <summary>Returns true if the focused control was adjustable and the key was consumed.</summary>
        public bool Adjust(int step)
        {
            OptionsChanged();
            var e = Current;
            if (e == null || e.Adjust == null || !e.IsAlive) return false;
            if (!e.IsInteractable) { _speech.Say("unavailable"); return true; }

            // A selected Slider also reacts to the arrow keys through the game's own UI navigation.
            // Give the game the first chance (same technique as the Up/Down arrows) to avoid a double step.
            if (e.Role == ElementRole.Slider && e.Selectable != null)
            {
                _sliderElement = e;
                _sliderStep = step;
                _sliderFrames = 3;
                _sliderValueAtPress = _sliderValueAtFrameEnd;
                return true;
            }

            try
            {
                e.Adjust(step);
            }
            catch (Exception ex)
            {
                ModLog.Exception("adjust", ex);
            }
            _pendingAfterActivation = null;
            // Value texts update on the next frame for some widgets; speak after a short delay.
            _delayedValueElement = e;
            _delayedValueFrames = 2;
            return true;
        }

        private AccessibleElement _sliderElement;
        private int _sliderStep;
        private int _sliderFrames;
        private float _sliderValueAtPress = float.NaN;
        private float _sliderValueAtFrameEnd = float.NaN;

        private static float SliderValue(AccessibleElement e)
        {
            try
            {
                var slider = e?.Selectable != null ? e.Selectable.TryCast<Slider>() : null;
                return slider != null ? slider.value : float.NaN;
            }
            catch { return float.NaN; }
        }

        private void ProcessQueuedSlider()
        {
            if (_sliderElement == null || --_sliderFrames > 0) return;
            var e = _sliderElement;
            _sliderElement = null;
            if (!e.IsAlive) return;
            float now = SliderValue(e);
            bool gameChangedIt = !float.IsNaN(now) && !float.IsNaN(_sliderValueAtPress) && Math.Abs(now - _sliderValueAtPress) > 0.00001f;
            if (!gameChangedIt)
            {
                try { e.Adjust(_sliderStep); }
                catch (Exception ex) { ModLog.Exception("adjust", ex); }
            }
            _delayedValueElement = e;
            _delayedValueFrames = 2;
        }

        private AccessibleElement _delayedValueElement;
        private int _delayedValueFrames;

        private string _pendingRegionId;
        private int _pendingRegionFrames;

        /// <summary>Moves the focus into a panel that an activation opened (for example the Codex).</summary>
        private void ProcessPendingRegion()
        {
            if (_pendingRegionId == null) return;
            _screen.ForcePoll();
            foreach (var r in _screen.Regions)
            {
                if (r.Id != _pendingRegionId) continue;
                _pendingRegionId = null;
                SwitchTo(r, announce: true, userInitiated: true);
                return;
            }
            if (--_pendingRegionFrames <= 0)
            {
                ModLog.Info("Panel " + _pendingRegionId + " did not appear, so the focus did not move.");
                _pendingRegionId = null;
            }
        }

        public void LateTick()
        {
            ProcessPendingRegion();
            if (_delayedValueElement != null && --_delayedValueFrames <= 0)
            {
                var e = _delayedValueElement;
                _delayedValueElement = null;
                if (e.IsAlive) _speech.Say(TextUtil.Join(Safe(e.Value), Safe(e.State)) is string s && s.Length > 0 ? s : Safe(e.Label));
            }
        }

        /// <summary>Ctrl+number: jump straight to a kind of panel, if one is open.</summary>
        public void JumpToRegion(Func<Region, bool> match, string missingMessage)
        {
            foreach (var r in _screen.Regions)
            {
                if (r.IsFallback || !match(r)) continue;
                SwitchTo(r, announce: true, userInitiated: true);
                return;
            }
            _speech.Say(missingMessage);
        }

        public void CycleRegion(int delta)
        {
            var regions = _screen.Regions;
            if (regions.Count == 0) { _speech.Say("No panels."); return; }
            int i = _region != null ? regions.FindIndex(r => r.Id == _region.Id) : -1;
            i = i < 0 ? 0 : (i + delta + regions.Count) % regions.Count;
            SwitchTo(regions[i], announce: true, userInitiated: true);
            if (regions.Count == 1 && ModConfig.AtLeast(Verbosity.High)) _speech.Say("Only one panel is open.", interrupt: false);
        }

        public void DescribeFocused() => _speech.Say(Current != null ? Describe(Current, detailed: true) : EmptyMessage());

        public void RepeatFocused() => _speech.Say(Current != null ? Describe(Current) : EmptyMessage());

        private string EmptyMessage()
        {
            if (_region == null) return "The game is still loading.";
            if (_region.IsFallback) return "The game is still loading, or this screen has no controls yet.";
            return _region.Name + ": no controls. Press F2 to read the text.";
        }

        /// <summary>All visible text of the current region in reading order.</summary>
        public List<string> RegionTexts()
        {
            if (_region == null) return new List<string>();
            if (_region.Root != null)
            {
                var texts = UiUtil.CollectTexts(_region.Root, includeHidden: false);
                if (_region.Id == "TokenInformationPanel") AppendDemographics(texts);
                return texts;
            }
            // Fallback region: texts of every visible selectable.
            var list = new List<string>();
            foreach (var e in _elements) list.Add(Safe(e.Label));
            return list;
        }

        /// <summary>The ethnicity/religion pie charts have headings but no text; add their data as lines.</summary>
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
            catch (Exception ex)
            {
                ModLog.Exception("demographics", ex);
            }
        }

        // ------------------------------------------------------------------ options: apply on leaving

        private IntPtr _optionsAppliedFor;

        /// <summary>
        /// Leaving the Options page with Backspace: changes such as window mode only take effect after the
        /// game's Apply (OptionsPage.OnApplyClick(), the same method as its Apply button). The first Backspace
        /// applies; if the game asks for confirmation, that dialog is announced. The next Backspace leaves.
        /// Changing another option makes the next Backspace apply again.
        /// Returns true if it applied (and the caller must not leave yet).
        /// </summary>
        private bool ApplyOptionsFirst(Transform menuRoot)
        {
            OptionsPage page = null;
            var pages = menuRoot.GetComponentsInChildren<OptionsPage>(false);
            for (int i = 0; i < pages.Length && page == null; i++)
                if (UiUtil.Alive(pages[i]) && pages[i].gameObject.activeInHierarchy) page = pages[i];
            if (page == null || page.Pointer == _optionsAppliedFor) return false;
            try
            {
                if (page.isApplyingSettings) return true;
                page.OnApplyClick();
                _optionsAppliedFor = page.Pointer;
                ModLog.Info("Options applied through OptionsPage.OnApplyClick().");
                _speech.Say("Settings applied. Press Backspace again to leave Options.");
                _screen.ForcePoll();
                return true;
            }
            catch (Exception ex)
            {
                ModLog.Exception("options-apply", ex);
                return false;
            }
        }

        /// <summary>Any change in the Options page means the next Backspace should apply again.</summary>
        private void OptionsChanged()
        {
            // Only changes made inside the Options page count (not, for example, confirming a dialog).
            if (_region != null && (_region.Id == "MainMenuPanel" || _region.Id == "EscapeMenuPanel") && _subPage == "Options")
                _optionsAppliedFor = IntPtr.Zero;
        }

        // ------------------------------------------------------------------ back

        public void Back()
        {
            var panels = ScreenTracker.GetPanels();
            if (_region == null || panels == null || _region.IsFallback) { _speech.Say("No back action here."); return; }

            try
            {
                switch (_region.Id)
                {
                    case "EscapeMenuPanel":
                    {
                        var menu = panels.EscapeMenuPanel;
                        if (menu.IsOptionsPageShowing() && ApplyOptionsFirst(menu.transform)) return;
                        if (SubPages.PauseMenuSubPage(menu)) menu.OnHeaderBackClick();
                        else menu.OnReturnClick();
                        return;
                    }
                    case "MainMenuPanel":
                    {
                        var menu = panels.MainMenuPanel;
                        if (menu.IsOptionsPageShowing() && ApplyOptionsFirst(menu.transform)) return;
                        if (!menu.IsMainPageShowing()) menu.OnHeaderBackClick();
                        else _speech.Say("Main menu. This is the top level.");
                        return;
                    }
                    case "ConversationPanel":
                    case "NarrationPanel":
                    case "PrologueEpiloguePanel":
                        _speech.Say("A conversation cannot be closed. Choose a response or press Space to continue.");
                        return;
                }

                var focus = _region.Descriptor.Focus?.Invoke(panels);
                if (UiUtil.Alive(focus))
                {
                    // Same code path the game uses for the gamepad "cancel/close" action on focusable panels.
                    string cameFrom = _previousRegionId;
                    focus.OnCloseButtonClick();
                    _screen.ForcePoll();
                    // Return to the panel this one was opened from (for example the Codex back to Connections).
                    if (cameFrom != null && cameFrom != _region.Id)
                    {
                        _pendingRegionId = cameFrom;
                        _pendingRegionFrames = 8;
                    }
                    return;
                }

                // Last resort: a visible close/back button (object-name heuristic, logged).
                foreach (var e in _elements)
                {
                    if (e.Role != ElementRole.Button || !e.IsInteractable) continue;
                    string n = e.GameObject.name.ToLowerInvariant();
                    if (n.Contains("close") || n.Contains("back") || n.Contains("return"))
                    {
                        ModLog.Info("Back: using button found by name heuristic: " + UiUtil.PathOf(e.GameObject.transform));
                        e.Activate?.Invoke();
                        _screen.ForcePoll();
                        return;
                    }
                }
                _speech.Say(_region.Tier == PanelTier.Base
                    ? _region.Name + " is part of the screen and cannot be closed. Control Tab moves to another panel."
                    : "No back action for " + _region.Name + ".");
            }
            catch (Exception ex)
            {
                ModLog.Exception("back", ex);
                _speech.Say("Back failed.");
            }
        }
    }
}
