using System;
using System.ComponentModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CUE4Parse_Conversion;
using CUE4Parse_Conversion.Animations;
using CUE4Parse_Conversion.Meshes;
using CUE4Parse.UE4.Assets.Exports.Nanite;

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

    // Texture
    [ObservableProperty] private ETextureImportMethod _textureImportMethod = ETextureImportMethod.Data;
    
    // Animation
    [ObservableProperty] private bool _loopAnimation = false;
    [ObservableProperty] private bool _updateTimelineLength = false;
    [ObservableProperty] private bool _importSounds = false;
    
    [RelayCommand]
    public async Task BrowseForgeBlendPath()
    {
        if (OperatingSystem.IsMacOS())
        {
            // Avalonia's StorageProvider file picker hard-crashes the app on macOS when opened
            // from settings (native NSOpenPanel re-entrancy). Shell out to AppleScript's
            // `choose file` dialog, exactly like the Blender.app picker in BlenderPluginViewModel.
            string? picked = null;
            try
            {
                using var process = new System.Diagnostics.Process();
                process.StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "/usr/bin/osascript",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                process.StartInfo.ArgumentList.Add("-e");
                process.StartInfo.ArgumentList.Add(
                    "POSIX path of (choose file of type {\"blend\"} " +
                    "default location (POSIX file \"" +
                    (string.IsNullOrEmpty(ForgeBlendPath) ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "/Downloads" : System.IO.Path.GetDirectoryName(ForgeBlendPath)) +
                    "\") with prompt \"Select Forge V4 Official.blend\")");
                process.Start();
                var output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();
                if (process.ExitCode == 0) picked = output.Trim();
            }
            catch
            {
                // osascript missing/blocked (or user cancelled) — leave the path unchanged.
            }

            if (!string.IsNullOrEmpty(picked)) ForgeBlendPath = picked.TrimEnd('/');
            return;
        }

        if (await App.BrowseFileDialog(fileTypes: Globals.BlendFileType, suggestedFileName: ForgeBlendPath) is { } path)
        {
            ForgeBlendPath = path;
        }
    }

    public override ExporterOptions CreateExportOptions()
    {
        return new ExporterOptions
        {
            LodFormat = ELodFormat.AllLods,
            MeshFormat = EMeshFormat.UEFormat,
            AnimFormat = EAnimFormat.UEFormat,
            NaniteMeshFormat = ExportNanite ? ENaniteMeshFormat.NaniteSeparateFile : ENaniteMeshFormat.OnlyNormalLODs,
            CompressionFormat = CompressionFormat,
            ExportMorphTargets = true,
            ExportMaterials = false
        };
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