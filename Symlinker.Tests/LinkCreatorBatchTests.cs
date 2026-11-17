namespace Symlinker.Tests
{
    using System;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Text.RegularExpressions;
    using System.Threading.Tasks;

    using Res = Symlinker.Properties.Resources;

    /// <summary>
    /// Creating several links in one step: every question first, administrator rights once,
    /// failures don't stop the rest, and one summary at the end.
    /// </summary>
    public sealed class LinkCreatorBatchTests : IDisposable
    {
        private readonly TempDir temp = new();
        private readonly FakeDialogs dialogs = new();
        private readonly FakeLinkRunner runner = new();
        private readonly LinkCreator creator;

        public LinkCreatorBatchTests()
        {
            creator = new LinkCreator(dialogs, runner);
        }

        public void Dispose() => temp.Dispose();

        private string Links => temp.PathOf("links");

        private static string Format(string format, params object[] args) =>
            string.Format(CultureInfo.CurrentUICulture, format, args);

        private LinkInput Input(string name, string target, string location = null, bool isFolder = true, LinkKind kind = LinkKind.Symbolic) =>
            new(location ?? Links, name, target, isFolder, kind);

        private static string Summary(int created, int total, string[] createdLines, string[] failedLines)
        {
            var text = Format(Res.BatchSummaryCount, created, total);
            if (createdLines.Length != 0)
                text += "\n\n" + Res.BatchSummaryCreated + "\n" + string.Join("\n", createdLines);
            if (failedLines.Length != 0)
                text += "\n\n" + Res.BatchSummaryNotCreated + "\n" + string.Join("\n", failedLines);
            return text;
        }

        [Fact]
        public async Task Batch_WithNothingToDo_DoesNothing()
        {
            var outcomes = await creator.CreateBatchAsync(Array.Empty<LinkInput>());

            Assert.Empty(outcomes);
            Assert.Empty(dialogs.Messages);
            Assert.Empty(runner.Prepared);
        }

        [Fact]
        public async Task Batch_CreatesEveryLink_AsksForAdminRightsOnce_AndShowsOneSummary()
        {
            temp.Dir("links");
            var targets = new[] { temp.Dir("A", "One"), temp.Dir("A", "Two"), temp.Dir("A", "Three") };

            var outcomes = await creator.CreateBatchAsync(new[] { Input("One", targets[0]), Input("Two", targets[1]), Input(" Three ", targets[2]) });

            Assert.All(outcomes, outcome => Assert.Equal(LinkStatus.Created, outcome.Status));
            Assert.Equal(
                new[] { (Path.Combine(Links, "One"), targets[0]), (Path.Combine(Links, "Two"), targets[1]), (Path.Combine(Links, "Three"), targets[2]) },
                runner.Calls.Select(call => (call.Link, call.Target)));
            Assert.Same(runner.Calls[0], Assert.Single(runner.Prepared));
            Assert.Empty(dialogs.Questions);

            var summary = Assert.Single(dialogs.Messages);
            Assert.Equal((Res.MessageBoxSuccessTitle, Summary(3, 3, new[] { "One", "Two", "Three" }, Array.Empty<string>())), summary);
        }

        [Fact]
        public async Task Batch_KeepsGoingPastAFailure_AndSaysWhy()
        {
            temp.Dir("links");
            var one = temp.Dir("One");
            var two = temp.Dir("Two");
            var three = temp.Dir("Three");
            runner.Handler = request => request.Target == two ? LinkResult.Failed("Access is denied.\r\n  Try again.") : FakeLinkRunner.Created;

            var outcomes = await creator.CreateBatchAsync(new[] { Input("One", one), Input("Two", two), Input("Three", three) });

            Assert.Equal(new[] { LinkStatus.Created, LinkStatus.Failed, LinkStatus.Created }, outcomes.Select(outcome => outcome.Status));
            Assert.Equal(3, runner.Calls.Count);
            Assert.Equal(
                (Res.BatchSummaryPartialTitle, Summary(2, 3, new[] { "One", "Three" }, new[] { "Two: Access is denied. Try again." })),
                Assert.Single(dialogs.Messages));
        }

        [Fact]
        public async Task Batch_AsksEveryQuestion_BeforeAnythingOnDiskChanges()
        {
            temp.Dir("links");
            var existingLink = temp.File(@"links\Replace\old.txt");
            temp.File(@"links\Move\data.txt", "data");
            var replaceTarget = temp.Dir("ReplaceTarget");
            temp.File(@"ReplaceTarget\inside.txt");
            var moveTarget = temp.PathOf("MoveTarget");
            var plain = temp.Dir("Plain");

            var questionsWhenPrepared = -1;
            var replacedWhenPrepared = true;
            runner.OnPrepare = () =>
            {
                questionsWhenPrepared = dialogs.Questions.Count;
                replacedWhenPrepared = !File.Exists(existingLink);
            };

            var outcomes = await creator.CreateBatchAsync(new[] { Input("Replace", replaceTarget), Input("Move", moveTarget), Input("Plain", plain) });

            Assert.Equal(2, questionsWhenPrepared);
            Assert.False(replacedWhenPrepared);
            Assert.Single(runner.Prepared);

            // In a batch, each question starts with the link it is about
            Assert.Equal(
                new[]
                {
                    (Res.DialogFolderExistsDialog, Path.Combine(Links, "Replace") + "\n\n" + Res.DialogFolderExists),
                    (Res.DialogMoveTitle, Path.Combine(Links, "Move") + "\n\n" + Format(Res.DialogMoveFolder, "Move", moveTarget)),
                },
                dialogs.Questions.Select(question => (question.Title, question.Message)));

            Assert.All(outcomes, outcome => Assert.Equal(LinkStatus.Created, outcome.Status));
            Assert.False(Directory.Exists(Path.Combine(Links, "Replace")));
            Assert.Equal("data", File.ReadAllText(Path.Combine(moveTarget, "data.txt")));
            Assert.Equal(3, runner.Calls.Count);
        }

        [Fact]
        public async Task Batch_SkipsTheLinksTheUserSaidNoTo()
        {
            temp.Dir("links");
            var existing = temp.File(@"links\Replace\old.txt");
            var target = temp.Dir("Target");
            temp.File(@"Target\inside.txt");
            var plain = temp.Dir("Plain");
            dialogs.Answer = false;

            var outcomes = await creator.CreateBatchAsync(new[] { Input("Replace", target), Input("Plain", plain) });

            Assert.Equal(new[] { LinkStatus.Skipped, LinkStatus.Created }, outcomes.Select(outcome => outcome.Status));
            Assert.True(File.Exists(existing));
            Assert.Equal(plain, Assert.Single(runner.Calls).Target);
            Assert.Equal(
                (Res.BatchSummaryPartialTitle, Summary(1, 2, new[] { "Plain" }, new[] { "Replace: " + Res.LinkCreationAborted })),
                Assert.Single(dialogs.Messages));
        }

        [Fact]
        public async Task Batch_StopsQuietly_WhenAdminRightsAreDeclined()
        {
            temp.Dir("links");
            var existing = temp.File(@"links\Replace\old.txt");
            var target = temp.Dir("Target");
            temp.File(@"Target\inside.txt");
            runner.PrepareResult = false;

            var outcomes = await creator.CreateBatchAsync(new[] { Input("Replace", target), Input("Other", temp.Dir("Other")) });

            Assert.Null(outcomes);
            Assert.True(File.Exists(existing));
            Assert.Empty(runner.Calls);
            Assert.Empty(dialogs.Messages);
        }

        [Fact]
        public async Task Batch_DoesNotAskForAdminRights_WhenNoLinkIsLeftToCreate()
        {
            temp.Dir("links");

            var outcomes = await creator.CreateBatchAsync(new[] { Input("One", temp.PathOf("missing")), Input("Two", " ") });

            Assert.All(outcomes, outcome => Assert.Equal(LinkStatus.Failed, outcome.Status));
            Assert.Empty(runner.Prepared);
            Assert.Equal(
                (Res.BatchSummaryPartialTitle, Summary(0, 2, Array.Empty<string>(), new[] { "One: " + Res.FilesOrFolderNotExists, "Two: " + Res.FillBlanks })),
                Assert.Single(dialogs.Messages));
        }

        [Fact]
        public async Task Batch_ReportsAnUnexpectedError_ForThatLinkOnly()
        {
            temp.Dir("links");
            var good = temp.Dir("Good");

            var outcomes = await creator.CreateBatchAsync(new[] { Input("Bad\0Name", good), Input("Good", good) });

            Assert.Equal(new[] { LinkStatus.Failed, LinkStatus.Created }, outcomes.Select(outcome => outcome.Status));
            Assert.StartsWith(Res.MessageBoxExceptionOcurred + "\n", outcomes[0].Message);
            Assert.Single(runner.Calls);
        }

        [Fact]
        public async Task Batch_ListsLinksOutsideTheMainFolder_ByTheirFullPath()
        {
            temp.Dir("links");
            var elsewhere = temp.Dir("elsewhere");
            var target = temp.Dir("Target");

            await creator.CreateBatchAsync(new[] { Input("One", target), Input("Two", target, location: elsewhere), Input("Three", target, location: Links + "\\") });

            Assert.Equal(Summary(3, 3, new[] { "One", Path.Combine(elsewhere, "Two"), "Three" }, Array.Empty<string>()), dialogs.Last.Message);
        }

        [Fact]
        public async Task Batch_MentionsALeftoverCopy_EvenThoughTheLinkWorks()
        {
            temp.File(@"links\name\data.txt");
            var target = temp.PathOf("target");
            var staging = temp.PathOf("links", "name.symlinker-move");
            creator.IsSameVolume = (_, _) => false;
            creator.ProgressInterval = TimeSpan.Zero;
            FileStream lockedFile = null;

            // Something holds a file open in the old copy, so it can't be removed
            runner.Handler = request =>
            {
                if (request.Target == target)
                    lockedFile = new FileStream(Path.Combine(staging, "data.txt"), FileMode.Open, FileAccess.Read, FileShare.None);
                return FakeLinkRunner.Created;
            };

            try
            {
                await creator.CreateBatchAsync(new[] { Input("name", target), Input("other", temp.Dir("other")) });
            }
            finally
            {
                lockedFile?.Dispose();
            }

            var leftover = Regex.Replace(Format(Res.MoveCleanupFailed, staging), @"\s*\n\s*", " ");
            Assert.Equal(
                (Res.MessageBoxSuccessTitle, Summary(2, 2, new[] { "name: " + leftover, "other" }, Array.Empty<string>())),
                dialogs.Last);
        }

        [Fact]
        public async Task Single_ReportsAnUnexpectedErrorWhilePlanning()
        {
            temp.Dir("links");

            await creator.CreateAsync(Input("Bad\0Name", temp.Dir("Target")));

            Assert.Equal(Res.MessageBoxErrorTitle, dialogs.Last.Title);
            Assert.StartsWith(Res.MessageBoxExceptionOcurred + "\n", dialogs.Last.Message);
            Assert.Empty(runner.Prepared);
        }
    }
}
