using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Layout;
using CommunityToolkit.Mvvm.ComponentModel;
using FluentAvalonia.UI.Controls;
using FortnitePorting.Framework;
using FortnitePorting.Models.Information;
using FortnitePorting.Models.Plugin;
using FortnitePorting.Services;
using Newtonsoft.Json;

namespace FortnitePorting.ViewModels.Plugin;

public partial class BlenderPluginViewModel : ViewModelBase
{
    [ObservableProperty] private bool _automaticallySync = true;
    [ObservableProperty] private ObservableCollection<BlenderInstallation> _installations = [];

    [ObservableProperty] private bool _completedFirstInstall;

    public override async Task Initialize()
    {
        if (!BlenderInstallation.PluginWorkingDirectory.Exists)
            BlenderInstallation.PluginWorkingDirectory.Create();

        foreach (var installation in Installations.ToArray())
        {
            if (installation.SyncExtensionVersion()) continue;
            
            installation.Uninstall();
            Installations.Remove(installation);
        }
    }

    public async Task AddInstallation()
    {
        string blenderPath;

        if (OperatingSystem.IsMacOS())
        {
            // Avalonia's StorageProvider doesn't make .app bundles selectable in its file picker
            // on macOS, even with AppleUniformTypeIdentifiers set — the panel ends up showing
            // bundles greyed out and only folders selectable. Shell out to AppleScript instead;
            // its `choose file of type` dialog goes straight through AppKit and treats .app
            // bundles as picks the same way Finder does.
            string? bundlePath = null;
            try
            {
                using var process = new Process();
                process.StartInfo = new ProcessStartInfo
                {
                    FileName = "/usr/bin/osascript",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                process.StartInfo.ArgumentList.Add("-e");
                process.StartInfo.ArgumentList.Add(
                    "POSIX path of (choose file of type {\"com.apple.application-bundle\"} " +
                    "default location (POSIX file \"/Applications\") " +
                    "with prompt \"Select Blender.app\")");
                process.Start();
                var output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();
                if (process.ExitCode == 0)
                {
                    bundlePath = output.Trim();
                }
            }
            catch
            {
                // osascript missing or blocked — bail; user can retry.
            }

            if (string.IsNullOrEmpty(bundlePath)) return;

            bundlePath = bundlePath.TrimEnd('/');
            blenderPath = Path.Combine(bundlePath, "Contents", "MacOS", "Blender");
            if (!File.Exists(blenderPath))
            {
                Info.Message("Blender Plugin",
                    $"Could not find the Blender binary inside {bundlePath}.\nExpected: {blenderPath}",
                    InfoBarSeverity.Error, autoClose: false);
                return;
            }
        }
        else
        {
            if (await App.BrowseFileDialog(fileTypes: Globals.BlenderFileType) is not { } filePath) return;
            blenderPath = filePath;
        }

        var blenderVersion = BlenderInstallation.GetVersion(blenderPath);
        if (Installations.Any(existing => existing.BlenderVersion == blenderVersion))
        {
            Info.Message("Blender Extension", $"The plugin for Blender {blenderVersion} has already been installed.", InfoBarSeverity.Warning);
            return;
        }
        
        if (blenderVersion < BlenderInstallation.MinimumVersion)
        {
            Info.Message("Blender Plugin", 
                $"Blender {blenderVersion} is too low of a version. Only Blender versions {BlenderInstallation.MinimumVersion} and higher are supported.", 
                InfoBarSeverity.Error, autoClose: false);
            return;
        }
        
        if (TryGetBlenderProcess(blenderPath, out var blenderProcess))
        {
            Info.Message("Failed to Add Blender Installation", 
                $"This version of blender is currently open. Please close it and re-add the installation.", 
                InfoBarSeverity.Error, autoClose: false, 
                useButton: true, buttonTitle: "Kill Blender Process", buttonCommand: () =>
                {
                    blenderProcess.Kill(entireProcessTree: true);
                });
            return;
        }

        var installation = new BlenderInstallation(blenderPath);
        Installations.Add(installation);

        await TaskService.RunAsync(() =>
        {
            installation.Install();

            if (!CompletedFirstInstall)
            {
                Info.Message("Blender Plugin", "In Fortnite Porting V4, you no longer need to enable the plugin in Blender. The plugin should now be working as is and you are free to continue!", autoClose: false);
                CompletedFirstInstall = true;
            }
        });
    }

    public async Task RemoveInstallation(BlenderInstallation installation)
    {
        TaskService.Run(() =>
        {
            installation.Uninstall();
            Installations.Remove(installation);
        });
    }

    public async Task SyncInstallations()
    {
        await SyncInstallations(true);
    }
    
    public async Task SyncInstallations(bool verbose)
    {
        var currentVersion = Globals.Version.ToVersion();
        foreach (var installation in Installations)
        {
            installation.SyncExtensionVersion();
            if (TryGetBlenderProcess(installation.BlenderPath, out var blenderProcess))
            {
                if (verbose)
                {
                    Info.Message("Blender Extension", 
                        $"Blender {installation.BlenderVersion} is currently open. Please close it and re-sync the installation.\nPath: {installation.BlenderPath}\nPID: {blenderProcess.Id}", 
                        InfoBarSeverity.Error, autoClose: false);
                }
                continue;
            }

            if (currentVersion == installation.ExtensionVersion)
            {
                if (verbose)
                {
                    Info.Message("Blender Extension", $"Blender {installation.BlenderVersion} is already up to date, syncing anyways.");
                }
                installation.Install(verbose);
                continue;
            }

            var previousVersion = installation.ExtensionVersion;
            installation.Install(verbose);

            if (verbose)
            {
                Info.Message("Blender Extension", $"Successfully updated the Blender {installation.BlenderVersion} extension from {previousVersion} to {currentVersion}");
            }
        }
    }

    private static bool TryGetBlenderProcess(string path, [MaybeNullWhen(false)] out Process process)
    {
        var blenderProcesses = Process.GetProcessesByName("blender");
        var normalizedPath = OperatingSystem.IsWindows() ? path.Replace("/", "\\") : path;
        process = blenderProcesses.FirstOrDefault(process => process.MainModule is { } mainModule && mainModule.FileName.Equals(normalizedPath));
        return process is not null;
    }
}