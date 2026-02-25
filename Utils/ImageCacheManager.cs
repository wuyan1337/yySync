using System;
using System.Buffers;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
namespace MusicRpc.Utils;
internal static class ImageCacheManager
{
    private static readonly int MaxCacheSize = PerformanceConfig.ImageCacheMaxSize;
    private static readonly Dictionary<string, LinkedListNode<CacheItem>> Cache = new();
    private static readonly LinkedList<CacheItem> LruList = [];
    private static readonly Lock LockObject = new();
    private static readonly HashSet<string> ActiveKeys = [];
    private static readonly HashSet<string> LoggedCacheKeys = [];
    private class CacheItem(string key, Image image)
    {
        public string Key { get; } = key;
        public Image Image { get; } = image;
        public DateTime LastAccessed { get; set; } = DateTime.Now;
    }
    public static void SetActiveKeys(IEnumerable<string> keys)
    {
        lock (LockObject)
        {
            ActiveKeys.Clear();
            foreach (var key in keys)
            {
                if (!string.IsNullOrEmpty(key))
                {
                    ActiveKeys.Add(key);
                }
            }
        }
    }
    public static void PerformAggressiveCleanup()
    {
        lock (LockObject)
        {
            var currentMemory = MemoryPressureMonitor.GetCurrentMemoryUsage();
            List<string> keysToRemove;
            if (currentMemory > 30 * 1024 * 1024)
            {
                var targetCount = Math.Max(5, Cache.Count / 2); 
                keysToRemove = Cache.OrderBy(kvp => kvp.Value.Value.LastAccessed)
                    .Take(Cache.Count - targetCount)
                    .Select(kvp => kvp.Key)
                    .ToList();
                foreach (var key in keysToRemove)
                {
                    RemoveFromCacheInternal(key);
                }
                Logger.Diagnose(
                    $"Aggressive cleanup removed {keysToRemove.Count} items. Memory before: {currentMemory / 1024 / 1024:F1}MB, after: {MemoryPressureMonitor.GetCurrentMemoryUsage() / 1024 / 1024:F1}MB");
            }
            if (Cache.Count <= 15) return;
            keysToRemove = Cache.OrderBy(kvp => kvp.Value.Value.LastAccessed)
                .Take(Cache.Count - 15)
                .Select(kvp => kvp.Key)
                .ToList();
            foreach (var key in keysToRemove)
            {
                RemoveFromCacheInternal(key);
            }
            Logger.Diagnose($"Cache size cleanup removed {keysToRemove.Count} items to maintain 15 items max");
        }
    }
    public static void PerformCleanup()
    {
        lock (LockObject)
        {
            var now = DateTime.Now;
            var keysToRemove =
                (from kvp in Cache where (now - kvp.Value.Value.LastAccessed) > TimeSpan.FromMinutes(5) select kvp.Key)
                .ToList();
            if (Cache.Count > MaxCacheSize * 0.7)
            {
                var targetCount = (int)(MaxCacheSize * 0.5); 
                var excessCount = Cache.Count - targetCount;
                var sortedByTime = Cache.OrderBy(kvp => kvp.Value.Value.LastAccessed).Take(excessCount);
                keysToRemove.AddRange(sortedByTime.Select(kvp => kvp.Key));
            }
            if (MemoryPressureMonitor.GetCurrentMemoryUsage() > 50 * 1024 * 1024)
            {
                var itemsToClean = Math.Min(5, Cache.Count); 
                var oldestItems = Cache.OrderBy(kvp => kvp.Value.Value.LastAccessed).Take(itemsToClean);
                keysToRemove.AddRange(oldestItems.Select(kvp => kvp.Key));
            }
            foreach (var key in keysToRemove.Distinct())
            {
                RemoveFromCacheInternal(key);
            }
            if (keysToRemove.Count > 0)
            {
                Logger.Diagnose($"Cleanup removed {keysToRemove.Count} expired cache items.");
            }
        }
    }
    private static bool IsValidImageData(byte[] data)
    {
        if (data.Length < 8)
        {
            return false;
        }
        if (data is [0xFF, 0xD8, ..])
        {
            return true;
        }
        switch (data.Length)
        {
            case >= 8 when
                data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47 &&
                data[4] == 0x0D && data[5] == 0x0A && data[6] == 0x1A && data[7] == 0x0A:
            case >= 4 when
                (data[0] == 0x47 && data[1] == 0x49 && data[2] == 0x46 && data[3] == 0x38) && 
                (data[4] == 0x37 || data[4] == 0x39) && 
                data[5] == 0x61:
            case >= 2 when data[0] == 0x42 && data[1] == 0x4D:
            case >= 12 when
                data[0] == 0x52 && data[1] == 0x49 && data[2] == 0x46 && data[3] == 0x46 && 
                data[8] == 0x57 && data[9] == 0x45 && data[10] == 0x42 && data[11] == 0x50:
                return true;
            default:
                return false;
        }
    }
    public static async Task<Image?> LoadImageAsync(string cacheKey, string imageUrl)
    {
        if (string.IsNullOrEmpty(imageUrl) || string.IsNullOrEmpty(cacheKey))
        {
            return null;
        }
        Image? cachedImage = null;
        lock (LockObject)
        {
            if (Cache.TryGetValue(cacheKey, out var node))
            {
                LruList.Remove(node);
                LruList.AddFirst(node);
                node.Value.LastAccessed = DateTime.Now;
                cachedImage = node.Value.Image;
            }
        }
        var shouldLog = false;
        lock (LoggedCacheKeys)
        {
            if (!LoggedCacheKeys.Contains(cacheKey))
            {
                shouldLog = true;
                LoggedCacheKeys.Add(cacheKey);
            }
        }
        if (shouldLog)
        {
            Logger.Diagnose($"ImageCacheManager received request for key: {cacheKey}");
        }
        if (cachedImage != null)
        {
            if (shouldLog)
            {
                Logger.Diagnose($"CACHE HIT for key: {cacheKey}");
            }
            return cachedImage;
        }
        Logger.Diagnose($"CACHE MISS for key: {cacheKey}. Downloading from URL.");
        const int maxRetries = 3;
        byte[]? downloadedBytes = null;
        for (var retry = 0; retry < maxRetries; retry++)
        {
            var retryDelay = 100 * (1 << retry);
            try
            {
                var httpClient = HttpClientManager.SharedClient;
                using var response = await httpClient.GetAsync(imageUrl);
                if (!response.IsSuccessStatusCode)
                {
                    Logger.Diagnose(
                        $"Image download failed for key {cacheKey}: HTTP Status {response.StatusCode} (Retry {retry + 1}/{maxRetries})");
                    return null;
                }
                var contentType = response.Content.Headers.ContentType?.MediaType;
                if (string.IsNullOrEmpty(contentType) || !contentType.StartsWith("image/"))
                {
                    Logger.Diagnose(
                        $"Downloaded content is not an image for key {cacheKey}. Content-Type: {contentType ?? "N/A"} (Retry {retry + 1}/{maxRetries})");
                    return null; 
                }
                await using (var httpStream = await response.Content.ReadAsStreamAsync())
                {
                    var contentLength = response.Content.Headers.ContentLength ?? 1024 * 1024; 
                    var buffer = ArrayPool<byte>.Shared.Rent((int)Math.Min(contentLength, 10 * 1024 * 1024)); 
                    try
                    {
                        var totalRead = 0;
                        int bytesRead;
                        while ((bytesRead =
                                   await httpStream.ReadAsync(buffer.AsMemory(totalRead, buffer.Length - totalRead))) >
                               0)
                        {
                            totalRead += bytesRead;
                            if (totalRead != buffer.Length) continue;
                            var newBuffer = ArrayPool<byte>.Shared.Rent(buffer.Length * 2);
                            Array.Copy(buffer, 0, newBuffer, 0, totalRead);
                            ArrayPool<byte>.Shared.Return(buffer);
                            buffer = newBuffer;
                        }
                        downloadedBytes = new byte[totalRead];
                        Array.Copy(buffer, 0, downloadedBytes, 0, totalRead);
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(buffer);
                    }
                }
                if (downloadedBytes is not { Length: > 0 })
                {
                    Logger.Diagnose(
                        $"Downloaded content is empty or invalid for key: {cacheKey}. (Retry {retry + 1}/{maxRetries})");
                    if (retry >= maxRetries - 1) return null;
                    await Task.Delay(retryDelay);
                    continue;
                }
                if (!IsValidImageData(downloadedBytes))
                {
                    var headerHex = downloadedBytes.Length >= 16
                        ? BitConverter.ToString(downloadedBytes, 0, 16).Replace("-", " ")
                        : BitConverter.ToString(downloadedBytes).Replace("-", " ");
                    Logger.Diagnose(
                        $"Downloaded content is not a valid image format for key: {cacheKey}. Data length: {downloadedBytes.Length}. Header: {headerHex}. (Retry {retry + 1}/{maxRetries})");
                    if (retry >= maxRetries - 1) return null;
                    await Task.Delay(retryDelay);
                    continue;
                }
                Image image;
                try
                {
                    var ms = new MemoryStream(downloadedBytes);
                    image = Image.FromStream(ms);
                    Logger.Diagnose($"Downloaded and created new image for key: {cacheKey}. Caching it.");
                }
                catch (ArgumentException ex)
                {
                    Logger.Diagnose(
                        $"ArgumentException during Image.FromStream for key {cacheKey}. Data length: {downloadedBytes.Length}. Error: {ex.Message}. (Retry {retry + 1}/{maxRetries})");
                    if (retry >= maxRetries - 1) return null;
                    await Task.Delay(retryDelay);
                    continue;
                }
                var imageSize = MemoryPressureMonitor.EstimateImageSize(image);
                lock (LockObject)
                {
                    if (MemoryPressureMonitor.ShouldProactivelyCleanup())
                    {
                        var cleanupCount = MemoryPressureMonitor.GetSuggestedCleanupCount();
                        var itemsToCleanup = Math.Min(cleanupCount, LruList.Count);
                        for (var i = 0; i < itemsToCleanup && LruList.Last != null; i++)
                        {
                            var lruNode = LruList.Last;
                            var evictedItem = lruNode.Value;
                            var evictedSize = MemoryPressureMonitor.EstimateImageSize(evictedItem.Image);
                            RemoveFromCacheInternal(evictedItem.Key);
                            MemoryPressureMonitor.UnregisterImageSize(evictedSize);
                            Logger.Diagnose(
                                $"Proactive cleanup evicted item with key: {evictedItem.Key}, freed: {evictedSize} bytes");
                        }
                    }
                    while (MemoryPressureMonitor.ShouldEvictItems(imageSize) && LruList.Last != null)
                    {
                        var lruNode = LruList.Last;
                        var evictedItem = lruNode.Value;
                        var evictedSize = MemoryPressureMonitor.EstimateImageSize(evictedItem.Image);
                        RemoveFromCacheInternal(evictedItem.Key);
                        MemoryPressureMonitor.UnregisterImageSize(evictedSize);
                        Logger.Diagnose(
                            $"Memory pressure evicted item with key: {evictedItem.Key}, freed: {evictedSize} bytes");
                    }
                    var newItem = new CacheItem(cacheKey, image);
                    var newNode = new LinkedListNode<CacheItem>(newItem);
                    LruList.AddFirst(newNode);
                    Cache[cacheKey] = newNode;
                    MemoryPressureMonitor.RegisterImageSize(imageSize);
                    Logger.Diagnose(
                        $"Cached new image with key: {cacheKey}, size: {imageSize} bytes, total cache memory: {MemoryPressureMonitor.GetCurrentMemoryUsage()} bytes");
                }
                return image;
            }
            catch (ArgumentException ex)
            {
                var dataLength = downloadedBytes?.Length.ToString() ?? "N/A";
                Logger.Diagnose(
                    $"ArgumentException during image creation for key {cacheKey}. Image data length: {dataLength}. Error: {ex.Message}. Attempt {retry + 1}/{maxRetries}.");
                if (retry < maxRetries - 1)
                {
                    await Task.Delay(retryDelay);
                }
            }
            catch (Exception ex)
            {
                var dataLength = downloadedBytes?.Length.ToString() ?? "N/A";
                Logger.Diagnose(
                    $"General Exception during image download/creation for key {cacheKey}. Image data length: {dataLength}. Error: {ex.Message}.");
                return null;
            }
        }
        Logger.Diagnose($"Failed to download and create image for key {cacheKey} after {maxRetries} attempts.");
        return null;
    }
    public static void ClearAllCache()
    {
        lock (LockObject)
        {
            var keysToRemove = Cache.Keys.ToList();
            foreach (var key in keysToRemove)
            {
                RemoveFromCacheInternal(key);
            }
            lock (LoggedCacheKeys)
            {
                LoggedCacheKeys.Clear();
            }
            Logger.Diagnose("[DIAGNOSE] All caches have been cleared.");
        }
    }
    public static void ForceCleanupCache(int keepCount = 10)
    {
        lock (LockObject)
        {
            if (Cache.Count <= keepCount) return;
            var itemsToRemove = Cache.Count - keepCount;
            var keysToRemove = new List<string>();
            var sortedByTime = Cache.OrderBy(kvp => kvp.Value.Value.LastAccessed).Take(itemsToRemove);
            keysToRemove.AddRange(sortedByTime.Select(kvp => kvp.Key));
            foreach (var key in keysToRemove)
            {
                RemoveFromCacheInternal(key);
            }
            Logger.Diagnose(
                $"Force cleanup removed {keysToRemove.Count} cache items, kept {keepCount} most recent items.");
        }
    }
    public static void RemoveFromCache(string cacheKey)
    {
        if (string.IsNullOrEmpty(cacheKey)) return;
        lock (LockObject)
        {
            RemoveFromCacheInternal(cacheKey);
        }
    }
    public static void RemoveCacheByPrefix(string prefix)
    {
        if (string.IsNullOrEmpty(prefix)) return;
        lock (LockObject)
        {
            var keysToRemove = Cache.Keys.Where(k => k.StartsWith(prefix)).ToList();
            foreach (var key in keysToRemove)
            {
                RemoveFromCacheInternal(key);
            }
            Logger.Diagnose($"Removed {keysToRemove.Count} cache items with prefix '{prefix}'.");
        }
    }
    private static void RemoveFromCacheInternal(string cacheKey)
    {
        if (!Cache.TryGetValue(cacheKey, out var node)) return;
        if (ActiveKeys.Contains(cacheKey))
        {
            return;
        }
        LruList.Remove(node);
        Cache.Remove(cacheKey);
        lock (LoggedCacheKeys)
        {
            LoggedCacheKeys.Remove(cacheKey);
        }
        var imageSize = MemoryPressureMonitor.EstimateImageSize(node.Value.Image);
        MemoryPressureMonitor.UnregisterImageSize(imageSize);
        try
        {
            node.Value.Image.Dispose();
        }
        catch (Exception ex)
        {
            Logger.Diagnose($"[WARNING] Failed to dispose image for key {cacheKey}: {ex.Message}");
        }
    }
}