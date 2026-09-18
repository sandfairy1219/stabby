using Stabby.Models;
using CaptureMode = Stabby.Models.CaptureMode;

namespace Stabby.ViewModels;

public class AudioSessionViewModel : ViewModelBase
{
    private readonly AudioSessionInfo _info;
    private float _volume;
    private bool _isMuted;
    private CaptureMode _captureMode;

    public AudioSessionViewModel(AudioSessionInfo info)
    {
        _info = info;
        ProcessId = info.ProcessId;
        DisplayName = info.DisplayName;
        ProcessName = info.ProcessName;
        _volume = 100f;
        _isMuted = false;
    }

    public int ProcessId { get; }
    public string DisplayName { get; }
    public string ProcessName { get; }

    public CaptureMode CaptureMode
    {
        get => _captureMode;
        set => SetProperty(ref _captureMode, value);
    }

    public bool IsSelected
    {
        get => _captureMode == CaptureMode.Include;
        set
        {
            if (value)
                CaptureMode = CaptureMode.Include;
            else if (_captureMode == CaptureMode.Include)
                CaptureMode = CaptureMode.None;
        }
    }

    public float Volume
    {
        get => _volume;
        set => SetProperty(ref _volume, value);
    }

    public bool IsMuted
    {
        get => _isMuted;
        set => SetProperty(ref _isMuted, value);
    }
}
