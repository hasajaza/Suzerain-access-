# Changelog

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
