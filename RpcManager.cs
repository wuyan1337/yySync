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
        public bool PermissionDenied { get; set; }
    }
    private readonly PlayerState _netEaseState = new();
    private readonly PlayerState _tencentState = new();
    private readonly PlayerState _lxMusicState = new();
    private volatile bool _stateRefreshRequested;
    private const double JumpToleranceSeconds = 0.4;
    private const double DebounceWindowSeconds = 1.5;
    private const double ProgressUpdateIntervalSeconds = 1.0;
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
    public (PlayerInfo? PlayerInfo, string PlayerName, bool IsActive, bool IsPermissionDenied)[] GetAllPlayersStatus()
    {
        return
        [
            (
                _netEaseState is { Player: not null, LastPolledInfo: not null }
                    ? _netEaseState.LastPolledInfo
                    : null,
                "网易云音乐",
                _netEaseState.Player != null,
                _netEaseState.PermissionDenied
            ),
            (
                _tencentState is { Player: not null, LastPolledInfo: not null }
                    ? _tencentState.LastPolledInfo
                    : null,
                "QQ音乐",
                _tencentState.Player != null,
                _tencentState.PermissionDenied
            ),
            (
                _lxMusicState is { Player: not null, LastPolledInfo: not null }
                    ? _lxMusicState.LastPolledInfo
                    : null,
                "LX Music",
                _lxMusicState.Player != null,
                _lxMusicState.PermissionDenied
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
                    try
                    {
                        await PollAndUpdatePlayer(_netEaseState, "NetEase CloudMusic", neteasePid,
                            pid => new NetEase(pid), currentTime);
                        _netEaseState.PermissionDenied = false;
                    }
                    catch (DllNotFoundException)
                    {
                        _netEaseState.PermissionDenied = true;
                        Debug.WriteLine("[NetEase] Permission denied (DllNotFound).");
                    }
                    catch (EntryPointNotFoundException)
                    {
                        _netEaseState.PermissionDenied = true;
                         Debug.WriteLine("[NetEase] Permission denied (EntryPointNotFound).");
                    }
                    catch (Exception ex)
                    {
                         Debug.WriteLine($"[NetEase] Error: {ex.Message}");
                    }
                }
                else
                {
                    CleanupPlayerState(_netEaseState, "NetEase CloudMusic");
                     _netEaseState.PermissionDenied = false;
                }
                var tencentHwnd = Win32Api.User32.FindWindow("QQMusic_Daemon_Wnd", null);
                if (tencentHwnd != IntPtr.Zero &&
                    Win32Api.User32.GetWindowThreadProcessId(tencentHwnd, out var tencentPid) != 0)
                {
                    try
                    {
                        await PollAndUpdatePlayer(_tencentState, "Tencent QQMusic", tencentPid,
                            pid => new Tencent(pid), currentTime);
                        _tencentState.PermissionDenied = false;
                    }
                    catch (DllNotFoundException)
                    {
                         _tencentState.PermissionDenied = true;
                         Debug.WriteLine("[Tencent] Permission denied (DllNotFound).");
                    }
                    catch (EntryPointNotFoundException)
                    {
                        _tencentState.PermissionDenied = true;
                        Debug.WriteLine("[Tencent] Permission denied (EntryPointNotFound).");
                    }
                     catch (Exception ex)
                    {
                         Debug.WriteLine($"[Tencent] Error: {ex.Message}");
                    }
                }
                else
                {
                    CleanupPlayerState(_tencentState, "Tencent QQMusic");
                    _tencentState.PermissionDenied = false;
                }
                var lxProcess = Process.GetProcessesByName("lx-music-desktop").FirstOrDefault();
                if (lxProcess != null)
                {
                     try
                     {
                        await PollAndUpdatePlayer(_lxMusicState, "LX Music", lxProcess.Id,
                            pid => new LxMusic(pid),
                            currentTime);
                        _lxMusicState.PermissionDenied = false;
                     }
                     catch (DllNotFoundException)
                     {
                         _lxMusicState.PermissionDenied = true;
                     }
                      catch (Exception ex)
                    {
                         Debug.WriteLine($"[LX Music] Error: {ex.Message}");
                    }
                }
                else
                {
                    CleanupPlayerState(_lxMusicState, "LX Music");
                    _lxMusicState.PermissionDenied = false;
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
                    UpdateProgressForActivePlayers(currentTime);
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
    private async void UpdateProgressForActivePlayers(DateTime currentTime)
    {
        PlayerState? activeState = null;
        string activePlayerName = "";
        if (_netEaseState is { Player: not null, LastPolledInfo: not null })
        {
            activeState = _netEaseState;
            activePlayerName = "网易云音乐";
        }
        else if (_tencentState is { Player: not null, LastPolledInfo: not null })
        {
            activeState = _tencentState;
            activePlayerName = "QQ音乐";
        }
        else if (_lxMusicState is { Player: not null, LastPolledInfo: not null })
        {
            activeState = _lxMusicState;
            activePlayerName = "LX Music";
        }
        if (activeState?.LastPolledInfo is { Pause: false } info)
        {
            var elapsedSincePoll = (currentTime - activeState.LastPollTime).TotalSeconds;
            var interpolatedSchedule = info.Schedule + elapsedSincePoll;
            var clampedSchedule = Math.Min(interpolatedSchedule, info.Duration);
            var updatedInfo = info with { Schedule = clampedSchedule };
            await steamManager.UpdateStatusAsync(updatedInfo, activePlayerName);
        }
    }
}