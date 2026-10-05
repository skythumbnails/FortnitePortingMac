using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CUE4Parse.UE4.Assets.Exports.Sound;
using FortnitePorting.Application;
using FortnitePorting.Exporting.Extensions;
using FortnitePorting.Extensions;
using FortnitePorting.Framework;
using FortnitePorting.Services;
using Material.Icons;
// using NAudio.Wave;

namespace FortnitePorting.WindowModels;

[Transient]
public partial class SoundPreviewWindowModel(
    SettingsService settings,
    AudioPlaybackService audio) : WindowModelBase
{
    [ObservableProperty] private SettingsService _settings = settings;
    
    [ObservableProperty] private string _soundName = string.Empty;
    [ObservableProperty] private USoundWave? _soundWave;
    
    [ObservableProperty] private TimeSpan _currentTime;
    [ObservableProperty] private TimeSpan _totalTime;
    
    [ObservableProperty, NotifyPropertyChangedFor(nameof(PauseIcon))] private bool _isPaused;
    public MaterialIconKind PauseIcon => IsPaused ? MaterialIconKind.Play : MaterialIconKind.Pause;

    public AudioPlaybackSession? Session = null; // audio disabled on macOS

    private readonly DispatcherTimer _updateTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(1)
    };

    public override async Task Initialize()
    {
        _updateTimer.Tick += OnUpdateTimerTick;
        _updateTimer.Start();
    }

    public override async Task OnViewExited()
    {
        _updateTimer.Stop();
        _updateTimer.Tick -= OnUpdateTimerTick;
        // audio disabled on macOS
    }

    private void OnUpdateTimerTick(object? sender, EventArgs e)
    {
        // audio disabled on macOS
    }

    public async Task Play()
    {
        // audio disabled on macOS
    }

    public void TogglePause()
    {
        // audio disabled on macOS
    }

    public void Scrub(TimeSpan time)
    {
        // audio disabled on macOS
    }
}
