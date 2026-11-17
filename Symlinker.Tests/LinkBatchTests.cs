namespace Symlinker.Tests
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;

    using Res = Symlinker.Properties.Resources;

    /// <summary>The list of links behind batch mode, and each row's display and auto-filled name.</summary>
    public sealed class LinkBatchTests : IDisposable
    {
        private readonly TempDir temp = new();
        private readonly LinkBatch batch = new();

        public void Dispose() => temp.Dispose();

        private static LinkRow Row(string location, string name, string target)
        {
            var row = new LinkRow { LinkLocation = location, LinkName = name };
            row.SetTarget(target);
            return row;
        }

        // ---- LinkRow ----

        [Fact]
        public void Row_StartsEmpty()
        {
            var row = new LinkRow();

            Assert.Equal((string.Empty, string.Empty, string.Empty), (row.LinkLocation, row.LinkName, row.Target));
            Assert.Equal((string.Empty, string.Empty, string.Empty, string.Empty), (row.LinkPath, row.LinkFolder, row.TargetFolder, row.TargetName));
            Assert.False(row.HasError);
            Assert.Null(row.ErrorText);
            Assert.Empty(row.Errors);
        }

        [Fact]
        public void Row_TreatsNullAsEmpty()
        {
            var row = new LinkRow { LinkLocation = null, LinkName = null };
            row.SetTarget(null);

            Assert.Equal((string.Empty, string.Empty, string.Empty), (row.LinkLocation, row.LinkName, row.Target));
        }

        [Theory]
        [InlineData(@"C:\links", "name", @"C:\links\", @"C:\links\name")]
        [InlineData(@" C:\links\ ", " name ", @"C:\links\", @"C:\links\name")]
        [InlineData(@"D:\", "name", @"D:\", @"D:\name")]
        [InlineData("", "name", "", "name")]
        public void Row_ShowsTheLinkPath(string location, string name, string folder, string path)
        {
            var row = new LinkRow { LinkLocation = location, LinkName = name };

            Assert.Equal((folder, path), (row.LinkFolder, row.LinkPath));
        }

        [Theory]
        [InlineData(@"C:\data\Photos", @"C:\data\", "Photos")]
        [InlineData(@"C:\data\Photos\", @"C:\data\", "Photos")]
        [InlineData(@"D:\", "", "D:")]
        [InlineData("Photos", "", "Photos")]
        public void Row_SplitsTheTarget(string target, string folder, string name)
        {
            var row = Row(string.Empty, string.Empty, target);

            Assert.Equal((folder, name), (row.TargetFolder, row.TargetName));
        }

        [Fact]
        public void Row_FillsInTheNameFromTheTarget_UntilTheUserTypesOne()
        {
            var row = new LinkRow();

            row.SetTarget(@"C:\data\Photos");
            Assert.Equal("Photos", row.LinkName);

            row.SetTarget(@"C:\data\Music");
            Assert.Equal("Music", row.LinkName);

            row.LinkName = "Tunes";
            row.SetTarget(@"C:\data\Videos");
            Assert.Equal("Tunes", row.LinkName);
            Assert.Equal(@"C:\data\Videos", row.Target);
        }

        [Fact]
        public void Row_AutoFill_IsPerRow()
        {
            var first = new LinkRow();
            var second = new LinkRow();

            first.SetTarget(@"C:\data\Photos");
            second.SetTarget(@"C:\data\Photos");
            first.LinkName = "Typed";
            second.SetTarget(@"C:\data\Music");

            Assert.Equal(("Typed", "Music"), (first.LinkName, second.LinkName));
        }

        [Fact]
        public void Row_RaisesChanges_OnlyWhenAValueChanges()
        {
            var row = new LinkRow();
            var raised = new List<string>();
            row.PropertyChanged += (s, e) => raised.Add(e.PropertyName);

            row.LinkLocation = @"C:\links";
            row.LinkLocation = @"C:\links";
            row.LinkName = "name";
            row.SetTarget(@"C:\data\target");

            // Location, name, then target (whose auto-filled name is skipped: the user's name stays)
            Assert.Equal(3, raised.Count);
            Assert.All(raised, name => Assert.Equal(string.Empty, name));
        }

        [Fact]
        public void Row_Errors_AreSetAndClearedPerField()
        {
            var row = new LinkRow();
            var raised = 0;
            row.PropertyChanged += (s, e) => raised++;

            row.SetErrors(new[] { (InputField.LinkName, "name"), (InputField.Target, "target") });
            Assert.True(row.HasError);
            Assert.Equal("name", row.ErrorText);

            row.ClearError(InputField.LinkLocation);
            Assert.Equal(2, row.Errors.Count);

            row.ClearError(InputField.LinkName);
            Assert.Equal("target", row.ErrorText);

            Assert.Equal(2, raised);
        }

        [Fact]
        public void Row_ToInput_CarriesTheModeAndKind()
        {
            var row = Row(@"C:\links", "name", @"C:\data\target");

            Assert.Equal(new LinkInput(@"C:\links", "name", @"C:\data\target", false, LinkKind.Hard), row.ToInput(false, LinkKind.Hard));
        }

        // ---- Adding and removing rows ----

        [Fact]
        public void Batch_StartsWithOneSelectedRow()
        {
            Assert.Same(batch.Selected, Assert.Single(batch.Rows));
            Assert.False(batch.IsBatch);
        }

        [Fact]
        public void Add_StartsInTheLastRowsLinkLocation_AndSelectsIt()
        {
            batch.Selected.LinkLocation = @"C:\first";
            batch.Rows.Add(new LinkRow { LinkLocation = @"C:\last" });

            var row = batch.Add();

            Assert.Same(row, batch.Rows[^1]);
            Assert.Same(row, batch.Selected);
            Assert.Equal(@"C:\last", row.LinkLocation);
            Assert.Equal((string.Empty, string.Empty), (row.LinkName, row.Target));
            Assert.True(batch.IsBatch);
        }

        [Fact]
        public void Remove_KeepsTheLastRow()
        {
            var only = batch.Selected;

            batch.Remove(only);

            Assert.Same(only, Assert.Single(batch.Rows));
        }

        [Fact]
        public void Remove_IgnoresARowNotInTheList()
        {
            batch.Add();

            batch.Remove(new LinkRow());

            Assert.Equal(2, batch.Rows.Count);
        }

        [Fact]
        public void Remove_MovesTheSelectionToTheNextRow_OrThePreviousAtTheEnd()
        {
            var first = batch.Selected;
            var second = batch.Add();
            var third = batch.Add();

            batch.Selected = second;
            batch.Remove(second);
            Assert.Same(third, batch.Selected);

            batch.Remove(third);
            Assert.Same(first, batch.Selected);
            Assert.False(batch.IsBatch);
        }

        [Fact]
        public void Remove_KeepsTheSelection_WhenAnotherRowGoes()
        {
            var first = batch.Selected;
            var second = batch.Add();

            batch.Remove(first);

            Assert.Same(second, batch.Selected);
            Assert.Same(second, Assert.Single(batch.Rows));
        }

        // ---- Several targets at once ----

        [Fact]
        public void AddTargets_DoesNothing_WithNoTargets()
        {
            batch.AddTargets(Array.Empty<string>());

            Assert.Single(batch.Rows);
            Assert.Equal(string.Empty, batch.Selected.Target);
        }

        [Fact]
        public void AddTargets_FillsTheSelectedRow_ThenAddsARowForEachOther()
        {
            var first = batch.Selected;
            first.LinkLocation = @"C:\FolderB";
            var last = batch.Add();
            batch.Selected = first;

            batch.AddTargets(new[] { @"C:\FolderA\One", @"C:\FolderA\Two", @"C:\FolderA\Three" });

            Assert.Equal(
                new[] { (@"C:\FolderB", "One", @"C:\FolderA\One"), (@"C:\FolderB", "Two", @"C:\FolderA\Two"), (@"C:\FolderB", "Three", @"C:\FolderA\Three"), (@"C:\FolderB", string.Empty, string.Empty) },
                batch.Rows.Select(row => (row.LinkLocation, row.LinkName, row.Target)));
            Assert.Same(first, batch.Selected);
            Assert.Same(last, batch.Rows[^1]);
        }

        // ---- Link location ----

        [Fact]
        public void SetLocation_MovesTheRowsThatSharedIt_AndTheEmptyOnes()
        {
            var first = batch.Selected;
            first.LinkLocation = @"C:\old";
            var sameFolder = batch.Add();
            var empty = batch.Add();
            empty.LinkLocation = " ";
            var other = batch.Add();
            other.LinkLocation = @"C:\other";
            batch.Selected = first;

            batch.SetLocation(@"C:\new");

            Assert.Equal(new[] { @"C:\new", @"C:\new", @"C:\new", @"C:\other" }, batch.Rows.Select(row => row.LinkLocation));
        }

        // ---- Where the target picker opens ----

        [Fact]
        public void TargetHint_IsTheRowsOwnTarget_ElseTheNearestOneAbove_ElseNull()
        {
            var first = batch.Selected;
            Assert.Null(batch.TargetHint(first));

            first.SetTarget(@"C:\data\one");
            var second = batch.Add();
            second.SetTarget(@"C:\data\two");
            var third = batch.Add();
            third.SetTarget("  ");

            Assert.Equal(@"C:\data\two", batch.TargetHint(third));
            Assert.Equal(@"C:\data\one", batch.TargetHint(first));
        }

        // ---- Validation ----

        [Fact]
        public void Validate_PassesGoodRows()
        {
            var location = temp.Dir("links");
            batch.Selected.LinkLocation = location;
            batch.Selected.SetTarget(temp.Dir("one"));
            batch.Add().SetTarget(temp.Dir("two"));

            Assert.True(batch.Validate(isFolder: true));
            Assert.All(batch.Rows, row => Assert.False(row.HasError));
        }

        [Fact]
        public void Validate_MarksEachBadRow()
        {
            var location = temp.Dir("links");
            var file = temp.File("file.txt");
            batch.Selected.LinkLocation = location;
            batch.Selected.SetTarget(temp.Dir("good"));
            var mismatch = batch.Add();
            mismatch.SetTarget(file);
            var missing = batch.Add();

            Assert.False(batch.Validate(isFolder: true));

            Assert.False(batch.Rows[0].HasError);
            Assert.Equal(Res.ErrorTargetIsFile, mismatch.ErrorText);
            Assert.Equal(Res.ErrorLinkNameMissing, missing.ErrorText);
        }

        [Fact]
        public void Validate_MarksTheSecondRowCreatingTheSameLink()
        {
            var location = temp.Dir("links");
            var target = temp.Dir("target");
            batch.Selected.LinkLocation = location;
            batch.Selected.SetTarget(target);
            var duplicate = batch.Add();
            duplicate.LinkLocation = location + "\\";
            duplicate.SetTarget(temp.Dir("other", "TARGET"));
            var otherFolder = batch.Add();
            otherFolder.LinkLocation = temp.Dir("elsewhere");
            otherFolder.SetTarget(target);

            Assert.False(batch.Validate(isFolder: true));

            Assert.False(batch.Rows[0].HasError);
            Assert.Equal((InputField.LinkName, Res.ErrorDuplicateLinkPath), Assert.Single(duplicate.Errors));
            Assert.False(otherFolder.HasError);
        }

        [Fact]
        public void Validate_SkipsTheDuplicateCheck_ForRowsWithABadNameOrLocation()
        {
            var location = temp.Dir("links");
            batch.Selected.LinkLocation = location;
            batch.Selected.SetTarget(temp.Dir("target"));
            var badName = batch.Add();
            badName.LinkName = "bad|name";
            badName.SetTarget(temp.Dir("target2"));

            Assert.False(batch.Validate(isFolder: true));

            Assert.Equal((InputField.LinkName, Res.ErrorLinkNameInvalid), Assert.Single(badName.Errors));
        }

        [Fact]
        public void ClearErrors_ClearsEveryRow()
        {
            batch.Add();
            batch.Validate(isFolder: true);

            batch.ClearErrors();

            Assert.All(batch.Rows, row => Assert.False(row.HasError));
        }

        // ---- After creating ----

        [Fact]
        public void RemoveCreated_KeepsEverything_WhenAllWereCreated()
        {
            batch.Add();
            var rows = batch.Rows.ToList();
            var created = new LinkOutcome(LinkStatus.Created, "t", "m");

            batch.RemoveCreated(rows, new[] { created, created });

            Assert.Equal(rows, batch.Rows);
        }

        [Fact]
        public void RemoveCreated_LeavesOnlyTheLinksStillToDo()
        {
            var first = batch.Selected;
            var second = batch.Add();
            var third = batch.Add();
            var rows = batch.Rows.ToList();

            batch.RemoveCreated(rows, new[]
            {
                new LinkOutcome(LinkStatus.Created, "t", "m"),
                new LinkOutcome(LinkStatus.Failed, "t", "m"),
                new LinkOutcome(LinkStatus.Skipped, "t", "m"),
            });

            Assert.Equal(new[] { second, third }, batch.Rows);
            Assert.DoesNotContain(first, batch.Rows);
        }
    }
}
