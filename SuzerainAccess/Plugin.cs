using System;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using SuzerainAccess.Config;
using SuzerainAccess.Core;

namespace SuzerainAccess
{
    /// <summary>
    /// BepInEx 6 IL2CPP entry point for Suzerain Access, a screen-reader accessibility mod for the
    /// Steam version of Suzerain. Built against the exact game build identified in VersionCheck.
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BasePlugin
    {
        public const string Guid = "com.suzerainaccess.mod";
        public const string Name = "Suzerain Access";
        public const string Version = "1.1.0";

        public override void Load()
        {
            ModLog.Init(Log);
            try
            {
                ModConfig.Init(Config);
                ModLog.DebugEnabled = ModConfig.DebugLogging.Value;

                // Read the BepInEx version from its assembly attributes (avoids a compile-time dependency on SemanticVersioning).
                var bepAsm = typeof(Paths).Assembly;
                string bepVersion = bepAsm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                                    ?? bepAsm.GetName().Version?.ToString() ?? "unknown";
                ModLog.Info($"{Name} {Version} starting. BepInEx {bepVersion}. Game folder: {Paths.GameRootPath}");
                VersionCheck.Start(Paths.GameRootPath);

                string pluginDir = Path.GetDirectoryName(typeof(Plugin).Assembly.Location);
                ModLog.Info("Plugin loaded from: " + typeof(Plugin).Assembly.Location);
                CheckForDuplicateCopies();
                AccessibilityManager.Create(pluginDir);
                AddComponent<AccessibilityRunner>();
                ModLog.Info("Accessibility runner attached.");
            }
            catch (Exception ex)
            {
                ModLog.Error("Suzerain Access failed to initialize: " + ex);
            }
        }

        /// <summary>
        /// Two copies of SuzerainAccess.dll under BepInEx\plugins (for example one copied by hand and one
        /// deployed by the build into plugins\SuzerainAccess) mean BepInEx may load an older one.
        /// </summary>
        private static void CheckForDuplicateCopies()
        {
            try
            {
                var copies = Directory.GetFiles(Paths.PluginPath, "SuzerainAccess.dll", SearchOption.AllDirectories);
                if (copies.Length > 1)
                    ModLog.Warn("More than one SuzerainAccess.dll found under BepInEx\\plugins. Keep only one: " + string.Join("; ", copies));
            }
            catch (Exception ex)
            {
                ModLog.Warn("Could not check for duplicate plugin copies: " + ex.Message);
            }
        }
    }
}
