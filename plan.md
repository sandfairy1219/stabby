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
| Screen Capture | **Windows.Graphics.Capture + DirectX** | High-quality preview and recording |
| Per-App Audio Capture | **WASAPI Process Loopback API** (`AUDIOCLIENT_ACTIVATION_TYPE_PROCESS_LOOPBACK`) | Official Windows 10 2004+ API, captures specific process audio |
| Audio Capture Bridge | **C++ DLL (`Stabby.AudioCapture`)** | Wraps COM/WASAPI loopback API for C# P/Invoke. Replaces third-party dependency. |
| Audio Encoding/Muxing | **FFmpeg** | Mix multiple WAV tracks + video into MP4 |
| Deployment | **.NET 8 Self-Contained Single File + Inno Setup** | No .NET runtime required |

---

## 3. Project Structure

```
Stabby/
├── Stabby.slnx
├── Stabby/                           # WPF Application
│   ├── App.xaml
│   ├── MainWindow.xaml
│   ├── Views/
│   │   ├── AudioMixerPanel.xaml
│   │   └── SettingsWindow.xaml
│   ├── ViewModels/
│   │   ├── ViewModelBase.cs
│   │   ├── RelayCommand.cs
│   │   ├── AudioSessionViewModel.cs
│   │   ├── SettingsViewModel.cs
│   │   └── MainViewModel.cs
│   ├── Models/
│   │   ├── AudioSessionInfo.cs
│   │   └── Settings.cs
│   ├── Services/
│   │   ├── AudioSessionService.cs    # List running audio sessions
│   │   ├── AudioCaptureNative.cs     # P/Invoke to C++ DLL
│   │   ├── CaptureService.cs         # Windows.Graphics.Capture wrapper
│   │   ├── Direct3DDeviceHelper.cs   # Direct3D device creation for WinRT
│   │   ├── RecordingService.cs       # Orchestrate video + audio capture + mux
│   │   └── SettingsService.cs        # JSON settings persistence
│   ├── ProcessAudioCapture.dll       # Current audio capture DLL (legacy/third-party)
│   └── Stabby.csproj
├── Stabby.AudioCapture/              # C++ DLL (in development)
│   ├── Stabby.AudioCapture.vcxproj
│   ├── dllmain.cpp
│   ├── LoopbackCapture.cpp
│   ├── LoopbackCapture.h
│   ├── exports.cpp
│   └── README.md
├── plan.md
└── README.md
```

---

## 4. Core Features

### 4.1 Screen Recording
- Select capture source via Windows capture picker (window / monitor / desktop)
- Real-time preview with original quality
- Configurable frame rate and video quality
- Output: H.264 video in MP4

### 4.2 Per-Application Audio Capture
- Enumerate audio-playing processes
- Per-process controls:
  - **Include**: capture only this app's audio
  - Volume slider
  - Mute
- Multiple included apps mixed into one audio track
- System-wide audio capture when no app is selected

### 4.3 Settings
- Output directory
- Frame rate
- Video CRF
- Audio bitrate
- Persisted to `%AppData%/Stabby/settings.json`

### 4.4 Recording Controls
- Record / Stop
- Pause / Resume (basic)

### 4.5 Final Muxing
- Combine video + per-app WAV files
- FFmpeg `amix` filter for audio mixing
- Output: single MP4 file

---

## 5. WASAPI Process Loopback API

Windows 10 2004+ provides `AUDIOCLIENT_ACTIVATION_TYPE_PROCESS_LOOPBACK` via `ActivateAudioInterfaceAsync`.

Modes:
- `PROCESS_LOOPBACK_MODE_INCLUDE_TARGET_PROCESS_TREE`: capture only target process
- `PROCESS_LOOPBACK_MODE_EXCLUDE_TARGET_PROCESS_TREE`: capture everything except target process

Device ID: `VIRTUAL_AUDIO_DEVICE_PROCESS_LOOPBACK`

---

## 6. Development Phases

### Phase 1: C++ Audio Capture DLL ✅ (scaffold)
- Create `Stabby.AudioCapture` C++ DLL project
- Implement process loopback capture skeleton
- Export C API matching `AudioCaptureNative.cs`
- **Next**: build, test, replace `ProcessAudioCapture.dll`

### Phase 2: Screen Capture ✅
- Windows.Graphics.Capture integration
- Direct3D device helper
- Live preview

### Phase 3: C# Integration ✅
- P/Invoke wrapper
- RecordingService with FFmpeg rawvideo pipe
- Per-app audio capture and mixing

### Phase 4: UI ✅
- Capture source picker
- Audio mixer panel
- Settings window
- Pause/Resume buttons

### Phase 5: Polish & Deployment 🔄
- Replace third-party DLL with `Stabby.AudioCapture.dll`
- Proper pause with timeline skipping
- Multi-track audio recording (MKV)
- Hardware-accelerated encoding (NVENC/AMF/QuickSync)
- Single-file publish + Inno Setup installer

---

## 7. Limitations

- Windows 10 version 2004+ required
- Cannot capture DRM-protected audio
- Some UWP apps may need special handling
- Pause/Resume keeps FFmpeg running, so paused duration is included in video length
- `Stabby.AudioCapture` DLL is not yet integrated/built
