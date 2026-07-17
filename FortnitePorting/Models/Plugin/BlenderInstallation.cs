using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FortnitePorting.Shared.Extensions;
using FortnitePorting.ViewModels;
using Newtonsoft.Json;

namespace FortnitePorting.Models.Plugin;

public partial class BlenderInstallation(string blenderExecutablePath) : ObservableObject
{
    [ObservableProperty] private string _blenderPath = blenderExecutablePath;
    [JsonIgnore] private bool IsValidInstallation => File.Exists(BlenderPath) && File.Exists(StartupPath);

    [ObservableProperty, NotifyPropertyChangedFor(nameof(ExtensionVersionString))]
    [property: JsonIgnore]
    private Version? _extensionVersion = null;

    [JsonIgnore]
    public string ExtensionVersionString => ExtensionVersion is null ? string.Empty : $"v{ExtensionVersion}";

    [ObservableProperty, NotifyPropertyChangedFor(nameof(StatusBrush))]
    [property: JsonIgnore]
    private EPluginStatusType _status = EPluginStatusType.Newest;

    [JsonIgnore]
    public SolidColorBrush StatusBrush => Status switch
    {
        EPluginStatusType.Newest => SolidColorBrush.Parse("#17854F"),
        EPluginStatusType.UpdateAvailable => SolidColorBrush.Parse("#E0A100"),
        EPluginStatusType.Failed => SolidColorBrush.Parse("#A61717"),
        EPluginStatusType.Modifying => SolidColorBrush.Parse("#6F6F75"),
    };

    [JsonIgnore]
    public Version? BlenderVersion => TryGetVersion(BlenderPath);

    private string? StartupPath
    {
        get
        {
            if (BlenderVersion is null) return null;
            if (OperatingSystem.IsMacOS())
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "Library", "Application Support", "Blender",
                    BlenderVersion.ToString(2), "scripts", "startup");
            }
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Blender Foundation", "Blender",
                BlenderVersion.ToString(2), "scripts", "startup");
        }
    }

    private string? MetaPath => StartupPath is null ? null : Path.Combine(StartupPath,
        "fortnite_porting",
        "fortnite_porting_meta.json");

    public static readonly DirectoryInfo PluginWorkingDirectory = new(Path.Combine(App.PluginsFolder.FullName, "Blender"));
    public static readonly Version MinimumVersion = new(5, 0);

    public static Version GetVersion(string blenderPath)
    {
        if (OperatingSystem.IsWindows())
            return new Version(FileVersionInfo.GetVersionInfo(blenderPath).ProductVersion!);

        // macOS: FileVersionInfo can't read a Mach-O binary's version. Blender's version lives in
        // the app bundle's Info.plist (CFBundleShortVersionString). blenderPath is the executable
        // at Blender.app/Contents/MacOS/Blender, so Info.plist is two levels up.
        var plistPath = Path.Combine(Path.GetDirectoryName(blenderPath)!, "..", "Info.plist");
        if (File.Exists(plistPath))
        {
            var plist = File.ReadAllText(plistPath);
            const string marker = "<key>CFBundleShortVersionString</key>";
            var idx = plist.IndexOf(marker, StringComparison.Ordinal);
            if (idx >= 0)
            {
                var start = plist.IndexOf("<string>", idx) + "<string>".Length;
                var end = plist.IndexOf("</string>", start);
                return new Version(plist.Substring(start, end - start));
            }
        }

        throw new Exception($"Could not determine Blender version from path: {blenderPath}");
    }

    public static Version? TryGetVersion(string? blenderPath)
    {
        if (string.IsNullOrWhiteSpace(blenderPath) || !File.Exists(blenderPath))
            return null;

        // Route through GetVersion so the macOS Info.plist path is used (FileVersionInfo returns
        // null/garbage for a Mach-O binary, which would otherwise make every Mac install "unknown").
        try
        {
            return GetVersion(blenderPath);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public bool SyncExtensionVersion()
    {
        if (!File.Exists(BlenderPath))
        {
            Status = EPluginStatusType.Failed;
            return false;
        }

        if (MetaPath is null || !File.Exists(MetaPath))
        {
            Status = EPluginStatusType.UpdateAvailable;
            return true;
        }

        var meta = JsonConvert.DeserializeObject<FPPluginMeta>(File.ReadAllText(MetaPath));
        if (meta is null || string.IsNullOrWhiteSpace(meta.Version))
        {
            Status = EPluginStatusType.UpdateAvailable;
            return true;
        }

        var metaVersion = new FPVersion(meta.Version);
        ExtensionVersion = new Version(metaVersion.Release, metaVersion.Major, metaVersion.Minor);
        Status = !Globals.IsDevBuild && !metaVersion.Equals(Globals.Version)
            ? EPluginStatusType.UpdateAvailable
            : EPluginStatusType.Newest;

        return true;
    }

    public void Install(bool verbose = true)
    {
        if (StartupPath is null)
        {
            if (verbose)
                Info.Message("Plugin Installation Failed",
                    "Could not determine Blender version from the provided path. Please check your Blender installation.");
            Status = EPluginStatusType.Failed;
            return;
        }

        Status = EPluginStatusType.Modifying;

        var destination = Path.Combine(StartupPath, "fortnite_porting");
        MiscExtensions.Copy(Path.Combine(PluginWorkingDirectory.FullName, "fortnite_porting"), destination);

        // Wipe Python's compiled-bytecode cache for the plugin. MiscExtensions.Copy preserves source
        // mtimes, so Python's stale-pyc check (mtime comparison) can reuse a .pyc compiled against
        // the OLD plugin — the cause of puzzling "X is not a valid EExportType" errors after an
        // enum change. Deleting __pycache__ forces a clean recompile.
        if (Directory.Exists(destination))
        {
            foreach (var pycache in Directory.EnumerateDirectories(destination, "__pycache__", SearchOption.AllDirectories).ToArray())
            {
                try { Directory.Delete(pycache, recursive: true); } catch { /* best-effort */ }
            }
        }

        if (MetaPath is not null)
            File.WriteAllText(MetaPath, JsonConvert.SerializeObject(new FPPluginMeta { Version = Globals.VersionString }));

        var didSyncProperly = SyncExtensionVersion();
        if (verbose && !didSyncProperly)
        {
            Info.Message("Plugin Installation Failed",
                "Failed to install the plugin, please install it manually by dragging and dropping the Fortnite Porting plugin in Blender.",
                useButton: true, buttonTitle: "Open Plugins Folder", buttonCommand: () => App.Launch(App.PluginsFolder.FullName));

            Status = EPluginStatusType.Failed;
            return;
        }

        Status = EPluginStatusType.Newest;
    }

    public void Uninstall()
    {
        if (StartupPath is null) return;

        Status = EPluginStatusType.Modifying;

        if (IsValidInstallation)
            Directory.Delete(Path.Combine(StartupPath, "fortnite_porting"), true);
    }

    public async Task Launch()
    {
        App.Launch(BlenderPath);
    }
}