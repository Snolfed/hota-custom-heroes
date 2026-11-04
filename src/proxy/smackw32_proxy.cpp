// Proxy for smackw32.dll: h3hota.exe imports it at startup, so this is how the
// HeroForge mod gets loaded (HD Mod skips Pack plugins in HotA mode).
// Every export is forwarded to the renamed original, smackw32_org.dll.
#include <windows.h>
#include "exports.h"

BOOL WINAPI DllMain(HINSTANCE inst, DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_ATTACH)
    {
        DisableThreadLibraryCalls(inst);
        char path[MAX_PATH];
        GetModuleFileNameA(inst, path, MAX_PATH);
        char* slash = strrchr(path, '\\');
        if (slash) lstrcpyA(slash + 1, "HeroForge\\HeroForge.dll");
        LoadLibraryA(path);
    }
    return TRUE;
}
