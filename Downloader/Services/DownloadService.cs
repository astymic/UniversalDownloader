using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Globalization;

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

        private void ReportProgress(string status, string? filename = null, double percentage = 0, bool isIndeterminate = false)
        {
            ProgressChanged?.Invoke(this, new DownloadProgressArgs
            {
                StatusMessage = status,
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
                    Arguments = $"-J --no-warnings --ignore-config --flat-playlist \"{url}\"",
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
                    
                    ReportProgress("Status: Downloading...", tempFileName, 0, true);

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
                                ReportProgress($"Status: Downloading ({Utilities.FormatBytesOutput(totalBytesRead)})...", tempFileName, 0, true);
                            }
                        }
                    }

                    CopyToFinalDestinationAndClean(tempDownloadFolder, finalDestinationFolder);
                    ReportProgress($"Status: Download complete! Saved as '{tempFileName}'", tempFileName, 100, false);
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

        public async Task<bool> DownloadWithYtDlpAsync(string url, string? formatSelection, string tempDownloadFolder, string finalDestinationFolder, bool extractAudio, string audioFormat, bool useTrimming, double trimStartSeconds, double trimEndSeconds, CancellationToken cancellationToken)
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

            string formatArgument = extractAudio 
                ? $"--extract-audio --audio-format {audioFormat} --audio-quality 0 -f \"{(string.IsNullOrWhiteSpace(formatSelection) ? "bestaudio/best" : formatSelection)}\""
                : $"-f \"{formatSelection}\"";

            string trimArgument = "";

            string arguments = $"-o \"{outputTemplate}\" {formatArgument} {trimArgument} --no-continue --progress --newline --no-warnings --ignore-config \"{url}\"";

            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = _dependencyManager.YtDlpExecutablePath,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                StandardErrorEncoding = System.Text.Encoding.UTF8
            };
            if (_dependencyManager.IsFfmpegReady)
            {
                psi.Arguments += $" --ffmpeg-location \"{_dependencyManager.FfmpegExecutablePath}\"";
            }
            psi.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";

            bool progressStarted = false;
            double lastPercentage = 0;

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
                    if (useTrimming && trimEndSeconds > trimStartSeconds)
                    {
                        // Offline trimming route: Video is fully downloaded to temp, now fast-trim it
                        string[] downloadedFiles = Directory.GetFiles(tempDownloadFolder);
                        if (downloadedFiles.Length > 0)
                        {
                            string sourceFile = downloadedFiles[0]; // yt-dlp final merged file
                            await TrimLocalVideoAsync(sourceFile, finalDestinationFolder, extractAudio, audioFormat, trimStartSeconds, trimEndSeconds, cancellationToken);
                        }
                    }
                    else
                    {
                        CopyToFinalDestinationAndClean(tempDownloadFolder, finalDestinationFolder);
                    }
                }
                else
                {
                    CleanDirectory(tempDownloadFolder);
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
            try
            {
                if (!Directory.Exists(finalDestinationFolder)) Directory.CreateDirectory(finalDestinationFolder);

                string[] files = Directory.GetFiles(tempDownloadFolder);
                foreach (var sourceFile in files)
                {
                    string fileName = Path.GetFileName(sourceFile);
                    string destFile = Path.Combine(finalDestinationFolder, fileName);
                    
                    ReportProgress("Status: Copying file to final destination...", fileName, 100, true);
                    File.Copy(sourceFile, destFile, true);
                    
                    try { File.Delete(sourceFile); } catch { }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error during copy/cleanup: {ex.Message}");
            }
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
                    ReportProgress("Status: Downloading...", null, 0, true);
                }
                else if (dlContent.Contains("has already been downloaded"))
                {
                    ReportProgress("Status: File already downloaded.", null, 100, false);
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
                ReportProgress("Status: Extracting audio...", null, 100, true);
            }
            else if (line.StartsWith("[Merger]"))
            {
                ReportProgress("Status: Merging formats...", null, 100, true);
            }
        }
    }
}
