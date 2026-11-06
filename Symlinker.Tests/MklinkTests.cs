namespace Symlinker.Tests
{
    using System;
    using System.IO;
    using System.Threading.Tasks;

    using Res = Symlinker.Properties.Resources;

    public sealed class MklinkTests : IDisposable
    {
        private readonly TempDir temp = new();

        public void Dispose() => temp.Dispose();

        [Theory]
        [InlineData(true, nameof(LinkKind.Symbolic), "/c mklink /D \"C:\\links\\a\" \"D:\\target\"")]
        [InlineData(true, nameof(LinkKind.Junction), "/c mklink /D /J \"C:\\links\\a\" \"D:\\target\"")]
        [InlineData(false, nameof(LinkKind.Symbolic), "/c mklink \"C:\\links\\a\" \"D:\\target\"")]
        [InlineData(false, nameof(LinkKind.Hard), "/c mklink /H \"C:\\links\\a\" \"D:\\target\"")]
        public void BuildArguments_MatchesTheLinkType(bool isFolder, string kind, string expected)
        {
            Assert.Equal(expected, Mklink.BuildArguments(isFolder, Enum.Parse<LinkKind>(kind), @"C:\links\a", @"D:\target"));
        }

        [Fact]
        public async Task Runner_ReturnsTheOutput_WhenTheCommandWorks()
        {
            var result = await new CmdMklinkRunner().RunAsync("/c echo hello");

            Assert.Equal((true, "hello"), result);
        }

        [Fact]
        public async Task Runner_ReturnsADefaultMessage_WhenTheCommandPrintsNothing()
        {
            var result = await new CmdMklinkRunner().RunAsync("/c exit 0");

            Assert.Equal((true, Res.LinkSuccessfullyCreated), result);
        }

        [Fact]
        public async Task Runner_ReturnsTheError_WhenTheCommandFails()
        {
            var result = await new CmdMklinkRunner().RunAsync("/c (echo oops 1>&2) & exit 3");

            Assert.Equal((false, "oops"), result);
        }

        [Fact]
        public async Task Runner_FallsBackToTheOutput_WhenAFailedCommandHasNoError()
        {
            var result = await new CmdMklinkRunner().RunAsync("/c echo plain& exit 2");

            Assert.Equal((false, "plain"), result);
        }

        [Fact]
        public async Task Runner_ReportsAMissingShell()
        {
            var result = await new CmdMklinkRunner(temp.PathOf("no-such-shell.exe")).RunAsync("/c echo hi");

            Assert.Equal((false, Res.CmdNotFound), result);
        }

        [Fact]
        public async Task Runner_CreatesARealJunction()
        {
            var target = temp.Dir("target");
            temp.File(@"target\inside.txt");
            var link = temp.PathOf("link");

            var (success, _) = await new CmdMklinkRunner().RunAsync(Mklink.BuildArguments(true, LinkKind.Junction, link, target));

            Assert.True(success);
            Assert.True(LinkMover.IsLink(new DirectoryInfo(link)));
            Assert.True(File.Exists(Path.Combine(link, "inside.txt")));
        }
    }
}
