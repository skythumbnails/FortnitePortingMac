using System;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;

namespace FortnitePorting.Services;

// No-op port of upstream's NAudio-backed session (WaveOutEvent + WaveStream are Windows-only).
// Keeps the public shape so upstream call sites compile; macOS playback paths use afplay in the
// window models and leave their Session fields null.
public sealed partial class AudioPlaybackSession : ObservableObject, IDisposable
{
    private readonly AudioPlaybackService _audio;
    private bool _disposed;

    [ObservableProperty] private float _volume;

    public TimeSpan CurrentTime { get; set; } = TimeSpan.Zero;

    public TimeSpan TotalTime => TimeSpan.Zero;

    public AudioPlaybackSession(AudioPlaybackService audio)
    {
        _audio = audio;
        Volume = audio.Volume;
        _audio.VolumeChanged += OnServiceVolumeChanged;
    }

    public void Load(Stream stream)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Play()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Pause()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Stop()
    {
    }

    public void Scrub(TimeSpan time) => CurrentTime = time;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _audio.VolumeChanged -= OnServiceVolumeChanged;
    }

    private void OnServiceVolumeChanged()
    {
        if (!_disposed)
            Volume = _audio.Volume;
    }
}
