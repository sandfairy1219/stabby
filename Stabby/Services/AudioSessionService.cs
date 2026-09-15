using NAudio.CoreAudioApi;
using Stabby.Models;
using System.Diagnostics;

namespace Stabby.Services;

public class AudioSessionService
{
    private readonly MMDeviceEnumerator _enumerator;
    private readonly MMDevice _defaultDevice;

    public AudioSessionService()
    {
        _enumerator = new MMDeviceEnumerator();
        _defaultDevice = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
    }

    public List<AudioSessionInfo> GetAudioSessions()
    {
        var sessions = _defaultDevice.AudioSessionManager.Sessions;
        var result = new List<AudioSessionInfo>();

        for (int i = 0; i < sessions.Count; i++)
        {
            var session = sessions[i];
            try
            {
                int pid = (int)session.GetProcessID;
                if (pid == 0) continue;

                using var process = Process.GetProcessById(pid);
                string processName = process.ProcessName;
                string displayName = string.IsNullOrWhiteSpace(process.MainWindowTitle)
                    ? processName
                    : $"{processName} ({process.MainWindowTitle})";

                result.Add(new AudioSessionInfo
                {
                    ProcessId = pid,
                    ProcessName = processName,
                    DisplayName = displayName,
                    Volume = session.SimpleAudioVolume.Volume,
                    IsMuted = session.SimpleAudioVolume.Mute,
                    VolumeControl = session.SimpleAudioVolume
                });
            }
            catch
            {
                // skip sessions without accessible process
            }
        }

        return result;
    }
}
