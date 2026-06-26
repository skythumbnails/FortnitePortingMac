using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.Texture;
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
            }
        }
    }

    // Captured when Object is first set so identity-only consumers (the search filter runs across
    // every item on each keystroke) never force a re-parse of the whole object.
    public string? ObjectPath { get; set; }
    public string ObjectName { get; set; } = string.Empty;

    // Drops the parsed object tree; the getter reloads it the next time it's genuinely needed.
    public void ReleaseObject() => _object = null;

    // Nullable + released after construction for the same reason: a loaded UTexture2D retains its
    // compressed mip bytes (~0.2-1.4 MB each). AssetItem records the icon path and lazy-decodes.
    public required UTexture2D? Icon { get; set; }
    public FGameplayTagContainer? GameplayTags { get; set; }

    public bool HideRarity { get; set; } = false;
}
