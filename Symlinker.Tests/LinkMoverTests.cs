namespace Symlinker.Tests
{
    using System;
    using System.IO;
    using System.Threading;

    /// <summary>
    /// Runs against the real file system. Symbolic links need Developer Mode or an elevated prompt.
    /// </summary>
    public sealed class LinkMoverTests : IDisposable
    {
        private static readonly DateTime OldTime = new(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);

        private readonly TempDir temp = new();

        public void Dispose() => temp.Dispose();

        [Fact]
        public void IsLink_IsFalse_ForRegularAndMissingEntries()
        {
            Assert.False(LinkMover.IsLink(new DirectoryInfo(temp.Dir("folder"))));
            Assert.False(LinkMover.IsLink(new FileInfo(temp.File("file.txt"))));
            Assert.False(LinkMover.IsLink(new DirectoryInfo(temp.PathOf("missing"))));
            Assert.False(LinkMover.IsLink(new FileInfo(temp.PathOf("missing", "file.txt"))));
        }

        [Fact]
        public void IsLink_IsTrue_ForSymbolicLinks()
        {
            var folderLink = Directory.CreateSymbolicLink(temp.PathOf("folder-link"), temp.Dir("folder"));
            var fileLink = File.CreateSymbolicLink(temp.PathOf("file-link"), temp.File("file.txt"));

            Assert.True(LinkMover.IsLink(folderLink));
            Assert.True(LinkMover.IsLink(fileLink));
        }

        [Theory]
        [InlineData(@"C:\data", @"C:\data", true)]
        [InlineData(@"C:\Data\", @"c:\data", true)]
        [InlineData(@"C:\data\inner\file", @"C:\data", true)]
        [InlineData(@"C:\database", @"C:\data", false)]
        [InlineData(@"C:\data", @"C:\data\inner", false)]
        [InlineData(@"D:\data", @"C:\data", false)]
        public void IsSameOrInside_ComparesWholePathParts(string path, string parent, bool expected)
        {
            Assert.Equal(expected, LinkMover.IsSameOrInside(path, parent));
        }

        [Theory]
        [InlineData(@"C:\a", @"c:\b\c", true)]
        [InlineData(@"C:\a", @"D:\a", false)]
        public void IsSameVolume_ComparesDrives(string first, string second, bool expected)
        {
            Assert.Equal(expected, LinkMover.IsSameVolume(first, second));
        }

        [Fact]
        public void GetUnusedPath_NumbersPathsThatAreTaken()
        {
            var path = temp.PathOf("item");
            Assert.Equal(path, LinkMover.GetUnusedPath(path));

            temp.File("item");
            Assert.Equal(path + "2", LinkMover.GetUnusedPath(path));

            temp.Dir("item2");
            Assert.Equal(path + "3", LinkMover.GetUnusedPath(path));
        }

        [Fact]
        public void Rename_MovesFilesAndFolders()
        {
            temp.File(@"folder\inside.txt");
            temp.File("file.txt");

            LinkMover.Rename(temp.PathOf("folder"), temp.PathOf("folder-moved"));
            LinkMover.Rename(temp.PathOf("file.txt"), temp.PathOf("file-moved.txt"));

            Assert.True(File.Exists(temp.PathOf("folder-moved", "inside.txt")));
            Assert.True(File.Exists(temp.PathOf("file-moved.txt")));
            Assert.False(Directory.Exists(temp.PathOf("folder")));
            Assert.False(File.Exists(temp.PathOf("file.txt")));
        }

        [Fact]
        public void GetTotalBytes_CountsFilesButNotNestedLinks()
        {
            var file = temp.File(@"source\a.txt", "12345");
            var hidden = temp.File(@"source\nested\hidden.txt", "123");
            File.SetAttributes(hidden, FileAttributes.Hidden);
            temp.File(@"elsewhere\big.txt", new string('x', 1000));
            Directory.CreateSymbolicLink(temp.PathOf("source", "link"), temp.PathOf("elsewhere"));

            Assert.Equal(5, LinkMover.GetTotalBytes(new FileInfo(file)));
            Assert.Equal(8, LinkMover.GetTotalBytes(new DirectoryInfo(temp.PathOf("source"))));
        }

        [Fact]
        public void Copy_File_KeepsContentTimesAndAttributes()
        {
            var source = temp.File("source.txt", "hello");
            File.SetCreationTimeUtc(source, OldTime);
            File.SetLastWriteTimeUtc(source, OldTime);
            File.SetAttributes(source, FileAttributes.ReadOnly | FileAttributes.Hidden);
            long copied = 0;

            LinkMover.Copy(new FileInfo(source), temp.PathOf("copy.txt"), bytes => copied += bytes, CancellationToken.None);

            var copy = new FileInfo(temp.PathOf("copy.txt"));
            Assert.Equal("hello", File.ReadAllText(copy.FullName));
            Assert.Equal(5, copied);
            Assert.Equal(OldTime, copy.CreationTimeUtc);
            Assert.Equal(OldTime, copy.LastWriteTimeUtc);
            Assert.Equal(FileAttributes.ReadOnly | FileAttributes.Hidden, copy.Attributes);
        }

        [Fact]
        public void Copy_File_WithoutAttributes_IsNormal()
        {
            var source = temp.File("source.txt");
            File.SetAttributes(source, FileAttributes.Normal);

            LinkMover.Copy(new FileInfo(source), temp.PathOf("copy.txt"), _ => { }, CancellationToken.None);

            Assert.Equal(FileAttributes.Normal, File.GetAttributes(temp.PathOf("copy.txt")));
        }

        [Fact]
        public void Copy_Folder_CopiesEverythingAndRecreatesLinks()
        {
            var linkedFolder = temp.Dir("linked-folder");
            var linkedFile = temp.File("linked.txt");

            temp.File(@"source\a.txt", "aaa");
            var hidden = temp.File(@"source\nested\hidden.txt", "bb");
            File.SetAttributes(hidden, FileAttributes.Hidden);
            temp.Dir(@"source\empty");
            Directory.CreateSymbolicLink(temp.PathOf("source", "folder-link"), linkedFolder);
            File.CreateSymbolicLink(temp.PathOf("source", "file-link.txt"), linkedFile);

            var source = new DirectoryInfo(temp.PathOf("source"));
            source.Attributes |= FileAttributes.ReadOnly;
            source.CreationTimeUtc = OldTime;
            source.LastWriteTimeUtc = OldTime;
            long copied = 0;

            LinkMover.Copy(source, temp.PathOf("copy"), bytes => copied += bytes, CancellationToken.None);

            var copy = new DirectoryInfo(temp.PathOf("copy"));
            Assert.Equal("aaa", File.ReadAllText(temp.PathOf("copy", "a.txt")));
            Assert.Equal("bb", File.ReadAllText(temp.PathOf("copy", "nested", "hidden.txt")));
            Assert.True(File.GetAttributes(temp.PathOf("copy", "nested", "hidden.txt")).HasFlag(FileAttributes.Hidden));
            Assert.True(Directory.Exists(temp.PathOf("copy", "empty")));
            Assert.Equal(linkedFolder, new DirectoryInfo(temp.PathOf("copy", "folder-link")).LinkTarget);
            Assert.Equal(linkedFile, new FileInfo(temp.PathOf("copy", "file-link.txt")).LinkTarget);
            Assert.Equal(5, copied);
            Assert.Equal(OldTime, copy.CreationTimeUtc);
            Assert.Equal(OldTime, copy.LastWriteTimeUtc);
            Assert.True(copy.Attributes.HasFlag(FileAttributes.ReadOnly));
        }

        [Fact]
        public void Copy_StopsWhenCanceled()
        {
            var file = temp.File(@"source\a.txt");
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();

            Assert.Throws<OperationCanceledException>(
                () => LinkMover.Copy(new FileInfo(file), temp.PathOf("file-copy"), _ => { }, canceled.Token));
            Assert.Throws<OperationCanceledException>(
                () => LinkMover.Copy(new DirectoryInfo(temp.PathOf("source")), temp.PathOf("folder-copy"), _ => { }, canceled.Token));
        }

        [Fact]
        public void Delete_IgnoresMissingPaths()
        {
            LinkMover.Delete(temp.PathOf("missing"));
        }

        [Fact]
        public void Delete_RemovesReadOnlyFiles()
        {
            var file = temp.File("file.txt");
            File.SetAttributes(file, FileAttributes.ReadOnly);

            LinkMover.Delete(file);

            Assert.False(File.Exists(file));
        }

        [Fact]
        public void Delete_OnALink_KeepsWhatItPointsTo()
        {
            var target = temp.Dir("target");
            temp.File(@"target\keep.txt");
            var link = temp.PathOf("link");
            Directory.CreateSymbolicLink(link, target);

            LinkMover.Delete(link);

            Assert.False(Directory.Exists(link));
            Assert.True(File.Exists(Path.Combine(target, "keep.txt")));
        }

        [Fact]
        public void Delete_RemovesReadOnlyContentButNotLinkedFiles()
        {
            var linkedFolder = temp.Dir("linked-folder");
            var kept = temp.File(@"linked-folder\keep.txt");
            var linkedFile = temp.File("linked.txt");

            var readOnly = temp.File(@"folder\nested\read-only.txt");
            File.SetAttributes(readOnly, FileAttributes.ReadOnly);
            temp.File(@"folder\plain.txt");
            var readOnlyFolder = new DirectoryInfo(temp.Dir(@"folder\read-only-folder"));
            readOnlyFolder.Attributes |= FileAttributes.ReadOnly;
            Directory.CreateSymbolicLink(temp.PathOf("folder", "folder-link"), linkedFolder);
            File.CreateSymbolicLink(temp.PathOf("folder", "file-link.txt"), linkedFile);
            var folder = new DirectoryInfo(temp.PathOf("folder"));
            folder.Attributes |= FileAttributes.ReadOnly;

            LinkMover.Delete(folder.FullName);

            Assert.False(Directory.Exists(folder.FullName));
            Assert.True(File.Exists(kept));
            Assert.True(File.Exists(linkedFile));
        }
    }
}
