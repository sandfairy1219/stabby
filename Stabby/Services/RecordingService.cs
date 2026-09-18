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
    private int _frameRate = 30;
    private int _videoCrf = 23;
    private int _audioBitrate = 128;
    private VideoEncoder _videoEncoder = VideoEncoder.H264;

    public bool IsRecording => _ffmpegProcess != null && !_ffmpegProcess.HasExited;
    public bool IsPaused => _isPaused;
    public string? OutputPath => _outputPath;

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
        if (_isPaused) return;

        lock (_ffmpegLock)
        {
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
            try
            {
                _ffmpegProcess.StandardInput.Close();
            }
            catch { }
            await _ffmpegProcess.WaitForExitAsync();
        }

        Mux();
        Cleanup();
    }

    private void Mux()
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

        if (audioPaths.Count == 1)
        {
            sb.Append($"-c:v copy -c:a aac -b:a {_audioBitrate}k ");
        }
        else
        {
            sb.Append($"-filter_complex \"amix=inputs={audioPaths.Count}:duration=first:dropout_transition=0\" -c:v copy -c:a aac -b:a {_audioBitrate}k ");
        }

        sb.Append($"-y \"{_outputPath}\"");

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
        _videoTempPath = null;
        _systemAudioTempPath = null;
        _outputPath = null;
        _ffmpegProcess = null;
        _systemAudioCapture = null;
        _captureService = null;
        _isPaused = false;
    }
}
