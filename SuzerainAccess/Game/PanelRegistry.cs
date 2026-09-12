using System;
using System.Collections.Generic;
using SuzerainAccess.UI;
using UnityEngine;

namespace SuzerainAccess.Game
{
    public enum PanelTier
    {
        /// <summary>Always-present gameplay bars (statistics, navigation, continue button).</summary>
        Base = 40,
        /// <summary>Side panels: codex, journal, map location panels, details panels.</summary>
        Side = 60,
        /// <summary>The main menu.</summary>
        MainMenu = 70,
        /// <summary>Centre panels: conversation, decisions, reports, newspaper...</summary>
        Center = 80,
        /// <summary>Pop-ups that block everything else (confirmation, pause menu, tutorial).</summary>
        Modal = 100
    }

    /// <summary>
    /// One Suzerain UI panel, reached through the game's Panels singleton.
    /// </summary>
    internal sealed class PanelDescriptor
    {
        public string Id;
        public string Name;
        public PanelTier Tier;
        public int Order;
        public Func<Panels, MonoBehaviour> Get;
        public Func<Panels, bool> IsShowing;
        /// <summary>The panel's FocusablePanelComponent (used for "Back": same path as the gamepad cancel button), or null.</summary>
        public Func<Panels, FocusablePanelComponent> Focus;
    }

    /// <summary>
    /// Registry of every navigable panel held by the game's <c>Panels</c> singleton.
    ///
    /// GENERATED from the supplied Assembly-CSharp interop: every property name below is a verified
    /// public property of <c>Panels</c> whose type is the same-named panel class. The visibility test is,
    /// in order of preference, the panel's own <c>IsShowing()</c> method, its <c>isShowing</c> field, or
    /// (for the war panels that expose neither) whether the panel actually shows something. Those war
    /// "panels" (battles, objectives, unit/city statistics, status effects) are containers for markers
    /// drawn over the war map; they are always active and simply empty outside the war.
    /// <c>Focus</c> is only wired for panels that implement IFocusablePanel (verified "Focus" property);
    /// conversation, narration, prologue, main menu and pause menu are excluded on purpose because
    /// "Back" is handled specially for them.
    /// Names are the mod's own English labels.
    /// NotificationPanel, TokenNamePanel, TokenIndicatorPanel, hover panels, splash screen, debug console,
    /// gamepad-only menus and render helpers are intentionally not navigable regions.
    /// </summary>
    internal static class PanelRegistry
    {
        private static readonly List<PanelDescriptor> All = new List<PanelDescriptor>();

        public static IReadOnlyList<PanelDescriptor> Descriptors => All;

        static PanelRegistry()
        {
            Add<ConfirmationPanel>("ConfirmationPanel", "Confirmation", PanelTier.Modal, p => p.ConfirmationPanel, x => x.IsShowing(), x => x.Focus);
            Add<SaveFileDeletionPanel>("SaveFileDeletionPanel", "Delete save files", PanelTier.Modal, p => p.SaveFileDeletionPanel, x => x.IsShowing(), x => x.Focus);
            Add<NewGameConfirmationPanel>("NewGameConfirmationPanel", "New game confirmation", PanelTier.Modal, p => p.NewGameConfirmationPanel, x => x.IsShowing(), x => x.Focus);
            Add<ConsentPanel>("ConsentPanel", "Consent", PanelTier.Modal, p => p.ConsentPanel, x => x.IsShowing(), x => x.Focus);
            Add<LoginPanel>("LoginPanel", "Account login", PanelTier.Modal, p => p.LoginPanel, x => x.IsShowing(), x => x.Focus);
            Add<WelcomePanel>("WelcomePanel", "Welcome", PanelTier.Modal, p => p.WelcomePanel, x => x.IsShowing(), null);
            Add<ProfileNamePanel>("ProfileNamePanel", "Profile name", PanelTier.Modal, p => p.ProfileNamePanel, x => x.IsShowing(), x => x.Focus);
            Add<RewardsPanel>("RewardsPanel", "Rewards", PanelTier.Modal, p => p.RewardsPanel, x => x.IsShowing(), x => x.Focus);
            Add<OfferPanel>("OfferPanel", "Offer", PanelTier.Modal, p => p.OfferPanel, x => x.IsShowing(), null);
            Add<AdPanel>("AdPanel", "Advertisement", PanelTier.Modal, p => p.AdPanel, x => x.IsShowing(), null);
            // The store/profile overlay is a mobile-port feature. Its controls exist while the game loads even
            // though nothing is really open, so it is only treated as a screen once it is fully shown.
            Add<OverlayPanel>("OverlayPanel", "Store and profile", PanelTier.Center, p => p.OverlayPanel,
                x => x.IsShowing() && UiUtil.HasVisibleContent(x.transform) && UiUtil.EffectiveAlpha(x.transform) > 0.9f, x => x.Focus);
            Add<HyperlinksPanel>("HyperlinksPanel", "Hyperlinks", PanelTier.Modal, p => p.HyperlinksPanel, x => x.IsShowing(), x => x.Focus);
            Add<EscapeMenuPanel>("EscapeMenuPanel", "Pause menu", PanelTier.Modal, p => p.EscapeMenuPanel, x => x.IsShowing(), null);
            Add<TutorialPanel>("TutorialPanel", "Tutorial", PanelTier.Modal, p => p.TutorialPanel, x => x.IsShowing(), x => x.Focus);
            Add<SkipProloguePanel>("SkipProloguePanel", "Skip prologue", PanelTier.Modal, p => p.SkipProloguePanel, x => x.IsShowing(), x => x.Focus);
            Add<LoadArchetypePanel>("LoadArchetypePanel", "Load archetype", PanelTier.Modal, p => p.LoadArchetypePanel, x => x.IsShowing(), x => x.Focus);
            Add<CreditsPanel>("CreditsPanel", "Credits", PanelTier.Modal, p => p.CreditsPanel, x => x.IsShowing(), null);
            Add<SummaryPanel>("SummaryPanel", "Summary", PanelTier.Modal, p => p.SummaryPanel, x => x.IsShowing(), x => x.Focus);
            Add<ConversationPanel>("ConversationPanel", "Conversation", PanelTier.Center, p => p.ConversationPanel, x => x.IsShowing(), null);
            Add<NarrationPanel>("NarrationPanel", "Narration", PanelTier.Center, p => p.NarrationPanel, x => x.IsShowing(), null);
            Add<PrologueEpiloguePanel>("PrologueEpiloguePanel", "Prologue and epilogue", PanelTier.Center, p => p.PrologueEpiloguePanel, x => x.IsShowing(), null);
            Add<DecisionPanel>("DecisionPanel", "Decision", PanelTier.Center, p => p.DecisionPanel, x => x.IsShowing(), x => x.Focus);
            Add<PagedDecisionPanel>("PagedDecisionPanel", "Multi-page decision", PanelTier.Center, p => p.PagedDecisionPanel, x => x.IsShowing(), x => x.Focus);
            Add<BillPanel>("BillPanel", "Bill", PanelTier.Center, p => p.BillPanel, x => x.IsShowing(), x => x.Focus);
            Add<ReportPanel>("ReportPanel", "Report", PanelTier.Center, p => p.ReportPanel, x => x.IsShowing(), x => x.Focus);
            Add<NewsPanel>("NewsPanel", "Newspaper", PanelTier.Center, p => p.NewsPanel, x => x.IsShowing(), x => x.Focus);
            Add<ReminderPanel>("ReminderPanel", "Reminder", PanelTier.Center, p => p.ReminderPanel, x => x.IsShowing(), x => x.Focus);
            Add<CharacterCustomizationPanel>("CharacterCustomizationPanel", "Character customization", PanelTier.Center, p => p.CharacterCustomizationPanel, x => x.IsShowing(), x => x.Focus);
            Add<ContinueCenterPanel>("ContinueCenterPanel", "Continue", PanelTier.Center, p => p.ContinueCenterPanel, x => x.IsShowing(), null);
            Add<StorySelectionPanel>("StorySelectionPanel", "Story selection", PanelTier.Center, p => p.StorySelectionPanel, x => x.IsShowing(), x => x.Focus);
            Add<ArchetypeSelectionPanel>("ArchetypeSelectionPanel", "Archetype selection", PanelTier.Center, p => p.ArchetypeSelectionPanel, x => x.IsShowing(), x => x.Focus);
            Add<TorporNewsPanel>("TorporNewsPanel", "Torpor news", PanelTier.Center, p => p.TorporNewsPanel, x => x.IsShowing(), x => x.Focus);
            Add<WarBattlePanel>("WarBattlePanel", "Battle", PanelTier.Center, p => p.WarBattlePanel, x => UiUtil.HasVisibleContent(x.transform), null);
            Add<WarDeploymentPanel>("WarDeploymentPanel", "Deployment", PanelTier.Center, p => p.WarDeploymentPanel, x => x.IsShowing(), x => x.Focus);
            Add<WarProductionPanel>("WarProductionPanel", "Production", PanelTier.Center, p => p.WarProductionPanel, x => x.IsShowing(), x => x.Focus);
            Add<MainMenuPanel>("MainMenuPanel", "Main menu", PanelTier.MainMenu, p => p.MainMenuPanel, x => x.IsShowing(), null);
            Add<TokenInteractionPanel>("TokenInteractionPanel", "Map location", PanelTier.Side, p => p.TokenInteractionPanel, x => x.IsShowing(), null);
            Add<CodexPanel>("CodexPanel", "Codex", PanelTier.Side, p => p.CodexPanel, x => x.IsShowing(), x => x.Focus);
            Add<ConnectionsPanel>("ConnectionsPanel", "Connections", PanelTier.Side, p => p.ConnectionsPanel, x => x.IsShowing(), x => x.Focus);
            Add<JournalPanel>("JournalPanel", "Journal", PanelTier.Side, p => p.JournalPanel, x => x.IsShowing(), x => x.Focus);
            Add<OverviewPanel>("OverviewPanel", "Overview", PanelTier.Side, p => p.OverviewPanel, x => x.IsShowing(), x => x.Focus);
            Add<TokenInformationPanel>("TokenInformationPanel", "Location information", PanelTier.Side, p => p.TokenInformationPanel, x => x.IsShowing(), x => x.Focus);
            Add<CountryDetailsPanel>("CountryDetailsPanel", "Country details", PanelTier.Side, p => p.CountryDetailsPanel, x => x.IsShowing(), x => x.Focus);
            Add<CharacterDetailsPanel>("CharacterDetailsPanel", "Character details", PanelTier.Side, p => p.CharacterDetailsPanel, x => x.IsShowing(), x => x.Focus);
            Add<OneTimeDecreesPanel>("OneTimeDecreesPanel", "One-time decrees", PanelTier.Side, p => p.OneTimeDecreesPanel, x => x.IsShowing(), x => x.Focus);
            Add<ReusableDecreesPanel>("ReusableDecreesPanel", "Reusable decrees", PanelTier.Side, p => p.ReusableDecreesPanel, x => x.IsShowing(), x => x.Focus);
            Add<GraphPanel>("GraphPanel", "Graph", PanelTier.Side, p => p.GraphPanel, x => x.IsShowing(), x => x.Focus);
            Add<WarTokenSelectionPanel>("WarTokenSelectionPanel", "Unit selection", PanelTier.Side, p => p.WarTokenSelectionPanel, x => x.IsShowing(), null);
            Add<WarObjectivePanel>("WarObjectivePanel", "War objectives", PanelTier.Side, p => p.WarObjectivePanel, x => UiUtil.HasVisibleContent(x.transform), null);
            Add<WarTokenUnitStatsPanel>("WarTokenUnitStatsPanel", "Unit statistics", PanelTier.Side, p => p.WarTokenUnitStatsPanel, x => UiUtil.HasVisibleContent(x.transform), null);
            Add<WarTokenStatusEffectsPanel>("WarTokenStatusEffectsPanel", "Unit status effects", PanelTier.Side, p => p.WarTokenStatusEffectsPanel, x => UiUtil.HasVisibleContent(x.transform), null);
            Add<WarTileStatusEffectsPanel>("WarTileStatusEffectsPanel", "Tile status effects", PanelTier.Side, p => p.WarTileStatusEffectsPanel, x => UiUtil.HasVisibleContent(x.transform), null);
            Add<WarTokenCityStatsPanel>("WarTokenCityStatsPanel", "City statistics", PanelTier.Side, p => p.WarTokenCityStatsPanel, x => UiUtil.HasVisibleContent(x.transform), null);
            Add<HUDPanel>("HUDPanel", "Statistics bar", PanelTier.Base, p => p.HUDPanel, x => x.IsShowing(), null);
            Add<NavigationPanel>("NavigationPanel", "Navigation bar", PanelTier.Base, p => p.NavigationPanel, x => x.IsShowing(), null);
            Add<ContinueButtonPanel>("ContinueButtonPanel", "Continue button", PanelTier.Base, p => p.ContinueButtonPanel, x => x.IsShowing(), null);
            Add<BottomLeftPanel>("BottomLeftPanel", "Bottom left panel", PanelTier.Base, p => p.BottomLeftPanel, x => x.IsShowing(), null);
            Add<RightPanel>("RightPanel", "Side panel", PanelTier.Base, p => p.RightPanel, x => x.IsShowing(), null);
            Add<TurnCostPanel>("TurnCostPanel", "Turn cost", PanelTier.Base, p => p.TurnCostPanel, x => x.IsShowing(), null);
            Add<SocialsButtonPanel>("SocialsButtonPanel", "Social links", PanelTier.Base, p => p.SocialsButtonPanel, x => x.IsShowing(), null);
            Add<TokenProgressPanel>("TokenProgressPanel", "Progress", PanelTier.Base, p => p.TokenProgressPanel, x => x.IsShowing(), null);
            Add<WarHUDPanel>("WarHUDPanel", "War status bar", PanelTier.Base, p => p.WarHUDPanel, x => x.isShowing, null);
            Add<WarTurnPanel>("WarTurnPanel", "War turn", PanelTier.Base, p => p.WarTurnPanel, x => x.IsShowing(), null);
            Add<WarPlayerActionsPanel>("WarPlayerActionsPanel", "War actions", PanelTier.Base, p => p.WarPlayerActionsPanel, x => x.isShowing, null);
            Add<WarInfoTextPanel>("WarInfoTextPanel", "War information", PanelTier.Base, p => p.WarInfoTextPanel, x => x.isShowing, null);
        }

        private static void Add<T>(string id, string name, PanelTier tier, Func<Panels, T> get, Func<T, bool> showing,
            Func<T, FocusablePanelComponent> focus) where T : MonoBehaviour
        {
            All.Add(new PanelDescriptor
            {
                Id = id,
                Name = name,
                Tier = tier,
                Order = All.Count,
                Get = p => get(p),
                IsShowing = p =>
                {
                    var panel = get(p);
                    return UiUtil.Alive(panel) && showing(panel);
                },
                Focus = focus == null ? null : new Func<Panels, FocusablePanelComponent>(p =>
                {
                    var panel = get(p);
                    return UiUtil.Alive(panel) ? focus(panel) : null;
                })
            });
        }

        public static PanelDescriptor Find(string id)
        {
            foreach (var d in All) if (d.Id == id) return d;
            return null;
        }
    }
}
