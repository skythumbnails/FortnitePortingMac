using System.Net;
using System.Net.Http.Headers;
using CUE4Parse.UE4.IO.Objects.OnDemand;
using CUE4Parse.UE4.IO.Objects.OnDemand.V1;
using CUE4Parse.UE4.Readers;
using CUE4Parse.UE4.Versions;
using CUE4Parse.Utils;

namespace CUE4Parse.UE4.IO;

public class IoChunkToc
{
    public IOnDemandToc OnDemandToc;

    public IoChunkToc(string file, VersionContainer versions) : this(new FileInfo(file), versions) { }
    public IoChunkToc(FileInfo file, VersionContainer versions) : this(new FByteArchive(file.FullName, File.ReadAllBytes(file.FullName), versions)) { }
    public IoChunkToc(FArchive Ar)
    {
        var bIsLegacy = Ar.Read<ulong>() == 0x6f6e64656d616e64;
        Ar.Position = 0;

        OnDemandToc = bIsLegacy ? new FOnDemandToc(Ar) : new Objects.OnDemand.V2.FOnDemandToc(Ar);
    }
}

public class IoStoreOnDemandOptions
{
    public HttpClient? DownloaderClient { get; set; }
    public required Uri ChunkHostUri { get; init; }
    public DirectoryInfo? ChunkCacheDirectory { get; init; }
    public AuthenticationHeaderValue? Authorization { get; set; }
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);

    public bool UseAuth => Authorization != null;
}

public class IoStoreOnDemandDownloader : IDisposable
{
    private readonly IoStoreOnDemandOptions _options;
    private readonly HttpClient _client;
    private readonly bool _disposeClient;

    public IoStoreOnDemandDownloader(IoStoreOnDemandOptions options)
    {
        _options = options;
        _client = options.DownloaderClient ?? new HttpClient(new SocketsHttpHandler
        {
            UseProxy = false,
            UseCookies = false,
            AutomaticDecompression = DecompressionMethods.None
        }) { Timeout = options.Timeout };
        _disposeClient = options.DownloaderClient is null;
    }

    public async Task<Stream> Download(string url, long position = 0)
    {
        var cachePath = _options.ChunkCacheDirectory is not null && _options.ChunkCacheDirectory.Exists
            ? Path.Combine(_options.ChunkCacheDirectory.FullName, url.SubstringAfterLast('/'))
            : null;
        if (cachePath is not null && File.Exists(cachePath))
        {
            if (IsValidCachedChunk(cachePath, position))
            {
                var fs = new FileStream(cachePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                fs.Position = position;
                return fs;
            }

            // Poisoned or truncated cache entry (e.g. a cached CDN error document, or a partial
            // write left behind by a previous run) — delete it and fall through to a fresh download.
            try { File.Delete(cachePath); } catch { /* ignored */ }
        }

        Exception? lastException = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                using var requestMessage = new HttpRequestMessage(HttpMethod.Get, new Uri(_options.ChunkHostUri, url));
                if (_options.UseAuth) requestMessage.Headers.Authorization = _options.Authorization;
                using var response = await _client.SendAsync(requestMessage).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                var outData = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                if (LooksLikeErrorDocument(outData))
                    throw new HttpRequestException($"Chunk request for '{url}' returned an error document instead of chunk data");

                var outStream = new MemoryStream(outData, 0, outData.Length, false, true);

                if (cachePath is not null)
                {
                    // Write to a temp file and atomically move it into place so a concurrent reader
                    // can never observe a partially written chunk, and a crashed run can never leave
                    // a truncated file at the final path.
                    var tempPath = cachePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    try
                    {
                        await using (var cacheFs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                        {
                            await outStream.CopyToAsync(cacheFs).ConfigureAwait(false);
                        }
                        File.Move(tempPath, cachePath, overwrite: true);
                    }
                    catch
                    {
                        try { File.Delete(tempPath); } catch { /* ignored */ }
                    }
                }

                outStream.Position = position;
                return outStream;
            }
            catch (Exception e)
            {
                lastException = e;
                Serilog.Log.Warning("OnDemand chunk download attempt {Attempt}/3 failed for '{Url}' (host {Host}): {Message}", attempt + 1, url, _options.ChunkHostUri, e.Message);
                if (attempt < 2)
                    await Task.Delay(TimeSpan.FromMilliseconds(250 * (attempt + 1))).ConfigureAwait(false);
            }
        }

        throw lastException!;
    }

    private static bool LooksLikeErrorDocument(ReadOnlySpan<byte> data)
    {
        // S3/CloudFront style error bodies ("<?xml ...><Error><Code>AccessDenied</Code>...") and
        // HTML error pages are occasionally served with a success status; they must never be
        // treated (or cached) as chunk data. Match known markup prefixes rather than a bare '<'
        // so a legitimate small binary chunk whose first byte happens to be 0x3C ('<') is never
        // misclassified — chunk files are content-addressed, so a false positive here would
        // deterministically break the same asset on every run.
        if (data.Length == 0) return true;
        if (data.Length >= 4096) return false;
        return data.StartsWith("<?xml"u8)
               || data.StartsWith("<Error"u8)
               || data.StartsWith("<!DOCTYPE"u8)
               || data.StartsWith("<html"u8)
               || data.StartsWith("<HTML"u8);
    }

    private static bool IsValidCachedChunk(string cachePath, long position)
    {
        try
        {
            var info = new FileInfo(cachePath);
            if (info.Length == 0 || info.Length <= position) return false;
            if (info.Length < 4096)
            {
                using var probe = new FileStream(cachePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                Span<byte> head = stackalloc byte[16];
                var read = probe.Read(head);
                if (LooksLikeErrorDocument(head[..read])) return false; // cached XML/HTML error document
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposeClient)
            _client.Dispose();
    }
}
