// Stabby.AudioCapture - dllmain.cpp
// DLL entry point (implementation is in exports.cpp; this file only defines DllMain).
#include "LoopbackCapture.h"

BOOL APIENTRY DllMain(HMODULE hModule, DWORD reason, LPVOID lpReserved)
{
    switch (reason)
    {
    case DLL_PROCESS_ATTACH:
    case DLL_THREAD_ATTACH:
    case DLL_THREAD_DETACH:
    case DLL_PROCESS_DETACH:
        break;
    }
    return TRUE;
}
