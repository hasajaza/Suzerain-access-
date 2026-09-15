# Keyboard reference

All mod keys can be changed:

- in game, with **F9 > "Key: ..."**: press Enter on the item, then press the new key or combination. Backspace cancels. Left or Right resets the key to its default.
- in `BepInEx\config\com.suzerainaccess.mod.cfg`, section `[Keys]`. The format is an optional `Ctrl+`, `Shift+` or `Alt+`, followed by a Unity Input System key name, for example `Ctrl+F2`, `PageDown`, `Home` or `Backquote`. All default keys work on laptop keyboards without a numeric keypad.

Keys refer to **physical key positions**, as in the Unity Input System. The defaults use only keys that are the same on every layout (Tab, Home, End, Page keys, F-keys, arrows, Enter, Backspace), so AZERTY and QWERTY behave identically.

## How the defaults avoid the game's own controls

The game's own control map was read from the game files. Its keyboard bindings are:

| Game action | Keys |
|---|---|
| Submit | Enter |
| Continue dialogue | Enter, Space |
| Pause menu | Escape |
| Navigate responses | Up, Down |
| Cycle panel content (reports, location panels) | Left, Right, A, D |
| Move map | W, A, S, D |
| Switch language | Q, E |

The mod does not use any of these, with three deliberate exceptions:

- **Enter.** When the game's own selection is already on the focused control, the mod lets the game's Submit press it. Otherwise the mod presses it itself. Either way the control is pressed exactly once. If Enter ever does nothing, use **Ctrl+Enter**, which is always handled by the mod, or set "Enter key activation" to "always handled by the mod" in F9.
- **Up and Down.** The game only uses them to move between dialogue responses. When you press one, the mod first lets the game react. If the game moves its selection (for example between responses), the mod announces the new item. If the game does nothing (for example in menus), the mod moves the focus itself. Either way you move exactly one step.
- **Left and Right.** On a slider the game may also react to these; the mod lets the game go first and only steps the slider itself if the game did not, so each press is one step. The mod only uses these when the focused control is a slider, selector or combo box. Otherwise they reach the game, for example to switch reports.

## Navigation

| Default | Command | Notes |
|---|---|---|
| Down arrow or Tab | Next control | Wraps around, and says "Top" when it does. |
| Up arrow or Shift+Tab | Previous control | Says "Bottom" when it wraps. |
| Home / End | First / last control | |
| Enter | Activate | Buttons, check boxes, tabs, responses, decision options, reports, map actions. On a text field, starts editing. |
| Ctrl+Enter | Activate (always by the mod). Use this on keyboards without a numeric keypad, or where Enter belongs to the game. |
| Right / Left | Change value | Sliders, selectors (paged decisions) and combo boxes. |
| Backspace | Back / close | Options page: the first Backspace applies your changes (the game's Apply), the second leaves. Pause menu: back to the menu, or resume. Main menu: back from a sub-page. Other panels: the game's own close action. Conversations cannot be closed. |
| Ctrl+1 | Main content | The conversation, decision, report, newspaper, pop-up question or menu in the middle of the screen. |
| Ctrl+2 | Side panel | Journal, codex, overview, connections, location panel, character or country details. |
| Ctrl+3 | Navigation bar | The buttons that open the journal, codex, overview... |
| Ctrl+4 | Statistics bar | |
| Ctrl+5 | Continue button panel | Ending the turn, or returning to a minimized conversation. |
| Ctrl+6 | Other bars | Progress (ongoing projects), your own details, turn cost. If a kind of panel is not open, the mod says so and focus stays where it is. |
| Ctrl+Tab / Ctrl+Shift+Tab | Next / previous panel | Several panels are often open at once, for example the statistics bar, navigation bar, side panel and continue button. |
| Page Down / Page Up | Next / previous line of text | Reads the current panel's text one line at a time. If you moved to a control with Tab or the arrows, reading starts at that control's text, not at the top of the panel. In the map browser these keys change the filter instead. |
| Ctrl+Page Down / Ctrl+Page Up | Next / previous heading | Jumps between titles in the panel text. A heading is text shown larger than the panel's normal text, or short bold text. |
| Ctrl+Home / Ctrl+End | First / last line | |

## Reading

**Reading long panels without starting at the top:**

- **Jump to what you opened.** After you press Enter on something that shows more text (a codex entry, a newspaper article, a journal turn), the new text is read automatically, and Page Down continues from there. Longer new text is announced as "N new lines of text"; press Page Down to read it. You can turn this off in F9: "Read new text after activating something".
- **Start at a control.** Move to a control with Tab or the arrows, then press Page Down: reading begins at that control.
- **Jump by headings.** Ctrl+Page Down and Ctrl+Page Up move between titles.
- **Read the main content.** F7 reads just the main content of reports, decisions, articles and location information, without the rest of the panel.


| Default | Command |
|---|---|
| F1 | Speak all key bindings. |
| F2 | Read all visible text of the current panel. |
| Shift+F2 | List the open panels and the current one. |
| F3 | Repeat the current dialogue or narration line. |
| Shift+F3 | Read every available response, with numbers and availability. |
| F4 | Read the statistics bar: every value, its maximum and modifiers, numbered 1, 2, 3... |
| Alt+1 to Alt+9 | Read only statistic 1 to 9, in the order F4 numbers them (for example Alt+1 = the first statistic). Uses the physical number keys, so it works on AZERTY too. |
| Alt+0 | Current turn number and the turn's title. |
| Shift+F4 | Turn number, game state, map type and open panels. |
| F5 | Latest notification. |
| Shift+F5 | The last five notifications. |
| Ctrl+F5 | Open the latest notification. This is the same as clicking it. |
| Shift+F6 | What can I do now: the map locations with an event waiting or new information (the markers sighted players see above locations), and whether the Continue button is available. |
| F7 | Read the current document: report (title, location, page, new or read, text), decision (title, description, number of options), paged decision (page, counters), news article, location information, or conversation participants. |
| F8 | Repeat the last announcement. |
| Shift+F8 | Describe the focused control in detail, including tooltip text. |
| Ctrl+F9 | Turn the whole mod off or on. While off, the mod does nothing at all: no speech, no keys (except Ctrl+F9), no changes to the game's selection, and no connection to JAWS. |
| F11 | Stop speech. |
| Shift+F11 | Switch the speech engine. Each press goes to the next choice: Automatic, then every screen reader or voice that is running right now (for example NVDA, JAWS, SAPI5 for the Windows voice), then back to Automatic. The mod cannot start or close screen readers; it only chooses among the running ones. |
| Ctrl+F11 | Reconnect speech and return to automatic engine choice. The mod also reconnects to JAWS by itself when the game window gets the focus back, and when JAWS is restarted. |

## Advancing the story in free roam

After the prologue, the game shows markers above map locations where something is waiting. The mod announces these as "Event waiting at ...". To play them:

1. Press **F6** to open the map browser. It says how many events are waiting and where, and **starts on the first location with an event**.
2. Press **Enter** to select that location. Its panel opens.
3. Use the **arrows or Tab** to find the action (a conversation or report), then press **Enter**.
4. If several events are waiting, **Ctrl+Tab** in the map browser jumps to the next location with an event.

When the current step is complete, the **Continue** button appears, and the mod announces it. Press Ctrl+Tab until you hear "Continue button", then Enter. **Shift+F6** tells you at any time what is waiting.

## Conversations

- **Enter or Space** continue the dialogue. The mod first lets the game react; if the game does not continue, the mod continues through the game's own continue function.
- When responses are offered, **Up and Down arrows** (or Tab) choose one, and **Enter** selects it.
- If you move to a participant's portrait or to the **Minimize conversation** button yourself, Enter presses it. As soon as a new line appears, Enter goes back to continuing the dialogue.
- On a participant's **portrait**, **Enter** opens that person's Codex entry, where F7 reads the whole article and Backspace returns to the conversation. Space still continues the dialogue.
- **Ctrl+Enter** always presses the focused control.
- **Page Up** reads the newest dialogue line; each further Page Up goes one line back through the conversation, and Page Down goes forward again. F3 repeats the newest line.
- Minimizing hides the conversation so you can use the map and menus. The **Return to conversation** button (Ctrl+Tab to the continue button panel) brings it back.

## Main map and world map

The game has a main map (your country) and a world map. **Ctrl+F6** switches between them. It presses the game's own map button in the statistics bar, and then announces "World map" or "Main map". The map button can also be reached with Ctrl+4 and the arrows. After switching, **F6** browses the locations of the map you are on. Switching is not possible while the game has the map button disabled, for example during a conversation; the mod says so.

## Map browser (F6)

The browser lists only the locations of the map you are on: your country's locations on the main map, the world's locations on the world map. If an event is waiting on the other map, the browser says so when it opens; Ctrl+F6 switches maps.

| Key | Action |
|---|---|
| Down / Up arrow, or Tab / Shift+Tab | Next / previous map location. |
| Home / End | First / last location. |
| Ctrl+Tab / Ctrl+Shift+Tab | Jump to the next / previous location with an event waiting (then new information). |
| Page Down / Page Up | Change the filter: all, locations with events or new information, countries, cities, locations, event locations. The browser always opens on 'all'. |
| Enter | Select the location. The game opens its location panel, and Tab then moves through its actions (conversations, reports, information). |
| F7 | Details from the game data: relations, capital, president, government, language, military strength, population, GDP, GDP rank, description, number of status effects. |
| Backspace or F6 | Close the browser. |

Each location also says where it is: its region of the map ("in the north-west of the map", "in the centre of the map"), and for cities and other locations the compass direction from the capital ("south-east of" followed by the capital's name). North is the top of the map as the game shows it. This is spoken at Normal and High verbosity; F7 repeats it.

## Settings (F9)

| Key | Action |
|---|---|
| Down / Up arrow, or Tab / Shift+Tab | Move between items. |
| Left / Right | Change the value. |
| Enter | Toggle, or start key rebinding. |
| F9 or Backspace | Close. Settings are saved immediately. |
