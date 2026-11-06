namespace Symlinker.Tests
{
    using System;
    using System.Globalization;
    using System.IO;

    public sealed class UiHelpersTests : IDisposable
    {
        private readonly TempDir temp = new();

        public void Dispose() => temp.Dispose();

        [Theory]
        [InlineData(@"C:\a\b", "b")]
        [InlineData(@"C:\a\b\", "b")]
        [InlineData("C:/a/b//", "b")]
        [InlineData("name", "name")]
        [InlineData(@"D:\", "D:")]
        public void GetName_ReturnsLastPart(string path, string expected)
        {
            Assert.Equal(expected, UiHelpers.GetName(path));
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("bad\0path")]
        public void GetExistingDirectory_ReturnsNull_ForEmptyOrInvalidPaths(string path)
        {
            Assert.Null(UiHelpers.GetExistingDirectory(path, useParent: false));
        }

        [Fact]
        public void GetExistingDirectory_ReturnsThePath_WhenItExists()
        {
            var folder = temp.Dir("folder");

            Assert.Equal(folder, UiHelpers.GetExistingDirectory("  " + folder + "  ", useParent: false));
        }

        [Fact]
        public void GetExistingDirectory_ReturnsNull_WhenThePathIsMissing()
        {
            Assert.Null(UiHelpers.GetExistingDirectory(temp.PathOf("missing"), useParent: false));
        }

        [Fact]
        public void GetExistingDirectory_WithParent_ReturnsTheContainingFolder()
        {
            var file = temp.File(@"folder\file.txt");

            Assert.Equal(Path.GetDirectoryName(file), UiHelpers.GetExistingDirectory(file, useParent: true));
            Assert.Equal(temp.Root, UiHelpers.GetExistingDirectory(temp.PathOf("folder") + @"\", useParent: true));
        }

        [Fact]
        public void GetExistingDirectory_WithParent_FallsBackToTheDriveRoot()
        {
            var root = Path.GetPathRoot(temp.Root);

            Assert.Equal(root, UiHelpers.GetExistingDirectory(root, useParent: true));
        }

        [Theory]
        [InlineData(0, "0 B")]
        [InlineData(1023, "1023 B")]
        [InlineData(1536, "1.5 KB")]
        [InlineData(5L * 1024 * 1024, "5.0 MB")]
        [InlineData(3L * 1024 * 1024 * 1024, "3.0 GB")]
        [InlineData(2048L * 1024 * 1024 * 1024 * 1024, "2048.0 TB")]
        public void FormatBytes_PicksTheLargestFittingUnit(long bytes, string expected)
        {
            var previous = CultureInfo.CurrentUICulture;
            CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
            try
            {
                Assert.Equal(expected, UiHelpers.FormatBytes(bytes));
            }
            finally
            {
                CultureInfo.CurrentUICulture = previous;
            }
        }

        [Fact]
        public void GetAppVersion_PrefersTheClickOnceVersion()
        {
            const string Variable = "ClickOnce_CurrentVersion";
            var previous = Environment.GetEnvironmentVariable(Variable);
            try
            {
                Environment.SetEnvironmentVariable(Variable, "9.8.7.6");
                Assert.Equal("9.8.7.6", UiHelpers.GetAppVersion());

                Environment.SetEnvironmentVariable(Variable, null);
                Assert.Equal(typeof(UiHelpers).Assembly.GetName().Version.ToString(), UiHelpers.GetAppVersion());
            }
            finally
            {
                Environment.SetEnvironmentVariable(Variable, previous);
            }
        }

        [Fact]
        public void LinkNameAutoFill_FollowsTheTargetUntilTheUserTypes()
        {
            var autoFill = new LinkNameAutoFill();

            Assert.Equal("first", autoFill.Update(string.Empty, "first"));

            // Still the name we filled in, so it keeps following the target
            Assert.Equal("second", autoFill.Update("first", "second"));

            // The user typed their own name, so it is left alone
            Assert.Null(autoFill.Update("mine", "third"));
        }

        [Fact]
        public void LinkNameAutoFill_SkipsNamesThatCantBeLinkNames()
        {
            var autoFill = new LinkNameAutoFill();

            Assert.Equal(string.Empty, autoFill.Update(string.Empty, "D:"));
            Assert.Equal("data", autoFill.Update(string.Empty, "data"));
        }
    }
}
