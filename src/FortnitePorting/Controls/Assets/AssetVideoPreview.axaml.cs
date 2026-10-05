using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using FortnitePorting.Models.API.Responses;
using FortnitePorting.Services;
using LibVLCSharp.Shared;
using Lucdem.Avalonia.SourceGenerators.Attributes;
using Serilog;

namespace FortnitePorting.Controls.Assets;

public partial class AssetVideoPreview : UserControl
{
    private static readonly Lazy<LibVLC?> SharedLibVLC = new(CreateLibVLC);

    [AvaStyledProperty] private string _cosmeticId = string.Empty;
    [AvaDirectProperty] private bool _isVideoReady;

    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);

    private CancellationTokenSource? _loadCts;
    private AssetVideoFrameSource? _frameSource;
    private MediaPlayer? _mediaPlayer;
    private Media? _media;
    private FortniteGGPreviewResponse? _preview;

    public AssetVideoPreview()
    {
        InitializeComponent();

        AttachedToVisualTree += OnAttached;
        DetachedFromVisualTree += OnDetached;
    }

    private void OnFortniteGGPressed(object? sender, PointerPressedEventArgs e)
    {
        App.Launch(_preview?.CosmeticsUrl ?? "https://fortnite.gg");
        e.Handled = true;
    }

    private void OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (!AppSettings.Application.ShowVideoPreviews) return;
        if (string.IsNullOrWhiteSpace(CosmeticId)) return;

        IsVideoReady = false;
        _preview = null;

        var cts = new CancellationTokenSource();
        _loadCts = cts;

        var cosmeticId = CosmeticId;
        TaskService.Run(async () => await LoadAsync(cosmeticId, cts.Token));
    }

    private void OnDetached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        IsVideoReady = false;
        _preview = null;

        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = null;

        TaskService.Run(async () => await TearDownAsync());
    }

    private async Task LoadAsync(string cosmeticId, CancellationToken cancellationToken)
    {
        try
        {
            await _lifecycleLock.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        try
        {
            if (cancellationToken.IsCancellationRequested) return;

            var preview = await Api.FortniteGG.ResolvePreview(cosmeticId);
            if (cancellationToken.IsCancellationRequested || preview is null) return;

            await TaskService.RunDispatcherAsync(() =>
            {
                if (cancellationToken.IsCancellationRequested) return;

                _preview = preview;
                StartPlayback(preview.VideoUrl);
            });
        }
        catch (OperationCanceledException) { }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    private async Task TearDownAsync()
    {
        await _lifecycleLock.WaitAsync();
        try
        {
            var player = _mediaPlayer;
            var media = _media;
            var frameSource = _frameSource;

            _mediaPlayer = null;
            _media = null;
            _frameSource = null;

            frameSource?.Active = false;

            if (player is not null)
            {
                player.EndReached -= OnEndReached;
                try
                {
                    player.Stop();
                    player.Dispose();
                    media?.Dispose();
                }
                catch
                {
                }
            }

            await TaskService.RunDispatcherAsync(() => frameSource?.Dispose());
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    private void StartPlayback(string url)
    {
        // macOS: no libvlc ships with the app (VideoLAN's NuGet build is Intel-only); without VLC.app there is
        // no preview rather than an error on every hover
        if (SharedLibVLC.Value is not { } libVlc) return;

        // this runs on the UI thread: a libvlc that loaded but misbehaves must cost the preview, not the window
        try
        {
            _frameSource = new AssetVideoFrameSource(FrameImage, OnFramePresented);
            _frameSource.Active = true;

            _mediaPlayer = new MediaPlayer(libVlc)
            {
                EnableHardwareDecoding = false,
                Mute = false,
                Volume = 100
            };
            _mediaPlayer.SetVideoFormatCallbacks(_frameSource.FormatCallback, _frameSource.CleanupCallback);
            _mediaPlayer.SetVideoCallbacks(_frameSource.LockCallback, null, _frameSource.DisplayCallback);
            _mediaPlayer.EndReached += OnEndReached;

            _media = new Media(libVlc, new Uri(url),
                ":input-repeat=65535",
                ":network-caching=300");
            _mediaPlayer.Play(_media);
        }
        catch (Exception e)
        {
            Log.Warning("Video preview unavailable: {Message}", e.Message);
            if (_mediaPlayer is not null) _mediaPlayer.EndReached -= OnEndReached;
            _mediaPlayer?.Dispose();
            _media?.Dispose();
            _frameSource?.Dispose();
            _mediaPlayer = null;
            _media = null;
            _frameSource = null;
        }
    }

    private void OnFramePresented()
    {
        if (!IsVideoReady)
            IsVideoReady = true;
    }

    private void OnEndReached(object? sender, EventArgs e)
    {
        TaskService.Run(() =>
        {
            if (_mediaPlayer is null) return;

            _mediaPlayer.Position = 0;
            _mediaPlayer.Play();
        });
    }

    [DllImport("libc")]
    private static extern int setenv(string name, string value, int overwrite);

    private static LibVLC? CreateLibVLC()
    {
        if (OperatingSystem.IsMacOS())
        {
            var vlcApp = new[] { "/Applications/VLC.app", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Applications", "VLC.app") }
                // libvlc.5 is the libvlc 3.x ABI that LibVLCSharp 3.x binds; VLC 4 bumps the soname, so it is skipped
                // here rather than loaded and left to fail symbol by symbol
                .FirstOrDefault(app => File.Exists(Path.Combine(app, "Contents", "MacOS", "lib", "libvlc.5.dylib")));
            if (vlcApp is null) return null;

            // libc's setenv, not Environment.SetEnvironmentVariable: on Unix .NET keeps that in a managed copy that
            // native getenv never sees, so libvlc started with no plugins and rejected its own options
            setenv("VLC_PLUGIN_PATH", Path.Combine(vlcApp, "Contents", "MacOS", "plugins"), 1);
            try
            {
                Core.Initialize(Path.Combine(vlcApp, "Contents", "MacOS", "lib"));
                return new LibVLC(false, "--quiet", "--verbose=-1", "--no-video-title-show", "--no-video-on-top", "--avcodec-hw=none");
            }
            catch (Exception e)
            {
                Log.Warning("Video preview unavailable, libvlc failed to load from {VlcApp}: {Message}", vlcApp, e.Message);
                return null;
            }
        }

        Core.Initialize();
        return new LibVLC(false,
            "--quiet",
            "--verbose=-1",
            "--no-video-title-show",
            "--no-video-on-top",
            "--avcodec-hw=none");
    }
}
