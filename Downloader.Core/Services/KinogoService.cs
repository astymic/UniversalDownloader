using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using UniversalDownloader.Models;

namespace UniversalDownloader.Services
{
    public class KinogoService
    {
        private static readonly HttpClient _httpClient = new HttpClient(new HttpClientHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.All
        })
        {
            Timeout = TimeSpan.FromSeconds(20)
        };

        private readonly AllohaResolverService _allohaResolver = new();

        private const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0.0.0 Safari/537.36";

        public static bool IsKinogoUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            return Regex.IsMatch(url, @"https?://(?:www\.)?kinogo\.[a-z]{2,8}/", RegexOptions.IgnoreCase);
        }

        private static async Task<string?> FetchPageHtmlAsync(string url, string? referer = null, CancellationToken cancellationToken = default)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                AddDesktopHeaders(req);
                if (!string.IsNullOrWhiteSpace(referer))
                {
                    req.Headers.TryAddWithoutValidation("Referer", referer);
                }

                using var resp = await _httpClient.SendAsync(req, cancellationToken);
                if (resp.IsSuccessStatusCode)
                {
                    string html = await resp.Content.ReadAsStringAsync(cancellationToken);
                    if (!string.IsNullOrWhiteSpace(html) && !html.Contains("<title>Just a moment...</title>", StringComparison.OrdinalIgnoreCase))
                    {
                        return html;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[KinogoService] HttpClient failed for {url}: {ex.Message}");
            }

            // Fallback to curl (bypasses Cloudflare TLS fingerprint challenge natively on Windows and Linux)
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "curl",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8
                };

                psi.ArgumentList.Add("-s");
                psi.ArgumentList.Add("-L");
                psi.ArgumentList.Add("-A");
                psi.ArgumentList.Add(UserAgent);
                psi.ArgumentList.Add("-H");
                psi.ArgumentList.Add("Accept: text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8");
                psi.ArgumentList.Add("-H");
                psi.ArgumentList.Add("Accept-Language: ru-RU,ru;q=0.9,en-US;q=0.8,en;q=0.7");
                psi.ArgumentList.Add("-H");
                psi.ArgumentList.Add("Sec-Fetch-Dest: document");
                psi.ArgumentList.Add("-H");
                psi.ArgumentList.Add("Sec-Fetch-Mode: navigate");
                psi.ArgumentList.Add("-H");
                psi.ArgumentList.Add("Sec-Fetch-Site: none");
                psi.ArgumentList.Add("-H");
                psi.ArgumentList.Add("Sec-Fetch-User: ?1");
                psi.ArgumentList.Add("-H");
                psi.ArgumentList.Add("Upgrade-Insecure-Requests: 1");

                if (!string.IsNullOrWhiteSpace(referer))
                {
                    psi.ArgumentList.Add("-e");
                    psi.ArgumentList.Add(referer);
                }

                psi.ArgumentList.Add(url);

                using var proc = Process.Start(psi);
                if (proc != null)
                {
                    string output = await proc.StandardOutput.ReadToEndAsync(cancellationToken);
                    await proc.WaitForExitAsync(cancellationToken);
                    if (!string.IsNullOrWhiteSpace(output) && !output.Contains("<title>Just a moment...</title>", StringComparison.OrdinalIgnoreCase))
                    {
                        return output;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[KinogoService] Curl fallback failed for {url}: {ex.Message}");
            }

            return null;
        }

        public async Task<AnimeSeriesInfo?> FetchSeriesAsync(string url, CancellationToken cancellationToken = default)
        {
            if (!IsKinogoUrl(url)) return null;

            try
            {
                string? html = await FetchPageHtmlAsync(url, cancellationToken: cancellationToken);
                if (string.IsNullOrWhiteSpace(html)) return null;

                var series = new AnimeSeriesInfo
                {
                    Slug = ExtractSlug(url),
                    SourceService = "Kinogo"
                };

                ParseMetadata(series, html, url);

                // Extract player tabs
                var tabMatches = Regex.Matches(html, @"<li[^>]*\bdata-src=[""']([^""']+)[""'][^>]*\bdata-provider=[""']([^""']+)[""'][^>]*>(.*?)</li>", RegexOptions.IgnoreCase);

                var tabs = new List<(string url, string provider, string name)>();
                foreach (Match m in tabMatches)
                {
                    string tabUrl = m.Groups[1].Value.Trim();
                    string provider = m.Groups[2].Value.Trim();
                    string tabName = Regex.Replace(m.Groups[3].Value, @"<[^>]+>", "").Trim();
                    if (!string.IsNullOrWhiteSpace(tabUrl))
                    {
                        tabs.Add((tabUrl, provider, tabName));
                    }
                }

                // If no tabs found, look for iframes directly in the page
                if (tabs.Count == 0)
                {
                    var iframeMatches = Regex.Matches(html, @"<iframe[^>]*\b(?:data-src|src)=[""']([^""']+)[""']", RegexOptions.IgnoreCase);
                    int idx = 0;
                    foreach (Match m in iframeMatches)
                    {
                        string iframeUrl = m.Groups[1].Value.Trim();
                        if (iframeUrl.Contains("embed") || iframeUrl.Contains("movie") || iframeUrl.Contains("serial") || iframeUrl.Contains("cinemar") || iframeUrl.Contains("ortified"))
                        {
                            tabs.Add((iframeUrl, idx.ToString(), $"Плеер {idx + 1}"));
                            idx++;
                        }
                    }
                }

                // Process all tabs concurrently
                var dubMap = new ConcurrentDictionary<string, List<AnimeEpisodeInfo>>(StringComparer.OrdinalIgnoreCase);

                var tabTasks = tabs.Select(async tab =>
                {
                    try
                    {
                        await ProcessPlayerTabAsync(tab.url, tab.provider, tab.name, url, dubMap, series, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[KinogoService] Tab processing failed for {tab.name} ({tab.url}): {ex.Message}");
                    }
                });

                await Task.WhenAll(tabTasks);

                // Populate Series Dubs
                foreach (var kvp in dubMap.OrderByDescending(x => x.Value.Count).ThenBy(x => x.Key))
                {
                    string dubName = kvp.Key;
                    var episodes = kvp.Value;

                    // Deduplicate episodes by SeasonNumber and EpisodeNumber
                    var distinctEpisodes = episodes
                        .GroupBy(e => (e.SeasonNumber, e.EpisodeNumber))
                        .Select(g =>
                        {
                            var best = g.First();
                            // Merge players
                            var allPlayers = g.SelectMany(x => x.Players).DistinctBy(p => (p.PlayerName, p.StreamUrl, p.DataToken)).ToList();
                            best.Players = allPlayers;
                            best.Subtitles = g.SelectMany(x => x.Subtitles).DistinctBy(s => s.Url).ToList();

                            // Prioritize Cinemar (1080p MP4/HLS) -> VideoCDN (1080p HLS) -> Alloha (1080p)
                            var chosenPlayer = allPlayers.FirstOrDefault(p => p.PlayerName.Contains("Cinemar", StringComparison.OrdinalIgnoreCase))
                                            ?? allPlayers.FirstOrDefault(p => p.PlayerName.Contains("VideoCDN", StringComparison.OrdinalIgnoreCase))
                                            ?? allPlayers.FirstOrDefault(p => p.PlayerName.Contains("Alloha", StringComparison.OrdinalIgnoreCase))
                                            ?? allPlayers.FirstOrDefault();

                            if (chosenPlayer != null)
                            {
                                best.SelectedPlayer = chosenPlayer;
                                best.BestPlayerName = chosenPlayer.PlayerName;
                                best.BestQualityText = chosenPlayer.Quality;
                            }

                            return best;
                        })
                        .OrderBy(e => e.SeasonNumber)
                        .ThenBy(e => e.EpisodeNumber)
                        .ToList();

                    var dubInfo = new AnimeDubInfo
                    {
                        DubId = Guid.NewGuid().ToString("N"),
                        Name = dubName,
                        AvailableEpisodesCount = distinctEpisodes.Count,
                        TotalEpisodesCount = distinctEpisodes.Count
                    };

                    foreach (var ep in distinctEpisodes)
                    {
                        dubInfo.Episodes.Add(ep);
                    }

                    series.Dubs.Add(dubInfo);
                }

                series.TotalEpisodesCount = series.Dubs.Count > 0 ? series.Dubs.Max(d => d.Episodes.Count) : 0;
                series.IsMovie = series.TotalEpisodesCount <= 1 && series.Dubs.All(d => d.Episodes.Count <= 1);

                return series;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[KinogoService] FetchSeriesAsync error: {ex.Message}");
                return null;
            }
        }

        private void ParseMetadata(AnimeSeriesInfo series, string html, string pageUrl)
        {
            // Title
            var titleMatch = Regex.Match(html, @"<h1[^>]*\bitemprop=[""']name[""'][^>]*>(.*?)</h1>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (!titleMatch.Success)
            {
                titleMatch = Regex.Match(html, @"<h1[^>]*>(.*?)</h1>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            }

            if (titleMatch.Success)
            {
                string rawTitle = Regex.Replace(titleMatch.Groups[1].Value, @"<[^>]+>", "").Trim();
                series.Title = System.Net.WebUtility.HtmlDecode(rawTitle);
            }
            else
            {
                series.Title = series.Slug;
            }

            // Original Title (English or secondary name)
            var origMatch = Regex.Match(html, @"<span[^>]*\bclass=[""']orig-title[""'][^>]*>(.*?)</span>", RegexOptions.IgnoreCase);
            if (origMatch.Success)
            {
                series.OriginalTitle = System.Net.WebUtility.HtmlDecode(origMatch.Groups[1].Value.Trim());
            }

            // Poster
            var posterMatch = Regex.Match(html, @"property=[""']og:image[""']\s+content=[""']([^""']+)[""']", RegexOptions.IgnoreCase);
            if (!posterMatch.Success)
            {
                posterMatch = Regex.Match(html, @"content=[""']([^""']+)[""']\s+property=[""']og:image[""']", RegexOptions.IgnoreCase);
            }
            if (!posterMatch.Success)
            {
                posterMatch = Regex.Match(html, @"""image"":\s*""(https?://[^""]+)""", RegexOptions.IgnoreCase);
            }

            if (posterMatch.Success)
            {
                string p = posterMatch.Groups[1].Value.Trim();
                if (p.StartsWith("/"))
                {
                    var uri = new Uri(pageUrl);
                    p = $"{uri.Scheme}://{uri.Host}{p}";
                }
                series.PosterUrl = p;
            }

            // Year
            var yearMatch = Regex.Match(html, @"(?:Год|Год выпуска)[:\s]*<[^>]*>.*?(\d{4})", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (!yearMatch.Success)
            {
                yearMatch = Regex.Match(series.Title, @"\((\d{4})\)");
            }
            if (yearMatch.Success)
            {
                series.Year = yearMatch.Groups[1].Value;
            }

            // Description
            var descMatch = Regex.Match(html, @"<div[^>]*class=[""'][^""']*(?:full-story|full-text|short-story)[^""']*[""'][^>]*>(.*?)</div>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (descMatch.Success)
            {
                string cleanDesc = Regex.Replace(descMatch.Groups[1].Value, @"<[^>]+>", "").Trim();
                series.Description = System.Net.WebUtility.HtmlDecode(cleanDesc);
            }
        }

        private async Task ProcessPlayerTabAsync(string tabUrl, string provider, string tabName, string referer,
            ConcurrentDictionary<string, List<AnimeEpisodeInfo>> dubMap, AnimeSeriesInfo series, CancellationToken ct)
        {
            if (tabUrl.StartsWith("//"))
            {
                tabUrl = "https:" + tabUrl;
            }

            string? tabHtml = await FetchPageHtmlAsync(tabUrl, referer, ct);
            if (string.IsNullOrWhiteSpace(tabHtml)) return;

            // 1. Cinemar (`cinemar.cc`)
            if (tabHtml.Contains("Cinemar("))
            {
                await ParseCinemarPlayerAsync(tabHtml, tabUrl, dubMap, ct);
            }
            // 2. VideoCDN / Ortified (`api.ortified.ws`)
            else if (tabHtml.Contains("makePlayer("))
            {
                ParseVideoCdnPlayer(tabHtml, tabUrl, dubMap);
            }
            // 3. Alloha / Stravers (`stravers.live` / Collaps)
            else if (tabHtml.Contains("fileList"))
            {
                ParseAllohaPlayer(tabHtml, tabUrl, dubMap);
            }
        }

        #region Cinemar Parser
        private async Task ParseCinemarPlayerAsync(string html, string embedUrl,
            ConcurrentDictionary<string, List<AnimeEpisodeInfo>> dubMap, CancellationToken ct)
        {
            var m = Regex.Match(html, @"Cinemar\s*\(\s*(\{.*?\})\s*\);?", RegexOptions.Singleline);
            if (!m.Success) return;

            try
            {
                using var doc = JsonDocument.Parse(m.Groups[1].Value);
                if (!doc.RootElement.TryGetProperty("file", out var fileProp)) return;

                string fileVal = fileProp.GetString() ?? "";
                if (string.IsNullOrWhiteSpace(fileVal)) return;

                string decodedJson = DecodeCinemarFileString(fileVal);
                if (string.IsNullOrWhiteSpace(decodedJson)) return;

                using var playlistDoc = JsonDocument.Parse(decodedJson);
                if (playlistDoc.RootElement.ValueKind != JsonValueKind.Array) return;

                var rootArray = playlistDoc.RootElement.EnumerateArray().ToList();
                if (rootArray.Count == 0) return;

                // Check if series (items contain "folder") or movie (items contain "data")
                bool isSeries = rootArray[0].TryGetProperty("folder", out _);

                if (isSeries)
                {
                    int seasonIdx = 1;
                    foreach (var seasonEl in rootArray)
                    {
                        string seasonTitle = seasonEl.TryGetProperty("title", out var stProp) ? stProp.GetString() ?? $"Сезон {seasonIdx}" : $"Сезон {seasonIdx}";
                        int sNum = ExtractNumber(seasonTitle, seasonIdx);

                        if (seasonEl.TryGetProperty("folder", out var epFolder) && epFolder.ValueKind == JsonValueKind.Array)
                        {
                            int epIdx = 1;
                            foreach (var epEl in epFolder.EnumerateArray())
                            {
                                string epTitle = epEl.TryGetProperty("title", out var etProp) ? etProp.GetString() ?? $"Серия {epIdx}" : $"Серия {epIdx}";
                                int epNum = ExtractNumber(epTitle, epIdx);

                                if (epEl.TryGetProperty("folder", out var dubFolder) && dubFolder.ValueKind == JsonValueKind.Array)
                                {
                                    foreach (var voiceEl in dubFolder.EnumerateArray())
                                    {
                                        string dubName = voiceEl.TryGetProperty("title", out var vtProp) ? CleanHtml(vtProp.GetString() ?? "Дубляж") : "Дубляж";
                                        string dataToken = voiceEl.TryGetProperty("data", out var dtProp) ? dtProp.GetString() ?? "" : "";

                                        var epInfo = new AnimeEpisodeInfo
                                        {
                                            EpisodeNumber = epNum,
                                            SeasonNumber = sNum,
                                            SeasonTitle = seasonTitle,
                                            Title = epTitle,
                                            BestQualityText = "1080p",
                                            BestPlayerName = "Cinemar"
                                        };

                                        var player = new AnimePlayerInfo
                                        {
                                            PlayerName = "Cinemar",
                                            Quality = "1080p",
                                            EpisodeNumber = epNum,
                                            SeasonNumber = sNum,
                                            IframeUrl = embedUrl,
                                            DataToken = dataToken
                                        };

                                        epInfo.Players.Add(player);
                                        epInfo.SelectedPlayer = player;

                                        dubMap.GetOrAdd(dubName, _ => new List<AnimeEpisodeInfo>()).Add(epInfo);
                                    }
                                }
                                epIdx++;
                            }
                        }
                        seasonIdx++;
                    }
                }
                else
                {
                    // Movie: list of dubs
                    foreach (var dubEl in rootArray)
                    {
                        string dubName = dubEl.TryGetProperty("title", out var dtProp) ? CleanHtml(dtProp.GetString() ?? "Дубляж") : "Дубляж";
                        string dataToken = dubEl.TryGetProperty("data", out var dProp) ? dProp.GetString() ?? "" : "";

                        var epInfo = new AnimeEpisodeInfo
                        {
                            EpisodeNumber = 1,
                            SeasonNumber = 1,
                            SeasonTitle = "",
                            Title = "Фильм",
                            BestQualityText = "1080p",
                            BestPlayerName = "Cinemar"
                        };

                        var player = new AnimePlayerInfo
                        {
                            PlayerName = "Cinemar",
                            Quality = "1080p",
                            EpisodeNumber = 1,
                            SeasonNumber = 1,
                            IframeUrl = embedUrl,
                            DataToken = dataToken
                        };

                        epInfo.Players.Add(player);
                        epInfo.SelectedPlayer = player;

                        dubMap.GetOrAdd(dubName, _ => new List<AnimeEpisodeInfo>()).Add(epInfo);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[KinogoService] Cinemar parse error: {ex.Message}");
            }
        }

        private static string DecodeCinemarFileString(string fileStr)
        {
            try
            {
                if (fileStr.StartsWith("#2"))
                {
                    fileStr = fileStr.Substring(2);
                }

                int dm = int.Parse(fileStr.Substring(0, 2));
                char delimiter = (char)dm;
                const int ml = 32;

                string rest = fileStr.Substring(2);
                string[] parts = rest.Split(delimiter);

                var sb = new StringBuilder();
                foreach (var part in parts)
                {
                    if (part.Length > ml)
                    {
                        int t = int.Parse(part.Substring(part.Length - 1, 1));
                        string sub1 = part.Substring(2 * t, part.Length - 3 * t - 1);
                        string sub2 = part.Substring(0, t);
                        sb.Append(sub1);
                        sb.Append(sub2);
                    }
                    else
                    {
                        sb.Append(part);
                    }
                }

                string val = sb.ToString();
                int pad = val.Length % 4;
                if (pad > 0)
                {
                    val += new string('=', 4 - pad);
                }

                byte[] b = Convert.FromBase64String(val);
                return Encoding.UTF8.GetString(b);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[KinogoService] DecodeCinemarFileString error: {ex.Message}");
                return string.Empty;
            }
        }
        #endregion

        #region VideoCDN / Ortified Parser
        private void ParseVideoCdnPlayer(string html, string embedUrl, ConcurrentDictionary<string, List<AnimeEpisodeInfo>> dubMap)
        {
            try
            {
                int idx = html.IndexOf("seasons:[", StringComparison.OrdinalIgnoreCase);
                if (idx == -1) idx = html.IndexOf("seasons: [", StringComparison.OrdinalIgnoreCase);
                if (idx == -1) return;

                int startArr = html.IndexOf('[', idx);
                if (startArr == -1) return;

                int depth = 0;
                int endArr = -1;
                for (int i = startArr; i < html.Length; i++)
                {
                    if (html[i] == '[') depth++;
                    else if (html[i] == ']')
                    {
                        depth--;
                        if (depth == 0)
                        {
                            endArr = i + 1;
                            break;
                        }
                    }
                }

                if (endArr == -1) return;

                string seasonsJson = html.Substring(startArr, endArr - startArr);
                using var doc = JsonDocument.Parse(seasonsJson);
                if (doc.RootElement.ValueKind != JsonValueKind.Array) return;

                foreach (var sEl in doc.RootElement.EnumerateArray())
                {
                    int sNum = sEl.TryGetProperty("season", out var sProp) && sProp.TryGetInt32(out int sn) ? sn : 1;
                    string sTitle = $"Сезон {sNum}";

                    if (!sEl.TryGetProperty("episodes", out var epsEl) || epsEl.ValueKind != JsonValueKind.Array) continue;

                    foreach (var epEl in epsEl.EnumerateArray())
                    {
                        string epNumStr = epEl.TryGetProperty("episode", out var epProp) ? epProp.GetString() ?? "1" : "1";
                        int epNum = ExtractNumber(epNumStr, 1);
                        string epTitle = $"Серия {epNum}";

                        string hlsUrl = epEl.TryGetProperty("hls", out var hlsProp) ? hlsProp.GetString() ?? "" : "";
                        string downloadUrl = epEl.TryGetProperty("download", out var dlProp) ? dlProp.GetString() ?? "" : "";

                        var audioNames = new List<string>();
                        if (epEl.TryGetProperty("audio", out var audioEl) && audioEl.TryGetProperty("names", out var namesEl) && namesEl.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var nameEl in namesEl.EnumerateArray())
                            {
                                string aName = CleanHtml(nameEl.GetString() ?? "Оригинал");
                                if (!string.IsNullOrWhiteSpace(aName)) audioNames.Add(aName);
                            }
                        }

                        if (audioNames.Count == 0)
                        {
                            audioNames.Add("Рус. Дублированный");
                        }

                        for (int aIdx = 0; aIdx < audioNames.Count; aIdx++)
                        {
                            string dubName = audioNames[aIdx];
                            string formatCode;
                            string langCode = string.Empty;

                            if (dubName.Contains("Eng", StringComparison.OrdinalIgnoreCase) ||
                                dubName.Contains("Original", StringComparison.OrdinalIgnoreCase) ||
                                dubName.Contains("English", StringComparison.OrdinalIgnoreCase) ||
                                dubName.Contains("Англ", StringComparison.OrdinalIgnoreCase))
                            {
                                langCode = "en";
                                formatCode = "bestvideo+bestaudio[language^=en]/bestvideo+bestaudio[format_id*=eng]/bestvideo+bestaudio/best";
                            }
                            else if (dubName.Contains("Рус", StringComparison.OrdinalIgnoreCase) ||
                                     dubName.Contains("Дубл", StringComparison.OrdinalIgnoreCase) ||
                                     dubName.Contains("Rus", StringComparison.OrdinalIgnoreCase))
                            {
                                langCode = "ru";
                                formatCode = "bestvideo+bestaudio[language^=ru]/bestvideo+bestaudio[format_id*=rus]/bestvideo+bestaudio/best";
                            }
                            else if (dubName.Contains("Укр", StringComparison.OrdinalIgnoreCase) ||
                                     dubName.Contains("Ukr", StringComparison.OrdinalIgnoreCase))
                            {
                                langCode = "uk";
                                formatCode = "bestvideo+bestaudio[language^=uk]/bestvideo+bestaudio[format_id*=ukr]/bestvideo+bestaudio/best";
                            }
                            else if (dubName.Contains("Япон", StringComparison.OrdinalIgnoreCase) ||
                                     dubName.Contains("Jap", StringComparison.OrdinalIgnoreCase))
                            {
                                langCode = "ja";
                                formatCode = "bestvideo+bestaudio[language^=ja]/bestvideo+bestaudio[format_id*=jap]/bestvideo+bestaudio/best";
                            }
                            else
                            {
                                formatCode = "bestvideo+bestaudio/best";
                            }

                            var epInfo = new AnimeEpisodeInfo
                            {
                                EpisodeNumber = epNum,
                                SeasonNumber = sNum,
                                SeasonTitle = sTitle,
                                Title = epTitle,
                                BestQualityText = "1080p",
                                BestPlayerName = "VideoCDN"
                            };

                            var player = new AnimePlayerInfo
                            {
                                PlayerName = "VideoCDN",
                                Quality = "1080p",
                                EpisodeNumber = epNum,
                                SeasonNumber = sNum,
                                IframeUrl = embedUrl,
                                StreamUrl = hlsUrl,
                                DownloadUrl = downloadUrl,
                                AudioTrackName = dubName,
                                AudioLanguage = langCode,
                                FormatCode = formatCode
                            };

                            epInfo.Players.Add(player);
                            epInfo.SelectedPlayer = player;

                            dubMap.GetOrAdd(dubName, _ => new List<AnimeEpisodeInfo>()).Add(epInfo);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[KinogoService] VideoCDN parse error: {ex.Message}");
            }
        }
        #endregion

        #region Alloha / Stravers Parser
        private void ParseAllohaPlayer(string html, string embedUrl, ConcurrentDictionary<string, List<AnimeEpisodeInfo>> dubMap)
        {
            try
            {
                var m = Regex.Match(html, @"const\s+fileList\s*=\s*JSON\.parse\(\'(.*?)\'\);", RegexOptions.Singleline);
                if (!m.Success) return;

                string raw = Regex.Unescape(m.Groups[1].Value);
                using var doc = JsonDocument.Parse(raw);

                if (!doc.RootElement.TryGetProperty("all", out var allEl)) return;

                // Structure: all -> season -> episode -> translation -> info
                foreach (var sProp in allEl.EnumerateObject())
                {
                    int sNum = ExtractNumber(sProp.Name, 1);
                    string sTitle = $"Сезон {sNum}";

                    foreach (var epProp in sProp.Value.EnumerateObject())
                    {
                        int epNum = ExtractNumber(epProp.Name, 1);
                        string epTitle = $"Серия {epNum}";

                        foreach (var tProp in epProp.Value.EnumerateObject())
                        {
                            string dubName = "Дублированный";
                            if (tProp.Value.TryGetProperty("translation", out var transProp))
                            {
                                dubName = CleanHtml(transProp.GetString() ?? "Дублированный");
                            }

                            var epInfo = new AnimeEpisodeInfo
                            {
                                EpisodeNumber = epNum,
                                SeasonNumber = sNum,
                                SeasonTitle = sTitle,
                                Title = epTitle,
                                BestQualityText = "1080p",
                                BestPlayerName = "Alloha"
                            };

                            var player = new AnimePlayerInfo
                            {
                                PlayerName = "Alloha",
                                Quality = "1080p",
                                EpisodeNumber = epNum,
                                SeasonNumber = sNum,
                                IframeUrl = embedUrl
                            };

                            epInfo.Players.Add(player);
                            epInfo.SelectedPlayer = player;

                            dubMap.GetOrAdd(dubName, _ => new List<AnimeEpisodeInfo>()).Add(epInfo);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[KinogoService] Alloha parse error: {ex.Message}");
            }
        }
        #endregion

        #region Stream & Subtitle Resolution
        public async Task<string?> ResolveEpisodeDownloadUrlAsync(AnimePlayerInfo player, CancellationToken cancellationToken = default)
        {
            if (player == null) return null;

            try
            {
                // 1. Cinemar: resolve via /api/playlist/load
                if (player.PlayerName.Contains("Cinemar", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(player.DataToken))
                {
                    return await ResolveCinemarStreamAsync(player, cancellationToken);
                }

                // 2. VideoCDN: stream URL already in player.StreamUrl
                if (player.PlayerName.Contains("VideoCDN", StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrWhiteSpace(player.StreamUrl))
                    {
                        return player.StreamUrl;
                    }
                }

                // 3. Alloha: resolve via AllohaResolverService
                if (player.PlayerName.Contains("Alloha", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(player.IframeUrl))
                {
                    var resolved = await _allohaResolver.ResolveStreamUrlAsync(player.IframeUrl, cancellationToken);
                    if (!string.IsNullOrWhiteSpace(resolved))
                    {
                        return resolved;
                    }
                }

                if (!string.IsNullOrWhiteSpace(player.StreamUrl)) return player.StreamUrl;
                if (!string.IsNullOrWhiteSpace(player.DownloadUrl)) return player.DownloadUrl;
                return player.IframeUrl;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[KinogoService] ResolveEpisodeDownloadUrlAsync error: {ex.Message}");
                return player.StreamUrl ?? player.IframeUrl;
            }
        }

        private async Task<string?> ResolveCinemarStreamAsync(AnimePlayerInfo player, CancellationToken ct)
        {
            try
            {
                var payloadJson = JsonSerializer.Serialize(player.DataToken);
                using var req = new HttpRequestMessage(HttpMethod.Post, "https://cinemar.cc/api/playlist/load")
                {
                    Content = new StringContent(payloadJson, Encoding.UTF8, "application/json")
                };

                AddDesktopHeaders(req);
                req.Headers.TryAddWithoutValidation("Referer", player.IframeUrl);
                req.Headers.TryAddWithoutValidation("Origin", "https://cinemar.cc");
                req.Headers.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");

                using var resp = await _httpClient.SendAsync(req, ct);
                if (!resp.IsSuccessStatusCode) return null;

                string json = await resp.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(json);

                string hlsFile = doc.RootElement.TryGetProperty("file", out var fileProp) ? fileProp.GetString() ?? "" : "";
                string subtitleStr = doc.RootElement.TryGetProperty("subtitle", out var subProp) ? subProp.GetString() ?? "" : "";
                string downloadToken = doc.RootElement.TryGetProperty("download", out var dlProp) ? dlProp.GetString() ?? "" : "";

                // Parse subtitles: e.g. "[Русский]/static/subtitle/.../rus.vtt,[English]..."
                if (!string.IsNullOrWhiteSpace(subtitleStr))
                {
                    var subParts = subtitleStr.Split(',', StringSplitOptions.RemoveEmptyEntries);
                    foreach (var sp in subParts)
                    {
                        var m = Regex.Match(sp.Trim(), @"^\[(.*?)\](.*)$");
                        if (m.Success)
                        {
                            string lang = m.Groups[1].Value.Trim();
                            string subPath = m.Groups[2].Value.Trim();
                            if (subPath.StartsWith("/"))
                            {
                                subPath = "https://cinemar.cc" + subPath;
                            }
                            player.Subtitles.Add(new SubtitleTrackInfo
                            {
                                Language = lang,
                                Url = subPath
                            });
                        }
                    }
                }

                // If download token exists, attempt to fetch direct MP4 links
                if (!string.IsNullOrWhiteSpace(downloadToken))
                {
                    try
                    {
                        string? directMp4 = await ResolveCinemarDirectMp4Async(downloadToken, player.IframeUrl, ct);
                        if (!string.IsNullOrWhiteSpace(directMp4))
                        {
                            return directMp4;
                        }
                    }
                    catch { }
                }

                if (!string.IsNullOrWhiteSpace(hlsFile))
                {
                    return hlsFile;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[KinogoService] ResolveCinemarStreamAsync error: {ex.Message}");
            }

            return null;
        }

        private async Task<string?> ResolveCinemarDirectMp4Async(string downloadToken, string referer, CancellationToken ct)
        {
            var payload = JsonSerializer.Serialize(downloadToken);
            using var req = new HttpRequestMessage(HttpMethod.Post, "https://cinemar.cc/api/player/download")
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };

            AddDesktopHeaders(req);
            req.Headers.TryAddWithoutValidation("Referer", referer);
            req.Headers.TryAddWithoutValidation("Origin", "https://cinemar.cc");
            req.Headers.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");

            using var resp = await _httpClient.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode) return null;

            string respText = await resp.Content.ReadAsStringAsync(ct);
            // Search for highest quality mp4 URL (1080.mp4, then 720.mp4, then any .mp4)
            var mp4Matches = Regex.Matches(respText, @"href=[""']([^""']+\.mp4)[\""']", RegexOptions.IgnoreCase);
            var mp4Urls = mp4Matches.Cast<Match>().Select(m => m.Groups[1].Value).ToList();

            var best = mp4Urls.FirstOrDefault(u => u.Contains("1080.mp4"))
                    ?? mp4Urls.FirstOrDefault(u => u.Contains("720.mp4"))
                    ?? mp4Urls.FirstOrDefault();

            return best;
        }
        #endregion

        #region Helpers
        private static void AddDesktopHeaders(HttpRequestMessage req)
        {
            req.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
            req.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8");
            req.Headers.TryAddWithoutValidation("Accept-Language", "ru-RU,ru;q=0.9,en-US;q=0.8,en;q=0.7");
            req.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", "document");
            req.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", "navigate");
            req.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "none");
            req.Headers.TryAddWithoutValidation("Sec-Fetch-User", "?1");
            req.Headers.TryAddWithoutValidation("Upgrade-Insecure-Requests", "1");
        }

        private static string ExtractSlug(string url)
        {
            try
            {
                var match = Regex.Match(url, @"/([^/]+)\.html", RegexOptions.IgnoreCase);
                if (match.Success) return match.Groups[1].Value;
            }
            catch { }
            return "kinogo-media";
        }

        private static string CleanHtml(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return string.Empty;
            string clean = Regex.Replace(input, @"<[^>]+>", "").Trim();
            return System.Net.WebUtility.HtmlDecode(clean);
        }

        private static int ExtractNumber(string text, int fallback = 1)
        {
            var match = Regex.Match(text, @"\d+");
            if (match.Success && int.TryParse(match.Value, out int num))
            {
                return num;
            }
            return fallback;
        }
        #endregion
    }
}
