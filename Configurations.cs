using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace MusicRpc;
internal class ConfigData
{
    public bool AutoStart { get; set; }
    public bool CloseToTray { get; set; } = true;
    public bool StartInTray { get; set; }
    public bool ShowArtistName { get; set; } = true;
    public bool ShowProgressBar { get; set; } = true;
    public bool EnableSteamSync { get; set; } = true;
    public string SteamUsername { get; set; } = "";
    public string SteamRefreshToken { get; set; } = "";
    public string SteamGuardData { get; set; } = "";
    public SteamStatusPriority StatusPriority { get; set; } = SteamStatusPriority.Artist;
    public bool EnableCustomPrefix { get; set; }
    public string CustomPrefix { get; set; } = "";
}
public enum SteamStatusPriority
{
    Artist,
    ProgressBar
}
internal class Configurations
{
    public static readonly Configurations Instance = new();
    private static readonly JsonSerializerOptions SJsonOptions = new() { WriteIndented = true };
    public ConfigData Settings { get; private set; }
    [JsonIgnore] public bool IsFirstLoad { get; }
    [JsonIgnore] private readonly string _path;
    private Configurations()
    {
        Settings = new ConfigData();
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "yySync");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "config.json");
        if (File.Exists(_path))
        {
            IsFirstLoad = false;
            Load();
        }
        else
        {
            IsFirstLoad = true;
            Save();
        }
    }
    public void Save()
    {
        try
        {
            var jsonString = JsonSerializer.Serialize(Settings, SJsonOptions);
            File.WriteAllText(_path, jsonString, Encoding.UTF8);
        }
        catch (Exception e)
        {
            Console.WriteLine($"[ERROR] Failed to save configurations: {e.Message}");
        }
    }
    private void Load()
    {
        try
        {
            var jsonString = File.ReadAllText(_path, Encoding.UTF8);
            var loadedConfig = JsonSerializer.Deserialize<ConfigData>(jsonString);
            if (loadedConfig == null) return;
            Settings = loadedConfig;
        }
        catch (Exception e)
        {
            Debug.WriteLine($"[ERROR] Failed to load configuration, resetting to defaults: {e.Message}");
            Save();
        }
    }
}