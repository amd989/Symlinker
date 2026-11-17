namespace Symlinker.Tests
{
    using System;
    using System.IO;

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

        [Theory]
        [InlineData(@"Symlinker.exe --location ""C:\""", @"C:\")]
        [InlineData(@"Symlinker.exe --location ""D:\My Links""", @"D:\My Links")]
        public void Parse_ReadsTheLocationAsExplorerPassesIt(string commandLine, string expected)
        {
            // Explorer passes a drive root as "C:\", which Windows splits into C:"
            Assert.Equal(expected, LaunchOptions.Parse(CommandLine.Split(commandLine)[1..]).LinkLocation);
        }

        [Fact]
        public void Parse_RestoresTheBackslashOfATarget()
        {
            var options = LaunchOptions.Parse(CommandLine.Split(@"Symlinker.exe --mode folder --target ""E:\""")[1..]);

            Assert.Equal(@"E:\", options.Target);
            Assert.Equal("folder", options.Mode);
        }

        [Fact]
        public void Parse_KeepsPathsWithSpaces()
        {
            var options = LaunchOptions.Parse(CommandLine.Split(@"Symlinker.exe --mode file --target ""D:\My Data\a b.txt""")[1..]);

            Assert.Equal(@"D:\My Data\a b.txt", options.Target);
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

        [Fact]
        public void IsPackaged_IsFalse_OutsideAnMsixPackage()
        {
            Assert.False(Elevation.IsPackaged());
        }
    }
}
