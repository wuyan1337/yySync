using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using MusicRpc.Models;
using MusicRpc.Utils;
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
        var config = Configurations.Instance.Settings;
        var newName = FormatStatusName(info, playerName, config);
        if (newName == _lastSetName) return;
        try
        {
            await _session.SetGameNameAsync(newName).ConfigureAwait(false);
            _lastSetName = newName;
            Logger.Steam($"状态已更新: {newName}");
        }
        catch (Exception ex)
        {
            Logger.Steam($"更新状态失败: {ex.Message}");
        }
    }
    public void ClearStatus()
    {
        if (!_session.IsLoggedOn) return;
        _session.ClearGameName();
        _lastSetName = string.Empty;
        Logger.Steam("状态已清除");
    }
    public string GetStatusPreview(PlayerInfo? info, string playerName)
    {
        return FormatStatusName(info, playerName, Configurations.Instance.Settings);
    }
    public static string GetStatusPreview(PlayerInfo? info, string playerName, ConfigData config)
    {
        return FormatStatusName(info, playerName, config);
    }
    private static int GetUtf8ByteCount(string str)
    {
        return System.Text.Encoding.UTF8.GetByteCount(str);
    }
    private static string TruncateToUtf8ByteLength(string str, int maxBytes)
    {
        if (string.IsNullOrEmpty(str)) return str;
        var bytes = System.Text.Encoding.UTF8.GetBytes(str);
        if (bytes.Length <= maxBytes) return str;
        var count = maxBytes;
        while (count > 0 && (bytes[count] & 0xC0) == 0x80)
        {
            count--;
        }
        int byteCount = 0;
        int charCount = 0;
        foreach (var c in str)
        {
            int cBytes = System.Text.Encoding.UTF8.GetByteCount(new [] { c });
            if (byteCount + cBytes > maxBytes) break;
            byteCount += cBytes;
            charCount++;
        }
        return str.Substring(0, charCount);
    }
    private static string FormatStatusName(PlayerInfo? info, string playerName, ConfigData config)
    {
        if (info is not { } playerInfo)
        {
            return "yySync";
        }
        var prefix = config.EnableCustomPrefix && !string.IsNullOrEmpty(config.CustomPrefix)
            ? config.CustomPrefix
            : string.Empty;
        var prefixBytes = GetUtf8ByteCount(prefix);
        var title = playerInfo.Title;
        var artistPart = string.Empty;
        var progressPart = string.Empty;
        if (config.ShowArtistName && !string.IsNullOrEmpty(playerInfo.Artists))
        {
            artistPart = $" - {playerInfo.Artists}";
        }
        if (config.ShowProgressBar && !playerInfo.Pause && playerInfo.Duration > 0)
        {
            var sbStats = new System.Text.StringBuilder();
            sbStats.Append(" [");
            var progress = Math.Clamp(playerInfo.Schedule / playerInfo.Duration, 0, 1);
            var filledCount = (int)(progress * ProgressBarLength);
            sbStats.Append(new string('#', filledCount));
            sbStats.Append(new string('-', ProgressBarLength - filledCount));
            sbStats.Append($"] {FormatTime(playerInfo.Schedule)}/{FormatTime(playerInfo.Duration)}");
            progressPart = sbStats.ToString();
        }
        else if (playerInfo.Pause)
        {
            progressPart = " (Paused)"; 
        }
        var contentMaxBytes = 63 - prefixBytes;
        if (contentMaxBytes <= 0) return TruncateToUtf8ByteLength(prefix, 63);
        var fullString = $"{title}{artistPart}{progressPart}";
        if (GetUtf8ByteCount(fullString) <= contentMaxBytes) return $"{prefix}{fullString}";
        if (config.StatusPriority == SteamStatusPriority.Artist)
        {
            var artistString = $"{title}{artistPart}";
            if (GetUtf8ByteCount(artistString) <= contentMaxBytes) return $"{prefix}{artistString}";
            return $"{prefix}{TruncateToUtf8ByteLength(title, contentMaxBytes)}";
        }
        else  
        {
            var progressString = $"{title}{progressPart}";
            if (GetUtf8ByteCount(progressString) <= contentMaxBytes) return $"{prefix}{progressString}";
            return $"{prefix}{TruncateToUtf8ByteLength(title, contentMaxBytes)}";
        }
    }
    private static string FormatTime(double totalSeconds)
    {
        var ts = TimeSpan.FromSeconds(Math.Max(0, totalSeconds));
        return ts.Hours > 0
            ? $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}"
            : $"{ts.Minutes}:{ts.Seconds:D2}";
    }
}