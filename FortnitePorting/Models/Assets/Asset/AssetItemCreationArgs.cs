using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Objects.GameplayTags;
using FortnitePorting.Models.Assets.Base;

namespace FortnitePorting.Models.Assets.Asset;

public class AssetItemCreationArgs : BaseAssetItemCreationArgs
{
    private UObject? _object;

    // The fully-parsed asset definition. A tab can create thousands of AssetItems (e.g. ~3,400
    // outfits), and holding every parsed UObject alive was the dominant memory cost while browsing
    // (multiple GB). Once the AssetItem constructor has read what the grid needs (rarity, series,
    // name, icon path), the object is released via ReleaseObject(); this getter re-parses it on
    // demand for the operations that actually need it (export, preview, style enumeration). The
    // CUE4Parse provider does not cache packages, so releasing genuinely returns the memory.
    public required UObject? Object
    {
        get
        {
            if (_object is not null) return _object;
            if (string.IsNullOrEmpty(ObjectPath)) return null;
            _object = UEParse.Provider.SafeLoadPackageObject(ObjectPath);
            return _object;
        }
        set
        {
            _object = value;
            if (value is not null)
            {
                ObjectPath = value.GetPathName();
                ObjectName = value.Name;
                ObjectClassName = value.ExportType;
            }
        }
    }

    // Captured when Object is first set so identity-only consumers (the search filter runs across
    // every item on each keystroke, and the category filters across every item when toggled) never
    // force a re-parse of the whole object after it's been released. ObjectClassName is the UE class
    // name (e.g. "AthenaGadgetItemDefinition") — distinct from the base ExportType, which is our
    // EExportType category enum.
    public string? ObjectPath { get; set; }
    public string ObjectName { get; set; } = string.Empty;
    public string ObjectClassName { get; set; } = string.Empty;

    // Drops the parsed object tree; the getter reloads it the next time it's genuinely needed.
    public void ReleaseObject() => _object = null;


    public string? LowResIconPath { get; set; }
    public string? HighResIconPath { get; set; }
    public string? IconPath => LowResIconPath ?? HighResIconPath;
    public FGameplayTagContainer? GameplayTags { get; set; }

    public bool HideRarity { get; set; } = false;
}
