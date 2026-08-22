using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace UniversalDownloader.Services
{
    public class TikTokMediaInfo
    {
        public string? DirectVideoUrl { get; set; }
        public string? DirectAudioUrl { get; set; }
        public string? Title { get; set; }
        public string? Author { get; set; }
        public string? CoverUrl { get; set; }
        public int DurationSeconds { get; set; }
    }

    public class TikTokExtractor
    {
        private readonly HttpClient _httpClient;

        public TikTokExtractor(HttpClient? httpClient = null)
        {
            _httpClient = httpClient ?? new HttpClient();
        }

        public static bool IsTikTokUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            return url.Contains("tiktok.com", StringComparison.OrdinalIgnoreCase) ||
                   url.Contains("douyin.com", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Extracts direct HD media streams (no watermark) for a TikTok / Douyin URL.
        /// </summary>
        public async Task<TikTokMediaInfo?> ExtractTikTokMediaAsync(string url)
        {
            if (!IsTikTokUrl(url)) return null;

            // Strategy 1: TikWM API
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Post, "https://www.tikwm.com/api/");
                request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
                request.Content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("url", url.Trim()),
                    new KeyValuePair<string, string>("count", "12"),
                    new KeyValuePair<string, string>("cursor", "0"),
                    new KeyValuePair<string, string>("web", "1"),
                    new KeyValuePair<string, string>("hd", "1")
                });

                using var response = await _httpClient.SendAsync(request);
                if (response.IsSuccessStatusCode)
                {
                    string jsonStr = await response.Content.ReadAsStringAsync();
                    var jObj = JObject.Parse(jsonStr);
                    int code = jObj["code"]?.ToObject<int>() ?? -1;

                    if (code == 0 && jObj["data"] is JObject data)
                    {
                        string? hdPlay = data["hdplay"]?.ToString();
                        string? play = data["play"]?.ToString();
                        string? music = data["music"]?.ToString();
                        string? title = data["title"]?.ToString();
                        string? author = data["author"]?["nickname"]?.ToString() ?? data["author"]?["unique_id"]?.ToString();
                        string? cover = data["cover"]?.ToString();
                        int duration = data["duration"]?.ToObject<int>() ?? 0;

                        string? directVideo = !string.IsNullOrWhiteSpace(hdPlay) ? hdPlay : play;
                        if (!string.IsNullOrWhiteSpace(directVideo))
                        {
                            if (directVideo.StartsWith("/")) directVideo = "https://www.tikwm.com" + directVideo;

                            return new TikTokMediaInfo
                            {
                                DirectVideoUrl = directVideo,
                                DirectAudioUrl = music,
                                Title = !string.IsNullOrWhiteSpace(title) ? title.Trim() : (author != null ? $"TikTok by {author}" : "TikTok Video"),
                                Author = author ?? "TikTok Creator",
                                CoverUrl = cover,
                                DurationSeconds = duration
                            };
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"TikWM extraction error: {ex.Message}");
            }

            // Strategy 2: SSSTik / SaveTik AJAX API
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Post, "https://ssstik.io/abc?url=dl");
                request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
                request.Headers.Add("HX-Request", "true");
                request.Headers.Add("HX-Target", "target");
                request.Headers.Add("Origin", "https://ssstik.io");
                request.Headers.Add("Referer", "https://ssstik.io/en");
                request.Content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("id", url.Trim()),
                    new KeyValuePair<string, string>("locale", "en"),
                    new KeyValuePair<string, string>("tt", "0")
                });

                using var response = await _httpClient.SendAsync(request);
                if (response.IsSuccessStatusCode)
                {
                    string html = await response.Content.ReadAsStringAsync();
                    var match = Regex.Match(html, @"href=""(https://[^""]*tikcdn[^""]*)""", RegexOptions.IgnoreCase);
                    if (!match.Success) match = Regex.Match(html, @"href=""(https://[^""]+download_without_watermark[^""]*)""", RegexOptions.IgnoreCase);
                    if (!match.Success) match = Regex.Match(html, @"href=""(https://[^""]+\.mp4[^""]*)""", RegexOptions.IgnoreCase);

                    if (match.Success)
                    {
                        return new TikTokMediaInfo
                        {
                            DirectVideoUrl = System.Net.WebUtility.HtmlDecode(match.Groups[1].Value),
                            Title = "TikTok Video (No Watermark)",
                            Author = "TikTok Creator"
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SSSTik extraction error: {ex.Message}");
            }

            return null;
        }
    }
}
