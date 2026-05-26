using System;
using System.IO;
using System.IO.Compression;
using Avalonia.Platform;
using FortnitePorting.Shared.Extensions;

namespace FortnitePorting.Services;

public class DependencyService : IService
{
    public bool FinishedEnsuring;
    
    public readonly FileInfo BinkaDecoderFile = new(Path.Combine(App.DataFolder.FullName, "binka", OperatingSystem.IsWindows() ? "binkadec.exe" : "binkadec"));
    public readonly FileInfo RadaDecoderFile = new(Path.Combine(App.DataFolder.FullName, "rada", OperatingSystem.IsWindows() ? "radadec.exe" : "radadec"));
    public readonly FileInfo NoodleFile = new(Path.Combine(App.DataFolder.FullName, OperatingSystem.IsWindows() ? "noodle.dll" : "libnoodle.dylib"));
    public readonly FileInfo VgmStreamFile = new(Path.Combine(App.DataFolder.FullName, "vgmstream", OperatingSystem.IsWindows() ? "vgmstream-cli.exe" : "vgmstream-cli"));
    
    public readonly DirectoryInfo VgmStreamFolder = new(Path.Combine(App.DataFolder.FullName, "vgmstream"));

    public void Ensure()
{
    TaskService.Run(() =>
    {
        if (OperatingSystem.IsWindows())
        {
            EnsureResource("Assets/Dependencies/noodle.dll", NoodleFile);
            EnsureResource("Assets/Dependencies/binkadec.exe", BinkaDecoderFile);
            EnsureResource("Assets/Dependencies/radadec.exe", RadaDecoderFile);
        }
        else if (OperatingSystem.IsMacOS())
        {
            EnsureResource("Assets/Dependencies/libnoodle.dylib", NoodleFile);
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
        : "https://github.com/vgmstream/vgmstream/releases/latest/download/vgmstream-macos.zip";
    var file = Api.DownloadFile(downloadUrl, VgmStreamFolder);
    if (!file.Exists || file.Length == 0) return;
    
    var zip = ZipFile.Open(file.FullName, ZipArchiveMode.Read);
    foreach (var zipFile in zip.Entries)
    {
        using var zipStream = zipFile.Open();
        using var fileStream = new FileStream(Path.Combine(VgmStreamFolder.FullName, zipFile.FullName), FileMode.OpenOrCreate, FileAccess.Write);
        zipStream.CopyTo(fileStream);
    }
}

    public void EnsureBlenderExtensions()
    {
        var assets = AssetLoader.GetAssets(new Uri("avares://FortnitePorting.Plugins/Blender"), null);
        foreach (var asset in assets)
        {
            var assetStream = AssetLoader.Open(asset);
            var targetFile = new FileInfo(Path.Combine(App.PluginsFolder.FullName, asset.AbsolutePath[1..]));
            if (targetFile is { Exists: true, Length: > 0 } && targetFile.GetHash() == assetStream.GetHash()) continue;
            targetFile.Directory?.Create();
            
            File.WriteAllBytes(targetFile.FullName, assetStream.ReadToEnd());
        }
    }
    
    public void EnsureUnrealPlugins()
    {
        var assets = AssetLoader.GetAssets(new Uri("avares://FortnitePorting.Plugins/Unreal"), null);
        foreach (var asset in assets)
        {
            var assetStream = AssetLoader.Open(asset);
            var targetFile = new FileInfo(Path.Combine(App.PluginsFolder.FullName, asset.AbsolutePath[1..]));
            if (targetFile is { Exists: true, Length: > 0 } && targetFile.GetHash() == assetStream.GetHash()) continue;
            targetFile.Directory?.Create();
            
            File.WriteAllBytes(targetFile.FullName, assetStream.ReadToEnd());
        }
    }
}