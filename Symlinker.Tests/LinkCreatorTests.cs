namespace Symlinker.Tests
{
    using System;
    using System.Globalization;
    using System.IO;
    using System.Threading.Tasks;

    using Res = Symlinker.Properties.Resources;

    /// <summary>
    /// Runs the link workflow against the real file system, with fake dialogs and a fake link runner.
    /// </summary>
    public sealed class LinkCreatorTests : IDisposable
    {
        private readonly TempDir temp = new();
        private readonly FakeDialogs dialogs = new();
        private readonly FakeLinkRunner runner = new();
        private readonly LinkCreator creator;

        public LinkCreatorTests()
        {
            creator = new LinkCreator(dialogs, runner);
        }

        public void Dispose() => temp.Dispose();

        private string Links => temp.PathOf("links");

        private static string Format(string format, params object[] args) =>
            string.Format(CultureInfo.CurrentUICulture, format, args);

        private Task Create(string linkName, string destination, bool isFolder = true, LinkKind kind = LinkKind.Symbolic, string linkLocation = null) =>
            creator.CreateAsync(new LinkInput(linkLocation ?? Links, linkName, destination, isFolder, kind));

        private void AssertLinked(string type, string link, string target, bool directory)
        {
            var request = Assert.Single(runner.Calls);
            Assert.Equal((type, link, target, directory), (request.Type, request.Link, request.Target, request.Directory));

            // Administrator rights are settled for the same link before it is created
            Assert.Equal(request, Assert.Single(runner.Prepared));
        }

        private void UseCopyMove(TimeSpan? progressInterval = null)
        {
            creator.IsSameVolume = (_, _) => false;
            creator.ProgressInterval = progressInterval ?? TimeSpan.Zero;
        }

        // ---- Validation ----

        [Theory]
        [InlineData("", "name", "target")]
        [InlineData("links", " ", "target")]
        [InlineData("links", "name", "")]
        public async Task Create_AsksToFillBlanks(string location, string name, string destination)
        {
            await creator.CreateAsync(new LinkInput(location, name, destination, true, LinkKind.Symbolic));

            Assert.Equal((Res.MessageBoxErrorTitle, Res.FillBlanks), dialogs.Last);
            Assert.Empty(runner.Calls);
        }

        [Theory]
        [InlineData("links\"", "name", "target")]
        [InlineData("links", "na\"me", "target")]
        [InlineData("links", "name", "\"target\"")]
        public async Task Create_RejectsQuotes(string location, string name, string destination)
        {
            await creator.CreateAsync(new LinkInput(location, name, destination, true, LinkKind.Symbolic));

            Assert.Equal((Res.MessageBoxErrorTitle, Res.InvalidQuoteInPath), dialogs.Last);
            Assert.Empty(runner.Calls);
        }

        [Theory]
        [InlineData(true, "missing-location", "target-folder")]
        [InlineData(false, "missing-location", "target.txt")]
        [InlineData(true, "links", "missing")]
        [InlineData(false, "links", "missing.txt")]
        [InlineData(true, "links", "target.txt")]
        [InlineData(false, "links", "target-folder")]
        [InlineData(false, "links", "relative")]
        public async Task Create_ReportsMissingOrMismatchedPaths(bool isFolder, string location, string destination)
        {
            temp.Dir("links");
            temp.Dir("target-folder");
            temp.File("target.txt");
            var fullDestination = destination == "relative" ? destination : temp.PathOf(destination);

            await Create("name", fullDestination, isFolder, linkLocation: temp.PathOf(location));

            Assert.Equal((Res.MessageBoxErrorTitle, Res.FilesOrFolderNotExists), dialogs.Last);
            Assert.Empty(runner.Calls);
        }

        // ---- Plain link creation ----

        [Fact]
        public async Task Create_FolderLink_RunsMklinkWithTrimmedPaths()
        {
            temp.Dir("links");
            var target = temp.Dir("target");

            await creator.CreateAsync(new LinkInput($"  {Links} ", " name ", $" {target} ", true, LinkKind.Junction));

            AssertLinked("junction", $@"{Links}\name", target, directory: true);
            Assert.Equal((Res.MessageBoxSuccessTitle, "created"), dialogs.Last);
            Assert.Empty(dialogs.Questions);
        }

        [Fact]
        public async Task Create_FileLink_RunsMklink()
        {
            temp.Dir("links");
            var target = temp.File("target.txt");

            await Create("name.txt", target, isFolder: false, kind: LinkKind.Hard);

            AssertLinked("hard", $@"{Links}\name.txt", target, directory: false);
        }

        [Fact]
        public async Task Create_ShowsMklinkErrors()
        {
            temp.Dir("links");
            var target = temp.Dir("target");
            runner.Handler = _ => LinkResult.Failed("access denied");

            await Create("name", target);

            Assert.Equal((Res.MessageBoxErrorTitle, "access denied"), dialogs.Last);
        }

        [Fact]
        public async Task Create_StopsQuietly_WhenAdminRightsAreDeclined()
        {
            temp.Dir("links");
            var target = temp.Dir("target");
            runner.PrepareResult = false;

            await Create("name", target);

            Assert.Equal("symbolic", Assert.Single(runner.Prepared).Type);
            Assert.Empty(runner.Calls);
            Assert.Empty(dialogs.Messages);
        }

        // ---- Replacing an existing entry ----

        [Fact]
        public async Task Create_ReplacesAnExistingFolder_WhenConfirmed()
        {
            temp.File(@"links\Name\old.txt");
            var target = temp.Dir("target");
            temp.File(@"target\inside.txt");

            await Create("name", target);

            var question = Assert.Single(dialogs.Questions);
            Assert.Equal((Res.DialogFolderExistsDialog, Res.DialogFolderExists), (question.Title, question.Message));
            Assert.False(Directory.Exists(temp.PathOf("links", "Name")));
            Assert.Single(runner.Calls);
        }

        [Fact]
        public async Task Create_KeepsAnExistingFolder_WhenDeclined()
        {
            temp.File(@"links\name\old.txt");
            var target = temp.Dir("target");
            temp.File(@"target\inside.txt");
            dialogs.Answer = false;

            await Create("name", target);

            Assert.Equal((Res.LinkCreationAbortedWarning, Res.LinkCreationAborted), dialogs.Last);
            Assert.True(File.Exists(temp.PathOf("links", "name", "old.txt")));
            Assert.Empty(runner.Calls);
        }

        [Fact]
        public async Task Create_AsksForAdminRights_BeforeReplacing()
        {
            var existing = temp.PathOf("links", "name");
            temp.File(@"links\name\old.txt");
            var target = temp.Dir("target");
            temp.File(@"target\inside.txt");
            var existedWhenAsked = false;
            runner.OnPrepare = () => existedWhenAsked = Directory.Exists(existing);
            runner.PrepareResult = false;

            await Create("name", target);

            Assert.Single(dialogs.Questions);
            Assert.True(existedWhenAsked);
            Assert.True(Directory.Exists(existing));
            Assert.Empty(runner.Calls);
            Assert.Empty(dialogs.Messages);
        }

        [Fact]
        public async Task Create_ReplacesAnExistingFile_WhenConfirmed()
        {
            temp.File(@"links\name.txt");
            var target = temp.File("target.txt");

            await Create("name.txt", target, isFolder: false);

            var question = Assert.Single(dialogs.Questions);
            Assert.Equal((Res.DialogDeleteFileWarning, Res.DialogDeleteFile), (question.Title, question.Message));
            Assert.False(File.Exists(temp.PathOf("links", "name.txt")));
            Assert.Single(runner.Calls);
        }

        [Fact]
        public async Task Create_KeepsAnExistingFile_WhenDeclined()
        {
            temp.File(@"links\name.txt");
            var target = temp.File("target.txt");
            dialogs.Answer = false;

            await Create("name.txt", target, isFolder: false);

            Assert.Equal((Res.LinkCreationAbortedWarning, Res.LinkCreationAborted), dialogs.Last);
            Assert.True(File.Exists(temp.PathOf("links", "name.txt")));
            Assert.Empty(runner.Calls);
        }

        [Fact]
        public async Task Create_TreatsAnExistingLinkAsReplaceable_NotMovable()
        {
            temp.Dir("links");
            temp.Junction(temp.PathOf("links", "name"), temp.Dir("old-target"));
            var target = temp.Dir("empty-target");
            dialogs.Answer = false;

            await Create("name", target);

            Assert.Equal(Res.DialogFolderExistsDialog, Assert.Single(dialogs.Questions).Title);
        }

        [Fact]
        public async Task Create_ReportsUnexpectedErrors()
        {
            var locked = temp.File(@"links\name\locked.txt");
            var target = temp.Dir("target");
            temp.File(@"target\inside.txt");

            using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
                await Create("name", target);

            Assert.Equal(Res.MessageBoxErrorTitle, dialogs.Last.Title);
            Assert.StartsWith(Res.MessageBoxExceptionOcurred + "\n", dialogs.Last.Message);
            Assert.Empty(runner.Calls);
        }

        // ---- Offering to move an existing entry ----

        [Fact]
        public async Task Move_DoesNothing_WhenDeclined()
        {
            temp.File(@"links\name\data.txt");
            var target = temp.PathOf("target");
            dialogs.Answer = false;

            await Create("name", target);

            var question = Assert.Single(dialogs.Questions);
            Assert.Equal(Res.DialogMoveTitle, question.Title);
            Assert.Equal(Format(Res.DialogMoveFolder, "name", target), question.Message);
            Assert.Equal((Res.ButtonMoveAndLink, Res.ButtonCancel), (question.Yes, question.No));
            Assert.Equal((Res.LinkCreationAbortedWarning, Res.LinkCreationAborted), dialogs.Last);
            Assert.True(File.Exists(temp.PathOf("links", "name", "data.txt")));
            Assert.Empty(runner.Calls);
        }

        [Fact]
        public async Task Move_AsksForAdminRights_BeforeMovingAnything()
        {
            temp.File(@"links\name\data.txt");
            var target = temp.PathOf("target");
            var movingWhenAsked = true;
            runner.OnPrepare = () => movingWhenAsked = creator.IsMoving;
            runner.PrepareResult = false;

            await Create("name", target);

            Assert.Equal(("symbolic", target), (Assert.Single(runner.Prepared).Type, runner.Prepared[0].Target));
            Assert.False(movingWhenAsked);
            Assert.True(File.Exists(temp.PathOf("links", "name", "data.txt")));
            Assert.False(Directory.Exists(target));
            Assert.Empty(runner.Calls);
            Assert.Empty(dialogs.Messages);
        }

        [Fact]
        public async Task Move_Folder_OnTheSameDrive_RenamesAndLinks()
        {
            temp.File(@"links\name\data.txt", "data");
            var target = temp.PathOf("new", "parent", "target");
            var movingDuringLink = false;
            runner.Handler = _ =>
            {
                movingDuringLink = creator.IsMoving;
                return FakeLinkRunner.Created;
            };

            await Create("name", target);

            AssertLinked("symbolic", $@"{Links}\name", target, directory: true);
            Assert.Equal("data", File.ReadAllText(Path.Combine(target, "data.txt")));
            Assert.False(Directory.Exists(temp.PathOf("links", "name")));
            Assert.Equal((Res.MessageBoxSuccessTitle, Format(Res.MoveSucceeded, "name", target)), dialogs.Last);
            Assert.True(movingDuringLink);
            Assert.False(creator.IsMoving);
            Assert.Null(dialogs.Progress.Shown);
        }

        [Fact]
        public async Task Move_File_IntoAnEmptyFolderTarget_IsRefused()
        {
            // An empty folder is only a free target for folders
            temp.File(@"links\name.txt");
            var target = temp.Dir("empty");

            await Create("name.txt", target, isFolder: false);

            Assert.Empty(dialogs.Questions);
            Assert.Equal((Res.MessageBoxErrorTitle, Res.FilesOrFolderNotExists), dialogs.Last);
        }

        [Fact]
        public async Task Move_File_OnTheSameDrive_RenamesAndLinks()
        {
            temp.File(@"links\name.txt", "data");
            var target = temp.PathOf("moved.txt");

            await Create("name.txt", target, isFolder: false);

            Assert.Equal(Format(Res.DialogMoveFile, "name.txt", target), Assert.Single(dialogs.Questions).Message);
            Assert.Equal("data", File.ReadAllText(target));
            Assert.False(File.Exists(temp.PathOf("links", "name.txt")));
            Assert.Equal(Res.MessageBoxSuccessTitle, dialogs.Last.Title);
        }

        [Fact]
        public async Task Move_IntoAnEmptyFolder_ReplacesIt()
        {
            temp.File(@"links\name\data.txt");
            var target = temp.Dir("empty-target");

            await Create("name", target);

            Assert.True(File.Exists(Path.Combine(target, "data.txt")));
            Assert.Equal(Res.MessageBoxSuccessTitle, dialogs.Last.Title);
        }

        [Fact]
        public async Task Move_IsRefused_WhenTheTargetIsInsideTheSource()
        {
            temp.File(@"links\name\data.txt");

            await Create("name", temp.PathOf("links", "name", "inner"));

            Assert.Equal((Res.MessageBoxErrorTitle, Res.MoveTargetInsideSource), dialogs.Last);
            Assert.True(File.Exists(temp.PathOf("links", "name", "data.txt")));
            Assert.Empty(runner.Calls);
        }

        [Fact]
        public async Task Move_Folder_ToAnotherDrive_CopiesWithProgressThenRemovesTheOriginal()
        {
            temp.File(@"links\name\a.txt", "aaa");
            temp.File(@"links\name\nested\b.txt", "bb");
            var target = temp.PathOf("target");
            UseCopyMove();

            await Create("name", target);

            Assert.Equal("aaa", File.ReadAllText(Path.Combine(target, "a.txt")));
            Assert.Equal("bb", File.ReadAllText(Path.Combine(target, "nested", "b.txt")));
            Assert.False(Directory.Exists(temp.PathOf("links", "name")));
            Assert.False(Directory.Exists(temp.PathOf("links", "name.symlinker-move")));
            Assert.Equal((Res.MessageBoxSuccessTitle, Format(Res.MoveSucceeded, "name", target)), dialogs.Last);

            var progress = dialogs.Progress;
            Assert.Equal((Res.ProgressMoveTitle, Res.ProgressMovePreparing, Res.ButtonCancel), progress.Shown);
            Assert.True(progress.Indeterminate);
            Assert.True(progress.Closed);
            Assert.Contains(0, progress.Values);
            Assert.Contains(Format(Res.ProgressMoveMessage, UiHelpers.FormatBytes(0), UiHelpers.FormatBytes(5)), progress.ShownMessages);
        }

        [Fact]
        public async Task Move_File_ToAnotherDrive_CopiesIt()
        {
            temp.File(@"links\name.txt", "data");
            var target = temp.PathOf("moved.txt");

            // Never reports progress mid-copy
            UseCopyMove(TimeSpan.MaxValue);

            await Create("name.txt", target, isFolder: false);

            Assert.Equal("data", File.ReadAllText(target));
            Assert.False(File.Exists(temp.PathOf("links", "name.txt")));
            Assert.Equal(Res.MessageBoxSuccessTitle, dialogs.Last.Title);
            Assert.Equal(new[] { 0d }, dialogs.Progress.Values);
        }

        [Fact]
        public async Task Move_EmptyFolder_ToAnotherDrive_ShowsFullProgress()
        {
            temp.Dir(@"links\name");
            var target = temp.PathOf("target");
            UseCopyMove();

            await Create("name", target);

            Assert.True(Directory.Exists(target));
            Assert.Equal(new[] { 1d }, dialogs.Progress.Values);
        }

        [Fact]
        public async Task Move_Canceled_PutsEverythingBack()
        {
            temp.File(@"links\name\data.txt");
            var target = temp.Dir("empty-target");
            UseCopyMove();
            dialogs.Progress.BeforeCopy = dialogs.Progress.Cancel;

            await Create("name", target);

            Assert.Equal((Res.LinkCreationAbortedWarning, Res.MoveCanceled), dialogs.Last);
            Assert.True(File.Exists(temp.PathOf("links", "name", "data.txt")));
            Assert.True(Directory.Exists(target));
            Assert.Empty(Directory.EnumerateFileSystemEntries(target));
            Assert.Empty(runner.Calls);
        }

        [Fact]
        public async Task Move_CopyFailure_PutsEverythingBack()
        {
            temp.File(@"links\name\data.txt");
            var target = temp.PathOf("target");
            UseCopyMove();

            // A file appears where the target folder should be created
            dialogs.Progress.BeforeCopy = () => File.WriteAllText(target, "in the way");

            await Create("name", target);

            Assert.Equal(Res.MessageBoxErrorTitle, dialogs.Last.Title);
            Assert.StartsWith(Format(Res.MoveFailed, string.Empty), dialogs.Last.Message);
            Assert.True(File.Exists(temp.PathOf("links", "name", "data.txt")));
            Assert.False(File.Exists(target));
            Assert.False(Directory.Exists(target));
            Assert.Empty(runner.Calls);
        }

        [Fact]
        public async Task Move_LinkFailure_OnTheSameDrive_PutsEverythingBack()
        {
            temp.File(@"links\name\data.txt");
            var target = temp.Dir("empty-target");
            runner.Handler = _ => LinkResult.Failed("no rights");

            await Create("name", target);

            Assert.Equal((Res.MessageBoxErrorTitle, Format(Res.MoveFailed, "no rights")), dialogs.Last);
            Assert.True(File.Exists(temp.PathOf("links", "name", "data.txt")));
            Assert.True(Directory.Exists(target));
            Assert.Empty(Directory.EnumerateFileSystemEntries(target));
        }

        [Fact]
        public async Task Move_LinkFailure_ToAnotherDrive_RemovesTheCopy()
        {
            temp.File(@"links\name\data.txt");
            var target = temp.PathOf("target");
            UseCopyMove();
            runner.Handler = _ => LinkResult.Failed("no rights");

            await Create("name", target);

            Assert.Equal((Res.MessageBoxErrorTitle, Format(Res.MoveFailed, "no rights")), dialogs.Last);
            Assert.True(File.Exists(temp.PathOf("links", "name", "data.txt")));
            Assert.False(Directory.Exists(target));
            Assert.False(Directory.Exists(temp.PathOf("links", "name.symlinker-move")));
        }

        [Fact]
        public async Task Move_RollbackFailure_SaysWhereTheFilesAre()
        {
            temp.File(@"links\name\data.txt");
            var target = temp.PathOf("target");

            // Something takes the original's place before it can be put back
            runner.Handler = _ =>
            {
                File.WriteAllText(temp.PathOf("links", "name"), "in the way");
                return LinkResult.Failed("no rights");
            };

            await Create("name", target);

            Assert.Equal((Res.MessageBoxErrorTitle, Format(Res.MoveRollbackFailed, target)), dialogs.Last);
            Assert.True(File.Exists(Path.Combine(target, "data.txt")));
        }

        [Fact]
        public async Task Move_CleanupFailure_StillReportsSuccess()
        {
            temp.File(@"links\name\data.txt");
            var target = temp.PathOf("target");
            var staging = temp.PathOf("links", "name.symlinker-move");
            UseCopyMove();
            FileStream lockedFile = null;

            // Something holds a file open in the old copy, so it can't be removed
            runner.Handler = _ =>
            {
                lockedFile = new FileStream(Path.Combine(staging, "data.txt"), FileMode.Open, FileAccess.Read, FileShare.None);
                return FakeLinkRunner.Created;
            };

            try
            {
                await Create("name", target);
            }
            finally
            {
                lockedFile?.Dispose();
            }

            Assert.Equal((Res.MessageBoxSuccessTitle, Format(Res.MoveCleanupFailed, staging)), dialogs.Last);
            Assert.True(File.Exists(Path.Combine(target, "data.txt")));
        }

        [Fact]
        public async Task Move_ThatFailsToStart_LeavesTheOriginalInPlace()
        {
            var original = temp.File(@"links\name.txt", "data");
            var target = temp.PathOf("moved.txt");

            using (new FileStream(original, FileMode.Open, FileAccess.Read, FileShare.None))
                await Create("name.txt", target, isFolder: false);

            Assert.Equal(Res.MessageBoxErrorTitle, dialogs.Last.Title);
            Assert.StartsWith(Format(Res.MoveFailed, string.Empty), dialogs.Last.Message);
            Assert.Equal("data", File.ReadAllText(original));
            Assert.False(File.Exists(target));
            Assert.Empty(runner.Calls);
        }
    }
}
