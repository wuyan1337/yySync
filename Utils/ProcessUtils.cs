using System;
using System.Collections.Generic;
using System.Diagnostics;
namespace MusicRpc.Utils;
internal static class ProcessUtils
{
    private static readonly Dictionary<(int, string), nint> ModuleAddressCache = new();
    private static int _lastPid = -1;
    private static DateTime _lastCleanupTime = DateTime.Now;
    public static nint GetModuleBaseAddress(int pid, string moduleName)
    {
        if (pid != _lastPid)
        {
            ModuleAddressCache.Clear();
            _lastPid = pid;
        }
        var now = DateTime.Now;
        if ((now - _lastCleanupTime) > PerformanceConfig.ProcessModuleCacheCleanupInterval ||
            ModuleAddressCache.Count > PerformanceConfig.ProcessModuleCacheMaxSize)
        {
            CleanupCache();
            _lastCleanupTime = now;
        }
        var cacheKey = (pid, moduleName);
        if (ModuleAddressCache.TryGetValue(cacheKey, out var cachedAddress))
        {
            return cachedAddress;
        }
        try
        {
            using var process = Process.GetProcessById(pid);
            foreach (ProcessModule module in process.Modules)
            {
                if (!module.ModuleName.Equals(moduleName, StringComparison.OrdinalIgnoreCase)) continue;
                var baseAddress = module.BaseAddress;
                ModuleAddressCache[cacheKey] = baseAddress;
                return baseAddress;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ERROR] Failed to get modules for PID {pid}: {ex.Message}");
        }
        ModuleAddressCache[cacheKey] = IntPtr.Zero;
        return IntPtr.Zero;
    }
    private static void CleanupCache()
    {
        if (ModuleAddressCache.Count <= PerformanceConfig.ProcessModuleCacheMaxSize / 2)
            return;
        var keysToRemove = (int)(ModuleAddressCache.Count * 0.7);
        var cacheList = new List<(int, string)>(ModuleAddressCache.Keys);
        cacheList.Sort((a, b) => a.Item1.CompareTo(b.Item1));
        for (var i = 0; i < keysToRemove && i < cacheList.Count; i++)
        {
            ModuleAddressCache.Remove(cacheList[i]);
        }
        Debug.WriteLine(
            $"[DIAGNOSE] Process module cache cleanup: removed {keysToRemove} items, {ModuleAddressCache.Count} items remaining");
    }
}