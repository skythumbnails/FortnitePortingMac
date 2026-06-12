using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CUE4Parse_Conversion.Textures;
using CUE4Parse.GameTypes.FN.Enums;
using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse.UE4.Objects.UObject;
using CUE4Parse.Utils;
using FortnitePorting.Exporting;
using FortnitePorting.Extensions;
using FortnitePorting.Framework;
using FortnitePorting.Models.Clipboard;
using FortnitePorting.Models.Fortnite;
using FortnitePorting.Views;
using FortnitePorting.Windows;
using Newtonsoft.Json;
using SkiaSharp;
using SkiaExtensions = FortnitePorting.Extensions.SkiaExtensions;

namespace FortnitePorting.Models.Assets.Asset;


public class AssetItem : Base.BaseAssetItem
{
    public new AssetItemCreationArgs CreationData
    {
        get => (AssetItemCreationArgs) base.CreationData;
        private init => base.CreationData = value;
    }

    public EFortRarity Rarity { get; set; }
    public int Season { get; set; }
    public UFortItemSeriesDefinition? Series { get; set; }
    public string? SetName { get; set; }

    public const int INVALID_SEASON = int.MaxValue;

    private static SKColor InnerBackgroundColor = SKColor.Parse("#2bb5f3");
    private static SKColor OuterBackgroundColor = SKColor.Parse("#174a89");

    private static ConcurrentDictionary<string, UFortItemSeriesDefinition> SeriesCache = [];
    private static ConcurrentDictionary<string, WriteableBitmap> BackgroundCache = [];
    
    // Package path of the icon texture; the texture itself is loaded per decode, never retained.
    private readonly string? _iconPath;

    public AssetItem(AssetItemCreationArgs args)
    {
        Id = Guid.NewGuid();
        CreationData = args;

        // Detach the loaded icon texture immediately: keep only its package path and let the
        // UTexture2D (with its compressed mip bytes) be garbage collected. LoadBitmap re-loads
        // it from the provider on demand. Retaining the texture on every grid item was the
        // single largest idle-memory consumer in the app (GBs across a few thousand cosmetics).
        _iconPath = args.Icon?.GetPathName();
        args.Icon = null;

        IsFavorite = AppSettings.Application.FavoriteAssets.Contains(CreationData.Object.GetPathName());

        Rarity = CreationData.Object.GetOrDefault("Rarity", EFortRarity.Uncommon);
        
        if (CreationData.Object.GetDataListItem<FName?>("Rarity") is { } dataListRarityName
            && Enum.TryParse(dataListRarityName.Text.SubstringAfter("::"), out EFortRarity dataListRarity))
            Rarity = dataListRarity;

        if (CreationData.GameplayTags.GetValueOrDefault("Cosmetics.Set")?.Text is { } setTag &&
            UEParse.SetNames.TryGetValue(setTag, out var setName))
        {
            SetName = setName;
        }
        
        var seasonTag = CreationData.GameplayTags.GetValueOrDefault("Cosmetics.Filter.Season.")?.Text;
        Season = int.TryParse(seasonTag?.SubstringAfterLast("."), out var seasonNumber) ? seasonNumber : INVALID_SEASON;

        if (CreationData.Object.GetDataListItem<FPackageIndex>("Series") is { } seriesPackage)
        {
            Series = SeriesCache.GetOrAdd(seriesPackage.Name,
                _ => seriesPackage.Load<UFortItemSeriesDefinition>());
        }
    }

    // Grid thumbnails render small, so there's no reason to decode/hold each cosmetic icon at its
    // native resolution (commonly 512x512–1024x1024 RGBA = 1–4 MB). Downscaling to this cap cuts
    // per-thumbnail memory 4–16x. 256px stays crisp on retina at the grid's display size and is
    // still a reasonable resolution for the "copy icon" action.
    private const int IconDisplayMaxDimension = 256;

    // Shared blank icon for assets whose texture can't be resolved (e.g. new asset classes whose
    // icon property didn't parse, or archives missing the placeholder). Items still render as
    // rarity cards instead of being dropped or stuck failing.
    private static readonly Lazy<WriteableBitmap> BlankIcon = new(() =>
    {
        using var blank = new SKBitmap(new SKImageInfo(2, 2, SKColorType.Bgra8888, SKAlphaType.Premul));
        return blank.ToWriteableBitmap();
    });

    public void LoadBitmap()
    {
        // Background first: even if the icon fails below, the item shows as a rarity card.
        BackgroundImage = CreateBackgroundImage();

        // Lazy-load the icon texture for this decode only — it goes out of scope right after,
        // so its compressed mip bytes don't accumulate across the grid.
        var iconTexture = _iconPath is not null
            ? UEParse.Provider.SafeLoadPackageObject<UTexture2D>(_iconPath)
            : null;
        if (iconTexture is null)
        {
            IconDisplayImage = BlankIcon.Value;
            return;
        }

        // `using` matters: ToWriteableBitmap copies pixels into the WriteableBitmap's own buffer,
        // so these intermediate SKBitmaps can be released immediately. Previously the decoded
        // SKBitmap was never disposed, leaking native Skia memory on every single decode.
        using var decoded = iconTexture.Decode()!.ToSkBitmap();
        using var scaled = DownscaleToFit(decoded, IconDisplayMaxDimension);
        IconDisplayImage = (scaled ?? decoded).ToWriteableBitmap();
    }

    // Returns a downscaled copy when the source exceeds maxDimension, or null when it's already
    // small enough (caller then uses the original). Caller owns disposing the returned bitmap.
    private static SKBitmap? DownscaleToFit(SKBitmap source, int maxDimension)
    {
        var longest = Math.Max(source.Width, source.Height);
        if (longest <= maxDimension) return null;

        var scale = maxDimension / (float) longest;
        var width = Math.Max(1, (int) (source.Width * scale));
        var height = Math.Max(1, (int) (source.Height * scale));
        var info = new SKImageInfo(width, height, source.ColorType, source.AlphaType);
        return source.Resize(info, SKFilterQuality.Medium);
    }

    protected sealed override WriteableBitmap CreateBackgroundImage()
    {
        var backgroundKey = Series?.Name ?? "Default";
        if (BackgroundCache.TryGetValue(backgroundKey, out var existingBackground))
        {
            return existingBackground;
        }
        
        var skiaBitmap = new SKBitmap(128, 160, SKColorType.Rgba8888, SKAlphaType.Opaque);
        using (var canvas = new SKCanvas(skiaBitmap))
        {
            var backgroundRect = new SKRect(0, 0, skiaBitmap.Width, skiaBitmap.Height);
            if (Series?.Colors is { } colors)
            {
                if (Series?.BackgroundTexture.LoadOrDefault<UTexture2D>() is { } seriesBackground)
                {
                    canvas.DrawBitmap(seriesBackground.Decode()?.ToSkBitmap(), backgroundRect);
                }
                else
                {
                    
                    var backgroundPaint = new SKPaint { Shader = SkiaExtensions.RadialGradient(skiaBitmap.Height, colors.Color1, colors.Color3) };
                    canvas.DrawRect(backgroundRect, backgroundPaint);
                }
            }
            else
            {
                var backgroundPaint = new SKPaint { Shader = SkiaExtensions.RadialGradient(skiaBitmap.Height, InnerBackgroundColor, OuterBackgroundColor) };
                canvas.DrawRect(backgroundRect, backgroundPaint);
            }
            
        }

        var bitmap = skiaBitmap.ToWriteableBitmap();
        BackgroundCache.TryAdd(backgroundKey, bitmap);
        return bitmap;
    }

    public override async Task NavigateTo()
    {
        Navigation.App.Open<FilesView>();

        var assetPath = UEParse.Provider.FixPath(CreationData.Object.GetPathName().SubstringBefore("."));
        FilesVM.JumpTo(assetPath);
        
        AppWM.Window.BringToTop();
    }

    public override async Task CopyPath()
    {
        await App.Clipboard.SetTextAsync(CreationData.Object.GetPathName());
    }

    public override async Task PreviewProperties()
    {
        var assets = await UEParse.Provider.LoadAllObjectsAsync(Exporter.FixPath(CreationData.Object.GetPathName()));
        var json = JsonConvert.SerializeObject(assets, Formatting.Indented);
        PropertiesPreviewWindow.Preview(CreationData.Object.Name, json);
    }
    
    public override async Task CopyIcon(bool withBackground = false)
    {
        await AvaloniaClipboard.SetImageAsync(IconDisplayImage);
    }
    
    public override void Favorite()
    {
        var path = CreationData.Object.GetPathName();
        if (AppSettings.Application.FavoriteAssets.Add(path))
        {
            IsFavorite = true;
        }
        else
        {
            AppSettings.Application.FavoriteAssets.Remove(path);
            IsFavorite = false;
        }
    }
}