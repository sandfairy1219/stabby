# Stabby - Screen Recorder with Per-App Audio Control

## 1. Overview

A lightweight, native Windows screen recorder focused on **per-application audio capture**. Users can choose which applications' audio is included in the recording, without manual virtual cable setup.

- Target OS: **Windows 10 version 2004+ (x64)**
- Core feature: Record screen + selected application audio only

---

## 2. Tech Stack

| Area | Choice | Reason |
|------|--------|--------|
| UI | **C# + WPF (.NET 8)** | Native Windows UI, fast development |
| Screen Capture | **FFmpeg gdigrab** | Simple, no WinRT/DirectX complexity |
| Per-App Audio Capture | **WASAPI Process Loopback API** (`AUDIOCLIENT_ACTIVATION_TYPE_PROCESS_LOOPBACK`) | Official Windows 10 2004+ API, captures specific process audio |
| Audio Capture Bridge | **C++ DLL (`Stabby.AudioCapture`)** | Wraps COM/WASAPI loopback API for C# P/Invoke |
| Audio Encoding/Muxing | **FFmpeg** | Mix multiple WAV tracks + video into MP4 |
| Deployment | **.NET 8 Self-Contained Single File + Inno Setup** | No .NET runtime required |

---

## 3. Project Structure

```
Stabby/
├── Stabby.sln
├── Stabby/                           # WPF Application
│   ├── App.xaml
│   ├── MainWindow.xaml
│   ├── Views/
│   │   └── AudioMixerPanel.xaml
│   ├── ViewModels/
│   │   ├── ViewModelBase.cs
│   │   ├── RelayCommand.cs
│   │   ├── AudioSessionViewModel.cs
│   │   └── MainViewModel.cs
│   ├── Models/
│   │   └── AudioSessionInfo.cs
│   ├── Services/
│   │   ├── AudioSessionService.cs    # List running audio sessions
│   │   ├── AudioCaptureNative.cs     # P/Invoke to C++ DLL
│   │   └── RecordingService.cs       # Orchestrate video + audio capture + mux
│   └── Stabby.csproj
├── Stabby.AudioCapture/              # C++ DLL
│   ├── Stabby.AudioCapture.vcxproj
│   ├── dllmain.cpp
│   ├── LoopbackCapture.cpp
│   ├── LoopbackCapture.h
│   └── exports.cpp
└── plan.md
```

---

## 4. Core Features

### 4.1 Screen Recording
- Full screen capture via FFmpeg gdigrab
- Configurable frame rate, quality
- Output: H.264 video in temporary MP4

### 4.2 Per-Application Audio Capture
- Enumerate audio-playing processes
- Per-process controls:
  - **Include**: capture only this app's audio
  - **Exclude**: capture all audio except this app
  - Volume slider
  - Mute
- Multiple included apps mixed into one audio track

### 4.3 Final Muxing
- Combine video MP4 + per-app WAV files
- FFmpeg `amix` filter for audio mixing
- Output: single MP4 file in `~/Videos/Stabby/`

---

## 5. WASAPI Process Loopback API

Windows 10 2004+ provides `AUDIOCLIENT_ACTIVATION_TYPE_PROCESS_LOOPBACK` via `ActivateAudioInterfaceAsync`.

Modes:
- `PROCESS_LOOPBACK_MODE_INCLUDE_TARGET_PROCESS_TREE`: capture only target process
- `PROCESS_LOOPBACK_MODE_EXCLUDE_TARGET_PROCESS_TREE`: capture everything except target process

Device ID: `VIRTUAL_AUDIO_DEVICE_PROCESS_LOOPBACK`

The C++ DLL exposes simple C exports:
```cpp
extern "C" __declspec(dllexport) bool Stabby_IsSupported();
extern "C" __declspec(dllexport) bool Stabby_StartCapture(int pid, int mode, const wchar_t* outputPath);
extern "C" __declspec(dllexport) void Stabby_StopCapture();
extern "C" __declspec(dllexport) bool Stabby_IsCapturing();
```

---

## 6. Development Phases

### Phase 1: C++ Audio Capture DLL
- Create `Stabby.AudioCapture` C++ DLL project
- Implement process loopback capture
- Export C API

### Phase 2: C# Integration
- P/Invoke wrapper
- Replace/extend `RecordingService` to capture per-app audio

### Phase 3: UI Update
- Add include/exclude mode selector
- Add per-app checkboxes in mixer panel

### Phase 4: Testing & Polish
- Test include/exclude recording
- Multi-app mixing
- Error handling
- Single-file publish + installer

---

## 7. Limitations

- Windows 10 version 2004+ required
- Cannot capture DRM-protected audio
- Some UWP apps may need special handling
- Included apps are captured to separate WAV files and mixed after recording
