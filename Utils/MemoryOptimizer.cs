using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
namespace MusicRpc.Utils;
internal static partial class MemoryOptimizer
{
    [LibraryImport("psapi.dll")]
    private static partial int EmptyWorkingSet(IntPtr hProcess);
    [LibraryImport("kernel32.dll")]
    private static partial IntPtr GetCurrentProcess();
    public static async Task PerformAggressiveOptimizationAsync()
    {
        if (!IsAppInBackground())
        {
            Logger.Memory("App is in foreground or active, skipping optimization to ensure UI smoothness.");
            return;
        }
        try
        {
            await Task.Run(async () =>
            {
                ObjectMemoryAnalyzer.LogMemoryAnalysis();
                Logger.Memory("App is in background. Starting GC collection...");
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true);
                GC.WaitForPendingFinalizers();
                if (GC.GetTotalMemory(false) > 40 * 1024 * 1024)
                {
                    await Task.Delay(100);
                    GC.Collect();
                }
                if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                {
                    var hProcess = GetCurrentProcess();
                    if (EmptyWorkingSet(hProcess) == 0)
                    {
                        Logger.Memory("EmptyWorkingSet returned 0.");
                    }
                    if (!SetProcessWorkingSetSize(hProcess, new IntPtr(-1), new IntPtr(-1)))
                    {
                        Logger.Memory("SetProcessWorkingSetSize failed.");
                    }
                }
                ClearTemporaryResources();
                Logger.Memory("Background aggressive optimization completed.");
            });
        }
        catch (Exception ex)
        {
            Logger.Error($"Memory optimization failed: {ex.Message}");
        }
    }
    private static bool IsAppInBackground()
    {
        try
        {
            var openForms = Application.OpenForms.Cast<Form>().ToList();
            if (openForms.Count == 0 || openForms.All(f => !f.Visible))
            {
                return true;
            }
            var mainForm = openForms.OfType<MainForm>().FirstOrDefault();
            if (mainForm != null && (mainForm.WindowState == FormWindowState.Minimized || !mainForm.Visible))
            {
                return !openForms.Any(f => f is SettingsForm || f.Modal);
            }
            return false;
        }
        catch
        {
            return false;
        }
    }
    private static void ClearTemporaryResources()
    {
        try
        {
            ImageCacheManager.ForceCleanupCache(3);
            MemoryPressureMonitor.UnregisterImageSize(0);
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to clear temporary resources: {ex.Message}");
        }
    }
    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetProcessWorkingSetSize(IntPtr hProcess, IntPtr dwMinimumWorkingSetSize,
        IntPtr dwMaximumWorkingSetSize);
}