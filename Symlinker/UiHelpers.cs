namespace Symlinker
{
    using System;
    using System.Diagnostics;
    using System.Globalization;
    using System.IO;

    /// <summary>Small helpers behind the window's text boxes and pickers.</summary>
    internal static class UiHelpers
    {
        /// <summary>The last part of a path (the file or folder name), ignoring trailing separators.</summary>
        public static string GetName(string path)
        {
            var trimmed = path.TrimEnd('\\', '/');
            var lastSep = trimmed.LastIndexOfAny(new[] { '\\', '/' });
            return lastSep >= 0 ? trimmed[(lastSep + 1)..] : trimmed;
        }

        /// <summary>
        /// Returns an existing directory to open a picker in, based on a path typed by the user.
        /// With <paramref name="useParent"/>, returns the folder that contains the path instead.
        /// Returns null when nothing usable exists.
        /// </summary>
        public static string GetExistingDirectory(string path, bool useParent)
        {
            try
            {
                path = path.Trim();
                if (string.IsNullOrEmpty(path))
                    return null;

                var full = Path.GetFullPath(path);
                var candidate = useParent ? Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(full)) : full;

                // Fall back to the path itself when it has no parent (e.g. a drive root)
                candidate ??= full;
                return Directory.Exists(candidate) ? candidate : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>A byte count in the largest unit that keeps it at or above 1, like "1.5 MB".</summary>
        public static string FormatBytes(long bytes)
        {
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double size = bytes;
            var unit = 0;
            while (size >= 1024 && unit < units.Length - 1)
            {
                size /= 1024;
                unit++;
            }

            return string.Format(CultureInfo.CurrentUICulture, unit == 0 ? "{0:0} {1}" : "{0:0.0} {1}", size, units[unit]);
        }

        /// <summary>The version shown in the About dialog: the ClickOnce version when installed, else the file version.</summary>
        public static string GetAppVersion()
        {
            var version = Environment.GetEnvironmentVariable("ClickOnce_CurrentVersion");
            if (string.IsNullOrEmpty(version))
                version = FileVersionInfo.GetVersionInfo(typeof(UiHelpers).Assembly.Location).FileVersion;
            return version;
        }
    }

    /// <summary>
    /// Keeps the link name in sync with the target's name until the user types their own.
    /// </summary>
    internal sealed class LinkNameAutoFill
    {
        // Last link name filled in from the target, so user-typed names are never overwritten
        private string lastFilled = string.Empty;

        /// <summary>
        /// Returns the link name to show for a new target name, or null to keep
        /// <paramref name="currentName"/> because the user typed it.
        /// </summary>
        public string Update(string currentName, string targetName)
        {
            if (currentName.Length != 0 && currentName != lastFilled)
                return null;

            // Skip names that can't be used as a link name, like a drive root ("D:")
            if (targetName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                targetName = string.Empty;

            lastFilled = targetName;
            return targetName;
        }
    }
}
