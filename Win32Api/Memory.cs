using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using MusicRpc.Utils;
namespace MusicRpc.Win32Api;
internal static class Memory
{
    private static readonly Dictionary<(int, nint), (nint pStart, byte[] memory)> ModuleCache = new();
    private static int _lastProcessId = -1;
    private static DateTime _lastCleanupTime = DateTime.Now;
    public static bool FindPattern(string pattern, int processId, nint moduleBaseAddress, out nint pointer)
    {
        if (processId != _lastProcessId)
        {
            ModuleCache.Clear();
            _lastProcessId = processId;
        }
        var now = DateTime.Now;
        if ((now - _lastCleanupTime) > PerformanceConfig.ModuleCacheCleanupInterval ||
            ModuleCache.Count > PerformanceConfig.ModuleCacheMaxSize)
        {
            CleanupCache();
            _lastCleanupTime = now;
        }
        if (!ModuleCache.TryGetValue((processId, moduleBaseAddress), out (nint pStart, byte[] memoryBlock) cacheEntry))
        {
            using var memory = new ProcessMemory(processId);
            try
            {
                var ntOffset = memory.ReadInt32(moduleBaseAddress, 0x3C);
                var ntHeader = moduleBaseAddress + ntOffset;
                var fileHeader = ntHeader + 4;
                var optHeader = fileHeader + 20;
                var sectionSize = memory.ReadInt16(fileHeader, 16);
                var sections = memory.ReadInt16(ntHeader, 6);
                var sectionHeader = optHeader + sectionSize;
                var cursor = sectionHeader;
                for (var i = 0; i < sections; i++)
                {
                    if (memory.ReadInt64(cursor) == 0x747865742E)
                    {
                        var pOffset = memory.ReadInt32(cursor, 12);
                        var pSize = memory.ReadInt32(cursor, 8);
                        cacheEntry.pStart = moduleBaseAddress + pOffset;
                        cacheEntry.memoryBlock = memory.ReadBytes(cacheEntry.pStart, pSize);
                        ModuleCache[(processId, moduleBaseAddress)] = cacheEntry;
                        break;
                    }
                    cursor += 40;
                }
            }
            catch (Exception)
            {
                pointer = nint.Zero;
                return false;
            }
        }
        if (cacheEntry.memoryBlock is null)
        {
            pointer = nint.Zero;
            return false;
        }
        pointer = FindPattern(pattern, cacheEntry.pStart, cacheEntry.memoryBlock);
        return pointer != nint.Zero;
    }
    private static nint FindPattern(string pattern, nint pStart, byte[] memoryBlock)
    {
        if (string.IsNullOrEmpty(pattern) || pStart == nint.Zero || memoryBlock.Length == 0)
        {
            return nint.Zero;
        }
        var patternBytes = ParseSignature(pattern);
        var firstByte = patternBytes[0];
        var searchRange = memoryBlock.Length - patternBytes.Length;
        for (var i = 0; i < searchRange; i++)
        {
            if (firstByte != 0xFFFF)
            {
                i = Array.IndexOf(memoryBlock, (byte)firstByte, i);
                if (i == -1)
                {
                    break;
                }
            }
            var found = true;
            for (var j = 1; j < patternBytes.Length; j++)
            {
                if (patternBytes[j] == 0xFFFF || patternBytes[j] == memoryBlock[i + j]) continue;
                found = false;
                break;
            }
            if (found)
            {
                return nint.Add(pStart, i);
            }
        }
        return nint.Zero;
    }
    private static ushort[] ParseSignature(string signature)
    {
        var bytesStr = signature.Split(' ');
        var bytes = new ushort[bytesStr.Length];
        for (var i = 0; i < bytes.Length; i++)
        {
            var str = bytesStr[i];
            if (str.Contains('?'))
            {
                bytes[i] = 0xFFFF;
            }
            else
            {
                bytes[i] = Convert.ToByte(str, 16);
            }
        }
        return bytes;
    }
    private static void CleanupCache()
    {
        if (ModuleCache.Count <= PerformanceConfig.ModuleCacheMaxSize / 2)
            return;
        var keysToRemove = (int)(ModuleCache.Count * 0.75);
        var cacheList = new List<(int, nint)>(ModuleCache.Keys);
        cacheList.Sort((a, b) => a.Item1.CompareTo(b.Item1));
        for (var i = 0; i < keysToRemove && i < cacheList.Count; i++)
        {
            ModuleCache.Remove(cacheList[i]);
        }
        Debug.WriteLine(
            $"[DIAGNOSE] Memory module cache cleanup: removed {keysToRemove} items, {ModuleCache.Count} items remaining");
    }
}
internal sealed partial class ProcessMemory(nint process) : IDisposable
{
    private bool _disposed;
    public int ProcessId { get; }
    public ProcessMemory(int processId) : this(OpenProcess(
        0x0010,
        false, 
        processId))
    {
        ProcessId = processId;
    }
    public void Dispose()
    {
        if (_disposed) return;
        if (process != IntPtr.Zero)
        {
            CloseHandle(process);
        }
        _disposed = true;
    }
    public byte[] ReadBytes(IntPtr offset, int length)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(length);
        try
        {
            ReadProcessMemory(process, offset, buffer, length, IntPtr.Zero);
            var result = new byte[length];
            Array.Copy(buffer, 0, result, 0, length);
            return result;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
    public float ReadFloat(IntPtr address, int offset = 0)
        => BitConverter.ToSingle(ReadBytes(IntPtr.Add(address, offset), 4), 0);
    public double ReadDouble(IntPtr address, int offset = 0)
        => BitConverter.ToDouble(ReadBytes(IntPtr.Add(address, offset), 8), 0);
    public long ReadInt64(IntPtr address, int offset = 0)
        => BitConverter.ToInt64(ReadBytes(IntPtr.Add(address, offset), 8), 0);
    public ulong ReadUInt64(IntPtr address, int offset = 0)
        => BitConverter.ToUInt64(ReadBytes(IntPtr.Add(address, offset), 8), 0);
    public short ReadInt16(IntPtr address, int offset = 0)
        => BitConverter.ToInt16(ReadBytes(IntPtr.Add(address, offset), 2), 0);
    public int ReadInt32(IntPtr address, int offset = 0)
        => BitConverter.ToInt32(ReadBytes(IntPtr.Add(address, offset), 4), 0);
    public uint ReadUInt32(IntPtr address, int offset = 0)
        => BitConverter.ToUInt32(ReadBytes(IntPtr.Add(address, offset), 4), 0);
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ReadProcessMemory(
        IntPtr hProcess,
        IntPtr lpBaseAddress,
        [Out] byte[] lpBuffer,
        int nSize,
        IntPtr lpNumberOfBytesRead);
    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial IntPtr OpenProcess(
        int dwDesiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle,
        int dwProcessId);
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(IntPtr hObject);
}