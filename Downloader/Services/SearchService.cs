using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using UniversalDownloader.Models;

namespace UniversalDownloader.Services
{
    public enum SearchMode
    {
        SmartMusic,
        RealYouTube
    }

    public class SearchResultBatch
    {
        public List<SearchResultItem> Items { get; set; } = new();
        public bool IsClosestFallback { get; set; }
        public string FallbackQuery { get; set; } = string.Empty;
    }

    public class SearchService
    {
        private readonly DependencyManager _dependencyManager;
        private readonly ConcurrentDictionary<string, (DateTime CachedAt, SearchResultBatch Batch)> _searchCache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(15);

        public SearchService(DependencyManager dependencyManager)
        {
            _dependencyManager = dependencyManager;
        }

        public async Task<SearchResultBatch> SearchRealYouTubeAsync(string query, CancellationToken cancellationToken = default)
        {
            var batch = new SearchResultBatch();
            if (string.IsNullOrWhiteSpace(query)) return batch;
            if (!_dependencyManager.IsYtDlpReady) return batch;

            string cleanQuery = query.Trim();
            string cacheKey = $"RealYouTube:{cleanQuery}";

            if (_searchCache.TryGetValue(cacheKey, out var cached) && (DateTime.UtcNow - cached.CachedAt) < CacheTtl)
            {
                return cached.Batch;
            }

            try
            {
                // Direct YouTube video search with authentic ranking (top 20 results)
                var results = await QueryYtDlpSearchAsync($"ytsearch20:{cleanQuery}", "YouTube", cancellationToken);
                batch.Items.AddRange(results);
                batch.IsClosestFallback = false;
                batch.FallbackQuery = string.Empty;

                if (batch.Items.Count > 0)
                {
                    _searchCache[cacheKey] = (DateTime.UtcNow, batch);
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Debug.WriteLine($"Real YouTube search failed: {ex.Message}");
            }

            return batch;
        }

        public async Task<SearchResultBatch> SearchAsync(string query, string platformFilter = "All", CancellationToken cancellationToken = default)
        {
            var batch = new SearchResultBatch();
            if (string.IsNullOrWhiteSpace(query)) return batch;
            if (!_dependencyManager.IsYtDlpReady) return batch;

            string cleanQuery = query.Trim();
            string cacheKey = $"{platformFilter}:{cleanQuery}";

            // 1. Instant in-memory cache lookup (0ms)
            if (_searchCache.TryGetValue(cacheKey, out var cached) && (DateTime.UtcNow - cached.CachedAt) < CacheTtl)
            {
                return cached.Batch;
            }

            bool searchYouTube = string.Equals(platformFilter, "All", StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(platformFilter, "YouTube", StringComparison.OrdinalIgnoreCase);

            bool searchSoundCloud = string.Equals(platformFilter, "All", StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(platformFilter, "SoundCloud", StringComparison.OrdinalIgnoreCase);

            // 2. Parallel async search execution across both platforms
            var ytSearchTask = searchYouTube 
                ? SearchYouTubeAsync(cleanQuery, cancellationToken)
                : Task.FromResult((Items: new List<SearchResultItem>(), IsFallback: false, FallbackQuery: string.Empty));

            var scSearchTask = searchSoundCloud
                ? SearchSoundCloudAsync(cleanQuery, cancellationToken)
                : Task.FromResult((Items: new List<SearchResultItem>(), IsFallback: false, FallbackQuery: string.Empty));

            await Task.WhenAll(ytSearchTask, scSearchTask);

            var (ytResults, ytFallback, ytFallbackQuery) = await ytSearchTask;
            var (scResults, scFallback, scFallbackQuery) = await scSearchTask;

            batch.Items.AddRange(ytResults);
            batch.Items.AddRange(scResults);
            batch.IsClosestFallback = ytFallback || scFallback;
            batch.FallbackQuery = !string.IsNullOrEmpty(ytFallbackQuery) ? ytFallbackQuery : scFallbackQuery;

            if (batch.Items.Count > 0)
            {
                _searchCache[cacheKey] = (DateTime.UtcNow, batch);
            }

            return batch;
        }

        private async Task<(List<SearchResultItem> Items, bool IsFallback, string FallbackQuery)> SearchYouTubeAsync(string cleanQuery, CancellationToken cancellationToken)
        {
            var ytResults = new List<SearchResultItem>();
            bool usedFallback = false;
            string matchedFallbackQuery = string.Empty;

            try
            {
                ytResults = await QueryYtDlpSearchAsync($"ytsearch15:{cleanQuery}", "YouTube", cancellationToken);

                if (ytResults.Count == 0 && cleanQuery.Contains(' '))
                {
                    var words = cleanQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries);

                    for (int len = words.Length - 1; len >= 1; len--)
                    {
                        string subQuery = string.Join(" ", words, 0, len);
                        if (subQuery.Length >= 2)
                        {
                            var fallbackYt = await QueryYtDlpSearchAsync($"ytsearch15:{subQuery}", "YouTube", cancellationToken);
                            if (fallbackYt.Count > 0)
                            {
                                ytResults.AddRange(fallbackYt);
                                usedFallback = true;
                                matchedFallbackQuery = subQuery;
                                break;
                            }
                        }
                    }

                    if (ytResults.Count == 0 && words.Length > 2)
                    {
                        for (int start = 1; start < words.Length; start++)
                        {
                            string subQuery = string.Join(" ", words, start, words.Length - start);
                            if (subQuery.Length >= 2)
                            {
                                var fallbackYt = await QueryYtDlpSearchAsync($"ytsearch15:{subQuery}", "YouTube", cancellationToken);
                                if (fallbackYt.Count > 0)
                                {
                                    ytResults.AddRange(fallbackYt);
                                    usedFallback = true;
                                    matchedFallbackQuery = subQuery;
                                    break;
                                }
                            }
                        }
                    }

                    if (ytResults.Count == 0)
                    {
                        var longestWords = words.Where(w => w.Length >= 3).OrderByDescending(w => w.Length).Take(2);
                        foreach (var word in longestWords)
                        {
                            var wordYt = await QueryYtDlpSearchAsync($"ytsearch10:{word}", "YouTube", cancellationToken);
                            if (wordYt.Count > 0)
                            {
                                foreach (var item in wordYt)
                                {
                                    if (!ytResults.Exists(x => x.Id == item.Id))
                                    {
                                        ytResults.Add(item);
                                    }
                                }
                                usedFallback = true;
                                matchedFallbackQuery = word;
                                if (ytResults.Count >= 8) break;
                            }
                        }
                    }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Debug.WriteLine($"YouTube search failed: {ex.Message}");
            }

            return (ytResults, usedFallback, matchedFallbackQuery);
        }

        private async Task<(List<SearchResultItem> Items, bool IsFallback, string FallbackQuery)> SearchSoundCloudAsync(string cleanQuery, CancellationToken cancellationToken)
        {
            var scResults = new List<SearchResultItem>();
            bool usedFallback = false;
            string matchedFallbackQuery = string.Empty;

            try
            {
                scResults = await QueryYtDlpSearchAsync($"scsearch10:{cleanQuery}", "SoundCloud", cancellationToken);

                if (scResults.Count == 0 && cleanQuery.Contains(' '))
                {
                    var words = cleanQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    for (int len = words.Length - 1; len >= 1; len--)
                    {
                        string subQuery = string.Join(" ", words, 0, len);
                        if (subQuery.Length >= 2)
                        {
                            var fallbackSc = await QueryYtDlpSearchAsync($"scsearch10:{subQuery}", "SoundCloud", cancellationToken);
                            if (fallbackSc.Count > 0)
                            {
                                scResults.AddRange(fallbackSc);
                                usedFallback = true;
                                matchedFallbackQuery = subQuery;
                                break;
                            }
                        }
                    }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Debug.WriteLine($"SoundCloud search failed: {ex.Message}");
            }

            return (scResults, usedFallback, matchedFallbackQuery);
        }

        private async Task<List<SearchResultItem>> QueryYtDlpSearchAsync(string searchTarget, string platform, CancellationToken cancellationToken)
        {
            var list = new List<SearchResultItem>();

            var psi = new ProcessStartInfo
            {
                FileName = _dependencyManager.YtDlpExecutablePath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            psi.ArgumentList.Add(searchTarget);
            psi.ArgumentList.Add("--flat-playlist");
            psi.ArgumentList.Add("--dump-json");
            psi.ArgumentList.Add("--no-warnings");
            psi.ArgumentList.Add("--ignore-config");
            psi.ArgumentList.Add("--no-playlist");
            psi.ArgumentList.Add("--no-check-certificates");
            psi.ArgumentList.Add("--no-call-home");
            psi.ArgumentList.Add("--socket-timeout");
            psi.ArgumentList.Add("5");
            psi.ArgumentList.Add("--extractor-args");
            psi.ArgumentList.Add("youtube:skip=translated_subs,comments,dash");
            psi.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";

            using var process = new Process { StartInfo = psi };
            var outputBuilder = new StringBuilder();

            process.OutputDataReceived += (s, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                {
                    try
                    {
                        var item = ParseJsonEntry(e.Data, platform);
                        if (item != null)
                        {
                            lock (list)
                            {
                                list.Add(item);
                            }
                        }
                    }
                    catch { }
                }
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync(cancellationToken);
            return list;
        }

        private SearchResultItem? ParseJsonEntry(string jsonLine, string platform)
        {
            try
            {
                using var doc = JsonDocument.Parse(jsonLine);
                var root = doc.RootElement;

                string id = root.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? "" : "";
                string title = root.TryGetProperty("title", out var titleProp) ? titleProp.GetString() ?? "" : "";
                if (string.IsNullOrWhiteSpace(title)) return null;

                string uploader = "";
                if (root.TryGetProperty("uploader", out var upProp)) uploader = upProp.GetString() ?? "";
                else if (root.TryGetProperty("channel", out var chProp)) uploader = chProp.GetString() ?? "";

                double duration = 0;
                if (root.TryGetProperty("duration", out var durProp))
                {
                    if (durProp.ValueKind == JsonValueKind.Number) duration = durProp.GetDouble();
                }

                string durationStr = FormatDuration(duration);

                string thumbUrl = "";
                if (root.TryGetProperty("thumbnails", out var thumbsProp) && thumbsProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var thumb in thumbsProp.EnumerateArray())
                    {
                        if (thumb.TryGetProperty("url", out var urlElem))
                        {
                            thumbUrl = urlElem.GetString() ?? "";
                        }
                    }
                }

                if (string.IsNullOrWhiteSpace(thumbUrl) && root.TryGetProperty("thumbnail", out var singleThumb))
                {
                    thumbUrl = singleThumb.GetString() ?? "";
                }

                string sourceUrl = "";
                if (root.TryGetProperty("url", out var urlProp))
                {
                    sourceUrl = urlProp.GetString() ?? "";
                }

                if (string.Equals(platform, "YouTube", StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrWhiteSpace(id) && (string.IsNullOrWhiteSpace(sourceUrl) || !sourceUrl.StartsWith("http")))
                    {
                        sourceUrl = $"https://www.youtube.com/watch?v={id}";
                    }
                    if (string.IsNullOrWhiteSpace(thumbUrl) && !string.IsNullOrWhiteSpace(id))
                    {
                        thumbUrl = $"https://i.ytimg.com/vi/{id}/hqdefault.jpg";
                    }
                }

                var defaultQualities = GetDefaultQualitiesForPlatform(platform);

                return new SearchResultItem
                {
                    Id = id,
                    Title = title,
                    ArtistOrChannel = string.IsNullOrWhiteSpace(uploader) ? platform : uploader,
                    DurationString = durationStr,
                    ThumbnailUrl = thumbUrl,
                    SourceUrl = sourceUrl,
                    Platform = platform,
                    AvailableQualities = defaultQualities,
                    SelectedQuality = defaultQualities.Count > 0 ? defaultQualities[0] : null
                };
            }
            catch
            {
                return null;
            }
        }

        private static string FormatDuration(double totalSeconds)
        {
            if (totalSeconds <= 0) return "--:--";
            var time = TimeSpan.FromSeconds(totalSeconds);
            return time.TotalHours >= 1 ? time.ToString(@"hh\:mm\:ss") : time.ToString(@"mm\:ss");
        }

        private static List<YouTubeQualityItem> GetDefaultQualitiesForPlatform(string platform)
        {
            if (string.Equals(platform, "SoundCloud", StringComparison.OrdinalIgnoreCase))
            {
                return new List<YouTubeQualityItem>
                {
                    new YouTubeQualityItem { Label = "Download as MP3", FormatCode = "bestaudio/best", IsAudioOnly = true, AudioFormat = "mp3", SortPriority = 100 },
                    new YouTubeQualityItem { Label = "Original Audio", FormatCode = "bestaudio/best", IsAudioOnly = true, AudioFormat = "best", SortPriority = 90 }
                };
            }

            return new List<YouTubeQualityItem>
            {
                new YouTubeQualityItem { Label = "Best Video + Audio", FormatCode = "bestvideo+bestaudio/best", IsAudioOnly = false, AudioFormat = "best", SortPriority = 9999 },
                new YouTubeQualityItem { Label = "1080p Full HD", FormatCode = "bestvideo[height<=1080]+bestaudio/best[height<=1080]/best", IsAudioOnly = false, AudioFormat = "best", SortPriority = 1080 },
                new YouTubeQualityItem { Label = "720p HD", FormatCode = "bestvideo[height<=720]+bestaudio/best[height<=720]/best", IsAudioOnly = false, AudioFormat = "best", SortPriority = 720 },
                new YouTubeQualityItem { Label = "Download as MP3", FormatCode = "bestaudio/best", IsAudioOnly = true, AudioFormat = "mp3", SortPriority = 48 },
                new YouTubeQualityItem { Label = "Best Audio Only", FormatCode = "bestaudio/best", IsAudioOnly = true, AudioFormat = "best", SortPriority = 49 }
            };
        }
    }
}
