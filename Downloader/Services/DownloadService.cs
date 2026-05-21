using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Globalization;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace UniversalDownloader.Services
{
    public class DownloadProgressArgs : EventArgs
    {
        public string? StatusMessage { get; set; }
        public string? Filename { get; set; }
        public double Percentage { get; set; }
        public bool IsIndeterminate { get; set; }
    }

    public partial class DownloadService
    {
        private readonly HttpClient _httpClient;
        private readonly DependencyManager _dependencyManager;

        public event EventHandler<DownloadProgressArgs>? ProgressChanged;

        public DownloadService(DependencyManager dependencyManager)
        {
            _dependencyManager = dependencyManager;
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");
            _httpClient.Timeout = TimeSpan.FromMinutes(10);
        }

        public string? ProgressPrefix { get; set; }

        private void ReportProgress(string status, string? filename = null, double percentage = 0, bool isIndeterminate = false)
        {
            string finalStatus = string.IsNullOrEmpty(ProgressPrefix) ? status : $"{ProgressPrefix} {status}";
            ProgressChanged?.Invoke(this, new DownloadProgressArgs
            {
                StatusMessage = finalStatus,
                Filename = filename,
                Percentage = percentage,
                IsIndeterminate = isIndeterminate
            });
        }

        public bool IsYouTubeLink(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            return Regex.IsMatch(url, @"(youtube\.com\/(watch\?v=|embed\/|shorts\/)|youtu\.be\/)", RegexOptions.IgnoreCase);
        }

        public bool IsYouTubePlaylistLink(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            // Only match pure playlist pages, NOT single videos that happen to have ?list= context
            return Regex.IsMatch(url, @"youtube\.com\/playlist\?list=", RegexOptions.IgnoreCase);
        }

        public bool IsGoogleDriveLink(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            return Regex.IsMatch(url, @"drive\.google\.com/(file/d/|open\?id=|uc\?id=)", RegexOptions.IgnoreCase);
        }

        public bool IsSpotifyLink(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            return Regex.IsMatch(url, @"open\.spotify\.com/(track|album|playlist)/", RegexOptions.IgnoreCase);
        }

        public bool IsSoundCloudLink(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            return Regex.IsMatch(url, @"soundcloud\.com/", RegexOptions.IgnoreCase);
        }

        public bool IsKnownAudioPlatformLink(string url)
        {
            return IsSpotifyLink(url) || IsSoundCloudLink(url);
        }

        public async Task<string?> GetTitleWithYtDlpAsync(string url)
        {
            if (!_dependencyManager.IsYtDlpReady) return null;

            try
            {
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = _dependencyManager.YtDlpExecutablePath,
                    Arguments = $"--get-title --no-warnings \"{url}\"",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = System.Text.Encoding.UTF8
                };
                psi.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";

                using (Process process = Process.Start(psi))
                {
                    string titleOutput = await process.StandardOutput.ReadToEndAsync();
                    await process.WaitForExitAsync();
                    if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(titleOutput))
                    {
                        return Utilities.SanitizeFileName(titleOutput.Trim());
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error fetching title: {ex.Message}");
            }
            return null;
        }

        public async Task<(string? Title, string? FormatsJson)> GetYouTubeInfoAsync(string url)
        {
            if (!_dependencyManager.IsYtDlpReady) return (null, null);

            try
            {
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = _dependencyManager.YtDlpExecutablePath,
                    Arguments = $"-J --no-warnings --ignore-config --no-playlist \"{url}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = System.Text.Encoding.UTF8,
                    StandardErrorEncoding = System.Text.Encoding.UTF8
                };
                psi.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";

                using (Process process = Process.Start(psi))
                {
                    string jsonOutput = await process.StandardOutput.ReadToEndAsync();
                    string errorOutput = await process.StandardError.ReadToEndAsync();
                    await process.WaitForExitAsync();
                    
                    if (process.ExitCode != 0 || (!string.IsNullOrWhiteSpace(errorOutput) && !errorOutput.ToLower().Contains("deprecated") && !errorOutput.ToLower().Contains("warning:")))
                    {
                        throw new Exception($"yt-dlp error: {(errorOutput.Split('\n')[0])?.Trim()}");
                    }

                    if (string.IsNullOrWhiteSpace(jsonOutput))
                    {
                        throw new Exception("yt-dlp returned empty info. Video might be unavailable.");
                    }

                    return ("Success", jsonOutput);
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Error getting YouTube info: {ex.Message}");
            }
        }

        /// <summary>
        /// Fetches playlist metadata using yt-dlp --flat-playlist -J.
        /// Returns the raw JSON string containing playlist title and entries.
        /// </summary>
        public async Task<string?> GetPlaylistInfoAsync(string url)
        {
            if (!_dependencyManager.IsYtDlpReady) return null;

            try
            {
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = _dependencyManager.YtDlpExecutablePath,
                    Arguments = $"--flat-playlist -J --no-warnings --ignore-config \"{url}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = System.Text.Encoding.UTF8,
                    StandardErrorEncoding = System.Text.Encoding.UTF8
                };
                psi.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";

                using (Process process = Process.Start(psi))
                {
                    string jsonOutput = await process.StandardOutput.ReadToEndAsync();
                    await process.WaitForExitAsync();

                    if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(jsonOutput))
                    {
                        return null;
                    }
                    return jsonOutput;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error fetching playlist info: {ex.Message}");
                return null;
            }
        }

        public async Task DownloadDirectFileAsync(string url, string tempDownloadFolder, string finalDestinationFolder, CancellationToken cancellationToken)
        {
            CleanDirectory(tempDownloadFolder);

            string tempFileName = "unknown_file.dat";
            string? tempFilePath = null;

            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Get, url))
                using (var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    response.EnsureSuccessStatusCode();

                    tempFileName = GetFileNameFromHeaders(response, url);
                    tempFilePath = Path.Combine(tempDownloadFolder, tempFileName);
                    
                    ReportProgress("Downloading...", tempFileName, 0, true);

                    long? totalBytes = response.Content.Headers.ContentLength;
                    int lastPercentage = -1;

                    using (var fileStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
                    using (var stream = await response.Content.ReadAsStreamAsync(cancellationToken))
                    {
                        byte[] buffer = new byte[81920]; int bytesRead; long totalBytesRead = 0;

                        while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            await fileStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);
                            totalBytesRead += bytesRead;
                            
                            if (totalBytes.HasValue && totalBytes.Value > 0)
                            {
                                double percentage = (double)totalBytesRead / totalBytes.Value * 100;
                                if ((int)percentage != lastPercentage)
                                {
                                    ReportProgress($"Downloading: {percentage:F1}% of {Utilities.FormatBytesOutput(totalBytes.Value)}", tempFileName, percentage, false);
                                    lastPercentage = (int)percentage;
                                }
                            }
                            else
                            {
                                ReportProgress($"Downloading ({Utilities.FormatBytesOutput(totalBytesRead)})...", tempFileName, 0, true);
                            }
                        }
                    }

                    CopyToFinalDestinationAndClean(tempDownloadFolder, finalDestinationFolder);
                    ReportProgress($"Download complete — saved as '{tempFileName}'", tempFileName, 100, false);
                }
            }
            catch (OperationCanceledException)
            {
                if (tempFilePath != null && File.Exists(tempFilePath))
                {
                    try { File.Delete(tempFilePath); } catch { }
                }
                throw;
            }
            catch (Exception ex)
            {
                if (tempFilePath != null && File.Exists(tempFilePath))
                {
                    try { File.Delete(tempFilePath); } catch { }
                }
                throw new Exception($"Download Error: {ex.Message}");
            }
        }

        public string GetFileNameFromHeaders(HttpResponseMessage response, string url)
        {
            string? fileName = null;
            if (response.Content.Headers.ContentDisposition != null)
            {
                fileName = response.Content.Headers.ContentDisposition.FileNameStar;
                if (string.IsNullOrWhiteSpace(fileName)) { fileName = response.Content.Headers.ContentDisposition.FileName; }
            }
            if (string.IsNullOrWhiteSpace(fileName))
            {
                if (Uri.TryCreate(url, UriKind.Absolute, out Uri uri))
                {
                    string pathFileName = Path.GetFileName(uri.LocalPath);
                    if (!string.IsNullOrWhiteSpace(pathFileName) && (pathFileName.Contains(".") || !string.IsNullOrEmpty(Path.GetExtension(pathFileName))))
                    {
                        fileName = pathFileName;
                    }
                }
            }
            fileName = Utilities.SanitizeFileName(Uri.UnescapeDataString(fileName ?? ""));
            if (string.IsNullOrWhiteSpace(fileName) || string.IsNullOrWhiteSpace(Path.GetExtension(fileName)))
            {
                string extension = Utilities.GetExtensionFromMimeType(response.Content.Headers.ContentType?.MediaType);
                string baseName = (string.IsNullOrWhiteSpace(fileName) || fileName == "downloaded_file")
                                  ? (IsGoogleDriveLink(url) ? "gdrive_download" : "downloaded_file")
                                  : Path.GetFileNameWithoutExtension(fileName);
                fileName = baseName + extension;
            }
            return string.IsNullOrWhiteSpace(fileName) ? "unknown_file.dat" : Utilities.SanitizeFileName(fileName);
        }

        public async Task<bool> DownloadWithYtDlpAsync(string url, string? formatSelection, string tempDownloadFolder, string finalDestinationFolder, bool extractAudio, string audioFormat, bool useTrimming, double trimStartSeconds, double trimEndSeconds, CancellationToken cancellationToken, string? overrideFileName = null)
        {
            if (!_dependencyManager.IsYtDlpReady)
            {
                throw new Exception("yt-dlp is not available.");
            }

            CleanDirectory(tempDownloadFolder);

            // We want the final filename to match the YouTube title as closely as possible without extra tags.
            string baseFileNameTemplate = "%(title)s.%(ext)s";
            if (IsKnownAudioPlatformLink(url) && extractAudio)
            {
                baseFileNameTemplate = "%(artist)s - %(title)s.%(ext)s";
            }
            string outputTemplate = Path.Combine(tempDownloadFolder, baseFileNameTemplate);

            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = _dependencyManager.YtDlpExecutablePath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                StandardErrorEncoding = System.Text.Encoding.UTF8
            };

            psi.ArgumentList.Add("-o");
            psi.ArgumentList.Add(outputTemplate);

            if (extractAudio)
            {
                psi.ArgumentList.Add("--extract-audio");
                psi.ArgumentList.Add("--audio-format");
                psi.ArgumentList.Add(audioFormat);
                psi.ArgumentList.Add("--audio-quality");
                psi.ArgumentList.Add("0");
                psi.ArgumentList.Add("-f");
                psi.ArgumentList.Add(string.IsNullOrWhiteSpace(formatSelection) ? "bestaudio/best" : formatSelection);
            }
            else
            {
                psi.ArgumentList.Add("-f");
                psi.ArgumentList.Add(formatSelection ?? "bestvideo+bestaudio/best");
                psi.ArgumentList.Add("--merge-output-format");
                psi.ArgumentList.Add("mp4");
            }

            psi.ArgumentList.Add("--no-playlist");
            psi.ArgumentList.Add("--no-continue");
            psi.ArgumentList.Add("--progress");
            psi.ArgumentList.Add("--newline");
            psi.ArgumentList.Add("--no-warnings");
            psi.ArgumentList.Add("--ignore-config");

            if (_dependencyManager.IsFfmpegReady)
            {
                psi.ArgumentList.Add("--ffmpeg-location");
                psi.ArgumentList.Add(_dependencyManager.FfmpegExecutablePath);
            }

            psi.ArgumentList.Add(url);
            psi.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";

            bool progressStarted = false;
            double lastPercentage = 0;
            var stderrOutput = new System.Text.StringBuilder();

            using (Process process = new Process { StartInfo = psi, EnableRaisingEvents = true })
            {
                process.OutputDataReceived += (s, e) =>
                {
                    if (e.Data != null)
                    {
                        ParseYtDlpProgress(e.Data, ref progressStarted, ref lastPercentage);
                    }
                };
                
                process.ErrorDataReceived += (s, e) =>
                {
                    if (e.Data != null)
                    {
                        stderrOutput.AppendLine(e.Data);
                        if (useTrimming && e.Data.Contains("time="))
                        {
                            ParseFfmpegProgress(e.Data, trimStartSeconds, trimEndSeconds, ref lastPercentage);
                        }
                        else if (!e.Data.Contains("[debug]") && !useTrimming)
                        {
                            Debug.WriteLine($"yt-dlp stderr: {e.Data}");
                        }
                    }
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                try
                {
                    await process.WaitForExitAsync(cancellationToken);
                }
                catch (TaskCanceledException)
                {
                    if (!process.HasExited) process.Kill(true);
                    CleanDirectory(tempDownloadFolder);
                    throw new OperationCanceledException();
                }
                catch (Exception)
                {
                    if (!process.HasExited) process.Kill(true);
                    CleanDirectory(tempDownloadFolder);
                    throw;
                }

                if (process.ExitCode == 0)
                {
                    string[] downloadedFiles = Directory.GetFiles(tempDownloadFolder);
                    if (downloadedFiles.Length > 0)
                    {
                        string downloadedFile = downloadedFiles[0];
                        string extension = Path.GetExtension(downloadedFile);
                        
                        if (!string.IsNullOrWhiteSpace(overrideFileName))
                        {
                            string sanitized = Utilities.SanitizeFileName(overrideFileName);
                            string newFileName = sanitized;
                            if (!newFileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                            {
                                newFileName += extension;
                            }
                            string newFilePath = Path.Combine(tempDownloadFolder, newFileName);
                            try
                            {
                                if (string.Compare(downloadedFile, newFilePath, StringComparison.OrdinalIgnoreCase) != 0)
                                {
                                    if (File.Exists(newFilePath))
                                    {
                                        File.Delete(newFilePath);
                                    }
                                    File.Move(downloadedFile, newFilePath);
                                }
                                downloadedFile = newFilePath;
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"Failed to rename downloaded file to override name: {ex.Message}");
                            }
                        }

                        if (useTrimming && trimEndSeconds > trimStartSeconds)
                        {
                            // Offline trimming route: Video is fully downloaded to temp, now fast-trim it
                            await TrimLocalVideoAsync(downloadedFile, finalDestinationFolder, extractAudio, audioFormat, trimStartSeconds, trimEndSeconds, cancellationToken);
                        }
                        else
                        {
                            CopyToFinalDestinationAndClean(tempDownloadFolder, finalDestinationFolder);
                        }
                    }
                    else
                    {
                        throw new Exception("No downloaded files found in the temporary folder.");
                    }
                }
                else
                {
                    CleanDirectory(tempDownloadFolder);
                    string errorMsg = stderrOutput.ToString();
                    var errorLines = errorMsg.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                                           .Where(line => line.ToLower().Contains("error") || line.ToLower().Contains("failed"))
                                           .Take(3)
                                           .ToArray();
                    string details = errorLines.Length > 0 ? string.Join("; ", errorLines) : "Unknown error";
                    throw new Exception($"yt-dlp failed (exit code {process.ExitCode}): {details}");
                }

                return process.ExitCode == 0;
            }
        }

        private void CleanDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    foreach (var file in Directory.GetFiles(path))
                    {
                        try { File.Delete(file); } catch { }
                    }
                }
            }
            catch { }
        }

        private void CopyToFinalDestinationAndClean(string tempDownloadFolder, string finalDestinationFolder)
        {
            if (!Directory.Exists(finalDestinationFolder))
            {
                Directory.CreateDirectory(finalDestinationFolder);
            }

            string[] files = Directory.GetFiles(tempDownloadFolder);
            foreach (var sourceFile in files)
            {
                string fileName = Path.GetFileName(sourceFile);
                string destFile = Path.Combine(finalDestinationFolder, fileName);
                
                ReportProgress("Copying to destination...", fileName, 100, true);
                File.Copy(sourceFile, destFile, true);
                
                try { File.Delete(sourceFile); } catch { }
            }
        }

        public async Task<SpotifyMetadata?> GetSpotifyMetadataAsync(string url)
        {
            var match = Regex.Match(url, @"/track/([a-zA-Z0-9]+)", RegexOptions.IgnoreCase);
            if (!match.Success) return null;
            string trackId = match.Groups[1].Value;

            string? oEmbedTitle = null;
            string? oEmbedArtist = null;

            // Layer 1: oEmbed Endpoint
            try
            {
                string oEmbedUrl = $"https://open.spotify.com/oembed?url={Uri.EscapeDataString(url)}";
                using (var request = new HttpRequestMessage(HttpMethod.Get, oEmbedUrl))
                {
                    request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");
                    using (var response = await _httpClient.SendAsync(request))
                    {
                        if (response.IsSuccessStatusCode)
                        {
                            string jsonStr = await response.Content.ReadAsStringAsync();
                            var jObj = JObject.Parse(jsonStr);
                            oEmbedTitle = jObj["title"]?.ToString()?.Trim();
                            oEmbedArtist = jObj["author_name"]?.ToString()?.Trim();

                            if (!string.IsNullOrWhiteSpace(oEmbedTitle))
                            {
                                if (string.IsNullOrWhiteSpace(oEmbedArtist))
                                {
                                    if (oEmbedTitle.Contains(" - "))
                                    {
                                        var parts = oEmbedTitle.Split(new[] { " - " }, StringSplitOptions.RemoveEmptyEntries);
                                        if (parts.Length >= 2)
                                        {
                                            oEmbedArtist = parts[0].Trim();
                                            oEmbedTitle = parts[1].Trim();
                                        }
                                    }
                                    else if (oEmbedTitle.Contains(" by "))
                                    {
                                        var parts = oEmbedTitle.Split(new[] { " by " }, StringSplitOptions.RemoveEmptyEntries);
                                        if (parts.Length >= 2)
                                        {
                                            oEmbedTitle = parts[0].Trim();
                                            oEmbedArtist = parts[1].Trim();
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Layer 1 (oEmbed) failed: {ex.Message}");
            }

            // Layer 2: initialState base64 parsing (from main track page)
            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");
                    request.Headers.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
                    using (var response = await _httpClient.SendAsync(request))
                    {
                        if (response.IsSuccessStatusCode)
                        {
                            string html = await response.Content.ReadAsStringAsync();
                            var scriptMatch = Regex.Match(html, @"<script\s+id=""initialState""\s+type=""text/plain""[^>]*>(.*?)</script>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
                            if (scriptMatch.Success)
                            {
                                string base64 = scriptMatch.Groups[1].Value.Trim();
                                byte[] bytes = Convert.FromBase64String(base64);
                                string jsonStr = System.Text.Encoding.UTF8.GetString(bytes);
                                var jObj = JObject.Parse(jsonStr);

                                // Find trackEntity at entities.items["spotify:track:{trackId}"]
                                string entityKey = $"spotify:track:{trackId}";
                                var trackEntity = jObj["entities"]?["items"]?[entityKey];
                                if (trackEntity != null)
                                {
                                    string? title = trackEntity["name"]?.ToString()?.Trim();
                                    
                                    // Primary artist
                                    string? primaryArtist = trackEntity["firstArtist"]?["items"]?[0]?["profile"]?["name"]?.ToString()?.Trim();
                                    
                                    // Other artists
                                    var otherArtistsList = new List<string>();
                                    var otherArtistsArray = trackEntity["otherArtists"]?["items"] as JArray;
                                    if (otherArtistsArray != null)
                                    {
                                        foreach (var artistItem in otherArtistsArray)
                                        {
                                            string? name = artistItem["profile"]?["name"]?.ToString()?.Trim();
                                            if (!string.IsNullOrWhiteSpace(name))
                                            {
                                                otherArtistsList.Add(name);
                                            }
                                        }
                                    }

                                    if (!string.IsNullOrWhiteSpace(title))
                                    {
                                        string artistStr = primaryArtist ?? "Unknown Artist";
                                        if (otherArtistsList.Count > 0)
                                        {
                                            artistStr = $"{artistStr}, {string.Join(", ", otherArtistsList)}";
                                        }

                                        return new SpotifyMetadata
                                        {
                                            Title = title,
                                            Artist = artistStr,
                                            TrackId = trackId
                                        };
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Layer 2 (initialState) failed: {ex.Message}");
            }

            // Layer 3: Embed Widget Scraper (__NEXT_DATA__)
            try
            {
                string embedUrl = $"https://open.spotify.com/embed/track/{trackId}";
                using (var request = new HttpRequestMessage(HttpMethod.Get, embedUrl))
                {
                    request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");
                    using (var response = await _httpClient.SendAsync(request))
                    {
                        if (response.IsSuccessStatusCode)
                        {
                            string html = await response.Content.ReadAsStringAsync();
                            var jsonMatch = Regex.Match(html, @"<script\s+id=""__NEXT_DATA__""\s+type=""application/json""[^>]*>(.*?)</script>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
                            if (jsonMatch.Success)
                            {
                                string jsonStr = jsonMatch.Groups[1].Value.Trim();
                                var jObj = JObject.Parse(jsonStr);
                                var pageProps = jObj["props"]?["pageProps"];
                                if (pageProps != null)
                                {
                                    var entity = pageProps["state"]?["data"]?["entity"] ?? pageProps["state"]?["entity"];
                                    string? title = entity?["name"]?.ToString() ?? entity?["title"]?.ToString() ?? pageProps["name"]?.ToString() ?? pageProps["title"]?.ToString();
                                    
                                    string? artist = null;
                                    var artistsArray = (entity?["artists"] ?? pageProps["artists"]) as JArray;
                                    if (artistsArray != null && artistsArray.Count > 0)
                                    {
                                        var artistNames = new List<string>();
                                        foreach (var art in artistsArray)
                                        {
                                            string? name = art?["name"]?.ToString()?.Trim();
                                            if (!string.IsNullOrWhiteSpace(name)) artistNames.Add(name);
                                        }
                                        if (artistNames.Count > 0) artist = string.Join(", ", artistNames);
                                    }
                                    
                                    artist = artist ?? entity?["subtitle"]?.ToString() ?? entity?["author_name"]?.ToString() ?? pageProps["author_name"]?.ToString();

                                    if (!string.IsNullOrWhiteSpace(title))
                                    {
                                        return new SpotifyMetadata
                                        {
                                            Title = title.Trim(),
                                            Artist = !string.IsNullOrWhiteSpace(artist) ? artist.Trim() : "Unknown Artist",
                                            TrackId = trackId
                                        };
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Layer 3 (Embed Widget) failed: {ex.Message}");
            }

            // Layer 4: HTML title / description meta tags on main track page
            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");
                    request.Headers.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
                    using (var response = await _httpClient.SendAsync(request))
                    {
                        if (response.IsSuccessStatusCode)
                        {
                            string html = await response.Content.ReadAsStringAsync();
                            var titleMatch = Regex.Match(html, @"<title[^>]*>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                            var ogTitleMatch = Regex.Match(html, @"<meta\s+property=""og:title""\s+content=""([^""]+)""", RegexOptions.IgnoreCase);
                            var ogDescMatch = Regex.Match(html, @"<meta\s+property=""og:description""\s+content=""([^""]+)""", RegexOptions.IgnoreCase);
                            
                            string? scrapedTitle = null;
                            if (ogTitleMatch.Success) scrapedTitle = System.Net.WebUtility.HtmlDecode(ogTitleMatch.Groups[1].Value.Trim());
                            else if (titleMatch.Success) scrapedTitle = System.Net.WebUtility.HtmlDecode(titleMatch.Groups[1].Value.Trim());

                            if (!string.IsNullOrWhiteSpace(scrapedTitle))
                            {
                                // Remove general Spotify postfixes if present in title
                                if (scrapedTitle.EndsWith(" | Spotify", StringComparison.OrdinalIgnoreCase))
                                {
                                    scrapedTitle = scrapedTitle.Substring(0, scrapedTitle.Length - " | Spotify".Length).Trim();
                                }

                                string artist = "Unknown Artist";
                                if (ogDescMatch.Success)
                                {
                                    string ogDesc = System.Net.WebUtility.HtmlDecode(ogDescMatch.Groups[1].Value.Trim());
                                    var songDotMatch = Regex.Match(ogDesc, @"Song\s+·\s+(.+?)\s+·\s+\d{4}", RegexOptions.IgnoreCase);
                                    var byMatch = Regex.Match(ogDesc, @"Song by\s+(.+?)(?:\s+on\s+|\s+·|\s*$)", RegexOptions.IgnoreCase);
                                    if (songDotMatch.Success) artist = songDotMatch.Groups[1].Value;
                                    else if (byMatch.Success) artist = byMatch.Groups[1].Value;
                                    else
                                    {
                                        var parts = ogDesc.Split(new[] { " · ", " - " }, StringSplitOptions.RemoveEmptyEntries);
                                        if (parts.Length > 0) artist = parts[0];
                                    }
                                }

                                // Check if title regex matches the English style "scrapedTitle - song and lyrics by artist | Spotify"
                                // or Russian style "scrapedTitle - песня и текст от artist | Spotify"
                                var songByMatch = Regex.Match(scrapedTitle, @"^(?<title>.*?) - song (?:and lyrics )?by (?<artist>.*?)$", RegexOptions.IgnoreCase);
                                if (songByMatch.Success)
                                {
                                    scrapedTitle = songByMatch.Groups["title"].Value.Trim();
                                    artist = songByMatch.Groups["artist"].Value.Trim();
                                }

                                return new SpotifyMetadata
                                {
                                    Title = scrapedTitle,
                                    Artist = artist,
                                    TrackId = trackId
                                };
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Layer 4 failed: {ex.Message}");
            }

            // Layer 5 Fallback: oEmbed
            if (!string.IsNullOrWhiteSpace(oEmbedTitle))
            {
                return new SpotifyMetadata
                {
                    Title = oEmbedTitle,
                    Artist = !string.IsNullOrWhiteSpace(oEmbedArtist) ? oEmbedArtist : "Unknown Artist",
                    TrackId = trackId
                };
            }

            return null;
        }

        public class SpotifyMetadata
        {
            public string Title { get; set; } = "";
            public string Artist { get; set; } = "";
            public string TrackId { get; set; } = "";
        }

        public class SpotifyPlaylistMetadata
        {
            public string Name { get; set; } = "";
            public string Type { get; set; } = ""; // "playlist" or "album"
            public List<SpotifyTrack> Tracks { get; set; } = new List<SpotifyTrack>();
        }

        public class SpotifyTrack
        {
            public string Title { get; set; } = "";
            public string Artist { get; set; } = "";
            public string Uri { get; set; } = "";
        }

        public bool IsSpotifyPlaylistOrAlbumLink(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            return Regex.IsMatch(url, @"open\.spotify\.com/(playlist|album)/", RegexOptions.IgnoreCase);
        }

        public async Task<string?> GetSpotifyDeveloperTokenAsync(string clientId, string clientSecret)
        {
            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Post, "https://accounts.spotify.com/api/token"))
                {
                    var keyValues = new List<KeyValuePair<string, string>>
                    {
                        new KeyValuePair<string, string>("grant_type", "client_credentials")
                    };
                    request.Content = new FormUrlEncodedContent(keyValues);
                    
                    string credentials = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}"));
                    request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", credentials);
                    
                    using (var response = await _httpClient.SendAsync(request))
                    {
                        if (response.IsSuccessStatusCode)
                        {
                            string json = await response.Content.ReadAsStringAsync();
                            var jObj = JObject.Parse(json);
                            return jObj["access_token"]?.ToString();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to get developer token: {ex.Message}");
            }
            return null;
        }

        public async Task<bool> TestSpotifyApiConnectionAsync(string token)
        {
            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Get, "https://api.spotify.com/v1/playlists/37i9dQZF1DXcBWIGsy6aRO"))
                {
                    request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                    using (var response = await _httpClient.SendAsync(request))
                    {
                        return response.IsSuccessStatusCode;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Test API connection failed: {ex.Message}");
                return false;
            }
        }

        public async Task<SpotifyPlaylistMetadata?> GetSpotifyPlaylistMetadataFromScrapingAsync(string type, string id)
        {
            string embedUrl = $"https://open.spotify.com/embed/{type}/{id}";
            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Get, embedUrl))
                {
                    request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");
                    using (var response = await _httpClient.SendAsync(request))
                    {
                        if (response.IsSuccessStatusCode)
                        {
                            string html = await response.Content.ReadAsStringAsync();
                            var match = Regex.Match(html, @"<script\s+id=""__NEXT_DATA__""\s+type=""application/json""[^>]*>(.*?)</script>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
                            if (match.Success)
                            {
                                string jsonStr = match.Groups[1].Value.Trim();
                                var jObj = JObject.Parse(jsonStr);
                                var pageProps = jObj["props"]?["pageProps"];
                                var entity = pageProps?["state"]?["data"]?["entity"] ?? pageProps?["state"]?["entity"];
                                
                                string name = entity?["name"]?.ToString() ?? entity?["title"]?.ToString() ?? pageProps?["name"]?.ToString() ?? pageProps?["title"]?.ToString() ?? (type == "playlist" ? "Spotify Playlist" : "Spotify Album");
                                
                                var metadata = new SpotifyPlaylistMetadata
                                {
                                    Name = name,
                                    Type = type,
                                    Tracks = new List<SpotifyTrack>()
                                };

                                var trackList = entity?["trackList"] as JArray;
                                if (trackList != null)
                                {
                                    foreach (var item in trackList)
                                    {
                                        string? trackTitle = item["title"]?.ToString();
                                        string? trackArtist = item["subtitle"]?.ToString() ?? item["artist"]?.ToString();
                                        string? trackUri = item["uri"]?.ToString();
                                        
                                        if (!string.IsNullOrEmpty(trackTitle))
                                        {
                                            metadata.Tracks.Add(new SpotifyTrack
                                            {
                                                Title = trackTitle,
                                                Artist = string.IsNullOrEmpty(trackArtist) ? "Unknown Artist" : trackArtist,
                                                Uri = trackUri ?? ""
                                            });
                                        }
                                    }
                                }
                                
                                return metadata;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Spotify playlist scraping failed: {ex.Message}");
            }
            return null;
        }

        public async Task<SpotifyPlaylistMetadata?> GetSpotifyPlaylistMetadataAsync(string url, string? userToken, string? clientId, string? clientSecret)
        {
            var match = Regex.Match(url, @"open\.spotify\.com/(playlist|album)/([a-zA-Z0-9]+)", RegexOptions.IgnoreCase);
            if (!match.Success) return null;

            string type = match.Groups[1].Value.ToLower();
            string id = match.Groups[2].Value;

            string? token = null;

            // 1. Try User Account Token if valid
            if (!string.IsNullOrEmpty(userToken))
            {
                token = userToken;
            }
            // 2. Try Developer Client Credentials Flow
            else if (!string.IsNullOrEmpty(clientId) && !string.IsNullOrEmpty(clientSecret))
            {
                token = await GetSpotifyDeveloperTokenAsync(clientId, clientSecret);
            }

            // 3. If token is available, use API Mode
            if (!string.IsNullOrEmpty(token))
            {
                try
                {
                    string name = type == "playlist" ? "Spotify Playlist" : "Spotify Album";
                    using (var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.spotify.com/v1/{type}s/{id}"))
                    {
                        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                        using (var response = await _httpClient.SendAsync(request))
                        {
                            if (response.IsSuccessStatusCode)
                            {
                                var jObj = JObject.Parse(await response.Content.ReadAsStringAsync());
                                name = jObj["name"]?.ToString() ?? name;
                            }
                        }
                    }

                    var metadata = new SpotifyPlaylistMetadata
                    {
                        Name = name,
                        Type = type,
                        Tracks = new List<SpotifyTrack>()
                    };

                    int offset = 0;
                    int limit = (type == "playlist") ? 100 : 50;
                    bool hasMore = true;

                    while (hasMore)
                    {
                        string tracksUrl = $"https://api.spotify.com/v1/{type}s/{id}/tracks?offset={offset}&limit={limit}";
                        using (var request = new HttpRequestMessage(HttpMethod.Get, tracksUrl))
                        {
                            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                            using (var response = await _httpClient.SendAsync(request))
                            {
                                if (!response.IsSuccessStatusCode)
                                {
                                    hasMore = false;
                                    break;
                                }

                                var jObj = JObject.Parse(await response.Content.ReadAsStringAsync());
                                var items = jObj["items"] as JArray;
                                if (items == null || items.Count == 0)
                                {
                                    hasMore = false;
                                    break;
                                }

                                foreach (var item in items)
                                {
                                    string title = "";
                                    string artist = "";
                                    string trackUri = "";

                                    if (type == "playlist")
                                    {
                                        var trackObj = item["track"];
                                        if (trackObj != null)
                                        {
                                            title = trackObj["name"]?.ToString() ?? "";
                                            trackUri = trackObj["uri"]?.ToString() ?? "";
                                            var artistsList = new List<string>();
                                            var artistsArr = trackObj["artists"] as JArray;
                                            if (artistsArr != null)
                                            {
                                                foreach (var art in artistsArr)
                                                {
                                                    string? aName = art["name"]?.ToString();
                                                    if (!string.IsNullOrEmpty(aName)) artistsList.Add(aName);
                                                }
                                            }
                                            artist = string.Join(", ", artistsList);
                                        }
                                    }
                                    else // album
                                    {
                                        title = item["name"]?.ToString() ?? "";
                                        trackUri = item["uri"]?.ToString() ?? "";
                                        var artistsList = new List<string>();
                                        var artistsArr = item["artists"] as JArray;
                                        if (artistsArr != null)
                                        {
                                            foreach (var art in artistsArr)
                                            {
                                                string? aName = art["name"]?.ToString();
                                                if (!string.IsNullOrEmpty(aName)) artistsList.Add(aName);
                                            }
                                        }
                                        artist = string.Join(", ", artistsList);
                                    }

                                    if (!string.IsNullOrEmpty(title))
                                    {
                                        metadata.Tracks.Add(new SpotifyTrack
                                        {
                                            Title = title,
                                            Artist = string.IsNullOrEmpty(artist) ? "Unknown Artist" : artist,
                                            Uri = trackUri
                                        });
                                    }
                                }

                                offset += items.Count;
                                hasMore = jObj["next"] != null && jObj["next"].Type != JTokenType.Null;
                            }
                        }
                    }

                    return metadata;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Spotify API retrieval failed: {ex.Message}. Falling back to scraping.");
                }
            }

            // 4. Fallback to scraping
            return await GetSpotifyPlaylistMetadataFromScrapingAsync(type, id);
        }


        private void ParseFfmpegProgress(string line, double trimStartSeconds, double trimEndSeconds, ref double lastPercentage)
        {
            var match = Regex.Match(line, @"time=(?<time>\d{2}:\d{2}:\d{2}\.\d+)");
            if (match.Success)
            {
                if (TimeSpan.TryParse(match.Groups["time"].Value, out TimeSpan currentTime))
                {
                    double currentSeconds = currentTime.TotalSeconds;
                    double totalSeconds = trimEndSeconds - trimStartSeconds;
                    
                    if (totalSeconds > 0)
                    {
                        double percentage = (currentSeconds / totalSeconds) * 100;
                        percentage = Math.Min(100, Math.Max(0, percentage));
                        
                        var sizeMatch = Regex.Match(line, @"size=\s*(?<size>\d+[a-zA-Z]+)");
                        var speedMatch = Regex.Match(line, @"speed=\s*(?<speed>[\d\.]+x)");
                        
                        string extraInfo = "";
                        if (sizeMatch.Success) extraInfo += $" ({sizeMatch.Groups["size"].Value})";
                        if (speedMatch.Success) extraInfo += $" Speed: {speedMatch.Groups["speed"].Value}";
                        
                        if (Math.Abs(percentage - lastPercentage) >= 0.1 || percentage >= 100)
                        {
                            var timeSpanCurrent = TimeSpan.FromSeconds(currentSeconds);
                            var timeSpanTotal = TimeSpan.FromSeconds(totalSeconds);
                            string currentStr = timeSpanCurrent.ToString(@"mm\:ss");
                            string totalStr = timeSpanTotal.ToString(@"mm\:ss");
                            
                            ReportProgress($"Downloading part: {percentage:F1}% ({currentStr} / {totalStr}){extraInfo}", null, percentage, false);
                            lastPercentage = percentage;
                        }
                    }
                }
            }
        }

        private void ParseYtDlpProgress(string line, ref bool progressStarted, ref double lastPercentage)
        {
            if (string.IsNullOrWhiteSpace(line)) return;

            string dlLabel = "[download]";
            if (line.StartsWith(dlLabel))
            {
                string dlContent = line.Substring(dlLabel.Length).Trim();
                if (dlContent.StartsWith("Destination:"))
                {
                    ReportProgress("Downloading...", null, 0, true);
                }
                else if (dlContent.Contains("has already been downloaded"))
                {
                    ReportProgress("File already downloaded.", null, 100, false);
                }
                else
                {
                    var match = Regex.Match(dlContent, @"(?<percent>[\d\.]+)%\s+of(?:\s+~)?\s*(?<size>[\d\.]+[\w]+)(?:\s+at\s+(?<speed>[\d\.]+[\w]+/s))?(?:\s+ETA\s+(?<eta>[\d:]+))?");
                    if (match.Success)
                    {
                        if (double.TryParse(match.Groups["percent"].Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double percent))
                        {
                            if (percent != lastPercentage)
                            {
                                string sizeStr = match.Groups["size"].Value;
                                string speedStr = match.Groups["speed"].Success ? match.Groups["speed"].Value : "N/A";
                                string etaStr = match.Groups["eta"].Success ? match.Groups["eta"].Value : "N/A";
                                ReportProgress($"Downloading: {percent:F1}% of {sizeStr} ({speedStr}) ETA: {etaStr}", null, percent, false);
                                lastPercentage = percent;
                            }
                        }
                    }
                }
            }
            else if (line.StartsWith("[ExtractAudio]"))
            {
                ReportProgress("Extracting audio...", null, 100, true);
            }
            else if (line.StartsWith("[Merger]"))
            {
                ReportProgress("Merging streams...", null, 100, true);
            }
        }
    }
}
