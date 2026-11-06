namespace Symlinker
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Diagnostics;
    using System.IO;
    using System.Runtime.InteropServices;
    using System.Security.Principal;
    using System.Text;
    using System.Windows;
    using System.Windows.Interop;
    using System.Windows.Media;
    using System.Windows.Media.Imaging;

    /// <summary>
    /// Values the window starts with. Used to carry the form over when restarting as administrator.
    /// </summary>
    internal sealed class LaunchOptions
    {
        private const string ModeArg = "--mode";
        private const string TypeArg = "--type";
        private const string LocationArg = "--location";
        private const string NameArg = "--name";
        private const string TargetArg = "--target";

        /// <summary>"folder" or "file".</summary>
        public string Mode { get; set; }

        /// <summary>"symbolic", "hard" or "junction".</summary>
        public string LinkType { get; set; }

        public string LinkLocation { get; set; }

        public string LinkName { get; set; }

        public string Target { get; set; }

        /// <summary>Reads options from the command line. Unknown arguments (like the old --engage) are ignored.</summary>
        public static LaunchOptions Parse(string[] args)
        {
            var options = new LaunchOptions();
            for (var i = 0; i < args.Length - 1; i++)
            {
                var value = args[i + 1];
                switch (args[i])
                {
                    case ModeArg: options.Mode = value; break;
                    case TypeArg: options.LinkType = value; break;
                    case LocationArg: options.LinkLocation = value; break;
                    case NameArg: options.LinkName = value; break;
                    case TargetArg: options.Target = value; break;
                    default: continue;
                }

                i++;
            }

            return options;
        }

        /// <summary>Builds a command line that <see cref="Parse"/> turns back into these options.</summary>
        public string ToArguments()
        {
            var parts = new List<string>();
            void Add(string name, string value)
            {
                if (!string.IsNullOrEmpty(value))
                {
                    parts.Add(name);
                    parts.Add(QuoteArgument(value));
                }
            }

            Add(ModeArg, Mode);
            Add(TypeArg, LinkType);
            Add(LocationArg, LinkLocation);
            Add(NameArg, LinkName);
            Add(TargetArg, Target);
            return string.Join(" ", parts);
        }

        /// <summary>
        /// Quotes one argument so Windows splits it back exactly as given, including spaces
        /// and trailing backslashes (like "C:\").
        /// </summary>
        internal static string QuoteArgument(string value)
        {
            if (value.Length != 0 && value.IndexOfAny(new[] { ' ', '\t', '\n', '\v', '"' }) < 0)
                return value;

            var quoted = new StringBuilder("\"");
            var backslashes = 0;
            foreach (var c in value)
            {
                if (c == '\\')
                {
                    backslashes++;
                    continue;
                }

                // Backslashes only need escaping when a quote follows them
                quoted.Append('\\', c == '"' ? (backslashes * 2) + 1 : backslashes);
                quoted.Append(c);
                backslashes = 0;
            }

            quoted.Append('\\', backslashes * 2);
            quoted.Append('"');
            return quoted.ToString();
        }
    }

    /// <summary>Administrator rights helpers. Symlinker runs unelevated and only asks for them when needed.</summary>
    internal static class Elevation
    {
        private const int ErrorPrivilegeNotHeld = 1314;
        private const int ErrorCancelled = 1223;

        public static bool IsElevated
        {
            get
            {
                using var identity = WindowsIdentity.GetCurrent();
                return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            }
        }

        /// <summary>
        /// Whether this process can create symbolic links: either it runs as administrator,
        /// or Windows Developer Mode allows it without elevation.
        /// Junctions and hard links never need this.
        /// </summary>
        public static bool CanCreateSymbolicLinks()
        {
            if (IsElevated)
                return true;

            // Try for real: a throwaway link in the temp folder, pointing nowhere
            var probe = Path.Combine(Path.GetTempPath(), "symlinker-probe-" + Guid.NewGuid().ToString("N"));
            try
            {
                File.CreateSymbolicLink(probe, probe + "-target");
                File.Delete(probe);
                return true;
            }
            catch (IOException exception) when ((exception.HResult & 0xFFFF) == ErrorPrivilegeNotHeld)
            {
                return false;
            }
            catch (Exception)
            {
                // Something else went wrong with the probe. Let mklink try and report its own error.
                return true;
            }
        }

        /// <summary>
        /// Starts a new elevated instance with the given options.
        /// Returns false when the user declines the UAC prompt.
        /// </summary>
        public static bool RestartElevated(LaunchOptions options)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = Environment.ProcessPath,
                Arguments = options.ToArguments(),
                UseShellExecute = true,
                Verb = "runas",
            };

            try
            {
                Process.Start(startInfo)?.Dispose();
                return true;
            }
            catch (Win32Exception exception) when (exception.NativeErrorCode == ErrorCancelled)
            {
                return false;
            }
        }

        /// <summary>
        /// The Windows UAC shield (small size), shown on actions that need administrator rights.
        /// Returns null if Windows doesn't provide it.
        /// </summary>
        public static ImageSource GetShieldIcon()
        {
            var info = new StockIconInfo { Size = (uint)Marshal.SizeOf<StockIconInfo>() };
            if (SHGetStockIconInfo(StockIconShield, StockIconGetIcon | StockIconSmall, ref info) != 0 || info.Icon == IntPtr.Zero)
                return null;

            try
            {
                var image = Imaging.CreateBitmapSourceFromHIcon(info.Icon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                image.Freeze();
                return image;
            }
            finally
            {
                DestroyIcon(info.Icon);
            }
        }

        private const uint StockIconShield = 77;
        private const uint StockIconGetIcon = 0x100;
        private const uint StockIconSmall = 0x1;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct StockIconInfo
        {
            public uint Size;
            public IntPtr Icon;
            public int SystemImageIndex;
            public int IconIndex;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string IconPath;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHGetStockIconInfo(uint stockIconId, uint flags, ref StockIconInfo info);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DestroyIcon(IntPtr icon);
    }
}
