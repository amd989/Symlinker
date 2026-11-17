namespace Symlinker.Tests
{
    using System;
    using System.Linq;

    using Microsoft.Win32;

    /// <summary>
    /// Registers the Explorer entries under a throwaway HKCU\Software\SymlinkerTests\... key
    /// instead of HKCU\Software\Classes, so Explorer never sees them.
    /// </summary>
    public sealed class ExplorerMenuTests : IDisposable
    {
        private const string ExePath = @"C:\Program Files\Symlinker\Symlinker.exe";
        private const string TestsKey = @"Software\SymlinkerTests";

        private readonly string classesPath = TestsKey + @"\" + Guid.NewGuid().ToString("N");

        public void Dispose()
        {
            Registry.CurrentUser.DeleteSubKeyTree(classesPath, throwOnMissingSubKey: false);

            // Remove the parent too once the last test is done with it
            try
            {
                Registry.CurrentUser.DeleteSubKey(TestsKey, throwOnMissingSubKey: false);
            }
            catch (InvalidOperationException)
            {
                // Another test still has its key in there
            }
        }

        [Fact]
        public void IsOff_ByDefault()
        {
            var menu = Menu();

            Assert.False(menu.IsEnabled);
            Assert.False(menu.IsCurrent);
        }

        [Fact]
        public void Enable_WritesEveryEntry()
        {
            var menu = Menu();

            menu.Enable();

            Assert.True(menu.IsEnabled);
            Assert.True(menu.IsCurrent);

            using var file = Open(@"*\shell\Symlinker.LinkTo");
            Assert.Equal("Link to", file.GetValue("MUIVerb"));
            Assert.Equal($"\"{ExePath}\",0", file.GetValue("Icon"));
            Assert.Equal("Single", file.GetValue("MultiSelectModel"));
            Assert.Equal($"\"{ExePath}\" --mode file --target \"%1\"", Command(@"*\shell\Symlinker.LinkTo"));

            using var folder = Open(@"Directory\shell\Symlinker.LinkTo");
            Assert.Equal("Link to", folder.GetValue("MUIVerb"));
            Assert.Equal($"\"{ExePath}\" --mode folder --target \"%1\"", Command(@"Directory\shell\Symlinker.LinkTo"));

            using var here = Open(@"Directory\shell\Symlinker.LinkHere");
            Assert.Equal("Link here", here.GetValue("MUIVerb"));
            Assert.Equal($"\"{ExePath}\" --location \"%1\"", Command(@"Directory\shell\Symlinker.LinkHere"));

            using var background = Open(@"Directory\Background\shell\Symlinker.LinkHere");
            Assert.Equal("Link here", background.GetValue("MUIVerb"));
            Assert.Equal($"\"{ExePath}\" --location \"%V\"", Command(@"Directory\Background\shell\Symlinker.LinkHere"));
        }

        [Fact]
        public void Commands_ReachTheWindowAsLaunchOptions()
        {
            // What Windows passes to Main for each command, with Explorer's placeholders filled in
            var entries = Menu().Entries;
            var toFile = Arguments(entries[0].Command.Replace("%1", @"D:\My Data\notes.txt"));
            var toFolder = Arguments(entries[1].Command.Replace("%1", @"D:\My Data"));
            var hereOnRoot = Arguments(entries[3].Command.Replace("%V", @"C:\"));

            Assert.Equal(false, LaunchOptions.Parse(toFile).IsFolderMode);
            Assert.Equal(@"D:\My Data\notes.txt", LaunchOptions.Parse(toFile).Target);
            Assert.Equal(true, LaunchOptions.Parse(toFolder).IsFolderMode);
            Assert.Equal(@"D:\My Data", LaunchOptions.Parse(toFolder).Target);
            Assert.Equal(@"C:\", LaunchOptions.Parse(hereOnRoot).LinkLocation);
        }

        [Fact]
        public void Disable_RemovesEveryEntry_AndLeavesOtherVerbs()
        {
            using (var other = Registry.CurrentUser.CreateSubKey(classesPath + @"\Directory\shell\SomethingElse"))
                other.SetValue("MUIVerb", "Other app");

            var menu = Menu();
            menu.Enable();
            menu.Disable();

            Assert.False(menu.IsEnabled);
            Assert.Null(Open(@"*\shell\Symlinker.LinkTo"));
            Assert.Null(Open(@"Directory\Background\shell\Symlinker.LinkHere"));
            Assert.NotNull(Open(@"Directory\shell\SomethingElse"));
        }

        [Fact]
        public void Disable_WhenAlreadyOff_DoesNothing()
        {
            Menu().Disable();

            Assert.False(Menu().IsEnabled);
        }

        [Fact]
        public void TrySetEnabled_TurnsTheEntriesOnAndOff()
        {
            var menu = Menu();

            Assert.Null(menu.TrySetEnabled(true));
            Assert.True(menu.IsEnabled);

            Assert.Null(menu.TrySetEnabled(false));
            Assert.False(menu.IsEnabled);
        }

        [Fact]
        public void TrySetEnabled_ReportsRegistryErrors()
        {
            Registry.CurrentUser.CreateSubKey(classesPath).Dispose();
            using var readOnly = Registry.CurrentUser.OpenSubKey(classesPath, writable: false);
            var menu = new ExplorerMenu(readOnly, @"Classes", ExePath, "Link to", "Link here");

            Assert.False(string.IsNullOrEmpty(menu.TrySetEnabled(true)));
        }

        [Fact]
        public void Refresh_WhenOff_LeavesItOff()
        {
            var menu = Menu();

            Assert.False(menu.Refresh());
            Assert.False(menu.IsEnabled);
        }

        [Fact]
        public void Refresh_WhenCurrent_DoesNothing()
        {
            var menu = Menu();
            menu.Enable();

            Assert.False(menu.Refresh());
        }

        [Fact]
        public void Refresh_AfterAnUpdate_PointsTheEntriesAtTheNewExe()
        {
            Menu(@"C:\Users\me\AppData\Local\Apps\2.0\old\Symlinker.exe").Enable();
            var current = Menu();

            Assert.True(current.IsEnabled);
            Assert.False(current.IsCurrent);
            Assert.True(current.Refresh());
            Assert.True(current.IsCurrent);
            Assert.StartsWith($"\"{ExePath}\"", Command(@"*\shell\Symlinker.LinkTo"));
        }

        [Fact]
        public void Refresh_AfterALanguageChange_RewritesTheText()
        {
            Menu().Enable();
            var german = new ExplorerMenu(Registry.CurrentUser, classesPath, ExePath, "Link hierauf erstellen", "Link hier erstellen");

            Assert.True(german.Refresh());
            using var key = Open(@"Directory\shell\Symlinker.LinkHere");
            Assert.Equal("Link hier erstellen", key.GetValue("MUIVerb"));
        }

        [Fact]
        public void Refresh_RestoresAMissingEntry()
        {
            var menu = Menu();
            menu.Enable();
            Registry.CurrentUser.DeleteSubKeyTree(classesPath + @"\*\shell\Symlinker.LinkTo");

            Assert.True(menu.IsEnabled);
            Assert.True(menu.Refresh());
            Assert.NotNull(Open(@"*\shell\Symlinker.LinkTo"));
        }

        [Fact]
        public void Refresh_TreatsAnEntryWithoutCommandAsStale()
        {
            var menu = Menu();
            menu.Enable();
            Registry.CurrentUser.DeleteSubKeyTree(classesPath + @"\*\shell\Symlinker.LinkTo\command");

            Assert.False(menu.IsCurrent);
            Assert.True(menu.Refresh());
            Assert.True(menu.IsCurrent);
        }

        [Fact]
        public void Refresh_SwallowsRegistryErrors()
        {
            Menu(@"D:\old\Symlinker.exe").Enable();
            using var readOnly = Registry.CurrentUser.OpenSubKey(TestsKey, writable: false);
            var menu = new ExplorerMenu(readOnly, classesPath[(TestsKey.Length + 1)..], ExePath, "Link to", "Link here");

            Assert.True(menu.IsEnabled);
            Assert.False(menu.Refresh());
        }

        [Fact]
        public void ForCurrentUser_UsesTheUserClassesKeyAndTheAppStrings()
        {
            var menu = ExplorerMenu.ForCurrentUser(ExePath);

            Assert.Equal(Properties.Resources.ExplorerMenuLinkTo, menu.Entries[0].Label);
            Assert.Equal(Properties.Resources.ExplorerMenuLinkHere, menu.Entries[3].Label);

            // Only reads the real registry; whatever is there, the answer is a plain bool
            Assert.IsType<bool>(menu.IsEnabled);
            Assert.Equal(@"Software\Classes", ExplorerMenu.UserClassesPath);
        }

        private ExplorerMenu Menu(string exePath = ExePath) =>
            new(Registry.CurrentUser, classesPath, exePath, "Link to", "Link here");

        private RegistryKey Open(string keyPath) => Registry.CurrentUser.OpenSubKey(classesPath + "\\" + keyPath);

        private string Command(string keyPath)
        {
            using var key = Open(keyPath + @"\command");
            return (string)key.GetValue(string.Empty);
        }

        /// <summary>Splits a command line the way Windows does for Main, dropping the exe.</summary>
        private static string[] Arguments(string commandLine) =>
            CommandLine.Split(commandLine).Skip(1).ToArray();
    }
}
