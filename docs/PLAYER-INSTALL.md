# Installing Suzerain Access (players)

**Requirements**
- The Steam version of Suzerain, build 3.1.0.1.175. This is the build the mod was made for; after a game update the mod may need an update too.
- A screen reader (NVDA or JAWS), or the Windows voice.
- An internet connection the first time the game starts with BepInEx.

## The complete zip (easiest)

1. Close Suzerain.
2. Extract `SuzerainAccess-<version>-complete.zip` to any folder, for example Downloads.
3. Open the extracted folder and run **Install.cmd** (select it and press Enter). The installer:
   - finds your Suzerain folder, including other Steam libraries (if it cannot find it, it asks you to paste the folder);
   - copies BepInEx, the mod and the speech files into the game folder, and reports each step as text your screen reader reads.

   If it says copying failed, run Install.cmd as administrator.

   *Without the installer:* copy everything inside the folder named `files` into the game folder (the folder that contains `Suzerain.exe`), and allow Windows to replace files.
4. Start the game **and wait**. The first start with BepInEx takes much longer than usual, often several minutes, while BepInEx downloads Unity support files and prepares the game's code (the `BepInEx\interop` folder). The game may seem frozen; do not close it.
5. When the main menu is ready, you will hear "Suzerain Access loaded. Press F1 for help, F9 for settings." Later starts are fast.

NVDA users: if the complete zip does not contain `nvdaControllerClient.dll` next to the other files, put the 64-bit one from NV Access's NVDA Controller Client package next to `Suzerain.exe`.

## If you downloaded the mod-only zip

First install BepInEx yourself:
1. Get **BepInEx 6 for Unity IL2CPP, Windows x64**. The mod was tested with bleeding-edge build **788** (6.0.0-be.788). The download is named like `BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.788+5b766a3.zip`. BepInEx 5 and the Mono editions of BepInEx do **not** work with Suzerain.
2. Extract that zip into the game folder. Afterwards the game folder contains, next to `Suzerain.exe`: a `BepInEx` folder, a `dotnet` folder, `winhttp.dll` and `doorstop_config.ini`.
3. Start the game once, wait for the main menu (the first start is slow, see above), and close it.

Then extract `SuzerainAccess-<version>-mod-only.zip` into the game folder, and continue with step 4 above.

## Checking that it worked

Open `BepInEx\LogOutput.log` in the game folder. It should contain `Loading [Suzerain Access` followed by the version number. If the file does not exist, BepInEx did not start: check that `winhttp.dll` is next to `Suzerain.exe`.

## Uninstalling

- To turn off only the mod: delete `BepInEx\plugins\SuzerainAccess.dll`.
- To remove BepInEx completely: delete `winhttp.dll`, `doorstop_config.ini`, and the `BepInEx` and `dotnet` folders.

Keys: see `KEYBOARD.md`. Problems: see the Troubleshooting guide in the project repository.
