using System;
using System.Collections.Generic;
using System.Diagnostics;
using SuzerainAccess.Core;
using SuzerainAccess.UI;
using UnityEngine;
using UnityEngine.UI;

namespace SuzerainAccess.Game
{
    /// <summary>A navigable area of the screen: one visible game panel, or the whole screen as fallback.</summary>
    internal sealed class Region
    {
        public PanelDescriptor Descriptor;
        public string Name;
        public Transform Root;

        public string Id => Descriptor != null ? Descriptor.Id : "__screen";
        public bool IsFallback => Descriptor == null;
        public PanelTier Tier => Descriptor != null ? Descriptor.Tier : PanelTier.Base;
    }

    /// <summary>
    /// Polls the game's Panels singleton (10 times per second) to find out which panels are showing.
    /// Suzerain creates and hides panels dynamically, so nothing is assumed to exist at startup.
    /// </summary>
    internal sealed class ScreenTracker
    {
        private const float PollInterval = 0.1f;
        private const float ModalCheckCacheSeconds = 0.5f;

        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly Dictionary<string, (float time, bool result)> _modalCache = new Dictionary<string, (float, bool)>();
        private float _nextPoll;
        private string _signature = "";
        private string _pendingSignature;

        public List<Region> Regions { get; private set; } = new List<Region>();
        public Region Top => Regions.Count > 0 ? Regions[0] : null;

        /// <summary>Raised when the top-most region changes (new, previous).</summary>
        public event Action<Region, Region> TopChanged;
        /// <summary>Raised when the set of visible regions changes.</summary>
        public event Action RegionsChanged;

        private float Now => (float)_clock.Elapsed.TotalSeconds;

        public static Panels GetPanels()
        {
            try
            {
                var p = Panels.Instance;
                return UiUtil.Alive(p) ? p : null;
            }
            catch
            {
                return null;
            }
        }

        public void ForcePoll() => _nextPoll = 0f;

        /// <summary>Panels that must never take the focus away by themselves (mobile-port leftovers).</summary>
        private static readonly HashSet<string> NeverAutoFocus = new HashSet<string> { "OverlayPanel", "AdPanel", "OfferPanel", "RewardsPanel" };

        public void Tick()
        {
            float now = Now;
            if (now < _nextPoll) return;
            _nextPoll = now + PollInterval;

            var panels = GetPanels();
            var visible = new List<PanelDescriptor>();
            if (panels != null)
            {
                foreach (var d in PanelRegistry.Descriptors)
                {
                    bool showing = false;
                    try { showing = d.IsShowing(panels); }
                    catch (Exception ex) { ModLog.Exception("panel-visibility:" + d.Id, ex); }
                    if (showing) visible.Add(d);
                }
            }

            // Modal panels are exclusive, but only when they actually offer something to interact with.
            // This guards against a panel that reports "showing" while being an empty overlay.
            var modal = visible.FindAll(d => d.Tier == PanelTier.Modal && ModalHasControls(panels, d, now));
            var chosen = modal.Count > 0 ? modal : visible;
            chosen.Sort((a, b) =>
            {
                bool na = NeverAutoFocus.Contains(a.Id), nb = NeverAutoFocus.Contains(b.Id);
                if (na != nb) return na ? 1 : -1;                       // these come last
                if (a.Tier != b.Tier) return b.Tier.CompareTo(a.Tier);
                return a.Order.CompareTo(b.Order);
            });

            var regions = new List<Region>();
            foreach (var d in chosen)
            {
                Transform root = null;
                try
                {
                    var mb = d.Get(panels);
                    if (UiUtil.Alive(mb)) root = mb.transform;
                }
                catch { }
                if (root != null) regions.Add(new Region { Descriptor = d, Name = d.Name, Root = root });
            }
            if (regions.Count == 0) regions.Add(new Region { Descriptor = null, Name = "Screen", Root = null });

            string signature = string.Join("|", regions.ConvertAll(r => r.Id));
            if (signature == _signature)
            {
                _pendingSignature = null;
                // Same panels; refresh roots in case objects were recreated.
                Regions = regions;
                return;
            }

            // Debounce: a new panel combination must be seen on two consecutive polls (0.1 s apart).
            // Panels that flicker for a frame while animating therefore cannot trigger repeated
            // "screen changed" announcements.
            if (_signature.Length > 0 && signature != _pendingSignature)
            {
                _pendingSignature = signature;
                return;
            }
            _pendingSignature = null;

            var previousTop = Top;
            _signature = signature;
            Regions = regions;
            ModLog.Debug("Visible regions: " + signature);
            // Exactly one event per change. Raising both made the new screen be announced twice
            // ("Main menu, 7 controls" twice), and the second time the focused item was skipped as a repeat.
            if (previousTop == null || previousTop.Id != Top.Id)
            {
                ModLog.Info("Detected screen: " + Top.Name + (Top.IsFallback ? " (no registered panel visible)" : ""));
                TopChanged?.Invoke(Top, previousTop);
            }
            else
            {
                RegionsChanged?.Invoke();
            }
        }

        public bool IsShowing(string panelId)
        {
            foreach (var r in Regions) if (r.Id == panelId) return true;
            return false;
        }

        private bool ModalHasControls(Panels panels, PanelDescriptor d, float now)
        {
            if (_modalCache.TryGetValue(d.Id, out var cached) && now - cached.time < ModalCheckCacheSeconds) return cached.result;
            bool result = false;
            try
            {
                var mb = d.Get(panels);
                if (UiUtil.Alive(mb))
                {
                    var selectables = mb.GetComponentsInChildren<Selectable>(false);
                    for (int i = 0; i < selectables.Length && !result; i++)
                        result = selectables[i].IsInteractable() && UiUtil.IsGameObjectVisible(selectables[i].gameObject);
                }
            }
            catch { }
            _modalCache[d.Id] = (now, result);
            return result;
        }
    }
}
