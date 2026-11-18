namespace Symlinker.Tests
{
    using System;
    using System.Globalization;

    using Res = Symlinker.Properties.Resources;

    public sealed class LinkResultMessageTests
    {
        private static LinkRequest Request(string type = "symbolic", string link = @"D:\Games\accounts\Test", string target = @"D:\Games\benchmarks") =>
            new() { Type = type, Link = link, Target = target, Directory = type != "hard" };

        private static string Format(string format, params object[] args) => string.Format(CultureInfo.CurrentUICulture, format, args);

        private static string Paths(string link, string target) =>
            Format(Res.ResultLinkPath, link) + "\n" + Format(Res.ResultTargetPath, target);

        [Theory]
        [InlineData("symbolic")]
        [InlineData("junction")]
        [InlineData(null)]
        public void For_ASymbolicLinkOrJunction_SaysWhatItPointsTo(string type)
        {
            var message = LinkResultMessage.For(Request(type), LinkResult.Created("symbolic link created for ... <<===>> ..."));

            Assert.Equal(type == "junction" ? Res.ResultJunctionCreated : Res.ResultSymbolicLinkCreated, message.Title);
            Assert.Equal(
                Format(Res.ResultPointsToSentence, "Test", "benchmarks") + "\n\n" + Paths(@"D:\Games\accounts\Test", @"D:\Games\benchmarks"),
                message.Message);
        }

        [Fact]
        public void For_AHardLink_SaysItIsAnotherName()
        {
            var message = LinkResultMessage.For(Request("hard", @"C:\a\copy.txt", @"C:\b\original.txt"), LinkResult.Created());

            Assert.Equal(Res.ResultHardLinkCreated, message.Title);
            Assert.Equal(Format(Res.ResultHardLinkSentence, "copy.txt", "original.txt") + "\n\n" + Paths(@"C:\a\copy.txt", @"C:\b\original.txt"), message.Message);
        }

        [Fact]
        public void For_ALinkToADriveRoot_NamesTheDrive()
        {
            var message = LinkResultMessage.For(Request(target: @"E:\"), LinkResult.Created());

            Assert.StartsWith(Format(Res.ResultPointsToSentence, "Test", @"E:\"), message.Message);
        }

        [Fact]
        public void For_LongPaths_ShortensThem()
        {
            var link = @"D:\Alejandro\Documents\Some Very Long Game Name\Saved Games\accounts\Test";
            var message = LinkResultMessage.For(Request(link: link), LinkResult.Created());

            Assert.Contains(Format(Res.ResultLinkPath, LinkResultMessage.CompactPath(link)), message.Message);
            Assert.DoesNotContain(link, message.Message);
        }

        [Fact]
        public void ForMove_SaysWhatMoved_AndWhereTheLinkIs()
        {
            var message = LinkResultMessage.ForMove(Request("junction"), "Test");

            Assert.Equal(Res.ResultJunctionCreated, message.Title);
            Assert.Equal(Format(Res.MoveSucceeded, "Test") + "\n\n" + Paths(@"D:\Games\accounts\Test", @"D:\Games\benchmarks"), message.Message);
        }

        [Fact]
        public void For_AFailure_UsesThePlainReason()
        {
            var message = LinkResultMessage.For(Request(), LinkResult.Failed(LinkError.NeedsAdmin, "A required privilege is not held by the client."));

            Assert.Equal((Res.ResultFailedTitle, Res.ResultNeedsAdmin), (message.Title, message.Message));
        }

        public static TheoryData<string, string> MappedReasons => new()
        {
            { nameof(LinkError.InvalidPath), Res.InvalidQuoteInPath },
            { nameof(LinkError.CommandNotFound), Res.CmdNotFound },
            { nameof(LinkError.NotCreated), Res.ResultNotCreated },
            { nameof(LinkError.LinkExists), Format(Res.ResultLinkExists, "Test") },
            { nameof(LinkError.AccessDenied), Format(Res.ResultAccessDenied, @"D:\Games\accounts") },
            { nameof(LinkError.NeedsAdmin), Res.ResultNeedsAdmin },
            { nameof(LinkError.HardLinkAcrossDrives), Res.ResultHardLinkAcrossDrives },
            { nameof(LinkError.JunctionNeedsLocalFolder), Res.ResultJunctionNeedsLocalFolder },
        };

        [Theory]
        [MemberData(nameof(MappedReasons))]
        public void Reason_ExplainsKnownFailures_WithoutMklinksText(string error, string expected)
        {
            Assert.Equal(expected, LinkResultMessage.Reason(Request(), LinkResult.Failed(Enum.Parse<LinkError>(error), "mklink text")));
        }

        [Theory]
        [InlineData("Cannot create a file when that file already exists.", "Cannot create a file when that file already exists.")]
        [InlineData(" ", null)]
        [InlineData(null, null)]
        public void Reason_ShowsMklinksText_ForUnknownFailures(string detail, string expected)
        {
            Assert.Equal(expected ?? Res.ResultUnknownError, LinkResultMessage.Reason(Request(), LinkResult.Failed(detail)));
        }

        [Theory]
        [InlineData(@"C:\short\path", 60, @"C:\short\path")]
        [InlineData(@"D:\Alejandro\Documents\Anno 1800\accounts\Test", 30, "D:\\\u2026\\Anno 1800\\accounts\\Test")]
        [InlineData(@"D:\Alejandro\Documents\Anno 1800\accounts\Test", 20, "D:\\\u2026\\accounts\\Test")]
        [InlineData(@"D:\Alejandro\Documents\A-very-long-folder-name", 20, "D:\\\u2026\\A-very-long-folder-name")]
        [InlineData(@"\\server\share\Documents\Anno 1800\Test", 31, "\\\\server\\share\\\u2026\\Anno 1800\\Test")]
        [InlineData(@"\\server\share\Documents\Anno 1800\Test", 30, "\\\\server\\share\\\u2026\\Test")]
        [InlineData(@"C:\a-very-long-file-name-here.txt", 20, "C:\\\u2026\\a-very-long-file-name-here.txt")]
        [InlineData(@"..\..\Documents\Anno 1800\accounts\Test", 25, "\u2026\\Anno 1800\\accounts\\Test")]
        [InlineData(@"\\a-very-long-server-name\share", 20, @"\\a-very-long-server-name\share")]
        public void CompactPath_KeepsTheDriveAndTheLastFoldersThatFit(string path, int maxLength, string expected)
        {
            Assert.Equal(expected, LinkResultMessage.CompactPath(path, maxLength));
        }
    }
}
