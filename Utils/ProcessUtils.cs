using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace MusicRpc.Utils;

internal static partial class ProcessUtils
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

        // 1. Try standard .NET Process.Modules (fastest, but fails on some permission/bitness scenarios)
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
        catch
        {
            // Ignore errors here, fallback to Win32 API
        }

        // 2. Fallback to EnumProcessModulesEx (slower, but works across 32/64 bit boundaries)
        var fallbackHandle = OpenProcess(PROCESS_QUERY_INFORMATION | PROCESS_VM_READ, false, pid);
        if (fallbackHandle == IntPtr.Zero)
        {
             ModuleAddressCache[cacheKey] = IntPtr.Zero;
             return IntPtr.Zero;
        }

        try
        {
            var modules = new IntPtr[1024];
            if (EnumProcessModulesEx(fallbackHandle, modules, (uint)(modules.Length * IntPtr.Size), out var cbNeeded, LIST_MODULES_ALL))
            {
                var count = Math.Min(modules.Length, (int)(cbNeeded / IntPtr.Size));
                var buffer = new char[1024];
                for (var i = 0; i < count; i++)
                {
                    var length = GetModuleBaseName(fallbackHandle, modules[i], buffer, (uint)buffer.Length);
                    if (length > 0)
                    {
                        var foundModuleName = new string(buffer, 0, (int)length);
                        if (foundModuleName.Equals(moduleName, StringComparison.OrdinalIgnoreCase))
                        {
                            if (GetModuleInformation(fallbackHandle, modules[i], out var moduleInfo, (uint)Marshal.SizeOf<MODULEINFO>()))
                            {
                                var baseAddress = moduleInfo.lpBaseOfDll;
                                ModuleAddressCache[cacheKey] = baseAddress;
                                return baseAddress;
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
             Debug.WriteLine($"[ERROR] Failed to get modules via Win32 for PID {pid}: {ex.Message}");
        }
        finally
        {
             CloseHandle(fallbackHandle);
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

    #region Win32 API
    private const uint LIST_MODULES_ALL = 0x03;
    private const int PROCESS_QUERY_INFORMATION = 0x0400;
    private const int PROCESS_VM_READ = 0x0010;

    [LibraryImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EnumProcessModulesEx(IntPtr hProcess, [Out] IntPtr[] lphModule, uint cb, out uint lpcbNeeded, uint dwFilterFlag);

    [LibraryImport("psapi.dll", EntryPoint = "GetModuleBaseNameW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint GetModuleBaseName(IntPtr hProcess, IntPtr hModule, [Out] char[] lpBaseName, uint nSize);

    [LibraryImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetModuleInformation(IntPtr hProcess, IntPtr hModule, out MODULEINFO lpmodinfo, uint cb);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial IntPtr OpenProcess(int dwDesiredAccess, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, int dwProcessId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(IntPtr hObject);

    [StructLayout(LayoutKind.Sequential)]
    private struct MODULEINFO
    {
        public IntPtr lpBaseOfDll;
        public uint SizeOfImage;
        public IntPtr EntryPoint;
    }
    #endregion
}