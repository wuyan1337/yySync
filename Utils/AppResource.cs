using System.Drawing;
using System.Reflection;
namespace MusicRpc.Utils;
internal static class AppResource
{
    public static Icon Icon { get; }
    static AppResource()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            const string resourceName = "MusicRpc.Resources.icon.ico";
            using var stream = assembly.GetManifestResourceStream(resourceName);
            Icon = stream != null ? new Icon(stream) : SystemIcons.Application;
        }
        catch
        {
            Icon = SystemIcons.Application;
        }
    }
}