# Technical notes

## 1. How the game was analysed

The supplied files were the only source of truth:

| File | Use |
|---|---|
| `interop.zip` (BepInEx `BepInEx\interop`) | Decompiled with the ILSpy decompiler engine into one-line-per-member signature dumps of `Assembly-CSharp`, `Assembly-CSharp-firstpass`, `UnityEngine.UI`, `Unity.TextMeshPro`, `Unity.InputSystem`, `UnityEngine.CoreModule`, `UnityEngine.UIModule` and `Il2Cppmscorlib`. Every game type, field and method named in the source was looked up there. |
| `global-metadata.dat` | String literals. The game's Input System asset ("Suzerain Controls") was extracted from it, giving the exact keyboard bindings the mod must avoid. The file's MD5 is used for version detection. |
| `GameAssembly.dll` | x86-64 PE, which is why a 64-bit speech DLL is required. Its MD5 is used for version detection. |

The interop assemblies were generated with **Il2CppInterop 1.5.3**, which indicates a recent BepInEx 6 build.

The project compiles with **zero errors and zero warnings** against these exact interop assemblies. That is a hard check that every referenced class, field, property and method exists with the used signature. Because the C# compiler resolves all names, **no member name in this mod is guessed**.

Runtime behaviour could not be executed here, since no Windows and no game were available. That is what `TESTING.md` is for.

## 2. Game architecture found

- **Engine:** Unity with the IL2CPP scripting backend. Input goes through the Unity **Input System** package (`SuzerainControls`, `PlayerInputHandler`). Legacy `UnityEngine.Input` is not used by the mod.
- **Codebase:** a unified multi-platform build. It has story packs (Sordland, Rizia, and flags for Galmland and Neutral Lens in `BuildConfiguration`), plus store, ads and account code from the mobile port.
- **Dialogue:** the Pixel Crushers Dialogue System with an Articy importer, in `Assembly-CSharp-firstpass`. Suzerain's own handlers receive the Dialogue System messages and keep the on-screen state:
  - `ConversationHandler` (in `ConversationPanel`)
  - `NarrationConversationHandler` (in `NarrationPanel`)
  - `PrologueEpilogueConversationHandler` (in `PrologueEpiloguePanel`)
- **UI:** uGUI with TextMeshPro. The `Panels` singleton (`Panels.Instance`) holds every panel. Almost every panel exposes `IsShowing()`, and focusable panels expose a `FocusablePanelComponent` through `Focus`. The game has full gamepad UI navigation, so its screens are made of standard `Selectable`s.
- **Game state:**
  - `Managers.Instance` provides `GameFlowManager` (turn and step), `TokenManager` (map tokens) and `MapManager` (Base, World or War map).
  - `Singleton<GameManager>.Instance` provides `GetGameState()` and the input type.
- **Map:** 3D map "tokens" (`Token`, `MapTokenData`) with rich data: relations, capital, president, government, language, military strength, GDP, population, rank, description and status effects. Selecting a token opens `TokenInteractionPanel`, whose actions are story fragments and reports, and `TokenInformationPanel`.
- **Statistics:** `HUDPanel.instantiatedHUDStats` (`TemplateHUDStat.currentValue` is an int) and `instantiatedHUDTextStats`. The text statistics show their state with red, yellow and green colours.

## 3. Architecture of the mod

```
Plugin (BasePlugin)                 -> config, version check, creates AccessibilityManager, injects AccessibilityRunner
AccessibilityRunner (MonoBehaviour) -> Update() every frame -> AccessibilityManager.Tick()
AccessibilityManager                -> runs every subsystem in its own try/catch, dispatches key commands
 |- SpeechManager                   -> the ONLY path to the screen reader (Universal Speech P/Invoke)
 |- KeyboardManager                 -> configurable key chords via Keyboard.current
 |- ScreenTracker                   -> polls Panels.* IsShowing() 10x/s, orders regions, detects screen changes
 |- FocusManager                    -> controls of the active region, focus, activation, Back, selection sync
 |- TextReviewer                    -> Page Up/Down line reading
 |- DialogueAccessibility           -> conversation/narration/prologue lines + responses
 |- ContentAccessibility            -> auto-read of reports/decisions/pop-ups, F7 document reader
 |- StatisticsAccessibility         -> statistics bar, value-change announcements, turn, status
 |- NotificationAccessibility       -> notification announcements, history, open
 |- MapAccessibility                -> keyboard map token browser
 |- InputFieldAccessibility         -> edit mode detection, typing echo
 |- SettingsMenu                    -> F9 speech-only settings + key rebinding
UI helpers: ElementFactory (game templates + Unity controls -> AccessibleElement), UiUtil, TextUtil
Game helpers: PanelRegistry (generated from the interop), SubPages, GameText
```

Design decisions:

- **No Harmony patches.** The mod reads the game's own lists and fields by polling. This cannot break the game's dialogue flow, cannot suffer from IL2CPP inlining, and survives panels being created and destroyed at any time.
- **No mouse simulation.** Controls are activated through their own events:
  - `Button.OnSubmit` and `Toggle.OnSubmit`
  - `ExecuteEvents` submit or pointer-click handlers
  - `TMP_InputField.ActivateInputField`
  - `Slider.value` and `TMP_Dropdown.value`
  - game methods such as `TemplateCarouselChoiceOption.IncrementIndex`, `TokenManager.SelectToken`, `TemplateNotification.OnNotificationClick` and `FocusablePanelComponent.OnCloseButtonClick`

  Nothing depends on screen coordinates.
- **Two-way focus sync with the game's EventSystem.** Tab selects the control in the game, so its hover and tooltip logic runs. When the game moves the selection itself (for example Up and Down on responses), the mod follows and announces it.
- **Region ownership.** A control belongs to the nearest visible registered panel above it, so the same control is never listed in two panels.
- **Robustness:**
  - Every Unity object is checked with `UiUtil.Alive` before use.
  - Every subsystem is isolated by try/catch.
  - Exceptions are logged in full 3 times, then once every 500 occurrences.
  - Unknown controls fall back to generic descriptions, and a control labelled from its object name is logged once.

## 3a. Keyboard input and focus (audited)

- **The mod installs no keyboard hook, registers no raw input, never changes window focus, and never enables or disables the game's input.** A source audit confirms this: the only Win32 function it imports is `FindWindowW`, used to detect JAWS.
- Keys are read by polling Unity's Input System once per frame (`Keyboard.current[key].wasPressedThisFrame` / `isPressed`). This is read-only, so keys always also reach the game and any screen reader. State is read fresh each frame, so nothing can stay "stuck" after focus loss.
- While Insert or Caps Lock (the JAWS or NVDA key) is held, the mod ignores all its commands, so screen reader shortcuts never also trigger mod actions.
- When the game window loses focus, queued actions are dropped: deferred arrow moves, slider steps, and dialogue continue.
- Focus loss and regain are logged with the modifier states the game sees.
- `UniversalSpeech.dll` exports `installKeyboardHook` (a JAWS low-level keyboard hook in its source), but nothing calls it. The mod does not, and a disassembly of the user's `UniversalSpeech.dll` found no internal call to it either.
- **JAWS known issue:** after switching away from Suzerain and back, JAWS stops responding inside the game window only, while working normally elsewhere. This was reproduced with the mod removed from BepInEx\plugins, so it is between JAWS and the game window.
- `DiagnosticKeyPauseAfterFocusSeconds` can disable the mod's key reading for a few seconds after focus returns. It exists to confirm that the mod's key reading has no effect on this issue.

## 4. Game members used

Everything below was verified in the supplied interop assemblies.

- **Panels:** the 65 panel properties listed in `Game/PanelRegistry.cs`, which was generated automatically from the dump, plus `IsAnyInputFieldInFocus()`.
- **Dialogue:**
  - `ConversationPanel.conversationHandler` and `instantiatedConversants` (`TemplateConversant.tooltipTitle`, `tooltipSubitle`)
  - `ConversationHandler.instantiatedConversationTexts`, `instantiatedResponses`, `responsesAreShown` and `continueButton`
  - `TemplateConversationText.conversationText`, `unformattedText` and `speakerName`
  - `TemplateConversationResponse.text` and `responseButton`
  - `NarrationConversationHandler.titleText`, `subtitleText`, `narrationText`, `instantiatedResponses` and `responsesAreShown`
  - `PrologueEpilogueConversationHandler.yearText`, `titleText`, `subtitleText` and `prologueEpilogueText`
- **Decisions:**
  - `DecisionPanel.decisionTitleText`, `decisionDescriptionText`, `instantiatedDecisionOptionButtons` and `currentDecisionData`
  - `TemplateDecisionOptionButton.decisionOptionText`
  - `PagedDecisionPanel.title`, `description`, `currentPageIndex`, `numberOfPages`, `currentTemplatePagedDecisionsPage`, `panelCounterTitle`, `panelCounterValue`, `panelBarTitle`, `panelBarSlider` and `currentPagedDecisionPanelData`
  - `TemplatePagedDecisionsPage.title`, `description` and `choiceAmountText`
  - `TemplateMultipleChoiceOption.title`
  - `TemplateCarouselChoiceOption.title`, `carouselText`, `description` and `IncrementIndex(int)`
  - `CarouselComponent.carouselText` and `IncrementIndex(int)`
- **Reports and news:**
  - `ReportPanel.reportTitleText`, `tokenNameText`, `reportDescriptionText`, `pageCount` and `reportData`
  - `ReportData.IsNotificationActive`
  - `TemplateJournalReport.titleText`, `subtitleText` and `reportData`
  - `TemplateJournalReportTurn` and `TemplateJournalTurn` titles
  - `TemplateNewsArticle.newsTitle`, `newsDescription`, `toggle` and `currentNewsData.IsRead`
- **Map:**
  - `TokenManager.GetOrderedTokenData`, `GetTokenByTokenDataNameInDatabase`, `SelectToken`, `HoverOverToken`, `StopHoverOverToken` and `isTokenSelectionEnabled`
  - the `MapTokenData` fields listed in `Features/MapAccessibility.cs`
  - `MapManager.GetCurrentMapType()`
  - `TokenInformationPanel.title`, `subtitle`, `eventText`, `instantiatedInfoTexts` and `instantiatedTokenEffects`
  - `TemplateInteractionButton.titleText`, `subtitleText` and `reportData`
- **Statistics:**
  - `HUDPanel.instantiatedHUDStats` and `instantiatedHUDTextStats`
  - `TemplateHUDStat.statText`, `statTooltipText`, `statTooltipModifierText`, `currentValue` and `currentHUDStatData`
  - `HUDStatProperties.Title`, `HasMaxValue` and `MaxValue`
  - `TemplateHUDTextStat.statNameText`, `statValueText`, `red`, `yellow` and `green`
- **Menus:**
  - `EscapeMenuPanel.OnHeaderBackClick()` and `OnReturnClick()`
  - `MainMenuPanel.IsMainPageShowing()` and `OnHeaderBackClick()`
  - `ConfirmationPanel.mainText`
  - page classes `OptionsPage`, `LoadGamePage`, `SaveGamePage`, `CollectionsPage` and others (see `Game/SubPages.cs`)
  - `TemplateSaveFile`, `TemplateCampaign`, `TemplateStoryPack` and `TemplateCodexEntry` label fields
- **Notifications:** `NotificationPanel.instantiatedNotifications`, and `TemplateNotification.title`, `subtitle` and `OnNotificationClick()`
- **State:** `Managers.Instance.GameFlowManager.currentTurnNo` and `currentTurn.TransitionTitle`, and `Singleton<GameManager>.Instance.GetGameState()`

## 5. Assumptions and limitations

These points could not be decided from static inspection alone, or are not implemented yet:

1. **Enter in "Auto" mode relies on the game's EventSystem delivering Submit to the selected object.** The startup log records the input module type and `sendNavigationEvents`. If Enter does nothing, switch the setting to "always handled by the mod", or use Numpad Enter.
2. **Statistic names are taken from the first line of the statistic's tooltip text** (`statTooltipText`). The fallback is `HUDStatProperties.Title`, then the database name. If a name sounds wrong, report it with the log.
3. **"New" on reports means the game's notification marker** (`IsNotificationActive`), which is what sighted players see as the new-item indicator. `ReportData.IsDone` exists but its meaning could not be confirmed, so it is not announced.
4. **Dialogue lines are read once the visible text contains the end of the line's `unformattedText`,** so the typewriter animation has finished. If the visible text never matches, the speaker name plus the unformatted text is spoken after 2.5 seconds.
5. **War map (Rizia).** The war map uses a separate tile system (`WarTileManager`, `WarTokenManager`) that is operated by pointing at tiles. The war panels (turn, actions, production, deployment, unit selection and statistics) are reachable with Tab and Ctrl+Tab. Moving units tile by tile with the keyboard is **not implemented** yet; the F6 map browser says so on the war map.
6. **Graphs.** The ethnicity and religion pie charts of `TokenInformationPanel` are read from their chart data (`ChartUtil.ChartData`), with percentages as each group's share of the chart; if a chart has not been drawn yet, `MapTokenData.DemographicsProperties` is used instead. Line graphs (`GraphPanel`, for example Economic Stability) are read from `GraphPanel.graphInput`: the plotted values from oldest to current, with the current value, its trend, the highest and lowest values, and the red danger line. The danger line comes from the Dialogue System variable named in `GraphPanelProperties.DangerThresholdVariable`. The numbers are the game's internal values behind the chart, whose axis only says High and Low.
7. **Keyword hyperlinks inside texts.** The game opens these through a gamepad-only action. The codex itself is fully reachable from the navigation bar.
8. **Population and GDP in the map browser are raw game values without units.** The location information panel, reached by selecting the location, shows the game's own formatted values, and those are read with F7.
9. **Decision costs and consequences** are not shown to players. The game's decision data (`DecisionProperties.DecisionOption`) contains only `Text`, plus two internal Dialogue System scripts: `Condition` (when an option is available) and `Instruction` (its hidden effects). The mod reads the option text and says "unavailable" for options that cannot be chosen, but deliberately does not read the hidden scripts. They are internal code, and they would reveal outcomes that sighted players do not see.
10. **Game version.** `Application.version` is logged. Compatibility is decided by the file hashes, not by that string.

## 6. Universal Speech

The official 1.0.0 binary is 32-bit. `native\UniversalSpeech.dll` was compiled for x86-64 from the official GitHub source using MinGW-w64 GCC. Two source changes: `src/windows/nvda.c` also tries `nvdaControllerClient64.dll` on 64-bit, and `composePath` in `src/windows/misc.c` was fixed (the original only worked with Microsoft's C runtime, so NVDA was never found in the first build). See `native\BUILD-NOTES.md`.
