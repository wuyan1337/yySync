using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using MusicRpc.Models;
namespace MusicRpc;
internal class SteamStatusManager
{
    private readonly SteamSessionManager _session;
    private string _lastSetName = string.Empty;
    private const int ProgressBarLength = 10;
    public bool IsReady => _session.IsLoggedOn;
    public SteamStatusManager(SteamSessionManager session)
    {
        _session = session;
    }
    public async Task UpdateStatusAsync(PlayerInfo? info, string playerName)
    {
        if (!_session.IsLoggedOn) return;
        var config = Configurations.Instance;
        var newName = FormatStatusName(info, playerName, config);
        if (newName == _lastSetName) return;
        try
        {
            await _session.SetGameNameAsync(newName).ConfigureAwait(false);
            _lastSetName = newName;
            Debug.WriteLine($"[SteamStatus] 状态已更新: {newName}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SteamStatus] 更新状态失败: {ex.Message}");
        }
    }
    public void ClearStatus()
    {
        if (!_session.IsLoggedOn) return;
        _session.ClearGameName();
        _lastSetName = string.Empty;
        Debug.WriteLine("[SteamStatus] 状态已清除");
    }
    public string GetStatusPreview(PlayerInfo? info, string playerName)
    {
        return FormatStatusName(info, playerName, Configurations.Instance);
    }
    private static string FormatStatusName(PlayerInfo? info, string playerName, Configurations config)
    {
        if (info is not { } playerInfo)
        {
            return "Music Steam RPC";
        }
        var sb = new System.Text.StringBuilder();
        sb.Append(playerInfo.Title);
        if (config.Settings.ShowArtistName && !string.IsNullOrEmpty(playerInfo.Artists))
        {
            sb.Append($" - {playerInfo.Artists}");
        }
        if (config.Settings.ShowProgressBar && !playerInfo.Pause && playerInfo.Duration > 0)
        {
            sb.Append(" [");
            var progress = Math.Clamp(playerInfo.Schedule / playerInfo.Duration, 0, 1);
            var filledCount = (int)(progress * ProgressBarLength);
            sb.Append(new string('#', filledCount));
            sb.Append(new string('-', ProgressBarLength - filledCount));
            sb.Append($"] {FormatTime(playerInfo.Schedule)}/{FormatTime(playerInfo.Duration)}");
        }
        else if (playerInfo.Pause)
        {
            sb.Append(" (Paused)");
        }
        return sb.ToString();
    }
    private static string FormatTime(double totalSeconds)
    {
        var ts = TimeSpan.FromSeconds(Math.Max(0, totalSeconds));
        return ts.Hours > 0
            ? $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}"
            : $"{ts.Minutes}:{ts.Seconds:D2}";
    }
}