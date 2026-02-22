using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MusicRpc.Utils;
namespace MusicRpc;
internal static class Program
{
    private static RpcManager? _rpcManager;
    private static SteamStatusManager? _steamManager;
    private static SteamSessionManager? _sessionManager;
    private static MainForm? _mainForm;
    private static NotifyIcon? TrayIcon { get; set; }
    public static RpcManager? GetRpcManager() => _rpcManager;
    public static SteamStatusManager? GetSteamManager() => _steamManager;
    public static SteamSessionManager? GetSessionManager() => _sessionManager;
    [STAThread]
    private static void Main()
    {
        MemoryProfiler.LogMemorySnapshot("程序启动前");
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        using var mutex = new Mutex(true, "yySyncMutex", out var isNewInstance);
        if (!isNewInstance)
        {
            MessageBox.Show("yySync is already running.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        using var cts = new CancellationTokenSource();
        var token = cts.Token;
        _sessionManager = new SteamSessionManager();
        _sessionManager.Start();
        var config = Configurations.Instance;
        var loggedIn = false;
        if (config.Settings.EnableSteamSync)
        {
            if (!string.IsNullOrEmpty(config.Settings.SteamUsername) &&
                !string.IsNullOrEmpty(config.Settings.SteamRefreshToken))
            {
                try
                {
                    var loginTask = _sessionManager.LoginWithTokenAsync(
                        config.Settings.SteamUsername,
                        config.Settings.SteamRefreshToken);
                    loginTask.Wait(15000);
                    loggedIn = loginTask.IsCompletedSuccessfully && loginTask.Result;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Program] Token 登录异常: {ex.Message}");
                }
                if (loggedIn)
                    Debug.WriteLine("[Program] Token 自动登录成功");
                else
                    Debug.WriteLine("[Program] Token 登录失败，弹出登录窗口");
            }
            if (!loggedIn)
            {
                using var loginForm = new SteamLoginForm(_sessionManager);
                loginForm.ShowDialog();
                loggedIn = loginForm.LoginSucceeded;
            }
            if (loggedIn)
            {
                Debug.WriteLine("[Program] Steam 登录成功，开始同步音乐状态");
            }
            else
            {
                MessageBox.Show(
                    "未登录 Steam，音乐状态将不会同步到好友列表。\n\n" +
                    "你可以在设置中重新登录。",
                    "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
        _steamManager = new SteamStatusManager(_sessionManager);
        _rpcManager = new RpcManager(_steamManager);
        Task.Run(_rpcManager.Start, token);
        Task.Run(() => GlobalMemoryMonitor(token), token);
        Task.Run(async () =>
        {
            try
            {
                await Task.Delay(5000, token);
                await MemoryOptimizer.PerformAggressiveOptimizationAsync();
            }
            catch (OperationCanceledException) { }
        }, token);
        _mainForm = new MainForm();
        TrayIcon = CreateTrayIcon();
        TrayIcon.Visible = true;
        MemoryProfiler.ForceGcAndLog();
        if (!Configurations.Instance.Settings.StartInTray)
            _mainForm.Show();
        else
            ShowMinimizeToTrayNotification();
        Application.Run();
        cts.Cancel();
        _steamManager.ClearStatus();
        _sessionManager.Dispose();
    }
    private static NotifyIcon CreateTrayIcon()
    {
        var showSettingsItem = new ToolStripMenuItem("显示设置");
        var showMainWindowItem = new ToolStripMenuItem("显示主窗口");
        var exitMenuItem = new ToolStripMenuItem("退出");
        var contextMenu = new ContextMenuStrip();
        contextMenu.Items.AddRange(
            showMainWindowItem, showSettingsItem, new ToolStripSeparator(),
            exitMenuItem);
        showSettingsItem.Click += (_, _) =>
        {
            using var settingsForm = new SettingsForm();
            settingsForm.StartPosition = FormStartPosition.CenterScreen;
            settingsForm.ShowDialog();
        };
        showMainWindowItem.Click += (_, _) =>
        {
            if (_mainForm == null) return;
            _mainForm.Show();
            _mainForm.WindowState = FormWindowState.Normal;
            _mainForm.Activate();
        };
        exitMenuItem.Click += (_, _) => Application.Exit();
        var notifyIcon = new NotifyIcon
        {
            Icon = AppResource.Icon,
            Text = "yySync",
            ContextMenuStrip = contextMenu
        };
        notifyIcon.DoubleClick += (_, _) =>
        {
            if (_mainForm == null) return;
            _mainForm.Show();
            _mainForm.WindowState = FormWindowState.Normal;
            _mainForm.Activate();
        };
        return notifyIcon;
    }
    private static async Task GlobalMemoryMonitor(CancellationToken token)
    {
        var lastCleanupTime = DateTime.MinValue;
        var lastImageCleanupTime = DateTime.MinValue;
        while (!token.IsCancellationRequested)
        {
            try
            {
                var now = DateTime.Now;
                if ((now - lastImageCleanupTime).TotalMinutes >= 2)
                {
                    ImageCacheManager.PerformCleanup();
                    lastImageCleanupTime = now;
                }
                using var process = Process.GetCurrentProcess();
                var workingSet = process.WorkingSet64;
                var privateMemory = process.PrivateMemorySize64;
                switch (workingSet)
                {
                    case > 50 * 1024 * 1024:
                    {
                        Logger.Memory(
                            $"High memory usage detected - Working Set: {workingSet / 1024 / 1024:F1}MB, Private: {privateMemory / 1024 / 1024:F1}MB");
                        ImageCacheManager.PerformAggressiveCleanup();
                        await MemoryOptimizer.PerformAggressiveOptimizationAsync();
                        lastCleanupTime = now;
                        break;
                    }
                    case > 30 * 1024 * 1024 when (now - lastCleanupTime).TotalSeconds >= 30:
                        ImageCacheManager.PerformAggressiveCleanup();
                        lastCleanupTime = now;
                        break;
                }
                await Task.Delay(TimeSpan.FromSeconds(10), token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Logger.Error($"Memory monitor error: {ex.Message}");
                try { await Task.Delay(TimeSpan.FromSeconds(30), token); }
                catch (OperationCanceledException) { break; }
            }
        }
    }
    public static void ShowMinimizeToTrayNotification()
    {
        TrayIcon?.ShowBalloonTip(1000, "应用仍在运行", "yySync 已最小化到托盘区域。", ToolTipIcon.Info);
    }
}