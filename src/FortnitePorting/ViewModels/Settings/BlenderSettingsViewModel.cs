using System;
using FluentAvalonia.UI.Controls;
using System.Linq;
using System.IO;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CUE4Parse_Conversion.Options;
using FortnitePorting.Exporting.Models;

namespace FortnitePorting.ViewModels.Settings;

public partial class BlenderSettingsViewModel : BaseExportSettings
{
    public bool IsTastyRig => RigType == ERigType.Tasty;
    
    // General
    [ObservableProperty] private bool _scaleDown = true;
    [ObservableProperty] private bool _importIntoCollection = true;
    [ObservableProperty] private bool _importAt3DCursor = false;
    
    // Armature
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsTastyRig))] private ERigType _rigType = ERigType.Default;
    [ObservableProperty] private bool _mergeArmatures = true;
    [ObservableProperty] private bool _reorientBones = false;
    [ObservableProperty] private bool _importSockets = true;
    [ObservableProperty] private bool _importVirtualBones = false;
    [ObservableProperty] private bool _useDynamicBoneShape = true;
    [ObservableProperty] private float _boneLength = 4.0f;
    
    // Mesh
    [ObservableProperty] private int _targetLOD = 0;
    [ObservableProperty] private EPolygonType _polygonType;
    [ObservableProperty] private bool _importCollision = false;
    
    // Material
    [ObservableProperty] private float _ambientOcclusion = 0.0f;
    [ObservableProperty] private float _cavity = 0.0f;
    [ObservableProperty] private float _subsurface = 0.0f;
    [ObservableProperty] private float _toonShadingBrightness = 0.5f;
    [ObservableProperty] private EMaterialImportMethod _materialImportMethod = EMaterialImportMethod.Data;
    [ObservableProperty] private bool _useForgeMaterials = false;
    [ObservableProperty] private string _forgeBlendPath = string.Empty;
    [ObservableProperty] private EForgeScope _forgeScope = EForgeScope.Everything;

    // Texture
    [ObservableProperty] private ETextureImportMethod _textureImportMethod = ETextureImportMethod.Data;
    
    // Animation
    [ObservableProperty] private bool _loopAnimation = false;
    [ObservableProperty] private bool _updateTimelineLength = false;
    [ObservableProperty] private bool _importSounds = false;
    
    [RelayCommand]
    public async Task BrowseForgeBlendPath()
    {
        // The standard picker is safe: the freeze it got blamed for was SettingsService.Save()
        // serializing this command's still-running task (see NoRuntimeStateContractResolver).
        if (await App.BrowseFileDialog(fileTypes: Globals.BlendFileType) is { } path)
        {
            ForgeBlendPath = path;

            // Forge is detected by the file's CONTENTS (the "Forge V4" material inside),
            // never by its filename — any name works. Warn if this doesn't look like one,
            // but keep the path; the export itself does the authoritative check.
            if (!await Task.Run(() => IsForgeBlend(path)))
            {
                Info.Message("Forge Materials",
                    $"\"{System.IO.Path.GetFileName(path)}\" doesn't contain a Forge V4 material — exports will fall back to the standard shader.",
                    InfoBarSeverity.Warning, autoClose: false);
            }
        }
    }

    // A Forge blend is identified by the datablocks inside it, never by its filename: Blender
    // stores datablock names as plain ASCII, so the "Forge V4" material and the "FV4" shader
    // groups are findable with a streaming byte scan (no Blender/parse dependency).
    private static bool IsForgeBlend(string path)
    {
        try
        {
            var material = System.Text.Encoding.ASCII.GetBytes("Forge V4");
            var groups = System.Text.Encoding.ASCII.GetBytes("FV4");
            var foundMaterial = false;
            var foundGroups = false;

            using var stream = File.OpenRead(path);
            var buffer = new byte[1 << 20];
            var carry = Math.Max(material.Length, groups.Length) - 1;
            var offset = 0;
            int read;
            while ((read = stream.Read(buffer, offset, buffer.Length - offset)) > 0)
            {
                var span = buffer.AsSpan(0, offset + read);
                if (!foundMaterial) foundMaterial = span.IndexOf(material) >= 0;
                if (!foundGroups) foundGroups = span.IndexOf(groups) >= 0;
                if (foundMaterial && foundGroups) return true;

                // Keep the tail so a name split across reads is still matched.
                span[^carry..].CopyTo(buffer);
                offset = carry;
            }
        }
        catch { /* unreadable/locked file */ }
        return false;
    }

    public override ExportSettings ToExportSettings()
    {
        var settings = base.ToExportSettings();
        settings.MeshFormat = EMeshFormat.UEFormat;
        settings.MeshQuality = EMeshQuality.All;
        return settings;
    }
}
public enum ERigType
{
    [Description("Default Rig (FK)")]
    Default,

    [Description("Tasty Rig (IK)")]
    Tasty
}

public enum EPolygonType
{
    [Description("Triangles")]
    Tris,

    [Description("Quads")]
    Quads
}

public enum ETextureImportMethod
{
    [Description("As Texture Data")]
    Data,

    [Description("As Object")]
    Object
}

public enum EMaterialImportMethod
{
    [Description("As Material Data")]
    Data,

    [Description("As Object")]
    Object
}
