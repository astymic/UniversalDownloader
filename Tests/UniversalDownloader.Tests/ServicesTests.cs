using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
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

        [Fact]
        public async Task AllohaResolverService_ResolvesStreamUrlAsync()
        {
            var resolver = new AllohaResolverService();
            string allohaUrl = "https://alloha.yani.tv/?token_movie=d6f95967930bd041348068c263ef87&translation=10&season=1&episode=1&token=8b5512267a2a52e9de06d67d342e0c&hidden=translation,season,episode";
            
            string? m3u8Url = await resolver.ResolveStreamUrlAsync(allohaUrl);
            Assert.NotNull(m3u8Url);
            Assert.Contains(".m3u8", m3u8Url);

            using var http = new HttpClient();
            using var req = new HttpRequestMessage(HttpMethod.Get, m3u8Url);
            req.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            req.Headers.Add("Referer", allohaUrl);
            req.Headers.Add("Origin", "https://alloha.yani.tv");

            using var resp = await http.SendAsync(req);
            Assert.True(resp.IsSuccessStatusCode);
            string manifest = await resp.Content.ReadAsStringAsync();
            Assert.Contains("#EXTM3U", manifest);
        }

        [Fact]
        public async Task YummyAnimeService_Resolves_AllohaEpisode()
        {
            var service = new YummyAnimeService();
            string allohaUrl = "https://alloha.yani.tv/?token_movie=d6f95967930bd041348068c263ef87&translation=10&season=1&episode=1&token=8b5512267a2a52e9de06d67d342e0c&hidden=translation,season,episode";

            string streamUrl = await service.ResolveEpisodeDownloadUrlAsync(allohaUrl);
            Assert.NotNull(streamUrl);
            Assert.Contains(".m3u8", streamUrl);
        }

        [Fact]
        public async Task YummyAnimeService_Fetches_TvoeImya_And_Prioritizes_Alloha()
        {
            var service = new YummyAnimeService();
            var series = await service.FetchAnimeSeriesAsync("https://ru.yummyani.me/catalog/item/tvoe-imya");
            Assert.NotNull(series);
            Assert.NotEmpty(series.Dubs);

            // Find a dub with Alloha player
            var dubWithAlloha = series.Dubs.FirstOrDefault(d => d.Episodes.Any(e => e.Players.Any(p => p.PlayerName.Contains("Alloha", StringComparison.OrdinalIgnoreCase))));
            if (dubWithAlloha != null)
            {
                var episode = dubWithAlloha.Episodes[0];
                var hasAlloha = episode.Players.Any(p => p.PlayerName.Contains("Alloha", StringComparison.OrdinalIgnoreCase));
                var hasKodik = episode.Players.Any(p => p.PlayerName.Contains("Kodik", StringComparison.OrdinalIgnoreCase));
                if (hasAlloha && hasKodik)
                {
                    // Alloha must be prioritized above Kodik
                    Assert.True(
                        episode.BestPlayerName.Contains("Aksor", StringComparison.OrdinalIgnoreCase) ||
                        episode.BestPlayerName.Contains("CVH", StringComparison.OrdinalIgnoreCase) ||
                        episode.BestPlayerName.Contains("Sibnet", StringComparison.OrdinalIgnoreCase) ||
                        episode.BestPlayerName.Contains("Alloha", StringComparison.OrdinalIgnoreCase),
                        $"Expected 1080p player (Alloha/Sibnet/CVH/Aksor) to be prioritized over Kodik, but got: {episode.BestPlayerName}");
                }
            }
        }

        [Fact]
        public async Task Test_YtDlp_Alloha_Stream()
        {
            var service = new YummyAnimeService();
            var series = await service.FetchAnimeSeriesAsync("https://ru.yummyani.me/catalog/item/tvoe-imya");
            Assert.NotNull(series);
            var aniLibria = series.Dubs.FirstOrDefault(d => d.Name.Contains("AniLibria"));
            Assert.NotNull(aniLibria);
            var allohaPlayer = aniLibria.Episodes[0].Players.FirstOrDefault(p => p.PlayerName.Contains("Alloha"));
            Assert.NotNull(allohaPlayer);

            var resolvedUrl = await service.ResolveEpisodeDownloadUrlAsync(allohaPlayer.IframeUrl);
            Assert.NotNull(resolvedUrl);

            string ytdlpPath = @"C:\Users\maksym.mulkov\Desktop\UniversalDownloader\Downloader\bin\Debug\net8.0-windows\win-x64\yt-dlp.exe";
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = ytdlpPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            psi.ArgumentList.Add("--user-agent");
            psi.ArgumentList.Add("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            psi.ArgumentList.Add("--referer");
            psi.ArgumentList.Add("https://alloha.yani.tv/");
            psi.ArgumentList.Add("--add-header");
            psi.ArgumentList.Add("Origin:https://alloha.yani.tv");
            psi.ArgumentList.Add("-f");
            psi.ArgumentList.Add("bestvideo+bestaudio/best");
            psi.ArgumentList.Add("--simulate");
            psi.ArgumentList.Add("--no-warnings");
            psi.ArgumentList.Add(resolvedUrl);

            using var proc = System.Diagnostics.Process.Start(psi)!;
            string stdout = await proc.StandardOutput.ReadToEndAsync();
            string stderr = await proc.StandardError.ReadToEndAsync();
            await proc.WaitForExitAsync();

            Assert.True(proc.ExitCode == 0, $"yt-dlp download failed (code {proc.ExitCode}):\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");
        }

        [Theory]
        [InlineData("https://kinogo.online/multserialy/32400-lilo-i-stich.html", true)]
        [InlineData("https://kinogo.online/multfilmy/31814-lilo-i-stich.html", true)]
        [InlineData("https://kinogo.la/film/123-avatar.html", true)]
        [InlineData("http://kinogo.biz/series/456.html", true)]
        [InlineData("https://youtube.com/watch?v=123", false)]
        [InlineData("https://ru.yummyani.me/catalog/item/tvoe-imya", false)]
        public void KinogoService_IsKinogoUrl_MatchesCorrectly(string url, bool expected)
        {
            bool result = KinogoService.IsKinogoUrl(url);
            Assert.Equal(expected, result);
        }

        [Fact]
        public async Task KinogoService_FetchesSeries_ExtractsAllDubs_LiloAndStitch()
        {
            var service = new KinogoService();
            var series = await service.FetchSeriesAsync("https://kinogo.online/multserialy/32400-lilo-i-stich.html");
            Assert.NotNull(series);
            Assert.False(series.IsMovie);
            Assert.Contains("Лило и Стич", series.Title);
            Assert.NotEmpty(series.Dubs);

            // Must detect dubs across multiple players ("Дубляж (Невафильм)", "Рус. Дублированный", "Eng.Original", "Дублированный")
            var dubNames = series.Dubs.Select(d => d.Name).ToList();
            Assert.Contains(dubNames, n => n.Contains("Невафильм") || n.Contains("Дубляж"));
            Assert.Contains(dubNames, n => n.Contains("Рус. Дублированный") || n.Contains("Eng.Original") || n.Contains("Дублированный"));

            // Check that episodes are sorted and have players
            var firstDub = series.Dubs[0];
            Assert.NotEmpty(firstDub.Episodes);
            Assert.Equal(1, firstDub.Episodes[0].EpisodeNumber);
            Assert.NotEmpty(firstDub.Episodes[0].Players);
        }

        [Fact]
        public async Task KinogoService_FetchesMovie_ExtractsDubsAndSubtitles()
        {
            var service = new KinogoService();
            var movie = await service.FetchSeriesAsync("https://kinogo.online/multfilmy/31814-lilo-i-stich.html");
            Assert.NotNull(movie);
            Assert.True(movie.IsMovie);
            Assert.Contains("Лило и Стич", movie.Title);
            Assert.NotEmpty(movie.Dubs);

            var firstEp = movie.Dubs[0].Episodes[0];
            var player = firstEp.Players[0];
            var streamUrl = await service.ResolveEpisodeDownloadUrlAsync(player);
            Assert.NotNull(streamUrl);
            Assert.StartsWith("http", streamUrl);

            // After resolving, verify subtitles were extracted
            Assert.NotEmpty(player.Subtitles);
            Assert.Contains(player.Subtitles, s => s.Language.Contains("Рус") || s.Language.Contains("Eng") || s.Language.Contains("Укр"));
        }

        [Fact]
        public async Task KinogoService_ResolvesStreamUrl()
        {
            var service = new KinogoService();
            var series = await service.FetchSeriesAsync("https://kinogo.online/multserialy/32400-lilo-i-stich.html");
            Assert.NotNull(series);

            var firstEp = series.Dubs[0].Episodes[0];
            var player = firstEp.Players[0];
            var resolvedStream = await service.ResolveEpisodeDownloadUrlAsync(player);
            Assert.NotNull(resolvedStream);
            Assert.StartsWith("http", resolvedStream);
            Assert.True(resolvedStream.Contains(".m3u8") || resolvedStream.Contains(".mp4"));
        }

        [Fact]
        public void VideoCompressor_CalculateTargetBitrates_CalculatesReasonableBitrates()
        {
            // 25 MB file for 2 minutes (120s) with AAC 128k
            var (videoKbps, audioKbps) = VideoCompressorService.CalculateTargetBitrates(25, TimeSpan.FromMinutes(2), VideoAudioMode.Aac128);
            
            Assert.Equal(128, audioKbps);
            Assert.True(videoKbps > 1000 && videoKbps < 1600, $"Expected video bitrate between 1000 and 1600 kbps, got {videoKbps}");
        }

        [Fact]
        public void VideoCompressor_BuildFfmpegArguments_VisuallyLossless_ContainsExpectedFlags()
        {
            var options = VideoCompressorOptions.CreateVisuallyLossless();
            var args = VideoCompressorService.BuildFfmpegArguments(
                "input.mp4",
                "output.mp4",
                options,
                TimeSpan.FromMinutes(1));

            // Must contain libx264, -crf 20, -preset slow, -c:a copy, +faststart
            Assert.Contains("-c:v", args);
            int cvIndex = args.IndexOf("-c:v");
            Assert.Equal("libx264", args[cvIndex + 1]);

            Assert.Contains("-crf", args);
            int crfIndex = args.IndexOf("-crf");
            Assert.Equal("20", args[crfIndex + 1]);

            Assert.Contains("-preset", args);
            int presetIndex = args.IndexOf("-preset");
            Assert.Equal("slow", args[presetIndex + 1]);

            Assert.Contains("-c:a", args);
            int caIndex = args.IndexOf("-c:a");
            Assert.Equal("copy", args[caIndex + 1]);

            Assert.Contains("+faststart", args);
        }

        [Fact]
        public void VideoCompressor_BuildFfmpegArguments_ResolutionAndFpsFilters()
        {
            var options = new VideoCompressorOptions
            {
                Preset = VideoCompressionPreset.Balanced,
                Codec = VideoCodec.H265,
                Resolution = "720p",
                Fps = "30",
                AudioMode = VideoAudioMode.Aac128
            };

            var args = VideoCompressorService.BuildFfmpegArguments(
                "input.mkv",
                "output.mp4",
                options,
                TimeSpan.FromMinutes(2));

            Assert.Contains("-c:v", args);
            int cvIndex = args.IndexOf("-c:v");
            Assert.Equal("libx265", args[cvIndex + 1]);

            Assert.Contains("-vf", args);
            int vfIndex = args.IndexOf("-vf");
            string filterStr = args[vfIndex + 1];
            Assert.Contains("scale=-2:720", filterStr);

            Assert.Contains("-r", args);
            int rIndex = args.IndexOf("-r");
            Assert.Equal("30", args[rIndex + 1]);

            Assert.Contains("-c:a", args);
            int caIndex = args.IndexOf("-c:a");
            Assert.Equal("aac", args[caIndex + 1]);
        }

        [Fact]
        public void VideoCompressor_BuildFfmpegArguments_AV1Codec()
        {
            var options = new VideoCompressorOptions
            {
                Preset = VideoCompressionPreset.MaxCompression,
                Codec = VideoCodec.AV1,
                Crf = 28,
                AudioMode = VideoAudioMode.Mute
            };

            var args = VideoCompressorService.BuildFfmpegArguments(
                "input.mp4",
                "output.mp4",
                options,
                TimeSpan.FromMinutes(1));

            Assert.Contains("-c:v", args);
            int cvIndex = args.IndexOf("-c:v");
            Assert.Equal("libsvtav1", args[cvIndex + 1]);

            Assert.Contains("-an", args);
        }

        [Fact]
        public void MediaDiscovery_FolderScanning_FiltersSupportedExtensions()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "UD_FolderScanTest_" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(tempDir);
                string subDir = Path.Combine(tempDir, "SubFolder");
                Directory.CreateDirectory(subDir);

                // Create dummy files
                File.WriteAllText(Path.Combine(tempDir, "video1.mp4"), "dummy");
                File.WriteAllText(Path.Combine(tempDir, "song.MP3"), "dummy");
                File.WriteAllText(Path.Combine(tempDir, "document.txt"), "dummy");
                File.WriteAllText(Path.Combine(tempDir, "app.exe"), "dummy");
                File.WriteAllText(Path.Combine(subDir, "nested_clip.MKV"), "dummy");
                File.WriteAllText(Path.Combine(subDir, "readme.md"), "dummy");

                var supportedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    ".mp4", ".mkv", ".avi", ".mov", ".webm", ".flv", ".wmv", ".m4v", ".ts",
                    ".mp3", ".m4a", ".flac", ".wav", ".aac", ".ogg", ".opus", ".wma"
                };

                var discovered = Directory.EnumerateFiles(tempDir, "*.*", SearchOption.AllDirectories)
                    .Where(f => supportedExtensions.Contains(Path.GetExtension(f)))
                    .Select(Path.GetFileName)
                    .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                Assert.Equal(3, discovered.Count);
                Assert.Contains("video1.mp4", discovered);
                Assert.Contains("song.MP3", discovered);
                Assert.Contains("nested_clip.MKV", discovered);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    try { Directory.Delete(tempDir, true); } catch { }
                }
            }
        }

        [Fact]
        public void GifWebp_BuildFfmpegArguments_HighQualityGif()
        {
            var options = GifWebpOptions.CreateMaxQuality(GifWebpFormat.Gif);
            options.StartTime = TimeSpan.FromSeconds(1);
            options.EndTime = TimeSpan.FromSeconds(4);

            var args = GifWebpService.BuildFfmpegArguments("input.mp4", "output.gif", options);

            Assert.Contains("-ss", args);
            Assert.Contains("1", args);
            Assert.Contains("-to", args);
            Assert.Contains("4", args);
            Assert.Contains("-filter_complex", args);

            int fcIdx = args.IndexOf("-filter_complex");
            string fc = args[fcIdx + 1];

            Assert.Contains("fps=30", fc);
            Assert.Contains("palettegen=max_colors=256:stats_mode=diff", fc);
            Assert.Contains("paletteuse=dither=sierra2_4a", fc);
            Assert.Contains("-loop", args);
            int loopIdx = args.IndexOf("-loop");
            Assert.Equal("0", args[loopIdx + 1]);
        }

        [Fact]
        public void GifWebp_BuildFfmpegArguments_CompressedGif()
        {
            var options = GifWebpOptions.CreateMaxCompression(GifWebpFormat.Gif);
            options.StartTime = TimeSpan.FromSeconds(0);
            options.EndTime = TimeSpan.FromSeconds(3);

            var args = GifWebpService.BuildFfmpegArguments("input.mp4", "output.gif", options);

            int fcIdx = args.IndexOf("-filter_complex");
            string fc = args[fcIdx + 1];

            Assert.Contains("fps=15", fc);
            Assert.Contains("scale=320:-2", fc);
            Assert.Contains("palettegen=max_colors=128", fc);
            Assert.Contains("paletteuse=dither=bayer:bayer_scale=3", fc);
        }

        [Fact]
        public void GifWebp_BuildFfmpegArguments_AnimatedWebp_LossyAndLossless()
        {
            // 1. Lossy WebP
            var lossyOptions = GifWebpOptions.CreateBalanced(GifWebpFormat.Webp);
            var lossyArgs = GifWebpService.BuildFfmpegArguments("input.mp4", "output.webp", lossyOptions);

            Assert.Contains("-vcodec", lossyArgs);
            int vcIdx = lossyArgs.IndexOf("-vcodec");
            Assert.Equal("libwebp", lossyArgs[vcIdx + 1]);

            Assert.Contains("-lossless", lossyArgs);
            int llIdx = lossyArgs.IndexOf("-lossless");
            Assert.Equal("0", lossyArgs[llIdx + 1]);

            Assert.Contains("-q:v", lossyArgs);
            int qIdx = lossyArgs.IndexOf("-q:v");
            Assert.Equal("70", lossyArgs[qIdx + 1]);

            // 2. Lossless WebP
            var losslessOptions = new GifWebpOptions
            {
                Format = GifWebpFormat.Webp,
                WebpLossless = true
            };
            var losslessArgs = GifWebpService.BuildFfmpegArguments("input.mp4", "output.webp", losslessOptions);

            int llIdx2 = losslessArgs.IndexOf("-lossless");
            Assert.Equal("1", losslessArgs[llIdx2 + 1]);
        }

        [Fact]
        public void GifWebp_BuildFfmpegArguments_SpeedAdjustment()
        {
            var options = new GifWebpOptions
            {
                Format = GifWebpFormat.Gif,
                SpeedMultiplier = 2.0, // 2x speed -> setpts=0.5*PTS
                Fps = 24
            };

            var args = GifWebpService.BuildFfmpegArguments("input.mp4", "output.gif", options);
            int fcIdx = args.IndexOf("-filter_complex");
            string fc = args[fcIdx + 1];

            Assert.Contains("setpts=0.5*PTS", fc);
            Assert.Contains("fps=24", fc);
        }

        [Fact]
        public void GifWebp_TargetSizeTuning_ScalesDownUnderTightBudget()
        {
            var options = new GifWebpOptions
            {
                Format = GifWebpFormat.Gif,
                Preset = GifWebpPreset.TargetSize,
                TargetSizeMb = 1.0, // 1 MB for 10 seconds clip
                StartTime = TimeSpan.Zero,
                EndTime = TimeSpan.FromSeconds(10)
            };

            GifWebpService.ApplyTargetSizeTuning(options);

            // Should scale down width and fps to fit tight 1MB budget
            Assert.True(options.Width <= 360);
            Assert.True(options.Fps <= 15);
            Assert.True(options.MaxColors <= 128);
        }

        [Fact]
        public async Task GifWebp_CreateClipAsync_CancelledToken_SetsIsCancelledAndNullErrorMessage()
        {
            var depManager = new DependencyManager();
            var service = new GifWebpService(depManager);
            using var cts = new CancellationTokenSource();
            cts.Cancel(); // Pre-cancel

            var options = GifWebpOptions.CreateMaxQuality();
            var result = await service.CreateClipAsync("dummy.mp4", "dummy.gif", options, null, cts.Token);

            Assert.False(result.Success);
            Assert.True(result.IsCancelled);
            Assert.Null(result.ErrorMessage);
        }

        [Fact]
        public void GifWebp_BuildFfmpegArguments_WithCrop_AddsCropFilter()
        {
            var crop = new VideoCropRect
            {
                X = 0.25,
                Y = 0.1,
                Width = 0.5,
                Height = 0.8
            };

            var options = new GifWebpOptions
            {
                Format = GifWebpFormat.Gif,
                Crop = crop,
                OriginalVideoWidth = 1920,
                OriginalVideoHeight = 1080
            };

            var args = GifWebpService.BuildFfmpegArguments("input.mp4", "output.gif", options);
            int fcIdx = args.IndexOf("-filter_complex");
            Assert.True(fcIdx >= 0);
            string fc = args[fcIdx + 1];

            // 1920 * 0.5 = 960 width, 1080 * 0.8 = 864 height, 1920 * 0.25 = 480 x, 1080 * 0.1 = 108 y
            Assert.Contains("crop=960:864:480:108,", fc);
        }

        [Fact]
        public void GifWebp_BuildFfmpegArguments_TelegramSticker_MatchesSpecs()
        {
            var options = GifWebpOptions.CreateTelegramSticker();
            options.OriginalVideoWidth = 1280;
            options.OriginalVideoHeight = 720;
            options.Fps = 30;

            var args = GifWebpService.BuildFfmpegArguments("input.mp4", "sticker.webm", options);

            // Verify VP9 codec, no audio, and bitrate bounds
            int cvIdx = args.IndexOf("-c:v");
            Assert.True(cvIdx >= 0);
            Assert.Equal("libvpx-vp9", args[cvIdx + 1]);

            Assert.Contains("-an", args);

            int bvIdx = args.IndexOf("-b:v");
            Assert.True(bvIdx >= 0);
            Assert.Equal("450k", args[bvIdx + 1]);

            int mrIdx = args.IndexOf("-maxrate");
            Assert.True(mrIdx >= 0);
            Assert.Equal("500k", args[mrIdx + 1]);

            int fcIdx = args.IndexOf("-filter_complex");
            Assert.True(fcIdx >= 0);
            string fc = args[fcIdx + 1];

            // Verify Telegram 512px constraint scale filter
            Assert.Contains("scale='if(gte(iw,ih),512,-2)':'if(gte(iw,ih),-2,512)'", fc);
        }

        [Fact]
        public void VideoCropRect_ToPixelCrop_EnsuresEvenDimensions()
        {
            var crop = new VideoCropRect
            {
                X = 0.111,
                Y = 0.222,
                Width = 0.555,
                Height = 0.666
            };

            var (cx, cy, cw, ch) = crop.ToPixelCrop(1921, 1081);

            // Codecs like VP9 and x264 require even dimensions and offsets
            Assert.True(cx % 2 == 0, $"cx ({cx}) must be even");
            Assert.True(cy % 2 == 0, $"cy ({cy}) must be even");
            Assert.True(cw % 2 == 0, $"cw ({cw}) must be even");
            Assert.True(ch % 2 == 0, $"ch ({ch}) must be even");
            Assert.True(cx + cw <= 1922);
            Assert.True(cy + ch <= 1082);
        }

        [Fact]
        public void GifWebp_BuildExtractFrameArguments_ProducesCorrectFfmpegPiping()
        {
            var args = GifWebpService.BuildExtractFrameArguments("test.mp4", TimeSpan.FromSeconds(7.5), 1280);

            Assert.Contains("-noaccurate_seek", args);
            Assert.Contains("-ss", args);
            int ssIdx = args.IndexOf("-ss");
            Assert.Equal("7.5", args[ssIdx + 1]);

            Assert.Contains("-i", args);
            Assert.Contains("test.mp4", args);

            Assert.Contains("-vframes", args);
            int vfIdx = args.IndexOf("-vframes");
            Assert.Equal("1", args[vfIdx + 1]);

            Assert.Contains("-vf", args);
            int vfFilterIdx = args.IndexOf("-vf");
            Assert.Contains("flags=fast_bilinear", args[vfFilterIdx + 1]);

            Assert.Contains("-f", args);
            int fIdx = args.IndexOf("-f");
            Assert.Equal("image2pipe", args[fIdx + 1]);

            Assert.Contains("-vcodec", args);
            int vcIdx = args.IndexOf("-vcodec");
            Assert.Equal("mjpeg", args[vcIdx + 1]);

            Assert.Contains("-threads", args);

            Assert.Equal("-", args[^1]);
        }

        [Fact]
        public void SystemPowerService_ParseFromIndex_ResolvesCorrectActions()
        {
            Assert.Equal(PowerAction.Shutdown, SystemPowerService.ParseFromIndex(0));
            Assert.Equal(PowerAction.Sleep, SystemPowerService.ParseFromIndex(1));
            Assert.Equal(PowerAction.Hibernate, SystemPowerService.ParseFromIndex(2));
            Assert.Equal(PowerAction.Shutdown, SystemPowerService.ParseFromIndex(99));
        }

        [Fact]
        public void SystemPowerService_GetActionDisplayName_ReturnsExpectedLabels()
        {
            Assert.Equal("Shut Down", SystemPowerService.GetActionDisplayName(PowerAction.Shutdown));
            Assert.Equal("Sleep", SystemPowerService.GetActionDisplayName(PowerAction.Sleep));
            Assert.Equal("Hibernate", SystemPowerService.GetActionDisplayName(PowerAction.Hibernate));
        }

        [Fact]
        public void DependencyManager_HasWriteAccess_DetectsWritableAndNonExistentDirectories()
        {
            string tempDir = Path.GetTempPath();
            Assert.True(UniversalDownloader.Services.DependencyManager.HasWriteAccess(tempDir));

            string nonExistent = Path.Combine(tempDir, "non_existent_" + Guid.NewGuid().ToString("N"));
            Assert.False(UniversalDownloader.Services.DependencyManager.HasWriteAccess(nonExistent));
        }

        [Fact]
        public void DependencyManager_GetWritableBinDirectory_ReturnsValidWritablePath()
        {
            string dir = UniversalDownloader.Services.DependencyManager.GetWritableBinDirectory();
            Assert.False(string.IsNullOrWhiteSpace(dir));
            Assert.True(Directory.Exists(dir));
            Assert.True(UniversalDownloader.Services.DependencyManager.HasWriteAccess(dir));
        }

        [Fact]
        public void ApplicationManifest_ContainsAsInvokerExecutionLevel()
        {
            string projectDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "Downloader"));
            string manifestPath = Path.Combine(projectDir, "app.manifest");
            Assert.True(File.Exists(manifestPath), $"app.manifest should exist at {manifestPath}");

            string content = File.ReadAllText(manifestPath);
            Assert.Contains("level=\"asInvoker\"", content);
            Assert.Contains("uiAccess=\"false\"", content);
        }

        [Fact]
        public async Task YouTube_ShortUrl_Download_Investigation()
        {
            var depMgr = new UniversalDownloader.Services.DependencyManager();
            var downloadService = new UniversalDownloader.Services.DownloadService(depMgr);
            _ = depMgr.InitializeDependenciesAsync();
            await depMgr.WaitForInitializationAsync();

            string url = "https://youtu.be/dlUO-XJcIxc";
            var (title, formatsJson) = await downloadService.GetYouTubeInfoAsync(url);
            Assert.NotNull(formatsJson);

            string tempDir = Path.Combine(Path.GetTempPath(), "test_yt_regress_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            string destDir = Path.Combine(Path.GetTempPath(), "test_yt_regress_dest_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(tempDir);
            Directory.CreateDirectory(destDir);

            try
            {
                bool success = await downloadService.DownloadWithYtDlpAsync(
                    url,
                    "bestvideo+bestaudio/best",
                    tempDir,
                    destDir,
                    false,
                    null,
                    false,
                    0,
                    0,
                    CancellationToken.None);

                Assert.True(success);
            }
            finally
            {
                try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
                try { if (Directory.Exists(destDir)) Directory.Delete(destDir, true); } catch { }
            }
        }
    }
}


