using System;
using System.Diagnostics;
using Microsoft.Win32;
namespace MusicRpc.Win32Api;
internal static class AutoStart
{
    private const string AppValueName = "MusicRpc";
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    internal static bool Set(bool enable)
    {
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath))
        {
            Debug.WriteLine("[ERROR] Could not determine the application's executable path.");
            return false;
        }
        var quotedPath = $"\"{exePath}\"";
        using var runKey = OpenRunKey(true);
        if (runKey is null) return false;
        try
        {
            if (enable)
            {
                var currentVal = runKey.GetValue(AppValueName) as string;
                if (currentVal != quotedPath)
                {
                    runKey.SetValue(AppValueName, quotedPath);
                }
            }
            else
            {
                if (runKey.GetValue(AppValueName) is not null)
                {
                    runKey.DeleteValue(AppValueName, false);
                }
            }
            return true;
        }
        catch (Exception e)
        {
            Debug.WriteLine($"[ERROR] Failed to set auto-start value: {e.Message}");
            return false;
        }
    }
    public static bool Check()
    {
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath)) return false;
        using var runKey = OpenRunKey(false);
        if (runKey is null) return false;
        try
        {
            var storedPath = runKey.GetValue(AppValueName) as string;
            if (string.IsNullOrEmpty(storedPath)) return false;
            var normalizedStoredPath = storedPath.Trim('\"');
            return exePath.Equals(normalizedStoredPath, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception e)
        {
            Debug.WriteLine($"[ERROR] Failed to check auto-start value: {e.Message}");
            return false;
        }
    }
    private static RegistryKey? OpenRunKey(bool writable)
    {
        try
        {
            return Registry.CurrentUser.OpenSubKey(RunKeyPath, writable);
        }
        catch (Exception e)
        {
            Debug.WriteLine($"[ERROR] Failed to open registry key '{RunKeyPath}': {e.Message}");
            return null;
        }
    }
}