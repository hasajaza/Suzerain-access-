# Test plan

Do every test **with the monitor off, or without looking**, using only the keyboard and the screen reader.

Before starting, open **F9** and turn on **"Debug logging"**. Leave it on for the whole session. Afterwards, send `BepInEx\LogOutput.log` together with a note of any step that failed.

Each step lists what you should hear or experience. A step fails if you hear nothing, the wrong thing, the same thing twice, or if you need the mouse.

## A. Startup and speech

| # | Step | Expected |
|---|---|---|
| A1 | Start the game with NVDA (or JAWS) running. | "Suzerain Access loaded. Press F1 for help, F9 for settings." |
| A2 | Look at the log. | Lines for the mod version, the BepInEx version, "Universal Speech loaded ... Active engine: NVDA" (or JAWS), the Suzerain version and Unity version, "EventSystem present ...", "Accessibility system initialized", and "Game build verified". |
| A3 | Press F1. | All key bindings are spoken. |
| A4 | Press F8. | The last announcement is repeated. |
| A5 | Press F11 during long speech. | Speech stops. |
| A6 | Close the screen reader and restart the game. | SAPI speaks, unless SAPI is turned off in F9. |

## B. Main menu

| # | Step | Expected |
|---|---|---|
| B1 | Wait at the main menu. | "Main menu, N controls." Then the focused button, for example "Continue, button, 1 of N". |
| B2 | Tab and Shift+Tab through the whole menu. | Every button has a readable name. Disabled buttons say "unavailable". "Top" or "Bottom" is heard when the list wraps. |
| B3 | Home, then End. | First and last buttons. |
| B4 | Enter on Options. | "Options page", then the first option. |
| B5 | Tab to a volume slider and press Left and Right. | The new value, for example "50 percent". The volume audibly changes. |
| B6 | Change a combo box, for example the language or resolution, with Left and Right. | The new value and its position ("2 of 5"). |
| B7 | Press Backspace. | Back to the main page, announced. |
| B8 | Enter on Load. | "Load game page". Campaigns and saves are read with their names. |
| B9 | Press Backspace. | Main page. |
| B10 | Press F2, then Page Down several times. | All text, then line by line. |

## C. New game

| # | Step | Expected |
|---|---|---|
| C1 | New Game. | "Story selection". Story packs, toggles such as "skip prologue", and next and previous buttons are all reachable and named. |
| C2 | Choose a story and continue to archetype selection, then confirmation. | Every screen is announced. Confirmation dialogs read their main text automatically. |
| C3 | Start the campaign. | The prologue or epilogue text is read automatically. Responses are announced with their count. |

## D. Dialogue

| # | Step | Expected |
|---|---|---|
| D1 | A conversation starts. | "Conversation." Then each line with its speaker, read once, in order, without cutting each other off. |
| D2 | Press Space. | The next line is read. |
| D3 | Responses appear. | "3 responses. Response 1 of 3: ...". |
| D4 | Up and Down arrows. | Each response is read, with its position. |
| D5 | Tab and Shift+Tab. | The same responses are reachable. |
| D6 | Press Enter on a response. | That response is chosen, **once**. The next lines are read. |
| D7 | Shift+F3. | All responses, with "unavailable" marked where it applies. |
| D8 | F3. | The last line again. |
| D9 | F7 during a conversation. | The participants and the last line. |
| D10 | A narration scene. | The title, subtitle and narration text are read. |
| D11 | Load a save made in the middle of a conversation. | Only the latest line is read, not the whole history. |

## E. Free roam, statistics and turns

| # | Step | Expected |
|---|---|---|
| E1 | After a story event, in free roam. | The top panel is announced. Ctrl+Tab cycles through the statistics bar, navigation bar, continue button, side panel and other open panels. |
| E2 | F4. | Every statistic with its value, maximum and modifiers. Coloured text statistics say "shown in red", "yellow" or "green". |
| E3 | Make a choice that changes a statistic. | For example "Budget plus 1, down from plus 3." |
| E4 | End the turn with the continue button. | "Turn N ..." is announced. |
| E5 | Shift+F4. | The turn, game state, map type and open panels. |

## F. Decisions, bills and decrees

| # | Step | Expected |
|---|---|---|
| F1 | A decision opens. | "Decision: title. description. N options." Tab reaches each option. Enter chooses it. |
| F2 | A multi-page decision, such as budget or policy pages. | The page title, "Page 1 of N", the description, counters and bar values. Selectors change with Left and Right, and the new value is spoken. Multiple-choice options say checked or not checked. Next, back and finish buttons work. |
| F3 | Open reusable and one-time decrees from the navigation bar. | The decree list and details pages are announced. Decrees can be activated. |
| F4 | A bill panel. | Read automatically. Its options are reachable. |

## G. Reports, journal, news, codex

| # | Step | Expected |
|---|---|---|
| G1 | Select a location that has a report (map, section H), then activate a report action. | "Report: title, location, report 1 of 3, new." Then the text. |
| G2 | Left and Right arrows in a report. | The next or previous report is read automatically. |
| G3 | Backspace. | The report closes, and focus returns. |
| G4 | Journal from the navigation bar. | "Journal". The Reports, Decisions and Notepad tabs are reachable. Turn groups say expanded or collapsed. Reports open with Enter. |
| G5 | Notepad: Enter on the text field and type. | "Editing", then each typed character. Tab leaves the field. |
| G6 | Newspaper. | Category tabs, and articles with "read" or "unread". Enter expands an article, and F7 reads it. |
| G7 | Codex. | Topics and entries are reachable. "Codex entry page" is announced. |
| G8 | Connections and Overview. | Pages and entries are reachable and named. |

## H. Map

| # | Step | Expected |
|---|---|---|
| H1 | F6 in free roam. | "Map browser. Base map. N locations." Then the first location with its type and relations. |
| H2 | Tab through the locations. | The name, type, subtitle, relations (for countries), and "N of M". |
| H3 | Page Down. | The filter changes to countries, cities, locations or events. |
| H4 | F7. | The details listed in the keyboard reference. |
| H5 | Enter. | "Selected X". The location panel is announced, and Tab moves through its actions and the information button. |
| H6 | Open location information. | The title and subtitle are read. F7 gives all information rows and status effects. Left and Right cycle locations. |
| H7 | Switch to the world map with the map button in the statistics bar (Ctrl+Tab to the bar, then Tab to the button). | F6 lists the world map locations. |

## I. Save, load and pause

| # | Step | Expected |
|---|---|---|
| I1 | Escape. | "Pause menu" and its buttons. |
| I2 | Save: type a name and save. | Typing is echoed. The save succeeds. A confirmation, if one appears, is read. |
| I3 | Load: pick a save and load it. | The game loads and the new screen is announced. |
| I4 | Backspace in a pause sub-page, then Backspace on the pause menu. | Back to the pause menu, then the game resumes. |

## J. Notifications and pop-ups

| # | Step | Expected |
|---|---|---|
| J1 | A notification appears. | "Notification: title, subtitle", read once. |
| J2 | F5, Shift+F5, Ctrl+F5. | The latest notification, the history, and opening the notification. |
| J3 | Tutorial pop-ups. | Read automatically. Next, back and close buttons work. |

## K. Robustness

| # | Step | Expected |
|---|---|---|
| K1 | Play for 30 minutes. | No repeated announcements of unchanged text, and no speech every frame. |
| K2 | Check the log size. | No flood. Repeated errors appear at most 3 times, then once every 500. |
| K3 | Alt+Tab away from the game and back. | Nothing breaks. |
| K4 | F9: change settings, restart the game. | The settings persist. |
| K5 | F9: rebind a key, for example F2 to Ctrl+R. Test it and restart. | The new key works and persists. |
| K6 | Verbosity Low, then High. | Low: names and values only. High: adds hints such as "Enter selects this response". |

## L. Rizia war content (if you own it)

| # | Step | Expected |
|---|---|---|
| L1 | War turn. | The war panels (turn, actions, production, deployment) are reachable with Ctrl+Tab and Tab. |
| L2 | F6 on the war map. | A clear message that the war map is not supported by the browser. This is a known limitation. |
