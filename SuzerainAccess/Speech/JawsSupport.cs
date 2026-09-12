using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using SuzerainAccess.Config;
using SuzerainAccess.Core;

namespace SuzerainAccess.Speech
{
    /// <summary>
    /// JAWS does not use a DLL next to the game (unlike NVDA). It provides the COM object
    /// "FreedomSci.JawsApi" (FSAPI), which JAWS registers when it is installed; Universal Speech uses it
    /// through COM. Universal Speech reports success even when JAWS cannot be reached, so JAWS could stay
    /// silent. This class makes JAWS reliable with three routes, tried in order:
    ///
    ///  1. Universal Speech itself (its exported jfwLoad() tells whether it could create the JAWS object).
    ///  2. Direct: the mod creates FreedomSci.JawsApi itself through .NET COM interop (64-bit).
    ///  3. Bridge: JawsBridge32.exe, a tiny 32-bit helper that creates the JAWS object in a 32-bit process,
    ///     for installations that only register the JAWS API for 32-bit programs.
    ///
    /// JAWS is detected exactly as Universal Speech does it: a window of class "JFWUI2" exists.
    /// Every step is written to the BepInEx log.
    /// </summary>
    internal sealed class JawsSupport
    {
        public enum Route { None, UniversalSpeech, Direct, Bridge, Unavailable }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindowW(string className, string windowName);

        [DllImport("UniversalSpeech", CallingConvention = CallingConvention.Cdecl, EntryPoint = "jfwLoad")]
        private static extern int UniversalSpeech_jfwLoad();

        // Universal Speech's own JAWS function; unlike speechSay it returns JAWS's real answer.
        [DllImport("UniversalSpeech", CallingConvention = CallingConvention.Cdecl, EntryPoint = "jfwSayW", CharSet = CharSet.Unicode)]
        private static extern int UniversalSpeech_jfwSayW([MarshalAs(UnmanagedType.LPWStr)] string text, int interrupt);

        private string _testPhrase = "Suzerain Access connected to JAWS.";
        private IntPtr _jawsWindow;
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private double _retryUnavailableAt;
        private int _falseAnswers;

        private readonly List<string> _directories;
        private object _api;
        private Type _apiType;
        private Process _bridge;
        private StreamWriter _bridgeInput;

        public Route Current { get; private set; } = Route.None;

        /// <param name="directories">Where to look for JawsBridge32.exe: the plugin folder and the game folder.
        /// The folder UniversalSpeech.dll was loaded from is added automatically.</param>
        public JawsSupport(IEnumerable<string> directories)
        {
            _directories = new List<string>(directories);
        }

        private string FindBridge(out string searched)
        {
            var dirs = new List<string>();
            if (!string.IsNullOrEmpty(UniversalSpeechNative.LoadedPath)) dirs.Add(Path.GetDirectoryName(UniversalSpeechNative.LoadedPath));
            dirs.AddRange(_directories);
            var tried = new List<string>();
            foreach (var d in dirs)
            {
                if (string.IsNullOrEmpty(d)) continue;
                string exe = Path.Combine(d, "JawsBridge32.exe");
                tried.Add(exe);
                if (File.Exists(exe)) { searched = null; return exe; }
            }
            searched = string.Join("; ", tried);
            return null;
        }

        public static bool IsJawsRunning()
        {
            try { return FindWindowW("JFWUI2", null) != IntPtr.Zero; }
            catch { return false; }
        }

        /// <summary>Chooses a route when JAWS is running. Cheap to call repeatedly.</summary>
        public void Update(bool universalSpeechAvailable)
        {
            if (!IsJawsRunning())
            {
                if (Current != Route.None)
                {
                    ModLog.Info("JAWS is no longer running.");
                    StopBridge();
                    Current = Route.None;
                }
                return;
            }
            if (Current != Route.None && Current != Route.Unavailable)
            {
                if (Current == Route.Bridge && (_bridge == null || _bridge.HasExited))
                {
                    ModLog.Warn("JAWS bridge exited; choosing a route again.");
                    Current = Route.None;
                }
                else return;
            }
            if (Current == Route.Unavailable) return;
            if (_jawsWindow == IntPtr.Zero) { try { _jawsWindow = FindWindowW("JFWUI2", null); } catch { } }

            ModLog.Info("JAWS is running (window JFWUI2 found). Choosing how to reach it...");
            _retryUnavailableAt = _clock.Elapsed.TotalSeconds + 30; // used if every route fails below
            // Each route must prove that JAWS accepts speech: SayString has to answer "true" for a test
            // phrase. Creating the JAWS object alone is not enough (seen in practice: object created,
            // but nothing spoken).

            var choice = ModConfig.JawsRoute.Value;
            ModLog.Info("  JawsRoute setting: " + choice);
            if (choice == JawsRouteChoice.Off)
            {
                ModLog.Info("  JAWS output is switched off in the configuration (JawsRoute = Off).");
                Current = Route.Unavailable;
                return;
            }

            string directError = null;
            if ((choice == JawsRouteChoice.Auto || choice == JawsRouteChoice.Direct) && TryDirect(out directError))
            {
                bool ok = DirectSay(_testPhrase, true, out string sayError);
                ModLog.Info("  Route 1, direct COM (FreedomSci.JawsApi, 64-bit): object created; SayString test " + (ok ? "accepted" : "NOT accepted" + sayError));
                if (ok) { Current = Route.Direct; _falseAnswers = 0; return; }
            }
            else if (choice == JawsRouteChoice.Auto || choice == JawsRouteChoice.Direct) ModLog.Info("  Route 1, direct COM (64-bit): failed - " + directError);

            string bridgeError = null;
            if ((choice == JawsRouteChoice.Auto || choice == JawsRouteChoice.Bridge) && TryBridge(out bridgeError))
            {
                string answer = BridgeTest(_testPhrase);
                bool ok = answer != null && answer.StartsWith("OK 1", StringComparison.Ordinal);
                ModLog.Info("  Route 2, JawsBridge32.exe (32-bit): started; SayString test answer: " + (answer ?? "none"));
                if (ok) { Current = Route.Bridge; return; }
                StopBridge();
            }
            else if (choice == JawsRouteChoice.Auto || choice == JawsRouteChoice.Bridge) ModLog.Info("  Route 2, JawsBridge32.exe (32-bit): failed - " + bridgeError);

            if ((choice == JawsRouteChoice.Auto || choice == JawsRouteChoice.UniversalSpeech) && universalSpeechAvailable)
            {
                try
                {
                    int loaded = UniversalSpeech_jfwLoad();
                    int said = loaded != 0 ? UniversalSpeech_jfwSayW(_testPhrase, 1) : 0;
                    ModLog.Info("  Route 3, Universal Speech: jfwLoad " + (loaded != 0 ? "success" : "failed") + "; jfwSayW test " + (said != 0 ? "accepted" : "NOT accepted"));
                    if (said != 0) { Current = Route.UniversalSpeech; return; }
                }
                catch (Exception ex)
                {
                    ModLog.Info("  Route 3, Universal Speech JAWS functions not callable: " + ex.GetType().Name);
                }
            }

            ModLog.Error("JAWS is running but its speech API could not be reached by any route. Is JAWS fully installed (not a portable copy)? See docs/TROUBLESHOOTING.md.");
            Current = Route.Unavailable;
        }

        /// <summary>
        /// Called about once per second. Detects JAWS being started, closed or restarted (its JFWUI2 window
        /// handle changes) and reconnects; <paramref name="forceReconnect"/> rebuilds the connection, for
        /// example when the game window gets the focus back. A failed state is retried every 30 seconds.
        /// </summary>
        public void Watch(bool universalSpeechAvailable, bool forceReconnect)
        {
            IntPtr window = IntPtr.Zero;
            try { window = FindWindowW("JFWUI2", null); } catch { }
            if (window != _jawsWindow)
            {
                if (_jawsWindow != IntPtr.Zero && window != IntPtr.Zero) ModLog.Info("JAWS was restarted; reconnecting.");
                _jawsWindow = window;
                Reset();
                _testPhrase = "JAWS connected.";
            }
            else if (forceReconnect && window != IntPtr.Zero)
            {
                ModLog.Info("Game window has the focus again; reconnecting to JAWS.");
                Reset();
                _testPhrase = "JAWS connected.";
            }
            else if (Current == Route.Unavailable && ModConfig.JawsRoute.Value != JawsRouteChoice.Off && _clock.Elapsed.TotalSeconds >= _retryUnavailableAt)
            {
                Reset();
            }
            Update(universalSpeechAvailable);
        }

        /// <summary>Drops the current connection so the next Update chooses and tests a route again.</summary>
        public void Reset()
        {
            StopBridge();
            try { if (_api != null && Marshal.IsComObject(_api)) Marshal.ReleaseComObject(_api); } catch { }
            _api = null;
            _apiType = null;
            _falseAnswers = 0;
            Current = Route.None;
        }

        /// <summary>True when the mod itself must send speech to JAWS (routes 2 and 3).</summary>
        public bool HandlesSpeech => Current == Route.Direct || Current == Route.Bridge;

        public void Say(string text, bool interrupt)
        {
            try
            {
                if (Current == Route.Direct)
                {
                    bool accepted = DirectSay(text, interrupt, out string err);
                    ModLog.Debug("JAWS SayString -> " + (accepted ? "accepted" : "NOT accepted" + err));
                    if (!accepted)
                    {
                        _falseAnswers++;
                        if (_falseAnswers <= 3) ModLog.Warn("JAWS SayString did not accept the text" + err);
                    }
                    else _falseAnswers = 0;
                }
                else if (Current == Route.Bridge)
                    Send((interrupt ? "S1" : "S0") + OneLine(text));
            }
            catch (Exception ex)
            {
                ModLog.Exception("jaws-say", ex);
                Current = Route.None; // choose again next time
            }
        }

        public void Braille(string text)
        {
            try
            {
                if (Current == Route.Direct)
                {
                    var sb = new StringBuilder(text.Length);
                    foreach (char c in text) sb.Append(c == '"' || c == '\\' || c < ' ' ? ' ' : c);
                    _apiType.InvokeMember("RunFunction", BindingFlags.InvokeMethod, null, _api, new object[] { "BrailleString(\"" + sb + "\")" });
                }
                else if (Current == Route.Bridge)
                {
                    Send("B" + OneLine(text));
                }
            }
            catch (Exception ex)
            {
                ModLog.Exception("jaws-braille", ex);
            }
        }

        public void Stop()
        {
            try
            {
                if (Current == Route.Direct) _apiType.InvokeMember("StopSpeech", BindingFlags.InvokeMethod, null, _api, null);
                else if (Current == Route.Bridge) Send("X");
            }
            catch (Exception ex)
            {
                ModLog.Exception("jaws-stop", ex);
            }
        }

        // ------------------------------------------------------------------ route 2

        private bool TryDirect(out string error)
        {
            error = null;
            try
            {
                // The .NET runtime initialises COM for this thread if needed; the game's own COM setup is left alone.
                _apiType = Type.GetTypeFromProgID("FreedomSci.JawsApi", false) ?? Type.GetTypeFromProgID("JFWApi", false);
                if (_apiType == null) { error = "FreedomSci.JawsApi is not registered for 64-bit programs"; return false; }
                _api = Activator.CreateInstance(_apiType);
                if (_api == null) { error = "object could not be created"; return false; }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.GetType().Name + ": " + ex.Message;
                _api = null;
                return false;
            }
        }

        private bool DirectSay(string text, bool interrupt, out string error)
        {
            error = "";
            try
            {
                object result = _apiType.InvokeMember("SayString", BindingFlags.InvokeMethod, null, _api, new object[] { text, interrupt });
                if (result is bool b) return b;
                if (result == null) return true; // method returned nothing: treat as accepted
                return Convert.ToInt32(result) != 0;
            }
            catch (Exception ex)
            {
                error = " (" + ex.GetType().Name + ": " + ex.Message + ")";
                return false;
            }
        }

        private string BridgeTest(string text)
        {
            try
            {
                Send("T" + OneLine(text));
                var read = _bridge.StandardOutput.ReadLineAsync();
                return read.Wait(5000) ? read.Result : null;
            }
            catch { return null; }
        }

        // ------------------------------------------------------------------ route 3

        private bool TryBridge(out string error)
        {
            error = null;
            string exe = FindBridge(out string searched);
            if (exe == null) { error = "JawsBridge32.exe not found. Looked for: " + searched; return false; }
            try
            {
                var psi = new ProcessStartInfo(exe)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    WorkingDirectory = Path.GetDirectoryName(exe)
                };
                _bridge = Process.Start(psi);
                if (_bridge == null) { error = "process did not start"; return false; }

                var readLine = _bridge.StandardOutput.ReadLineAsync();
                if (!readLine.Wait(5000)) { error = "no answer within 5 seconds"; StopBridge(); return false; }
                string answer = readLine.Result ?? "";
                if (!answer.StartsWith("READY", StringComparison.Ordinal))
                {
                    error = "helper reported: " + answer + (answer.Contains("800401f3") ? " (JAWS API not registered for 32-bit programs either)" : "");
                    StopBridge();
                    return false;
                }
                _bridgeInput = new StreamWriter(_bridge.StandardInput.BaseStream, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
                return true;
            }
            catch (Exception ex)
            {
                error = ex.GetType().Name + ": " + ex.Message;
                StopBridge();
                return false;
            }
        }

        private void Send(string line)
        {
            if (_bridgeInput == null || _bridge == null || _bridge.HasExited) { Current = Route.None; return; }
            _bridgeInput.WriteLine(line);
        }

        private static string OneLine(string text) => text.Replace('\r', ' ').Replace('\n', ' ');

        private void StopBridge()
        {
            try { _bridgeInput?.Dispose(); } catch { }
            try { if (_bridge != null && !_bridge.HasExited) _bridge.Kill(); } catch { }
            _bridgeInput = null;
            _bridge = null;
        }
    }
}
