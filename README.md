# Suzerain Access

A screen-reader accessibility mod for the Steam version of **Suzerain**. It is built for keyboard and screen reader use, with no mouse needed.

It is a BepInEx 6 (IL2CPP) plugin. All speech goes through **Universal Speech**, which talks to NVDA, JAWS or other supported screen readers, and falls back to Windows SAPI when no screen reader is running.

The mod was built and verified against one exact game build:

- `GameAssembly.dll`: 75,134,464 bytes, MD5 `4966c5a0e8d51db95efddcd280cf2c3b`
- `global-metadata.dat`: 17,847,660 bytes, MD5 `320a4959bca3dfd31455d0c0545561f6`

At startup the mod hashes your files. If they differ, it writes a warning to the log and speaks one as well. It is **not** claimed to be compatible with any other build.

---

## What you get

- **Final DLL:** `SuzerainAccess.dll`, placed in `<Suzerain folder>\BepInEx\plugins\`.
- **Speech files:** `UniversalSpeech.dll` (64-bit build, included in `native\`) and `JawsBridge32.exe` (the JAWS helper, only started when needed), plus for NVDA users `nvdaControllerClient.dll` (not included, see below). Place them together, next to `Suzerain.exe` or next to `SuzerainAccess.dll`.

## Requirements

1. Suzerain (Steam, Windows, 64-bit), the build described above.
2. **BepInEx 6 IL2CPP** installed in the game folder. The game must have been started once, so that `BepInEx\interop` and `BepInEx\core` exist. The mod compiles against *your* copies of those folders.
3. **Visual Studio 2022** with the ".NET desktop development" workload (this includes the .NET SDK). The project targets `net6.0`, which is the runtime BepInEx 6 uses. Visual Studio downloads the .NET 6 reference pack automatically on the first build.
4. A screen reader. NVDA and JAWS are supported through Universal Speech. SAPI is used as a fallback.

The mod has no NuGet packages and no Harmony patches. Every reference comes from your game folder.

## Build (Visual Studio)

1. **Close the game.** Windows does not allow replacing the plugin while the game is running.
2. Open `SuzerainAccess.sln` and choose **Build > Rebuild Solution**.
3. The build copies **`SuzerainAccess.dll` straight into `<Suzerain folder>\BepInEx\plugins\`**, and `JawsBridge32.exe` next to `Suzerain.exe`. The Output window confirms it with "SuzerainAccess: plugin copied to ...". Your own speech files next to `Suzerain.exe` (`UniversalSpeech.dll`, `nvdaControllerClient.dll`, `ZDSRAPI.dll`) are never overwritten.
4. If the build says it cannot find the game, put your game folder between `<SuzerainDir>` and `</SuzerainDir>` in `SuzerainAccess\GamePath.props`.
5. If the build warns that it could not copy the plugin, close the game and build again. If that still fails, start Visual Studio with "Run as administrator", or copy `SuzerainAccess\bin\Release\SuzerainAccess.dll` by hand.

To turn the automatic copy off, set `DeployToGame` to `false` in `GamePath.props`.

## Install (first time only)

Put `UniversalSpeech.dll` (in `SuzerainAccess\bin\Release\` after a build) and, for NVDA, `nvdaControllerClient.dll` next to `Suzerain.exe`. Everything else is copied by the build.

## Universal Speech setup

The official Universal Speech 1.0.0 release contains only a **32-bit** DLL, which cannot load into 64-bit Suzerain. This project therefore ships `native\UniversalSpeech.dll`, compiled for x64 from the official MIT-licensed source. `native\BUILD-NOTES.md` explains exactly how it was built and the one small change made.

- **JAWS:** needs **no DLL**. JAWS provides its speech API as a Windows COM object that is installed with JAWS. The mod tries three routes and uses the first that works: Universal Speech, a direct COM connection, or the included 32-bit helper `JawsBridge32.exe` (deployed automatically). The log shows which route was chosen, and Shift+F4 names it.
- **NVDA:** the 64-bit `nvdaControllerClient.dll` (from NV Access's NVDA Controller Client package) must be next to `UniversalSpeech.dll`. It is not part of this repository. If you put it in `native\`, the build copies it too. The first release of this mod had a bug in its `UniversalSpeech.dll` that prevented NVDA from being found, so it always fell back to SAPI. This is fixed and was verified; see `native\BUILD-NOTES.md`.
- **No screen reader:** Windows SAPI is used, unless you turn off "Use Windows SAPI" in the mod settings (F9).
- The speech rate setting only affects engines that report rate support, usually SAPI. Screen readers keep their own rate.

## Keyboard

The full reference is in [`docs/KEYBOARD.md`](docs/KEYBOARD.md). Every key can be changed in the settings menu (F9) or in `BepInEx\config\com.suzerainaccess.mod.cfg`.

| Key | Action |
|---|---|
| Down / Up arrow, or Tab / Shift+Tab | Next / previous control |
| Home / End | First / last control |
| Enter (or Numpad Enter) | Activate |
| Left / Right | Change a slider, selector or combo box |
| Backspace | Back / close panel |
| Ctrl+Tab / Ctrl+Shift+Tab | Next / previous panel on screen |
| Ctrl+1 to Ctrl+5 | Go to: main content / side panel / navigation bar / statistics bar / Continue button |
| Page Down / Page Up | Read the panel's text line by line, starting at the focused control |
| Ctrl+Page Down / Ctrl+Page Up | Next / previous heading in the panel text |
| Ctrl+Home / Ctrl+End | First / last line of the panel text |
| F1 | Help |
| F2 / Shift+F2 | Read panel text / list open panels |
| F3 / Shift+F3 | Repeat dialogue line / list responses |
| F4 / Shift+F4 | All statistics, numbered / turn and status |
| Alt+1 to Alt+9 | One statistic (in the order F4 numbers them) |
| Alt+0 | Current turn and its title |
| F5 / Shift+F5 / Ctrl+F5 | Latest / recent / open notification |
| F6 | Map location browser |
| Ctrl+F6 | Switch between the main map and the world map |
| Shift+F6 | What can I do now? Events waiting on the map, new information, the Continue button |
| F7 | Read the current report, decision, article or location |
| F8 / Shift+F8 | Repeat last speech / describe focused control in detail |
| F9 | Accessibility settings |
| Ctrl+F9 | Turn Suzerain Access off / on |
| F11 | Stop speech |
| Ctrl+F11 | Reconnect speech and return to automatic engine choice |
| Shift+F11 | Switch speech engine: automatic, then each running screen reader or the Windows voice |

The game's own keys keep working:

- **Space** continues dialogue.
- **Left and Right arrows** switch reports.
- **Escape** opens the pause menu.

## Documentation

- [`docs/KEYBOARD.md`](docs/KEYBOARD.md): every command, what it does on each screen, and how conflicts with the game's own keys are avoided.
- [`docs/TECHNICAL_NOTES.md`](docs/TECHNICAL_NOTES.md): the technical assessment, which game classes are used and how each was verified, the architecture, and known limitations.
- [`docs/TESTING.md`](docs/TESTING.md): a systematic test plan.
- [`docs/TROUBLESHOOTING.md`](docs/TROUBLESHOOTING.md): what to do when something does not work.

## For players

1. From the project's **Releases** page, download `SuzerainAccess-<version>-complete.zip`. It contains everything: BepInEx, the mod, and the speech files.
2. Extract it anywhere, open the folder, and run **Install.cmd**. It finds your Suzerain folder and copies everything into it.
3. Start the game and **wait**: the first start with BepInEx takes several minutes.

The full guide, including installing BepInEx yourself, is `docs/PLAYER-INSTALL.md`. You do not need Visual Studio.

## Licence notes

Suzerain Access is released under the MIT licence (`LICENSE`). Third-party components are listed in `THIRD-PARTY-NOTICES.md`. Suzerain is © Torpor Games. This unofficial mod contains no game code or assets, and is not affiliated with or endorsed by Torpor Games.
