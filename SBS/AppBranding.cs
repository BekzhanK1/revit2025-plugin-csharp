using System;

namespace SmartRemont.ExportRooms
{
    public static class AppBranding
    {
        public const string ProductName = "Smart Remont";
        const string TitleSeparator = " — ";

        public static string DisplayName =>
            Configs.IsTestApi ? $"{ProductName} · ТЕСТ" : ProductName;

        public static string FormatTitle(string section = null)
        {
            if (string.IsNullOrWhiteSpace(section))
                return DisplayName;

            return $"{DisplayName}{TitleSeparator}{section.Trim()}";
        }

        public static string FormatLoginTitle(string pluginVersion)
        {
            var version = string.IsNullOrWhiteSpace(pluginVersion) ? string.Empty : $" ({pluginVersion.Trim()})";
            return $"Вход — {DisplayName}{version}";
        }

        public static string NormalizeWindowTitle(string currentTitle)
        {
            var title = (currentTitle ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(title))
                return DisplayName;

            if (title.StartsWith("Вход — ", StringComparison.Ordinal))
            {
                var versionStart = title.LastIndexOf('(');
                if (versionStart > 0 && title.EndsWith(")", StringComparison.Ordinal))
                {
                    var version = title.Substring(versionStart + 1, title.Length - versionStart - 2);
                    return FormatLoginTitle(version);
                }

                return FormatLoginTitle(null);
            }

            var section = ExtractSection(title);
            return FormatTitle(section);
        }

        static string ExtractSection(string title)
        {
            var idx = title.IndexOf(TitleSeparator, StringComparison.Ordinal);
            if (idx < 0)
                return title == ProductName || title.StartsWith(ProductName + " ·", StringComparison.Ordinal)
                    ? null
                    : title;

            return title.Substring(idx + TitleSeparator.Length).Trim();
        }
    }
}
