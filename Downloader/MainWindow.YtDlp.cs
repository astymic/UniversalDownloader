using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows; 
using System.Windows.Controls;
using System.Windows.Threading;
using Newtonsoft.Json.Linq;

namespace UniversalDownloader
{
    public partial class MainWindow 
    {
        private const string YtDlpFileName = "yt-dlp.exe"; 
        private string _ytDlpExecutablePath; 
        private const string YtDlpDownloadUrl = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe";
        private const string YtDlpVersionApiUrl = "https://api.github.com/repos/yt-dlp/yt-dlp/releases/latest";
        private bool _isYtDlpReady = false; 
        private string _currentDownloadingComponent = null; 
        private long _ytDlpCurrentComponentTotalBytes = -1; 


        private async Task<string> GetLocalYtDlpVersionAsync()
        {
            if (!File.Exists(_ytDlpExecutablePath))
            {
                return null;
            }

            try
            {
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = _ytDlpExecutablePath,
                    Arguments = "--version",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = System.Text.Encoding.UTF8
                };

                using (Process process = Process.Start(psi))
                {
                    string versionOutput = await process.StandardOutput.ReadToEndAsync();
                    await process.WaitForExitAsync(); // Use async version
                    if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(versionOutput))
                    {
                        return versionOutput.Trim();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting local yt-dlp version: {ex.Message}");
            }
            return null;
        }

        private async Task<string> GetLatestYtDlpVersionTagAsync()
        {
            try
            {
                using (var tempHttpClient = new HttpClient()) // Use a temp client for this specific API call
                {
                    tempHttpClient.DefaultRequestHeaders.UserAgent.ParseAdd("UniversalDownloaderApp/1.0");
                    HttpResponseMessage response = await tempHttpClient.GetAsync(YtDlpVersionApiUrl);
                    response.EnsureSuccessStatusCode();
                    string jsonResponse = await response.Content.ReadAsStringAsync();
                    JObject releaseInfo = JObject.Parse(jsonResponse);
                    return releaseInfo["tag_name"]?.ToString()?.Trim();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error fetching latest yt-dlp version: {ex.Message}");
                return null;
            }
        }

        private async Task CheckAndEnsureYtDlpExistsAsync(bool forceUpdateCheck = false)
        {
            _isYtDlpReady = false;
            if (FileNameTextBlock != null) FileNameTextBlock.Text = "";

            bool fileExists = File.Exists(_ytDlpExecutablePath);
            string localVersion = null;

            if (fileExists)
            {
                localVersion = await GetLocalYtDlpVersionAsync();
                if (localVersion == null)
                {
                    if (StatusTextBlock != null) StatusTextBlock.Text = $"Status: Local {YtDlpFileName} seems corrupted. Re-downloading...";
                    if (FileNameTextBlock != null) FileNameTextBlock.Text = $"Re-downloading: {YtDlpFileName}";
                    try { File.Delete(_ytDlpExecutablePath); } catch { /* best effort */ }
                    fileExists = false;
                }
            }

            bool needsDownload = !fileExists;
            bool updateAvailable = false;

            if (fileExists && (forceUpdateCheck || IsFirstRunToday("YtDlpUpdateCheck")))
            {
                if (StatusTextBlock != null) StatusTextBlock.Text = $"Status: Checking for {YtDlpFileName} updates...";
                string latestVersionTag = await GetLatestYtDlpVersionTagAsync();

                if (!string.IsNullOrWhiteSpace(latestVersionTag) && localVersion != latestVersionTag)
                {
                    updateAvailable = true;
                    if (MessageBox.Show($"A new version of {YtDlpFileName} ({latestVersionTag}) is available (you have {localVersion}).\nWould you like to update now?",
                                        "yt-dlp Update Available", MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
                    {
                        if (StatusTextBlock != null) StatusTextBlock.Text = $"Status: Updating {YtDlpFileName} to version {latestVersionTag}...";
                        if (FileNameTextBlock != null) FileNameTextBlock.Text = $"Updating: {YtDlpFileName}";
                        try { File.Delete(_ytDlpExecutablePath); } catch { /* best effort */ }
                        needsDownload = true;
                    }
                    else
                    {
                        if (StatusTextBlock != null) StatusTextBlock.Text = $"Status: {YtDlpFileName} (version {localVersion}) found. Update to {latestVersionTag} skipped.";
                        _isYtDlpReady = true;
                    }
                }
                else if (string.IsNullOrWhiteSpace(latestVersionTag))
                {
                    if (StatusTextBlock != null) StatusTextBlock.Text = $"Status: Could not check for {YtDlpFileName} updates. Using local version {localVersion}.";
                    _isYtDlpReady = true;
                }
                else
                {
                    if (StatusTextBlock != null) StatusTextBlock.Text = $"Status: {YtDlpFileName} (version {localVersion}) is up to date.";
                    _isYtDlpReady = true;
                }
                SetLastRunTimestamp("YtDlpUpdateCheck");
            }
            else if (fileExists && localVersion != null) // File exists, valid, and no update check was due/forced
            {
                if (StatusTextBlock != null) StatusTextBlock.Text = $"Status: {YtDlpFileName} (version {localVersion}) found.";
                _isYtDlpReady = true;
            }


            if (needsDownload)
            {
                if (!fileExists && !updateAvailable) // Initial download message if not an update
                {
                    if (StatusTextBlock != null) StatusTextBlock.Text = $"Status: {YtDlpFileName} not found. Attempting to download...";
                    if (FileNameTextBlock != null) FileNameTextBlock.Text = $"Downloading: {YtDlpFileName}";
                }

                bool downloaded = await TryDownloadYtDlpInternalAsync();
                if (downloaded)
                {
                    string newLocalVersion = await GetLocalYtDlpVersionAsync();
                    if (newLocalVersion != null)
                    {
                        if (StatusTextBlock != null) StatusTextBlock.Text = $"Status: {YtDlpFileName} (version {newLocalVersion}) downloaded. Ready.";
                        if (FileNameTextBlock != null) FileNameTextBlock.Text = "";
                        _isYtDlpReady = true;
                    }
                    else
                    {
                        if (StatusTextBlock != null) StatusTextBlock.Text = $"Status: Downloaded {YtDlpFileName} appears corrupted. YouTube downloads unavailable.";
                        if (FileNameTextBlock != null) FileNameTextBlock.Text = $"{YtDlpFileName} download corrupted.";
                        _isYtDlpReady = false;
                        try { File.Delete(_ytDlpExecutablePath); } catch { /* best effort */ }
                    }
                }
                else
                {
                    if (StatusTextBlock != null) StatusTextBlock.Text = $"Status: Failed to download {YtDlpFileName}. YouTube downloads unavailable.";
                    if (FileNameTextBlock != null) FileNameTextBlock.Text = $"{YtDlpFileName} download failed.";
                    _isYtDlpReady = false;
                }
            }
            else if (!_isYtDlpReady && fileExists && localVersion != null) // File exists, no download, but ensure ready state
            {
                _isYtDlpReady = true; // Already confirmed localVersion is not null
            }
            else if (!_isYtDlpReady && fileExists && localVersion == null) // File exists, but was problematic and no download occurred (e.g. update check failed and user skipped)
            {
                if (StatusTextBlock != null) StatusTextBlock.Text = $"Status: Local {YtDlpFileName} exists but may be non-functional. YouTube features might be limited.";
            }
        }

        private async Task<bool> TryDownloadYtDlpInternalAsync()
        {
            if (DownloadProgressBar != null)
            {
                DownloadProgressBar.Value = 0;
                DownloadProgressBar.IsIndeterminate = true;
            }
            try
            {
                using (var downloadClient = new HttpClient())
                {
                    downloadClient.Timeout = TimeSpan.FromMinutes(10); // Increased timeout for potentially large file
                    if (StatusTextBlock != null) StatusTextBlock.Text = $"Status: Downloading {YtDlpFileName} from {YtDlpDownloadUrl}...";

                    var response = await downloadClient.GetAsync(YtDlpDownloadUrl, HttpCompletionOption.ResponseHeadersRead);
                    response.EnsureSuccessStatusCode();
                    long? totalDownloadSize = response.Content.Headers.ContentLength;
                    long totalBytesRead = 0;
                    int lastPercentage = -1;

                    if (DownloadProgressBar != null)
                    {
                        DownloadProgressBar.IsIndeterminate = !(totalDownloadSize.HasValue && totalDownloadSize.Value > 0);
                        if (!DownloadProgressBar.IsIndeterminate) DownloadProgressBar.Maximum = 100;
                    }

                    using (var contentStream = await response.Content.ReadAsStreamAsync())
                    using (var fileStream = new FileStream(_ytDlpExecutablePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true)) // Increased buffer
                    {
                        byte[] buffer = new byte[81920]; // Matched buffer size
                        int bytesRead;
                        while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                        {
                            await fileStream.WriteAsync(buffer, 0, bytesRead);
                            totalBytesRead += bytesRead;
                            if (totalDownloadSize.HasValue && totalDownloadSize.Value > 0)
                            {
                                if (DownloadProgressBar != null && DownloadProgressBar.IsIndeterminate) DownloadProgressBar.IsIndeterminate = false;
                                double percentage = (double)totalBytesRead / totalDownloadSize.Value * 100;
                                if ((int)percentage != lastPercentage)
                                {
                                    if (DownloadProgressBar != null) DownloadProgressBar.Value = percentage;
                                    if (StatusTextBlock != null) StatusTextBlock.Text = $"Status: Downloading {YtDlpFileName}... {percentage:F0}%";
                                    lastPercentage = (int)percentage;
                                }
                            }
                            else
                            {
                                if (DownloadProgressBar != null && !DownloadProgressBar.IsIndeterminate) DownloadProgressBar.IsIndeterminate = true;
                                if (StatusTextBlock != null) StatusTextBlock.Text = $"Status: Downloading {YtDlpFileName} ({Utilities.FormatBytesOutput(totalBytesRead)})...";
                            }
                        }
                    }
                    if (DownloadProgressBar != null && !DownloadProgressBar.IsIndeterminate) DownloadProgressBar.Value = 100;
                    return true;
                }
            }
            catch (Exception ex)
            {
                if (StatusTextBlock != null) StatusTextBlock.Text = $"Status: Error downloading {YtDlpFileName}: {ex.Message.Split('\n')[0]}";
                if (File.Exists(_ytDlpExecutablePath)) { try { File.Delete(_ytDlpExecutablePath); } catch { /* best effort */ } }
                return false;
            }
            finally { if (DownloadProgressBar != null) DownloadProgressBar.IsIndeterminate = false; }
        }

        private async Task LoadYouTubeQualitiesWithYtDlp(string videoUrl)
        {
            if (QualitySection != null) QualitySection.Visibility = Visibility.Collapsed;
            if (YouTubeQualityComboBox == null || StatusTextBlock == null || FileNameTextBlock == null || DownloadButton == null) return;
            YouTubeQualityComboBox.ItemsSource = null;

            if (!_isYtDlpReady)
            {
                StatusTextBlock.Text = $"Status: {YtDlpFileName} is not available. Cannot fetch YouTube qualities.";
                FileNameTextBlock.Text = ""; return;
            }

            StatusTextBlock.Text = "Status: Fetching YouTube video qualities...";
            FileNameTextBlock.Text = "Fetching YouTube Info...";

            var qualitiesToAdd = new List<YouTubeQualityItem>();
            JArray ytDlpReportedFormats = null;

            try
            {
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = _ytDlpExecutablePath,
                    Arguments = $"-J --no-warnings --ignore-config --flat-playlist \"{videoUrl}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = System.Text.Encoding.UTF8,
                    StandardErrorEncoding = System.Text.Encoding.UTF8
                };
                using (Process process = Process.Start(psi))
                {
                    string jsonOutput = await process.StandardOutput.ReadToEndAsync();
                    string errorOutput = await process.StandardError.ReadToEndAsync();
                    await process.WaitForExitAsync();
                    if (process.ExitCode != 0 || (!string.IsNullOrWhiteSpace(errorOutput) && !errorOutput.ToLower().Contains("deprecated") && !errorOutput.ToLower().Contains("warning:")))
                    {
                        StatusTextBlock.Text = $"Status: yt-dlp error: {(errorOutput.Split('\n')[0])?.Trim()}"; FileNameTextBlock.Text = "YouTube Info Error";
                        if (QualitySection != null) QualitySection.Visibility = Visibility.Collapsed; return;
                    }
                    if (string.IsNullOrWhiteSpace(jsonOutput))
                    {
                        StatusTextBlock.Text = "Status: yt-dlp returned empty info. Video might be unavailable or private."; FileNameTextBlock.Text = "YouTube Info Error (Empty)";
                        if (QualitySection != null) QualitySection.Visibility = Visibility.Collapsed; return;
                    }
                    JObject videoInfo = JObject.Parse(jsonOutput);
                    string videoTitle = Utilities.SanitizeFileName(videoInfo["title"]?.ToString() ?? "Unknown Title");
                    FileNameTextBlock.Text = $"Video: {videoTitle}";
                    ytDlpReportedFormats = videoInfo["formats"] as JArray;
                }

                if (ytDlpReportedFormats == null || !ytDlpReportedFormats.HasValues)
                {
                    StatusTextBlock.Text = "Status: No formats reported by yt-dlp for this video."; FileNameTextBlock.Text = "No YouTube formats found.";
                    if (QualitySection != null) QualitySection.Visibility = Visibility.Collapsed; return;
                }

                var allAvailableVideoHeights = new HashSet<int>();
                var rawAudioFormats = new List<YouTubeQualityItem>();

                foreach (JToken format in ytDlpReportedFormats)
                {
                    string vcodec = format["vcodec"]?.ToString()?.ToLower() ?? "none";
                    string acodec = format["acodec"]?.ToString()?.ToLower() ?? "none";
                    int? height = format["height"]?.ToObject<int?>();
                    string protocol = format["protocol"]?.ToString()?.ToLower() ?? "";

                    if (protocol.Contains("dash") || protocol.Contains("hls"))
                    {
                        if (vcodec == "none" && acodec == "none") continue;
                    }

                    if (vcodec != "none" && vcodec != "unknown" && height.HasValue)
                    {
                        allAvailableVideoHeights.Add(height.Value);
                    }
                    else if (acodec != "none" && acodec != "unknown" && (vcodec == "none" || vcodec == "unknown"))
                    {
                        string formatId = format["format_id"]?.ToString();
                        if (string.IsNullOrEmpty(formatId)) continue;
                        double? abr = format["abr"]?.ToObject<double?>();
                        string ext = format["ext"]?.ToString() ?? "N/A";
                        long? filesize = format["filesize"]?.ToObject<long?>() ?? format["filesize_approx"]?.ToObject<long?>();
                        string filesizeStr = filesize.HasValue ? $" ({Utilities.FormatBytesOutput(filesize.Value)})" : "";
                        string label = $"Audio Only ({ext}) ~{abr ?? 0:F0}k{filesizeStr}";
                        rawAudioFormats.Add(new YouTubeQualityItem { Label = label, FormatCode = formatId, IsAudioOnly = true, SortPriority = (int)(abr ?? 0) });
                    }
                }

                qualitiesToAdd.Add(new YouTubeQualityItem { Label = "Best Video + Best Audio", FormatCode = "bestvideo[ext=mp4]+bestaudio[ext=m4a]/bestvideo+bestaudio/best", IsAudioOnly = false, SortPriority = 2000000 });

                var targetResolutionTiers = new List<(int height, string labelName)> {
                    (4320, "8K"), (3840, "4K UHD"), (2160, "4K"), (1440, "1440p QHD"),
                    (1080, "1080p FHD"), (720, "720p HD"), (480, "480p SD"), (360, "360p")
                }.OrderByDescending(t => t.height).ToList();

                var addedTierLabels = new HashSet<string>();
                foreach (var tier in targetResolutionTiers)
                {
                    if (allAvailableVideoHeights.Any(h => h >= tier.height))
                    {
                        string tierLabel = $"{tier.labelName} ({tier.height}p)";
                        if (addedTierLabels.Add(tierLabel))
                        {
                            string formatCode = $"bestvideo[height<={tier.height}][ext=mp4]+bestaudio[ext=m4a]/bestvideo[height<={tier.height}]+bestaudio/best[height<={tier.height}]";

                            qualitiesToAdd.Add(new YouTubeQualityItem
                            {
                                Label = tierLabel,
                                FormatCode = formatCode,
                                IsAudioOnly = false,
                                SortPriority = 1500000 + tier.height
                            });
                        }
                    }
                }

                qualitiesToAdd.Add(new YouTubeQualityItem { Label = "Best Audio Only", FormatCode = "bestaudio/best", IsAudioOnly = true, SortPriority = 100000 });

                if (rawAudioFormats.Any())
                {
                    var bestSpecificAudio = rawAudioFormats.OrderByDescending(a => a.SortPriority).First();
                    if (bestSpecificAudio.Label != "Best Audio Only" && !qualitiesToAdd.Any(q => q.Label == bestSpecificAudio.Label))
                    {
                        bestSpecificAudio.SortPriority = 90000;
                        qualitiesToAdd.Add(bestSpecificAudio);
                    }
                }

                var finalSortedQualities = qualitiesToAdd
                                     .GroupBy(q => q.Label)
                                     .Select(g => g.OrderByDescending(i => i.SortPriority).First())
                                     .OrderByDescending(q => q.SortPriority)
                                     .ToList();

                if (finalSortedQualities.Any())
                {
                    YouTubeQualityComboBox.ItemsSource = finalSortedQualities;
                    if (QualitySection != null) QualitySection.Visibility = Visibility.Visible;
                    await Dispatcher.InvokeAsync(() => { if (YouTubeQualityComboBox.Items.Count > 0) YouTubeQualityComboBox.SelectedIndex = 0; UpdateDownloadButtonState(); }, DispatcherPriority.ContextIdle);
                    StatusTextBlock.Text = "Status: YouTube qualities listed. Select quality to download.";
                }
                else
                {
                    StatusTextBlock.Text = "Status: No downloadable formats could be determined."; FileNameTextBlock.Text = "No YouTube formats found.";
                    if (QualitySection != null) QualitySection.Visibility = Visibility.Collapsed;
                }
            }
            catch (Win32Exception ex) { StatusTextBlock.Text = $"Status: {YtDlpFileName} execution error. {ex.Message.Split('\n')[0]}"; _isYtDlpReady = false; FileNameTextBlock.Text = "yt-dlp Error"; if (QualitySection != null) QualitySection.Visibility = Visibility.Collapsed; }
            catch (Newtonsoft.Json.JsonReaderException jsonEx) { StatusTextBlock.Text = $"Status: Error parsing yt-dlp output: {jsonEx.Message.Split('\n')[0]}."; FileNameTextBlock.Text = "YouTube Info Parse Error"; if (QualitySection != null) QualitySection.Visibility = Visibility.Collapsed; }
            catch (Exception ex) { StatusTextBlock.Text = $"Status: Error processing YouTube info: {ex.Message.Split('\n')[0]}."; FileNameTextBlock.Text = "YouTube Info Error"; if (QualitySection != null) QualitySection.Visibility = Visibility.Collapsed; }
        }
        
        private async Task DownloadYouTubeVideoWithYtDlp(string videoUrl, string formatCode, string tempDownloadFolderPath)
        {
            if (StatusTextBlock == null || FileNameTextBlock == null || DownloadProgressBar == null || YouTubeQualityComboBox == null) return;
            if (!_isYtDlpReady) { StatusTextBlock.Text = $"Status: {YtDlpFileName} is not available. Cannot download YouTube video."; return; }

            _ytDlpCurrentComponentTotalBytes = -1;
            var selectedQualityItem = YouTubeQualityComboBox.SelectedItem as YouTubeQualityItem;
            string baseFileNameTemplate = selectedQualityItem != null && selectedQualityItem.IsAudioOnly ? "%(title)s [%(id)s] (Audio).%(ext)s" : "%(title)s [%(id)s].%(ext)s";
            string outputTemplateInTemp = Path.Combine(tempDownloadFolderPath, baseFileNameTemplate);

            StatusTextBlock.Text = $"Status: Downloading YouTube (format: {formatCode})...";
            FileNameTextBlock.Text = "Preparing YouTube download...";
            DownloadProgressBar.Value = 0; DownloadProgressBar.Maximum = 100; DownloadProgressBar.IsIndeterminate = true;
            _currentDownloadingComponent = null;
            string finalReportedFilePathInTemp = null;

            try
            {
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = _ytDlpExecutablePath,
                    Arguments = $"-f \"{formatCode}\" -o \"{outputTemplateInTemp}\" --no-continue --progress --newline --no-warnings --ignore-config \"{videoUrl}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = System.Text.Encoding.UTF8,
                    StandardErrorEncoding = System.Text.Encoding.UTF8
                };

                string actualFileNameFromYtDlpOutput = "downloaded_video"; 
                bool progressStartedForAnyComponent = false;
                double lastReportedPercentageForComponent = 0;

                using (Process process = new Process())
                {
                    process.StartInfo = psi; process.EnableRaisingEvents = true;

                    process.OutputDataReceived += (s, e) =>
                    {
                        if (e.Data != null)
                            Dispatcher.Invoke(() => ParseYtDlpProgress(e.Data, ref actualFileNameFromYtDlpOutput, ref progressStartedForAnyComponent, ref lastReportedPercentageForComponent, ref finalReportedFilePathInTemp));
                    };
                    process.ErrorDataReceived += (s, e) => { if (e.Data != null && !e.Data.ToLower().Contains("warning:") && !e.Data.ToLower().Contains("deprecated")) Dispatcher.Invoke(() => StatusTextBlock.Text = $"yt-dlp Info/Error: {e.Data.Split('\n')[0]}"); };

                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                    await process.WaitForExitAsync();
                    DownloadProgressBar.IsIndeterminate = false;

                    if (process.ExitCode == 0)
                    {
                        if (_ytDlpCurrentComponentTotalBytes > 0 && DownloadProgressBar.Maximum == _ytDlpCurrentComponentTotalBytes) { DownloadProgressBar.Value = DownloadProgressBar.Maximum; }
                        else if (DownloadProgressBar.Maximum == 100) { DownloadProgressBar.Value = 100; }
                        else { DownloadProgressBar.Value = DownloadProgressBar.Maximum; } 

                        string fileToMove = finalReportedFilePathInTemp; 

                        if (string.IsNullOrEmpty(fileToMove) || !File.Exists(fileToMove))
                        {
                            Debug.WriteLine($"yt-dlp fallback: finalReportedFilePathInTemp was '{fileToMove}'. Searching temp folder '{tempDownloadFolderPath}'...");
                            await Task.Delay(500); 

                            if (Directory.Exists(tempDownloadFolderPath))
                            {
                                var potentialFiles = new DirectoryInfo(tempDownloadFolderPath)
                                    .GetFiles()
                                    .Where(f => !f.Extension.Equals(".part", StringComparison.OrdinalIgnoreCase) &&
                                                 (f.Extension.Equals(".mp4", StringComparison.OrdinalIgnoreCase) ||
                                                  f.Extension.Equals(".mkv", StringComparison.OrdinalIgnoreCase) ||
                                                  f.Extension.Equals(".webm", StringComparison.OrdinalIgnoreCase) ||
                                                  f.Extension.Equals(".m4a", StringComparison.OrdinalIgnoreCase) ||
                                                  f.Extension.Equals(".mp3", StringComparison.OrdinalIgnoreCase) ||
                                                  f.Extension.Equals(".opus", StringComparison.OrdinalIgnoreCase) ||
                                                  f.Extension.Equals(".ogg", StringComparison.OrdinalIgnoreCase)))
                                    .OrderByDescending(f => f.Length) 
                                    .ThenByDescending(f => f.LastWriteTimeUtc) 
                                    .ToList();

                                if (potentialFiles.Any())
                                {
                                    fileToMove = potentialFiles.First().FullName;
                                    Debug.WriteLine($"yt-dlp fallback found: {fileToMove}");
                                }
                                else // Still no media file, check for *any* non-part file
                                {
                                    var anyFiles = new DirectoryInfo(tempDownloadFolderPath)
                                        .GetFiles()
                                        .Where(f => !f.Extension.Equals(".part", StringComparison.OrdinalIgnoreCase))
                                        .OrderByDescending(f => f.Length)
                                        .ToList();
                                    if (anyFiles.Any())
                                    {
                                        fileToMove = anyFiles.First().FullName;
                                        Debug.WriteLine($"yt-dlp fallback (any file type) found: {fileToMove}");
                                    }
                                }
                            }
                        }


                        if (!string.IsNullOrEmpty(fileToMove) && File.Exists(fileToMove))
                        {
                            string targetFileName = Path.GetFileName(fileToMove);
                            string targetPath = Path.Combine(SelectedDirectory, targetFileName);

                            if (!Directory.Exists(SelectedDirectory))
                            {
                                StatusTextBlock.Text = $"Status: Error - Destination directory '{SelectedDirectory}' not found.";
                                FileNameTextBlock.Text = "Move Error";
                                goto EndYtDlpLogic;
                            }

                            int count = 1;
                            string fileNameOnly = Path.GetFileNameWithoutExtension(targetPath);
                            string extension = Path.GetExtension(targetPath);
                            while (File.Exists(targetPath))
                            {
                                targetFileName = $"{fileNameOnly} ({count++}){extension}"; 
                                targetPath = Path.Combine(SelectedDirectory, targetFileName);
                                if (count > 100) { StatusTextBlock.Text = "Status: Too many existing files with similar names."; FileNameTextBlock.Text = "Move Error"; goto EndYtDlpLogic; }
                            }

                            try
                            {
                                File.Move(fileToMove, targetPath);
                                Debug.WriteLine($"Moved yt-dlp file: {fileToMove} TO {targetPath}");
                                StatusTextBlock.Text = $"Status: YouTube download '{Path.GetFileName(targetPath)}' complete!";
                                FileNameTextBlock.Text = $"Completed: {Path.GetFileName(targetPath)}";
                            }
                            catch (Exception moveEx)
                            {
                                Debug.WriteLine($"Error moving yt-dlp file: {moveEx.ToString()}");
                                StatusTextBlock.Text = $"Status: Download complete to temp, but failed to move: {moveEx.Message.Split('\n')[0]}";
                                FileNameTextBlock.Text = "File Move Error";
                            }
                        }
                        else
                        {
                            Debug.WriteLine($"yt-dlp: Final file to move not found or identified. Searched in '{tempDownloadFolderPath}'. finalReportedFilePathInTemp was '{finalReportedFilePathInTemp}'");
                            StatusTextBlock.Text = "Status: YouTube download completed, but output file not identified in temp folder.";
                            FileNameTextBlock.Text = "YouTube Download Error (File Missing)";
                        }
                    }
                    else
                    {
                        DownloadProgressBar.Value = 0;
                        if (!StatusTextBlock.Text.ToLower().Contains("error") && !StatusTextBlock.Text.ToLower().Contains("yt-dlp error"))
                        {
                            StatusTextBlock.Text = $"Status: yt-dlp download failed (code {process.ExitCode}).";
                            FileNameTextBlock.Text = "YouTube Download Failed";
                        }
                    }
                EndYtDlpLogic:;
                }
            }
            catch (Win32Exception ex) { StatusTextBlock.Text = $"Status: {YtDlpFileName} execution error. {ex.Message.Split('\n')[0]}"; _isYtDlpReady = false; DownloadProgressBar.Value = 0; DownloadProgressBar.IsIndeterminate = false; }
            catch (Exception ex) { StatusTextBlock.Text = $"Status: Error during YouTube download: {ex.Message.Split('\n')[0]}"; FileNameTextBlock.Text = "YouTube Download Error"; DownloadProgressBar.Value = 0; DownloadProgressBar.IsIndeterminate = false; }
            finally
            {
                 CleanUpTempFolder(tempDownloadFolderPath);
            }
        }

        private void ParseYtDlpProgress(string outputLine,
                                 ref string baseFileNameFromYtDlpOutput, 
                                 ref bool progressStartedForAnyComponent,
                                 ref double lastKnownPercentageForComponent,
                                 ref string finalReportedFilePathInTemp) 
        {
            if (StatusTextBlock == null || FileNameTextBlock == null || DownloadProgressBar == null) return;
            outputLine = outputLine.Trim();
            
            Match finalFileMatch = Regex.Match(outputLine, @"(?:\[download\] Destination:|\\[Merger\\] Merging formats into ""|\\[ExtractAudio\\] Destination: |\\[VideoRemuxer\\] Destination: |\\[Fixup[^\]]*\\] Destination: )\s*(?:""?)([^""\r\n]+)");
            if (finalFileMatch.Success)
            {
                string reportedPath = finalFileMatch.Groups[1].Value.Trim('"', ' ');

                bool isProcessingStep = outputLine.Contains("[Merger]") || outputLine.Contains("[ExtractAudio]") ||
                                        outputLine.Contains("[VideoRemuxer]") || outputLine.Contains("[Fixup");

                _currentDownloadingComponent = reportedPath; 
                finalReportedFilePathInTemp = reportedPath;  

                if (isProcessingStep)
                {
                    FileNameTextBlock.Text = $"Processing: {Path.GetFileName(_currentDownloadingComponent)}";
                    StatusTextBlock.Text = $"Status: yt-dlp - {outputLine}"; 
                    if (!DownloadProgressBar.IsIndeterminate) DownloadProgressBar.IsIndeterminate = true;
                    _ytDlpCurrentComponentTotalBytes = 0;
                }
                else // It's a "[download] Destination:" line for a component
                {
                    FileNameTextBlock.Text = $"Downloading: {Path.GetFileName(_currentDownloadingComponent)}";
                    
                    _ytDlpCurrentComponentTotalBytes = -1;
                    DownloadProgressBar.IsIndeterminate = true;
                    DownloadProgressBar.Value = 0;
                    DownloadProgressBar.Maximum = 100; 
                    lastKnownPercentageForComponent = 0;
                }
                progressStartedForAnyComponent = true;
                Debug.WriteLine($"yt-dlp reported path: {finalReportedFilePathInTemp} (Processing: {isProcessingStep})");
                return;
            }

            Match alreadyDownloadedMatch = Regex.Match(outputLine, @"\[download\]\s+""?([^""\r\n]+)""?\s+has already been downloaded");
            if (alreadyDownloadedMatch.Success)
            {
                _currentDownloadingComponent = alreadyDownloadedMatch.Groups[1].Value.Trim('"', ' ');
                finalReportedFilePathInTemp = _currentDownloadingComponent;
                FileNameTextBlock.Text = $"File: {Path.GetFileName(finalReportedFilePathInTemp)} (already exists)";
                StatusTextBlock.Text = "Status: File already downloaded by yt-dlp.";
                DownloadProgressBar.IsIndeterminate = false;
                DownloadProgressBar.Maximum = 100;
                DownloadProgressBar.Value = 100;
                lastKnownPercentageForComponent = 100;
                _ytDlpCurrentComponentTotalBytes = 0; 
                progressStartedForAnyComponent = true;
                Debug.WriteLine($"yt-dlp reported already downloaded: {finalReportedFilePathInTemp}");
                return; 
            }
            
            Match progressMatch = Regex.Match(outputLine, @"\[download\]\s+(?<percent>[\d\.]+?)%\s+of\s+(?:~?\s*)?(?<total_size_str>[\d\.]+[KMGT]?i?B|unknown)(?:\s+at\s+(?<speed>[\d\.]+[KMGT]?i?B/s))?(?:\s+ETA\s+(?<eta>[\d:]+))?(\s+in\s+[\d:]+)?|\[download\]\s+100%\s+of\s+(?<total_size_full_str>[\d\.]+[KMGT]?i?B)\s+in\s+[\d:]+");
            if (progressMatch.Success)
            {
                progressStartedForAnyComponent = true;
                double currentPercent = 0.0;
                string totalSizeStringForDisplay;

                if (progressMatch.Groups["percent"].Success && !string.IsNullOrEmpty(progressMatch.Groups["percent"].Value))
                {
                    if (!double.TryParse(progressMatch.Groups["percent"].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out currentPercent))
                    {
                        Debug.WriteLine($"Failed to parse percent from yt-dlp progress: {progressMatch.Groups["percent"].Value}");
                        return;
                    }
                    totalSizeStringForDisplay = progressMatch.Groups["total_size_str"].Value;
                }
                else if (progressMatch.Groups["total_size_full_str"].Success && !string.IsNullOrEmpty(progressMatch.Groups["total_size_full_str"].Value)) // Handles "100% of X in Y"
                {
                    currentPercent = 100.0;
                    totalSizeStringForDisplay = progressMatch.Groups["total_size_full_str"].Value;
                }
                else
                {
                    Debug.WriteLine($"yt-dlp progress regex matched but no percent/total_size_full_str group found: {outputLine}");
                    return;
                }

                if (_ytDlpCurrentComponentTotalBytes == -1 || (_ytDlpCurrentComponentTotalBytes == 0 && totalSizeStringForDisplay.ToLower() != "unknown"))
                {
                    long parsedTotalBytes = Utilities.ParseYtDlpSizeStringToBytes(totalSizeStringForDisplay);
                    _ytDlpCurrentComponentTotalBytes = parsedTotalBytes > 0 ? parsedTotalBytes : 0; 

                    if (_ytDlpCurrentComponentTotalBytes > 0)
                    {
                        DownloadProgressBar.Maximum = _ytDlpCurrentComponentTotalBytes;
                        DownloadProgressBar.IsIndeterminate = false;
                    }
                    else // Size is unknown or zero
                    {
                        DownloadProgressBar.Maximum = 100; // Use percentage based max
                        DownloadProgressBar.IsIndeterminate = false; // Still show percentage progress
                    }
                }

                string speed = progressMatch.Groups["speed"].Value;
                string eta = progressMatch.Groups["eta"].Value;
                string componentNameDisplay = string.IsNullOrWhiteSpace(Path.GetFileName(_currentDownloadingComponent)) ? baseFileNameFromYtDlpOutput : Path.GetFileName(_currentDownloadingComponent);

                if (DownloadProgressBar.IsIndeterminate && _ytDlpCurrentComponentTotalBytes >= 0) 
                {
                    DownloadProgressBar.IsIndeterminate = false;
                }

                if (_ytDlpCurrentComponentTotalBytes > 0)
                {
                    if (DownloadProgressBar.Maximum != _ytDlpCurrentComponentTotalBytes)
                    {
                        DownloadProgressBar.Maximum = _ytDlpCurrentComponentTotalBytes;
                    }
                    long currentDownloadedBytes = (long)((currentPercent / 100.0) * _ytDlpCurrentComponentTotalBytes);
                    DownloadProgressBar.Value = Math.Min(currentDownloadedBytes, _ytDlpCurrentComponentTotalBytes); 
                    StatusTextBlock.Text = $"Downloading ({componentNameDisplay}): {currentPercent:F1}% ({Utilities.FormatBytesOutput(currentDownloadedBytes)} / {Utilities.FormatBytesOutput(_ytDlpCurrentComponentTotalBytes)}) | Speed: {speed} | ETA: {eta}";
                }
                else // Size unknown or zero, work with percentages
                {
                    if (DownloadProgressBar.Maximum != 100) DownloadProgressBar.Maximum = 100; 
                    DownloadProgressBar.Value = Math.Min(currentPercent, 100.0); 
                    StatusTextBlock.Text = $"Downloading ({componentNameDisplay}): {currentPercent:F1}% of {totalSizeStringForDisplay} | Speed: {speed} | ETA: {eta}";
                }
                lastKnownPercentageForComponent = currentPercent;
            }
        }
    }
}