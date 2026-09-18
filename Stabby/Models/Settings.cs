namespace Stabby.Models;

public class Settings
{
    public string OutputDirectory { get; set; } = @"D:\videos";
    public int FrameRate { get; set; } = 30;
    public int VideoCrf { get; set; } = 23;
    public int AudioBitrate { get; set; } = 128;
    public VideoEncoder VideoEncoder { get; set; } = VideoEncoder.H264;
}
