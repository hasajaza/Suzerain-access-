using System;
using System.Collections.Generic;
using SuzerainAccess.Config;
using SuzerainAccess.Core;
using SuzerainAccess.Game;
using SuzerainAccess.Speech;
using SuzerainAccess.UI;

namespace SuzerainAccess.Features
{
    /// <summary>
    /// Announces the game's pop-up notifications (NotificationPanel.instantiatedNotifications,
    /// TemplateNotification.title/subtitle) and keeps a history so a notification that disappeared
    /// visually can still be read. "Open latest" calls TemplateNotification.OnNotificationClick(),
    /// the same method the game runs when the notification is clicked.
    /// </summary>
    internal sealed class NotificationAccessibility
    {
        private sealed class Entry
        {
            public string Text;
            public TemplateNotification Widget;
        }

        private const int HistoryLimit = 30;

        private readonly SpeechManager _speech;
        private readonly List<Entry> _history = new List<Entry>();
        private readonly HashSet<string> _seen = new HashSet<string>();
        private readonly Dictionary<IntPtr, int> _waitingFrames = new Dictionary<IntPtr, int>();

        public NotificationAccessibility(SpeechManager speech)
        {
            _speech = speech;
        }

        public void Tick()
        {
            try
            {
                var panels = ScreenTracker.GetPanels();
                if (panels == null) return;
                var panel = panels.NotificationPanel;
                if (!UiUtil.Alive(panel)) return;
                var list = panel.instantiatedNotifications;
                for (int i = 0; list != null && i < list.Count; i++)
                {
                    var n = list[i];
                    if (!UiUtil.Alive(n) || !n.gameObject.activeInHierarchy) continue;
                    string title = UiUtil.TextOf(n.title);
                    string subtitle = UiUtil.TextOf(n.subtitle);
                    string text = TextUtil.Join(title, subtitle);
                    if (string.IsNullOrEmpty(text))
                    {
                        // Texts are filled in Setup; give it a few frames.
                        _waitingFrames.TryGetValue(n.Pointer, out int f);
                        _waitingFrames[n.Pointer] = f + 1;
                        continue;
                    }
                    // Notification widgets are pooled and reused: identify by object + text.
                    string key = n.Pointer.ToInt64() + "|" + text;
                    if (!_seen.Add(key)) continue;
                    _waitingFrames.Remove(n.Pointer);

                    _history.Add(new Entry { Text = text, Widget = n });
                    if (_history.Count > HistoryLimit) _history.RemoveAt(0);
                    ModLog.Debug("Notification: " + text);
                    if (ModConfig.AnnounceNotifications.Value)
                        _speech.Say("Notification: " + text, interrupt: false, important: true, automatic: true);
                }
                if (_seen.Count > 500) _seen.Clear();
            }
            catch (Exception ex)
            {
                ModLog.Exception("notifications", ex);
            }
        }

        public void ReadLatest()
        {
            _speech.Say(_history.Count == 0 ? "No notifications yet." : "Latest notification: " + _history[_history.Count - 1].Text);
        }

        public void ReadHistory()
        {
            if (_history.Count == 0) { _speech.Say("No notifications yet."); return; }
            var parts = new List<string>();
            for (int i = _history.Count - 1, n = 0; i >= 0 && n < 5; i--, n++) parts.Add(_history[i].Text);
            _speech.Say("Recent notifications, newest first: " + TextUtil.JoinWith(". ", parts) + ".");
        }

        public void OpenLatest()
        {
            if (_history.Count == 0) { _speech.Say("No notifications yet."); return; }
            var entry = _history[_history.Count - 1];
            var w = entry.Widget;
            if (!UiUtil.Alive(w) || !w.gameObject.activeInHierarchy || TextUtil.Join(UiUtil.TextOf(w.title), UiUtil.TextOf(w.subtitle)) != entry.Text)
            {
                _speech.Say("That notification is no longer on screen.");
                return;
            }
            try
            {
                w.OnNotificationClick();
                _speech.Say("Opening notification.");
            }
            catch (Exception ex)
            {
                ModLog.Exception("notification-open", ex);
                _speech.Say("Could not open the notification.");
            }
        }
    }
}
