using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using MusicRpc.Utils;
using Button = System.Windows.Forms.Button;
namespace MusicRpc;
internal class SettingsForm : Form
{
    private readonly ConfigData _originalSettings;
    private CheckBox? _autoStartCheckBox;
    private CheckBox? _closeToTrayCheckBox;
    private CheckBox? _startInTrayCheckBox;
    private CheckBox? _showArtistNameCheckBox;
    private CheckBox? _showProgressBarCheckBox;
    private CheckBox? _enableSteamSyncCheckBox;
    private RadioButton? _priorityArtistRadioButton;
    private RadioButton? _priorityProgressBarRadioButton;
    private Label? _steamStatusPreviewLabel;
    private Button? _okButton;
    private Button? _cancelButton;
    private Button? _applyButton;
    public SettingsForm()
    {
        _originalSettings = Configurations.Instance.Settings;
        InitializeComponent();
        LoadSettings();
    }
    private void InitializeComponent()
    {
        Text = "设置 - yySync";
        Size = new Size(480, 550);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        BackColor = Color.White;
        var scrollPanel = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = Color.White
        };
        var yOffset = 10;
        var programGroupBox = CreateProgramSettingsGroup(ref yOffset);
        scrollPanel.Controls.Add(programGroupBox);
        var steamDisplayGroupBox = CreateSteamDisplaySettingsGroup(ref yOffset);
        scrollPanel.Controls.Add(steamDisplayGroupBox);
        var previewGroupBox = CreateSteamPreviewGroup(ref yOffset);
        scrollPanel.Controls.Add(previewGroupBox);
        var performanceGroupBox = CreatePerformanceGroup(ref yOffset);
        scrollPanel.Controls.Add(performanceGroupBox);
        scrollPanel.Controls.Add(new Label { Location = new Point(0, yOffset), Size = new Size(1, 60) });
        var buttonPanel = CreateButtonPanelControl();
        Controls.Add(buttonPanel);
        Controls.Add(scrollPanel);
    }
    private int GetHorizontalCenterOffset(int controlWidth)
    {
        return (ClientSize.Width - 20 - controlWidth) / 2;
    }
    private GroupBox CreateProgramSettingsGroup(ref int yOffset)
    {
        var groupBox = new GroupBox
        {
            Text = "程序设置",
            Location = new Point(GetHorizontalCenterOffset(410), yOffset),
            Size = new Size(410, 105),
            BackColor = Color.White
        };
        yOffset += 115;
        _autoStartCheckBox = new CheckBox
        {
            Text = "开机自启",
            Location = new Point(15, 25),
            AutoSize = true,
            BackColor = Color.White
        };
        _closeToTrayCheckBox = new CheckBox
        {
            Text = "关闭窗口时隐藏到托盘",
            Location = new Point(15, 50),
            AutoSize = true,
            BackColor = Color.White
        };
        _startInTrayCheckBox = new CheckBox
        {
            Text = "启动时隐藏到托盘",
            Location = new Point(15, 75),
            AutoSize = true,
            BackColor = Color.White
        };
        groupBox.Controls.AddRange([_autoStartCheckBox, _closeToTrayCheckBox, _startInTrayCheckBox]);
        return groupBox;
    }
    private GroupBox CreateSteamDisplaySettingsGroup(ref int yOffset)
    {
        var groupBox = new GroupBox
        {
            Text = "Steam 显示设置",
            Location = new Point(GetHorizontalCenterOffset(410), yOffset),
            Size = new Size(410, 185),  
            BackColor = Color.White
        };
        yOffset += 195;  
        _enableSteamSyncCheckBox = new CheckBox
        {
            Text = "启用 Steam 同步",
            Location = new Point(15, 25),
            AutoSize = true,
            BackColor = Color.White
        };
        _showArtistNameCheckBox = new CheckBox
        {
            Text = "显示歌手名称",
            Location = new Point(15, 75),
            AutoSize = true,
            BackColor = Color.White
        };
        _showProgressBarCheckBox = new CheckBox
        {
            Text = "显示进度条 (▰▰▰▱▱▱ 2:30/4:15)",
            Location = new Point(15, 100),
            AutoSize = true,
            BackColor = Color.White
        };
        _showProgressBarCheckBox.CheckedChanged += (_, _) => UpdatePreview();
        _showArtistNameCheckBox.CheckedChanged += (_, _) => UpdatePreview();
        var priorityLabel = new Label
        {
            Text = "字数过多时优先显示:",
            Location = new Point(15, 130),
            AutoSize = true,
            BackColor = Color.White
        };
        _priorityArtistRadioButton = new RadioButton
        {
            Text = "歌手名称",
            Location = new Point(160, 128),
            AutoSize = true,
            BackColor = Color.White
        };
        _priorityProgressBarRadioButton = new RadioButton
        {
            Text = "进度条",
            Location = new Point(250, 128),
            AutoSize = true,
            BackColor = Color.White
        };
        _priorityArtistRadioButton.CheckedChanged += (_, _) => UpdatePreview();
        _priorityProgressBarRadioButton.CheckedChanged += (_, _) => UpdatePreview();
        groupBox.Controls.AddRange([
            _enableSteamSyncCheckBox, 
            _showArtistNameCheckBox, 
            _showProgressBarCheckBox,
            priorityLabel,
            _priorityArtistRadioButton,
            _priorityProgressBarRadioButton
        ]);
        return groupBox;
    }
    private GroupBox CreateSteamPreviewGroup(ref int yOffset)
    {
        var groupBox = new GroupBox
        {
            Text = "Steam 状态预览",
            Location = new Point(GetHorizontalCenterOffset(410), yOffset),
            Size = new Size(410, 80),
            BackColor = Color.White
        };
        yOffset += 90;
        var prefixLabel = new Label
        {
            Text = "好友看到：",
            Location = new Point(15, 28),
            AutoSize = true,
            BackColor = Color.White,
            ForeColor = Color.Gray,
            Font = new Font("Microsoft YaHei", 9)
        };
        _steamStatusPreviewLabel = new Label
        {
            Text = "🎵 稻香 - 周杰伦 ▰▰▰▰▰▱▱▱▱▱ 2:30/4:15",
            Location = new Point(15, 48),
            Size = new Size(380, 20),
            BackColor = Color.FromArgb(240, 240, 240),
            ForeColor = Color.FromArgb(50, 50, 50),
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            Padding = new Padding(5, 2, 5, 2)
        };
        groupBox.Controls.AddRange([prefixLabel, _steamStatusPreviewLabel]);
        return groupBox;
    }
    private GroupBox CreatePerformanceGroup(ref int yOffset)
    {
        var groupBox = new GroupBox
        {
            Text = "性能监控",
            Location = new Point(GetHorizontalCenterOffset(410), yOffset),
            Size = new Size(410, 150),
            BackColor = Color.White
        };
        yOffset += 160;
        var memoryLabel = new Label
        {
            Text = "内存使用情况:",
            Location = new Point(15, 25),
            AutoSize = true,
            BackColor = Color.White,
            Font = new Font("Microsoft YaHei", 9, FontStyle.Bold)
        };
        var memoryInfoLabel = new Label
        {
            Name = "memoryInfoLabel",
            Text = "点击刷新查看当前内存使用情况",
            Location = new Point(15, 45),
            Size = new Size(380, 40),
            BackColor = Color.White,
            ForeColor = Color.Gray
        };
        var refreshButton = new Button
        {
            Text = "刷新内存信息",
            Location = new Point(15, 90),
            Size = new Size(100, 25),
            BackColor = Color.White,
            Font = new Font("Microsoft YaHei", 8)
        };
        refreshButton.Click += RefreshMemoryInfo_Click;
        groupBox.Controls.AddRange([memoryLabel, memoryInfoLabel, refreshButton]);
        return groupBox;
    }
    private Panel CreateButtonPanelControl()
    {
        var buttonPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 60,
            BackColor = Color.FromArgb(245, 245, 245),
            Padding = new Padding(0, 5, 0, 0)
        };
        const int buttonWidth = 80;
        const int buttonHeight = 30;
        const int spacing = 10;
        const int totalWidth = buttonWidth * 3 + spacing * 2;
        var startX = (ClientSize.Width - totalWidth) / 2;
        _okButton = new Button
        {
            Text = "确定",
            Size = new Size(buttonWidth, buttonHeight),
            Location = new Point(startX, 10),
            DialogResult = DialogResult.OK,
            BackColor = Color.White
        };
        _cancelButton = new Button
        {
            Text = "取消",
            Size = new Size(buttonWidth, buttonHeight),
            Location = new Point(startX + buttonWidth + spacing, 10),
            DialogResult = DialogResult.Cancel,
            BackColor = Color.White
        };
        _applyButton = new Button
        {
            Text = "应用",
            Size = new Size(buttonWidth, buttonHeight),
            Location = new Point(startX + (buttonWidth + spacing) * 2, 10),
            BackColor = Color.White
        };
        _okButton.Click += OkButton_Click;
        _cancelButton.Click += CancelButton_Click;
        _applyButton.Click += ApplyButton_Click;
        buttonPanel.Controls.AddRange([_okButton, _cancelButton, _applyButton]);
        return buttonPanel;
    }
    private void RefreshMemoryInfo_Click(object? sender, EventArgs e)
    {
        try
        {
            var memoryInfo = PerformanceMonitor.GetMemoryInfo();
            var cacheStats = PerformanceMonitor.GetCacheStatistics();
            if (Controls.Find("memoryInfoLabel", true).FirstOrDefault() is not Label memoryLabel) return;
            memoryLabel.Text = $"""
                                工作集: {memoryInfo.GetFormattedWorkingSet()}, 私有内存: {memoryInfo.GetFormattedPrivateMemory()}
                                GC内存: {memoryInfo.GetFormattedGcMemory()}, 缓存项: {cacheStats.TotalCacheCount}
                                """;
            memoryLabel.ForeColor = Color.Black;
        }
        catch (Exception ex)
        {
            if (Controls.Find("memoryInfoLabel", true).FirstOrDefault() is Label memoryLabel)
            {
                memoryLabel.Text = $"获取内存信息失败: {ex.Message}";
                memoryLabel.ForeColor = Color.Red;
            }
        }
    }
    private void LoadSettings()
    {
        var isAutoStartEnabled = Win32Api.AutoStart.Check();
        _autoStartCheckBox!.Checked = isAutoStartEnabled;
        _originalSettings.AutoStart = isAutoStartEnabled;
        _closeToTrayCheckBox!.Checked = _originalSettings.CloseToTray;
        _startInTrayCheckBox!.Checked = _originalSettings.StartInTray;
        _showArtistNameCheckBox!.Checked = _originalSettings.ShowArtistName;
        _showProgressBarCheckBox!.Checked = _originalSettings.ShowProgressBar;
        _enableSteamSyncCheckBox!.Checked = _originalSettings.EnableSteamSync;
        if (_originalSettings.StatusPriority == SteamStatusPriority.Artist)
        {
            _priorityArtistRadioButton!.Checked = true;
        }
        else
        {
            _priorityProgressBarRadioButton!.Checked = true;
        }
        UpdatePreview();
    }
    private void UpdatePreview()
    {
        if (_steamStatusPreviewLabel == null) return;
        var showArtist = _showArtistNameCheckBox?.Checked ?? true;
        var showProgress = _showProgressBarCheckBox?.Checked ?? true;
        var dummyInfo = new Models.PlayerInfo
        {
            Title = "稻香",
            Artists = "周杰伦",
            Schedule = 150,
            Duration = 255,
            Pause = false,
            Url = "",
            Cover = "",
            Album = "",
            Identity = ""
        };
        var tempConfig = new ConfigData();
        _steamStatusPreviewLabel.Text = SteamStatusManager.GetStatusPreview(dummyInfo, "yySync", new ConfigData
        {
            ShowArtistName = showArtist,
            ShowProgressBar = showProgress,
            StatusPriority = _priorityArtistRadioButton?.Checked == true ? SteamStatusPriority.Artist : SteamStatusPriority.ProgressBar
        });
    }
    private void SaveSettings()
    {
        var settings = Configurations.Instance.Settings;
        var isAutoStartChecked = _autoStartCheckBox!.Checked;
        settings.AutoStart = isAutoStartChecked;
        settings.CloseToTray = _closeToTrayCheckBox!.Checked;
        settings.StartInTray = _startInTrayCheckBox!.Checked;
        settings.ShowArtistName = _showArtistNameCheckBox!.Checked;
        settings.ShowProgressBar = _showProgressBarCheckBox!.Checked;
        settings.EnableSteamSync = _enableSteamSyncCheckBox!.Checked;
        settings.StatusPriority = _priorityArtistRadioButton!.Checked ? SteamStatusPriority.Artist : SteamStatusPriority.ProgressBar;
        Configurations.Instance.Save();
        Program.GetRpcManager()?.RequestStateRefresh();
        if (isAutoStartChecked == Win32Api.AutoStart.Check()) return;
        var success = Win32Api.AutoStart.Set(isAutoStartChecked);
        if (!success)
        {
            MessageBox.Show(
                $"无法 {(isAutoStartChecked ? "设置" : "取消")} 开机自启。\n请尝试以管理员权限运行本程序一次。",
                "操作失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
    private void OkButton_Click(object? sender, EventArgs e)
    {
        SaveSettings();
        Close();
    }
    private void CancelButton_Click(object? sender, EventArgs e)
    {
        Close();
    }
    private void ApplyButton_Click(object? sender, EventArgs e)
    {
        SaveSettings();
    }
}