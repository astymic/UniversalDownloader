using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace UniversalDownloader.Services
{
    public class LyricsResult
    {
        public bool Success { get; set; }
        public string? TrackName { get; set; }
        public string? ArtistName { get; set; }
        public string? AlbumName { get; set; }
        public string? PlainLyrics { get; set; }
        public string? SyncedLyrics { get; set; }
        public string? LrcFilePath { get; set; }
    }

    public class LyricsService
    {
        private readonly HttpClient _httpClient;

        public LyricsService(HttpClient? httpClient = null)
        {
            _httpClient = httpClient ?? new HttpClient();
            if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
            {
                _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("UniversalDownloader/2.0 (https://github.com/astymic/UniversalDownloader)");
            }
        }

        public async Task<LyricsResult> FetchAndSaveLyricsAsync(string audioFilePath, string trackTitle, string? artistName = null, double? durationSeconds = null)
        {
            var result = new LyricsResult();
            if (string.IsNullOrWhiteSpace(trackTitle)) return result;

            try
            {
                string cleanTitle = CleanTitleForSearch(trackTitle, out string? extractedArtist);
                string artist = !string.IsNullOrWhiteSpace(artistName) ? artistName.Trim() : (extractedArtist ?? "");

                string url;
                if (!string.IsNullOrWhiteSpace(artist))
                {
                    url = $"https://lrclib.net/api/get?artist_name={Uri.EscapeDataString(artist)}&track_name={Uri.EscapeDataString(cleanTitle)}";
                    if (durationSeconds.HasValue && durationSeconds.Value > 0)
                    {
                        url += $"&duration={(int)durationSeconds.Value}";
                    }
                }
                else
                {
                    url = $"https://lrclib.net/api/search?q={Uri.EscapeDataString(trackTitle)}";
                }

                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                using var res = await _httpClient.SendAsync(req);
                if (!res.IsSuccessStatusCode)
                {
                    if (!url.Contains("/api/search"))
                    {
                        string searchUrl = $"https://lrclib.net/api/search?q={Uri.EscapeDataString(trackTitle)}";
                        using var searchReq = new HttpRequestMessage(HttpMethod.Get, searchUrl);
                        using var searchRes = await _httpClient.SendAsync(searchReq);
                        if (searchRes.IsSuccessStatusCode)
                        {
                            string json = await searchRes.Content.ReadAsStringAsync();
                            var array = JArray.Parse(json);
                            if (array.Count > 0)
                            {
                                return ProcessLyricsJson(array[0] as JObject, audioFilePath);
                            }
                        }
                    }
                    return result;
                }

                string responseJson = await res.Content.ReadAsStringAsync();
                if (responseJson.TrimStart().StartsWith("["))
                {
                    var array = JArray.Parse(responseJson);
                    if (array.Count > 0)
                    {
                        return ProcessLyricsJson(array[0] as JObject, audioFilePath);
                    }
                }
                else if (responseJson.TrimStart().StartsWith("{"))
                {
                    var obj = JObject.Parse(responseJson);
                    return ProcessLyricsJson(obj, audioFilePath);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to fetch lyrics: {ex.Message}");
            }

            return result;
        }

        private LyricsResult ProcessLyricsJson(JObject? obj, string audioFilePath)
        {
            var result = new LyricsResult();
            if (obj == null) return result;

            result.TrackName = obj["trackName"]?.ToString() ?? obj["name"]?.ToString();
            result.ArtistName = obj["artistName"]?.ToString();
            result.AlbumName = obj["albumName"]?.ToString();
            result.PlainLyrics = obj["plainLyrics"]?.ToString();
            result.SyncedLyrics = obj["syncedLyrics"]?.ToString();

            string? lyricsToWrite = !string.IsNullOrWhiteSpace(result.SyncedLyrics) ? result.SyncedLyrics : result.PlainLyrics;

            if (!string.IsNullOrWhiteSpace(lyricsToWrite) && !string.IsNullOrWhiteSpace(audioFilePath) && File.Exists(audioFilePath))
            {
                try
                {
                    string dir = Path.GetDirectoryName(audioFilePath) ?? "";
                    string baseName = Path.GetFileNameWithoutExtension(audioFilePath);
                    string lrcPath = Path.Combine(dir, $"{baseName}.lrc");

                    File.WriteAllText(lrcPath, lyricsToWrite, System.Text.Encoding.UTF8);
                    result.LrcFilePath = lrcPath;
                    result.Success = true;
                    Debug.WriteLine($"Saved synced lyrics to '{lrcPath}'");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to write .lrc file: {ex.Message}");
                }
            }

            if (!string.IsNullOrWhiteSpace(result.PlainLyrics) || !string.IsNullOrWhiteSpace(result.SyncedLyrics))
            {
                result.Success = true;
            }

            return result;
        }

        private string CleanTitleForSearch(string title, out string? extractedArtist)
        {
            extractedArtist = null;
            if (string.IsNullOrWhiteSpace(title)) return "";

            if (title.Contains(" - "))
            {
                var parts = title.Split(new[] { " - " }, 2, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2)
                {
                    extractedArtist = parts[0].Trim();
                    title = parts[1].Trim();
                }
            }

            string clean = title;
            clean = System.Text.RegularExpressions.Regex.Replace(clean, @"\s*[\(\[](official\s*(video|audio|music\s*video|lyric\s*video|visualizer)?|hd|4k|hq|lyrics|remastered|slowed|reverb)[\)\]]", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            return clean.Trim();
        }
    }
}
