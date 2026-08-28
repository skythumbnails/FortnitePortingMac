using System;
using System.IO;
using System.IO.Compression;
using Avalonia.Platform;
using FortnitePorting.Shared.Extensions;

namespace FortnitePorting.Services;

public class DependencyService : IService
{
    public bool FinishedEnsuring;
    
    // Native Oodle (oo2core universal dylib). Upstream 4.2.1 switched to the managed
    // OodleSharp port, which fails to decompress some block types on streamed cosmetic
    // payloads (head-only skin ports). The native library handles everything.
    public readonly FileInfo NoodleFile = new(Path.Combine(App.DataFolder.FullName, "libnoodle.dylib"));

    public void EnsureNoodle() => EnsureResource("Assets/Dependencies/libnoodle.dylib", NoodleFile);

    public readonly FileInfo BinkaDecoderFile = new(Path.Combine(App.DataFolder.FullName, "binka", OperatingSystem.IsWindows() ? "binkadec.exe" : "binkadec"));
    public readonly FileInfo RadaDecoderFile = new(Path.Combine(App.DataFolder.FullName, "rada", OperatingSystem.IsWindows() ? "radadec.exe" : "radadec"));
    public readonly FileInfo VgmStreamFile = new(Path.Combine(App.DataFolder.FullName, "vgmstream", OperatingSystem.IsWindows() ? "vgmstream-cli.exe" : "vgmstream-cli"));
    
    public readonly DirectoryInfo VgmStreamFolder = new(Path.Combine(App.DataFolder.FullName, "vgmstream"));

    public void Ensure()
{
    TaskService.Run(() =>
    {
        if (OperatingSystem.IsWindows())
        {
            EnsureResource("Assets/Dependencies/binkadec.exe", BinkaDecoderFile);
            EnsureResource("Assets/Dependencies/radadec.exe", RadaDecoderFile);
        }
        EnsureVgmStream();
        EnsureBlenderExtensions();
        EnsureUnrealPlugins();
        FinishedEnsuring = true;
    });
}

    private void EnsureResource(string path, FileInfo targetFile)
    {
        var assetStream = AssetLoader.Open(new Uri($"avares://FortnitePorting/{path}"));
        if (targetFile is { Exists: true, Length: > 0 } && targetFile.GetHash() == assetStream.GetHash()) return;

        targetFile.Directory?.Create();
        targetFile.Delete();
        File.WriteAllBytes(targetFile.FullName, assetStream.ReadToEnd());
    }

    private void EnsureVgmStream()
{
    if (VgmStreamFile is { Exists: true, Length: > 0 } ) return;
    
    VgmStreamFolder.Create();
    var downloadUrl = OperatingSystem.IsWindows()
        ? "https://github.com/vgmstream/vgmstream/releases/latest/download/vgmstream-win.zip"
        : "https://github.com/vgmstream/vgmstream/releases/latest/download/vgmstream-mac.zip";
    var file = Api.DownloadFile(downloadUrl, VgmStreamFolder);
    if (file is null || !file.Exists || file.Length == 0) return;
    
    var zip = ZipFile.Open(file.FullName, ZipArchiveMode.Read);
    foreach (var zipFile in zip.Entries)
    {
        using var zipStream = zipFile.Open();
        using var fileStream = new FileStream(Path.Combine(VgmStreamFolder.FullName, zipFile.FullName), FileMode.OpenOrCreate, FileAccess.Write);
        zipStream.CopyTo(fileStream);
    }

    // ZipArchive drops the unix mode bits, so the extracted CLI lands non-executable and every
    // Process.Start on it fails with EACCES. Windows doesn't care; everything else does.
    // Refresh() is load-bearing: this FileInfo was constructed at service-construction time,
    // before the download, and FileInfo caches Exists until you ask it to look again.
    VgmStreamFile.Refresh();
    if (!OperatingSystem.IsWindows() && VgmStreamFile.Exists)
        File.SetUnixFileMode(VgmStreamFile.FullName,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
}

    public void EnsureBlenderExtensions()
    {
        var blenderFolder = new DirectoryInfo(Path.Combine(App.PluginsFolder.FullName, "Blender"));
        if (blenderFolder.Exists)
            blenderFolder.Delete(true);

        var assets = AssetLoader.GetAssets(new Uri("avares://FortnitePorting.Plugins/Blender"), null);
        foreach (var asset in assets)
        {
            var assetStream = AssetLoader.Open(asset);
            var targetFile = new FileInfo(Path.Combine(App.PluginsFolder.FullName, asset.AbsolutePath[1..]));
            targetFile.Directory?.Create();
            
            File.WriteAllBytes(targetFile.FullName, assetStream.ReadToEnd());
        }
    }
    
    public void EnsureUnrealPlugins()
    {
        var unrealFolder = new DirectoryInfo(Path.Combine(App.PluginsFolder.FullName, "Unreal"));
        if (unrealFolder.Exists)
            unrealFolder.Delete(true);

        var assets = AssetLoader.GetAssets(new Uri("avares://FortnitePorting.Plugins/Unreal"), null);
        foreach (var asset in assets)
        {
            var assetStream = AssetLoader.Open(asset);
            var targetFile = new FileInfo(Path.Combine(App.PluginsFolder.FullName, asset.AbsolutePath[1..]));
            targetFile.Directory?.Create();
            
            File.WriteAllBytes(targetFile.FullName, assetStream.ReadToEnd());
        }
    }
}