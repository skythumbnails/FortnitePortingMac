using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CUE4Parse.UE4.Assets.Exports.Sound;
using FortnitePorting.Application;
using FortnitePorting.Extensions;
using FortnitePorting.Framework;
using FortnitePorting.Models.Radio;
using FortnitePorting.Services;
using FortnitePorting.ViewModels;
using FortnitePorting.Windows;
using Material.Icons;
using Serilog;

namespace FortnitePorting.WindowModels;

[Transient]
public partial class MusicPlayerWindowModel(
    SettingsService settings,
    MusicViewModel music) : WindowModelBase
{
    public SettingsService Settings { get; } = settings;
    public MusicViewModel Music { get; } = music;

    [ObservableProperty] private MusicPackItem? _activeItem;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(PlayIconKind))]
    private bool _isPlaying;

    public MaterialIconKind PlayIconKind => IsPlaying ? MaterialIconKind.Pause : MaterialIconKind.Play;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(VolumeIconKind))]
    private float _volume = 1.0f;

    public MaterialIconKind VolumeIconKind => Volume switch
    {
        0.0f => MaterialIconKind.VolumeMute,
        < 0.3f => MaterialIconKind.VolumeLow,
        < 0.66f => MaterialIconKind.VolumeMedium,
        <= 1.0f => MaterialIconKind.VolumeHigh,
        _ => MaterialIconKind.VolumeHigh
    };

    [ObservableProperty] private ESoundFormat _soundFormat;
    [ObservableProperty] private TimeSpan _currentTime;
    [ObservableProperty] private TimeSpan _totalTime;
    [ObservableProperty] private bool _isLooping;
    [ObservableProperty] private bool _isShuffling;

    public object? AudioReader = null;
    public object OutputDevice = null;

    private Process? _afPlay;
    private string? _currentWavPath;
    private DateTime _resumeUtc;
    private TimeSpan _accumulated;
    private bool _isPausedExternally;

    private readonly DispatcherTimer _updateTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(200)
    };

    public override async Task Initialize()
    {
        Volume = AppSettings.Application.Volume;
        _updateTimer.Tick += OnUpdateTimerTick;
        _updateTimer.Start();
    }

    public override void OnApplicationExit()
    {
        AppSettings.Application.Volume = Volume;
        Stop(suppressClose: true);
        MusicPlayerWindow.Instance?.Close();
    }

    private void OnUpdateTimerTick(object? sender, EventArgs e)
    {
        if (!IsPlaying) return;
        var elapsed = _accumulated + (DateTime.UtcNow - _resumeUtc);
        if (TotalTime > TimeSpan.Zero && elapsed > TotalTime) elapsed = TotalTime;
        CurrentTime = elapsed;
    }

    [RelayCommand]
    public void TogglePlayPause()
    {
        if (ActiveItem is null) return;
        if (IsPlaying) Pause();
        else Play();
    }

    [RelayCommand]
    public void Previous()
    {
        var list = Music.PlaylistMusicPacks;
        if (ActiveItem is null || list.Count == 0) return;
        var idx = list.IndexOf(ActiveItem);
        if (idx < 0) return;
        var prev = list[(idx - 1 + list.Count) % list.Count];
        PlayItem(prev);
    }

    [RelayCommand]
    public void Next()
    {
        var list = Music.PlaylistMusicPacks;
        if (ActiveItem is null || list.Count == 0) return;
        var idx = list.IndexOf(ActiveItem);
        if (idx < 0) return;
        var next = list[(idx + 1) % list.Count];
        PlayItem(next);
    }

    [RelayCommand]
    public void CloseWindow()
    {
        Stop(suppressClose: true);
        MusicPlayerWindow.Instance?.Close();
    }

    public void PlayItem(MusicPackItem item)
    {
        if (item.SoundWave is null) return;

        StopAfPlay();

        if (ActiveItem is not null && !ReferenceEquals(ActiveItem, item))
            ActiveItem.IsPlaying = false;

        ActiveItem = item;

        try
        {
            if (!SoundExtensions.TrySaveSoundToAssets(
                    item.SoundWave.Load<USoundWave>(),
                    AppSettings.Application.AssetPath,
                    out string wavPath))
            {
                return;
            }
            _currentWavPath = wavPath;
            TotalTime = TryReadWavDuration(wavPath);
            CurrentTime = TimeSpan.Zero;
            _accumulated = TimeSpan.Zero;
            StartAfPlay(wavPath);
            item.IsPlaying = true;
            IsPlaying = true;
            _resumeUtc = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to start playback for {Track}", item.TrackName);
        }
    }

    public void Play()
    {
        if (ActiveItem is null) return;

        if (_isPausedExternally && _afPlay is not null && !_afPlay.HasExited)
        {
            try { Process.Start("/bin/kill", $"-CONT {_afPlay.Id}"); } catch { }
            _isPausedExternally = false;
            _resumeUtc = DateTime.UtcNow;
            IsPlaying = true;
            ActiveItem.IsPlaying = true;
            return;
        }

        if (_currentWavPath is not null && (_afPlay is null || _afPlay.HasExited))
        {
            StartAfPlay(_currentWavPath);
            _resumeUtc = DateTime.UtcNow;
            IsPlaying = true;
            ActiveItem.IsPlaying = true;
        }
    }

    public void Pause()
    {
        if (_afPlay is null || _afPlay.HasExited) return;
        try { Process.Start("/bin/kill", $"-STOP {_afPlay.Id}"); } catch { }
        _accumulated += DateTime.UtcNow - _resumeUtc;
        _isPausedExternally = true;
        IsPlaying = false;
        if (ActiveItem is not null) ActiveItem.IsPlaying = false;
    }

    public void Stop(bool suppressClose = false)
    {
        StopAfPlay();
        _accumulated = TimeSpan.Zero;
        CurrentTime = TimeSpan.Zero;
        IsPlaying = false;
        _isPausedExternally = false;
        if (ActiveItem is not null)
        {
            ActiveItem.IsPlaying = false;
            if (!suppressClose) ActiveItem = null;
        }
    }

    public void Restart()
    {
        if (ActiveItem is null || _currentWavPath is null) return;
        StopAfPlay();
        _accumulated = TimeSpan.Zero;
        CurrentTime = TimeSpan.Zero;
        StartAfPlay(_currentWavPath);
        _resumeUtc = DateTime.UtcNow;
        IsPlaying = true;
        ActiveItem.IsPlaying = true;
    }

    public void Scrub(TimeSpan time) { /* afplay does not support seeking */ }

    public void SetVolume(float value)
    {
        AppSettings.Application.Volume = value;
        if (_afPlay is not null && !_afPlay.HasExited && _currentWavPath is not null)
        {
            // afplay's volume is set at start; restart with the new volume to apply.
            var wasPaused = _isPausedExternally;
            var keepPos = _accumulated + (wasPaused ? TimeSpan.Zero : DateTime.UtcNow - _resumeUtc);
            StopAfPlay();
            StartAfPlay(_currentWavPath);
            _resumeUtc = DateTime.UtcNow;
            _accumulated = keepPos;
            IsPlaying = true;
            _isPausedExternally = false;
            if (ActiveItem is not null) ActiveItem.IsPlaying = true;
        }
    }

    public void UpdateOutputDevice() { /* macOS uses default output */ }

    partial void OnVolumeChanged(float value) => SetVolume(value);

    private void StartAfPlay(string wavPath)
    {
        try
        {
            var psi = new ProcessStartInfo("/usr/bin/afplay")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            psi.ArgumentList.Add("-v");
            psi.ArgumentList.Add(Volume.ToString(CultureInfo.InvariantCulture));
            psi.ArgumentList.Add(wavPath);

            _afPlay = new Process { StartInfo = psi, EnableRaisingEvents = true };
            _afPlay.Exited += OnAfPlayExited;
            _afPlay.Start();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "afplay failed to start");
            _afPlay = null;
        }
    }

    private void StopAfPlay()
    {
        if (_afPlay is null) return;
        try
        {
            _afPlay.Exited -= OnAfPlayExited;
            if (!_afPlay.HasExited) _afPlay.Kill(true);
        }
        catch { }
        _afPlay = null;
        _isPausedExternally = false;
    }

    private void OnAfPlayExited(object? sender, EventArgs e)
    {
        if (sender is not Process p || !ReferenceEquals(p, _afPlay)) return;
        Dispatcher.UIThread.Post(() =>
        {
            IsPlaying = false;
            if (ActiveItem is not null) ActiveItem.IsPlaying = false;
            if (IsLooping && ActiveItem is not null && _currentWavPath is not null)
            {
                _accumulated = TimeSpan.Zero;
                StartAfPlay(_currentWavPath);
                _resumeUtc = DateTime.UtcNow;
                IsPlaying = true;
                ActiveItem.IsPlaying = true;
            }
            else
            {
                Next();
            }
        });
    }

    private static TimeSpan TryReadWavDuration(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            using var br = new BinaryReader(fs);
            if (br.ReadUInt32() != 0x46464952) return TimeSpan.Zero; // "RIFF"
            br.ReadUInt32();
            if (br.ReadUInt32() != 0x45564157) return TimeSpan.Zero; // "WAVE"
            uint sampleRate = 0; ushort channels = 0; ushort bitsPerSample = 0; uint dataSize = 0;
            while (fs.Position < fs.Length - 8)
            {
                var id = br.ReadUInt32();
                var size = br.ReadUInt32();
                if (id == 0x20746d66) // "fmt "
                {
                    var start = fs.Position;
                    br.ReadUInt16();
                    channels = br.ReadUInt16();
                    sampleRate = br.ReadUInt32();
                    br.ReadUInt32();
                    br.ReadUInt16();
                    bitsPerSample = br.ReadUInt16();
                    fs.Position = start + size;
                }
                else if (id == 0x61746164) // "data"
                {
                    dataSize = size;
                    break;
                }
                else
                {
                    fs.Position += size;
                }
            }
            if (sampleRate == 0 || channels == 0 || bitsPerSample == 0) return TimeSpan.Zero;
            var seconds = dataSize / (double)(sampleRate * channels * (bitsPerSample / 8));
            return TimeSpan.FromSeconds(seconds);
        }
        catch
        {
            return TimeSpan.Zero;
        }
    }
}
