/*
 * JawsBridge32 - part of Suzerain Access.
 *
 * Why this exists: JAWS exposes its speech API ("FreedomSci.JawsApi", FSAPI) as a COM object that is
 * installed with JAWS. Suzerain is a 64-bit game. If a JAWS installation only registers this COM object
 * for 32-bit programs, a 64-bit game cannot create it. This small 32-bit helper creates the object in a
 * 32-bit process and speaks on the game's behalf. It is only started when JAWS is running and the
 * direct 64-bit routes failed.
 *
 * Protocol (stdin, one command per line, UTF-8):
 *   S1<text>   speak, interrupting current speech     S0<text>   speak, queued
 *   B<text>    braille                                X          stop speech
 *   T<text>    speak and answer "OK 1" if JAWS accepted it, "OK 0" if not (used to verify the route)
 * Output (stdout): "READY" once the JAWS API object was created, or "FAIL <hresult>" and exit.
 * The helper exits when stdin closes (the game exits), so it never outlives the game.
 */
#include <windows.h>
#include <stdio.h>
#include <string.h>
#include "disphelper.h"

static IDispatch* jfw = NULL;

static wchar_t* toWide(const char* s) {
    int n = MultiByteToWideChar(CP_UTF8, 0, s, -1, NULL, 0);
    wchar_t* w = (wchar_t*)malloc(sizeof(wchar_t) * (n > 0 ? n : 1));
    if (n > 0) MultiByteToWideChar(CP_UTF8, 0, s, -1, w, n); else w[0] = 0;
    return w;
}

int main(void) {
    HRESULT hr;
    const char* progid = getenv("JAWSBRIDGE_PROGID"); /* test hook only */
    dhInitialize(TRUE);
    dhToggleExceptions(FALSE);
    if (progid) {
        wchar_t* w = toWide(progid);
        hr = dhCreateObject(w, NULL, &jfw);
        free(w);
    } else {
        hr = dhCreateObject(L"FreedomSci.JawsApi", NULL, &jfw);
        if (!jfw) hr = dhCreateObject(L"JFWApi", NULL, &jfw);
    }
    if (!jfw) { printf("FAIL %08lx\n", (unsigned long)hr); fflush(stdout); return 1; }
    printf("READY\n"); fflush(stdout);

    static char line[65536];
    while (fgets(line, sizeof(line), stdin)) {
        size_t len = strlen(line);
        while (len > 0 && (line[len-1] == '\n' || line[len-1] == '\r')) line[--len] = 0;
        if (len == 0) continue;
        if (line[0] == 'S' && len >= 2) {
            BOOL interrupt = line[1] == '1';
            wchar_t* w = toWide(line + 2);
            BOOL ok = FALSE;
            dhGetValue(L"%b", &ok, jfw, L".SayString(%S,%b)", w, interrupt);
            free(w);
        } else if (line[0] == 'T') {
            /* Test: speak and report whether JAWS accepted the text ("OK 1" / "OK 0"). */
            wchar_t* w = toWide(line + 1);
            BOOL ok = FALSE;
            HRESULT thr = dhGetValue(L"%b", &ok, jfw, L".SayString(%S,%b)", w, TRUE);
            free(w);
            printf("OK %d %08lx\n", ok ? 1 : 0, (unsigned long)thr); fflush(stdout);
        } else if (line[0] == 'X') {
            BOOL ok = FALSE;
            dhGetValue(L"%b", &ok, jfw, L".StopSpeech()");
        } else if (line[0] == 'B') {
            /* Same as Universal Speech: RunFunction("BrailleString(\"...\")") with quotes/backslashes removed. */
            wchar_t* w = toWide(line + 1);
            size_t n = wcslen(w);
            wchar_t* call = (wchar_t*)malloc(sizeof(wchar_t) * (n + 32));
            for (size_t i = 0; i < n; i++) if (w[i] == L'"' || w[i] == L'\\' || w[i] < 32) w[i] = L' ';
            swprintf(call, n + 32, L"BrailleString(\"%ls\")", w);
            BOOL ok = FALSE;
            dhGetValue(L"%b", &ok, jfw, L".RunFunction(%S)", call);
            free(call); free(w);
        }
    }
    SAFE_RELEASE(jfw);
    return 0;
}
