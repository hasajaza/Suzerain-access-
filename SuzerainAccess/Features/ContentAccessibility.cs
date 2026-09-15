using System;
using System.Collections.Generic;
using System.Diagnostics;
using SuzerainAccess.Config;
using SuzerainAccess.Core;
using SuzerainAccess.Game;
using SuzerainAccess.Navigation;
using SuzerainAccess.Speech;
using SuzerainAccess.UI;
using UnityEngine;

namespace SuzerainAccess.Features
{
    /// <summary>
    /// Automatically reads the main content of panels when they open or change, and provides the
    /// "read current document" command (F7). Verified game members used:
    ///  ReportPanel: reportTitleText, tokenNameText, reportDescriptionText, pageCount, reportData (ReportData.IsNotificationActive)
    ///               CycleReport(int) is bound by the game to Left/Right (PanelContentCycle) - changes are announced.
    ///  DecisionPanel: decisionTitleText, decisionDescriptionText, instantiatedDecisionOptionButtons, currentDecisionData
    ///  PagedDecisionPanel: title, description, currentPageIndex, numberOfPages, currentTemplatePagedDecisionsPage,
    ///               panelCounterTitle/panelCounterValue, panelBarTitle/panelBarSlider, currentPagedDecisionPanelData
    ///  TemplatePagedDecisionsPage: title, description, choiceAmountText
    ///  ConfirmationPanel: mainText
    ///  TokenInformationPanel: title, subtitle, eventText, instantiatedInfoTexts (TemplateInfoText.infoTitleText/infoValueText),
    ///               instantiatedTokenEffects (TemplateTokenEffect.tooltipTitle/tooltipSubtitle)
    ///  NewsPanel articles: TemplateNewsArticle.newsTitle/newsDescription, currentNewsData.IsRead
    /// </summary>
    internal sealed class ContentAccessibility
    {
        /// <summary>Panels whose whole visible text is read automatically when it appears or changes.</summary>
        private static readonly HashSet<string> ReadWholePanel = new HashSet<string>
        {
            "TutorialPanel", "BillPanel", "ReminderPanel", "SummaryPanel", "ContinueCenterPanel",
            "SkipProloguePanel", "NewGameConfirmationPanel", "SaveFileDeletionPanel", "WelcomePanel",
            "RewardsPanel", "WarBattlePanel", "LoadArchetypePanel"
        };

        private readonly SpeechManager _speech;
        private readonly FocusManager _focus;
        private readonly ScreenTracker _screen;
        private readonly DialogueAccessibility _dialogue;
        private readonly Stopwatch _clock = Stopwatch.StartNew();

        private readonly Dictionary<string, string> _announcedKeys = new Dictionary<string, string>();
        private readonly Dictionary<string, (string key, float since)> _pending = new Dictionary<string, (string, float)>();

        public ContentAccessibility(SpeechManager speech, FocusManager focus, ScreenTracker screen, DialogueAccessibility dialogue)
        {
            _speech = speech;
            _focus = focus;
            _screen = screen;
            _dialogue = dialogue;
        }

        private float Now => (float)_clock.Elapsed.TotalSeconds;

        private float _nextPoll;

        public void Tick()
        {
            // Text collection walks the panel hierarchy; 7 times per second is plenty.
            if (Now < _nextPoll) return;
            _nextPoll = Now + 0.15f;
            var panels = ScreenTracker.GetPanels();
            if (panels == null) return;

            // Forget panels that closed, so reopening them reads again.
            var closed = new List<string>();
            foreach (var id in _announcedKeys.Keys) if (!_screen.IsShowing(id)) closed.Add(id);
            foreach (var id in closed) { _announcedKeys.Remove(id); _pending.Remove(id); }

            if (!ModConfig.AutoReadContent.Value) return;

            foreach (var region in _screen.Regions)
            {
                if (region.IsFallback) continue;
                try
                {
                    string id = region.Id;
                    string key, text;
                    if (!TryGetAutoContent(panels, region, out key, out text)) continue;
                    Announce(id, key, text);
                }
                catch (Exception ex)
                {
                    ModLog.Exception("content:" + region.Id, ex);
                }
            }
        }

        /// <summary>Reads content once it is non-empty and stable for 0.3 s, and only when it changed.</summary>
        private void Announce(string id, string key, string text)
        {
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(text)) return;
            if (_announcedKeys.TryGetValue(id, out var done) && done == key) return;
            if (!_pending.TryGetValue(id, out var p) || p.key != key) { _pending[id] = (key, Now); return; }
            if (Now - p.since < 0.3f) return;
            _announcedKeys[id] = key;
            _pending.Remove(id);
            _speech.Say(text, interrupt: false, important: true, automatic: true);
        }

        private bool TryGetAutoContent(Panels panels, Region region, out string key, out string text)
        {
            key = text = null;
            switch (region.Id)
            {
                case "ReportPanel":
                {
                    var p = panels.ReportPanel;
                    text = ReportText(p, withHint: true);
                    key = (p.reportData != null ? p.reportData.Pointer.ToInt64().ToString() : "") + UiUtil.TextOf(p.reportTitleText);
                    return true;
                }
                case "DecisionPanel":
                {
                    var p = panels.DecisionPanel;
                    text = DecisionText(p);
                    key = (p.currentDecisionData != null ? p.currentDecisionData.Pointer.ToInt64().ToString() : "") + UiUtil.TextOf(p.decisionTitleText);
                    return true;
                }
                case "PagedDecisionPanel":
                {
                    var p = panels.PagedDecisionPanel;
                    text = PagedDecisionText(p);
                    key = (p.currentPagedDecisionPanelData != null ? p.currentPagedDecisionPanelData.Pointer.ToInt64().ToString() : "") + "#" + p.currentPageIndex;
                    return true;
                }
                case "ConfirmationPanel":
                {
                    var p = panels.ConfirmationPanel;
                    text = UiUtil.TextOf(p.mainText);
                    key = text;
                    return true;
                }
                case "TokenProgressPanel":
                {
                    string progress = ProgressText(panels.TokenProgressPanel);
                    if (string.IsNullOrEmpty(progress)) return false;
                    text = progress;
                    key = progress;
                    return true;
                }
                case "OverviewPanel":
                {
                    // The policy or situation currently selected (verified OverviewPanel.entryTitle/entryDescription).
                    var p = panels.OverviewPanel;
                    string entry = TextUtil.Join(UiUtil.VisibleTextOf(p.entryTitle), UiUtil.VisibleTextOf(p.entryDescription));
                    if (string.IsNullOrWhiteSpace(entry)) return false;
                    text = TextUtil.Sentence(UiUtil.VisibleTextOf(p.entryTitle)) + " " + UiUtil.VisibleTextOf(p.entryDescription);
                    key = entry;
                    return true;
                }
                case "CodexPanel":
                {
                    string article = CodexArticle(panels.CodexPanel);
                    if (string.IsNullOrEmpty(article)) return false;
                    text = article;
                    key = article;
                    return true;
                }
                case "GraphPanel":
                {
                    var p = panels.GraphPanel;
                    text = GraphText(p);
                    key = text; // re-read when the filter changes the plotted values
                    return true;
                }
                case "TokenInformationPanel":
                {
                    var p = panels.TokenInformationPanel;
                    text = TextUtil.Join(UiUtil.TextOf(p.title), UiUtil.TextOf(p.subtitle));
                    key = text;
                    if (ModConfig.AtLeast(Verbosity.High)) text = TextUtil.Sentence(text) + " Press F7 for details.";
                    return true;
                }
                default:
                    if (!ReadWholePanel.Contains(region.Id) || region.Root == null) return false;
                    var texts = UiUtil.CollectTexts(region.Root, includeHidden: false);
                    text = TextUtil.JoinWith(". ", texts);
                    key = text;
                    return true;
            }
        }

        // ------------------------------------------------------------------ text builders

        private static string ReportText(ReportPanel p, bool withHint)
        {
            string title = UiUtil.TextOf(p.reportTitleText);
            string token = UiUtil.TextOf(p.tokenNameText);
            string page = TextUtil.SpeakFraction(UiUtil.TextOf(p.pageCount));
            string description = UiUtil.TextOf(p.reportDescriptionText);
            bool isNew = false;
            try { isNew = p.reportData != null && p.reportData.IsNotificationActive; } catch { }
            // The game shows an "archived" stamp (verified field ReportPanel.archivedLogo) on archived reports.
            bool archived = false;
            try { archived = p.archivedLogo != null && UiUtil.IsGameObjectVisible(p.archivedLogo.gameObject); } catch { }
            string header = TextUtil.Join("Report: " + title, token, string.IsNullOrEmpty(page) ? "" : "report " + page, isNew ? "new" : "", archived ? "archived" : "");
            string text = TextUtil.Sentence(header) + " " + description;
            if (withHint && ModConfig.AtLeast(Verbosity.High) && !string.IsNullOrEmpty(page))
                text = TextUtil.Sentence(text) + " Left and Right arrows switch reports.";
            return text;
        }

        private static string DecisionText(DecisionPanel p)
        {
            string title = UiUtil.TextOf(p.decisionTitleText);
            string description = UiUtil.TextOf(p.decisionDescriptionText);
            int options = 0;
            var list = p.instantiatedDecisionOptionButtons;
            for (int i = 0; list != null && i < list.Count; i++)
                if (UiUtil.Alive(list[i]) && list[i].gameObject.activeInHierarchy) options++;
            string text = TextUtil.Sentence("Decision: " + title) + " " + TextUtil.Sentence(description);
            if (options > 0) text += " " + options + (options == 1 ? " option." : " options.");
            return text;
        }

        private static string PagedDecisionText(PagedDecisionPanel p)
        {
            var parts = new List<string>();
            parts.Add(TextUtil.Sentence(UiUtil.TextOf(p.title)));
            parts.Add(TextUtil.Sentence(UiUtil.VisibleTextOf(p.description)));
            int pages = p.numberOfPages;
            if (pages > 1) parts.Add("Page " + (p.currentPageIndex + 1) + " of " + pages + ".");
            var page = p.currentTemplatePagedDecisionsPage;
            if (UiUtil.Alive(page))
            {
                parts.Add(TextUtil.Sentence(UiUtil.TextOf(page.title)));
                parts.Add(TextUtil.Sentence(UiUtil.VisibleTextOf(page.description)));
                parts.Add(TextUtil.Sentence(UiUtil.VisibleTextOf(page.choiceAmountText)));
            }
            string counterTitle = UiUtil.VisibleTextOf(p.panelCounterTitle);
            if (!string.IsNullOrEmpty(counterTitle))
                parts.Add(TextUtil.Sentence(counterTitle + ": " + UiUtil.TextOf(p.panelCounterValue)));
            string barTitle = UiUtil.VisibleTextOf(p.panelBarTitle);
            if (!string.IsNullOrEmpty(barTitle) && UiUtil.Alive(p.panelBarSlider))
            {
                var s = p.panelBarSlider;
                parts.Add(TextUtil.Sentence(barTitle + ": " + TextUtil.Number(s.value) + " of " + TextUtil.Number(s.maxValue)));
            }
            return TextUtil.JoinWith(" ", parts);
        }

        private static string TokenInformationText(TokenInformationPanel p)
        {
            var parts = new List<string>();
            parts.Add(TextUtil.Sentence(TextUtil.Join(UiUtil.TextOf(p.title), UiUtil.TextOf(p.subtitle))));
            var infos = p.instantiatedInfoTexts;
            for (int i = 0; infos != null && i < infos.Count; i++)
            {
                var info = infos[i];
                if (!UiUtil.Alive(info) || !info.gameObject.activeInHierarchy) continue;
                string t = UiUtil.TextOf(info.infoTitleText), v = UiUtil.TextOf(info.infoValueText);
                if (t.Length + v.Length > 0) parts.Add(TextUtil.Sentence(string.IsNullOrEmpty(v) ? t : t + ": " + v));
            }
            var effects = p.instantiatedTokenEffects;
            var names = new List<string>();
            for (int i = 0; effects != null && i < effects.Count; i++)
            {
                var e = effects[i];
                if (!UiUtil.Alive(e) || !e.gameObject.activeInHierarchy) continue;
                names.Add(TextUtil.Join(UiUtil.TextOf(e.tooltipTitle), UiUtil.TextOf(e.tooltipSubtitle)));
            }
            if (names.Count > 0) parts.Add("Status effects: " + TextUtil.JoinWith("; ", names) + ".");

            // Ethnicity and religion are pie charts: read the chart data, or the location's data if the
            // chart has not been drawn yet.
            string ethnicity = Demographics.FromChart(p.ethnicityChart);
            string religion = Demographics.FromChart(p.religionChart);
            if (string.IsNullOrEmpty(ethnicity)) ethnicity = Demographics.FromTokenData(p.currentTokenData, ethnicity: true);
            if (string.IsNullOrEmpty(religion)) religion = Demographics.FromTokenData(p.currentTokenData, ethnicity: false);
            if (!string.IsNullOrEmpty(ethnicity)) parts.Add("Ethnicity: " + ethnicity + ".");
            if (!string.IsNullOrEmpty(religion)) parts.Add("Religion: " + religion + ".");
            parts.Add(TextUtil.Sentence(UiUtil.VisibleTextOf(p.eventText)));
            return TextUtil.JoinWith(" ", parts);
        }

        /// <summary>
        /// Graph panels (e.g. Economic Stability) draw a line chart with only "High"/"Low" axis labels.
        /// The plotted points are GraphPanel.graphInput (after the game's filter, oldest first, the last one
        /// being "Current"); the red danger line is the Dialogue System variable named by
        /// GraphPanelProperties.DangerThresholdVariable. All verified in the interop.
        /// </summary>
        private static string GraphText(GraphPanel p)
        {
            var parts = new List<string>();
            parts.Add(TextUtil.Sentence(UiUtil.TextOf(p.title)));
            string filter = "";
            try { filter = UiUtil.TextOf(p.filterCarousel != null ? p.filterCarousel.carouselText : null); } catch { }
            var values = new List<int>();
            try
            {
                var input = p.graphInput;
                for (int i = 0; input != null && i < input.Count; i++) values.Add(input[i]);
            }
            catch { }

            int? threshold = null;
            try
            {
                string variable = p.currentGraphPanelData?.GraphPanelProperties?.DangerThresholdVariable;
                if (!string.IsNullOrWhiteSpace(variable))
                {
                    var result = PixelCrushers.DialogueSystem.DialogueLua.GetVariable(variable);
                    if (result.isNumber) threshold = result.asInt;
                }
            }
            catch { }

            if (values.Count == 0)
            {
                parts.Add("The graph has no data yet.");
            }
            else
            {
                parts.Add(TextUtil.Sentence((string.IsNullOrEmpty(filter) ? "" : filter + ": ") +
                          "values from oldest to current: " + string.Join(", ", values)));
                int current = values[values.Count - 1];
                int max = int.MinValue, min = int.MaxValue;
                foreach (var v in values) { if (v > max) max = v; if (v < min) min = v; }
                string trend = "";
                if (values.Count > 1)
                {
                    int previous = values[values.Count - 2];
                    trend = current > previous ? "up from " + previous : current < previous ? "down from " + previous : "unchanged";
                }
                parts.Add(TextUtil.Sentence(TextUtil.Join("Current " + current, trend, "highest " + max, "lowest " + min)));
                if (threshold.HasValue)
                {
                    string relation = current > threshold.Value ? "above" : current < threshold.Value ? "below" : "exactly on";
                    parts.Add("Danger line at " + threshold.Value + "; the current value is " + relation + " it.");
                }
            }
            return TextUtil.JoinWith(" ", parts);
        }

        /// <summary>The open Codex article: its title and text (verified CodexEntryPage.title/description).</summary>
        private static string CodexArticle(CodexPanel codex)
        {
            try
            {
                var page = codex.codexEntryPage;
                if (!UiUtil.Alive(page) || !UiUtil.IsGameObjectVisible(page.gameObject)) return null;
                string title = UiUtil.TextOf(page.title);
                string description = UiUtil.TextOf(page.description);
                if (string.IsNullOrWhiteSpace(description)) return null;
                return TextUtil.Sentence(title) + " " + description;
            }
            catch { return null; }
        }

        /// <summary>
        /// Ongoing projects on the map with their progress (verified: TokenProgressPanel.instantiatedTokenProgress,
        /// TemplateTokenProgress.currentToken / instantiatedTokenEffects, TokenStatusEffectData.ProgressPercentage).
        /// </summary>
        private static string ProgressText(TokenProgressPanel panel)
        {
            try
            {
                if (!UiUtil.Alive(panel)) return null;
                var parts = new List<string>();
                // Walk the panel for the effect widgets: the game's own lists are not always filled in.
                var effects = panel.transform.GetComponentsInChildren<TemplateTokenEffect>(false);
                for (int i = 0; i < effects.Length; i++)
                {
                    var effect = effects[i];
                    if (!UiUtil.Alive(effect) || !effect.gameObject.activeInHierarchy) continue;
                    string where = "";
                    try
                    {
                        var item = effect.GetComponentInParent<TemplateTokenProgress>();
                        if (item != null && item.currentToken != null) where = TextUtil.Clean(item.currentToken.GetTokenData()?.Title);
                    }
                    catch { }
                    int percent = GameText.EffectPercent(effect);
                    string entry = TextUtil.Join(GameText.EffectTitle(effect), where,
                        percent < 0 ? "" : percent + " percent complete", GameText.EffectSubtitle(effect));
                    if (!string.IsNullOrWhiteSpace(entry)) parts.Add(TextUtil.Sentence(entry));
                }
                if (parts.Count == 0) ModLog.InfoOnce("progress-empty", "Progress panel: no readable effect widgets were found under " + UiUtil.PathOf(panel.transform) + ".");
                return parts.Count == 0 ? null : TextUtil.JoinWith(" ", parts);
            }
            catch (Exception ex)
            {
                ModLog.Exception("progress-text", ex);
                return null;
            }
        }

        // ------------------------------------------------------------------ F7

        public void ReadDocument()
        {
            var panels = ScreenTracker.GetPanels();
            var region = _focus.CurrentRegion;
            if (panels == null || region == null) { _speech.Say("Nothing to read."); return; }

            try
            {
                // An open Codex entry is what the player just asked to read, even when the focus is still
                // in the conversation underneath it.
                if (_screen.IsShowing("CodexPanel"))
                {
                    string article = CodexArticle(panels.CodexPanel);
                    if (!string.IsNullOrEmpty(article)) { _speech.Say(article); return; }
                }

                switch (region.Id)
                {
                    case "ReportPanel": _speech.Say(ReportText(panels.ReportPanel, withHint: false)); return;
                    case "DecisionPanel": _speech.Say(DecisionText(panels.DecisionPanel)); return;
                    case "PagedDecisionPanel": _speech.Say(PagedDecisionText(panels.PagedDecisionPanel)); return;
                    case "TokenInformationPanel": _speech.Say(TokenInformationText(panels.TokenInformationPanel)); return;
                    case "GraphPanel": _speech.Say(GraphText(panels.GraphPanel)); return;
                    case "TokenProgressPanel":
                    {
                        string progress = ProgressText(panels.TokenProgressPanel);
                        _speech.Say(string.IsNullOrEmpty(progress) ? "No projects in progress." : progress);
                        return;
                    }
                    case "CodexPanel":
                        _speech.Say("The Codex is showing its topic list. Choose an entry, or use the search field.");
                        return;
                    case "CharacterCustomizationPanel":
                    {
                        // Every customization row with its current value, plus the character's details.
                        var p = panels.CharacterCustomizationPanel;
                        var parts = new List<string>();
                        parts.Add(TextUtil.Sentence(TextUtil.Join(UiUtil.TextOf(p.panelTitle), UiUtil.TextOf(p.panelSubtitle))));
                        parts.Add(TextUtil.Sentence(TextUtil.Join(UiUtil.TextOf(p.characterName), UiUtil.TextOf(p.characterTitle))));
                        parts.Add(TextUtil.Sentence(TextUtil.Join(UiUtil.VisibleTextOf(p.characterInfoTitle_1), UiUtil.VisibleTextOf(p.characterInfoSubtitle_1))));
                        parts.Add(TextUtil.Sentence(TextUtil.Join(UiUtil.VisibleTextOf(p.characterInfoTitle_2), UiUtil.VisibleTextOf(p.characterInfoSubtitle_2))));
                        var options = p.instantiatedCustomizationOptions;
                        for (int i = 0; options != null && i < options.Count; i++)
                        {
                            var o = options[i];
                            if (!UiUtil.Alive(o) || !o.gameObject.activeInHierarchy) continue;
                            parts.Add(TextUtil.Sentence(UiUtil.TextOf(o.customizationOptionTitle) + ": " + UiUtil.TextOf(o.carouselText)));
                        }
                        _speech.Say(TextUtil.JoinWith(" ", parts));
                        return;
                    }
                    case "StorySelectionPanel":
                    {
                        // The story's own description: country or leader information, depending on the
                        // selected tab (verified StorySelectionPanel fields).
                        var p = panels.StorySelectionPanel;
                        var parts = new List<string>();
                        parts.Add(TextUtil.Sentence(TextUtil.Join(UiUtil.VisibleTextOf(p.countryName), UiUtil.VisibleTextOf(p.countryTitle))));
                        parts.Add(TextUtil.Sentence(TextUtil.Join(UiUtil.VisibleTextOf(p.leaderName), UiUtil.VisibleTextOf(p.leaderTitle))));
                        parts.Add(TextUtil.Sentence(TextUtil.Join(UiUtil.VisibleTextOf(p.protagonistName), UiUtil.VisibleTextOf(p.protagonistTitle))));
                        var infos = new List<TemplateInfoText>();
                        if (p.instantiatedCountryInfoTexts != null) for (int i = 0; i < p.instantiatedCountryInfoTexts.Count; i++) infos.Add(p.instantiatedCountryInfoTexts[i]);
                        if (p.instantiatedLeaderInfoTexts != null) for (int i = 0; i < p.instantiatedLeaderInfoTexts.Count; i++) infos.Add(p.instantiatedLeaderInfoTexts[i]);
                        foreach (var info in infos)
                        {
                            if (!UiUtil.Alive(info) || !UiUtil.IsGameObjectVisible(info.gameObject)) continue;
                            string t = UiUtil.TextOf(info.infoTitleText), v = UiUtil.TextOf(info.infoValueText);
                            if (t.Length + v.Length > 0) parts.Add(TextUtil.Sentence(string.IsNullOrEmpty(v) ? t : t + ": " + v));
                        }
                        parts.Add(TextUtil.Sentence(UiUtil.VisibleTextOf(p.descriptionText)));
                        parts.Add(TextUtil.Sentence(UiUtil.VisibleTextOf(p.protagonistDescriptionText)));
                        string text = TextUtil.JoinWith(" ", parts);
                        if (!string.IsNullOrWhiteSpace(text)) { _speech.Say(text); return; }
                        break;
                    }
                    case "OverviewPanel":
                    {
                        // The selected policy or situation (verified fields OverviewPanel.entryTitle / entryDescription).
                        var p = panels.OverviewPanel;
                        string title = UiUtil.TextOf(p.entryTitle), description = UiUtil.TextOf(p.entryDescription);
                        if (title.Length + description.Length > 0) { _speech.Say(TextUtil.Sentence(title) + " " + description); return; }
                        break;
                    }
                    case "ConversationPanel":
                    case "NarrationPanel":
                    case "PrologueEpiloguePanel":
                        _speech.Say(TextUtil.Join(_dialogue.Participants(), _dialogue.LastLine ?? "No dialogue yet."));
                        return;
                    case "NewsPanel":
                    {
                        var e = _focus.Current;
                        var article = e != null && e.IsAlive ? UiUtil.FindInSelfOrParents<TemplateNewsArticle>(e.GameObject.transform, 2) : null;
                        if (article != null)
                        {
                            _speech.Say(TextUtil.Sentence(UiUtil.TextOf(article.newsTitle)) + " " + UiUtil.TextOf(article.newsDescription));
                            return;
                        }
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                ModLog.Exception("read-document", ex);
            }

            var texts = _focus.RegionTexts();
            _speech.Say(texts.Count == 0 ? region.Name + ": no text." : TextUtil.Sentence(region.Name) + " " + TextUtil.JoinWith(". ", texts));
        }
    }
}
