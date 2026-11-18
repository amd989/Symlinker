namespace Symlinker
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    using Microsoft.Win32;

    using Res = Symlinker.Properties.Resources;

    /// <summary>
    /// The "Create link to this" and "Create link here" entries in the Explorer right-click menu.
    /// They are classic shell verbs registered per user under HKCU\Software\Classes, so no
    /// administrator rights are needed. Each entry starts Symlinker with the form prefilled
    /// through the usual command-line options (see <see cref="LaunchOptions"/>).
    /// </summary>
    internal sealed class ExplorerMenu
    {
        /// <summary>Where per-user file associations and shell verbs live, under HKEY_CURRENT_USER.</summary>
        public const string UserClassesPath = @"Software\Classes";

        private const string LinkToVerb = "Symlinker.LinkTo";
        private const string LinkHereVerb = "Symlinker.LinkHere";
        private const string CommandKey = "command";

        private readonly RegistryKey root;
        private readonly string classesPath;
        private readonly string exePath;
        private readonly string linkToLabel;
        private readonly string linkHereLabel;

        /// <param name="root">The registry hive, HKEY_CURRENT_USER in the app.</param>
        /// <param name="classesPath">The classes key under <paramref name="root"/>; tests point it at a throwaway key.</param>
        /// <param name="exePath">The Symlinker.exe the entries start.</param>
        /// <param name="linkToLabel">Menu text on files and folders.</param>
        /// <param name="linkHereLabel">Menu text on folders and a folder's background.</param>
        public ExplorerMenu(RegistryKey root, string classesPath, string exePath, string linkToLabel, string linkHereLabel)
        {
            this.root = root;
            this.classesPath = classesPath;
            this.exePath = exePath;
            this.linkToLabel = linkToLabel;
            this.linkHereLabel = linkHereLabel;
        }

        /// <summary>The entries for the current user, starting this copy of Symlinker, in the current UI language.</summary>
        public static ExplorerMenu ForCurrentUser(string exePath) =>
            new(Registry.CurrentUser, UserClassesPath, exePath, Res.ExplorerMenuLinkTo, Res.ExplorerMenuLinkHere);

        /// <summary>Whether any of the entries is registered.</summary>
        public bool IsEnabled => Entries.Any(entry => Exists(entry.KeyPath));

        /// <summary>
        /// Whether every entry is registered exactly as this copy would write it: same exe,
        /// same arguments, same menu text.
        /// </summary>
        public bool IsCurrent => Entries.All(IsWrittenAs);

        /// <summary>
        /// One registry key per menu entry. The path goes last on each command line: Explorer
        /// passes a drive root as "C:\", and the backslash before the closing quote would
        /// otherwise swallow the arguments after it (see <see cref="LaunchOptions.Parse"/>).
        /// </summary>
        internal IReadOnlyList<Entry> Entries => new[]
        {
            new Entry(@"*\shell\" + LinkToVerb, linkToLabel, Command("--mode file --target \"%1\"")),
            new Entry(@"Directory\shell\" + LinkToVerb, linkToLabel, Command("--mode folder --target \"%1\"")),
            new Entry(@"Directory\shell\" + LinkHereVerb, linkHereLabel, Command("--location \"%1\"")),
            new Entry(@"Directory\Background\shell\" + LinkHereVerb, linkHereLabel, Command("--location \"%V\"")),
        };

        /// <summary>Adds the entries, or rewrites them in place.</summary>
        public void Enable()
        {
            foreach (var entry in Entries)
            {
                using var key = root.CreateSubKey(FullPath(entry.KeyPath));
                key.SetValue("MUIVerb", entry.Label);
                key.SetValue("Icon", Quote(exePath) + ",0");

                // One window per right-click; with several items selected the entry is hidden
                key.SetValue("MultiSelectModel", "Single");

                using var command = key.CreateSubKey(CommandKey);
                command.SetValue(string.Empty, entry.Command);
            }
        }

        /// <summary>Removes the entries. Entries that are already gone are skipped.</summary>
        public void Disable()
        {
            foreach (var entry in Entries)
                root.DeleteSubKeyTree(FullPath(entry.KeyPath), throwOnMissingSubKey: false);
        }

        /// <summary>Adds or removes the entries. Returns null when it worked, else the error message.</summary>
        public string TrySetEnabled(bool enabled)
        {
            try
            {
                if (enabled)
                    Enable();
                else
                    Disable();

                return null;
            }
            catch (Exception exception)
            {
                // Access denied by policy, or the key is locked
                return exception.Message;
            }
        }

        /// <summary>
        /// When the entries are on but point at another exe (ClickOnce installs every update in a
        /// new folder) or use other menu text (the Windows language changed), rewrites them for
        /// this copy. Returns true when it rewrote them. Never throws: a failure here must not
        /// stop the app from starting.
        /// </summary>
        public bool Refresh()
        {
            try
            {
                if (!IsEnabled || IsCurrent)
                    return false;

                Enable();
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static string Quote(string value) => "\"" + value + "\"";

        private string Command(string arguments) => Quote(exePath) + " " + arguments;

        private string FullPath(string keyPath) => classesPath + "\\" + keyPath;

        private bool Exists(string keyPath)
        {
            using var key = root.OpenSubKey(FullPath(keyPath));
            return key != null;
        }

        private bool IsWrittenAs(Entry entry)
        {
            using var key = root.OpenSubKey(FullPath(entry.KeyPath));
            using var command = key?.OpenSubKey(CommandKey);
            return command != null
                && Equals(key.GetValue("MUIVerb"), entry.Label)
                && Equals(command.GetValue(string.Empty), entry.Command);
        }

        /// <summary>One menu entry: its key under the classes key, its text and the command it runs.</summary>
        internal sealed record Entry(string KeyPath, string Label, string Command);
    }
}
