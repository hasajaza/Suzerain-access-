# Troubleshooting

The log is `<Suzerain folder>\BepInEx\LogOutput.log`. For detailed lines, turn on "Debug logging" in F9, or set `DebugLogging = true` in `BepInEx\config\com.suzerainaccess.mod.cfg`.

## Build problems

**"SuzerainAccess: cannot find the Suzerain game folder..."**
Open `SuzerainAccess\GamePath.props` and put the folder that contains `Suzerain.exe` between `<SuzerainDir>` and `</SuzerainDir>`. That folder must also contain `BepInEx\interop` and `BepInEx\core`. If `BepInEx\interop` is missing, start the game once with BepInEx installed and wait for the main menu.

**Error NU1100 or "Unable to resolve Microsoft.NETCore.App.Ref 6.0"**
Visual Studio needs internet access once to download the .NET 6 reference pack from NuGet. Make sure NuGet.org is enabled under Tools > Options > NuGet Package Manager > Package Sources.

**CS0656 "Missing compiler required member 'System.Runtime.CompilerServices.NullableAttribute..ctor'"**
Fixed in this version by `Core/CompilerAttributes.cs`. The game's interop `UnityEngine.CoreModule.dll` exposes empty public copies of the compiler's nullability attributes; the project now declares correct ones, which the compiler prefers. If you still see it, make sure `Core/CompilerAttributes.cs` is in the project, then choose Build > Rebuild Solution.

**Errors about missing types or members after a game update**
The interop assemblies changed. This mod targets one exact build (see the README). Please report the errors.

## Nothing is spoken

1. Look in the log for `Universal Speech loaded from ...`.
   - **"UniversalSpeech.dll not found"**: copy `UniversalSpeech.dll` into `BepInEx\plugins\SuzerainAccess\`.
   - **"Found but could not load ... (is it a 64-bit DLL?)"**: you have the 32-bit official release. Use the DLL from this project's `native` folder.
2. **SAPI speaks although NVDA or JAWS is running:** make sure `BepInEx\plugins\SuzerainAccess\UniversalSpeech.dll` is the fixed build from this version (MD5 `e564e89b6bab9442453199383ff00a20`), with `nvdaControllerClient.dll` next to it. Copy both from `SuzerainAccess\bin\Release\` (see the README install steps). Press Shift+F4 in game: the last item is the speech engine in use. The log also records every engine change as `Speech engine is now: ...`.
3. **JAWS:** JAWS needs no DLL. Start JAWS before the game, or wait up to 10 seconds after starting it. Search the log for `JAWS is running`: the following lines show, for each of the three routes (direct COM, JawsBridge32.exe, Universal Speech), whether JAWS accepted a test phrase. If none did, the Windows voice is used and the log says so. If all three fail, the log says so. This usually means JAWS's API is not registered: repair or reinstall JAWS (a portable or trial copy may not register it), then send the log.
   If the log says JAWS **accepted** the test phrase but you hear nothing, check `JawsBraille = false` in `BepInEx\config\com.suzerainaccess.mod.cfg` (the default since 1.0.11). JAWS's braille function can silence the speech that was just sent.
4. **NVDA:** the log says `Active engine: ...`. If it is not NVDA, the 64-bit NVDA controller client is missing. Place it next to `UniversalSpeech.dll`, named `nvdaControllerClient.dll` or `nvdaControllerClient64.dll`.
5. **No screen reader:** make sure "Use Windows SAPI when no screen reader runs" is on in F9.
6. If the log does not mention Suzerain Access at all, BepInEx did not load the plugin. Check that the file is in `BepInEx\plugins\SuzerainAccess\SuzerainAccess.dll` and that other BepInEx messages appear in the log.

## JAWS silent only inside the game window (JAWS Sleep Mode for Unity games)

**Symptoms:**
- JAWS works when the game starts, but after switching to another window and back it is silent in the game.
- Even JAWS's own commands (Insert+T, Insert+F12) do nothing in the game window, while JAWS works normally everywhere else.
- If the game loads while another window is active, JAWS never works in it.

**Cause:** JAWS has its own settings for "Unity Player", the program type of Suzerain and many other Unity games, and in them **Sleep Mode** was switched on. In Sleep Mode, JAWS goes silent in that program and passes every key through to it. JAWS applies these settings when the game window gets the focus, which is why it starts after switching windows. Neither the mod nor BepInEx is involved.

**Fix (found and confirmed by a JAWS user of this mod):**
1. Open JAWS **Settings Center**.
2. Choose the settings for **Unity Player**.
3. Go to **Miscellaneous**.
4. Change **Sleep Mode** from enabled to **disabled**, and save.

This also helps other Unity games.

## The mod speaks, but a key does nothing

- Press F1 to hear the current bindings. They may have been changed in the config.
- **Enter does nothing on a control:** use Numpad Enter, or set F9 > "Enter key activation" to "always handled by the mod". Please send the log line starting with `EventSystem present`.
- **Enter presses something twice:** set "Enter key activation" back to "automatic", and report which control it was.
- **While typing in a text field**, only Tab and the F-keys are used by the mod. Leave the field with Tab.

## Something is not announced, or is announced wrongly

- Press F2 to read all the text of the panel. Press Shift+F8 for full details of the focused control, including tooltip text.
- Controls without visible text are named from their internal object name. The log contains `Control without visible text; using object name as label: <path>`. Please report the path.
- A panel that is not recognised shows up as "Screen". The log line `Detected screen: Screen (no registered panel visible)` identifies the situation.

## Wrong game version warning

The mod compares your game files to the build it was made for. After a game update the warning is expected, and parts of the mod may stop working until the mod is rebuilt against the new interop assemblies.

## Too much or too little speech

Use F9:

- **Verbosity:** Low, Normal or High.
- **Announce control types / list positions.**
- **Announce statistic changes / notifications / turn changes.**
- **Read dialogue automatically / read reports and pop-ups automatically.**
- **Filter duplicate announcements.**
