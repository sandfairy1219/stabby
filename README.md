# Stabby

A lightweight, native screen recorder for Windows with **per-application audio capture**. Choose exactly which application audio is included in your recording — no virtual cable setup required.

![Windows](https://img.shields.io/badge/platform-Windows%2010%2B-blue.svg)
![.NET](https://img.shields.io/badge/.NET-8-blueviolet.svg)
![License](https://img.shields.io/badge/license-MIT-green.svg)

---

## Description

Stabby is a screen recording tool inspired by OBS, focused on simplicity and speed. It lets you record your screen while selecting which applications' audio should be included in the final video. For example, you can listen to music while gaming, but only record the game audio.

It uses the Windows 10 2004+ native **WASAPI Process Loopback API** to capture audio from specific processes, and **Windows.Graphics.Capture** with DirectX for high-quality video preview and recording.

---

## Features

- 🎥 **Screen Recording** — capture the entire desktop, a specific monitor, or a selected window
- 🎙️ **Per-Application Audio Capture** — include or exclude audio from individual applications
- 🖥️ **Live Preview** — real-time preview of the capture source
- ⚙️ **Simple Settings** — configure output directory, frame rate, video quality, and audio bitrate
- ⏸️ **Pause / Resume** — pause and resume recording *(basic implementation)*
- 📁 **Easy Output** — saves recordings as MP4 files

---

## Requirements

- Windows 10 version 2004 (build 19041) or later
- Windows 11 supported
- .NET 8 runtime *(or use self-contained publish)*
- FFmpeg in PATH
- 64-bit system

---

## Installation

### Option 1: Build from source

```bash
# Clone the repository
git clone https://github.com/sandfairy1219/stabby.git
cd stabby/Stabby

# Build and run
dotnet run
```

### Option 2: Download installer *(coming soon)*

A single-file executable and installer will be available in future releases.

---

## Usage

1. **Select Source** — click the button and pick a window or monitor to capture
2. **Audio Mixer** — check the applications you want to include in the recording
3. **Record** — click `Record` to start
4. **Pause / Resume** — click `Pause` to temporarily stop capturing
5. **Stop** — click `Stop` to finalize and save the MP4 file
6. **Settings** — click `Settings` to change output path, FPS, CRF, and audio bitrate

Recordings are saved to the configured output directory (default: `D:\videos`).

---

## Tech Stack

| Component | Technology |
|-----------|------------|
| UI | C# + WPF (.NET 8) |
| Screen Capture | Windows.Graphics.Capture + DirectX |
| Per-App Audio | WASAPI Process Loopback API |
| Audio Bridge | `ProcessAudioCapture.dll` (MIT) — to be replaced by `Stabby.AudioCapture` (in development) |
| Encoding | FFmpeg (rawvideo → H.264/AAC) |
| Packaging | .NET self-contained single file + Inno Setup *(planned)* |

---

## Architecture

```
[GraphicsCapturePicker] ──> [GraphicsCaptureItem]
                                    │
                                    ▼
                    [Direct3D11CaptureFramePool]
                                    │
                    ┌───────────────┴───────────────┐
                    ▼                               ▼
           [WPF Preview]                     [FFmpeg Encoder]
                    │                               │
                    └───────────┬───────────────────┘
                                ▼
                         [Output MP4]

[Audio Mixer] ──> [WASAPI Process Loopback] ──> [FFmpeg Audio Mix]
```

---

## Known Limitations

- Windows 10 2004+ is required for per-application audio capture
- DRM-protected content cannot be recorded
- Some UWP apps may need special handling
- Pause/Resume currently keeps FFmpeg running, so paused time is included in video duration

---

## Roadmap

- [x] Basic screen recording
- [x] Per-application audio capture
- [x] Live preview
- [x] Settings window
- [x] Pause/Resume (basic)
- [ ] Replace `ProcessAudioCapture.dll` with self-built `Stabby.AudioCapture` C++ DLL
- [ ] Proper pause with timeline skipping
- [ ] Multi-track audio recording (MKV)
- [ ] Hardware-accelerated encoding (NVENC/AMF/QuickSync)
- [ ] Streaming output (RTMP)
- [ ] Installer and single-file distribution

---

## License

This project is licensed under the MIT License.

The included `ProcessAudioCapture.dll` is used under its MIT License. See `Stabby/LICENSE-ProcessAudioCapture.txt` for details.

---

## Acknowledgments

- [Microsoft Windows Classic Samples](https://github.com/microsoft/Windows-classic-samples) — ApplicationLoopback sample
- [ProcessAudioCapture](https://github.com/tsubome/ProcessAudioCapture) — per-process audio capture DLL
- [FFmpeg](https://ffmpeg.org/) — video/audio encoding
- [NAudio](https://github.com/naudio/NAudio) — Windows audio APIs

---

## Contributing

Contributions are welcome. Please open an issue or pull request on GitHub.
