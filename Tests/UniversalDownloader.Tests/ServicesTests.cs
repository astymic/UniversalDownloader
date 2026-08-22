using System;
using System.IO;
using System.Threading.Tasks;
using UniversalDownloader.Models;
using UniversalDownloader.Services;
using Xunit;

namespace UniversalDownloader.Tests
{
    public class ServicesTests
    {
        [Theory]
        [InlineData("https://www.youtube.com/watch?v=Tc2BafKqTE8", true)]
        [InlineData("https://youtu.be/Tc2BafKqTE8", true)]
        [InlineData("https://open.spotify.com/track/4cOdK2wGLETKBW3PvgPWqT", true)]
        [InlineData("https://www.instagram.com/reel/Dbs_KLsDirG/", true)]
        [InlineData("https://www.tiktok.com/@user/video/7123456789012345678", true)]
        [InlineData("https://x.com/username/status/1234567890123456789", true)]
        [InlineData("https://twitter.com/username/status/1234567890123456789", true)]
        [InlineData("https://www.reddit.com/r/videos/comments/123456/sample_video/", true)]
        [InlineData("https://soundcloud.com/artist/track-name", true)]
        [InlineData("https://drive.google.com/file/d/1234567890abcdef/view", true)]
        [InlineData("https://example.com/not-media", false)]
        [InlineData("not a url at all", false)]
        public void ClipboardMonitor_Detects_SupportedMediaUrls(string url, bool expected)
        {
            bool result = ClipboardMonitorService.IsSupportedMediaUrl(url);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData("https://www.tiktok.com/@user/video/7123456789012345678", true)]
        [InlineData("https://vt.tiktok.com/ZS2xyz123/", true)]
        [InlineData("https://www.douyin.com/video/7123456789", true)]
        [InlineData("https://www.youtube.com/watch?v=abc", false)]
        public void TikTokExtractor_Identifies_TikTokUrls(string url, bool expected)
        {
            bool result = TikTokExtractor.IsTikTokUrl(url);
            Assert.Equal(expected, result);
        }

        [Fact]
        public async Task HistoryService_AddAndClear_WorksCorrectly()
        {
            var historyService = new HistoryService();
            await historyService.ClearHistoryAsync();
            Assert.Empty(historyService.Items);

            var item = new DownloadHistoryItem
            {
                Title = "Test Song",
                Url = "https://youtu.be/test",
                Platform = "YouTube",
                FilePath = Path.Combine(Path.GetTempPath(), "test.mp3"),
                FileSizeBytes = 1024 * 1024,
                FormattedSize = "1.00 MB",
                DownloadDate = DateTime.Now,
                IsAudio = true,
                FormatExtension = "mp3"
            };

            await historyService.AddItemAsync(item);
            Assert.Single(historyService.Items);
            Assert.Equal("Test Song", historyService.Items[0].Title);

            await historyService.ClearHistoryAsync();
            Assert.Empty(historyService.Items);
        }

        [Theory]
        [InlineData("Valid Title.mp4", "Valid Title.mp4")]
        [InlineData("Title With /\\:*?\"<>| Invalid Chars", "Title With _________ Invalid Chars")]
        public void Utilities_Sanitizes_FileNames(string input, string expected)
        {
            string sanitized = Utilities.SanitizeFileName(input);
            Assert.Equal(expected, sanitized);
        }
    }
}
