using System;
using System.Diagnostics;
using MusicRpc.Win32Api;
namespace MusicRpc.Utils;
internal static class PerformanceMonitor
{
    private static readonly Process CurrentProcess = Process.GetCurrentProcess();
    public static ProcessMemoryInfo GetMemoryInfo()
    {
        try
        {
            var workingSet = CurrentProcess.WorkingSet64;
            var privateMemory = CurrentProcess.PrivateMemorySize64;
            var virtualMemory = CurrentProcess.VirtualMemorySize64;
            var gcMemory = GC.GetTotalMemory(false);
            return new ProcessMemoryInfo
            {
                WorkingSetSize = workingSet,
                PrivateMemorySize = privateMemory,
                VirtualMemorySize = virtualMemory,
                GcMemorySize = gcMemory,
                Timestamp = DateTime.Now
            };
        }
        catch
        {
            return new ProcessMemoryInfo();
        }
    }
    public static CacheStatistics GetCacheStatistics()
    {
        return new CacheStatistics
        {
            ImageCacheCount = GetImageCacheCount(),
            ModuleCacheCount = GetModuleCacheCount(),
            ProcessModuleCacheCount = GetProcessModuleCacheCount()
        };
    }
    private static int GetImageCacheCount()
    {
        try
        {
            var imageCacheType = typeof(ImageCacheManager);
            var cacheField = imageCacheType.GetField("Cache", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            if (cacheField?.GetValue(null) is System.Collections.IDictionary cacheDict)
            {
                return cacheDict.Count;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ERROR] Failed to get image cache count: {ex.Message}");
        }
        return 0;
    }
    private static int GetModuleCacheCount()
    {
        try
        {
            var memoryType = typeof(Memory);
            var moduleCacheField = memoryType.GetField("ModuleCache", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            if (moduleCacheField?.GetValue(null) is System.Collections.IDictionary cacheDict)
            {
                return cacheDict.Count;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ERROR] Failed to get module cache count: {ex.Message}");
        }
        return 0;
    }
    private static int GetProcessModuleCacheCount()
    {
        try
        {
            var processUtilsType = typeof(ProcessUtils);
            var moduleAddressCacheField = processUtilsType.GetField("ModuleAddressCache", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            if (moduleAddressCacheField?.GetValue(null) is System.Collections.IDictionary cacheDict)
            {
                return cacheDict.Count;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ERROR] Failed to get process module cache count: {ex.Message}");
        }
        return 0;
    }
    public static ProcessMemoryInfo ForceGcAndGetMemoryInfo()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        return GetMemoryInfo();
    }
}
internal record ProcessMemoryInfo
{
    public long WorkingSetSize { get; init; }
    public long PrivateMemorySize { get; init; }
    public long VirtualMemorySize { get; init; }
    public long GcMemorySize { get; init; }
    public DateTime Timestamp { get; init; }
    public string GetFormattedWorkingSet() => FormatBytes(WorkingSetSize);
    public string GetFormattedPrivateMemory() => FormatBytes(PrivateMemorySize);
    public string GetFormattedVirtualMemory() => FormatBytes(VirtualMemorySize);
    public string GetFormattedGcMemory() => FormatBytes(GcMemorySize);
    private static string FormatBytes(long bytes)
    {
        string[] suffixes = ["B", "KB", "MB", "GB", "TB"];
        var counter = 0;
        decimal number = bytes;
        while (Math.Round(number / 1024) >= 1)
        {
            number /= 1024;
            counter++;
        }
        return $"{number:n1} {suffixes[counter]}";
    }
}
internal record CacheStatistics
{
    public int ImageCacheCount { get; init; }
    public int ModuleCacheCount { get; init; }
    public int ProcessModuleCacheCount { get; init; }
    public int TotalCacheCount => ImageCacheCount + ModuleCacheCount + ProcessModuleCacheCount;
}