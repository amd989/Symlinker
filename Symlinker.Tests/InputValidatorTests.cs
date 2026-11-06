namespace Symlinker.Tests
{
    using System;
    using System.Linq;
    using System.Windows.Media;

    using Res = Symlinker.Properties.Resources;

    public sealed class InputValidatorTests : IDisposable
    {
        private readonly TempDir temp = new();

        public InputValidatorTests()
        {
            temp.Dir("links");
            temp.Dir("target-folder");
            temp.File("target.txt");
            temp.Dir(@"links\taken-folder");
            temp.File(@"links\taken-file");
        }

        public void Dispose() => temp.Dispose();

        private string[] Validate(string name, string location, string target, bool isFolder = true) =>
            InputValidator.Validate(name, Resolve(location), Resolve(target), isFolder)
                .Select(error => $"{error.Field}: {error.Message}")
                .ToArray();

        // Names starting with "@" are paths inside the temp folder
        private string Resolve(string value) => value.StartsWith('@') ? temp.PathOf(value[1..]) : value;

        [Fact]
        public void Validate_AcceptsAGoodFolderLink()
        {
            Assert.Empty(Validate(" name ", "@links", "@target-folder"));
        }

        [Fact]
        public void Validate_AcceptsAGoodFileLink()
        {
            Assert.Empty(Validate("name.txt", "@links", "@target.txt", isFolder: false));
        }

        [Fact]
        public void Validate_ListsEveryProblemInScreenOrder()
        {
            Assert.Equal(
                new[]
                {
                    $"LinkName: {Res.ErrorLinkNameMissing}",
                    $"LinkLocation: {Res.ErrorLinkLocationMissing}",
                    $"Target: {Res.ErrorTargetFolderMissing}",
                },
                Validate(" ", string.Empty, "  "));
        }

        [Theory]
        [InlineData("a|b", "LinkName", nameof(Res.ErrorLinkNameInvalid))]
        [InlineData("a:b", "LinkName", nameof(Res.ErrorLinkNameInvalid))]
        public void Validate_RejectsBadNames(string name, string field, string resource)
        {
            Assert.Equal(new[] { $"{field}: {Res.ResourceManager.GetString(resource)}" }, Validate(name, "@links", "@target-folder"));
        }

        [Theory]
        [InlineData("links\"", nameof(Res.InvalidQuoteInPath))]
        [InlineData("@missing", nameof(Res.ErrorLinkLocationNotFound))]
        public void Validate_RejectsBadLocations(string location, string resource)
        {
            Assert.Equal(new[] { $"LinkLocation: {Res.ResourceManager.GetString(resource)}" }, Validate("name", location, "@target-folder"));
        }

        [Theory]
        [InlineData(true, "", nameof(Res.ErrorTargetFolderMissing))]
        [InlineData(false, "", nameof(Res.ErrorTargetFileMissing))]
        [InlineData(true, "\"quoted\"", nameof(Res.InvalidQuoteInPath))]
        [InlineData(true, "@target.txt", nameof(Res.ErrorTargetIsFile))]
        [InlineData(false, "@target-folder", nameof(Res.ErrorTargetIsFolder))]
        [InlineData(true, "@missing", nameof(Res.ErrorTargetFolderNotFound))]
        [InlineData(false, "@missing.txt", nameof(Res.ErrorTargetFileNotFound))]
        public void Validate_RejectsBadTargets(bool isFolder, string target, string resource)
        {
            Assert.Equal(new[] { $"Target: {Res.ResourceManager.GetString(resource)}" }, Validate("name", "@links", target, isFolder));
        }

        [Theory]
        [InlineData("taken-folder", true)]
        [InlineData("taken-file", false)]
        public void Validate_AllowsAMissingTarget_WhenSomethingIsAtTheLinkPath(string name, bool isFolder)
        {
            // Link creation offers to move the existing item to the target
            Assert.Empty(Validate(name, "@links", "@missing", isFolder));
        }

        [Fact]
        public void Validate_ReportsAMissingTarget_WhenTheLinkPathCantBeChecked()
        {
            Assert.Contains($"Target: {Res.ErrorTargetFolderNotFound}", Validate(string.Empty, "@links", "@missing"));
            Assert.Contains($"Target: {Res.ErrorTargetFolderNotFound}", Validate("taken-folder", string.Empty, "@missing"));
        }

        [Fact]
        public void TargetFitsMode_ChecksTheTargetMatchesTheMode()
        {
            Assert.False(InputValidator.TargetFitsMode("  ", folderMode: true));
            Assert.True(InputValidator.TargetFitsMode(temp.PathOf("target-folder"), folderMode: true));
            Assert.False(InputValidator.TargetFitsMode(temp.PathOf("target.txt"), folderMode: true));
            Assert.True(InputValidator.TargetFitsMode($" {temp.PathOf("target.txt")} ", folderMode: false));
            Assert.False(InputValidator.TargetFitsMode(temp.PathOf("target-folder"), folderMode: false));
        }

        [Fact]
        public void ContrastRatio_FollowsWcag()
        {
            Assert.Equal(21, ColorContrast.ContrastRatio(Colors.Black, Colors.White), 3);
            Assert.Equal(21, ColorContrast.ContrastRatio(Colors.White, Colors.Black), 3);
            Assert.Equal(1, ColorContrast.ContrastRatio(Colors.Gray, Colors.Gray), 3);

            // Very dark channels use the linear part of the sRGB curve
            Assert.Equal(1.061, ColorContrast.ContrastRatio(Color.FromRgb(10, 10, 10), Colors.Black), 3);
        }

        [Fact]
        public void Blend_MixesByAlpha()
        {
            Assert.Equal(Colors.Red, ColorContrast.Blend(Colors.Red, Colors.Blue));
            Assert.Equal(Colors.Blue, ColorContrast.Blend(Color.FromArgb(0, 255, 0, 0), Colors.Blue));
            Assert.Equal(Color.FromRgb(128, 128, 128), ColorContrast.Blend(Color.FromArgb(128, 255, 255, 255), Colors.Black));
        }

        [Fact]
        public void TextOn_PicksTheMoreReadableColor()
        {
            Assert.Equal(Colors.White, ColorContrast.TextOn(Color.FromRgb(0, 90, 158), null));
            Assert.Equal(Colors.Black, ColorContrast.TextOn(Color.FromRgb(255, 200, 60), null));

            // A faint dark accent over a white page looks light
            Assert.Equal(Colors.Black, ColorContrast.TextOn(Color.FromArgb(40, 0, 90, 158), Colors.White));
        }
    }
}
