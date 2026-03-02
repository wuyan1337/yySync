using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using MusicRpc.Models;
using MusicRpc.Players.Interfaces;
using MusicRpc.Utils;
namespace MusicRpc.Players;
internal sealed class LxMusic : IMusicPlayer
{
    private readonly HttpClient _httpClient;
    private readonly string? _apiBaseUrl;
    private readonly bool _isEnabled;
    private string? _lastSongTitle;
    private string? _lastSongArtist;
    private string _currentSongId = Guid.NewGuid().ToString();
    public LxMusic(int pid)
    {
        _httpClient = HttpClientManager.SharedClient;
        try
        {
            var configPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "lx-music-desktop", "LxDatas", "config_v2.json");
            if (!File.Exists(configPath))
            {
                Debug.WriteLine("[LX Music] Config file not found.");
                _isEnabled = false;
                return;
            }
            var configJson = File.ReadAllText(configPath);
            var config = JsonSerializer.Deserialize<LxMusicConfig>(configJson);
            if (config?.Setting is { Enable: true, Port: not null } settings)
            {
                _apiBaseUrl = $"http://localhost:{settings.Port}";
                _isEnabled = true;
                Debug.WriteLine($"[LX Music] API enabled on port: {settings.Port}");
            }
            else
            {
                _isEnabled = false;
                Debug.WriteLine("[LX Music] OpenAPI is disabled or could not be read from config file.");
                if (config?.Setting != null)
                {
                    Debug.WriteLine(
                        $"[LX Music] Debug Info: Found Enable={config.Setting.Enable}, Port={config.Setting.Port ?? "null"}");
                }
            }
        }
        catch (Exception e)
        {
            Debug.WriteLine($"[ERROR] Failed to initialize LX Music player: {e.Message}");
            _isEnabled = false;
        }
    }
    public async Task<PlayerInfo?> GetPlayerInfoAsync()
    {
        if (!_isEnabled || string.IsNullOrEmpty(_apiBaseUrl)) return null;
        try
        {
            var requestUrl = $"{_apiBaseUrl}/status?filter=status,name,singer,albumName,duration,progress,picUrl";
            var responseJson = await _httpClient.GetStringAsync(requestUrl);
            var status = JsonSerializer.Deserialize<LxMusicStatus>(responseJson);
            if (status is null || string.IsNullOrEmpty(status.Name)) return null;
            var isPaused = status.Status switch
            {
                "playing" => false,
                "paused" => true,
                _ => (bool?)null
            };
            if (isPaused is null) return null;
            if (status.Name != _lastSongTitle || status.Singer != _lastSongArtist)
            {
                _currentSongId = Guid.NewGuid().ToString();
                _lastSongTitle = status.Name;
                _lastSongArtist = status.Singer;
            }
            return new PlayerInfo
            {
                Identity = _currentSongId,
                Title = status.Name,
                Artists = status.Singer,
                Album = status.AlbumName,
                Cover = status.PicUrl,
                Schedule = status.Progress,
                Duration = status.Duration,
                Pause = isPaused.Value,
                Url = string.Empty
            };
        }
        catch (Exception e)
        {
            Debug.WriteLine($"[Lx Music] API request failed: {e.Message}");
            return null;
        }
    }
}
file record LxMusicConfig
{
    [JsonPropertyName("setting")] public LxMusicSetting? Setting { get; init; }
}
file record LxMusicSetting
{
    [JsonPropertyName("openAPI.enable")] public bool Enable { get; init; }
    [JsonPropertyName("openAPI.port")] public string? Port { get; init; }
}
file record LxMusicStatus(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("singer")] string Singer,
    [property: JsonPropertyName("albumName")]
    string AlbumName,
    [property: JsonPropertyName("duration")]
    double Duration,
    [property: JsonPropertyName("progress")]
    double Progress,
    [property: JsonPropertyName("picUrl")] string PicUrl
);