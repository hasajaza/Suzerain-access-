# Changelog

## 1.1.0 (stable)
Same code as 1.0.53, released as the first stable version after play testing. Changes since 1.0.36:
- Map: events start correctly from the map browser; the browser lists only the current map and opens on the waiting event; Ctrl+F6 switches maps; locations give their position and direction from the capital.
- Tabbed panels (Connections, Overview, journal, newspaper) list only the open tab, and choosing a tab moves to its contents.
- Progress, statistics, graphs and demographics are read with real numbers.
- Codex entries open from conversations and from Connections, and closing a panel returns to where it was opened from.
- Many names corrected (icon-only buttons, newspapers, story selection, character customization) and keys made laptop-friendly.

## 1.0.53
- Closing a panel returns the focus to the panel it was opened from, so closing a Codex entry opened from Connections (or from a conversation) goes back there.

## 1.0.52
- Location information: the two arrow buttons are named "Previous location" and "Next location" instead of "Cycle token information".

## 1.0.51
- Tabbed panels (Connections, Overview, journal...) now list only the open tab's contents. The other tabs' entries, which the game keeps alive but switched off, are no longer read as "unavailable" items.
- F2 and line-by-line reading follow the same rule.

## 1.0.50
- Map location actions (events, reports) now start properly: the game's own click method is called, as a mouse click does. Pressing the button alone removed the action without starting it.

## 1.0.48
- Progress: the percentage is now found even when the game's own lists are empty, falling back to how far the bar is filled. F7 lists every project.
- Backspace on a fixed bar explains that it cannot be closed.

## 1.0.47
- Progress: ongoing projects are read with their percentage ("H-3 Highway, 40 percent complete"), and F7 lists them all.
- Status effects everywhere now include their progress percentage.
- Ctrl+6 reaches the remaining bars (progress, your own details, turn cost); Ctrl+3, Ctrl+4 and Ctrl+5 also reach their war equivalents.

## 1.0.46
- Overview and Connections: choosing a category (Economy, Law...) now moves to its Policies / Situations tabs instead of skipping them; choosing one of those moves to its entries.

## 1.0.45
- Choosing any category tab (Overview, Connections, newspaper, journal...) moves straight to its first entry and says how many there are.
- A toggle no longer answers with a bare "selected": its name is included.
- Overview: the selected policy or situation is read automatically when it changes.

## 1.0.44
- Newspaper: the newspaper tabs now come before the articles, and choosing a newspaper moves straight to its first article.

## 1.0.43
- Newspaper tabs are named after the newspaper instead of showing only their unread counter, and the count is spoken as "N unread articles".

## 1.0.42
- Codex entries opened from a conversation now really show that person's article: the game is asked by database name and, if that finds nothing, by title.
- A Codex article is read automatically when it opens, and F7 reads its title and text wherever the focus is.

## 1.0.41
- Opening a person's Codex entry from a conversation now moves the focus into the Codex, and F7 reads the article whenever the Codex is open, even with the conversation still underneath.

## 1.0.40
- Conversations: Enter on a participant's portrait now opens that person's Codex entry (Space continues the dialogue). The result is spoken, including when a person has no entry.

## 1.0.39
- Conversations: Ctrl+Enter on a participant's portrait opens that character's Codex entry; plain Enter always continues the dialogue instead of pressing the portrait.
- F7 reads the whole Codex article.

## 1.0.38
- Statistics are read properly: "Government Budget: minus 7, range minus 20 to 30" instead of repeating the range inside the name and reading an internal variable name as the maximum.
- Empty statistic widgets and "None" placeholders are skipped.

## 1.0.37
- The store and profile overlay of the mobile port is no longer read while the game is loading ("3-day ticket", "Torpor Account", "Panel Title" slider).
- While no game panel is open, only controls that can actually be used are offered, and the mod says the game is still loading.
- The second activation key is now Ctrl+Enter instead of Numpad Enter, so it works on laptop keyboards without a numeric keypad.

## 1.0.33
- JAWS: the cause of JAWS going silent in the game was found. JAWS Sleep Mode was enabled for "Unity Player"; see docs/TROUBLESHOOTING.md for the fix.
- Player package: one complete zip with BepInEx, the mod, the speech files and an installer (Install.cmd) that finds the game folder automatically.
- The automatic JAWS reconnection when returning to the game is now off by default (setting JawsReconnectOnFocus), so "JAWS connected" is no longer spoken after every window switch.

## 1.0.32
- Map browser: each location says its region of the map and, for cities, its direction from the capital.
- Map browser lists only the locations of the map you are on; events on the other map are announced.
- Ctrl+F6 switches between the main map and the world map.
- Shift+F11 switches the speech engine; Ctrl+F9 turns the mod off and on.
- Graphs (such as Economic Stability) are read: values, trend and danger line.
- Ctrl+1 to Ctrl+5 jump to panel kinds; Alt+1 to Alt+9 read single statistics; Alt+0 reads the turn.
- Icon-only buttons and tabs are named from their tooltips, click actions or object names.
- Reports announce "archived"; "new" markers are read everywhere.
- JAWS: verified speech routes (direct, 32-bit bridge, Universal Speech), reconnection, SAPI fallback.
- Conversations: Enter/Space continue, responses first, no double reading.
- Many fixes from play testing: arrow navigation, duplicate announcements, panel detection, Options apply on Backspace.

## 1.0.0
- First release.
