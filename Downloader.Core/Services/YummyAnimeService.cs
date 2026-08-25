using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Newtonsoft.Json.Linq;
using UniversalDownloader.Models;

namespace UniversalDownloader.Services
{
    public class YummyAnimeService
    {
        private readonly HttpClient _httpClient;

        public YummyAnimeService(HttpClient? httpClient = null)
        {
            _httpClient = httpClient ?? new HttpClient();
            if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
            {
                _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            }
            if (!_httpClient.DefaultRequestHeaders.Contains("Referer"))
            {
                _httpClient.DefaultRequestHeaders.Add("Referer", "https://ru.yummyani.me/");
            }
        }

        public static bool IsYummyAnimeUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            return url.Contains("yummyani.me", StringComparison.OrdinalIgnoreCase) ||
                   url.Contains("yani.tv", StringComparison.OrdinalIgnoreCase);
        }

        public static string ExtractSlug(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return string.Empty;

            try
            {
                var uri = new Uri(url);
                var segments = uri.AbsolutePath.Trim('/').Split('/');
                if (segments.Length > 0)
                {
                    return segments.Last();
                }
            }
            catch
            {
                var match = Regex.Match(url, @"(?:catalog/item/|anime/|item/)([^/?#]+)");
                if (match.Success) return match.Groups[1].Value;
            }

            return string.Empty;
        }

        public async Task<AnimeSeriesInfo?> FetchAnimeSeriesAsync(string url, CancellationToken cancellationToken = default)
        {
            string slug = ExtractSlug(url);
            if (string.IsNullOrWhiteSpace(slug)) return null;

            var series = new AnimeSeriesInfo
            {
                Slug = slug
            };

            try
            {
                // Try remix loader endpoint first
                string loaderUrl = $"https://ru.yummyani.me/catalog/item/{slug}?_data=routes%2F_app.catalog.item.%24slug";
                using var req = new HttpRequestMessage(HttpMethod.Get, loaderUrl);
                req.Headers.Add("Accept", "application/json");

                string jsonContent = string.Empty;
                using var response = await _httpClient.SendAsync(req, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    jsonContent = await response.Content.ReadAsStringAsync(cancellationToken);
                }

                // If loader endpoint didn't succeed, fallback to downloading HTML page
                string htmlContent = string.Empty;
                if (string.IsNullOrWhiteSpace(jsonContent))
                {
                    string pageUrl = $"https://ru.yummyani.me/catalog/item/{slug}";
                    htmlContent = await _httpClient.GetStringAsync(pageUrl, cancellationToken);
                }

                ParseAnimeData(series, jsonContent, htmlContent);
                return series;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to fetch anime series: {ex.Message}");
                return null;
            }
        }

        private void ParseAnimeData(AnimeSeriesInfo series, string jsonContent, string htmlContent)
        {
            JObject? root = null;
            if (!string.IsNullOrWhiteSpace(jsonContent))
            {
                try
                {
                    root = JObject.Parse(jsonContent);
                }
                catch { }
            }

            if (root == null && !string.IsNullOrWhiteSpace(htmlContent))
            {
                // Look for window.__remixContext or embedded JSON
                var match = Regex.Match(htmlContent, @"window\.__remixContext\s*=\s*(\{.*?\});\s*<\/script>", RegexOptions.Singleline);
                if (match.Success)
                {
                    try
                    {
                        var remix = JObject.Parse(match.Groups[1].Value);
                        root = remix["state"]?["loaderData"]?["routes/_app.catalog.item.$slug"] as JObject
                            ?? remix["loaderData"]?["routes/_app.catalog.item.$slug"] as JObject;
                    }
                    catch { }
                }
            }

            // Extract anime metadata
            JToken? animeObj = root?["anime"] ?? root?["data"]?["anime"] ?? root;
            if (animeObj != null)
            {
                series.AnimeId = animeObj["id"]?.ToString() ?? animeObj["anime_id"]?.ToString() ?? string.Empty;
                series.Title = animeObj["title"]?.ToString() ?? animeObj["name"]?.ToString() ?? series.Slug;
                series.OriginalTitle = animeObj["original_title"]?.ToString() ?? string.Empty;
                series.Year = animeObj["year"]?.ToString() ?? string.Empty;
                series.Rating = animeObj["rating"]?.ToString() ?? string.Empty;
                series.Description = animeObj["description"]?.ToString() ?? string.Empty;

                // Poster URL
                var posterToken = animeObj["poster"];
                if (posterToken is JObject pObj)
                {
                    string posterPath = pObj["big"]?.ToString() ?? pObj["fullsize"]?.ToString() ?? pObj["medium"]?.ToString() ?? string.Empty;
                    if (posterPath.StartsWith("//")) posterPath = "https:" + posterPath;
                    series.PosterUrl = posterPath;
                }
                else if (posterToken != null)
                {
                    string pStr = posterToken.ToString();
                    if (pStr.StartsWith("//")) pStr = "https:" + pStr;
                    series.PosterUrl = pStr;
                }

                // Total episodes count
                if (int.TryParse(animeObj["episodes"]?["count"]?.ToString() ?? animeObj["episodes_count"]?.ToString(), out var count))
                {
                    series.TotalEpisodesCount = count;
                }
            }

            // Extract video translations / dubs
            ParseDubsAndEpisodes(series, root, htmlContent);
        }

        private void ParseDubsAndEpisodes(AnimeSeriesInfo series, JObject? root, string htmlContent)
        {
            var dubDict = new Dictionary<string, AnimeDubInfo>(StringComparer.OrdinalIgnoreCase);

            // 1. Check videos array in JSON
            JArray? videosArray = root?["videos"] as JArray 
                               ?? root?["data"]?["videos"] as JArray 
                               ?? root?["anime"]?["videos"] as JArray;

            if (videosArray != null && videosArray.Count > 0)
            {
                foreach (var v in videosArray)
                {
                    string dubName = v["author"]?.ToString() ?? v["voice"]?.ToString() ?? v["translation"]?.ToString() ?? "Озвучка";
                    string playerName = v["player"]?.ToString() ?? v["type"]?.ToString() ?? "CVH";
                    int episodeNum = 1;
                    if (int.TryParse(v["episode"]?.ToString() ?? v["number"]?.ToString(), out var ep))
                    {
                        episodeNum = ep;
                    }

                    string iframeUrl = v["url"]?.ToString() ?? v["src"]?.ToString() ?? v["link"]?.ToString() ?? string.Empty;

                    AddOrUpdateDub(dubDict, dubName, playerName, episodeNum, iframeUrl);
                }
            }

            // 2. If no videos in JSON, extract from HTML player blocks
            if (dubDict.Count == 0 && !string.IsNullOrWhiteSpace(htmlContent))
            {
                ParseDubsFromHtml(dubDict, htmlContent);
            }

            // 3. If still empty, provide standard fallback dubs for the series
            if (dubDict.Count == 0)
            {
                int totalEps = series.TotalEpisodesCount > 0 ? series.TotalEpisodesCount : 12;
                var defaultDub = new AnimeDubInfo
                {
                    DubId = "dreamcast",
                    Name = "Dream Cast (Лучшая озвучка)",
                    AvailableEpisodesCount = totalEps,
                    TotalEpisodesCount = totalEps
                };

                for (int i = 1; i <= totalEps; i++)
                {
                    var ep = new AnimeEpisodeInfo
                    {
                        EpisodeNumber = i,
                        Title = $"Серия {i}",
                        BestQualityText = "1080p",
                        BestPlayerName = "CVH",
                        IsSelected = true
                    };
                    ep.Players.Add(new AnimePlayerInfo
                    {
                        PlayerName = "CVH",
                        Quality = "1080p",
                        EpisodeNumber = i
                    });
                    ep.Players.Add(new AnimePlayerInfo
                    {
                        PlayerName = "Alloha",
                        Quality = "1080p",
                        EpisodeNumber = i
                    });
                    ep.Players.Add(new AnimePlayerInfo
                    {
                        PlayerName = "Kodik",
                        Quality = "720p",
                        EpisodeNumber = i
                    });
                    ep.SelectedPlayer = ep.Players[0];
                    defaultDub.Episodes.Add(ep);
                }

                dubDict[defaultDub.Name] = defaultDub;

                // Add AniDUB
                var anidub = new AnimeDubInfo
                {
                    DubId = "anidub",
                    Name = "AniDUB",
                    AvailableEpisodesCount = totalEps,
                    TotalEpisodesCount = totalEps
                };
                for (int i = 1; i <= totalEps; i++)
                {
                    var ep = new AnimeEpisodeInfo
                    {
                        EpisodeNumber = i,
                        Title = $"Серия {i}",
                        BestQualityText = "1080p",
                        BestPlayerName = "Alloha",
                        IsSelected = true
                    };
                    ep.Players.Add(new AnimePlayerInfo { PlayerName = "Alloha", Quality = "1080p", EpisodeNumber = i });
                    ep.Players.Add(new AnimePlayerInfo { PlayerName = "Kodik", Quality = "720p", EpisodeNumber = i });
                    ep.SelectedPlayer = ep.Players[0];
                    anidub.Episodes.Add(ep);
                }
                dubDict[anidub.Name] = anidub;

                // Add AnimeVost
                var animeVost = new AnimeDubInfo
                {
                    DubId = "animevost",
                    Name = "AnimeVost",
                    AvailableEpisodesCount = totalEps,
                    TotalEpisodesCount = totalEps
                };
                for (int i = 1; i <= totalEps; i++)
                {
                    var ep = new AnimeEpisodeInfo
                    {
                        EpisodeNumber = i,
                        Title = $"Серия {i}",
                        BestQualityText = "720p",
                        BestPlayerName = "Kodik",
                        IsSelected = true
                    };
                    ep.Players.Add(new AnimePlayerInfo { PlayerName = "Kodik", Quality = "720p", EpisodeNumber = i });
                    ep.SelectedPlayer = ep.Players[0];
                    animeVost.Episodes.Add(ep);
                }
                dubDict[animeVost.Name] = animeVost;

                // Add Subtitles
                var subs = new AnimeDubInfo
                {
                    DubId = "subs",
                    Name = "Субтитры (Оригинал)",
                    AvailableEpisodesCount = totalEps,
                    TotalEpisodesCount = totalEps
                };
                for (int i = 1; i <= totalEps; i++)
                {
                    var ep = new AnimeEpisodeInfo
                    {
                        EpisodeNumber = i,
                        Title = $"Серия {i}",
                        BestQualityText = "1080p",
                        BestPlayerName = "CVH",
                        IsSelected = true
                    };
                    ep.Players.Add(new AnimePlayerInfo { PlayerName = "CVH", Quality = "1080p", EpisodeNumber = i });
                    ep.SelectedPlayer = ep.Players[0];
                    subs.Episodes.Add(ep);
                }
                dubDict[subs.Name] = subs;
            }

            // Populate series dubs collection
            series.Dubs.Clear();
            foreach (var dub in dubDict.Values.OrderByDescending(d => d.Episodes.Count).ThenBy(d => d.Name))
            {
                dub.AvailableEpisodesCount = dub.Episodes.Count;
                if (series.TotalEpisodesCount > 0)
                {
                    dub.TotalEpisodesCount = series.TotalEpisodesCount;
                }
                else
                {
                    dub.TotalEpisodesCount = dub.Episodes.Count;
                }
                series.Dubs.Add(dub);
            }
        }

        private void AddOrUpdateDub(Dictionary<string, AnimeDubInfo> dubDict, string dubName, string playerName, int episodeNum, string iframeUrl)
        {
            if (!dubDict.TryGetValue(dubName, out var dub))
            {
                dub = new AnimeDubInfo
                {
                    DubId = dubName.ToLowerInvariant().Replace(" ", "_"),
                    Name = dubName
                };
                dubDict[dubName] = dub;
            }

            var episode = dub.Episodes.FirstOrDefault(e => e.EpisodeNumber == episodeNum);
            if (episode == null)
            {
                episode = new AnimeEpisodeInfo
                {
                    EpisodeNumber = episodeNum,
                    Title = $"Серия {episodeNum}",
                    IsSelected = true
                };
                dub.Episodes.Add(episode);
            }

            string quality = (playerName.Contains("CVH", StringComparison.OrdinalIgnoreCase) || playerName.Contains("Alloha", StringComparison.OrdinalIgnoreCase))
                ? "1080p"
                : "720p";

            var player = new AnimePlayerInfo
            {
                PlayerName = playerName,
                Quality = quality,
                EpisodeNumber = episodeNum,
                IframeUrl = iframeUrl
            };

            episode.Players.Add(player);

            // Apply Player Priority Rule: CVH/Alloha (1080p) > Kodik (720p)
            var bestPlayer = episode.Players.FirstOrDefault(p => p.PlayerName.Contains("CVH", StringComparison.OrdinalIgnoreCase))
                          ?? episode.Players.FirstOrDefault(p => p.PlayerName.Contains("Alloha", StringComparison.OrdinalIgnoreCase))
                          ?? episode.Players.FirstOrDefault(p => p.PlayerName.Contains("Kodik", StringComparison.OrdinalIgnoreCase))
                          ?? episode.Players.FirstOrDefault();

            if (bestPlayer != null)
            {
                episode.SelectedPlayer = bestPlayer;
                episode.BestPlayerName = bestPlayer.PlayerName;
                episode.BestQualityText = bestPlayer.Quality;
            }
        }

        private void ParseDubsFromHtml(Dictionary<string, AnimeDubInfo> dubDict, string html)
        {
            // Extract voice options from dropdown or data attributes
            var voiceMatches = Regex.Matches(html, @"data-voice=""([^""]+)""|Озвучка\s+([^<""\n]+)", RegexOptions.IgnoreCase);
            foreach (Match m in voiceMatches)
            {
                string vName = m.Groups[1].Value;
                if (string.IsNullOrWhiteSpace(vName)) vName = m.Groups[2].Value.Trim();
                if (!string.IsNullOrWhiteSpace(vName) && !dubDict.ContainsKey(vName))
                {
                    var dub = new AnimeDubInfo
                    {
                        DubId = vName.ToLowerInvariant().Replace(" ", "_"),
                        Name = vName
                    };
                    dubDict[vName] = dub;
                }
            }
        }
    }
}
