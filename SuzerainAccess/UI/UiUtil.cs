using System;
using System.Collections.Generic;
using System.Text;
using SuzerainAccess.Config;
using TMPro;
using UnityEngine;

namespace SuzerainAccess.UI
{
    /// <summary>
    /// Helpers that inspect real Unity UI state (GameObject activity, CanvasGroup alpha, Canvas enabled,
    /// TextMeshPro texts). Nothing here depends on screen coordinates or the mouse.
    /// </summary>
    internal static class UiUtil
    {
        /// <summary>True if the managed wrapper still points at a live Unity object.</summary>
        public static bool Alive(UnityEngine.Object o)
        {
            try
            {
                return !ReferenceEquals(o, null) && !o.WasCollected && o != null;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Effective opacity from the CanvasGroups above a transform. Suzerain fades panels in and out
        /// with CanvasGroup alpha, so an active GameObject is not necessarily visible.
        /// </summary>
        public static float EffectiveAlpha(Transform t)
        {
            float alpha = 1f;
            var groups = t.GetComponentsInParent<CanvasGroup>(false);
            for (int i = 0; i < groups.Length; i++)
            {
                var g = groups[i];
                if (!g.enabled) continue;
                alpha *= g.alpha;
                if (g.ignoreParentGroups) break;
            }
            return alpha;
        }

        /// <summary>Any disabled Canvas above the transform hides it.</summary>
        public static bool CanvasesEnabled(Transform t)
        {
            var canvases = t.GetComponentsInParent<Canvas>(false);
            for (int i = 0; i < canvases.Length; i++)
                if (!canvases[i].enabled) return false;
            return true;
        }

        /// <summary>
        /// True if a CanvasGroup above the control switches interaction off. Suzerain keeps the pages of a
        /// tabbed panel (Connections, Overview...) alive and fully drawn but non-interactive, so this marks
        /// controls that belong to a page other than the one on show.
        /// </summary>
        public static bool IsOnInactivePage(Transform t)
        {
            if (!Alive(t)) return false;
            var groups = t.GetComponentsInParent<CanvasGroup>(false);
            for (int i = 0; i < groups.Length; i++)
            {
                var g = groups[i];
                if (!g.enabled) continue;
                if (!g.interactable) return true;
                if (g.ignoreParentGroups) break;
            }
            return false;
        }

        public static bool IsGameObjectVisible(GameObject go)
        {
            if (!Alive(go)) return false;
            if (!go.activeInHierarchy) return false;
            var t = go.transform;
            return EffectiveAlpha(t) > 0.01f && CanvasesEnabled(t);
        }

        /// <summary>
        /// True if the object is visible AND contains at least one visible text or interactable control.
        /// Used for container-like panels that are always active but usually empty.
        /// </summary>
        public static bool HasVisibleContent(Transform root)
        {
            if (!Alive(root) || !IsGameObjectVisible(root.gameObject)) return false;
            var texts = root.GetComponentsInChildren<TMP_Text>(false);
            for (int i = 0; i < texts.Length; i++)
                if (IsTextVisible(texts[i]) && !string.IsNullOrWhiteSpace(texts[i].text)) return true;
            var controls = root.GetComponentsInChildren<UnityEngine.UI.Selectable>(false);
            for (int i = 0; i < controls.Length; i++)
                if (controls[i].IsInteractable() && IsGameObjectVisible(controls[i].gameObject)) return true;
            return false;
        }

        public static bool IsTextVisible(TMP_Text text)
        {
            if (!Alive(text) || !text.enabled) return false;
            if (text.color.a <= 0.01f) return false;
            return IsGameObjectVisible(text.gameObject);
        }

        /// <summary>Cleaned text of a TMP component, or empty if missing.</summary>
        public static string TextOf(TMP_Text text)
        {
            if (!Alive(text)) return string.Empty;
            try { return TextUtil.Clean(text.text); }
            catch { return string.Empty; }
        }

        /// <summary>Cleaned text only if the component is currently visible.</summary>
        public static string VisibleTextOf(TMP_Text text) => IsTextVisible(text) ? TextOf(text) : string.Empty;

        /// <summary>
        /// Collects the texts under <paramref name="root"/> in hierarchy order.
        /// </summary>
        /// <param name="root">Where to start.</param>
        /// <param name="includeHidden">Include texts that are inactive or faded out (tooltips, collapsed content).</param>
        /// <param name="skip">Optional filter; return true to skip a text component.</param>
        public static List<string> CollectTexts(Transform root, bool includeHidden, Func<TMP_Text, bool> skip = null)
        {
            var result = new List<string>();
            if (!Alive(root)) return result;
            var seen = new HashSet<string>();
            var texts = root.GetComponentsInChildren<TMP_Text>(includeHidden);
            for (int i = 0; i < texts.Length; i++)
            {
                var t = texts[i];
                try
                {
                    if (!includeHidden && (!IsTextVisible(t) || IsOnInactivePage(t.transform))) continue;
                    if (skip != null && skip(t)) continue;
                    string s = TextOf(t);
                    if (ModConfig.IgnoreDecorativeText.Value && TextUtil.IsDecorative(s)) continue;
                    if (string.IsNullOrEmpty(s)) continue;
                    // Text shadows/outlines are often implemented as duplicated text layers.
                    if (ModConfig.IgnoreDecorativeText.Value && !seen.Add(s)) continue;
                    result.Add(s);
                }
                catch
                {
                    // A text destroyed mid-iteration is simply skipped.
                }
            }
            return result;
        }

        /// <summary>Short hierarchy path for log messages.</summary>
        public static string PathOf(Transform t, int maxDepth = 6)
        {
            if (!Alive(t)) return "<destroyed>";
            var parts = new List<string>();
            try
            {
                for (var cur = t; cur != null && parts.Count < maxDepth; cur = cur.parent)
                    parts.Insert(0, cur.name);
            }
            catch { }
            return string.Join("/", parts);
        }

        /// <summary>Returns the component on the object or on one of its first few ancestors.</summary>
        public static T FindInSelfOrParents<T>(Transform t, int levels) where T : Component
        {
            var cur = t;
            for (int i = 0; i <= levels && cur != null; i++)
            {
                var c = cur.GetComponent<T>();
                if (c != null) return c;
                cur = cur.parent;
            }
            return null;
        }
    }
}
