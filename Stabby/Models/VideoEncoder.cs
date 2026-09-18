namespace Stabby.Models;

public enum VideoEncoder
{
    H264,        // libx264 (software, default)
    H264_NVENC,  // NVIDIA
    H264_QSV,    // Intel QuickSync
    H264_AMF     // AMD
}
