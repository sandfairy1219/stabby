using Stabby.Services;
using System.Windows;
using System.Windows.Input;

namespace Stabby.ViewModels;

public class SettingsViewModel : ViewModelBase
{
    private readonly SettingsService _settingsService;
    private readonly Window _window;
    private string _outputDirectory = string.Empty;
    private int _frameRate;
    private int _videoCrf;
    private int _audioBitrate;

    public string OutputDirectory
    {
        get => _outputDirectory;
        set => SetProperty(ref _outputDirectory, value);
    }

    public int FrameRate
    {
        get => _frameRate;
        set => SetProperty(ref _frameRate, value);
    }

    public int VideoCrf
    {
        get => _videoCrf;
        set => SetProperty(ref _videoCrf, value);
    }

    public int AudioBitrate
    {
        get => _audioBitrate;
        set => SetProperty(ref _audioBitrate, value);
    }

    public ICommand SaveCommand { get; }

    public SettingsViewModel(SettingsService settingsService, Window window)
    {
        _settingsService = settingsService;
        _window = window;
        var s = settingsService.Settings;
        OutputDirectory = s.OutputDirectory;
        FrameRate = s.FrameRate;
        VideoCrf = s.VideoCrf;
        AudioBitrate = s.AudioBitrate;
        SaveCommand = new RelayCommand(Save);
    }

    private void Save()
    {
        var s = _settingsService.Settings;
        s.OutputDirectory = OutputDirectory;
        s.FrameRate = FrameRate;
        s.VideoCrf = VideoCrf;
        s.AudioBitrate = AudioBitrate;
        _settingsService.Save();
        _window.Close();
    }
}
