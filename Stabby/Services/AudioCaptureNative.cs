using System.Runtime.InteropServices;

namespace Stabby.Services;

public enum PacCaptureMode
{
    Include = 0,
    Exclude = 1
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
public struct PacProcessInfo
{
    public uint ProcessId;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
    public string ProcessName;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
    public string WindowTitle;
}

public static class AudioCaptureNative
{
    private const string DllName = "ProcessAudioCapture.dll";

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int PacIsSupported();

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int PacEnumerateAudioProcesses(
        [Out] PacProcessInfo[] processes,
        int maxCount,
        out int actualCount);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    public static extern int PacStartCapture(
        uint processId,
        PacCaptureMode mode,
        string? outputPath,
        IntPtr levelCallback,
        IntPtr userData,
        out IntPtr handle);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int PacStopCapture(IntPtr handle);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int PacIsCapturing(IntPtr handle);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int PacPauseCapture(IntPtr handle);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int PacResumeCapture(IntPtr handle);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    public static extern void PacGetLastErrorMessage(
        [MarshalAs(UnmanagedType.LPWStr)] string buffer,
        int bufferSize);
}

public enum PacErrorCode
{
    Success = 0,
    InvalidParam = -1,
    NotSupported = -2,
    ProcessNotFound = -3,
    AudioInitFailed = -4,
    FileCreateFailed = -5,
    AlreadyRecording = -6,
    NotRecording = -7,
    Unknown = -100
}
