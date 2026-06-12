using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse.UE4.Objects.GameplayTags;
using FortnitePorting.Models.Assets.Base;

namespace FortnitePorting.Models.Assets.Asset;

public class AssetItemCreationArgs : BaseAssetItemCreationArgs
{
    public required UObject Object { get; set; }
    // Nullable because AssetItem's constructor detaches it: a loaded UTexture2D retains its
    // compressed mip bytes in managed memory (~0.2-1.4 MB each), and grid tabs create thousands
    // of items — keeping every icon texture alive accounted for multiple GB of idle memory.
    // AssetItem records the package path instead and lazy-loads the texture per decode.
    public required UTexture2D? Icon { get; set; }
    public FGameplayTagContainer? GameplayTags { get; set; }

    public bool HideRarity { get; set; } = false;
}