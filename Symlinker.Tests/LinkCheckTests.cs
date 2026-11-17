namespace Symlinker.Tests
{
    using System;
    using System.IO;
    using System.Security.AccessControl;
    using System.Security.Principal;
    using System.Threading.Tasks;

    public sealed class LinkCheckTests : IDisposable
    {
        private readonly TempDir temp = new();

        public void Dispose() => temp.Dispose();

        private static LinkRequest Request(string type, string link, string target, bool directory = true) =>
            new() { Type = type, Link = link, Target = target, Directory = directory };

        // ---- IsCreated ----

        [Fact]
        public void IsCreated_AcceptsAJunctionToTheTarget_WithOrWithoutATrailingSeparator()
        {
            var target = temp.Dir("target");
            var link = temp.Junction(temp.PathOf("link"), target).FullName;

            Assert.True(LinkCheck.IsCreated(Request("junction", link, target)));
            Assert.True(LinkCheck.IsCreated(Request("junction", link, target + "\\")));
        }

        [Fact]
        public void IsCreated_RejectsALinkThatPointsSomewhereElse()
        {
            var target = temp.Dir("target");
            var link = temp.Junction(temp.PathOf("link"), temp.Dir("other")).FullName;

            Assert.False(LinkCheck.IsCreated(Request("junction", link, target)));
        }

        [Fact]
        public void IsCreated_RejectsAPlainFolderOrNothing()
        {
            var target = temp.Dir("target");

            Assert.False(LinkCheck.IsCreated(Request("junction", temp.Dir("plain"), target)));
            Assert.False(LinkCheck.IsCreated(Request("symbolic", temp.PathOf("missing"), target)));
        }

        [Fact]
        public void IsCreated_ResolvesARelativeSymbolicLinkTarget_AgainstTheLinksFolder()
        {
            SymlinkSupport.Require();
            temp.File(@"files\target.txt");
            var link = temp.PathOf("links", "link.txt");
            Directory.CreateDirectory(temp.PathOf("links"));
            File.CreateSymbolicLink(link, @"..\files\target.txt");

            Assert.True(LinkCheck.IsCreated(Request("symbolic", link, @"..\files\target.txt", directory: false)));
            Assert.False(LinkCheck.IsCreated(Request("symbolic", link, @"..\files\other.txt", directory: false)));
        }

        [Fact]
        public async Task IsCreated_AcceptsAHardLinkToTheSameFile_Only()
        {
            var target = temp.File("target.txt");
            var link = temp.PathOf("link.txt");
            Assert.True((await LinkCommand.RunAsync(Request("hard", link, target, directory: false))).Success);
            var other = temp.File("other.txt");

            Assert.True(LinkCheck.IsCreated(Request("hard", link, target, directory: false)));
            Assert.False(LinkCheck.IsCreated(Request("hard", other, target, directory: false)));
            Assert.False(LinkCheck.IsCreated(Request("hard", temp.PathOf("missing.txt"), target, directory: false)));
        }

        [Fact]
        public void IsSameFile_AssumesYes_WhenAFileCantBeOpened()
        {
            var target = temp.File("target.txt");
            var other = temp.File("other.txt");

            using (new FileStream(other, FileMode.Open, FileAccess.Read, FileShare.None))
                Assert.True(LinkCheck.IsSameFile(target, other));

            Assert.False(LinkCheck.IsSameFile(target, other));
        }

        // ---- Diagnose ----

        private static LinkError Diagnose(LinkRequest request, bool existedBefore = false, bool canCreateSymlinks = true, bool canWrite = true) =>
            LinkCheck.Diagnose(request, existedBefore, () => canCreateSymlinks, _ => canWrite);

        [Fact]
        public void Diagnose_ReportsAnExistingLinkPathFirst()
        {
            Assert.Equal(LinkError.LinkExists, Diagnose(Request("hard", @"C:\a\link", @"D:\b\target"), existedBefore: true, canWrite: false));
        }

        [Theory]
        [InlineData(@"D:\b\target", nameof(LinkError.HardLinkAcrossDrives))]
        [InlineData(@"C:\b\target", nameof(LinkError.Unknown))]
        public void Diagnose_ChecksHardLinksAreOnTheTargetsDrive(string target, string expected)
        {
            Assert.Equal(Enum.Parse<LinkError>(expected), Diagnose(Request("hard", @"C:\a\link", target, directory: false)));
        }

        [Theory]
        [InlineData(@"\\server\share\folder", nameof(LinkError.JunctionNeedsLocalFolder))]
        [InlineData(@"C:\folder", nameof(LinkError.Unknown))]
        public void Diagnose_ChecksJunctionsPointToALocalFolder(string target, string expected)
        {
            Assert.Equal(Enum.Parse<LinkError>(expected), Diagnose(Request("junction", @"C:\a\link", target)));
        }

        [Theory]
        [InlineData(false, nameof(LinkError.NeedsAdmin))]
        [InlineData(true, nameof(LinkError.Unknown))]
        public void Diagnose_ChecksTheSymbolicLinkPrivilege(bool canCreateSymlinks, string expected)
        {
            Assert.Equal(Enum.Parse<LinkError>(expected), Diagnose(Request("symbolic", @"C:\a\link", @"\\server\share"), canCreateSymlinks: canCreateSymlinks));
        }

        [Fact]
        public void Diagnose_ChecksTheLinksFolderCanBeWritten()
        {
            string folder = null;
            var error = LinkCheck.Diagnose(Request("junction", @"C:\a\link", @"C:\b"), false, () => true, f =>
            {
                folder = f;
                return false;
            });

            Assert.Equal((LinkError.AccessDenied, @"C:\a"), (error, folder));
        }

        // ---- Helpers ----

        [Fact]
        public void CanWriteTo_IsTrue_ForAWritableFolder_AndLeavesNothingBehind()
        {
            var folder = temp.Dir("open");

            Assert.True(LinkCheck.CanWriteTo(folder));
            Assert.Empty(Directory.GetFileSystemEntries(folder));
        }

        [Fact]
        public void CanWriteTo_IsTrue_WhenTheProblemIsNotAccess()
        {
            Assert.True(LinkCheck.CanWriteTo(temp.PathOf("missing")));
        }

        [Fact]
        public void CanWriteTo_IsFalse_WhenCreatingFilesIsDenied()
        {
            var folder = new DirectoryInfo(temp.Dir("locked"));
            var security = folder.GetAccessControl();
            var deny = new FileSystemAccessRule(WindowsIdentity.GetCurrent().User, FileSystemRights.CreateFiles, AccessControlType.Deny);
            security.AddAccessRule(deny);
            folder.SetAccessControl(security);
            try
            {
                Assert.False(LinkCheck.CanWriteTo(folder.FullName));
            }
            finally
            {
                security.RemoveAccessRule(deny);
                folder.SetAccessControl(security);
            }
        }

        [Theory]
        [InlineData(@"\\server\share\folder", true)]
        [InlineData(@"C:\folder", false)]
        public void IsNetworkPath_SpotsShares(string path, bool expected)
        {
            Assert.Equal(expected, LinkCheck.IsNetworkPath(path));
        }
    }
}
