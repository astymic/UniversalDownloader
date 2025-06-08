﻿using System;
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

        #region Trimming Properties
        private double _videoDurationInSeconds;
        public double VideoDurationInSeconds
        {
            get => _videoDurationInSeconds;
            set { _videoDurationInSeconds = value; OnPropertyChanged(nameof(VideoDurationInSeconds)); }
        }

        private bool _isTrimmingEnabled;
        public bool IsTrimmingEnabled
        {
            get => _isTrimmingEnabled;
            set { _isTrimmingEnabled = value; OnPropertyChanged(nameof(IsTrimmingEnabled)); }
        }

        private double _trimStartTimeInSeconds;
        public double TrimStartTimeInSeconds
        {
            get => _trimStartTimeInSeconds;
            set
            {
                if (_trimStartTimeInSeconds != value)
                {
                    _trimStartTimeInSeconds = value;
                    OnPropertyChanged(nameof(TrimStartTimeInSeconds));
                    TrimStartTimeText = SecondsToTimeString(value); // Update text when slider moves
                }
            }
        }

        private double _trimEndTimeInSeconds;
        public double TrimEndTimeInSeconds
        {
            get => _trimEndTimeInSeconds;
            set
            {
                if (_trimEndTimeInSeconds != value)
                {
                    _trimEndTimeInSeconds = value;
                    OnPropertyChanged(nameof(TrimEndTimeInSeconds));
                    TrimEndTimeText = SecondsToTimeString(value); // Update text when slider moves
                }
            }
        }

        private string _trimStartTimeText;
        public string TrimStartTimeText
        {
            get => _trimStartTimeText;
            set
            {
                if (_trimStartTimeText != value)
                {
                    _trimStartTimeText = value;
                    OnPropertyChanged(nameof(TrimStartTimeText));
                }
            }
        }

        private string _trimEndTimeText;
        public string TrimEndTimeText
        {
            get => _trimEndTimeText;
            set
            {
                if (_trimEndTimeText != value)
                {
                    _trimEndTimeText = value;
                    OnPropertyChanged(nameof(TrimEndTimeText));
                }
            }
        }

        private string SecondsToTimeString(double totalSeconds)
        {
            TimeSpan time = TimeSpan.FromSeconds(totalSeconds);
            return time.ToString(@"hh\:mm\:ss");
        }

        private bool TimeStringToSeconds(string timeString, out double seconds)
        {
            if (TimeSpan.TryParseExact(timeString, @"hh\:mm\:ss", CultureInfo.InvariantCulture, out TimeSpan parsedTime))
            {
                seconds = parsedTime.TotalSeconds;
                return true;
            }
            seconds = 0;
            return false;
        }

        #endregion

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
                    await process.WaitForExitAsync();
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
                using (var tempHttpClient = new HttpClient())
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
            _isManagingYtDlp = true;
            var ytdlpManageCts = new CancellationTokenSource();
            UpdateUiElementStates($"Status: Managing {YtDlpFileName}...");

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
            else if (fileExists && localVersion != null)
            {
                if (StatusTextBlock != null) StatusTextBlock.Text = $"Status: {YtDlpFileName} (version {localVersion}) found.";
                _isYtDlpReady = true;
            }


            if (needsDownload)
            {
                if (!fileExists && !updateAvailable)
                {
                    if (StatusTextBlock != null) StatusTextBlock.Text = $"Status: {YtDlpFileName} not found. Attempting to download...";
                    if (FileNameTextBlock != null) FileNameTextBlock.Text = $"Downloading: {YtDlpFileName}";
                }

                bool downloaded = false;
                try
                {
                    downloaded = await TryDownloadYtDlpInternalAsync(ytdlpManageCts.Token);
                }
                catch (OperationCanceledException)
                {
                    _isYtDlpReady = false;
                }

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
            else if (!_isYtDlpReady && fileExists && localVersion != null)
            {
                _isYtDlpReady = true;
            }
            else if (!_isYtDlpReady && fileExists && localVersion == null)
            {
                if (StatusTextBlock != null) StatusTextBlock.Text = $"Status: Local {YtDlpFileName} exists but may be non-functional. YouTube features might be limited.";
            }

            ytdlpManageCts.Dispose();
            _isManagingYtDlp = false;
            UpdateUiElementStates();
        }

        private async Task<bool> TryDownloadYtDlpInternalAsync(CancellationToken cancellationToken)
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
                    downloadClient.Timeout = TimeSpan.FromMinutes(10);
                    if (StatusTextBlock != null) StatusTextBlock.Text = $"Status: Downloading {YtDlpFileName} from {YtDlpDownloadUrl}...";

                    var response = await downloadClient.GetAsync(YtDlpDownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                    response.EnsureSuccessStatusCode();
                    long? totalDownloadSize = response.Content.Headers.ContentLength;
                    long totalBytesRead = 0;
                    int lastPercentage = -1;

                    if (DownloadProgressBar != null)
                    {
                        DownloadProgressBar.IsIndeterminate = !(totalDownloadSize.HasValue && totalDownloadSize.Value > 0);
                        if (!DownloadProgressBar.IsIndeterminate) DownloadProgressBar.Maximum = 100;
                    }

                    using (var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken))
                    using (var fileStream = new FileStream(_ytDlpExecutablePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
                    {
                        byte[] buffer = new byte[81920];
                        int bytesRead;
                        while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            await fileStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);
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
            catch (OperationCanceledException)
            {
                Debug.WriteLine("yt-dlp.exe download was canceled.");
                if (StatusTextBlock != null) StatusTextBlock.Text = $"Status: Download of {YtDlpFileName} canceled.";
                if (File.Exists(_ytDlpExecutablePath)) { try { File.Delete(_ytDlpExecutablePath); } catch { /* best effort */ } }
                return false;
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
            if (TrimmingSection != null) TrimmingSection.Visibility = Visibility.Collapsed;
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
                    string rawVideoTitle = videoInfo["title"]?.ToString() ?? "Unknown Video";
                    FileNameTextBlock.Text = Utilities.SanitizeFileName(rawVideoTitle);
                    ytDlpReportedFormats = videoInfo["formats"] as JArray;

                    // --- Trimming Logic Initialization ---
                    double? duration = videoInfo["duration"]?.ToObject<double?>();
                    if (duration.HasValue && duration > 0)
                    {
                        VideoDurationInSeconds = duration.Value;
                        TrimStartTimeInSeconds = 0;
                        TrimEndTimeInSeconds = duration.Value;
                        // Explicitly set the start time text, as the property setter won't fire if the value is already 0.
                        TrimStartTimeText = SecondsToTimeString(0);
                        IsTrimmingEnabled = false; // Default to off
                        if (TrimmingSection != null) TrimmingSection.Visibility = Visibility.Visible;
                        UpdateCustomSliderVisuals(); // Set initial thumb positions
                    }
                    else
                    {
                        if (TrimmingSection != null) TrimmingSection.Visibility = Visibility.Collapsed;
                    }
                    // --- End Trimming Logic ---
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
                    await Dispatcher.InvokeAsync(() => {
                        if (YouTubeQualityComboBox.Items.Count > 0) YouTubeQualityComboBox.SelectedIndex = 0;
                        UpdateUiElementStates();
                    }, DispatcherPriority.ContextIdle);
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

        private async Task DownloadWithYtDlpAsync(string itemUrl, string formatSelection,
                                                 string tempDownloadFolderPath, CancellationToken cancellationToken,
                                                 bool extractAudio = false, string audioFormat = "best")
        {
            if (StatusTextBlock == null || FileNameTextBlock == null || DownloadProgressBar == null) return;
            if (!_isYtDlpReady) { StatusTextBlock.Text = $"Status: {YtDlpFileName} is not available."; return; }

            _ytDlpCurrentComponentTotalBytes = -1;

            string baseFileNameTemplate;
            if (extractAudio && (IsSpotifyLink(itemUrl) || IsSoundCloudLink(itemUrl)))
            {
                baseFileNameTemplate = "%(artist)s - %(title)s.%(ext)s";
            }
            else if (extractAudio || (YouTubeQualityComboBox.SelectedItem as YouTubeQualityItem)?.IsAudioOnly == true)
            {
                baseFileNameTemplate = "%(title)s (Audio).%(ext)s";
            }
            else
            {
                baseFileNameTemplate = "%(title)s [%(id)s].%(ext)s";
            }
            if (IsYouTubeLink(itemUrl) && !baseFileNameTemplate.Contains("%(id)s"))
            {
                var extPart = Path.GetExtension(baseFileNameTemplate);
                var namePart = Path.GetFileNameWithoutExtension(baseFileNameTemplate);
                baseFileNameTemplate = $"{namePart} [%(id)s]{extPart}";
            }

            string outputTemplateInTemp = Path.Combine(tempDownloadFolderPath, baseFileNameTemplate);

            if (StatusTextBlock != null) StatusTextBlock.Text = $"Status: Preparing download...";

            DownloadProgressBar.Value = 0; DownloadProgressBar.Maximum = 100; DownloadProgressBar.IsIndeterminate = true;
            _currentDownloadingComponent = null;
            _currentYtDlpProcess = null;
            string finalReportedFilePathInTemp = null;

            try
            {
                string arguments;
                string baseArguments = $"--no-continue --progress --newline --no-warnings --ignore-config \"{itemUrl}\"";

                string formatArgument;
                if (extractAudio && (IsSpotifyLink(itemUrl) || IsSoundCloudLink(itemUrl)))
                {
                    formatArgument = $"--extract-audio --audio-format {audioFormat} --audio-quality 0";
                }
                else if (extractAudio)
                {
                    formatArgument = $"--extract-audio --audio-format {audioFormat} --audio-quality 0 -f \"{(string.IsNullOrWhiteSpace(formatSelection) ? "bestaudio/best" : formatSelection)}\"";
                }
                else
                {
                    formatArgument = $"-f \"{formatSelection}\"";
                }

                string trimArgument = "";
                if (IsTrimmingEnabled && TrimmingSection.Visibility == Visibility.Visible)
                {
                    trimArgument = $"--download-sections \"*{TrimStartTimeText}-{TrimEndTimeText}\"";
                }

                arguments = $"-o \"{outputTemplateInTemp}\" {formatArgument} {trimArgument} {baseArguments}";

                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = _ytDlpExecutablePath,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = System.Text.Encoding.UTF8,
                    StandardErrorEncoding = System.Text.Encoding.UTF8
                };

                string actualFileNameFromYtDlpOutput = extractAudio ? "extracted_audio" : "downloaded_video";
                bool progressStartedForAnyComponent = false;
                double lastReportedPercentageForComponent = 0;

                using (Process process = new Process())
                {
                    _currentYtDlpProcess = process;
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
                    DownloadProgressBar.IsIndeterminate = false;

                    try
                    {
                        await process.WaitForExitAsync(cancellationToken);
                    }
                    catch (TaskCanceledException)
                    {
                        Debug.WriteLine("WaitForExitAsync was canceled.");
                        if (!_currentYtDlpProcess.HasExited)
                        {
                            try { _currentYtDlpProcess.Kill(true); } catch { /* ignore */ }
                        }
                        throw new OperationCanceledException("yt-dlp download canceled.", cancellationToken);
                    }


                    DownloadProgressBar.IsIndeterminate = false;
                    _currentYtDlpProcess = null;

                    if (cancellationToken.IsCancellationRequested)
                    {
                        StatusTextBlock.Text = "Status: YouTube download canceled during completion processing.";
                        FileNameTextBlock.Text = "Download Canceled";
                        throw new OperationCanceledException("yt-dlp download canceled.", cancellationToken);
                    }

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
                                else
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
                            if (!Directory.Exists(SelectedDirectory))
                            {
                                StatusTextBlock.Text = $"Status: Error - Destination directory '{SelectedDirectory}' not found.";
                                FileNameTextBlock.Text = "Move Error";
                                goto EndYtDlpLogic;
                            }

                            string originalFileName = Path.GetFileName(fileToMove);
                            string cleanFileNameOnly = Path.GetFileNameWithoutExtension(originalFileName);
                            cleanFileNameOnly = Regex.Replace(cleanFileNameOnly, @"\s*\[[^\]]+\]\s*$", "").Trim(); // removes [id]
                            cleanFileNameOnly = Regex.Replace(cleanFileNameOnly, @"\s*\(Audio\)\s*$", "").Trim(); // removes (Audio)
                            string extension = Path.GetExtension(originalFileName);
                            string desiredFileName = Utilities.SanitizeFileName(cleanFileNameOnly + extension);

                            string targetPath;
                            try
                            {
                                targetPath = Utilities.GetUniqueFilePath(SelectedDirectory, desiredFileName);
                            }
                            catch (IOException ex)
                            {
                                StatusTextBlock.Text = "Status: Too many existing files with similar names. Could not move.";
                                FileNameTextBlock.Text = "Move Error";
                                Debug.WriteLine($"GetUniqueFilePath failed: {ex.Message}");
                                goto EndYtDlpLogic;
                            }

                            try
                            {
                                File.Move(fileToMove, targetPath);
                                Debug.WriteLine($"Moved yt-dlp file: {fileToMove} TO {targetPath}");

                                if (FileNameTextBlock != null)
                                {
                                    FileNameTextBlock.Text = Path.GetFileNameWithoutExtension(targetPath);
                                }
                                StatusTextBlock.Text = $"Status: Download complete! Saved as '{Path.GetFileName(targetPath)}'";
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
            catch (OperationCanceledException) { throw; }
            catch (Win32Exception ex) { StatusTextBlock.Text = $"Status: {YtDlpFileName} execution error. {ex.Message.Split('\n')[0]}"; _isYtDlpReady = false; DownloadProgressBar.Value = 0; DownloadProgressBar.IsIndeterminate = false; }
            catch (Exception ex) { StatusTextBlock.Text = $"Status: Error during YouTube download: {ex.Message.Split('\n')[0]}"; FileNameTextBlock.Text = "YouTube Download Error"; DownloadProgressBar.Value = 0; DownloadProgressBar.IsIndeterminate = false; }
            finally
            {
                _currentYtDlpProcess = null;
            }
        }


        private static readonly Regex YtDlpProgressRegex = new Regex(
           @"\[download\]\s+(?<percent>[\d\.]+?)%\s+of\s+(?:~?\s*)?(?<total_size_str>[\d\.]+[KMGTPEZiY]?i?B|unknown)(?:\s+at\s+(?<speed>[\d\.]+[KMGTPEZiY]?i?B/s|\S+))?(?:\s+ETA\s+(?<eta>[\d:SMPH]+|\S+))?(?:\s+in\s+[\d:SMPH]+)?",
           RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex YtDlpDestinationRegex = new Regex(
            @"\[download\]\s+Destination:\s*(?<path>.+)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex YtDlpAlreadyDownloadedRegex = new Regex(
            @"\[download\]\s+(?:""?)(?<path>.+?)(?:""?)?\s+has already been downloaded",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex YtDlpProcessingRegex = new Regex(
            @"\[(?<type>Merger|ExtractAudio|VideoRemuxer|Fixup[^\]]*)\]\s+(?:Merging formats into|Destination|Extracting audio to|Remuxing video to)\s*(?:""?)(?<path>.+?)(?:""?)?$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private void ParseYtDlpProgress(string outputLine,
                                 ref string baseFileNameFromYtDlpOutput,
                                 ref bool progressStartedForAnyComponent,
                                 ref double lastKnownPercentageForComponent,
                                 ref string finalReportedFilePathInTemp)
        {
            if (StatusTextBlock == null || FileNameTextBlock == null || DownloadProgressBar == null) return;
            outputLine = outputLine.Trim();
            string currentOperationStatus = "";

            Match destMatch = YtDlpDestinationRegex.Match(outputLine);
            if (destMatch.Success)
            {
                string newComponentPath = destMatch.Groups["path"].Value.Trim('"', ' ');
                _currentDownloadingComponent = newComponentPath;
                finalReportedFilePathInTemp = newComponentPath;

                // Do NOT update the FileNameTextBlock here. Let it keep the clean title.
                currentOperationStatus = $"Status: Starting download...";

                _ytDlpCurrentComponentTotalBytes = -1;
                DownloadProgressBar.IsIndeterminate = true;
                DownloadProgressBar.Value = 0;
                DownloadProgressBar.Maximum = 100;
                lastKnownPercentageForComponent = 0;
                progressStartedForAnyComponent = true;
                Debug.WriteLine($"YT-DLP DEST: {_currentDownloadingComponent}");
                if (StatusTextBlock != null) StatusTextBlock.Text = currentOperationStatus;
                return;
            }

            Match alreadyDownloadedMatch = YtDlpAlreadyDownloadedRegex.Match(outputLine);
            if (alreadyDownloadedMatch.Success)
            {
                _currentDownloadingComponent = alreadyDownloadedMatch.Groups["path"].Value.Trim('"', ' ');
                finalReportedFilePathInTemp = _currentDownloadingComponent;

                // Do NOT update the FileNameTextBlock here.
                currentOperationStatus = $"Status: File already downloaded.";

                DownloadProgressBar.IsIndeterminate = false;
                DownloadProgressBar.Maximum = 100; DownloadProgressBar.Value = 100;
                lastKnownPercentageForComponent = 100; _ytDlpCurrentComponentTotalBytes = 0;
                progressStartedForAnyComponent = true;
                Debug.WriteLine($"YT-DLP ALREADY_DOWNLOADED: {finalReportedFilePathInTemp}");
                if (StatusTextBlock != null) StatusTextBlock.Text = currentOperationStatus;
                return;
            }

            Match processingMatch = YtDlpProcessingRegex.Match(outputLine);
            if (processingMatch.Success)
            {
                string processingType = processingMatch.Groups["type"].Value;
                string processingPath = processingMatch.Groups["path"].Value.Trim('"', ' ');
                _currentDownloadingComponent = processingPath;
                finalReportedFilePathInTemp = processingPath;

                // Do NOT update the FileNameTextBlock here.
                currentOperationStatus = $"Status: {processingType}...";

                DownloadProgressBar.IsIndeterminate = true; DownloadProgressBar.Value = 0;
                _ytDlpCurrentComponentTotalBytes = -2;
                lastKnownPercentageForComponent = 0;
                progressStartedForAnyComponent = true;
                Debug.WriteLine($"YT-DLP PROCESSING ({processingType}): {finalReportedFilePathInTemp}");
                if (StatusTextBlock != null) StatusTextBlock.Text = currentOperationStatus;
                return;
            }

            Match progressMatch = YtDlpProgressRegex.Match(outputLine);
            if (progressMatch.Success)
            {
                if (_ytDlpCurrentComponentTotalBytes == -2) return;

                progressStartedForAnyComponent = true;
                double currentPercent = 0.0;
                string totalSizeString = progressMatch.Groups["total_size_str"].Value;

                if (!double.TryParse(progressMatch.Groups["percent"].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out currentPercent))
                {
                    Debug.WriteLine($"Failed to parse percent: {progressMatch.Groups["percent"].Value}");
                    return;
                }

                long currentLineTotalBytes = Utilities.ParseYtDlpSizeStringToBytes(totalSizeString);
                if (_ytDlpCurrentComponentTotalBytes == -1 ||
                    (_ytDlpCurrentComponentTotalBytes == 0 && currentLineTotalBytes > 0) ||
                    (currentLineTotalBytes > 0 && _ytDlpCurrentComponentTotalBytes != currentLineTotalBytes))
                {
                    _ytDlpCurrentComponentTotalBytes = currentLineTotalBytes;
                    Debug.WriteLine($"YT-DLP Updated Total Bytes: {_ytDlpCurrentComponentTotalBytes} from '{totalSizeString}'");
                }

                string speed = progressMatch.Groups["speed"].Value;
                string eta = progressMatch.Groups["eta"].Value;


                if (DownloadProgressBar.IsIndeterminate && _ytDlpCurrentComponentTotalBytes >= 0)
                {
                    DownloadProgressBar.IsIndeterminate = false;
                }

                if (_ytDlpCurrentComponentTotalBytes > 0)
                {
                    DownloadProgressBar.Maximum = _ytDlpCurrentComponentTotalBytes;
                    long currentDownloadedBytes = (long)((currentPercent / 100.0) * _ytDlpCurrentComponentTotalBytes);
                    DownloadProgressBar.Value = Math.Min(currentDownloadedBytes, _ytDlpCurrentComponentTotalBytes);
                    currentOperationStatus = $"Downloading: {currentPercent:F1}% of {Utilities.FormatBytesOutput(_ytDlpCurrentComponentTotalBytes)}";
                }
                else
                {
                    DownloadProgressBar.Maximum = 100;
                    DownloadProgressBar.Value = Math.Min(currentPercent, 100.0);
                    currentOperationStatus = $"Downloading: {currentPercent:F1}% of {totalSizeString}";
                }

                if (!string.IsNullOrWhiteSpace(speed)) currentOperationStatus += $" | Speed: {speed}";
                if (!string.IsNullOrWhiteSpace(eta)) currentOperationStatus += $" | ETA: {eta}";

                lastKnownPercentageForComponent = currentPercent;
                if (StatusTextBlock != null) StatusTextBlock.Text = currentOperationStatus;
            }
        }
    }
}