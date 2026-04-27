using System;
using System.Diagnostics;
using Microsoft.Win32;
namespace MusicRpc.Win32Api;
internal static class AutoStart
{
    private const string AppValueName = "yySync";
    private const string LegacyAppValueName = "MusicRpc";
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
                SetValueIfDifferent(runKey, AppValueName, quotedPath);
                DeleteLegacyValueIfOwnedByCurrentExecutable(runKey, exePath);
            }
            else
            {
                DeleteValueIfExists(runKey, AppValueName);
                DeleteLegacyValueIfOwnedByCurrentExecutable(runKey, exePath);
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
            if (ValueMatchesExecutable(runKey, AppValueName, exePath))
            {
                return true;
            }
            if (!ValueMatchesExecutable(runKey, LegacyAppValueName, exePath))
            {
                return false;
            }
            TryMigrateLegacyValue(exePath);
            return true;
        }
        catch (Exception e)
        {
            Debug.WriteLine($"[ERROR] Failed to check auto-start value: {e.Message}");
            return false;
        }
    }
    internal static void MigrateLegacyRegistration()
    {
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath)) return;
        TryMigrateLegacyValue(exePath);
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
    private static void TryMigrateLegacyValue(string exePath)
    {
        using var runKey = OpenRunKey(true);
        if (runKey is null) return;
        try
        {
            if (!ValueMatchesExecutable(runKey, LegacyAppValueName, exePath))
            {
                return;
            }
            SetValueIfDifferent(runKey, AppValueName, $"\"{exePath}\"");
            DeleteValueIfExists(runKey, LegacyAppValueName);
        }
        catch (Exception e)
        {
            Debug.WriteLine($"[ERROR] Failed to migrate legacy auto-start value: {e.Message}");
        }
    }
    private static bool ValueMatchesExecutable(RegistryKey runKey, string valueName, string exePath)
    {
        var storedPath = runKey.GetValue(valueName) as string;
        if (string.IsNullOrWhiteSpace(storedPath))
        {
            return false;
        }
        var normalizedStoredPath = storedPath.Trim().Trim('\"');
        return exePath.Equals(normalizedStoredPath, StringComparison.OrdinalIgnoreCase);
    }
    private static void SetValueIfDifferent(RegistryKey runKey, string valueName, string value)
    {
        var currentValue = runKey.GetValue(valueName) as string;
        if (!string.Equals(currentValue, value, StringComparison.Ordinal))
        {
            runKey.SetValue(valueName, value);
        }
    }
    private static void DeleteLegacyValueIfOwnedByCurrentExecutable(RegistryKey runKey, string exePath)
    {
        if (ValueMatchesExecutable(runKey, LegacyAppValueName, exePath))
        {
            DeleteValueIfExists(runKey, LegacyAppValueName);
        }
    }
    private static void DeleteValueIfExists(RegistryKey runKey, string valueName)
    {
        if (runKey.GetValue(valueName) is not null)
        {
            runKey.DeleteValue(valueName, false);
        }
    }
}
