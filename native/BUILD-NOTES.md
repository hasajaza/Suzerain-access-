# UniversalSpeech.dll (x64)

- Source: https://github.com/qtnc/UniversalSpeech, branch `master`, downloaded as a zip from codeload.github.com. MIT licence; see `UniversalSpeech-LICENSE.txt`.
- Reason: the official release `v1.0.0/UniversalSpeech.zip` only contains a 32-bit (PE32 i386) `UniversalSpeech.dll`. A 32-bit DLL cannot be loaded into Suzerain's 64-bit process.
- Compiler: MinGW-w64 GCC, `x86_64-w64-mingw32-gcc`, from the Ubuntu 24.04 package `gcc-mingw-w64-x86-64`.
- SHA-256 of the included DLL: `5d6bfbcf8b696e9660882302763fcd54aca0440bf4b8792c4e813aa489a453a7`

## First source change: extra NVDA client name

In `src/windows/nvda.c`, function `nvdaLoad`, directly after the line that tries `nvdaControllerClient32.dll`:

```c
#ifdef _WIN64
if (!nvda) nvda = LoadLibraryW(composePath(L"nvdaControllerClient64.dll"));
#endif
```

With this change, the 64-bit NVDA controller client is found whether it is named `nvdaControllerClient.dll` or `nvdaControllerClient64.dll`.

## Second source change: `composePath` in `src/windows/misc.c` (bug fix, version 1.0.1 of this package)

The original function built the path of `nvdaControllerClient.dll` like this:

```c
swprintf(c+1, (path-c)-3, L"%s", dll);
```

The buffer size `(path-c)-3` is negative. Microsoft's C runtime tolerates that, but MinGW's `swprintf` does not: it never writes the file name, and it writes a terminator outside the buffer. As a result, the first 64-bit build handed out with this mod **could never load NVDA's controller client and fell back to SAPI**. The out-of-bounds write could also disturb other engines.

The function now copies the file name with a correctly sized `lstrcpynW`.

**Verified under Wine** with a test program that loads each DLL and calls its exported `nvdaLoad()`, using the 64-bit `nvdaControllerClient.dll` placed next to it:

| DLL | `nvdaLoad()` | Controller client loaded | Engine reported |
|---|---|---|---|
| first build (before the fix) | 0 | no | SAPI5 |
| fixed build (included) | 1 | yes | NVDA |
| user-supplied `UniversalSpeech.dll` (MSVC build) | 1 | yes | NVDA |

## Other files in this folder

- `nvdaControllerClient.dll`: the 64-bit NVDA controller client (exports `nvdaController_speakText`, `cancelSpeech`, `testIfRunning` and `brailleMessage`). Supplied by the user.
- `ZDSRAPI.dll`: the 64-bit API for the ZDSR screen reader. Supplied by the user.

UniversalSpeech loads both from its own folder, so they are deployed next to it.

## Commands

```sh
mkdir obj64
for f in src/*.c src/windows/*.c; do
  x86_64-w64-mingw32-gcc -std=gnu99 -DRELEASE -O2 -c -o obj64/$(basename $f .c).o $f
done
grep -v "^Java_" src/windows/main.def > main64.def   # the Java/JNI exports are not built
x86_64-w64-mingw32-gcc -shared -s -static-libgcc -o UniversalSpeech.dll obj64/*.o main64.def \
  -lole32 -loleaut32 -luuid -lpsapi -lversion
```

## Result

- `PE32+ executable (DLL) x86-64`
- Exports used by the mod: `speechSay`, `speechStop`, `brailleDisplay`, `speechGetValue`, `speechSetValue`, `speechGetString`.
- Imports only standard Windows system DLLs: KERNEL32, USER32, ADVAPI32, OLE32, OLEAUT32, PSAPI, VERSION, msvcrt.

The DLL was built and inspected on Linux; it has not been run on Windows as part of this build.

# JawsBridge32.exe (32-bit JAWS helper)

JAWS needs no DLL next to the game. Its speech API is the COM object `FreedomSci.JawsApi`, which JAWS registers when it is installed. The mod reaches JAWS by the first route that works, logging each attempt:

1. directly, with .NET COM interop from the 64-bit game;
2. through `JawsBridge32.exe`, for JAWS installations that register the API only for 32-bit programs;
3. through Universal Speech (its exported `jfwLoad()` and `jfwSayW()`).

A route is only used once JAWS has **accepted a test phrase**: its `SayString` must return true. Creating the JAWS object is not enough; in a real test the object was created but JAWS spoke nothing. If no route passes, Universal Speech is switched to the Windows voice (SAPI), so the game is never silent.

- Source: `JawsBridge32/jawsbridge.c`. It uses `disphelper.c`/`.h` from the Universal Speech source, which is the same COM helper Universal Speech uses for JAWS.
- Build: `i686-w64-mingw32-gcc -std=gnu99 -O2 -s -static -o JawsBridge32.exe jawsbridge.c disphelper.c -lole32 -loleaut32 -luuid`
- SHA-256: `308c4fe0e5a348c7a680825da4620c4774226cf3045721c573b2da4a7d2f9ffb`
- Protocol: one command per line on stdin (`S1`/`S0` + text = speak with or without interrupting, `T` + text = speak and answer `OK 1` or `OK 0`, `B` + text = braille, `X` = stop). The helper prints `READY` or `FAIL <hresult>`, and exits when the game closes its input.

## Tests under Wine

- The same COM helper, compiled 64-bit, created a COM object by ProgID (`Scripting.Dictionary`) and called methods with a wide string and a boolean argument, returning correct results. This confirms the 64-bit JAWS call path of `UniversalSpeech.dll` works mechanically.
- The bridge protocol was tested with a 64-bit build of the same source:
  - without JAWS it prints `FAIL 800401f3` ("class not registered"), as expected;
  - with a registered COM object it prints `READY`, processes the commands, and exits cleanly when input ends.
- The 32-bit binary itself could not be run here, and JAWS itself was not available for testing.
