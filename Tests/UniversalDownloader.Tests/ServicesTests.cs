using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
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

        [Fact]
        public async Task YummyAnimeService_Fetches_LiveAnime()
        {
            var service = new YummyAnimeService();

            // 1. One Piece (1160+ episodes) - verify ascending sorting
            var onepiece = await service.FetchAnimeSeriesAsync("https://ru.yummyani.me/catalog/item/neobyatnyy-okean-3");
            Assert.NotNull(onepiece);
            Assert.NotEmpty(onepiece.Dubs);
            var dub = onepiece.Dubs[0];
            Assert.True(dub.Episodes.Count >= 2);
            Assert.Equal(1, dub.Episodes[0].EpisodeNumber);
            Assert.Equal(2, dub.Episodes[1].EpisodeNumber);

            // 2. Aksor (Josee the Tiger and the Fish)
            var zhoze = await service.FetchAnimeSeriesAsync("https://ru.yummyani.me/catalog/item/zhoze-tigr-i-ryba");
            Assert.NotNull(zhoze);
            var reanimedia = zhoze.Dubs.FirstOrDefault(d => d.Name.Contains("Reanimedia"));
            Assert.NotNull(reanimedia);
            string aksorStream = await service.ResolveEpisodeDownloadUrlAsync(reanimedia.Episodes[0].Players.First(p => p.PlayerName.Contains("Aksor")).IframeUrl);
            Assert.Contains("1080.mpd", aksorStream);

            // 3. CVH (Grand Blue Season 3) - 1080p stream resolution
            string cvhTestUrl = "https://ru.yummyani.me/iframeCVH.html?dubbing_code=AniBaza&anime_id=62542&episode=1&dubbing=%D0%9E%D0%B7%D0%B2%D1%83%D1%87%D0%BA%D0%B0+AniBaza";
            string cvhResolved = await service.ResolveEpisodeDownloadUrlAsync(cvhTestUrl);
            Assert.NotNull(cvhResolved);
            Assert.Contains("okcdn.ru", cvhResolved);

            // 4. Sibnet (Vakfu)
            var vakfu = await service.FetchAnimeSeriesAsync("https://ru.yummyani.me/catalog/item/vakfu-legenda-ob-ogreste");
            Assert.NotNull(vakfu);
            Assert.NotEmpty(vakfu.Dubs);
            Assert.Contains("Sibnet", vakfu.Dubs[0].Episodes[0].Players.First().PlayerName);
        }

        [Fact]
        public async Task YummyAnimeService_Fetches_BlueLock()
        {
            var service = new YummyAnimeService();
            var anime = await service.FetchAnimeSeriesAsync("https://ru.yummyani.me/catalog/item/sinyaya-tyurma-blyu-lok");
            Assert.NotNull(anime);
            Assert.NotEmpty(anime.Title);
            Assert.NotEmpty(anime.PosterUrl);
            Assert.NotEmpty(anime.Dubs);

            // Blue Lock is 24 episodes completed
            var aniLibria = anime.Dubs.FirstOrDefault(d => d.Name.Contains("AniLibria"));
            Assert.NotNull(aniLibria);
            Assert.Equal(24, aniLibria.Episodes.Count);
        }
    }
}
