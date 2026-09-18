using NAudio.Wave;
using Stabby.Models;
using Stabby.ViewModels;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Stabby.Services;

public class RecordingService
{
    private readonly List<IntPtr> _captureHandles = new();
    private readonly List<string> _audioTempPaths = new();
    private readonly List<AudioSessionViewModel> _audioSessions = new();
    private CaptureService? _captureService;
    private Process? _ffmpegProcess;
    private WasapiLoopbackCapture? _systemAudioCapture;
    private WaveFileWriter? _systemAudioWriter;
    private TaskCompletionSource? _audioStoppedTcs;
    private string? _videoTempPath;
    private string? _systemAudioTempPath;
    private string? _outputPath;
    private readonly object _ffmpegLock = new object();
    private bool _isPaused;
    private bool _stopRequested;
    private int _frameRate = 30;
    private int _videoCrf = 23;
    private int _audioBitrate = 128;
    private VideoEncoder _videoEncoder = VideoEncoder.H264;

    public bool IsRecording => _ffmpegProcess != null && !_ffmpegProcess.HasExited;
    public bool IsPaused => _isPaused;
    public string? OutputPath => _outputPath;
    public string? LastError { get; private set; }

    public void StartRecording(
        string outputPath,
        CaptureService captureService,
        Settings settings,
        List<AudioSessionViewModel> includeSessions,
        AudioSessionViewModel? excludeSession)
    {
        _outputPath = outputPath;
        _captureService = captureService;
        _frameRate = settings.FrameRate;
        _videoCrf = settings.VideoCrf;
        _audioBitrate = settings.AudioBitrate;
        _videoEncoder = settings.VideoEncoder;
        _isPaused = false;
        _stopRequested = false;
        _audioSessions.Clear();

        var tempDir = Path.GetTempPath();
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        _videoTempPath = Path.Combine(tempDir, $"stabby_video_{timestamp}.mp4");

        if (excludeSession != null)
        {
            // Exclude mode: capture all audio EXCEPT the selected process.
            var excludePath = Path.Combine(tempDir, $"stabby_audio_exclude_{excludeSession.ProcessId}_{timestamp}.wav");
            var result = AudioCaptureNative.PacStartCapture(
                (uint)excludeSession.ProcessId,
                PacCaptureMode.Exclude,
                excludePath,
                IntPtr.Zero,
                IntPtr.Zero,
                out var handle);

            if (result == (int)PacErrorCode.Success)
            {
                _captureHandles.Add(handle);
                _audioTempPaths.Add(excludePath);
            }
        }
        else if (includeSessions.Count > 0)
        {
            foreach (var session in includeSessions)
            {
                _audioSessions.Add(session);
                var audioPath = Path.Combine(tempDir, $"stabby_audio_{session.ProcessId}_{timestamp}.wav");
                var result = AudioCaptureNative.PacStartCapture(
                    (uint)session.ProcessId,
                    PacCaptureMode.Include,
                    audioPath,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    out var handle);

                if (result == (int)PacErrorCode.Success)
                {
                    _captureHandles.Add(handle);
                    _audioTempPaths.Add(audioPath);
                }
            }
        }
        else
        {
            _systemAudioTempPath = Path.Combine(tempDir, $"stabby_audio_system_{timestamp}.wav");
            StartSystemAudioCapture(_systemAudioTempPath);
        }
    }

    public void WriteFrame(CapturedFrame frame)
    {
        if (_isPaused || _stopRequested) return;

        lock (_ffmpegLock)
        {
            if (_stopRequested) return;
            if (_ffmpegProcess == null)
            {
                StartVideoCapture(_videoTempPath!, frame.Width, frame.Height);
            }

            try
            {
                _ffmpegProcess?.StandardInput.BaseStream.Write(frame.Data);
            }
            catch { }
        }
    }

    private void StartVideoCapture(string outputPath, int width, int height)
    {
        var videoArgs = _videoEncoder switch
        {
            VideoEncoder.H264_NVENC => $"-c:v h264_nvenc -preset p4 -cq {_videoCrf} -pix_fmt yuv420p",
            VideoEncoder.H264_QSV => $"-c:v h264_qsv -preset medium -global_quality {_videoCrf} -pix_fmt yuv420p",
            VideoEncoder.H264_AMF => $"-c:v h264_amf -quality balanced -qp_i {_videoCrf} -qp_p {_videoCrf} -pix_fmt yuv420p",
            _ => $"-c:v libx264 -preset fast -crf {_videoCrf} -pix_fmt yuv420p"
        };

        var psi = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            Arguments = $"-f rawvideo -pix_fmt bgra -s {width}x{height} -r {_frameRate} -thread_queue_size 512 -i - {videoArgs} -y \"{outputPath}\"",
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardError = false,
            CreateNoWindow = true
        };
        _ffmpegProcess = Process.Start(psi);
    }

    private void StartSystemAudioCapture(string outputPath)
    {
        _systemAudioCapture = new WasapiLoopbackCapture();
        _systemAudioWriter = new WaveFileWriter(outputPath, _systemAudioCapture.WaveFormat);

        _systemAudioCapture.DataAvailable += (s, e) =>
        {
            _systemAudioWriter?.Write(e.Buffer, 0, e.BytesRecorded);
        };

        _systemAudioCapture.RecordingStopped += (s, e) =>
        {
            _systemAudioWriter?.Dispose();
            _systemAudioWriter = null;
            _audioStoppedTcs?.TrySetResult();
        };

        _systemAudioCapture.StartRecording();
    }

    public void Pause()
    {
        _isPaused = true;
        foreach (var handle in _captureHandles)
        {
            AudioCaptureNative.PacPauseCapture(handle);
        }
    }

    public void Resume()
    {
        _isPaused = false;
        foreach (var handle in _captureHandles)
        {
            AudioCaptureNative.PacResumeCapture(handle);
        }
    }

    public async Task StopRecordingAsync()
    {
        _stopRequested = true;

        // Stop the frame source first so WriteFrame stops pumping frames.
        _captureService?.StopCapture();

        if (_systemAudioCapture != null)
        {
            _audioStoppedTcs = new TaskCompletionSource();
            _systemAudioCapture.StopRecording();
            await _audioStoppedTcs.Task;
        }

        foreach (var handle in _captureHandles)
        {
            AudioCaptureNative.PacStopCapture(handle);
        }
        _captureHandles.Clear();

        if (_ffmpegProcess != null)
        {
            lock (_ffmpegLock)
            {
                try
                {
                    _ffmpegProcess.StandardInput.Close();
                }
                catch { }
            }
            await _ffmpegProcess.WaitForExitAsync();
        }

        Mux();
        Cleanup();
    }

    private void Mux()
    {
        try
        {
            if (_videoTempPath == null || _outputPath == null) return;

            var audioPaths = new List<string>();
            if (!string.IsNullOrEmpty(_systemAudioTempPath) && File.Exists(_systemAudioTempPath))
                audioPaths.Add(_systemAudioTempPath);
            audioPaths.AddRange(_audioTempPaths.Where(File.Exists));

            if (audioPaths.Count == 0)
            {
                File.Move(_videoTempPath, _outputPath, overwrite: true);
                return;
            }

            var sb = new StringBuilder();
            sb.Append($"-i \"{_videoTempPath}\" ");
            foreach (var audioPath in audioPaths)
            {
                sb.Append($"-i \"{audioPath}\" ");
            }

            // Build volume filters. System audio (index 0 if present) stays at 1.0.
            // Per-app audio sessions use their recording volume from the mixer UI.
            var filters = new List<string>();
            int sessionIndex = !string.IsNullOrEmpty(_systemAudioTempPath) ? 1 : 0;
            for (int i = 0; i < audioPaths.Count; i++)
            {
                double gain = 1.0;
                if (i >= sessionIndex && i - sessionIndex < _audioSessions.Count)
                {
                    var session = _audioSessions[i - sessionIndex];
                    gain = session.IsMuted ? 0.0 : session.Volume / 100.0;
                }
                filters.Add($"[{i}:a]volume={gain:F2}[a{i}]");
            }
            var mixInputs = string.Join("", audioPaths.Select((_, i) => $"[a{i}]"));
            filters.Add($"{mixInputs}amix=inputs={audioPaths.Count}:duration=first:dropout_transition=0[aout]");

            sb.Append($"-filter_complex \"{string.Join(";", filters)}\" ");
            sb.Append($"-map 0:v -map [aout] -c:v copy -c:a aac -b:a {_audioBitrate}k -y \"{_outputPath}\"");

            var psi = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = sb.ToString(),
                UseShellExecute = false,
                RedirectStandardError = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi);
            process?.WaitForExit();

            if (process?.ExitCode != 0)
            {
                LastError = $"FFmpeg mux failed with exit code {process?.ExitCode}.";
            }
        }
        catch (Exception ex)
        {
            LastError = $"Mux failed: {ex.Message}";
        }
    }

    private void Cleanup()
    {
        try
        {
            if (!string.IsNullOrEmpty(_videoTempPath) && File.Exists(_videoTempPath))
                File.Delete(_videoTempPath);
            if (!string.IsNullOrEmpty(_systemAudioTempPath) && File.Exists(_systemAudioTempPath))
                File.Delete(_systemAudioTempPath);
            foreach (var path in _audioTempPaths.Where(File.Exists))
            {
                File.Delete(path);
            }
        }
        catch { }

        _captureHandles.Clear();
        _audioTempPaths.Clear();
        _audioSessions.Clear();
        _videoTempPath = null;
        _systemAudioTempPath = null;
        _outputPath = null;
        _ffmpegProcess = null;
        _systemAudioCapture = null;
        _captureService = null;
        _isPaused = false;
        _stopRequested = false;
    }
}
