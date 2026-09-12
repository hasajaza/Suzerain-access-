using System;
using System.Collections.Generic;
using System.Diagnostics;
using SuzerainAccess.Config;
using SuzerainAccess.Core;
using SuzerainAccess.UI;

namespace SuzerainAccess.Speech
{
    /// <summary>
    /// The single path through which every announcement reaches the screen reader.
    ///
    /// Rules:
    ///  * User-initiated speech (a key press) interrupts whatever is being said.
    ///  * Important automatic speech (dialogue, report content, notifications) is queued so that two
    ///    consecutive lines are both heard.
    ///  * Informational automatic speech (e.g. focus moving because a screen opened) does not cut off
    ///    important speech that started a moment ago; it is queued instead.
    ///  * Identical automatic announcements inside the duplicate window are dropped.
    /// </summary>
    internal sealed class SpeechManager
    {
        private const float ImportantGuardSeconds = 1.5f;
        private const int HistoryLimit = 100;

        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly List<string> _history = new List<string>();
        private string _lastText;
        private float _lastTime = -100f;
        private float _lastImportantTime = -100f;

        public bool Available { get; private set; }

        private JawsSupport _jaws;

        /// <summary>Engine chosen by the player with Shift+F11, or null for automatic choice.</summary>
        private string _forcedEngine;

        /// <summary>The mod's own JAWS routes are only used in automatic mode or when JAWS was chosen.</summary>
        private bool JawsAllowed => _forcedEngine == null || _forcedEngine.Equals("Jaws", StringComparison.OrdinalIgnoreCase);
        public string EngineName { get; private set; }

        /// <summary>Most recent non-empty text spoken (after cleaning).</summary>
        public string LastSpoken => _history.Count > 0 ? _history[_history.Count - 1] : null;

        private float Now => (float)_clock.Elapsed.TotalSeconds;

        public void Initialize(IEnumerable<string> searchDirectories)
        {
            _jaws = new JawsSupport(searchDirectories);

            if (!UniversalSpeechNative.TryLoad(searchDirectories, out string error))
            {
                Available = false;
                ModLog.Error("Universal Speech could not be loaded: " + error +
                             ". Announcements will only be written to the log unless JAWS can be reached directly. See docs/TROUBLESHOOTING.md.");
                return;
            }

            try
            {
                UniversalSpeechNative.speechSetValue(UniversalSpeechNative.SP_ENABLE_NATIVE_SPEECH, ModConfig.UseSapiFallback.Value ? 1 : 0);
                EngineName = UniversalSpeechNative.CurrentEngineName();
                Available = true;
                ModLog.Info($"Universal Speech loaded from {UniversalSpeechNative.LoadedPath}. Active engine: {EngineName ?? "none detected yet"}.");
                ApplyRate();
                // JAWS routes are chosen on the first game frame (QueryEngine), not during plugin load,
                // so nothing touches COM before the game has finished initialising.
            }
            catch (Exception ex)
            {
                Available = false;
                ModLog.Error("Universal Speech loaded but a call failed: " + ex.Message);
            }
        }

        /// <summary>
        /// Speak text.
        /// </summary>
        /// <param name="text">Raw or rich text; it is cleaned before speaking.</param>
        /// <param name="interrupt">Interrupt current speech (true for responses to key presses).</param>
        /// <param name="important">Important content (dialogue, report text, notifications).</param>
        /// <param name="automatic">Not caused by a key press. Subject to the duplicate filter and the important-speech guard.</param>
        public void Say(string text, bool interrupt = true, bool important = false, bool automatic = false)
        {
            text = TextUtil.Clean(text);
            if (string.IsNullOrWhiteSpace(text)) return;

            float now = Now;
            if (automatic && ModConfig.FilterDuplicates.Value &&
                text == _lastText && now - _lastTime < ModConfig.DuplicateWindowSeconds.Value)
            {
                return;
            }

            // Do not let an automatic informational message cut off important speech that just began.
            if (automatic && !important && interrupt && now - _lastImportantTime < ImportantGuardSeconds)
                interrupt = false;

            _lastText = text;
            _lastTime = now;
            if (important) _lastImportantTime = now;
            _history.Add(text);
            if (_history.Count > HistoryLimit) _history.RemoveAt(0);

            ModLog.Debug("SAY" + (interrupt ? "!" : "+") + (important ? "*" : "") + ": " + text);

            // JAWS braille goes through JAWS's RunFunction("BrailleString(...)"). Running a JAWS function
            // right after SayString can silence the speech that was just started, which matches the
            // reported "JAWS accepts the text but says nothing". So for JAWS, braille is off unless
            // JawsBraille is enabled, and when enabled it is sent BEFORE the speech and only for
            // interrupting messages (so queued lines are never cut off).
            bool jawsEngine = JawsAllowed &&
                              ((_jaws != null && _jaws.Current != JawsSupport.Route.None && _jaws.Current != JawsSupport.Route.Unavailable)
                               || string.Equals(EngineName, "Jaws", StringComparison.OrdinalIgnoreCase));
            bool braille = ModConfig.BrailleOutput.Value && (!jawsEngine || (ModConfig.JawsBraille.Value && interrupt));

            if (JawsAllowed && _jaws != null && _jaws.HandlesSpeech)
            {
                if (braille) _jaws.Braille(text);
                _jaws.Say(text, interrupt);
                if (_jaws.HandlesSpeech) return;
                // The JAWS route failed on this call; fall through to Universal Speech.
            }
            if (!Available) return;

            try
            {
                if (braille && jawsEngine) UniversalSpeechNative.brailleDisplay(text);
                UniversalSpeechNative.speechSay(text, interrupt ? 1 : 0);
                if (braille && !jawsEngine) UniversalSpeechNative.brailleDisplay(text);
            }
            catch (Exception ex)
            {
                ModLog.Exception("speech", ex);
            }
        }

        /// <summary>Queue behind current speech.</summary>
        public void Queue(string text, bool important = false) => Say(text, interrupt: false, important: important, automatic: true);

        public void RepeatLast()
        {
            string last = LastSpoken;
            Say(last ?? "Nothing has been spoken yet.");
            // RepeatLast adds the same text again; remove the duplicate history entry.
            if (last != null && _history.Count >= 2) _history.RemoveAt(_history.Count - 1);
        }

        /// <summary>Returns the n-th most recent spoken item (0 = latest), or null.</summary>
        public string History(int back)
        {
            int i = _history.Count - 1 - back;
            return i >= 0 && i < _history.Count ? _history[i] : null;
        }

        public int HistoryCount => _history.Count;

        public void Stop()
        {
            if (JawsAllowed && _jaws != null && _jaws.HandlesSpeech) { _jaws.Stop(); return; }
            if (!Available) return;
            try { UniversalSpeechNative.speechStop(); }
            catch (Exception ex) { ModLog.Exception("speech-stop", ex); }
        }

        /// <summary>Asks Universal Speech which engine it is using right now (it re-detects periodically).</summary>
        private bool _forcedSapi;

        /// <summary>Mod turned off: release the JAWS connection so nothing of the mod stays attached to JAWS.</summary>
        public void Suspend()
        {
            try { _jaws?.Reset(); } catch { }
        }

        /// <summary>Mod turned on again: reconnect to the screen reader.</summary>
        public void Resume()
        {
            if (JawsAllowed) _jaws?.Watch(Available, forceReconnect: true);
            ApplyJawsFallback();
        }

        /// <summary>Checks JAWS about once per second (see JawsSupport.Watch).</summary>
        public void WatchJaws(bool forceReconnect)
        {
            if (_jaws == null || !JawsAllowed) return;
            var before = _jaws.Current;
            _jaws.Watch(Available, forceReconnect);
            ApplyJawsFallback();
            if (_jaws.Current != before) ModLog.Info("Speech engine is now: " + QueryEngineName());
        }

        /// <summary>Reconnect command: rebuilds the JAWS connection (if JAWS runs) and re-detects the engine.</summary>
        public void Reconnect()
        {
            try { if (Available) UniversalSpeechNative.speechSetValue(UniversalSpeechNative.SP_ENGINE, -1); } catch { }
            try { if (Available) UniversalSpeechNative.speechSetValue(UniversalSpeechNative.SP_ENABLE_NATIVE_SPEECH, ModConfig.UseSapiFallback.Value ? 1 : 0); } catch { }
            _forcedSapi = false;
            _forcedEngine = null; // reconnecting returns to automatic engine choice
            _jaws?.Watch(Available, forceReconnect: true);
            ApplyJawsFallback();
            Say("Speech reconnected: " + QueryEngineName() + ".");
        }

        private string QueryEngineName()
        {
            if (JawsAllowed && _jaws != null && _jaws.Current == JawsSupport.Route.Direct) return "Jaws (direct)";
            if (_jaws != null && _jaws.Current == JawsSupport.Route.Bridge) return "Jaws (32-bit bridge)";
            if (!Available) return "none";
            return UniversalSpeechNative.CurrentEngineName() ?? "none";
        }

        public string QueryEngine()
        {
            if (JawsAllowed) _jaws?.Update(Available);
            ApplyJawsFallback();
            if (JawsAllowed && _jaws != null && _jaws.Current == JawsSupport.Route.Direct) return "Jaws (direct)";
            if (JawsAllowed && _jaws != null && _jaws.Current == JawsSupport.Route.Bridge) return "Jaws (32-bit bridge)";
            if (!Available) return "none (Universal Speech not loaded)";
            string name = UniversalSpeechNative.CurrentEngineName();
            if (name != EngineName)
            {
                ModLog.Info("Speech engine is now: " + (name ?? "none"));
                EngineName = name;
            }
            return name ?? "none";
        }

        /// <summary>
        /// If JAWS is running but refuses speech on every route, Universal Speech would keep choosing JAWS
        /// and stay silent. In that case switch Universal Speech to the Windows voice (SAPI) so the player
        /// hears something, and switch back to automatic choice once JAWS is reachable or closed.
        /// </summary>
        private void ApplyJawsFallback()
        {
            if (!Available || _jaws == null || _forcedEngine != null) return;
            try
            {
                if (_jaws.Current == JawsSupport.Route.Unavailable && !_forcedSapi)
                {
                    for (int i = 0; i < 32; i++)
                    {
                        IntPtr p = UniversalSpeechNative.speechGetString(UniversalSpeechNative.SP_ENGINE + i);
                        if (p == IntPtr.Zero) break;
                        string name = System.Runtime.InteropServices.Marshal.PtrToStringUni(p);
                        if (name != null && name.StartsWith("SAPI", StringComparison.OrdinalIgnoreCase))
                        {
                            UniversalSpeechNative.speechSetValue(UniversalSpeechNative.SP_ENABLE_NATIVE_SPEECH, 1);
                            UniversalSpeechNative.speechSetValue(UniversalSpeechNative.SP_ENGINE, i);
                            _forcedSapi = true;
                            ModLog.Warn("JAWS refused speech on every route; using the Windows voice (" + name + ") instead.");
                            Say("JAWS is not accepting speech from the game, so the Windows voice is used. Please send the log file.", interrupt: true, important: true);
                            break;
                        }
                    }
                }
                else if (_forcedSapi && _jaws.Current != JawsSupport.Route.Unavailable)
                {
                    UniversalSpeechNative.speechSetValue(UniversalSpeechNative.SP_ENGINE, -1);
                    UniversalSpeechNative.speechSetValue(UniversalSpeechNative.SP_ENABLE_NATIVE_SPEECH, ModConfig.UseSapiFallback.Value ? 1 : 0);
                    _forcedSapi = false;
                    ModLog.Info("Automatic speech engine choice restored.");
                }
            }
            catch (Exception ex)
            {
                ModLog.Exception("jaws-fallback", ex);
            }
        }

        /// <summary>
        /// Shift+F11: cycle Automatic -> each speech engine that is running right now -> Automatic.
        /// Uses Universal Speech's own engine list and availability check (SP_ENGINE + n for names,
        /// SP_ENGINE_AVAILABLE + n for "running and working"), and SP_ENGINE to select one.
        /// The mod cannot start or stop screen readers; it only chooses among the running ones.
        /// </summary>
        public void CycleEngine()
        {
            if (!Available) { Say("Universal Speech is not loaded, so the speech engine cannot be changed."); return; }
            try
            {
                // Let SAPI count as available while listing.
                UniversalSpeechNative.speechSetValue(UniversalSpeechNative.SP_ENABLE_NATIVE_SPEECH, 1);
                var options = new List<(int index, string name)> { (-1, "Automatic") };
                var seen = new HashSet<string>();
                for (int i = 0; i < 16; i++)
                {
                    IntPtr p = UniversalSpeechNative.speechGetString(UniversalSpeechNative.SP_ENGINE + i);
                    if (p == IntPtr.Zero) break;
                    string name = System.Runtime.InteropServices.Marshal.PtrToStringUni(p);
                    if (string.IsNullOrEmpty(name) || !seen.Add(name)) break;
                    bool available = UniversalSpeechNative.speechGetValue(UniversalSpeechNative.SP_ENGINE_AVAILABLE + i) != 0;
                    if (available) options.Add((i, name));
                }

                int currentIndex = 0;
                if (_forcedEngine != null) currentIndex = Math.Max(0, options.FindIndex(o => o.name == _forcedEngine));
                var next = options[(currentIndex + 1) % options.Count];

                if (next.index < 0)
                {
                    _forcedEngine = null;
                    _forcedSapi = false;
                    UniversalSpeechNative.speechSetValue(UniversalSpeechNative.SP_ENGINE, -1);
                    UniversalSpeechNative.speechSetValue(UniversalSpeechNative.SP_ENABLE_NATIVE_SPEECH, ModConfig.UseSapiFallback.Value ? 1 : 0);
                    _jaws?.Watch(Available, forceReconnect: true);
                    ApplyJawsFallback();
                    ModLog.Info("Speech engine: automatic (" + QueryEngineName() + ").");
                    Say("Automatic speech engine: " + QueryEngineName() + ".");
                    return;
                }

                _forcedEngine = next.name;
                if (!JawsAllowed) _jaws?.Reset(); // another engine chosen: the mod lets go of JAWS
                else _jaws?.Watch(Available, forceReconnect: true);
                if (!next.name.StartsWith("SAPI", StringComparison.OrdinalIgnoreCase))
                    UniversalSpeechNative.speechSetValue(UniversalSpeechNative.SP_ENABLE_NATIVE_SPEECH, ModConfig.UseSapiFallback.Value ? 1 : 0);
                UniversalSpeechNative.speechSetValue(UniversalSpeechNative.SP_ENGINE, next.index);
                ModLog.Info("Speech engine chosen by the player: " + next.name + ".");
                Say(next.name + ". " + (options.Count - 1) + (options.Count == 2 ? " engine is" : " engines are") + " running. Shift F11 switches again.");
            }
            catch (Exception ex)
            {
                ModLog.Exception("engine-cycle", ex);
                Say("Could not change the speech engine.");
            }
        }

        public bool IsRateSupported()
        {
            if (!Available) return false;
            try { return UniversalSpeechNative.speechGetValue(UniversalSpeechNative.SP_RATE_SUPPORTED) != 0; }
            catch { return false; }
        }

        public void ApplyRate()
        {
            int percent = ModConfig.SpeechRatePercent.Value;
            if (percent < 0 || !IsRateSupported()) return;
            try
            {
                int min = UniversalSpeechNative.speechGetValue(UniversalSpeechNative.SP_RATE_MIN);
                int max = UniversalSpeechNative.speechGetValue(UniversalSpeechNative.SP_RATE_MAX);
                if (max <= min) return;
                int value = min + (int)Math.Round((max - min) * Math.Clamp(percent, 0, 100) / 100.0);
                UniversalSpeechNative.speechSetValue(UniversalSpeechNative.SP_RATE, value);
                ModLog.Info($"Speech rate set to {value} (range {min}..{max}).");
            }
            catch (Exception ex)
            {
                ModLog.Exception("speech-rate", ex);
            }
        }

        public void ApplyNativeSpeechSetting()
        {
            if (!Available) return;
            try { UniversalSpeechNative.speechSetValue(UniversalSpeechNative.SP_ENABLE_NATIVE_SPEECH, ModConfig.UseSapiFallback.Value ? 1 : 0); }
            catch (Exception ex) { ModLog.Exception("speech-native", ex); }
        }
    }
}
