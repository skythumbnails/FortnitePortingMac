using System.Collections.Generic;
using CUE4Parse.UE4.AssetRegistry.Readers;
using CUE4Parse.UE4.Exceptions;
using CUE4Parse.UE4.Objects.UObject;
using CUE4Parse.UE4.Readers;
using CUE4Parse.UE4.Versions;
using CUE4Parse.Utils;

namespace CUE4Parse.UE4.AssetRegistry.Objects;

public class FAssetRegistryReader : FAssetRegistryArchive
{
    private const uint AssetRegistryNumberedNameBit = 0x80000000u; // int32 max
    public readonly FStore Tags;

    public FAssetRegistryReader(FArchive Ar, FAssetRegistryHeader header) : base(Ar, header)
    {
        NameMap = FNameEntrySerialized.LoadNameBatch(Ar);
        Tags = new FStore(this);
    }

    public override void SkipFName()
    {
        var index = baseArchive.Read<uint>();
        if ((index & AssetRegistryNumberedNameBit) > 0u) baseArchive.Position += 4;
    }

    public override FName ReadFName()
    {
        var index = baseArchive.Read<uint>();
        var number = 0u;
        if ((index & AssetRegistryNumberedNameBit) > 0u)
        {
            index -= AssetRegistryNumberedNameBit;
            number = baseArchive.Read<uint>();
        }

#if !NO_FNAME_VALIDATION
        if (index >= NameMap.Length)
        {
            throw new ParserException(baseArchive, $"FName could not be read, requested index {index}, name map size {NameMap.Length}");
        }
#endif
        return new FName(NameMap, (int) index, (int) number);
    }

    public override void SkipTagsAndBundles()
    {
        Position += 8;
        var len = Read<int>();
        for (var i = 0; i < len; i++)
        {
            SkipFName();
            var BundleAssetsLength = Read<int>();
            for (var j = 0; j < BundleAssetsLength; j++)
            {
                SkipFName();
                if (Header.Version >= FAssetRegistryVersionType.ClassPaths)
                    SkipFName();
                SkipFString();
            }
        }

    }

    public override void SerializeTagsAndBundles(FAssetData assetData)
    {
        var size = baseArchive.Read<ulong>();
        // Tags live in the shared `Tags` store and are read by handle, so we don't need to iterate
        // them to keep the archive position correct (only the size read above advances baseArchive).
        // FortnitePorting never reads asset tags, so skip materializing the per-asset dictionary and
        // its value strings entirely — this is the bulk of the registry-parse memory.
        assetData.TagsAndValues = FAssetData.EmptyTags;
        assetData.TaggedAssetBundles = new FAssetBundleData(this);
    }

    public void AlignPosInArchive()
    {
        if (Header.Version < FAssetRegistryVersionType.MemoryMappedTagDataStore)
            return;
        
        baseArchive.Position = baseArchive.Position.Align(16);
    }

    public override object Clone() => new FAssetRegistryReader((FArchive) baseArchive.Clone(), Header);
}
