using System.Linq;
using System.Reflection;

namespace SmartRemont.ExportRooms
{
    public static class PluginVersion
    {
        public static string GetCurrent()
        {
            var assembly = Assembly.GetExecutingAssembly();
            var informational = assembly
                .GetCustomAttributes(typeof(AssemblyInformationalVersionAttribute), false)
                .OfType<AssemblyInformationalVersionAttribute>()
                .FirstOrDefault()
                ?.InformationalVersion;
            if (!string.IsNullOrWhiteSpace(informational))
                return informational.Trim();

            var version = assembly.GetName().Version;
            return version?.ToString() ?? "unknown";
        }
    }
}
