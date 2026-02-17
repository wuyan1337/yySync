using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using MusicRpc.Models;
using MusicRpc.Players;
using MusicRpc.Players.Interfaces;
using MusicRpc.Utils;
namespace MusicRpc;
internal class RpcManager(SteamStatusManager steamManager)
{
    private class PlayerState
    {
        public IMusicPlayer? Player { get; set; }
        public PlayerInfo? LastPolledInfo { get; set; }
        public DateTime LastPollTime { get; set; } = DateTime.MinValue;
        public PlayerInfo? PendingUpdateInfo { get; set; }
        public DateTime LastChangeDetectedTime { get; set; } = DateTime.MinValue;
    }
    private readonly PlayerState _netEaseState = new();
    private readonly PlayerState _tencentState = new();
    private readonly PlayerState _lxMusicState = new();
    private volatile bool _stateRefreshRequested;
    private const double JumpToleranceSeconds = 0.4;
    private const double DebounceWindowSeconds = 1.5;
    private const double ProgressUpdateIntervalSeconds = 5.0;
    private DateTime _lastProgressUpdateTime = DateTime.MinValue;
    public void RequestStateRefresh() => _stateRefreshRequested = true;
    public (PlayerInfo? PlayerInfo, string PlayerName) GetCurrentPlayerInfo()
    {
        if (_netEaseState is { Player: not null, LastPolledInfo: not null })
        {
            return (_netEaseState.LastPolledInfo, "网易云音乐");
        }
        if (_tencentState is { Player: not null, LastPolledInfo: not null })
        {
            return (_tencentState.LastPolledInfo, "QQ音乐");
        }
        return _lxMusicState is { Player: not null, LastPolledInfo: not null }
            ? (_lxMusicState.LastPolledInfo, "LX Music")
            :
            (null, "");
    }
    public (PlayerInfo? PlayerInfo, string PlayerName, bool IsActive)[] GetAllPlayersStatus()
    {
        return
        [
            (
                _netEaseState is { Player: not null, LastPolledInfo: not null }
                    ? _netEaseState.LastPolledInfo
                    : null,
                "网易云音乐",
                _netEaseState.Player != null
            ),
            (
                _tencentState is { Player: not null, LastPolledInfo: not null }
                    ? _tencentState.LastPolledInfo
                    : null,
                "QQ音乐",
                _tencentState.Player != null
            ),
            (
                _lxMusicState is { Player: not null, LastPolledInfo: not null }
                    ? _lxMusicState.LastPolledInfo
                    : null,
                "LX Music",
                _lxMusicState.Player != null
            )
        ];
    }
    public async Task Start()
    {
        while (true)
        {
            var currentTime = DateTime.UtcNow;
            try
            {
                var neteaseHwnd = Win32Api.User32.FindWindow("OrpheusBrowserHost", null);
                if (neteaseHwnd != IntPtr.Zero &&
                    Win32Api.User32.GetWindowThreadProcessId(neteaseHwnd, out var neteasePid) != 0)
                {
                    await PollAndUpdatePlayer(_netEaseState, "NetEase CloudMusic", neteasePid,
                        pid => new NetEase(pid), currentTime);
                }
                else
                {
                    CleanupPlayerState(_netEaseState, "NetEase CloudMusic");
                }
                var tencentHwnd = Win32Api.User32.FindWindow("QQMusic_Daemon_Wnd", null);
                if (tencentHwnd != IntPtr.Zero &&
                    Win32Api.User32.GetWindowThreadProcessId(tencentHwnd, out var tencentPid) != 0)
                {
                    await PollAndUpdatePlayer(_tencentState, "Tencent QQMusic", tencentPid,
                        pid => new Tencent(pid), currentTime);
                }
                else
                {
                    CleanupPlayerState(_tencentState, "Tencent QQMusic");
                }
                var lxProcess = Process.GetProcessesByName("lx-music-desktop").FirstOrDefault();
                if (lxProcess != null)
                {
                    await PollAndUpdatePlayer(_lxMusicState, "LX Music", lxProcess.Id,
                        pid => new LxMusic(pid),
                        currentTime);
                }
                else
                {
                    CleanupPlayerState(_lxMusicState, "LX Music");
                }
                if (_stateRefreshRequested)
                {
                    _stateRefreshRequested = false;
                    Debug.WriteLine("Settings changed. Forcing immediate Steam status update for active players.");
                    ForceRefreshPlayer(_netEaseState, "NetEase CloudMusic");
                    ForceRefreshPlayer(_tencentState, "Tencent QQMusic");
                    ForceRefreshPlayer(_lxMusicState, "LX Music");
                }
                if ((currentTime - _lastProgressUpdateTime).TotalSeconds >= ProgressUpdateIntervalSeconds)
                {
                    UpdateProgressForActivePlayers();
                    _lastProgressUpdateTime = currentTime;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FATAL ERROR] An exception occurred in the main poll loop: {ex.Message}");
                ClearAllPlayers();
            }
            finally
            {
                await Task.Delay(TimeSpan.FromMilliseconds(233));
            }
        }
    }
    private async Task PollAndUpdatePlayer(PlayerState state, string playerName,
        int pid,
        Func<int, IMusicPlayer> playerFactory, DateTime currentTime)
    {
        if (state.Player is null)
        {
            Debug.WriteLine($"[{playerName}] Player process detected. Creating instance.");
            state.Player = playerFactory(pid);
        }
        var currentInfo = await state.Player.GetPlayerInfoAsync();
        var isStateChanged = DetectStateChange(currentInfo, state.LastPolledInfo, currentTime, state.LastPollTime,
            JumpToleranceSeconds);
        if (isStateChanged)
        {
            Debug.WriteLine(
                $"[{playerName}] State change detected. Resetting debounce timer for: {currentInfo?.Title ?? "None (Clear)"}");
            state.PendingUpdateInfo = currentInfo;
            state.LastChangeDetectedTime = currentTime;
        }
        if (state.PendingUpdateInfo is not null &&
            (currentTime - state.LastChangeDetectedTime).TotalSeconds > DebounceWindowSeconds)
        {
            Debug.WriteLine($"[{playerName}] Debounce window passed. Sending Steam status update.");
            await UpdateOrClearSteamStatusAsync(state.PendingUpdateInfo, playerName);
            state.PendingUpdateInfo = null;
        }
        state.LastPolledInfo = currentInfo;
        state.LastPollTime = currentTime;
    }
    private void CleanupPlayerState(PlayerState state, string playerName)
    {
        if (state.Player is null) return;
        Debug.WriteLine($"[{playerName}] Player process lost. Clearing instance and Steam status.");
        steamManager.ClearStatus();  
        state.Player = null;
        state.LastPolledInfo = null;
        state.PendingUpdateInfo = null;
    }
    private async void ForceRefreshPlayer(PlayerState state, string playerName)
    {
        if (state.Player is not null)
        {
            await UpdateOrClearSteamStatusAsync(state.LastPolledInfo, playerName);
        }
    }
    private void ClearAllPlayers()
    {
        CleanupPlayerState(_netEaseState, "NetEase CloudMusic");
        CleanupPlayerState(_tencentState, "Tencent QQMusic");
        CleanupPlayerState(_lxMusicState, "LX Music");
    }
    private static bool DetectStateChange(PlayerInfo? current, PlayerInfo? last, DateTime currentTime,
        DateTime lastTime, double tolerance)
    {
        if ((current is null && last is not null) || (current is not null && last is null)) return true;
        if (current is not { } c || last is not { } l) return false;
        if (c.Identity != l.Identity || c.Pause != l.Pause) return true;
        if (c.Pause) return false;
        var elapsed = (currentTime - lastTime).TotalSeconds;
        var progressDelta = c.Schedule - l.Schedule;
        return Math.Abs(progressDelta - elapsed) > tolerance;
    }
    private async Task UpdateOrClearSteamStatusAsync(PlayerInfo? info, string playerName)
    {
        if (info is not { } playerInfo)
        {
            steamManager.ClearStatus();
            return;
        }
        Debug.WriteLine(
            $"pause: {playerInfo.Pause}, progress: {playerInfo.Schedule}, duration: {playerInfo.Duration}");
        Debug.WriteLine(
            $"id: {playerInfo.Identity}, name: {playerInfo.Title}, singer: {playerInfo.Artists}, album: {playerInfo.Album}");
        await steamManager.UpdateStatusAsync(info, playerName);
    }
    private async void UpdateProgressForActivePlayers()
    {
        var (info, playerName) = GetCurrentPlayerInfo();
        if (info is { Pause: false })
        {
            await steamManager.UpdateStatusAsync(info, playerName);
        }
    }
}