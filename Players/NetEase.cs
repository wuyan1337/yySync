using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using MusicRpc.Models;
using MusicRpc.Players.Interfaces;
using MusicRpc.Utils;
using MusicRpc.Win32Api;
namespace MusicRpc.Players;
internal sealed class NetEase : IMusicPlayer
{
    private readonly ProcessMemory _process;
    private readonly nint _audioPlayerPointer;
    private readonly nint _schedulePointer;
    private readonly string _playlistPath; 
    private readonly string _fmPlayPath; 
    private NetEasePlaylist? _cachedPlaylist; 
    private DateTime _lastPlaylistWriteTime; 
    private string? _cachedPlaylistHash; 
    private NetEaseFmPlaylist? _cachedFmPlaylist;
    private DateTime _lastFmPlayWriteTime;
    private string? _cachedFmPlayHash;
    private double _lastSchedule;
    private string? _lastIdentity;
    private DateTime _lastScheduleUpdateTime;
    private bool _isPausedByHeuristic;
    private bool _hasScheduleChanged;
    private const string AudioPlayerPattern
        = "48 8D 0D ? ? ? ? E8 ? ? ? ? 48 8D 0D ? ? ? ? E8 ? ? ? ? 90 48 8D 0D ? ? ? ? E8 ? ? ? ? 48 8D 05 ? ? ? ? 48 8D A5 ? ? ? ? 5F 5D C3 CC CC CC CC CC 48 89 4C 24 ? 55 57 48 81 EC ? ? ? ? 48 8D 6C 24 ? 48 8D 7C 24";
    private const string AudioSchedulePattern = "66 0F 2E 0D ? ? ? ? 7A ? 75 ? 66 0F 2E 15";
    private readonly nint _cloudMusicDllBase;
    private readonly string _clientVersion;
    private readonly bool _isLegacyMemoryMode;
    public NetEase(int pid)
    {
        var fileDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NetEase", "CloudMusic", "WebData", "file");
        _playlistPath = Path.Combine(fileDirectory, "playingList");
        _fmPlayPath = Path.Combine(fileDirectory, "fmPlay");
        _lastPlaylistWriteTime = DateTime.MinValue;
        _lastFmPlayWriteTime = DateTime.MinValue;
        var moduleBaseAddress = ProcessUtils.GetModuleBaseAddress(pid, "cloudmusic.dll");
        if (moduleBaseAddress == IntPtr.Zero)
        {
            throw new DllNotFoundException("Could not find cloudmusic.dll in the target process.");
        }
        _cloudMusicDllBase = moduleBaseAddress;
        _process = new ProcessMemory(pid);
        _clientVersion = GetClientVersion(pid);
        if (Memory.FindPattern(AudioPlayerPattern, pid, moduleBaseAddress, out var app))
        {
            var textAddress = nint.Add(app, 3);
            var displacement = _process.ReadInt32(textAddress);
            _audioPlayerPointer = textAddress + displacement + sizeof(int);
        }
        if (Memory.FindPattern(AudioSchedulePattern, pid, moduleBaseAddress, out var asp))
        {
            var textAddress = nint.Add(asp, 4);
            var displacement = _process.ReadInt32(textAddress);
            _schedulePointer = textAddress + displacement + sizeof(int);
        }
        if (_audioPlayerPointer == nint.Zero || _schedulePointer == nint.Zero)
        {
            _isLegacyMemoryMode = true;
            Debug.WriteLine($"[NetEase] Memory pattern mismatch for version {_clientVersion}. Using Target Version Memory Mode.");
        }
    }
    private string GetClientVersion(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            var versionInfo = FileVersionInfo.GetVersionInfo(process.MainModule!.FileName!);
            return $"{versionInfo.FileMajorPart}.{versionInfo.FileMinorPart}.{versionInfo.FileBuildPart}";
        }
        catch
        {
            return "Unknown";
        }
    }
    public Task<PlayerInfo?> GetPlayerInfoAsync()
    {
        PlayerInfo? info;
        if (_isLegacyMemoryMode)
        {
            info = GetLegacyWindowPlayerInfo();
        }
        else
        {
            var identity = GetCurrentSongId();
            if (string.IsNullOrEmpty(identity)) return Task.FromResult<PlayerInfo?>(null);
            var status = GetPlayerStatus();
            var schedule = GetSchedule();
            var playerInfo = UpdateAndSearchPlaylist(identity, status);
            playerInfo ??= UpdateAndSearchFmPlaylist(identity, status);
            if (playerInfo != null)
            {
                var duration = playerInfo.Value.Duration;
                if (duration <= 0)
                {
                    duration = GetSongDuration();
                }
                info = playerInfo.Value with 
                { 
                    Schedule = schedule,
                    Duration = duration
                };
            }
            else
            {
                info = null;
            }
        }
        return Task.FromResult<PlayerInfo?>(info != null ? ApplyHeuristicPause(info.Value) : null);
    }
    private PlayerInfo ApplyHeuristicPause(PlayerInfo info)
    {
        if (info.Identity == _lastIdentity)
        {
            if (Math.Abs(info.Schedule - _lastSchedule) > 0.01)
            {
                _lastSchedule = info.Schedule;
                _lastScheduleUpdateTime = DateTime.Now;
                _isPausedByHeuristic = false;
                _hasScheduleChanged = true;
            }
            else if (_hasScheduleChanged && (DateTime.Now - _lastScheduleUpdateTime).TotalSeconds > 1.5)
            {
                _isPausedByHeuristic = true;
            }
        }
        else
        {
            _lastIdentity = info.Identity;
            _lastSchedule = info.Schedule;
            _lastScheduleUpdateTime = DateTime.Now;
            _isPausedByHeuristic = false;
            _hasScheduleChanged = false;
        }
        return info.Pause || _isPausedByHeuristic ? info with { Pause = true } : info;
    }
    private PlayerInfo? GetLegacyWindowPlayerInfo()
    {
        try
        {
            using var process = Process.GetProcessById(_process.ProcessId);
            var title = process.MainWindowTitle;
            if (string.IsNullOrEmpty(title) || title == "网易云音乐" || title == "NetEase Cloud Music")
            {
                return null; 
            }
            var parts = title.Split(" - ");
            var song = parts.Length > 0 ? parts[0] : title;
            var artist = parts.Length > 1 ? parts[1] : "Unknown Artist";
            return new PlayerInfo
            {
                Identity = "legacy_" + title.GetHashCode(),
                Title = song,
                Artists = artist,
                Album = "NetEase Cloud Music",
                Cover = "",
                Duration = 0,
                Schedule = 0,
                Pause = false,
                Url = "" 
            };
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[NetEase] Legacy window mode error: {ex.Message}");
            return null;
        }
    }
    private PlayerInfo? UpdateAndSearchPlaylist(string identity, PlayStatus status)
    {
        try
        {
            if (!File.Exists(_playlistPath))
            {
                _cachedPlaylist = null;
                return null;
            }
            var currentWriteTime = File.GetLastWriteTimeUtc(_playlistPath);
            if (currentWriteTime != _lastPlaylistWriteTime || _cachedPlaylist is null)
            {
                var fileBytes = File.ReadAllBytes(_playlistPath);
                if (TryGetNormalizedPlaylistContent(fileBytes, out var normalizedJson, out var newHash))
                {
                    if (newHash != _cachedPlaylistHash || _cachedPlaylist is null)
                    {
                        Debug.WriteLine("[NetEase] Playlist content changed. Deserializing new playlist.");
                        _cachedPlaylist = JsonSerializer.Deserialize<NetEasePlaylist>(normalizedJson);
                        _cachedPlaylistHash = newHash;
                    }
                }
                else
                {
                    _cachedPlaylist = null;
                }
                _lastPlaylistWriteTime = currentWriteTime;
            }
            var currentTrackItem = _cachedPlaylist?.List.Find(x => x.Identity == identity);
            if (currentTrackItem is not { Track: { } track })
            {
                return null;
            }
            return new PlayerInfo
            {
                Identity = identity,
                Title = track.Name,
                Artists = string.Join(',', track.Artists.Select(x => x.Singer)),
                Album = track.Album.Name,
                Cover = track.Album.Cover,
                Duration = track.Duration / 1000.0,
                Schedule = GetSchedule(),
                Pause = status == PlayStatus.Paused,
                Url = $"https://music.163.com/#/song?id={identity}",
            };
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ERROR] Failed to process NetEase playlist: {ex.Message}");
            _cachedPlaylist = null;
            return null;
        }
    }
    private PlayerInfo? UpdateAndSearchFmPlaylist(string identity, PlayStatus status)
    {
        try
        {
            if (!File.Exists(_fmPlayPath))
            {
                _cachedFmPlaylist = null;
                return null;
            }
            var currentWriteTime = File.GetLastWriteTimeUtc(_fmPlayPath);
            if (currentWriteTime != _lastFmPlayWriteTime || _cachedFmPlaylist is null)
            {
                var fileBytes = File.ReadAllBytes(_fmPlayPath);
                if (TryGetNormalizedFmContent(fileBytes, out var normalizedJson, out var newHash))
                {
                    if (newHash != _cachedFmPlayHash || _cachedFmPlaylist is null)
                    {
                        Debug.WriteLine("[NetEase] FM Playlist content changed. Deserializing new FM playlist.");
                        _cachedFmPlaylist = JsonSerializer.Deserialize<NetEaseFmPlaylist>(normalizedJson);
                        _cachedFmPlayHash = newHash;
                    }
                }
                else
                {
                    _cachedFmPlaylist = null;
                }
                _lastFmPlayWriteTime = currentWriteTime;
            }
            var currentTrackItem = _cachedFmPlaylist?.Queue.Find(x => x.Identity == identity);
            if (currentTrackItem is null)
            {
                return null;
            }
            return new PlayerInfo
            {
                Identity = identity,
                Title = currentTrackItem.Name,
                Artists = string.Join(',', currentTrackItem.Artists.Select(x => x.Singer)),
                Album = currentTrackItem.Album.Name,
                Cover = currentTrackItem.Album.Cover,
                Duration = currentTrackItem.Duration / 1000.0,
                Schedule = GetSchedule(),
                Pause = status == PlayStatus.Paused,
                Url = $"https://music.163.com/#/song?id={identity}",
            };
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ERROR] Failed to process NetEase FM playlist: {ex.Message}");
            _cachedFmPlaylist = null;
            return null;
        }
    }
    private static bool TryGetNormalizedPlaylistContent(byte[] fileBytes, out string normalizedJson, out string newHash)
    {
        normalizedJson = string.Empty;
        newHash = string.Empty;
        try
        {
            var rootNode = JsonNode.Parse(fileBytes);
            if (rootNode is not JsonObject rootObj || !rootObj.ContainsKey("list") || rootObj["list"] is not JsonArray)
            {
                return false;
            }
            var listArray = rootNode["list"]!.AsArray();
            var clonedArray = JsonNode.Parse(listArray.ToJsonString())!.AsArray();
            foreach (var item in clonedArray)
            {
                if (item is not JsonObject songObject) continue;
                songObject.Remove("randomOrder");
                songObject.Remove("privilege");
                songObject.Remove("referInfo");
                songObject.Remove("fromInfo");
                if (songObject.TryGetPropertyValue("track", out var trackNode) && trackNode is JsonObject trackObj)
                {
                    trackObj.Remove("privilege");
                }
            }
            var newRoot = new JsonObject { ["list"] = clonedArray };
            normalizedJson = newRoot.ToJsonString();
            var hashBytes = MD5.HashData(Encoding.UTF8.GetBytes(normalizedJson));
            newHash = Convert.ToBase64String(hashBytes);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
    private static bool TryGetNormalizedFmContent(byte[] fileBytes, out string normalizedJson, out string newHash)
    {
        normalizedJson = string.Empty;
        newHash = string.Empty;
        try
        {
            var rootNode = JsonNode.Parse(fileBytes);
            if (rootNode is not JsonObject rootObj || !rootObj.ContainsKey("queue") ||
                rootObj["queue"] is not JsonArray)
            {
                return false;
            }
            var listArray = rootNode["queue"]!.AsArray();
            var clonedArray = JsonNode.Parse(listArray.ToJsonString())!.AsArray();
            foreach (var item in clonedArray)
            {
                if (item is not JsonObject songObject) continue;
                songObject.Remove("privilege");
                songObject.Remove("alg");
                songObject.Remove("score");
            }
            var newRoot = new JsonObject { ["queue"] = clonedArray };
            normalizedJson = newRoot.ToJsonString();
            var hashBytes = MD5.HashData(Encoding.UTF8.GetBytes(normalizedJson));
            newHash = Convert.ToBase64String(hashBytes);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
    private enum PlayStatus
    {
        Waiting,
        Playing,
        Paused,
        Unknown3,
        Unknown4,
    }
    private double GetSchedule()
        => _process.ReadDouble(_schedulePointer);
    private PlayStatus GetPlayerStatus()
        => (PlayStatus)_process.ReadInt32(_audioPlayerPointer, 0x60);
    private float GetPlayerVolume()
        => _process.ReadFloat(_audioPlayerPointer, 0x64);
    private float GetCurrentVolume()
        => _process.ReadFloat(_audioPlayerPointer, 0x68);
    private double GetSongDuration()
        => _process.ReadDouble(_audioPlayerPointer, 0xa8);
    private string GetCurrentSongId()
    {
        var audioPlayInfo = _process.ReadInt64(_audioPlayerPointer, 0x50);
        if (audioPlayInfo == 0)
        {
            return string.Empty;
        }
        var strPtr = audioPlayInfo + 0x10;
        var strLength = _process.ReadInt64((nint)strPtr, 0x10);
        byte[] strBuffer;
        if (strLength <= 15)
        {
            strBuffer = _process.ReadBytes((nint)strPtr, (int)strLength);
        }
        else
        {
            var strAddress = _process.ReadInt64((nint)strPtr);
            if (strAddress == 0) return string.Empty;
            strBuffer = _process.ReadBytes((nint)strAddress, (int)strLength);
        }
        var str = Encoding.UTF8.GetString(strBuffer);
        if (string.IsNullOrEmpty(str) || str.Any(c => char.IsControl(c) && c != '\0'))
        {
            str = Encoding.Unicode.GetString(strBuffer);
        }
        if (string.IsNullOrEmpty(str)) return string.Empty;
        var separatorIndex = str.IndexOf('_');
        return separatorIndex > 0 ? str[..separatorIndex] : str.Replace("\0", "");
    }
}
internal class FlexibleDurationConverter : JsonConverter<double>
{
    public override double Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number)
        {
            return reader.GetDouble();
        }
        if (reader.TokenType == JsonTokenType.String && double.TryParse(reader.GetString(), out var result))
        {
            return result;
        }
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        if (root.TryGetProperty("dt", out var dtProp) && dtProp.ValueKind == JsonValueKind.Number)
        {
            return dtProp.GetDouble();
        }
        return 0;
    }
    public override void Write(Utf8JsonWriter writer, double value, JsonSerializerOptions options)
    {
        writer.WriteNumberValue(value);
    }
}
internal record NetEasePlaylistTrackArtist([property: JsonPropertyName("name")] string Singer);
internal record NetEasePlaylistTrackAlbum(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("cover")] string Cover);
internal record NetEasePlaylistTrack(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("artists")]
    NetEasePlaylistTrackArtist[] Artists,
    [property: JsonPropertyName("album")] NetEasePlaylistTrackAlbum Album,
    [property: JsonPropertyName("duration"), JsonConverter(typeof(FlexibleDurationConverter))] double Duration);
internal record NetEasePlaylistItem(
    [property: JsonPropertyName("id")] string Identity,
    [property: JsonPropertyName("track")] NetEasePlaylistTrack Track);
internal record NetEasePlaylist([property: JsonPropertyName("list")] List<NetEasePlaylistItem> List);
internal record NetEaseFmPlaylist(
    [property: JsonPropertyName("queue")] List<NetEaseFmPlaylistItem> Queue
);
internal record NetEaseFmPlaylistItem(
    [property: JsonPropertyName("id")] string Identity,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("artists")]
    NetEasePlaylistTrackArtist[] Artists,
    [property: JsonPropertyName("album")] NetEasePlaylistTrackAlbum Album,
    [property: JsonPropertyName("duration"), JsonConverter(typeof(FlexibleDurationConverter))] double Duration
);