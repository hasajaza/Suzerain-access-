using BepInEx.Configuration;

namespace SuzerainAccess.Config
{
    public enum Verbosity
    {
        /// <summary>Names and values only.</summary>
        Low = 0,
        /// <summary>Names, roles, states and list positions.</summary>
        Normal = 1,
        /// <summary>Everything, including usage hints.</summary>
        High = 2
    }

    public enum EnterMode
    {
        /// <summary>
        /// Let the game's own UI "Submit" handle Enter when the game's selected object is the focused
        /// control and it has a submit handler; otherwise the mod activates the control itself.
        /// Prevents double activation.
        /// </summary>
        Auto = 0,
        /// <summary>The mod always activates the focused control on Enter.</summary>
        AlwaysMod = 1
    }

    /// <summary>
    /// All settings, persisted by BepInEx in BepInEx\config\com.suzerainaccess.mod.cfg.
    /// The in-game settings menu (F9 by default) edits the same entries.
    /// </summary>
    public enum JawsRouteChoice
    {
        /// <summary>Try direct COM, then the 32-bit bridge, then Universal Speech.</summary>
        Auto = 0,
        Direct = 1,
        Bridge = 2,
        UniversalSpeech = 3,
        /// <summary>Do not speak through JAWS; use the Windows voice.</summary>
        Off = 4
    }

    internal static class ModConfig
    {
        public static ConfigFile File { get; private set; }

        // Speech
        public static ConfigEntry<Verbosity> Verbosity;
        public static ConfigEntry<int> SpeechRatePercent;
        public static ConfigEntry<bool> UseSapiFallback;
        public static ConfigEntry<bool> BrailleOutput;
        public static ConfigEntry<bool> JawsBraille;
        public static ConfigEntry<JawsRouteChoice> JawsRoute;
        public static ConfigEntry<bool> JawsReconnectOnFocus;
        public static ConfigEntry<bool> FilterDuplicates;
        public static ConfigEntry<float> DuplicateWindowSeconds;

        // Reading
        public static ConfigEntry<bool> AutoReadDialogue;
        public static ConfigEntry<bool> ReadDialogueHistoryOnReturn;
        public static ConfigEntry<bool> AutoReadScreens;
        public static ConfigEntry<bool> AutoReadContent;
        public static ConfigEntry<bool> AnnounceRoles;
        public static ConfigEntry<bool> AnnouncePositions;
        public static ConfigEntry<bool> DetailedNumbers;
        public static ConfigEntry<bool> IgnoreDecorativeText;
        public static ConfigEntry<bool> AnnounceStatChanges;
        public static ConfigEntry<bool> AnnounceNotifications;
        public static ConfigEntry<bool> AnnounceTurnChanges;
        public static ConfigEntry<bool> AnnounceNewEvents;
        public static ConfigEntry<bool> AutoReadNewText;
        public static ConfigEntry<bool> DescribeColors;
        public static ConfigEntry<bool> IncludeDisabledControls;

        // Behaviour
        public static ConfigEntry<bool> SyncGameSelection;
        public static ConfigEntry<EnterMode> EnterActivation;
        public static ConfigEntry<bool> EchoTyping;
        public static ConfigEntry<bool> MapHoverTokens;
        public static ConfigEntry<bool> DebugLogging;
        public static ConfigEntry<int> DiagnosticKeyPauseAfterFocusSeconds;

        public static void Init(ConfigFile file)
        {
            File = file;

            Verbosity = file.Bind("Speech", "Verbosity", Config.Verbosity.Normal,
                "Low: names and values only. Normal: adds roles, states and list positions. High: adds usage hints.");
            SpeechRatePercent = file.Bind("Speech", "SpeechRatePercent", -1,
                "Speech rate 0-100 for engines that support it (usually only SAPI). -1 leaves the rate unchanged. Screen readers use their own rate.");
            UseSapiFallback = file.Bind("Speech", "UseSapiFallback", true,
                "If no screen reader is running, let Universal Speech fall back to Windows SAPI.");
            BrailleOutput = file.Bind("Speech", "BrailleOutput", true,
                "Also send announcements to the braille display (screen readers that support it).");
            JawsRoute = file.Bind("Speech", "JawsRoute", JawsRouteChoice.Auto,
                "How the mod speaks through JAWS: Auto, Direct (COM inside the game), Bridge (separate 32-bit helper JawsBridge32.exe), UniversalSpeech, or Off (use the Windows voice).");
            JawsReconnectOnFocus = file.Bind("Speech", "JawsReconnectOnFocus", false,
                "Reconnect to JAWS every time the game window gets the focus back (says 'JAWS connected'). Not needed once JAWS Sleep Mode for Unity Player is disabled; see docs/TROUBLESHOOTING.md.");
            JawsBraille = file.Bind("Speech", "JawsBraille", false,
                "Send braille to JAWS as well. Off by default: JAWS's braille function can cut off the speech that was just sent.");
            FilterDuplicates = file.Bind("Speech", "FilterDuplicates", true,
                "Do not repeat identical automatic announcements that occur within the duplicate window.");
            DuplicateWindowSeconds = file.Bind("Speech", "DuplicateWindowSeconds", 1.0f,
                "Duplicate filter window in seconds.");

            AutoReadDialogue = file.Bind("Reading", "AutoReadDialogue", true,
                "Automatically speak new dialogue lines, narration and the list of responses.");
            ReadDialogueHistoryOnReturn = file.Bind("Reading", "ReadLastLineWhenConversationReopens", true,
                "When a conversation becomes visible again, speak its most recent line.");
            AutoReadScreens = file.Bind("Reading", "AutoReadScreens", true,
                "Announce the name of a screen/panel when it opens and move focus into it.");
            AutoReadContent = file.Bind("Reading", "AutoReadContent", true,
                "Automatically read the main content of reports, decisions, confirmations and tutorials when they open.");
            AnnounceRoles = file.Bind("Reading", "AnnounceRoles", true,
                "Say the control type (button, check box, slider...).");
            AnnouncePositions = file.Bind("Reading", "AnnouncePositions", true,
                "Say the position in the list (for example 2 of 5).");
            DetailedNumbers = file.Bind("Reading", "DetailedNumbers", true,
                "Speak signed values in words (plus 2, minus 3) and include minimum/maximum and modifier details.");
            IgnoreDecorativeText = file.Bind("Reading", "IgnoreDecorativeText", true,
                "Skip single-symbol texts and duplicated text layers when reading screens.");
            AnnounceStatChanges = file.Bind("Reading", "AnnounceStatChanges", true,
                "Announce when a statistic in the top statistics bar changes value.");
            AnnounceNotifications = file.Bind("Reading", "AnnounceNotifications", true,
                "Announce game notifications when they appear.");
            AnnounceTurnChanges = file.Bind("Reading", "AnnounceTurnChanges", true,
                "Announce when the turn number changes.");
            AnnounceNewEvents = file.Bind("Reading", "AnnounceNewEvents", true,
                "Announce when a map location gets an event marker, and when the Continue button becomes available.");
            AutoReadNewText = file.Bind("Reading", "AutoReadNewText", true,
                "After you activate something, read the text that appeared (for example an opened codex entry or expanded article) and continue reading from there.");
            DescribeColors = file.Bind("Reading", "DescribeColors", true,
                "Describe gameplay colour coding (for example a status shown in red).");
            IncludeDisabledControls = file.Bind("Reading", "IncludeDisabledControls", true,
                "Include disabled controls in navigation and announce them as unavailable.");

            SyncGameSelection = file.Bind("Behaviour", "SyncGameSelection", true,
                "Move the game's own UI selection to the focused control, so tooltips and the game's keyboard/gamepad logic follow the accessibility focus.");
            EnterActivation = file.Bind("Behaviour", "EnterActivation", EnterMode.Auto,
                "Auto: avoid double activation by letting the game handle Enter when it will. AlwaysMod: the mod always activates on Enter (use if Enter sometimes does nothing).");
            EchoTyping = file.Bind("Behaviour", "EchoTyping", true,
                "Speak characters typed into text fields (for example save names).");
            MapHoverTokens = file.Bind("Behaviour", "MapHoverTokens", true,
                "When browsing the map, also highlight the location in the game (same as mouse hover).");
            DiagnosticKeyPauseAfterFocusSeconds = file.Bind("Behaviour", "DiagnosticKeyPauseAfterFocusSeconds", 0,
                "Diagnostic only. After the game window gets the focus back, the mod ignores ALL keys for this many seconds (0 = off). Used to test whether the mod's key reading affects screen reader shortcuts.");
            DebugLogging = file.Bind("Behaviour", "DebugLogging", false,
                "Write detailed diagnostic lines to BepInEx\\LogOutput.log.");
        }

        public static bool AtLeast(Verbosity v) => Verbosity.Value >= v;
    }
}
