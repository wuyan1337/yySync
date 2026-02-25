using System;
using System.Diagnostics;
using System.IO;
namespace MusicRpc.Utils;
internal static class Logger
{
    private static readonly string LogFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "yySync", "yySync.log");
    private static readonly object Lock = new();
    static Logger()
    {
        try
        {
            var fi = new FileInfo(LogFilePath);
            if (fi.Exists && fi.Length > 2 * 1024 * 1024)
            {
                fi.Delete();
            }
        }
        catch { }
    }
    private static void WriteToFile(string level, string message)
    {
        try
        {
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] {message}";
            System.Diagnostics.Debug.WriteLine(line);
            lock (Lock)
            {
                File.AppendAllText(LogFilePath, line + Environment.NewLine);
            }
        }
        catch { }
    }
    public static void Steam(string message) => WriteToFile("STEAM", message);
    public static void Debug(string message) => WriteToFile("DEBUG", message);
    public static void Diagnose(string message) => WriteToFile("DIAGNOSE", message);
    public static void Error(string message) => WriteToFile("ERROR", message);
    public static void Info(string message) => WriteToFile("INFO", message);
    public static void Memory(string message) => WriteToFile("MEMORY", message);
}