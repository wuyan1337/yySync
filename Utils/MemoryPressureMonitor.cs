using System;
using System.Threading;
namespace MusicRpc.Utils;
internal static class MemoryPressureMonitor
{
    private const long MaxMemoryBytes = 40 * 1024 * 1024; 
    private static long _currentCacheMemory;
    private static readonly Lock LockObject = new();
    public static bool ShouldEvictItems(long newImageSize)
    {
        lock (LockObject)
        {
            return _currentCacheMemory + newImageSize > MaxMemoryBytes;
        }
    }
    public static bool ShouldProactivelyCleanup()
    {
        lock (LockObject)
        {
            return _currentCacheMemory > 25 * 1024 * 1024;
        }
    }
    public static int GetSuggestedCleanupCount()
    {
        lock (LockObject)
        {
            var memoryUsage = _currentCacheMemory;
            return memoryUsage switch
            {
                > 35 * 1024 * 1024 => 15,
                > 30 * 1024 * 1024 => 10,
                > 25 * 1024 * 1024 => 5,
                _ => 3
            };
        }
    }
    public static void RegisterImageSize(long size)
    {
        lock (LockObject)
        {
            _currentCacheMemory += size;
        }
    }
    public static void UnregisterImageSize(long size)
    {
        lock (LockObject)
        {
            _currentCacheMemory = Math.Max(0, _currentCacheMemory - size);
        }
    }
    public static long GetCurrentMemoryUsage()
    {
        lock (LockObject)
        {
            return _currentCacheMemory;
        }
    }
    public static long EstimateImageSize(System.Drawing.Image image)
    {
        var baseSize = (long)image.Width * image.Height * 4;
        return (long)(baseSize * 1.2);
    }
}