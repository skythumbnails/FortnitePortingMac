using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using FortnitePorting.ViewModels;
using FortnitePorting.ViewModels.Settings;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Serilog;

namespace FortnitePorting.Services;

public partial class SettingsService : ObservableObject, IService
{
    // The settings view models expose MVVM-toolkit generated *Command properties. Newtonsoft
    // happily serializes those command objects — including their live ExecutionTask, whose
    // Result getter BLOCKS until the command finishes. If a command is still running when a
    // tab switch triggers Save() (e.g. a file picker is open), the UI thread waits on a task
    // whose continuation needs the UI thread: a permanent deadlock that looks like a freeze.
    // Commands and tasks are runtime state, not settings — never serialize them.
    private class NoRuntimeStateContractResolver : DefaultContractResolver
    {
        protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization)
        {
            var property = base.CreateProperty(member, memberSerialization);
            if (property.PropertyType is { } type &&
                (typeof(ICommand).IsAssignableFrom(type) || typeof(Task).IsAssignableFrom(type)))
            {
                property.Ignored = true;
            }
            return property;
        }
    }

    private static readonly JsonSerializerSettings SerializerSettings = new()
    {
        ContractResolver = new NoRuntimeStateContractResolver()
    };

    [ObservableProperty] private ExportSettingsViewModel _exportSettings = new();
    [ObservableProperty] private InstallationSettingsViewModel _installation = new();
    [ObservableProperty] private ApplicationSettingsViewModel _application = new();
    [ObservableProperty] private AccountSettingsViewModel _account = new();
    [ObservableProperty] private PluginViewModel _plugin = new();
    [ObservableProperty] private DeveloperSettingsViewModel _developer = new();

    [JsonIgnore]
    public bool ShouldSaveOnExit = true;
    
    public static readonly DirectoryInfo DirectoryPath = new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FortnitePorting"));
    public static readonly FileInfo FilePath = new(Path.Combine(DirectoryPath.FullName, "AppSettingsV4.json"));

    public SettingsService()
    {
        DirectoryPath.Create();
    }
    
    public void Load()
    {
        if (!FilePath.Exists) return;
        
        try
        {
            var settings = JsonConvert.DeserializeObject<SettingsService>(File.ReadAllText(FilePath.FullName), SerializerSettings);
            if (settings is null) return;

            foreach (var property in settings.GetType().GetProperties())
            {
                // Skip read-only properties — a `return` here (as it was) aborts the whole load on
                // the first one, leaving every later setting (incl. export/Forge) at its default.
                if (!property.CanWrite) continue;

                var value = property.GetValue(settings);
                property.SetValue(this, value);
            }
        }
        catch (Exception e)
        {
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd-hh-mm-ss");
            var backupName = $"{Path.GetFileNameWithoutExtension(FilePath.Name)}.recovery-{timestamp}.json";
            var backupFile = new FileInfo(Path.Combine(FilePath.DirectoryName!, backupName));
            File.Copy(FilePath.FullName, backupFile.FullName);
            
            Log.Information($"Failed to load settings, backed up to {backupFile.FullName}");
            Log.Error(e.ToString());
        }
    }

    public void Save()
    {
        try
        {
            File.WriteAllText(FilePath.FullName, JsonConvert.SerializeObject(this, Formatting.Indented, SerializerSettings));
        }
        catch (Exception e)
        {
            Log.Error("Failed to save settings:");
            Log.Error(e.ToString());
        }
    }
    
    public void Reset()
    {
        File.Delete(FilePath.FullName);
        ShouldSaveOnExit = false;
    }
}