using Stabby.Models;
using Stabby.Services;
using CaptureMode = Stabby.Models.CaptureMode;
using Stabby.Views;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Graphics.Capture;

namespace Stabby.ViewModels;

public class MainViewModel : ViewModelBase
{
    private readonly AudioSessionService _audioSessionService;
    private readonly RecordingService _recordingService;
    private readonly CaptureService _captureService;
    private readonly SettingsService _settingsService;
    private readonly DispatcherTimer _refreshTimer;
    private bool _isRecording;
    private bool _isPaused;
    private string _statusMessage = "Ready";
    private ImageSource? _previewImage;
    private double _previewWidth = 640;
    private double _previewHeight = 360;
    private double _previewContainerWidth = 640;
    private double _previewContainerHeight = 360;
    private string _captureSourceName = "선택 안 됨";
    private DateTime _lastPreviewUpdate = DateTime.MinValue;
    private readonly TimeSpan _previewThrottleInterval = TimeSpan.FromMilliseconds(33); // ~30 fps cap

    public ObservableCollection<AudioSessionViewModel> AudioSessions { get; } = new();

    public ICommand StartRecordingCommand { get; }
    public ICommand StopRecordingCommand { get; }
    public ICommand PauseCommand { get; }
    public ICommand SettingsCommand { get; }

    public ImageSource? PreviewImage
    {
        get => _previewImage;
        private set => SetProperty(ref _previewImage, value);
    }

    public double PreviewWidth
    {
        get => _previewWidth;
        private set => SetProperty(ref _previewWidth, value);
    }

    public double PreviewHeight
    {
        get => _previewHeight;
        private set => SetProperty(ref _previewHeight, value);
    }

    public double PreviewContainerWidth
    {
        get => _previewContainerWidth;
        private set => SetProperty(ref _previewContainerWidth, value);
    }

    public double PreviewContainerHeight
    {
        get => _previewContainerHeight;
        private set => SetProperty(ref _previewContainerHeight, value);
    }

    public string CaptureSourceName
    {
        get => _captureSourceName;
        private set => SetProperty(ref _captureSourceName, value);
    }

    public bool IsRecording
    {
        get => _isRecording;
        private set
        {
            if (SetProperty(ref _isRecording, value))
            {
                CommandManager.InvalidateRequerySuggested();
                OnPropertyChanged(nameof(PauseButtonText));
            }
        }
    }

    public bool IsPaused
    {
        get => _isPaused;
        private set
        {
            if (SetProperty(ref _isPaused, value))
            {
                OnPropertyChanged(nameof(PauseButtonText));
            }
        }
    }

    public string PauseButtonText => IsPaused ? "Resume" : "Pause";

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public MainViewModel()
    {
        _audioSessionService = new AudioSessionService();
        _recordingService = new RecordingService();
        _captureService = new CaptureService();
        _settingsService = new SettingsService();
        _captureService.FrameReady += OnFrameReady;

        StartRecordingCommand = new RelayCommand(
            () => _ = StartRecordingAsync(),
            () => !IsRecording);

        StopRecordingCommand = new RelayCommand(
            () => _ = StopRecordingAsync(),
            () => IsRecording);

        PauseCommand = new RelayCommand(
            TogglePause,
            () => IsRecording);

        SettingsCommand = new RelayCommand(OpenSettings);

        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _refreshTimer.Tick += (s, e) =>
        {
            RefreshAudioSessions();
            UpdateCaptureStats();
        };
        _refreshTimer.Start();

        RefreshAudioSessions();
    }

    private void OnFrameReady(object? sender, CapturedFrame frame)
    {
        // Only write frames once a recording has actually started.
        if (_recordingService.OutputPath != null)
            _recordingService.WriteFrame(frame);

        var now = DateTime.UtcNow;
        if (now - _lastPreviewUpdate < _previewThrottleInterval)
            return;

        _lastPreviewUpdate = now;

        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            var wb = new WriteableBitmap(frame.Width, frame.Height, 96, 96, PixelFormats.Bgra32, null);
            wb.Lock();
            Marshal.Copy(frame.Data, 0, wb.BackBuffer, frame.Data.Length);
            wb.AddDirtyRect(new System.Windows.Int32Rect(0, 0, frame.Width, frame.Height));
            wb.Unlock();
            wb.Freeze();

            PreviewImage = wb;
            PreviewWidth = frame.Width;
            PreviewHeight = frame.Height;
            UpdatePreviewContainerHeight();
        }, DispatcherPriority.Render);
    }

    public void SetCaptureSource(GraphicsCaptureItem item)
    {
        _captureService.StartCapture(item);
        CaptureSourceName = item.DisplayName;
        StatusMessage = $"Source: {item.DisplayName}";
    }

    private void TogglePause()
    {
        if (!IsRecording) return;

        if (IsPaused)
        {
            _recordingService.Resume();
            IsPaused = false;
            StatusMessage = "Recording (Resumed)";
        }
        else
        {
            _recordingService.Pause();
            IsPaused = true;
            StatusMessage = "Recording (Paused)";
        }
    }

    private void OpenSettings()
    {
        var window = new SettingsWindow();
        window.DataContext = new SettingsViewModel(_settingsService, window);
        window.Owner = Application.Current.MainWindow;
        window.ShowDialog();
    }

    public void UpdatePreviewContainerSize(double width)
    {
        PreviewContainerWidth = width;
        UpdatePreviewContainerHeight();
    }

    private void UpdatePreviewContainerHeight()
    {
        if (PreviewWidth > 0)
        {
            var ratio = PreviewHeight / PreviewWidth;
            PreviewContainerHeight = PreviewContainerWidth * ratio;
        }
    }

    private async Task StartRecordingAsync()
    {
        var outputDir = _settingsService.Settings.OutputDirectory;
        Directory.CreateDirectory(outputDir);
        var fileName = $"recording_{DateTime.Now:yyyyMMdd_HHmmss}.mp4";
        var outputPath = Path.Combine(outputDir, fileName);

        var includeSessions = AudioSessions.Where(s => s.CaptureMode == CaptureMode.Include).ToList();
        var excludeSession = AudioSessions.FirstOrDefault(s => s.CaptureMode == CaptureMode.Exclude);

        _recordingService.StartRecording(outputPath, _captureService, _settingsService.Settings, includeSessions, excludeSession);
        IsRecording = true;
        IsPaused = false;

        var audioStatus = excludeSession != null
            ? $" (excluding {excludeSession.DisplayName})"
            : includeSessions.Count > 0
                ? $" ({includeSessions.Count} audio sources)"
                : " (system audio)";
        StatusMessage = $"Recording: {fileName}" + audioStatus;
    }

    private async Task StopRecordingAsync()
    {
        var outputPath = _recordingService.OutputPath;
        var fileName = outputPath != null ? Path.GetFileName(outputPath) : "unknown";
        StatusMessage = $"Finalizing... ({fileName})";

        try
        {
            await _recordingService.StopRecordingAsync();

            if (!string.IsNullOrEmpty(outputPath) && File.Exists(outputPath))
            {
                StatusMessage = $"Saved: {outputPath}";
            }
            else if (!string.IsNullOrEmpty(_recordingService.LastError))
            {
                StatusMessage = $"Failed: {_recordingService.LastError}";
            }
            else
            {
                StatusMessage = $"Failed: output file not found ({fileName})";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed: {ex.Message}";
        }

        IsRecording = false;
        IsPaused = false;
    }

    private void RefreshAudioSessions()
    {
        var sessions = _audioSessionService.GetAudioSessions();

        for (int i = AudioSessions.Count - 1; i >= 0; i--)
        {
            var existing = AudioSessions[i];
            if (!sessions.Any(s => s.ProcessId == existing.ProcessId))
            {
                AudioSessions.RemoveAt(i);
            }
        }

        foreach (var session in sessions)
        {
            var existing = AudioSessions.FirstOrDefault(s => s.ProcessId == session.ProcessId);
            if (existing == null)
            {
                var vm = new AudioSessionViewModel(session);
                vm.PropertyChanged += OnAudioSessionPropertyChanged;
                AudioSessions.Add(vm);
            }
        }
    }

    private void UpdateCaptureStats()
    {
        var captured = _captureService.FrameCount;
        var written = _recordingService.FramesWritten;
        var captureError = _captureService.LastError;
        var recorderError = _recordingService.LastError;

        if (!string.IsNullOrEmpty(recorderError))
        {
            StatusMessage = $"Recorder error: {recorderError}";
        }
        else if (!string.IsNullOrEmpty(captureError))
        {
            StatusMessage = $"Capture error: {captureError}";
        }
        else if (captured > 0 || written > 0)
        {
            StatusMessage = $"Frames: captured {captured}, written {written}";
        }
    }

    private void OnAudioSessionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AudioSessionViewModel.CaptureMode) && sender is AudioSessionViewModel vm)
        {
            // Exclude mode must be exclusive. When one session switches to Exclude,
            // reset all other sessions to None.
            if (vm.CaptureMode == CaptureMode.Exclude)
            {
                foreach (var other in AudioSessions.Where(s => s != vm))
                {
                    other.CaptureMode = CaptureMode.None;
                }
            }
        }
    }
}
