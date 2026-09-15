using System;
using System.Collections.Generic;
using SuzerainAccess.Config;
using SuzerainAccess.Core;
using SuzerainAccess.Game;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SuzerainAccess.UI
{
    /// <summary>
    /// Creates <see cref="AccessibleElement"/>s from Selectables. Suzerain's own template components
    /// (verified in Assembly-CSharp) are recognised first so that responses, decision options, reports,
    /// statistics etc. get meaningful names, roles and states; anything else falls back to the standard
    /// Unity UI control types (Button, Toggle, Slider, TMP_Dropdown, TMP_InputField).
    /// </summary>
    internal static class ElementFactory
    {
        private const int TemplateSearchLevels = 2;

        public static AccessibleElement Create(Selectable selectable)
        {
            var go = selectable.gameObject;
            var e = new AccessibleElement { GameObject = go, Selectable = selectable, Identity = go.Pointer };
            e.Activate = () => ActivateDefault(selectable);

            try
            {
                // The game's "new" dot (NotificationIcon) inside or on the control.
                var icons = selectable.GetComponentsInChildren<NotificationIcon>(true);
                if (icons.Length > 0) e.Notice = icons[0];
                else e.Notice = UiUtil.FindInSelfOrParents<NotificationIcon>(selectable.transform, 1);
            }
            catch { }

            try
            {
                if (!ApplyGameTemplate(e, selectable)) ApplyStandardControl(e, selectable);
            }
            catch (Exception ex)
            {
                ModLog.Exception("element-factory", ex);
                ApplyStandardControl(e, selectable);
            }
            return e;
        }

        // ------------------------------------------------------------------ game templates

        private static bool ApplyGameTemplate(AccessibleElement e, Selectable s)
        {
            // The conversation's continue button (verified field ConversationHandler.continueButton) covers the
            // dialogue area, so its "own text" would be the oldest dialogue lines. Name it properly instead.
            try
            {
                var p = Panels.Instance;
                var handler = p != null && p.ConversationPanel != null ? p.ConversationPanel.conversationHandler : null;
                var cont = handler != null ? handler.continueButton : null;
                if (cont != null && cont.Pointer == s.Pointer)
                {
                    e.Role = ElementRole.Button;
                    e.IsConversationContinue = true;
                    e.Label = () => "Continue";
                    e.Hint = "Enter or Space continues the dialogue.";
                    return true;
                }
            }
            catch { }

            // The statistics bar's map button (verified fields HUDPanel.mapButtonContainer / mapButtonTooltipText).
            try
            {
                var hudPanel = Panels.Instance != null ? Panels.Instance.HUDPanel : null;
                var mapContainer = hudPanel != null ? hudPanel.mapButtonContainer : null;
                if (mapContainer != null && (s.transform.Pointer == mapContainer.Pointer || s.transform.IsChildOf(mapContainer)))
                {
                    e.Role = ElementRole.Button;
                    e.Label = () =>
                    {
                        string tip = UiUtil.TextOf(hudPanel.mapButtonTooltipText);
                        return string.IsNullOrEmpty(tip) ? "Switch map" : tip;
                    };
                    e.Hint = "Switches between the main map and the world map. Control F6 does the same from anywhere.";
                    return true;
                }
            }
            catch { }

            // Location information arrows (verified TokenInformationPanel.cycleButtonsContainer): they move to
            // the previous or next location's information, the same as the game's Left and Right keys.
            try
            {
                var info = Panels.Instance != null ? Panels.Instance.TokenInformationPanel : null;
                var infoCycle = info != null ? info.cycleButtonsContainer : null;
                if (infoCycle != null && s.transform.IsChildOf(infoCycle))
                {
                    bool leftMost = true;
                    var buttons = infoCycle.GetComponentsInChildren<Button>(false);
                    for (int i = 0; i < buttons.Length; i++)
                        if (buttons[i].Pointer != s.Pointer && buttons[i].transform.position.x < s.transform.position.x) leftMost = false;
                    e.Role = ElementRole.Button;
                    e.Label = () => leftMost ? "Previous location" : "Next location";
                    e.Hint = "Shows another location's information. The Left and Right arrow keys do the same.";
                    return true;
                }
            }
            catch { }

            // Report panel arrows (verified field ReportPanel.cycleButtonsContainer): icon-only previous/next.
            try
            {
                var panels = Panels.Instance;
                var report = panels != null ? panels.ReportPanel : null;
                var cycle = report != null ? report.cycleButtonsContainer : null;
                if (cycle != null && s.transform.IsChildOf(cycle))
                {
                    // The left arrow is "previous", the right arrow is "next" - as a sighted player sees them.
                    bool leftMost = true;
                    var buttons = cycle.GetComponentsInChildren<Button>(false);
                    for (int i = 0; i < buttons.Length; i++)
                        if (buttons[i].Pointer != s.Pointer && buttons[i].transform.position.x < s.transform.position.x) leftMost = false;
                    e.Role = ElementRole.Button;
                    e.Label = () => leftMost ? "Previous report" : "Next report";
                    e.Hint = "The Left and Right arrow keys also switch reports.";
                    return true;
                }
            }
            catch { }

            // Story selection: the Leader/Country tabs, the main/side story pack tabs, the story tokens,
            // the Torpor mode and skip-prologue toggles and the start button (verified StorySelectionPanel fields).
            try
            {
                var story = s.GetComponentInParent<StorySelectionPanel>();
                if (story != null)
                {
                    var toggle = s.TryCast<Toggle>();
                    string name = null;
                    ElementRole role = ElementRole.Tab;
                    if (toggle != null && story.leaderToggle != null && toggle.Pointer == story.leaderToggle.Pointer) name = "Leader information";
                    else if (toggle != null && story.countryToggle != null && toggle.Pointer == story.countryToggle.Pointer) name = "Country information";
                    else if (toggle != null && story.mainStoryPackToggle != null && toggle.Pointer == story.mainStoryPackToggle.Pointer) name = "Main stories";
                    else if (toggle != null && story.sideStoryPackToggle != null && toggle.Pointer == story.sideStoryPackToggle.Pointer) name = "Side stories";
                    else if (toggle != null && story.torporModeToggle != null && toggle.Pointer == story.torporModeToggle.Pointer)
                    {
                        name = "Torpor mode"; role = ElementRole.CheckBox;
                        e.Detail = () => UiUtil.TextOf(story.torporModeTooltipText);
                    }
                    else if (toggle != null && story.skipPrologueToggle != null && toggle.Pointer == story.skipPrologueToggle.Pointer)
                    {
                        name = "Skip prologue"; role = ElementRole.CheckBox;
                    }

                    if (name != null)
                    {
                        e.Role = role;
                        e.Label = () => name;
                        e.State = () => ToggleState(toggle, e);
                        return true;
                    }

                    var button = s.TryCast<Button>();
                    if (button != null && story.nextStoryPackButton != null && button.Pointer == story.nextStoryPackButton.Pointer)
                    {
                        e.Role = ElementRole.Button; e.Label = () => "Next story"; return true;
                    }
                    if (button != null && story.previousStoryPackButton != null && button.Pointer == story.previousStoryPackButton.Pointer)
                    {
                        e.Role = ElementRole.Button; e.Label = () => "Previous story"; return true;
                    }
                    if (button != null && story.buttonText != null && button.transform.IsChildOf(story.buttonText.transform.parent))
                    {
                        e.Role = ElementRole.Button;
                        e.Label = () =>
                        {
                            string text = UiUtil.TextOf(story.buttonText);
                            string cost = UiUtil.VisibleTextOf(story.buttonCostText);
                            return TextUtil.Join(string.IsNullOrEmpty(text) ? "Start story" : text, string.IsNullOrEmpty(cost) ? "" : "cost " + cost);
                        };
                        return true;
                    }
                }
            }
            catch { }

            // The Options page's Apply button (verified field OptionsPage.applyButton).
            try
            {
                var page = s.GetComponentInParent<OptionsPage>();
                if (page != null && page.applyButton != null && page.applyButton.Pointer == s.Pointer)
                {
                    e.Role = ElementRole.Button;
                    e.Label = () => { string own = OwnText(s); return string.IsNullOrEmpty(own) ? "Apply" : own; };
                    e.Hint = "Applies the changed settings. Backspace also applies before leaving Options.";
                    return true;
                }
            }
            catch { }

            // The conversation's minimize button (verified field ConversationPanel.minimizeButton) is an icon.
            try
            {
                var panels = Panels.Instance;
                var minimize = panels != null && panels.ConversationPanel != null ? panels.ConversationPanel.minimizeButton : null;
                if (minimize != null && (s.transform.Pointer == minimize.Pointer || s.transform.IsChildOf(minimize)))
                {
                    e.Role = ElementRole.Button;
                    e.IsConversationMinimize = true;
                    e.Label = () => "Minimize conversation";
                    e.Hint = "Hides the conversation so you can use the map and menus. A Return to conversation button then brings it back. Use Control Enter to press it.";
                    return true;
                }
            }
            catch { }

            var t = s.transform;
            for (int level = 0; level <= TemplateSearchLevels && t != null; level++, t = t.parent)
            {
                var response = t.GetComponent<TemplateConversationResponse>();
                if (response != null)
                {
                    e.Role = ElementRole.Choice;
                    e.Label = () => UiUtil.TextOf(response.text);
                    e.State = () => e.IsInteractable ? "" : "unavailable";
                    var btn = response.responseButton;
                    if (btn != null) e.Activate = () => ActivateDefault(btn);
                    e.Hint = "Enter selects this response.";
                    return true;
                }

                var decision = t.GetComponent<TemplateDecisionOptionButton>();
                if (decision != null)
                {
                    e.Role = ElementRole.DecisionOption;
                    e.Label = () => UiUtil.TextOf(decision.decisionOptionText);
                    e.State = () => e.IsInteractable ? "" : "unavailable";
                    e.Hint = "Enter chooses this option.";
                    return true;
                }

                var multi = t.GetComponent<TemplateMultipleChoiceOption>();
                if (multi != null)
                {
                    var toggle = s.TryCast<Toggle>();
                    e.Role = toggle != null && toggle.group != null ? ElementRole.RadioButton : ElementRole.CheckBox;
                    e.Label = () => UiUtil.TextOf(multi.title);
                    e.State = () => ToggleState(toggle, e);
                    e.Identity = multi.gameObject.Pointer;
                    return true;
                }

                // Character customization rows (verified TemplateCustomizationOption fields). Each row has two
                // arrow buttons for one setting, so both are merged into one selector item.
                var customization = t.GetComponent<TemplateCustomizationOption>();
                if (customization != null)
                {
                    e.Role = ElementRole.Carousel;
                    e.Label = () => UiUtil.TextOf(customization.customizationOptionTitle);
                    e.Value = () => UiUtil.TextOf(customization.carouselText);
                    e.Adjust = step => customization.IncrementIndex(step);
                    e.Activate = () => customization.IncrementIndex(1);
                    e.Identity = customization.gameObject.Pointer;
                    e.Hint = "Left and Right arrows change this feature.";
                    return true;
                }

                var pagedCarousel = t.GetComponent<TemplateCarouselChoiceOption>();
                if (pagedCarousel != null)
                {
                    e.Role = ElementRole.Carousel;
                    e.Label = () => UiUtil.TextOf(pagedCarousel.title);
                    e.Value = () => UiUtil.TextOf(pagedCarousel.carouselText);
                    e.Detail = () => UiUtil.TextOf(pagedCarousel.description);
                    e.Adjust = step => pagedCarousel.IncrementIndex(step);
                    e.Activate = () => pagedCarousel.IncrementIndex(1);
                    e.Identity = pagedCarousel.gameObject.Pointer;
                    e.Hint = "Left and Right arrows change the selection.";
                    return true;
                }

                var carousel = t.GetComponent<CarouselComponent>();
                if (carousel != null)
                {
                    e.Role = ElementRole.Carousel;
                    bool graphFilter = false;
                    try
                    {
                        var gp = Panels.Instance != null ? Panels.Instance.GraphPanel : null;
                        graphFilter = gp != null && gp.filterCarousel != null && gp.filterCarousel.Pointer == carousel.Pointer;
                    }
                    catch { }
                    // The graph's filter (verified field GraphPanel.filterCarousel); the graph is re-read when it changes.
                    e.Label = () => graphFilter ? "Graph filter" : SiblingLabel(carousel.transform, s);
                    e.Value = () => UiUtil.TextOf(carousel.carouselText);
                    e.Adjust = step => carousel.IncrementIndex(step);
                    e.Activate = () => carousel.IncrementIndex(1);
                    e.Identity = carousel.gameObject.Pointer;
                    e.Hint = "Left and Right arrows change the selection.";
                    return true;
                }

                // Newspaper tabs are logos, so their only text is the unread counter. The newspaper's name
                // comes from its database name (verified TemplateNewsCategoryToggle.GetNewsCategoryData()).
                var newsTab = t.GetComponent<TemplateNewsCategoryToggle>();
                if (newsTab != null && level <= 1)
                {
                    var toggle = newsTab.toggle;
                    e.Role = ElementRole.Tab;
                    e.Label = () =>
                    {
                        string name = "";
                        try
                        {
                            var data = newsTab.GetNewsCategoryData();
                            if (data != null) name = TextUtil.CamelToWords(data.NameInDatabase ?? "");
                        }
                        catch { }
                        if (string.IsNullOrWhiteSpace(name)) name = "Newspaper " + (newsTab.currentIndex + 1);
                        return name;
                    };
                    e.Value = () =>
                    {
                        try
                        {
                            int unread = newsTab.GetUnreadNewsCount();
                            return unread <= 0 ? "no unread articles" : unread + (unread == 1 ? " unread article" : " unread articles");
                        }
                        catch { return ""; }
                    };
                    e.State = () => ToggleState(toggle, e);
                    e.Identity = newsTab.gameObject.Pointer;
                    return true;
                }

                var article = t.GetComponent<TemplateNewsArticle>();
                if (article != null)
                {
                    e.Role = ElementRole.Article;
                    e.Label = () => UiUtil.TextOf(article.newsTitle);
                    e.State = () =>
                    {
                        string read = "";
                        try { var d = article.currentNewsData; if (d != null) read = d.IsRead ? "read" : "unread"; } catch { }
                        string expanded = "";
                        try { if (article.toggle != null) expanded = article.toggle.isOn ? "expanded" : "collapsed"; } catch { }
                        return TextUtil.Join(read, expanded);
                    };
                    e.Detail = () => UiUtil.TextOf(article.newsDescription);
                    e.Identity = article.gameObject.Pointer;
                    e.Hint = "Enter expands or collapses the article. F7 reads it.";
                    return true;
                }

                var journalReport = t.GetComponent<TemplateJournalReport>();
                if (journalReport != null)
                {
                    e.Role = ElementRole.Report;
                    e.Label = () => TextUtil.Join(UiUtil.TextOf(journalReport.titleText), UiUtil.TextOf(journalReport.subtitleText));
                    e.State = () => ReportState(journalReport.reportData);
                    return true;
                }

                var interaction = t.GetComponent<TemplateInteractionButton>();
                if (interaction != null)
                {
                    e.Role = ElementRole.MapAction;
                    // Pressing the button alone did not start the event: the game runs its own OnClick(),
                    // which is what a mouse click reaches (verified TemplateInteractionButton.OnClick).
                    e.Activate = () =>
                    {
                        try { interaction.OnClick(); }
                        catch (Exception ex)
                        {
                            ModLog.Exception("interaction-click", ex);
                            ActivateDefault(s);
                        }
                    };
                    e.Label = () => TextUtil.Join(UiUtil.TextOf(interaction.titleText), UiUtil.TextOf(interaction.subtitleText));
                    e.State = () =>
                    {
                        try { return interaction.reportData != null ? ReportState(interaction.reportData) : ""; }
                        catch { return ""; }
                    };
                    return true;
                }

                var hudStat = t.GetComponent<TemplateHUDStat>();
                if (hudStat != null)
                {
                    e.Role = ElementRole.Statistic;
                    e.Label = () => GameText.StatName(hudStat);
                    e.Value = () => GameText.StatValue(hudStat);
                    e.Detail = () => TextUtil.Join(GameText.StatTooltip(hudStat), GameText.StatModifiers(hudStat));
                    return true;
                }

                var effect = t.GetComponent<TemplateTokenEffect>();
                if (effect != null)
                {
                    // Sighted players see the progress as a filling bar; the real number is in the game data
                    // (verified TokenStatusEffectData.ProgressPercentage and its Title/Subtitle).
                    e.Role = ElementRole.StatusEffect;
                    e.Label = () => GameText.EffectTitle(effect);
                    e.Value = () =>
                    {
                        int percent = GameText.EffectPercent(effect);
                        return percent < 0 ? "" : percent + " percent complete";
                    };
                    e.Detail = () => GameText.EffectSubtitle(effect);
                    return true;
                }

                var notification = t.GetComponent<TemplateNotification>();
                if (notification != null)
                {
                    e.Role = ElementRole.Notification;
                    e.Label = () => UiUtil.TextOf(notification.title);
                    e.Detail = () => UiUtil.TextOf(notification.subtitle);
                    return true;
                }

                var conversant = t.GetComponent<TemplateConversant>();
                if (conversant != null)
                {
                    e.Role = ElementRole.Character;
                    e.Label = () => UiUtil.TextOf(conversant.tooltipTitle);
                    // The portrait tooltip's second line (the character's title/role) is read as its value.
                    e.Value = () => UiUtil.TextOf(conversant.tooltipSubitle);
                    // Open this person's Codex entry (verified: TemplateConversant.GetCharacterData(),
                    // CharacterProperties.NoCodexEntry, CodexPanel.Show/GoToCodexEntryByNameInDatabase).
                    e.Activate = () => OpenCodexForCharacter(conversant);
                    e.Hint = "A person in this conversation. Enter opens their Codex entry; Space continues the dialogue.";
                    return true;
                }

                var reportTurn = t.GetComponent<TemplateJournalReportTurn>();
                if (reportTurn != null && level <= 1)
                {
                    var toggle = s.TryCast<Toggle>();
                    e.Role = ElementRole.ExpandableGroup;
                    e.Label = () => UiUtil.TextOf(reportTurn.journalReportTurnTitle);
                    e.State = () => toggle != null ? (toggle.isOn ? "expanded" : "collapsed") : "";
                    return true;
                }

                var journalTurn = t.GetComponent<TemplateJournalTurn>();
                if (journalTurn != null && level <= 1)
                {
                    var toggle = s.TryCast<Toggle>();
                    e.Role = ElementRole.ExpandableGroup;
                    e.Label = () => UiUtil.TextOf(journalTurn.journalTurnTitle);
                    e.State = () => toggle != null ? (toggle.isOn ? "expanded" : "collapsed") : "";
                    e.Detail = () => TextUtil.JoinWith(". ", UiUtil.CollectTexts(journalTurn.transform, true));
                    return true;
                }

                var saveFile = t.GetComponent<TemplateSaveFile>();
                if (saveFile != null)
                {
                    bool isCloud = saveFile.cloudToggle != null && saveFile.cloudToggle.Pointer == s.Pointer;
                    var toggle = s.TryCast<Toggle>();
                    e.Role = isCloud ? ElementRole.CheckBox : ElementRole.RadioButton;
                    e.Label = () =>
                    {
                        string name = TextUtil.Join(UiUtil.TextOf(saveFile.saveFileName), UiUtil.TextOf(saveFile.saveTypeName));
                        return isCloud ? name + ", cloud" : name;
                    };
                    e.State = () => ToggleState(toggle, e);
                    return true;
                }

                var campaign = t.GetComponent<TemplateCampaign>();
                if (campaign != null && campaign.toggle != null && campaign.toggle.Pointer == s.Pointer)
                {
                    var toggle = campaign.toggle;
                    e.Role = ElementRole.RadioButton;
                    e.Label = () => TextUtil.Join(UiUtil.TextOf(campaign.campaignTitle), UiUtil.TextOf(campaign.storyPackTitle));
                    e.State = () => ToggleState(toggle, e);
                    return true;
                }

                var storyPack = t.GetComponent<TemplateStoryPack>();
                if (storyPack != null)
                {
                    var toggle = s.TryCast<Toggle>();
                    e.Role = ElementRole.RadioButton;
                    e.Hint = "A story to play. Enter selects it; the description on this screen then changes.";
                    e.Label = () =>
                    {
                        string own = OwnText(s);
                        return string.IsNullOrEmpty(own) ? TextUtil.FirstLine(GameText.SafeText(() => storyPack.tooltipText.text)) : own;
                    };
                    e.State = () => ToggleState(toggle, e);
                    e.Detail = () => UiUtil.TextOf(storyPack.tooltipText);
                    return true;
                }

                // Icon-only category tabs (Overview, Connections...): the name is in the hover tooltip
                // (verified field RightPanelCategoryToggle.tooltip), which is hidden unless hovered.
                var categoryToggle = t.GetComponent<RightPanelCategoryToggle>();
                if (categoryToggle != null && level <= 1)
                {
                    var toggle = s.TryCast<Toggle>();
                    e.Role = ElementRole.Tab;
                    e.Label = () =>
                    {
                        string own = OwnText(s);
                        if (!string.IsNullOrEmpty(own)) return own;
                        var tip = categoryToggle.tooltip;
                        if (tip != null)
                        {
                            var texts = UiUtil.CollectTexts(tip, includeHidden: true);
                            if (texts.Count > 0) return texts[0];
                        }
                        return HiddenOwnText(s);
                    };
                    e.State = () => ToggleState(toggle, e);
                    return true;
                }

                var overviewEntry = t.GetComponent<TemplateOverviewEntry>();
                if (overviewEntry != null)
                {
                    var toggle = overviewEntry.toggle;
                    e.Role = ElementRole.RadioButton;
                    e.Label = () =>
                    {
                        string shown = UiUtil.TextOf(overviewEntry.entryTitle);
                        return string.IsNullOrEmpty(shown) ? TextUtil.Clean(GameText.SafeText(() => overviewEntry.titleText)) : shown;
                    };
                    e.State = () => ToggleState(toggle, e);
                    e.Detail = () => TextUtil.Clean(GameText.SafeText(() => overviewEntry.descriptionText));
                    e.Hint = "Enter shows its description.";
                    return true;
                }

                var codexEntry = t.GetComponent<TemplateCodexEntry>();
                if (codexEntry != null)
                {
                    e.Role = ElementRole.Button;
                    e.Label = () => UiUtil.TextOf(codexEntry.codexEntryTitle);
                    return true;
                }
            }
            return false;
        }

        private static string ReportState(ReportData data)
        {
            try
            {
                if (data == null) return "";
                return data.IsNotificationActive ? "new" : "";
            }
            catch { return ""; }
        }

        // ------------------------------------------------------------------ standard Unity controls

        private static void ApplyStandardControl(AccessibleElement e, Selectable s)
        {
            e.Label = () => LabelFor(s);

            var input = s.TryCast<TMP_InputField>();
            if (input != null)
            {
                e.Role = ElementRole.EditField;
                e.Value = () =>
                {
                    string v = GameText.SafeText(() => input.text);
                    return string.IsNullOrEmpty(v) ? "blank" : v;
                };
                e.Label = () => InputLabel(input);
                e.SuppressSelectionSync = true;
                e.Activate = () =>
                {
                    var es = EventSystem.current;
                    if (es != null) es.SetSelectedGameObject(input.gameObject);
                    input.ActivateInputField();
                };
                e.Hint = "Enter starts editing. Type, then Enter to confirm.";
                return;
            }

            var dropdown = s.TryCast<TMP_Dropdown>();
            if (dropdown != null)
            {
                e.Role = ElementRole.ComboBox;
                e.Value = () =>
                {
                    string caption = UiUtil.TextOf(dropdown.captionText);
                    int count = dropdown.options != null ? dropdown.options.Count : 0;
                    return count > 0 && ModConfig.AnnouncePositions.Value
                        ? TextUtil.Join(caption, (dropdown.value + 1) + " of " + count)
                        : caption;
                };
                e.Label = () => SiblingLabel(dropdown.transform, s, exclude: dropdown.captionText);
                e.Adjust = step =>
                {
                    int count = dropdown.options != null ? dropdown.options.Count : 0;
                    if (count == 0) return;
                    dropdown.value = Math.Clamp(dropdown.value + step, 0, count - 1);
                };
                e.Activate = () => e.Adjust(1);
                e.Hint = "Left and Right arrows change the value.";
                return;
            }

            var slider = s.TryCast<Slider>();
            if (slider != null)
            {
                e.Role = ElementRole.Slider;
                e.Value = () => SliderValue(slider);
                e.Label = () => SiblingLabel(slider.transform, s);
                e.Adjust = step =>
                {
                    float range = slider.maxValue - slider.minValue;
                    float delta = slider.wholeNumbers ? 1f : range / 20f;
                    slider.value = Mathf.Clamp(slider.value + delta * step, slider.minValue, slider.maxValue);
                };
                e.Activate = null;
                e.Hint = "Left and Right arrows change the value.";
                return;
            }

            var toggleCtl = s.TryCast<Toggle>();
            if (toggleCtl != null)
            {
                if (IsTabToggle(s.transform))
                {
                    e.Role = ElementRole.Tab;
                    e.IsSubTab = IsSubTabToggle(s.transform);
                }
                else if (toggleCtl.group != null) e.Role = ElementRole.RadioButton;
                else e.Role = ElementRole.CheckBox;
                e.State = () => ToggleState(toggleCtl, e);
                if (string.IsNullOrEmpty(OwnText(s)))
                    e.Label = () =>
                    {
                        string hidden = HiddenOwnText(s);
                        return string.IsNullOrEmpty(hidden) ? SiblingLabel(s.transform, s) : hidden;
                    };
                return;
            }

            if (s.TryCast<Scrollbar>() != null)
            {
                e.Role = ElementRole.Item;
                e.Label = () => "Scroll bar";
                return;
            }

            var asButton = s.TryCast<Button>();
            e.Role = asButton != null ? ElementRole.Button : ElementRole.Item;
            e.State = () => e.IsInteractable ? "" : "unavailable";
            if (asButton != null)
            {
                e.Label = () =>
                {
                    string own = OwnText(s);
                    if (!string.IsNullOrEmpty(own)) return own;
                    string hidden = HiddenOwnText(s);
                    if (!string.IsNullOrEmpty(hidden)) return hidden;
                    string action = ActionName(asButton);
                    if (!string.IsNullOrEmpty(action)) return action;
                    // The designer's object name ("Close Button", "Next Button") describes an icon button
                    // better than nearby text such as the location name of a report.
                    string named = MeaningfulObjectName(s.transform);
                    if (!string.IsNullOrEmpty(named)) return named;
                    return SiblingLabel(s.transform, s);
                };
            }
        }

        /// <summary>Second-level tabs: Policies / Situations inside an Overview or Connections category.</summary>
        private static bool IsSubTabToggle(Transform t)
        {
            for (int i = 0; i <= 1 && t != null; i++, t = t.parent)
                if (t.GetComponent<RightPanelSubCategoryToggle>() != null) return true;
            return false;
        }

        /// <summary>Toggle types that Suzerain uses as tabs / category switches (verified class names).</summary>
        private static bool IsTabToggle(Transform t)
        {
            for (int i = 0; i <= 1 && t != null; i++, t = t.parent)
            {
                if (t.GetComponent<NavigationToggle>() != null) return true;
                if (t.GetComponent<RightPanelCategoryToggle>() != null) return true;
                if (t.GetComponent<RightPanelSubCategoryToggle>() != null) return true;
                if (t.GetComponent<TemplateNewsCategoryToggle>() != null) return true;
                if (t.GetComponent<TemplatePanelToggle>() != null) return true;
                if (t.GetComponent<TemplateConnectionCategoryToggle>() != null) return true;
            }
            return false;
        }

        private static string ToggleState(Toggle toggle, AccessibleElement e)
        {
            string s = "";
            try
            {
                if (toggle != null)
                {
                    if (e.Role == ElementRole.Tab || e.Role == ElementRole.RadioButton)
                        s = toggle.isOn ? "selected" : "";
                    else
                        s = toggle.isOn ? "checked" : "not checked";
                }
            }
            catch { }
            if (!e.IsInteractable) s = TextUtil.Join(s, "unavailable");
            return s;
        }

        private static string SliderValue(Slider slider)
        {
            float v = slider.value, min = slider.minValue, max = slider.maxValue;
            if (!slider.wholeNumbers && Math.Abs(min) < 0.0001f && Math.Abs(max - 1f) < 0.0001f)
                return Mathf.RoundToInt(v * 100f) + " percent";
            string value = TextUtil.Number(v);
            if (ModConfig.DetailedNumbers.Value) value += ", range " + TextUtil.Number(min) + " to " + TextUtil.Number(max);
            return value;
        }

        // ------------------------------------------------------------------ labels

        /// <summary>Visible texts inside the control itself.</summary>
        public static string OwnText(Selectable s)
        {
            var texts = UiUtil.CollectTexts(s.transform, false);
            if (texts.Count > 3) texts.RemoveRange(3, texts.Count - 3);
            return TextUtil.Join(texts.ToArray());
        }

        private static readonly HashSet<string> NonDescriptiveMethods = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "SetActive", "Play", "PlayOneShot", "PlaySound", "PlayClickSound", "Invoke", "OnClick", "OnButtonClick", "Select",
          "SetTrigger", "set_enabled", "IncrementIndex", "CycleCarouselForward", "CycleCarouselBackward", "SetSelectedOption" };

        /// <summary>
        /// Name for an icon-only button taken from the game method its click runs (the button's persistent
        /// onClick listeners, as set up by the game's designers), e.g. OnCloseButtonClick -> "Close",
        /// OnHeaderBackClick -> "Header back". Empty if the button has no descriptive listener.
        /// </summary>
        public static string ActionName(Button button)
        {
            try
            {
                var ev = button.onClick;
                int n = ev.GetPersistentEventCount();
                for (int i = 0; i < n; i++)
                {
                    string method = ev.GetPersistentMethodName(i);
                    if (string.IsNullOrEmpty(method) || NonDescriptiveMethods.Contains(method)) continue;
                    string m = method;
                    if (m.StartsWith("On", StringComparison.Ordinal) && m.Length > 2 && char.IsUpper(m[2])) m = m.Substring(2);
                    foreach (var suffix in new[] { "ButtonClicked", "ButtonClick", "ButtonPressed", "Clicked", "Click", "Pressed", "Button" })
                        if (m.EndsWith(suffix, StringComparison.Ordinal) && m.Length > suffix.Length) { m = m.Substring(0, m.Length - suffix.Length); break; }
                    string words = TextUtil.CamelToWords(m);
                    if (words.Length == 0) continue;
                    words = char.ToUpperInvariant(words[0]) + words.Substring(1).ToLowerInvariant();
                    ModLog.InfoOnce("label-from-action:" + UiUtil.PathOf(button.transform), "Icon-only button named from its click method '" + method + "': " + UiUtil.PathOf(button.transform));
                    return words;
                }
            }
            catch { }
            return string.Empty;
        }

        private static readonly System.Text.RegularExpressions.Regex GenericName =
            new System.Text.RegularExpressions.Regex(@"^(button|btn|image|icon|toggle|item|element|background|bg|template|clone|\(clone\)|\d|\s)*$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

        /// <summary>The object's own name as words, unless it is generic ("Button", "Button 1", "Image").</summary>
        public static string MeaningfulObjectName(Transform t)
        {
            try
            {
                string name = TextUtil.CamelToWords(t.name.Replace("(Clone)", ""));
                foreach (var suffix in new[] { " Button", " Btn", " Toggle" })
                    if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) name = name.Substring(0, name.Length - suffix.Length);
                name = name.Trim();
                if (name.Length < 2 || GenericName.IsMatch(name)) return string.Empty;
                ModLog.InfoOnce("label-from-object-name:" + UiUtil.PathOf(t), "Icon-only button named from its object name: " + UiUtil.PathOf(t));
                return name;
            }
            catch { return string.Empty; }
        }

        /// <summary>First text inside the control that is currently hidden (typically its hover tooltip).</summary>
        public static string HiddenOwnText(Selectable s)
        {
            try
            {
                var texts = UiUtil.CollectTexts(s.transform, includeHidden: true);
                return texts.Count > 0 ? texts[0] : string.Empty;
            }
            catch { return string.Empty; }
        }

        private static string LabelFor(Selectable s)
        {
            string own = OwnText(s);
            if (!string.IsNullOrEmpty(own)) return own;
            // Icon-only controls often carry their name in a hidden tooltip inside them.
            string hidden = HiddenOwnText(s);
            if (!string.IsNullOrEmpty(hidden)) return hidden;
            string sibling = SiblingLabel(s.transform, s);
            return sibling;
        }

        private static string InputLabel(TMP_InputField input)
        {
            string placeholder = "";
            try
            {
                var ph = input.placeholder != null ? input.placeholder.TryCast<TMP_Text>() : null;
                placeholder = UiUtil.TextOf(ph);
            }
            catch { }
            string sibling = SiblingLabel(input.transform, input, exclude: input.textComponent);
            return TextUtil.Join(sibling, placeholder);
        }

        private static readonly System.Text.RegularExpressions.Regex NumberOnly =
            new System.Text.RegularExpressions.Regex(@"^[\d\s.,%:+\-/]+$", System.Text.RegularExpressions.RegexOptions.Compiled);

        /// <summary>
        /// Label for controls without their own text (sliders, selectors, check boxes). Looks for the
        /// nearest visible text that is not part of another control, preferring text placed BEFORE the
        /// control in the hierarchy (a row label), searching outwards one level at a time. Pure numbers
        /// (value displays such as "50%") are never used as labels. Falls back to the object name (logged).
        /// </summary>
        public static string SiblingLabel(Transform control, Selectable self, TMP_Text exclude = null)
        {
            try
            {
                // Pass 1: previous siblings, nearest first, at increasing distance. Pass 2: following siblings.
                for (int pass = 0; pass < 2; pass++)
                {
                    var node = control;
                    for (int depth = 0; depth < 3 && node != null && node.parent != null; depth++, node = node.parent)
                    {
                        var parent = node.parent;
                        int index = node.GetSiblingIndex();
                        int count = parent.childCount;
                        if (pass == 0)
                        {
                            for (int j = index - 1; j >= 0; j--)
                            {
                                string t = FirstLabelText(parent.GetChild(j), self, exclude);
                                if (t != null) return t;
                            }
                        }
                        else
                        {
                            for (int j = index + 1; j < count; j++)
                            {
                                string t = FirstLabelText(parent.GetChild(j), self, exclude);
                                if (t != null) return t;
                            }
                        }
                    }
                }
            }
            catch { }

            string name = TextUtil.CamelToWords(control.name);
            foreach (var suffix in new[] { " Button", " Toggle", " Btn", " Slider", " Dropdown" })
                if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) name = name.Substring(0, name.Length - suffix.Length);
            ModLog.InfoOnce("label-from-name:" + UiUtil.PathOf(control), "Control without visible text; using object name as label: " + UiUtil.PathOf(control));
            return name;
        }

        /// <summary>First visible, non-numeric text in a subtree that does not belong to another control.</summary>
        private static string FirstLabelText(Transform subtree, Selectable self, TMP_Text exclude)
        {
            var texts = subtree.GetComponentsInChildren<TMP_Text>(false);
            for (int i = 0; i < texts.Length; i++)
            {
                var t = texts[i];
                if (exclude != null && t.Pointer == exclude.Pointer) continue;
                if (!UiUtil.IsTextVisible(t)) continue;
                var owner = t.GetComponentInParent<Selectable>();
                if (owner != null && owner.Pointer != self.Pointer) continue;
                string s = UiUtil.TextOf(t);
                if (TextUtil.IsDecorative(s) || NumberOnly.IsMatch(s)) continue;
                return s;
            }
            return null;
        }

        /// <summary>True if the Codex entry page is showing an article for this person.</summary>
        private static bool CodexShows(CodexPanel codex, string person)
        {
            try
            {
                var page = codex.codexEntryPage;
                if (!UiUtil.Alive(page)) return false;
                var entry = page.GetCodexEntryData();
                if (entry != null && !string.IsNullOrEmpty(entry.NameInDatabase)) return true;
                string shown = UiUtil.TextOf(page.title);
                return !string.IsNullOrEmpty(shown) && shown.IndexOf(person, StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch { return false; }
        }

        /// <summary>Message spoken by the last Codex attempt, so the focus manager can announce it.</summary>
        public static string LastCodexMessage;

        /// <summary>Panel the focus should move to after the last activation (e.g. the Codex), or null.</summary>
        public static string RequestedRegionId;

        /// <summary>Opens the Codex at a conversation participant's own entry.</summary>
        private static void OpenCodexForCharacter(TemplateConversant conversant)
        {
            LastCodexMessage = null;
            try
            {
                var data = conversant.GetCharacterData();
                var codex = Panels.Instance != null ? Panels.Instance.CodexPanel : null;
                if (data == null || !UiUtil.Alive(codex))
                {
                    LastCodexMessage = "No Codex entry for this person.";
                    ModLog.Info("No Codex entry available for this character.");
                    return;
                }
                var props = data.CharacterProperties;
                if (props != null && props.NoCodexEntry)
                {
                    LastCodexMessage = TextUtil.Clean(data.CharacterName) + " has no Codex entry.";
                    ModLog.Info("This character has no Codex entry.");
                    return;
                }
                if (!codex.IsShowing()) codex.Show();
                // A character's database name is not always the Codex entry's; the game also offers lookup
                // by the entry title, which for people is their name. Both are tried, and the result is
                // checked against CodexEntryPage.title.
                string person = TextUtil.Clean(data.CharacterName);
                codex.GoToCodexEntryByNameInDatabase(data.NameInDatabase);
                if (!CodexShows(codex, person))
                {
                    codex.GoToCodexEntryByTitle(person);
                    ModLog.Info("Codex lookup by database name failed for '" + data.NameInDatabase + "'; tried the title '" + person + "'.");
                }
                // The conversation stays open underneath, so the focus must be moved into the Codex.
                RequestedRegionId = "CodexPanel";
                LastCodexMessage = "Codex: " + person + ". F7 reads the article, Backspace returns to the conversation.";
                ModLog.Info("Opened Codex entry for " + person + ".");
            }
            catch (Exception ex)
            {
                ModLog.Exception("codex-for-character", ex);
                LastCodexMessage = "Could not open the Codex entry.";
            }
        }

        // ------------------------------------------------------------------ activation

        /// <summary>
        /// Activates a control through its own UI events (no mouse simulation):
        /// Button/Toggle -> OnSubmit (runs onClick / toggles, respects interactable),
        /// other selectables -> ISubmitHandler, otherwise IPointerClickHandler.
        /// </summary>
        public static void ActivateDefault(Selectable s)
        {
            var es = EventSystem.current;
            var go = s.gameObject;

            var button = s.TryCast<Button>();
            if (button != null) { button.OnSubmit(new BaseEventData(es)); return; }

            var toggle = s.TryCast<Toggle>();
            if (toggle != null) { toggle.OnSubmit(new BaseEventData(es)); return; }

            if (ExecuteEvents.GetEventHandler<ISubmitHandler>(go) != null)
            {
                ExecuteEvents.Execute<ISubmitHandler>(go, new BaseEventData(es), ExecuteEvents.submitHandler);
                return;
            }
            ExecuteEvents.Execute<IPointerClickHandler>(go, new PointerEventData(es), ExecuteEvents.pointerClickHandler);
        }
    }
}
