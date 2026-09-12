using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using SuzerainAccess.Core;

namespace SuzerainAccess.Speech
{
    /// <summary>
    /// P/Invoke bindings for Universal Speech (https://github.com/qtnc/UniversalSpeech), MIT licence.
    /// Signatures and parameter constants are taken from include/UniversalSpeech.h of that project:
    ///   int speechSay(const wchar_t* str, int interrupt);
    ///   int brailleDisplay(const wchar_t* str);
    ///   int speechStop(void);
    ///   int speechGetValue(int what);
    ///   int speechSetValue(int what, int value);
    ///   const wchar_t* speechGetString(int what);
    /// The official 1.0.0 binary is 32-bit and cannot load into 64-bit Suzerain, so this project ships a
    /// 64-bit build compiled from the official source (see native\BUILD-NOTES.md).
    /// </summary>
    internal static class UniversalSpeechNative
    {
        private const string Lib = "UniversalSpeech";

        // enum speechparam (UniversalSpeech.h): values are declared sequentially from 0.
        public const int SP_VOLUME = 0, SP_VOLUME_MAX = 1, SP_VOLUME_MIN = 2, SP_VOLUME_SUPPORTED = 3;
        public const int SP_RATE = 4, SP_RATE_MAX = 5, SP_RATE_MIN = 6, SP_RATE_SUPPORTED = 7;
        public const int SP_ENABLE_NATIVE_SPEECH = 0xFFFF;
        public const int SP_ENGINE = 0x40000;
        public const int SP_ENGINE_AVAILABLE = 0x50000;

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        public static extern int speechSay([MarshalAs(UnmanagedType.LPWStr)] string str, int interrupt);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        public static extern int brailleDisplay([MarshalAs(UnmanagedType.LPWStr)] string str);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int speechStop();

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int speechGetValue(int what);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int speechSetValue(int what, int value);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr speechGetString(int what);

        private static IntPtr _handle = IntPtr.Zero;
        private static bool _resolverInstalled;

        public static string LoadedPath { get; private set; }

        /// <summary>
        /// Loads UniversalSpeech.dll from an explicit location (plugin folder first, then the game folder)
        /// and routes the DllImports above to it. Explicit loading is used because the default native
        /// search path of the game process does not include BepInEx\plugins.
        /// UniversalSpeech itself looks for nvdaControllerClient*.dll next to its own DLL.
        /// </summary>
        public static bool TryLoad(IEnumerable<string> directories, out string error)
        {
            error = null;
            if (_handle != IntPtr.Zero) return true;

            var tried = new List<string>();
            foreach (var dir in directories)
            {
                if (string.IsNullOrEmpty(dir)) continue;
                string path = Path.Combine(dir, "UniversalSpeech.dll");
                tried.Add(path);
                if (!File.Exists(path)) continue;
                if (NativeLibrary.TryLoad(path, out _handle))
                {
                    LoadedPath = path;
                    break;
                }
                error = "Found but could not load " + path + " (is it a 64-bit DLL?)";
            }

            if (_handle == IntPtr.Zero)
            {
                if (error == null) error = "UniversalSpeech.dll not found. Looked in: " + string.Join("; ", tried);
                return false;
            }

            if (!_resolverInstalled)
            {
                NativeLibrary.SetDllImportResolver(typeof(UniversalSpeechNative).Assembly, Resolve);
                _resolverInstalled = true;
            }
            return true;
        }

        private static IntPtr Resolve(string name, Assembly assembly, DllImportSearchPath? path)
        {
            if (_handle != IntPtr.Zero && name.StartsWith(Lib, StringComparison.OrdinalIgnoreCase)) return _handle;
            return IntPtr.Zero;
        }

        /// <summary>Name of the engine Universal Speech is currently using, or null.</summary>
        public static string CurrentEngineName()
        {
            try
            {
                int index = speechGetValue(SP_ENGINE);
                if (index < 0) return null;
                IntPtr p = speechGetString(SP_ENGINE + index);
                return p == IntPtr.Zero ? null : Marshal.PtrToStringUni(p);
            }
            catch
            {
                return null;
            }
        }
    }
}
