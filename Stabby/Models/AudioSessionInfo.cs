using NAudio.CoreAudioApi;

namespace Stabby.Models;

public class AudioSessionInfo
{
    public int ProcessId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string ProcessName { get; set; } = string.Empty;
    public float Volume { get; set; }
    public bool IsMuted { get; set; }
    public SimpleAudioVolume? VolumeControl { get; set; }
}
