using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UniversalDownloader.Models;
using UniversalDownloader.Services;
using Xunit;

namespace UniversalDownloader.Tests
{
    public class GoogleDriveTests
    {
        [Theory]
        [InlineData("https://drive.google.com/drive/folders/1fujj2taRaun-dtRSwq4lVRi3uD3TmxP7", true)]
        [InlineData("https://drive.google.com/drive/u/0/folders/1fujj2taRaun-dtRSwq4lVRi3uD3TmxP7", true)]
        [InlineData("https://drive.google.com/drive/u/1/folders/1CkgsJnaS90f2OhN_-peEiiT_tgMyVQGH?usp=sharing", true)]
        [InlineData("https://drive.google.com/open?id=1fujj2taRaun-dtRSwq4lVRi3uD3TmxP7", true)]
        [InlineData("https://drive.google.com/file/d/1234567890abcdef/view", false)]
        [InlineData("https://www.youtube.com/watch?v=12345", false)]
        public void IsGoogleDriveFolderUrl_IdentifiesFolderUrlsCorrectly(string url, bool expected)
        {
            bool result = GoogleDriveFolderService.IsGoogleDriveFolderUrl(url);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData("https://drive.google.com/drive/folders/1fujj2taRaun-dtRSwq4lVRi3uD3TmxP7", "1fujj2taRaun-dtRSwq4lVRi3uD3TmxP7")]
        [InlineData("https://drive.google.com/drive/u/0/folders/1fujj2taRaun-dtRSwq4lVRi3uD3TmxP7?usp=sharing", "1fujj2taRaun-dtRSwq4lVRi3uD3TmxP7")]
        [InlineData("https://drive.google.com/open?id=1vSbSGuoAqyaF2tuXRL-eHZmFN8C_fxH6", "1vSbSGuoAqyaF2tuXRL-eHZmFN8C_fxH6")]
        public void ExtractFolderId_ExtractsCorrectId(string url, string expectedId)
        {
            string? id = GoogleDriveFolderService.ExtractFolderId(url);
            Assert.Equal(expectedId, id);
        }

        [Fact]
        public void GoogleDriveItem_SelectionPropagation_WorksBothWays()
        {
            var parentFolder = new GoogleDriveItem
            {
                Id = "root",
                Name = "Parent",
                IsFolder = true,
                IsSelected = true
            };

            var child1 = new GoogleDriveItem
            {
                Id = "f1",
                Name = "File1.mp4",
                IsFolder = false,
                Parent = parentFolder,
                IsSelected = true
            };

            var child2 = new GoogleDriveItem
            {
                Id = "f2",
                Name = "File2.mp4",
                IsFolder = false,
                Parent = parentFolder,
                IsSelected = true
            };

            parentFolder.Children.Add(child1);
            parentFolder.Children.Add(child2);

            // Both selected -> parent is true
            Assert.True(parentFolder.IsSelected);

            // Deselect child1 -> parent becomes null (indeterminate)
            child1.IsSelected = false;
            Assert.Null(parentFolder.IsSelected);

            // Deselect child2 -> parent becomes false
            child2.IsSelected = false;
            Assert.False(parentFolder.IsSelected);

            // Selecting parent sets all children to true
            parentFolder.IsSelected = true;
            Assert.True(child1.IsSelected);
            Assert.True(child2.IsSelected);
        }

        [Fact]
        public void FolderStructureRule_SingleSubfolder_ResolvesDirectSubfolderPath()
        {
            // Simulate user choosing C:\Downloads as destination
            string selectedDirectory = @"C:\Downloads";

            // User selects only items inside Footage
            var selectedFiles = new List<GoogleDriveItem>
            {
                new() { Id = "1", Name = "AA000101.MXF", RelativePath = "Footage/Canon XF100 CAM 1/AA0001/AA000101.MXF" },
                new() { Id = "2", Name = "AA0001.CIF", RelativePath = "Footage/Canon XF100 CAM 1/AA0001/AA0001.CIF" }
            };

            var topLevelParts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in selectedFiles)
            {
                int slashIdx = file.RelativePath.IndexOf('/');
                if (slashIdx > 0)
                {
                    topLevelParts.Add(file.RelativePath.Substring(0, slashIdx));
                }
                else
                {
                    topLevelParts.Add("");
                }
            }

            bool isSingleSubfolderOnly = topLevelParts.Count == 1 && !topLevelParts.Contains("");
            Assert.True(isSingleSubfolderOnly);

            string baseTargetDir = selectedDirectory; // Directly inside C:\Downloads without root catalog wrapper
            string file1Relative = selectedFiles[0].RelativePath.Replace('/', Path.DirectorySeparatorChar);
            string finalPath = Path.Combine(baseTargetDir, file1Relative);

            Assert.Equal(@"C:\Downloads\Footage\Canon XF100 CAM 1\AA0001\AA000101.MXF", finalPath);
        }

        [Fact]
        public void FolderStructureRule_MultipleSubfolders_ResolvesRootCatalogWrapperPath()
        {
            string selectedDirectory = @"C:\Downloads";
            string rootCatalogTitle = "Erica & Niall's Wedding (Fri. 12th June)";

            // User selects items from Footage AND Music
            var selectedFiles = new List<GoogleDriveItem>
            {
                new() { Id = "1", Name = "AA000101.MXF", RelativePath = "Footage/Canon XF100 CAM 1/AA0001/AA000101.MXF" },
                new() { Id = "2", Name = "Song.mp3", RelativePath = "Music/Song.mp3" }
            };

            var topLevelParts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in selectedFiles)
            {
                int slashIdx = file.RelativePath.IndexOf('/');
                if (slashIdx > 0)
                {
                    topLevelParts.Add(file.RelativePath.Substring(0, slashIdx));
                }
                else
                {
                    topLevelParts.Add("");
                }
            }

            bool isSingleSubfolderOnly = topLevelParts.Count == 1 && !topLevelParts.Contains("");
            Assert.False(isSingleSubfolderOnly);

            string baseTargetDir = Path.Combine(selectedDirectory, rootCatalogTitle);
            string file1Relative = selectedFiles[0].RelativePath.Replace('/', Path.DirectorySeparatorChar);
            string file2Relative = selectedFiles[1].RelativePath.Replace('/', Path.DirectorySeparatorChar);

            string finalPath1 = Path.Combine(baseTargetDir, file1Relative);
            string finalPath2 = Path.Combine(baseTargetDir, file2Relative);

            Assert.Equal(@"C:\Downloads\Erica & Niall's Wedding (Fri. 12th June)\Footage\Canon XF100 CAM 1\AA0001\AA000101.MXF", finalPath1);
            Assert.Equal(@"C:\Downloads\Erica & Niall's Wedding (Fri. 12th June)\Music\Song.mp3", finalPath2);
        }

        [Fact]
        public async Task GoogleDriveFolderService_LiveTestUrl_ParsesStructure()
        {
            var service = new GoogleDriveFolderService();
            string testUrl = "https://drive.google.com/drive/folders/1fujj2taRaun-dtRSwq4lVRi3uD3TmxP7";

            var result = await service.FetchFolderTreeAsync(testUrl);

            Assert.NotNull(result);
            Assert.Contains("Erica", result.RootTitle, StringComparison.OrdinalIgnoreCase);
            Assert.True(result.Items.Count >= 2);

            var footage = result.Items.FirstOrDefault(i => i.Name.Equals("Footage", StringComparison.OrdinalIgnoreCase));
            var music = result.Items.FirstOrDefault(i => i.Name.Equals("Music", StringComparison.OrdinalIgnoreCase));

            Assert.NotNull(footage);
            Assert.True(footage.IsFolder);
            Assert.NotEmpty(footage.Children);

            Assert.NotNull(music);
            Assert.True(music.IsFolder);
            Assert.NotEmpty(music.Children);

            // Music contains audio files
            var anyMusicFile = music.Children.FirstOrDefault(c => !c.IsFolder);
            Assert.NotNull(anyMusicFile);
            Assert.EndsWith(".mp3", anyMusicFile.Name, StringComparison.OrdinalIgnoreCase);
        }
    }
}
