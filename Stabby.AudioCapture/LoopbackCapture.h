// Stabby.AudioCapture - LoopbackCapture.h
// WASAPI Process Loopback capture wrapper (Windows 10 2004+).
#pragma once

#include <windows.h>
#include <mmdeviceapi.h>
#include <audioclient.h>
#include <audioclientactivationparams.h>
#include <wrl/implements.h>
#include <mfapi.h>
#include <stdio.h>
#include <string>
#include <atomic>
#include <thread>

// Capture mode: 0 = include only target process, 1 = exclude target process
enum class PacMode : int
{
    Include = 0,
    Exclude = 1
};

class LoopbackCapture
{
public:
    LoopbackCapture();
    ~LoopbackCapture();

    // Start capture. outputPath = .wav file to write. Returns S_OK on success.
    HRESULT Start(DWORD processId, PacMode mode, const wchar_t* outputPath);

    // Stop capture and finalize the wav file (flush + close).
    void Stop();

    // Pause / resume the capture (writes are skipped / resumed).
    void Pause();
    void Resume();

    bool IsCapturing() const { return m_capturing.load(); }
    bool IsPaused() const { return m_paused.load(); }

    // Get the last error string (UTF-16).
    const wchar_t* GetLastErrorMessage() const { return m_lastError.c_str(); }

private:
    void CaptureThread(DWORD processId, PacMode mode, std::wstring outputPath);
    void WriteWavHeader(FILE* file, WAVEFORMATEX* fmt, DWORD dataSize);
    void SetError(HRESULT hr, const wchar_t* context);

    std::atomic<bool> m_capturing{ false };
    std::atomic<bool> m_paused{ false };
    std::thread m_thread;
    std::wstring m_lastError;
    CRITICAL_SECTION m_errorLock;
};
