namespace Symlinker
{
    using System;
    using System.IO;
    using System.Runtime.InteropServices;
    using System.Security.Principal;
    using System.Windows;
    using System.Windows.Interop;
    using System.Windows.Media;
    using System.Windows.Media.Imaging;

    /// <summary>Values the window starts with, read from the command line.</summary>
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
    }

    /// <summary>
    /// Administrator rights helpers. Symlinker runs unelevated and only elevates the link
    /// creation step when needed (see <see cref="ElevatedLinker"/>).
    /// </summary>
    internal static class Elevation
    {
        private const int ErrorPrivilegeNotHeld = 1314;

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
