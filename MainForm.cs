using System;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using MusicRpc.Models;
using MusicRpc.Utils;
namespace MusicRpc;
internal class MainForm : Form
{
    private readonly Timer _updateTimer;
    private readonly Panel[] _playerPanels = new Panel[3];
    private readonly PictureBox[] _coverPictureBoxes = new PictureBox[3];
    private readonly Label[] _songTitleLabels = new Label[3];
    private readonly Label[] _artistLabels = new Label[3];
    private readonly Label[] _albumLabels = new Label[3];
    private readonly Label[] _statusLabels = new Label[3];
    private readonly ProgressBar[] _progressBars = new ProgressBar[3];
    private readonly Label[] _progressLabels = new Label[3];
    private readonly Label[] _playerNameLabels = new Label[3];
    private Label _lastUpdateLabel = null!;
    private Button _settingsButton = null!;
    private readonly string[] _playerNames = ["网易云音乐", "QQ音乐", "洛雪音乐"];
    private readonly Color[] _playerColors =
    [
        Color.FromArgb(241, 98, 70),
        Color.FromArgb(217, 215, 23),
        Color.FromArgb(96, 213, 105)
    ];
    private readonly string[] _currentSongIds = new string[3];
    private readonly string[] _currentCoverUrls = ["", "", ""];
    private readonly string[] _currentCacheKeys = ["", "", ""];
    private readonly PlayerInfo?[] _lastPlayerInfos = new PlayerInfo?[3];
    private readonly bool[] _lastActiveStates = new bool[3];
    public MainForm()
    {
        MemoryProfiler.LogMemorySnapshot("MainForm构造函数开始");
        InitializeComponent();
        MemoryProfiler.LogMemorySnapshot("InitializeComponent后");
        SetupForm();
        MemoryProfiler.LogMemorySnapshot("SetupForm后");
        _updateTimer = new Timer { Interval = 1000 }; 
        _updateTimer.Tick += UpdateTimer_Tick;
        KeyPreview = true;
        KeyDown += MainForm_KeyDown;
        MemoryProfiler.LogMemorySnapshot("MainForm构造函数完成");
        MemoryProfiler.ForceGcAndLog();
        ObjectMemoryAnalyzer.LogMemoryAnalysis();
    }
    private void InitializeComponent()
    {
        for (var i = 0; i < 3; i++)
        {
            CreatePlayerPanel(i);
        }
        _lastUpdateLabel = new Label
        {
            AutoSize = true,
            Font = new Font("Microsoft YaHei", 8),
            ForeColor = Color.Gray,
            Location = new Point(10, 490),
            Size = new Size(150, 13),
            Text = "最后更新: --:--:--"
        };
        _settingsButton = new Button
        {
            Text = "设置",
            Size = new Size(75, 30),
            Location = new Point(516, 485),
            BackColor = Color.White,
            ForeColor = Color.Black,
            Font = new Font("Microsoft YaHei", 9),
            FlatStyle = FlatStyle.Flat,
            FlatAppearance = { BorderSize = 1 }
        };
        _settingsButton.Click += SettingsButton_Click;
        Controls.AddRange([_lastUpdateLabel, _settingsButton]);
    }
    private void CreatePlayerPanel(int index)
    {
        var yOffset = index * 160;
        var playerColor = _playerColors[index];
        var panel = new Panel
        {
            Size = new Size(580, 155),
            Location = new Point(10, yOffset),
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.White
        };
        var coverPictureBox = new PictureBox
        {
            Size = new Size(80, 80),
            Location = new Point(10, 15),
            SizeMode = PictureBoxSizeMode.StretchImage,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.LightGray
        };
        var playerNameLabel = new Label
        {
            AutoSize = true,
            Font = new Font("Microsoft YaHei", 10, FontStyle.Bold),
            ForeColor = playerColor,
            Location = new Point(100, 15),
            Size = new Size(100, 15),
            Text = _playerNames[index]
        };
        var textFlowPanel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            Location = new Point(100, 35),
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        var songTitleLabel = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(240, 0),
            Font = new Font("Microsoft YaHei", 11, FontStyle.Bold),
            ForeColor = Color.Black,
            Text = "暂无播放",
            Padding = new Padding(0, 0, 0, 5),
            Margin = new Padding(0)
        };
        var artistLabel = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(240, 0),
            Font = new Font("Microsoft YaHei", 9),
            ForeColor = Color.DarkGray,
            Text = "",
            Padding = new Padding(0, 0, 0, 5),
            Margin = new Padding(0)
        };
        var albumLabel = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(240, 0),
            Font = new Font("Microsoft YaHei", 9),
            ForeColor = Color.DarkGray,
            Text = "",
            Margin = new Padding(0)
        };
        textFlowPanel.Controls.AddRange(songTitleLabel, artistLabel, albumLabel);
        var statusLabel = new Label
        {
            AutoSize = true,
            Font = new Font("Microsoft YaHei", 9, FontStyle.Bold),
            ForeColor = Color.Gray,
            Location = new Point(350, 15),
            Size = new Size(80, 14),
            Text = "未运行"
        };
        var progressBar = new ProgressBar
        {
            Location = new Point(350, 40),
            Size = new Size(200, 18),
            Style = ProgressBarStyle.Continuous,
            Value = 0
        };
        var progressLabel = new Label
        {
            AutoSize = true,
            Font = new Font("Microsoft YaHei", 8),
            ForeColor = Color.DarkGray,
            Location = new Point(350, 65),
            Size = new Size(100, 12),
            Text = "00:00 / 00:00"
        };
        panel.Controls.AddRange(coverPictureBox, playerNameLabel, textFlowPanel, statusLabel, progressBar,
            progressLabel);
        _playerPanels[index] = panel;
        _coverPictureBoxes[index] = coverPictureBox;
        _playerNameLabels[index] = playerNameLabel;
        _songTitleLabels[index] = songTitleLabel;
        _artistLabels[index] = artistLabel;
        _albumLabels[index] = albumLabel;
        _statusLabels[index] = statusLabel;
        _progressBars[index] = progressBar;
        _progressLabels[index] = progressLabel;
        Controls.Add(panel);
    }
    private void SetupForm()
    {
        Text = "yySync - 播放器状态";
        Size = new Size(620, 570);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        MinimizeBox = true;
        ShowInTaskbar = true;
        BackColor = Color.WhiteSmoke;
        Icon = AppResource.Icon;
        FormClosing += MainForm_FormClosing;
    }
    private void MainForm_FormClosing(object? sender, FormClosingEventArgs e)
    {
        if (e.CloseReason != CloseReason.UserClosing) return;
        var config = Configurations.Instance;
        if (config.Settings.CloseToTray)
        {
            e.Cancel = true;
            Hide();
            Program.ShowMinimizeToTrayNotification();
        }
        else
        {
            Application.Exit();
        }
    }
    private void SettingsButton_Click(object? sender, EventArgs e)
    {
        using var settingsForm = new SettingsForm();
        var result = settingsForm.ShowDialog(this);
        if (result == DialogResult.Abort)
        {
            var session = Program.GetSessionManager();
            if (session != null)
            {
                using var loginForm = new SteamLoginForm(session);
                loginForm.ShowDialog(this);
            }
        }
    }
    private void UpdateTimer_Tick(object? sender, EventArgs e)
    {
        UpdateDisplay();
    }
    private void UpdateDisplay(bool forceRefresh = false)
    {
        try
        {
            var rpcManager = Program.GetRpcManager();
            if (rpcManager == null) return;
            var allPlayersStatus = rpcManager.GetAllPlayersStatus();
            for (var i = 0; i < 3; i++)
            {
                var (playerInfo, playerName, isActive, lastError) = allPlayersStatus[i];
                UpdatePlayerDisplay(i, playerInfo, playerName, isActive, forceRefresh, lastError);
            }
            ImageCacheManager.SetActiveKeys(_currentCacheKeys.Where(k => !string.IsNullOrEmpty(k)));
            _lastUpdateLabel.Text = $"最后更新: {DateTime.Now:HH:mm:ss}";
        }
        catch (Exception ex)
        {
            _lastUpdateLabel.Text = $"更新失败: {ex.Message}";
        }
    }
    private void UpdatePlayerDisplay(int index, PlayerInfo? playerInfo, string playerName, bool isActive,
        bool forceRefresh, RpcManager.ErrorCode lastError)
    {
        var lastInfo = _lastPlayerInfos[index];
        var lastActive = _lastActiveStates[index];
        if (!forceRefresh && lastActive == isActive && lastError == RpcManager.ErrorCode.None)
        {
            if (!isActive) return; 
            if (lastInfo.HasValue && playerInfo.HasValue &&
                lastInfo.Value.Title == playerInfo.Value.Title &&
                lastInfo.Value.Artists == playerInfo.Value.Artists &&
                lastInfo.Value.Album == playerInfo.Value.Album &&
                lastInfo.Value.Cover == playerInfo.Value.Cover &&
                lastInfo.Value.Pause == playerInfo.Value.Pause &&
                Math.Abs(lastInfo.Value.Schedule - playerInfo.Value.Schedule) < 0.5 && 
                Math.Abs(lastInfo.Value.Duration - playerInfo.Value.Duration) < 0.5) 
            {
                return;
            }
        }
        _lastPlayerInfos[index] = playerInfo;
        _lastActiveStates[index] = isActive;
        if (isActive && playerInfo != null)
        {
            const string zeroWidthSpace = "\u200B";
            var title = string.IsNullOrEmpty(playerInfo.Value.Title)
                ? StringUtils.GetTruncatedStringByMaxByteLength("未知歌曲", 128)
                : StringUtils.GetTruncatedStringByMaxByteLength(playerInfo.Value.Title + zeroWidthSpace, 128);
            var artists = string.IsNullOrEmpty(playerInfo.Value.Artists)
                ? ""
                : StringUtils.GetTruncatedStringByMaxByteLength(playerInfo.Value.Artists + zeroWidthSpace, 128);
            var album = string.IsNullOrEmpty(playerInfo.Value.Album)
                ? ""
                : StringUtils.GetTruncatedStringByMaxByteLength(playerInfo.Value.Album + zeroWidthSpace, 128);
            var currentSongId = playerInfo.Value.Identity;
            var previousSongId = _currentSongIds[index];
            if (!string.IsNullOrEmpty(currentSongId) && currentSongId != previousSongId)
            {
                _currentSongIds[index] = currentSongId;
            }
            if (_songTitleLabels[index].Text != title) _songTitleLabels[index].Text = title;
            var artistText = string.IsNullOrEmpty(artists) ? "" : $"🎤 {artists}";
            if (_artistLabels[index].Text != artistText) _artistLabels[index].Text = artistText;
            var albumText = string.IsNullOrEmpty(album) ? "" : $"💿 {album}";
            if (_albumLabels[index].Text != albumText) _albumLabels[index].Text = albumText;
            var statusText = playerInfo.Value.Pause ? "⏸️ 已暂停" : "▶️ 正在播放";
            var statusColor = playerInfo.Value.Pause ? Color.Orange : Color.Green;
            if (_statusLabels[index].Text != statusText)
            {
                _statusLabels[index].Text = statusText;
                _statusLabels[index].ForeColor = statusColor;
            }
            if (playerInfo.Value.Duration > 0)
            {
                var progressPercentage = (int)((playerInfo.Value.Schedule / playerInfo.Value.Duration) * 100);
                var newValue = Math.Max(0, Math.Min(100, progressPercentage));
                if (_progressBars[index].Value != newValue) _progressBars[index].Value = newValue;
                var currentTime = TimeSpan.FromSeconds(playerInfo.Value.Schedule);
                var totalTime = TimeSpan.FromSeconds(playerInfo.Value.Duration);
                var progressText = $@"{currentTime:mm\:ss} / {totalTime:mm\:ss}";
                if (_progressLabels[index].Text != progressText) _progressLabels[index].Text = progressText;
            }
            else
            {
                if (_progressBars[index].Value != 0) _progressBars[index].Value = 0;
                if (_progressLabels[index].Text != "00:00 / 00:00") _progressLabels[index].Text = "00:00 / 00:00";
            }
            if (!string.IsNullOrEmpty(playerInfo.Value.Cover))
            {
                var uniqueCacheKey = string.IsNullOrEmpty(currentSongId)
                    ? playerInfo.Value.Cover
                    : $"{playerInfo.Value.Cover}_{currentSongId}";
                _currentCacheKeys[index] = uniqueCacheKey;
                if (_currentCoverUrls[index] != playerInfo.Value.Cover)
                {
                    _currentCoverUrls[index] = playerInfo.Value.Cover;
                    Logger.Diagnose(
                        $"Updating cover for '{playerInfo.Value.Title}' (ID: {currentSongId}).");
                    Logger.Diagnose($"  - Cover URL: {playerInfo.Value.Cover}");
                    Logger.Diagnose($"  - Cache Key: {uniqueCacheKey}");
                    LoadCoverAsyncWithUniqueKey(index, playerInfo.Value.Cover, uniqueCacheKey);
                }
            }
            else
            {
                _currentCacheKeys[index] = "";
                if (_coverPictureBoxes[index].Image != null)
                {
                    _coverPictureBoxes[index].Image = null;
                    _coverPictureBoxes[index].BackColor = Color.LightGray;
                    _currentCoverUrls[index] = "";
                }
            }
            if (_playerPanels[index].BackColor != Color.White) _playerPanels[index].BackColor = Color.White;
            if (_playerNameLabels[index].ForeColor != _playerColors[index])
                _playerNameLabels[index].ForeColor = _playerColors[index];
        }
        else
        {
            var defaultTitle = StringUtils.GetTruncatedStringByMaxByteLength("播放器未运行", 128);
            if (_songTitleLabels[index].Text != defaultTitle) _songTitleLabels[index].Text = defaultTitle;
            if (_artistLabels[index].Text != "") _artistLabels[index].Text = "";
            if (_albumLabels[index].Text != "") _albumLabels[index].Text = "";
            string statusText;
            switch (lastError)
            {
                case RpcManager.ErrorCode.PermissionDenied:
                    statusText = "⚠️ 需要管理员运行";
                    break;
                case RpcManager.ErrorCode.DllNotFound:
                    statusText = "⚠️ 缺少组件(DLL)";
                    break;
                case RpcManager.ErrorCode.VersionNotSupported:
                    statusText = "⚠️ 版本不支持/特征码失效";
                    break;
                default:
                    statusText = "未运行";
                    break;
            }
            if (_statusLabels[index].Text != statusText)
            {
                _statusLabels[index].Text = statusText;
                _statusLabels[index].ForeColor = lastError != RpcManager.ErrorCode.None ? Color.Red : Color.Gray;
            }
            if (_progressBars[index].Value != 0) _progressBars[index].Value = 0;
            if (_progressLabels[index].Text != "00:00 / 00:00") _progressLabels[index].Text = "00:00 / 00:00";
            if (_coverPictureBoxes[index].Image != null)
            {
                _coverPictureBoxes[index].Image = null;
                _coverPictureBoxes[index].BackColor = Color.LightGray;
            }
            var inactiveColor = Color.FromArgb(248, 248, 248);
            if (_playerPanels[index].BackColor != inactiveColor) _playerPanels[index].BackColor = inactiveColor;
            if (_playerNameLabels[index].ForeColor != Color.Gray) _playerNameLabels[index].ForeColor = Color.Gray;
            _currentSongIds[index] = string.Empty;
            _currentCoverUrls[index] = string.Empty;
            _currentCacheKeys[index] = string.Empty;
        }
    }
    private async void LoadCoverAsyncWithUniqueKey(int index, string coverUrl, string forceCacheKey)
    {
        try
        {
            var image = await ImageCacheManager.LoadImageAsync(forceCacheKey, coverUrl);
            if (IsHandleCreated && image != null)
            {
                await InvokeAsync(() =>
                {
                    if (coverUrl == _currentCoverUrls[index])
                    {
                        var pictureBox = _coverPictureBoxes[index];
                        try
                        {
                            _ = image.RawFormat;
                            pictureBox.Image = image;
                            pictureBox.BackColor = Color.White;
                        }
                        catch (Exception ex)
                        {
                            pictureBox.Image = null;
                            pictureBox.BackColor = Color.LightGray;
                            Logger.Error($"Invalid image detected: {ex.Message}");
                        }
                    }
                    else
                    {
                        Logger.Diagnose($"Stale cover update ignored for URL: {coverUrl}");
                    }
                });
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to load cover image with timestamp: {ex.Message}");
            if (IsHandleCreated)
            {
                await InvokeAsync(() =>
                {
                    var pictureBox = _coverPictureBoxes[index];
                    pictureBox.Image = null;
                    pictureBox.BackColor = Color.LightGray;
                });
            }
        }
    }
    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        _updateTimer.Start();
        UpdateDisplay(true);
        Task.Run(async () =>
        {
            await Task.Delay(2000); 
            MemoryProfiler.LogMemorySnapshot("窗体加载后优化前");
            await MemoryOptimizer.PerformAggressiveOptimizationAsync();
            MemoryProfiler.LogMemorySnapshot("窗体加载后优化后");
        });
    }
    private static void MainForm_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e is not { Control: true, KeyCode: Keys.R }) return;
        var beforeMemory = MemoryPressureMonitor.GetCurrentMemoryUsage();
        ImageCacheManager.ForceCleanupCache();
        var afterMemory = MemoryPressureMonitor.GetCurrentMemoryUsage();
        var freedMemory = beforeMemory - afterMemory;
        MessageBox.Show(
            $"缓存清理完成！\n清理前: {beforeMemory / 1024 / 1024:F1} MB\n清理后: {afterMemory / 1024 / 1024:F1} MB\n释放: {freedMemory / 1024 / 1024:F1} MB\n\n提示: 按 Ctrl+R 可随时清理缓存",
            "缓存清理", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _updateTimer.Dispose();
        }
        base.Dispose(disposing);
    }
}