// Stabby.AudioCapture - exports.cpp
// C export surface matching AudioCaptureNative.cs P/Invoke signatures.
#include "LoopbackCapture.h"
#include <map>
#include <mutex>

static std::mutex g_handlesMutex;
static std::map<DWORD, LoopbackCapture*> g_handles;   // key: (DWORD)handle value
static DWORD g_nextHandle = 1;

static void CleanupHandle(DWORD h)
{
    std::lock_guard<std::mutex> lock(g_handlesMutex);
    auto it = g_handles.find(h);
    if (it != g_handles.end())
    {
        delete it->second;
        g_handles.erase(it);
    }
}

extern "C" {

    __declspec(dllexport) int __cdecl PacIsSupported()
    {
        // Process loopback requires Windows 10 version 2004+ (build 19041+).
        // We use RtlGetVersion to detect.
        typedef LONG (WINAPI *RtlGetVersionPtr)(PRTL_OSVERSIONINFOW);
        RTL_OSVERSIONINFOW info = {};
        info.dwOSVersionInfoSize = sizeof(info);
        HMODULE ntdll = GetModuleHandleW(L"ntdll.dll");
        if (!ntdll) return 0;
        auto fn = (RtlGetVersionPtr)GetProcAddress(ntdll, "RtlGetVersion");
        if (!fn) return 0;
        if (fn(&info) != 0) return 0;
        if (info.dwMajorVersion > 10) return 1;
        if (info.dwMajorVersion == 10 && info.dwBuildNumber >= 19041) return 1;
        return 0;
    }

    __declspec(dllexport) int __cdecl PacEnumerateAudioProcesses(
        void* processes,
        int maxCount,
        int* actualCount)
    {
        // This is handled in C# via IAudioMeterInformation + IAudioSessionManager2 enumeration.
        // The DLL does not need to reimplement this — we return 0 here as a placeholder.
        if (actualCount) *actualCount = 0;
        return 0;
    }

    __declspec(dllexport) int __cdecl PacStartCapture(
        unsigned int processId,
        int mode,                 // 0 = include, 1 = exclude
        const wchar_t* outputPath,
        void* levelCallback,      // unused for now
        void* userData,           // unused for now
        void** handle)
    {
        if (handle == nullptr || outputPath == nullptr) return -1;
        *handle = nullptr;

        auto capture = new LoopbackCapture();
        HRESULT hr = capture->Start(processId,
            mode == 0 ? PacMode::Include : PacMode::Exclude,
            outputPath);
        if (FAILED(hr))
        {
            delete capture;
            return (int)hr;
        }

        std::lock_guard<std::mutex> lock(g_handlesMutex);
        DWORD h = g_nextHandle++;
        g_handles[h] = capture;
        *handle = (void*)(uintptr_t)h;
        return 0;  // S_OK
    }

    __declspec(dllexport) int __cdecl PacStopCapture(void* handle)
    {
        if (handle == nullptr) return -1;
        DWORD h = (DWORD)(uintptr_t)handle;
        std::lock_guard<std::mutex> lock(g_handlesMutex);
        auto it = g_handles.find(h);
        if (it == g_handles.end()) return -2;
        it->second->Stop();
        delete it->second;
        g_handles.erase(it);
        return 0;
    }

    __declspec(dllexport) int __cdecl PacIsCapturing(void* handle)
    {
        if (handle == nullptr) return -1;
        DWORD h = (DWORD)(uintptr_t)handle;
        std::lock_guard<std::mutex> lock(g_handlesMutex);
        auto it = g_handles.find(h);
        if (it == g_handles.end()) return 0;
        return it->second->IsCapturing() ? 1 : 0;
    }

    __declspec(dllexport) int __cdecl PacPauseCapture(void* handle)
    {
        if (handle == nullptr) return -1;
        DWORD h = (DWORD)(uintptr_t)handle;
        std::lock_guard<std::mutex> lock(g_handlesMutex);
        auto it = g_handles.find(h);
        if (it == g_handles.end()) return -2;
        it->second->Pause();
        return 0;
    }

    __declspec(dllexport) int __cdecl PacResumeCapture(void* handle)
    {
        if (handle == nullptr) return -1;
        DWORD h = (DWORD)(uintptr_t)handle;
        std::lock_guard<std::mutex> lock(g_handlesMutex);
        auto it = g_handles.find(h);
        if (it == g_handles.end()) return -2;
        it->second->Resume();
        return 0;
    }

    __declspec(dllexport) void __cdecl PacGetLastErrorMessage(
        wchar_t* buffer,
        int bufferSize)
    {
        if (buffer == nullptr || bufferSize <= 0) return;
        std::lock_guard<std::mutex> lock(g_handlesMutex);
        // For simplicity, we return the error from the most recent handle (or empty).
        if (!g_handles.empty())
        {
            auto it = g_handles.rbegin();
            const wchar_t* msg = it->second->GetLastErrorMessage();
            wcsncpy_s(buffer, bufferSize, msg, _TRUNCATE);
        }
        else
        {
            buffer[0] = L'\0';
        }
    }

    __declspec(dllexport) int __cdecl PacGetLastCaptureErrorCode(void* handle)
    {
        // Placeholder — error code retrieval is done via error message string.
        return 0;
    }

}  // extern "C"

// DLL entry
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
