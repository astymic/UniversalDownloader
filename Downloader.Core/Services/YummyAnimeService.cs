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
        private readonly AllohaResolverService _allohaResolver = new AllohaResolverService();

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
                string pageUrl = $"https://ru.yummyani.me/catalog/item/{slug}";
                string htmlContent = string.Empty;

                try
                {
                    htmlContent = await _httpClient.GetStringAsync(pageUrl, cancellationToken);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to get page HTML: {ex.Message}");
                }

                if (string.IsNullOrWhiteSpace(htmlContent))
                {
                    return null;
                }

                ParseAnimeData(series, htmlContent);

                // Fetch full dynamic videos/dubs from api.yani.tv if anime ID is available
                if (!string.IsNullOrWhiteSpace(series.AnimeId))
                {
                    await FetchAndPopulateVideosAsync(series, series.AnimeId, cancellationToken);
                }

                return series;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to fetch anime series: {ex.Message}");
                return null;
            }
        }

        private void ParseAnimeData(AnimeSeriesInfo series, string htmlContent)
        {
            // 1. Try to extract from window.__staticRouterHydrationData
            var hydrationMatch = Regex.Match(htmlContent, @"window\.__staticRouterHydrationData\s*=\s*JSON\.parse\(""(.*?)""\);", RegexOptions.Singleline);
            if (hydrationMatch.Success)
            {
                try
                {
                    string jsonEscaped = hydrationMatch.Groups[1].Value;
                    string jsonStr = Regex.Unescape(jsonEscaped);
                    var root = JObject.Parse(jsonStr);
                    var loaderData = root["loaderData"] as JObject;
                    var animeRoute = loaderData?.Properties().FirstOrDefault(p => p.Value["anime"] != null)?.Value;
                    var animeObj = animeRoute?["anime"];

                    if (animeObj != null)
                    {
                        series.AnimeId = animeObj["anime_id"]?.ToString() ?? animeObj["id"]?.ToString() ?? string.Empty;
                        series.Title = animeObj["title"]?.ToString() ?? string.Empty;
                        series.OriginalTitle = animeObj["original"]?.ToString() ?? animeObj["other_titles"]?[0]?.ToString() ?? string.Empty;
                        series.Description = animeObj["description"]?.ToString() ?? string.Empty;
                        series.Rating = animeObj["rating"]?["val"]?.ToString() ?? animeObj["rating"]?.ToString() ?? string.Empty;
                        series.Year = animeObj["year"]?.ToString() ?? string.Empty;

                        // Poster
                        var posterToken = animeObj["poster"];
                        if (posterToken is JObject pObj)
                        {
                            string p = pObj["big"]?.ToString() ?? pObj["fullsize"]?.ToString() ?? pObj["huge"]?.ToString() ?? pObj["medium"]?.ToString() ?? string.Empty;
                            if (p.StartsWith("//")) p = "https:" + p;
                            series.PosterUrl = p;
                        }

                        // Episodes count
                        var epsToken = animeObj["episodes"];
                        if (epsToken != null)
                        {
                            if (int.TryParse(epsToken["count"]?.ToString() ?? epsToken["aired"]?.ToString(), out var count))
                            {
                                series.TotalEpisodesCount = count;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to parse hydration JSON: {ex.Message}");
                }
            }

            // Fallbacks from OpenGraph and HTML Meta tags if missing
            if (string.IsNullOrWhiteSpace(series.Title))
            {
                var titleMatch = Regex.Match(htmlContent, @"<meta\s+property=""og:title""\s+content=""([^""]+)""");
                if (titleMatch.Success) series.Title = titleMatch.Groups[1].Value;
            }
            if (string.IsNullOrWhiteSpace(series.PosterUrl))
            {
                var posterMatch = Regex.Match(htmlContent, @"<meta\s+property=""og:image""\s+content=""([^""]+)""");
                if (posterMatch.Success)
                {
                    string p = posterMatch.Groups[1].Value;
                    if (p.StartsWith("//")) p = "https:" + p;
                    series.PosterUrl = p;
                }
            }
            if (string.IsNullOrWhiteSpace(series.Description))
            {
                var descMatch = Regex.Match(htmlContent, @"<meta\s+property=""og:description""\s+content=""([^""]+)""");
                if (descMatch.Success) series.Description = descMatch.Groups[1].Value;
            }
            if (string.IsNullOrWhiteSpace(series.AnimeId))
            {
                var idMatch = Regex.Match(htmlContent, @"""anime_id"":\s*(\d+)|""id"":\s*(\d+)");
                if (idMatch.Success)
                {
                    series.AnimeId = idMatch.Groups[1].Success ? idMatch.Groups[1].Value : idMatch.Groups[2].Value;
                }
            }
            if (string.IsNullOrWhiteSpace(series.Title))
            {
                series.Title = series.Slug;
            }
        }

        private async Task FetchAndPopulateVideosAsync(AnimeSeriesInfo series, string animeId, CancellationToken cancellationToken)
        {
            try
            {
                string apiUrl = $"https://api.yani.tv/anime/{animeId}/videos";
                using var req = new HttpRequestMessage(HttpMethod.Get, apiUrl);
                req.Headers.Add("Accept", "application/json");

                using var response = await _httpClient.SendAsync(req, cancellationToken);
                if (!response.IsSuccessStatusCode) return;

                string json = await response.Content.ReadAsStringAsync(cancellationToken);
                var token = JToken.Parse(json);

                JArray? videosArray = token is JArray arr
                    ? arr
                    : (token["response"] as JArray ?? token["data"] as JArray ?? token["videos"] as JArray);

                if (videosArray == null || videosArray.Count == 0) return;

                var dubDict = new Dictionary<string, AnimeDubInfo>(StringComparer.OrdinalIgnoreCase);

                foreach (var v in videosArray)
                {
                    string dubName = v["data"]?["dubbing"]?.ToString() ?? v["dubbing"]?.ToString() ?? "Озвучка";
                    string playerName = v["data"]?["player"]?.ToString() ?? v["player"]?.ToString() ?? "Kodik";
                    string iframeUrl = v["iframe_url"]?.ToString() ?? v["url"]?.ToString() ?? string.Empty;

                    if (!int.TryParse(v["number"]?.ToString() ?? v["episode"]?.ToString(), out var episodeNum) || episodeNum <= 0)
                    {
                        episodeNum = 1;
                    }

                    if (iframeUrl.StartsWith("//"))
                    {
                        iframeUrl = "https:" + iframeUrl;
                    }

                    AddOrUpdateDubVideo(dubDict, dubName, playerName, episodeNum, iframeUrl);
                }

                if (dubDict.Count > 0)
                {
                    series.Dubs.Clear();
                    foreach (var dub in dubDict.Values.OrderByDescending(d => d.Episodes.Count).ThenBy(d => d.Name))
                    {
                        dub.Episodes = new ObservableCollection<AnimeEpisodeInfo>(dub.Episodes.OrderBy(e => e.EpisodeNumber));
                        dub.AvailableEpisodesCount = dub.Episodes.Count;
                        dub.TotalEpisodesCount = series.TotalEpisodesCount > 0 ? series.TotalEpisodesCount : dub.Episodes.Count;
                        series.Dubs.Add(dub);
                    }

                    if (series.TotalEpisodesCount <= 0)
                    {
                        series.TotalEpisodesCount = series.Dubs.Max(d => d.Episodes.Count);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to fetch videos from API: {ex.Message}");
            }
        }

        private void AddOrUpdateDubVideo(Dictionary<string, AnimeDubInfo> dubDict, string dubName, string playerName, int episodeNum, string iframeUrl)
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

            string quality = "720p";
            if (playerName.Contains("Aksor", StringComparison.OrdinalIgnoreCase) ||
                playerName.Contains("Sibnet", StringComparison.OrdinalIgnoreCase) ||
                playerName.Contains("CVH", StringComparison.OrdinalIgnoreCase) ||
                playerName.Contains("Alloha", StringComparison.OrdinalIgnoreCase))
            {
                quality = "1080p";
            }
            else if (playerName.Contains("Kodik", StringComparison.OrdinalIgnoreCase))
            {
                quality = "720p";
            }

            var player = new AnimePlayerInfo
            {
                PlayerName = playerName,
                Quality = quality,
                EpisodeNumber = episodeNum,
                IframeUrl = iframeUrl
            };

            episode.Players.Add(player);

            // Priority: Working download stream players: Aksor (1080p MPD) -> CVH (1080p MP4/HLS) -> Sibnet (1080p) -> Kodik (720p decrypted stream) -> Alloha -> others
            var bestPlayer = episode.Players.FirstOrDefault(p => p.PlayerName.Contains("Aksor", StringComparison.OrdinalIgnoreCase))
                          ?? episode.Players.FirstOrDefault(p => p.PlayerName.Contains("CVH", StringComparison.OrdinalIgnoreCase))
                          ?? episode.Players.FirstOrDefault(p => p.PlayerName.Contains("Sibnet", StringComparison.OrdinalIgnoreCase))
                          ?? episode.Players.FirstOrDefault(p => p.PlayerName.Contains("Kodik", StringComparison.OrdinalIgnoreCase))
                          ?? episode.Players.FirstOrDefault(p => p.PlayerName.Contains("Alloha", StringComparison.OrdinalIgnoreCase))
                          ?? episode.Players.FirstOrDefault();

            if (bestPlayer != null)
            {
                episode.SelectedPlayer = bestPlayer;
                episode.BestPlayerName = bestPlayer.PlayerName;
                episode.BestQualityText = bestPlayer.Quality;
            }
        }

        /// <summary>
        /// Resolves an episode player iframe URL into a direct playable .m3u8 or video stream link for yt-dlp.
        /// </summary>
        public async Task<string> ResolveEpisodeDownloadUrlAsync(string playerUrl, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(playerUrl)) return string.Empty;

            if (playerUrl.StartsWith("//"))
            {
                playerUrl = "https:" + playerUrl;
            }

            // 1. Aksor Player (1080p MPD / HLS)
            if (playerUrl.Contains("aksor.tv", StringComparison.OrdinalIgnoreCase))
            {
                var resolvedAksor = await ResolveAksorStreamAsync(playerUrl, cancellationToken);
                if (!string.IsNullOrWhiteSpace(resolvedAksor))
                {
                    return resolvedAksor;
                }
            }

            // 2. CVH Player (CDNVideoHub - 1080p MP4 / HLS)
            if (playerUrl.Contains("iframeCVH.html", StringComparison.OrdinalIgnoreCase) || playerUrl.Contains("cdnvideohub.com", StringComparison.OrdinalIgnoreCase))
            {
                var resolvedCvh = await ResolveCvhStreamAsync(playerUrl, cancellationToken);
                if (!string.IsNullOrWhiteSpace(resolvedCvh))
                {
                    return resolvedCvh;
                }
            }

            // 3. Sibnet Player (1080p / 720p)
            if (playerUrl.Contains("sibnet.ru", StringComparison.OrdinalIgnoreCase))
            {
                var resolvedSibnet = await ResolveSibnetStreamAsync(playerUrl, cancellationToken);
                if (!string.IsNullOrWhiteSpace(resolvedSibnet))
                {
                    return resolvedSibnet;
                }
            }

            // 4. Kodik embed player URL
            if (playerUrl.Contains("kodik", StringComparison.OrdinalIgnoreCase))
            {
                var resolvedKodik = await ResolveKodikStreamAsync(playerUrl, cancellationToken);
                if (!string.IsNullOrWhiteSpace(resolvedKodik))
                {
                    return resolvedKodik;
                }
            }

            // 5. Alloha Player (1080p / 720p decrypted stream via in-memory V8)
            if (playerUrl.Contains("alloha", StringComparison.OrdinalIgnoreCase))
            {
                var resolvedAlloha = await ResolveAllohaStreamAsync(playerUrl, cancellationToken);
                if (!string.IsNullOrWhiteSpace(resolvedAlloha))
                {
                    return resolvedAlloha;
                }
            }

            return playerUrl;
        }

        private async Task<string?> ResolveAllohaStreamAsync(string allohaUrl, CancellationToken cancellationToken)
        {
            try
            {
                return await _allohaResolver.ResolveStreamUrlAsync(allohaUrl, cancellationToken);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to resolve Alloha stream: {ex.Message}");
                return null;
            }
        }

        private async Task<string?> ResolveCvhStreamAsync(string cvhUrl, CancellationToken cancellationToken)
        {
            try
            {
                var uri = new Uri(cvhUrl);
                var queryParams = System.Web.HttpUtility.ParseQueryString(uri.Query);
                string animeId = queryParams["anime_id"] ?? "";
                string episodeStr = queryParams["episode"] ?? "1";
                string dubbingCode = queryParams["dubbing_code"] ?? "";
                string dubbingName = queryParams["dubbing"] ?? "";

                if (string.IsNullOrWhiteSpace(animeId)) return null;

                int.TryParse(episodeStr, out int episodeNum);
                if (episodeNum <= 0) episodeNum = 1;

                // 1. Fetch playlist from CDNVideoHub API
                string playlistUrl = $"https://plapi.cdnvideohub.com/api/v1/player/sv/playlist?pub=745&id={animeId}&aggr=mali";
                using var plReq = new HttpRequestMessage(HttpMethod.Get, playlistUrl);
                plReq.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
                plReq.Headers.Add("Origin", "https://ru.yummyani.me");
                plReq.Headers.Add("Referer", "https://ru.yummyani.me/");
                plReq.Headers.Add("x-origin", "https://ru.yummyani.me");
                plReq.Headers.Add("Accept", "application/json");

                using var plResp = await _httpClient.SendAsync(plReq, cancellationToken);
                if (!plResp.IsSuccessStatusCode) return null;

                string plJson = await plResp.Content.ReadAsStringAsync(cancellationToken);
                var plToken = JToken.Parse(plJson);
                var items = plToken["items"] as JArray;
                if (items == null || items.Count == 0) return null;

                JToken? matchingItem = null;
                foreach (var item in items)
                {
                    int.TryParse(item["episode"]?.ToString(), out int ep);
                    if (ep == episodeNum)
                    {
                        string studio = item["voiceStudio"]?.ToString() ?? "";
                        if (!string.IsNullOrWhiteSpace(dubbingCode) && studio.Contains(dubbingCode, StringComparison.OrdinalIgnoreCase))
                        {
                            matchingItem = item;
                            break;
                        }
                        if (!string.IsNullOrWhiteSpace(dubbingName) && (dubbingName.Contains(studio, StringComparison.OrdinalIgnoreCase) || studio.Contains(dubbingName, StringComparison.OrdinalIgnoreCase)))
                        {
                            matchingItem = item;
                            break;
                        }
                    }
                }

                if (matchingItem == null)
                {
                    matchingItem = items.FirstOrDefault(i => int.TryParse(i["episode"]?.ToString(), out int ep) && ep == episodeNum);
                }

                if (matchingItem == null) return null;

                string vkId = matchingItem["vkId"]?.ToString() ?? "";
                if (string.IsNullOrWhiteSpace(vkId)) return null;

                // 2. Fetch video streams for this vkId
                string videoApiUrl = $"https://plapi.cdnvideohub.com/api/v1/player/sv/video/{vkId}";
                using var vidReq = new HttpRequestMessage(HttpMethod.Get, videoApiUrl);
                vidReq.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
                vidReq.Headers.Add("Origin", "https://ru.yummyani.me");
                vidReq.Headers.Add("Referer", "https://ru.yummyani.me/");
                vidReq.Headers.Add("x-origin", "https://ru.yummyani.me");
                vidReq.Headers.Add("Accept", "application/json");

                using var vidResp = await _httpClient.SendAsync(vidReq, cancellationToken);
                if (!vidResp.IsSuccessStatusCode) return null;

                string vidJson = await vidResp.Content.ReadAsStringAsync(cancellationToken);
                var vidToken = JToken.Parse(vidJson);
                var sources = vidToken["sources"];
                if (sources == null) return null;

                string mpegFullHd = sources["mpegFullHdUrl"]?.ToString() ?? "";
                if (!string.IsNullOrWhiteSpace(mpegFullHd)) return mpegFullHd;

                string hlsUrl = sources["hlsUrl"]?.ToString() ?? "";
                if (!string.IsNullOrWhiteSpace(hlsUrl)) return hlsUrl;

                string mpegHigh = sources["mpegHighUrl"]?.ToString() ?? "";
                if (!string.IsNullOrWhiteSpace(mpegHigh)) return mpegHigh;

                string dashUrl = sources["dashUrl"]?.ToString() ?? "";
                if (!string.IsNullOrWhiteSpace(dashUrl)) return dashUrl;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to resolve CVH stream: {ex.Message}");
            }
            return null;
        }

        private async Task<string?> ResolveAksorStreamAsync(string aksorUrl, CancellationToken cancellationToken)
        {
            try
            {
                var match = Regex.Match(aksorUrl, @"/video/([a-fA-F0-9]+)");
                if (!match.Success) return null;
                string hash = match.Groups[1].Value;

                string apiUrl = $"https://player.aksor.tv/api/video/{hash}";
                using var req = new HttpRequestMessage(HttpMethod.Get, apiUrl);
                req.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
                req.Headers.Add("Referer", "https://player.aksor.tv/");

                using var resp = await _httpClient.SendAsync(req, cancellationToken);
                if (!resp.IsSuccessStatusCode) return null;

                string json = await resp.Content.ReadAsStringAsync(cancellationToken);
                var root = JObject.Parse(json);
                var qualities = root["qualities"] as JObject;
                if (qualities != null)
                {
                    string? stream = qualities["q1080"]?.ToString() 
                                  ?? qualities["q2k"]?.ToString() 
                                  ?? qualities["q4k"]?.ToString() 
                                  ?? qualities["q720"]?.ToString() 
                                  ?? qualities["q480"]?.ToString() 
                                  ?? qualities["q360"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(stream))
                    {
                        return stream;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to resolve Aksor stream: {ex.Message}");
            }
            return null;
        }

        private async Task<string?> ResolveSibnetStreamAsync(string sibnetUrl, CancellationToken cancellationToken)
        {
            try
            {
                if (sibnetUrl.StartsWith("//")) sibnetUrl = "https:" + sibnetUrl;
                return sibnetUrl;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to resolve Sibnet stream: {ex.Message}");
            }
            return null;
        }

        private async Task<string?> ResolveKodikStreamAsync(string kodikUrl, CancellationToken cancellationToken)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, kodikUrl);
                req.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
                req.Headers.Add("Referer", "https://ru.yummyani.me/");

                using var resp = await _httpClient.SendAsync(req, cancellationToken);
                if (!resp.IsSuccessStatusCode) return null;

                string html = await resp.Content.ReadAsStringAsync(cancellationToken);

                var uri = new Uri(kodikUrl);
                string netloc = uri.Host;

                // Extract params
                string d = Regex.Match(html, @"var\s+domain\s*=\s*['""]([^'""]+)['""]").Groups[1].Value;
                string d_sign = Regex.Match(html, @"var\s+d_sign\s*=\s*['""]([^'""]+)['""]").Groups[1].Value;
                string pd = Regex.Match(html, @"var\s+pd\s*=\s*['""]([^'""]+)['""]").Groups[1].Value;
                string pd_sign = Regex.Match(html, @"var\s+pd_sign\s*=\s*['""]([^'""]+)['""]").Groups[1].Value;
                string refUrl = Regex.Match(html, @"var\s+ref\s*=\s*['""]([^'""]+)['""]").Groups[1].Value;
                string ref_sign = Regex.Match(html, @"var\s+ref_sign\s*=\s*['""]([^'""]+)['""]").Groups[1].Value;
                
                string type = Regex.Match(html, @"vInfo\.type\s*=\s*['""]([^'""]+)['""]").Groups[1].Value;
                if (string.IsNullOrWhiteSpace(type)) type = Regex.Match(html, @"var\s+type\s*=\s*['""]([^'""]+)['""]").Groups[1].Value;
                if (string.IsNullOrWhiteSpace(type)) type = "seria";
                
                string hash = Regex.Match(html, @"vInfo\.hash\s*=\s*['""]([^'""]+)['""]").Groups[1].Value;
                if (string.IsNullOrWhiteSpace(hash)) hash = Regex.Match(html, @"var\s+videoHash\s*=\s*['""]([^'""]+)['""]").Groups[1].Value;
                if (string.IsNullOrWhiteSpace(hash)) hash = Regex.Match(html, @"var\s+hash\s*=\s*['""]([^'""]+)['""]").Groups[1].Value;

                string id = Regex.Match(html, @"vInfo\.id\s*=\s*['""]([^'""]+)['""]").Groups[1].Value;
                if (string.IsNullOrWhiteSpace(id)) id = Regex.Match(html, @"var\s+videoId\s*=\s*['""]([^'""]+)['""]").Groups[1].Value;
                if (string.IsNullOrWhiteSpace(id)) id = Regex.Match(html, @"var\s+id\s*=\s*['""]([^'""]+)['""]").Groups[1].Value;

                if (string.IsNullOrWhiteSpace(hash) || string.IsNullOrWhiteSpace(id))
                {
                    // Fallback to urlParams
                    var matchParams = Regex.Match(html, @"var\s+urlParams\s*=\s*'([^']+)'");
                    if (matchParams.Success)
                    {
                        try
                        {
                            var pObj = JObject.Parse(matchParams.Groups[1].Value);
                            if (string.IsNullOrWhiteSpace(d)) d = pObj["d"]?.ToString() ?? "";
                            if (string.IsNullOrWhiteSpace(d_sign)) d_sign = pObj["d_sign"]?.ToString() ?? "";
                            if (string.IsNullOrWhiteSpace(pd)) pd = pObj["pd"]?.ToString() ?? "";
                            if (string.IsNullOrWhiteSpace(pd_sign)) pd_sign = pObj["pd_sign"]?.ToString() ?? "";
                            if (string.IsNullOrWhiteSpace(refUrl)) refUrl = pObj["ref"]?.ToString() ?? "";
                            if (string.IsNullOrWhiteSpace(ref_sign)) ref_sign = pObj["ref_sign"]?.ToString() ?? "";
                        }
                        catch { }
                    }
                }

                if (string.IsNullOrWhiteSpace(type)) type = "seria";

                var formData = new Dictionary<string, string>
                {
                    { "d", d },
                    { "d_sign", d_sign },
                    { "pd", pd },
                    { "pd_sign", pd_sign },
                    { "ref", refUrl },
                    { "ref_sign", ref_sign },
                    { "type", type },
                    { "hash", hash },
                    { "id", id }
                };

                using var postReq = new HttpRequestMessage(HttpMethod.Post, $"https://{netloc}/ftor");
                postReq.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
                postReq.Headers.Add("Referer", kodikUrl);
                postReq.Headers.Add("Origin", $"https://{netloc}");
                postReq.Content = new FormUrlEncodedContent(formData);

                using var postResp = await _httpClient.SendAsync(postReq, cancellationToken);
                if (!postResp.IsSuccessStatusCode) return null;

                string respJson = await postResp.Content.ReadAsStringAsync(cancellationToken);
                var root = JObject.Parse(respJson);
                var links = root["links"] as JObject;
                if (links == null) return null;

                var srcToken = links["720"]?[0]?["src"] ?? links["480"]?[0]?["src"] ?? links["360"]?[0]?["src"] ?? links.Properties().FirstOrDefault()?.Value?[0]?["src"];
                string encodedSrc = srcToken?.ToString() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(encodedSrc)) return null;

                // Decode Caesar + Base64
                for (int shift = 0; shift < 26; shift++)
                {
                    try
                    {
                        char[] shifted = encodedSrc.ToCharArray();
                        for (int i = 0; i < shifted.Length; i++)
                        {
                            char c = shifted[i];
                            if (c >= 'a' && c <= 'z')
                            {
                                shifted[i] = (char)('a' + (c - 'a' + shift) % 26);
                            }
                            else if (c >= 'A' && c <= 'Z')
                            {
                                shifted[i] = (char)('A' + (c - 'A' + shift) % 26);
                            }
                        }
                        byte[] b = Convert.FromBase64String(new string(shifted));
                        string decoded = System.Text.Encoding.UTF8.GetString(b);
                        if (decoded.Contains("http") || decoded.Contains(".m3u8") || decoded.Contains(".mp4"))
                        {
                            if (decoded.StartsWith("//")) decoded = "https:" + decoded;
                            return decoded;
                        }
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to resolve Kodik stream: {ex.Message}");
            }

            return null;
        }
    }
}
