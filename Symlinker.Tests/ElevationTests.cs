namespace Symlinker.Tests
{
    using System;
    using System.IO;
    using System.Runtime.InteropServices;

    public sealed class ElevationTests
    {
        // ERROR_PRIVILEGE_NOT_HELD as an HRESULT
        private const int PrivilegeNotHeld = unchecked((int)0x80070522);

        [Fact]
        public void Parse_ReadsEveryOption()
        {
            var options = LaunchOptions.Parse(new[]
            {
                "--mode", "file", "--type", "hard", "--location", @"C:\links", "--name", "my link", "--target", @"D:\data.txt",
            });

            Assert.Equal("file", options.Mode);
            Assert.Equal("hard", options.LinkType);
            Assert.Equal(@"C:\links", options.LinkLocation);
            Assert.Equal("my link", options.LinkName);
            Assert.Equal(@"D:\data.txt", options.Target);
        }

        [Fact]
        public void Parse_IgnoresUnknownAndIncompleteArguments()
        {
            var options = LaunchOptions.Parse(new[] { "--engage", "--name", "link", "--target" });

            Assert.Equal("link", options.LinkName);
            Assert.Null(options.Target);
            Assert.Null(options.Mode);
        }

        [Fact]
        public void ToArguments_SkipsEmptyValues()
        {
            var options = new LaunchOptions { Mode = "folder", LinkName = string.Empty, Target = @"C:\My Data\" };

            Assert.Equal(@"--mode folder --target ""C:\My Data\\""", options.ToArguments());
        }

        [Fact]
        public void ToArguments_RoundTripsThroughTheWindowsCommandLine()
        {
            var options = LaunchOptions.From(true, LinkKind.Junction, @"C:\", @"say ""hi""", @"D:\a\""b\ c\\");

            var parsed = LaunchOptions.Parse(SplitCommandLine(options.ToArguments()));

            Assert.Equal("folder", parsed.Mode);
            Assert.Equal("junction", parsed.LinkType);
            Assert.Equal(@"C:\", parsed.LinkLocation);
            Assert.Equal(@"say ""hi""", parsed.LinkName);
            Assert.Equal(@"D:\a\""b\ c\\", parsed.Target);
        }

        [Theory]
        [InlineData("plain", "plain")]
        [InlineData(@"C:\no-spaces\", @"C:\no-spaces\")]
        [InlineData("", @"""""")]
        [InlineData("a b", @"""a b""")]
        [InlineData(@"C:\My Folder\", @"""C:\My Folder\\""")]
        [InlineData(@"say ""hi""", @"""say \""hi\""""")]
        [InlineData(@"a\""b", @"""a\\\""b""")]
        [InlineData(@"a\b c", @"""a\b c""")]
        public void QuoteArgument_EscapesOnlyWhatWindowsNeeds(string value, string expected)
        {
            Assert.Equal(expected, LaunchOptions.QuoteArgument(value));
        }

        [Theory]
        [InlineData("folder", true)]
        [InlineData("FOLDER", true)]
        [InlineData("file", false)]
        [InlineData("disk", null)]
        [InlineData(null, null)]
        public void IsFolderMode_ReadsTheMode(string mode, bool? expected)
        {
            Assert.Equal(expected, new LaunchOptions { Mode = mode }.IsFolderMode);
        }

        [Theory]
        [InlineData("symbolic", "Symbolic")]
        [InlineData("Hard", "Hard")]
        [InlineData("junction", "Junction")]
        [InlineData("shortcut", null)]
        [InlineData(null, null)]
        public void Kind_ReadsTheLinkType(string linkType, string expected)
        {
            Assert.Equal(expected, new LaunchOptions { LinkType = linkType }.Kind?.ToString());
        }

        [Theory]
        [InlineData(true, "Symbolic", "folder", "symbolic")]
        [InlineData(true, "Junction", "folder", "junction")]
        [InlineData(false, "Hard", "file", "hard")]
        public void From_DescribesTheForm(bool isFolder, string kind, string mode, string linkType)
        {
            var options = LaunchOptions.From(isFolder, Enum.Parse<LinkKind>(kind), "location", "name", "target");

            Assert.Equal((mode, linkType, "location", "name", "target"), (options.Mode, options.LinkType, options.LinkLocation, options.LinkName, options.Target));
        }

        [Fact]
        public void CanCreateSymbolicLinks_IsTrue_WhenElevated()
        {
            Assert.True(Elevation.CanCreateSymbolicLinks(true, (_, _) => throw new InvalidOperationException("not probed")));
        }

        [Fact]
        public void CanCreateSymbolicLinks_IsTrue_WhenTheProbeWorks()
        {
            Assert.True(Elevation.CanCreateSymbolicLinks(false, (_, _) => null));
        }

        [Fact]
        public void CanCreateSymbolicLinks_IsFalse_WithoutThePrivilege()
        {
            Assert.False(Elevation.CanCreateSymbolicLinks(false, (_, _) => throw new IOException("A required privilege is not held by the client.", PrivilegeNotHeld)));
        }

        [Fact]
        public void CanCreateSymbolicLinks_IsTrue_WhenTheProbeFailsForOtherReasons()
        {
            // Let mklink try and report its own error
            Assert.True(Elevation.CanCreateSymbolicLinks(false, (_, _) => throw new IOException("disk full", unchecked((int)0x80070070))));
            Assert.True(Elevation.CanCreateSymbolicLinks(false, (_, _) => throw new UnauthorizedAccessException()));
        }

        [Fact]
        public void CanCreateSymbolicLinks_ProbesForReal()
        {
            Assert.Equal(SymlinkSupport.Available, Elevation.CanCreateSymbolicLinks());
        }

        private static string[] SplitCommandLine(string arguments)
        {
            // The program name is parsed by different rules, so give it a plain one
            var argv = CommandLineToArgvW("program.exe " + arguments, out var count);
            try
            {
                var result = new string[count - 1];
                for (var i = 1; i < count; i++)
                    result[i - 1] = Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv, i * IntPtr.Size));
                return result;
            }
            finally
            {
                LocalFree(argv);
            }
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CommandLineToArgvW(string commandLine, out int count);

        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr memory);
    }
}
