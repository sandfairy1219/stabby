// Stabby.AudioCapture - LoopbackCapture.cpp
// Per-process WASAPI loopback capture implementation.
#include "LoopbackCapture.h"
#include <mmdeviceapi.h>
#include <audioclient.h>
#include <audioclientactivationparams.h>
#include <mfapi.h>
#include <wrl/implements.h>
#include <wrl/wrappers/corewrappers.h>
#include <cmath>
#include <cstring>

using namespace Microsoft::WRL;
using namespace Microsoft::WRL::Wrappers;

namespace
{
    // Virtual audio device used by process loopback API.
    const wchar_t* ProcessLoopbackDeviceId = L"VIRTUAL_AUDIO_DEVICE_PROCESS_LOOPBACK";

    class ActivateHandler :
        public RuntimeClass<RuntimeClassFlags<ClassicCom>, FtmBase, IAgileObject, IActivateAudioInterfaceCompletionHandler>
    {
    public:
        ActivateHandler() {}

        STDMETHOD(ActivateCompleted)(IActivateAudioInterfaceAsyncOperation* operation)
        {
            SetEvent(m_done.Get());
            return S_OK;
        }

        HANDLE GetEvent() const { return m_done.Get(); }

        ComPtr<IUnknown> audioInterface;
        HRESULT activateResult = E_FAIL;

    private:
        Event m_done;
    };
}

LoopbackCapture::LoopbackCapture()
{
    InitializeCriticalSection(&m_errorLock);
}

LoopbackCapture::~LoopbackCapture()
{
    Stop();
    DeleteCriticalSection(&m_errorLock);
}

void LoopbackCapture::SetError(HRESULT hr, const wchar_t* context)
{
    wchar_t buf[512];
    swprintf_s(buf, L"%s (hr=0x%08X)", context, hr);
    EnterCriticalSection(&m_errorLock);
    m_lastError = buf;
    LeaveCriticalSection(&m_errorLock);
}

HRESULT LoopbackCapture::Start(DWORD processId, PacMode mode, const wchar_t* outputPath)
{
    if (m_capturing.load()) return E_NOT_VALID_STATE;

    m_thread = std::thread(&LoopbackCapture::CaptureThread, this, processId, mode, std::wstring(outputPath));
    return S_OK;
}

void LoopbackCapture::CaptureThread(DWORD processId, PacMode mode, std::wstring outputPath)
{
    HRESULT hr;

    // 1) Build activation params for process loopback
    AUDIOCLIENT_ACTIVATION_PARAMS activationParams = {};
    activationParams.ActivationType = AUDIOCLIENT_ACTIVATION_TYPE_PROCESS_LOOPBACK;
    activationParams.ProcessLoopbackParams.TargetProcessId = processId;
    if (mode == PacMode::Include)
        activationParams.ProcessLoopbackParams.ProcessLoopbackMode = PROCESS_LOOPBACK_MODE_INCLUDE_TARGET_PROCESS_TREE;
    else
        activationParams.ProcessLoopbackParams.ProcessLoopbackMode = PROCESS_LOOPBACK_MODE_EXCLUDE_TARGET_PROCESS_TREE;

    PROPVARIANT pv = {};
    pv.vt = VT_BLOB;
    pv.blob.cbSize = sizeof(activationParams);
    pv.blob.pBlobData = reinterpret_cast<BYTE*>(&activationParams);

    ComPtr<ActivateHandler> handler = Make<ActivateHandler>();
    ComPtr<IActivateAudioInterfaceAsyncOperation> asyncOp;
    hr = ActivateAudioInterfaceAsync(ProcessLoopbackDeviceId, __uuidof(IAudioClient), &pv, handler.Get(), &asyncOp);
    if (FAILED(hr))
    {
        SetError(hr, L"ActivateAudioInterfaceAsync failed");
        return;
    }

    WaitForSingleObject(handler->GetEvent(), INFINITE);

    hr = asyncOp->GetActivateResult(&handler->activateResult, &handler->audioInterface);
    if (FAILED(hr) || FAILED(handler->activateResult) || !handler->audioInterface)
    {
        SetError(hr != S_OK ? hr : handler->activateResult, L"GetActivateResult failed");
        return;
    }

    ComPtr<IAudioClient> audioClient;
    hr = handler->audioInterface.As(&audioClient);
    if (FAILED(hr))
    {
        SetError(hr, L"IUnknown -> IAudioClient failed");
        return;
    }

    // 2) Get default mix format
    WAVEFORMATEX* mixFormat = nullptr;
    hr = audioClient->GetMixFormat(&mixFormat);
    if (FAILED(hr) || mixFormat == nullptr)
    {
        SetError(hr, L"GetMixFormat failed");
        return;
    }

    // 3) Initialize audio client (loopback, event-driven, low-latency)
    const REFERENCE_TIME bufferDuration = 200000;  // 20ms
    hr = audioClient->Initialize(AUDCLNT_SHAREMODE_SHARED,
        AUDCLNT_STREAMFLAGS_LOOPBACK | AUDCLNT_STREAMFLAGS_EVENTCALLBACK,
        bufferDuration, 0, mixFormat, nullptr);
    if (FAILED(hr))
    {
        SetError(hr, L"AudioClient Initialize failed");
        CoTaskMemFree(mixFormat);
        return;
    }

    // 4) Set event for buffer-full
    HANDLE bufferEvent = CreateEventW(nullptr, FALSE, FALSE, nullptr);
    hr = audioClient->SetEventHandle(bufferEvent);
    if (FAILED(hr))
    {
        SetError(hr, L"SetEventHandle failed");
        CoTaskMemFree(mixFormat);
        CloseHandle(bufferEvent);
        return;
    }

    // 5) Get capture client
    ComPtr<IAudioCaptureClient> captureClient;
    hr = audioClient->GetService(__uuidof(IAudioCaptureClient), &captureClient);
    if (FAILED(hr))
    {
        SetError(hr, L"GetService(IAudioCaptureClient) failed");
        CoTaskMemFree(mixFormat);
        CloseHandle(bufferEvent);
        return;
    }

    // 6) Open the output wav file for writing
    FILE* outFile = nullptr;
    if (_wfopen_s(&outFile, outputPath.c_str(), L"wb") != 0 || outFile == nullptr)
    {
        SetError(E_FAIL, L"WAV file open failed");
        CoTaskMemFree(mixFormat);
        CloseHandle(bufferEvent);
        return;
    }

    // Write placeholder wav header (44 bytes) — we'll rewrite with real sizes at Stop()
    BYTE wavHeader[44] = {};
    fwrite(wavHeader, 1, sizeof(wavHeader), outFile);

    // Start streaming
    hr = audioClient->Start();
    if (FAILED(hr))
    {
        SetError(hr, L"Start streaming failed");
        fclose(outFile);
        CoTaskMemFree(mixFormat);
        CloseHandle(bufferEvent);
        return;
    }

    m_capturing = true;

    // Capture loop
    UINT32 totalDataSize = 0;
    while (m_capturing.load())
    {
        DWORD waitResult = WaitForSingleObject(bufferEvent, 2000);
        if (waitResult != WAIT_OBJECT_0)
        {
            continue;
        }

        BYTE* data = nullptr;
        UINT32 framesAvailable = 0;
        DWORD flags = 0;
        UINT64 devicePosition = 0;
        UINT64 qpcPosition = 0;

        hr = captureClient->GetNextPacketSize(&framesAvailable);
        if (FAILED(hr)) { break; }

        while (framesAvailable > 0)
        {
            hr = captureClient->GetBuffer(&data, &framesAvailable, &flags, &devicePosition, &qpcPosition);
            if (FAILED(hr)) break;

            UINT32 bytesPerFrame = mixFormat->nBlockAlign;
            UINT32 bytesToWrite = framesAvailable * bytesPerFrame;

            if (!m_paused.load() && bytesToWrite > 0)
            {
                // Write out
                fwrite(data, 1, bytesToWrite, outFile);
                totalDataSize += bytesToWrite;
            }

            hr = captureClient->ReleaseBuffer(framesAvailable);
            if (FAILED(hr)) break;

            hr = captureClient->GetNextPacketSize(&framesAvailable);
            if (FAILED(hr)) break;
        }
    }

    // Stop
    audioClient->Stop();
    fclose(outFile);

    // Rewrite the wav header with real data size
    FILE* rewriteFile = nullptr;
    if (_wfopen_s(&rewriteFile, outputPath.c_str(), L"r+b") == 0 && rewriteFile != nullptr)
    {
        WriteWavHeader(rewriteFile, mixFormat, totalDataSize);
        fclose(rewriteFile);
    }

    CoTaskMemFree(mixFormat);
    CloseHandle(bufferEvent);
}

void LoopbackCapture::WriteWavHeader(FILE* file, WAVEFORMATEX* fmt, DWORD dataSize)
{
    if (file == nullptr) return;

    DWORD sampleRate = fmt->nSamplesPerSec;
    WORD channels = static_cast<WORD>(fmt->nChannels);
    WORD bitsPerSample = static_cast<WORD>(fmt->wBitsPerSample);
    WORD blockAlign = static_cast<WORD>(fmt->nBlockAlign);
    DWORD byteRate = fmt->nAvgBytesPerSec;

    fseek(file, 0, SEEK_SET);

    // RIFF header
    const BYTE riffHeader[12] = {
        'R', 'I', 'F', 'F',
        0, 0, 0, 0,  // size, patched below
        'W', 'A', 'V', 'E'
    };
    fwrite(riffHeader, 1, 12, file);

    // fmt chunk
    const BYTE fmtChunkHeader[8] = {
        'f', 'm', 't', ' ',
        16, 0, 0, 0
    };
    fwrite(fmtChunkHeader, 1, 8, file);

    BYTE fmtData[16] = {};
    memcpy(fmtData, &fmt->wFormatTag, 2);
    memcpy(fmtData + 2, &channels, 2);
    memcpy(fmtData + 4, &sampleRate, 4);
    memcpy(fmtData + 8, &byteRate, 4);
    memcpy(fmtData + 12, &blockAlign, 2);
    memcpy(fmtData + 14, &bitsPerSample, 2);
    fwrite(fmtData, 1, 16, file);

    // data chunk header
    const BYTE dataChunkHeader[8] = {
        'd', 'a', 't', 'a',
        0, 0, 0, 0
    };
    fwrite(dataChunkHeader, 1, 8, file);

    // Patch sizes: riff chunk size and data chunk size
    DWORD riffSize = 4 + 8 + 16 + 8 + dataSize;  // wave + fmt + data
    fseek(file, 4, SEEK_SET);
    fwrite(&riffSize, 4, 1, file);
    fseek(file, 40, SEEK_SET);
    fwrite(&dataSize, 4, 1, file);
    fseek(file, 0, SEEK_END);
}

void LoopbackCapture::Stop()
{
    m_capturing = false;
    if (m_thread.joinable())
    {
        m_thread.join();
    }
}

void LoopbackCapture::Pause()
{
    m_paused = true;
}

void LoopbackCapture::Resume()
{
    m_paused = false;
}
