using System;
using System.Text;
using System.Threading.Tasks;
using MusicRpc.Models;
using MusicRpc.Players.Interfaces;
using MusicRpc.Win32Api;
namespace MusicRpc.Players;
internal sealed class Tencent : IMusicPlayer
{
    private const string CurrentSongInfoPattern
        = "A2 ? ? ? ? A3 ? ? ? ? C7 05 ? ? ? ? ? ? ? ? A2 ? ? ? ? A3 ? ? ? ? C7 05 ? ? ? ? ? ? ? ? A2 ? ? ? ? A3";
    private const int StdStringSize = 0x18;
    private readonly nint _currentSongInfoAddress;
    private readonly int _pid;
    private readonly ProcessMemory _process;
    public Tencent(int pid)
    {
        _pid = pid;
        var moduleBaseAddress = Utils.ProcessUtils.GetModuleBaseAddress(pid, "QQMusic.dll");
        if (moduleBaseAddress == IntPtr.Zero)
        {
            throw new DllNotFoundException(
                "Could not find QQMusic.dll in the target process. It might not be fully loaded yet.");
        }
        _process = new ProcessMemory(pid);
        if (Memory.FindPattern(CurrentSongInfoPattern, pid, moduleBaseAddress, out var patternAddress))
        {
            var songInfoPointer = _process.ReadInt32(patternAddress, 1);
            _currentSongInfoAddress = songInfoPointer;
        }
        if (_currentSongInfoAddress == 0)
        {
            throw new EntryPointNotFoundException(
                "_currentSongInfoAddress is 0. Pattern might be outdated or process state is invalid.");
        }
    }
    public bool Validate(int pid)
        => _pid == pid;
    public Task<PlayerInfo?> GetPlayerInfoAsync()
    {
        var id = GetSongIdentity();
        if (id == 0)
        {
            return Task.FromResult<PlayerInfo?>(null);
        }
        var playStatus = GetPlayStatus();
        var isPaused = playStatus is 0 or 2;
        return Task.FromResult<PlayerInfo?>(new PlayerInfo
        {
            Identity = id.ToString(),
            Title = GetSongName(),
            Artists = GetArtistName(),
            Album = GetAlbumName(),
            Cover = GetAlbumThumbnailUrl(),
            Schedule = GetSongSchedule() * 0.001,
            Duration = GetSongDuration() * 0.001,
            Pause = isPaused,
            Url = $"https://y.qq.com/n/ryqq/songDetail/{id}",
        });
    }
    private uint GetSongIdentity()
        => _process.ReadUInt32(_currentSongInfoAddress, StdStringSize * 4);
    private int GetSongDuration()
        => _process.ReadInt32(_currentSongInfoAddress, (StdStringSize * 4) + 8 );
    private int GetSongSchedule()
        => _process.ReadInt32(_currentSongInfoAddress, (StdStringSize * 4) + 12);
    private string GetSongName()
        => ReadStdString(_currentSongInfoAddress);
    private string GetArtistName()
        => ReadStdString(_currentSongInfoAddress + StdStringSize);
    private string GetAlbumName()
        => ReadStdString(_currentSongInfoAddress + (StdStringSize * 2));
    private string GetAlbumThumbnailUrl()
        => ReadStdString(_currentSongInfoAddress + (StdStringSize * 3));
    private string ReadStdString(nint address)
    {
        var strLength = _process.ReadInt32(address, 0x10);
        if (strLength == 0)
        {
            return string.Empty;
        } 
        byte[] strBuffer;
        if (strLength <= 15)
        {
            strBuffer = _process.ReadBytes(address, strLength);
        }
        else
        {
            var strAddress = _process.ReadInt32(address);
            strBuffer = _process.ReadBytes(strAddress, strLength);
        }
        return Encoding.UTF8.GetString(strBuffer);
    }
    private int GetPlayStatus()
        => _process.ReadInt32(_currentSongInfoAddress, (StdStringSize * 4) + 16);
}