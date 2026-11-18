namespace Symlinker.Tests
{
    using System;
    using System.IO;
    using System.Threading.Tasks;

    public sealed class LinkCommandTests : IDisposable
    {
        private readonly TempDir temp = new();

        public void Dispose() => temp.Dispose();

        private static LinkRequest Request(string type = "symbolic", string link = @"C:\links\a", string target = @"D:\target", bool directory = true) =>
            new() { Type = type, Link = link, Target = target, Directory = directory };

        [Theory]
        [InlineData("symbolic", true, "/c mklink /D \"C:\\links\\a\" \"D:\\target\"")]
        [InlineData("junction", true, "/c mklink /D /J \"C:\\links\\a\" \"D:\\target\"")]
        [InlineData("symbolic", false, "/c mklink \"C:\\links\\a\" \"D:\\target\"")]
        [InlineData("hard", false, "/c mklink /H \"C:\\links\\a\" \"D:\\target\"")]
        public void BuildArguments_MatchesTheLinkType(string type, bool directory, string expected)
        {
            Assert.Equal(expected, LinkCommand.BuildArguments(Request(type, directory: directory)));
        }

        [Theory]
        [InlineData("shortcut", @"C:\links\a", @"D:\target")]
        [InlineData("Symbolic", @"C:\links\a", @"D:\target")]
        [InlineData(null, @"C:\links\a", @"D:\target")]
        [InlineData("symbolic", " ", @"D:\target")]
        [InlineData("symbolic", @"C:\links\a", "")]
        [InlineData("symbolic", "C:\\links\\a\" & del x", @"D:\target")]
        [InlineData("symbolic", @"C:\links\a", "D:\\target\r\nrd /s C:\\")]
        [InlineData("symbolic", @"C:\links\a", "D:\\target\n")]
        [InlineData("symbolic", "C:\\links\\a\0", @"D:\target")]
        public void BuildArguments_RefusesAnythingThatCouldBreakOutOfTheCommand(string type, string link, string target)
        {
            // The elevated helper runs whatever it is sent, so only exact, quote-safe requests pass
            Assert.Null(LinkCommand.BuildArguments(Request(type, link, target)));
        }

        [Fact]
        public void BuildArguments_RefusesAMissingRequest()
        {
            Assert.Null(LinkCommand.BuildArguments(null));
        }

        [Fact]
        public async Task RunAsync_ReportsARefusedRequest_WithoutRunningAnything()
        {
            var result = await LinkCommand.RunAsync(Request(target: "D:\\\"x"), temp.PathOf("no-such-shell.exe"));

            Assert.Equal((false, LinkError.InvalidPath), (result.Success, result.Error));
        }

        [Fact]
        public async Task RunAsync_ReportsAMissingShell()
        {
            var result = await LinkCommand.RunAsync(Request(), temp.PathOf("no-such-shell.exe"));

            Assert.Equal((false, LinkError.CommandNotFound), (result.Success, result.Error));
        }

        [Theory]
        [InlineData("@echo hello", "hello")]
        [InlineData("@exit 0", "")]
        public async Task RunAsync_ChecksThatTheLinkIsThere_EvenWhenTheShellSaysItWorked(string script, string output)
        {
            // A stand-in for cmd.exe that ignores the mklink arguments, so no link appears
            var shell = temp.File("shell.cmd", script);

            var result = await LinkCommand.RunAsync(Request("junction", temp.PathOf("link"), temp.Dir("target")), shell);

            Assert.Equal((false, LinkError.NotCreated, output), (result.Success, result.Error, result.Message));
        }

        [Fact]
        public async Task RunAsync_Succeeds_WhenTheShellWorkedAndTheLinkIsThere()
        {
            var target = temp.Dir("target");
            var link = temp.Junction(temp.PathOf("link"), target).FullName;
            var shell = temp.File("shell.cmd", "@echo made it");

            var result = await LinkCommand.RunAsync(Request("junction", link, target), shell);

            Assert.Equal((true, LinkError.None, "made it"), (result.Success, result.Error, result.Message));
        }

        [Theory]
        [InlineData("@echo oops 1>&2\r\n@exit 3", "oops")]
        [InlineData("@echo plain\r\n@exit 2", "plain")]
        public async Task RunAsync_KeepsTheShellsErrorText_WhenTheReasonIsUnknown(string script, string message)
        {
            var shell = temp.File("shell.cmd", script);

            var result = await LinkCommand.RunAsync(Request("junction", temp.PathOf("link"), temp.Dir("target")), shell);

            Assert.Equal((false, LinkError.Unknown, message), (result.Success, result.Error, result.Message));
        }

        [Fact]
        public async Task RunAsync_CreatesARealJunction()
        {
            var target = temp.Dir("target");
            temp.File(@"target\inside.txt");
            var link = temp.PathOf("link");

            var result = await LinkCommand.RunAsync(Request("junction", link, target));

            Assert.True(result.Success, result.Message);
            Assert.Equal(LinkError.None, result.Error);
            Assert.True(LinkMover.IsLink(new DirectoryInfo(link)));
            Assert.True(File.Exists(Path.Combine(link, "inside.txt")));
        }

        [Fact]
        public async Task RunAsync_ReportsAnExistingLinkPath_WithMklinksText()
        {
            var target = temp.Dir("target");
            var link = temp.Dir("taken");

            var result = await LinkCommand.RunAsync(Request("junction", link, target));

            Assert.Equal((false, LinkError.LinkExists), (result.Success, result.Error));
            Assert.NotEmpty(result.Message);
        }

        [Fact]
        public async Task RunAsync_CreatesARealHardLink()
        {
            var target = temp.File("target.txt", "same");
            var link = temp.PathOf("link.txt");

            var result = await LinkCommand.RunAsync(Request("hard", link, target, directory: false));

            Assert.True(result.Success, result.Message);
            Assert.Equal("same", File.ReadAllText(link));
        }

        [Fact]
        public async Task RunAsync_CreatesARealSymbolicLink()
        {
            SymlinkSupport.Require();
            var target = temp.Dir("target");
            var link = temp.PathOf("link");

            var result = await LinkCommand.RunAsync(Request("symbolic", link, target));

            Assert.True(result.Success, result.Message);
        }

        [Theory]
        [InlineData("Symbolic", "symbolic")]
        [InlineData("Hard", "hard")]
        [InlineData("Junction", "junction")]
        public void LinkKinds_RoundTripNames(string kind, string name)
        {
            var linkKind = Enum.Parse<LinkKind>(kind);

            Assert.Equal(name, LinkKinds.ToName(linkKind));
            Assert.Equal(linkKind, LinkKinds.Parse(name.ToUpperInvariant()));
        }

        [Fact]
        public void LinkKinds_Parse_ReturnsNull_ForUnknownNames()
        {
            Assert.Null(LinkKinds.Parse("shortcut"));
            Assert.Null(LinkKinds.Parse(null));
        }
    }
}
