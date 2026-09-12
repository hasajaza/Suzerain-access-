using System;
using UnityEngine;

namespace SuzerainAccess.Core
{
    /// <summary>
    /// MonoBehaviour injected into IL2CPP (BasePlugin.AddComponent registers it with Il2CppInterop's
    /// ClassInjector and attaches it to a persistent, hidden GameObject). Its only job is to give the
    /// mod an Update() call every frame on Unity's main thread.
    /// </summary>
    public class AccessibilityRunner : MonoBehaviour
    {
        public AccessibilityRunner(IntPtr ptr) : base(ptr) { }

        private void Update()
        {
            try
            {
                AccessibilityManager.Instance?.Tick();
            }
            catch (Exception ex)
            {
                ModLog.Exception("runner", ex);
            }
        }
    }
}
