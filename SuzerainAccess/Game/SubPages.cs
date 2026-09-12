using System;
using System.Collections.Generic;
using SuzerainAccess.UI;
using UnityEngine;

namespace SuzerainAccess.Game
{
    /// <summary>
    /// Several Suzerain panels contain pages (the main menu contains Options, Load and Collections;
    /// the journal contains Reports, Decisions and Notepad, ...). Each page is its own MonoBehaviour
    /// class (all verified in Assembly-CSharp), so the visible page is identified by component type.
    /// </summary>
    internal static class SubPages
    {
        private static readonly List<(string name, Func<Transform, MonoBehaviour> find)> Pages = new List<(string, Func<Transform, MonoBehaviour>)>
        {
            ("Options", r => First<OptionsPage>(r)),
            ("Load game", r => First<LoadGamePage>(r)),
            ("Save game", r => First<SaveGamePage>(r)),
            ("Collection item", r => First<CollectionsItemDetailsPage>(r)),
            ("Collections", r => First<CollectionsPage>(r)),
            ("Controls", r => First<GamepadControlsPage>(r)),
            ("Codex entry", r => First<CodexEntryPage>(r)),
            ("Codex topics", r => First<CodexMenuPage>(r)),
            ("Decree details", r => First<DecreeDetailsPage>(r)),
            ("Decree list", r => First<DecreeListPage>(r)),
            ("Reports", r => First<JournalReportsPage>(r)),
            ("Decisions", r => First<JournalDecisionsPage>(r)),
            ("Notepad", r => First<JournalNotepadPage>(r)),
            ("Advisors", r => First<ConnectionsAdvisorsPage>(r)),
            ("Composition", r => First<ConnectionsCompositionPage>(r)),
            ("Factions", r => First<ConnectionsFactionsPage>(r)),
            ("Store", r => First<StorePage>(r)),
            ("Profile", r => First<ProfilePage>(r)),
            ("Login", r => First<LoginPage>(r)),
            ("Create account", r => First<CreateAccountPage>(r)),
        };

        /// <summary>
        /// Name of the visible page of a region, or null. For the main menu and the pause menu the game's
        /// own page-state methods are used (MainMenuPanel.IsMainPageShowing / IsOptionsPageShowing /
        /// IsCollectionsPageShowing, EscapeMenuPanel.IsOptionsPageShowing / IsCollectionsPageShowing):
        /// looking for active page objects reported "Options" while the main page was showing.
        /// </summary>
        public static string Detect(Region region)
        {
            if (region == null || !UiUtil.Alive(region.Root)) return null;
            try
            {
                var panels = ScreenTracker.GetPanels();
                if (panels != null && region.Id == "MainMenuPanel")
                {
                    var m = panels.MainMenuPanel;
                    if (m.IsOptionsPageShowing()) return "Options";
                    if (m.IsCollectionsPageShowing()) return "Collections";
                    // Other pages (Load, Credits...): searching for page objects announced "Load game page" on the
                    // Credits page, so no page name is guessed here; the first control is the page header.
                    return null;
                }
                if (panels != null && region.Id == "EscapeMenuPanel")
                {
                    var m = panels.EscapeMenuPanel;
                    if (m.IsOptionsPageShowing()) return "Options";
                    if (m.IsCollectionsPageShowing()) return "Collections";
                    return null;
                }
            }
            catch { }
            return DetectGeneric(region.Root, skipMenuPages: false);
        }

        private static string DetectGeneric(Transform root, bool skipMenuPages)
        {
            if (!UiUtil.Alive(root)) return null;
            foreach (var (name, find) in Pages)
            {
                if (skipMenuPages && (name == "Options" || name == "Collections" || name == "Collection item")) continue;
                try
                {
                    var page = find(root);
                    if (page != null) return name;
                }
                catch { }
            }
            return null;
        }

        /// <summary>True if the pause menu shows a page other than its top level.</summary>
        public static bool PauseMenuSubPage(EscapeMenuPanel menu)
        {
            if (!UiUtil.Alive(menu)) return false;
            try
            {
                if (menu.IsOptionsPageShowing() || menu.IsCollectionsPageShowing()) return true;
            }
            catch { }
            var root = menu.transform;
            return First<LoadGamePage>(root) != null || First<SaveGamePage>(root) != null || First<GamepadControlsPage>(root) != null;
        }

        private static T First<T>(Transform root) where T : MonoBehaviour
        {
            var found = root.GetComponentsInChildren<T>(false);
            for (int i = 0; i < found.Length; i++)
                if (UiUtil.IsGameObjectVisible(found[i].gameObject)) return found[i];
            return null;
        }
    }
}
