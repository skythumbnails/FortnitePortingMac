using System;

namespace FortnitePorting.Services;

// NAudio (WaveOutEvent/DirectSound) is Windows-only, so this build ships without the package.
// The service keeps upstream's API surface — settings passthrough + change events that
// TimeWaster/MusicPlayer/ApplicationSettings wire up — while actual playback on macOS goes
// through afplay in the window models instead of an NAudio output device.
public class AudioPlaybackService(SettingsService settings) : IService
{
    public int DeviceIndex => settings.Application.AudioDeviceIndex;

    public float Volume => settings.Application.Volume;

    // Output-device selection is DirectSound-based upstream; inert on macOS.
    public string[] Devices => [];

    public event Action? OutputDeviceChanged;
    public event Action? VolumeChanged;

    public void NotifyOutputDeviceChanged() => OutputDeviceChanged?.Invoke();

    public void NotifyVolumeChanged() => VolumeChanged?.Invoke();

    public AudioPlaybackSession CreateSession() => new(this);
}
