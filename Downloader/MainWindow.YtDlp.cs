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
    public partial class MainWindow // Must be partial
    {
        private const string YtDlpFileName = "yt-dlp.exe";
        private string _ytDlpExecutablePath; // Initialized in MainWindow constructor
        private const string YtDlpDownloadUrl = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe";
        private bool _isYtDlpReady = false;
        private string _currentDownloadingComponent = null;
        private long _ytDlpCurrentComponentTotalBytes = -1;

        private async Task CheckAndEnsureYtDlpExistsAsync()
        {
            _isYtDlpReady = false;
            if (FileNameTextBlock != null) FileNameTextBlock.Text = "";
            await Task.Delay(10);

            if (File.Exists(_ytDlpExecutablePath))
            {
                if (StatusTextBlock != null) StatusTextBlock.Text = $"Status: {YtDlpFileName} found.";
                _isYtDlpReady = true;
            }
            else
            {
                if (StatusTextBlock != null) StatusTextBlock.Text = $"Status: {YtDlpFileName} not found. Attempting to download...";
                if (FileNameTextBlock != null) FileNameTextBlock.Text = $"Downloading: {YtDlpFileName}";
                bool downloaded = await TryDownloadYtDlpInternalAsync();
                if (downloaded)
                {
                    if (StatusTextBlock != null) StatusTextBlock.Text = $"Status: {YtDlpFileName} downloaded successfully. Ready.";
                    if (FileNameTextBlock != null) FileNameTextBlock.Text = "";
                    _isYtDlpReady = true;
                }
                else
                {
                    if (StatusTextBlock != null) StatusTextBlock.Text = $"Status: Failed to download {YtDlpFileName}. YouTube downloads unavailable. Please place it in the app folder.";
                    if (FileNameTextBlock != null) FileNameTextBlock.Text = $"{YtDlpFileName} download failed.";
                    _isYtDlpReady = false;
                }
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
                    downloadClient.Timeout = TimeSpan.FromMinutes(5);
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
                    using (var fileStream = new FileStream(_ytDlpExecutablePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                    {
                        byte[] buffer = new byte[81920];
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
            var availableQualities = new List<YouTubeQualityItem>();
            JArray ytDlpReportedFormats = null;
            string videoTitle = "Unknown Video";

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
                    videoTitle = Utilities.SanitizeFileName(videoInfo["title"]?.ToString() ?? "Unknown Title");
                    FileNameTextBlock.Text = $"Video: {videoTitle}";
                    ytDlpReportedFormats = videoInfo["formats"] as JArray;
                }

                if (ytDlpReportedFormats == null || !ytDlpReportedFormats.HasValues)
                {
                    StatusTextBlock.Text = "Status: No formats reported by yt-dlp for this video."; FileNameTextBlock.Text = "No YouTube formats found.";
                    if (QualitySection != null) QualitySection.Visibility = Visibility.Collapsed; return;
                }

                var distinctVideoHeights = new SortedSet<int>();
                var audioFormatItems = new List<YouTubeQualityItem>();
                var preMergedFormatItems = new List<YouTubeQualityItem>();

                foreach (JToken format in ytDlpReportedFormats)
                {
                    string vcodec = format["vcodec"]?.ToString() ?? "none";
                    string acodec = format["acodec"]?.ToString() ?? "none";
                    int height = format["height"]?.ToObject<int?>() ?? 0;
                    if (vcodec != "none" && height > 0) distinctVideoHeights.Add(height);

                    if (acodec != "none" && vcodec == "none") // Audio only
                    {
                        string formatId = format["format_id"]?.ToString();
                        string ext = format["ext"]?.ToString() ?? "N/A";
                        double abr = format["abr"]?.ToObject<double?>() ?? 0;
                        string note = format["format_note"]?.ToString() ?? acodec;
                        long? filesize = format["filesize"]?.ToObject<long?>() ?? format["filesize_approx"]?.ToObject<long?>();
                        string sizeStr = filesize.HasValue ? $" ({Utilities.FormatBytesOutput(filesize.Value)})" : "";
                        string label = $"Audio Only: {note.Replace("audio only", "").Trim()} ({ext}, ~{abr:F0}k) [{formatId}]{sizeStr}";
                        if (!string.IsNullOrEmpty(formatId)) audioFormatItems.Add(new YouTubeQualityItem { Label = label, FormatCode = formatId, IsAudioOnly = true, SortPriority = (int)abr + 20000 });
                    }
                    else if (vcodec != "none" && acodec != "none") // Pre-merged
                    {
                        string formatId = format["format_id"]?.ToString();
                        string ext = format["ext"]?.ToString() ?? "N/A";
                        string resolutionStr = format["resolution"]?.ToString();
                        string note = format["format_note"]?.ToString() ?? $"{height}p";
                        int fps = format["fps"]?.ToObject<int?>() ?? 0;
                        long? filesize = format["filesize"]?.ToObject<long?>() ?? format["filesize_approx"]?.ToObject<long?>();
                        string sizeStr = filesize.HasValue ? $" ({Utilities.FormatBytesOutput(filesize.Value)})" : "";
                        string fpsStr = fps > 0 ? $"@{fps}fps" : "";
                        string displayRes = note.Contains("p") && !note.Contains("DASH") ? note : (resolutionStr ?? (height > 0 ? $"{height}p" : "Video"));
                        string label = $"Pre-merged: {displayRes} {fpsStr} ({ext}) [{formatId}]{sizeStr}";
                        if (!string.IsNullOrEmpty(formatId)) preMergedFormatItems.Add(new YouTubeQualityItem { Label = label, FormatCode = formatId, IsAudioOnly = false, SortPriority = height + (fps > 30 ? 500 : 0) + 10000 });
                    }
                }

                availableQualities.Add(new YouTubeQualityItem { Label = "Best Available (Video+Audio Merged, MP4 H.264 Preferred)", FormatCode = "bestvideo[ext=mp4][vcodec^=avc]+bestaudio[ext=m4a]/bestvideo[ext=mp4]+bestaudio[ext=m4a]/bestvideo[vcodec!*=av01][vcodec!*=vp09]+bestaudio/bestvideo+bestaudio/best", IsAudioOnly = false, SortPriority = 100001 });
                availableQualities.Add(new YouTubeQualityItem { Label = "Audio Only (Best Available)", FormatCode = "bestaudio/best", IsAudioOnly = true, SortPriority = 90000 });
                var targetResolutionTiers = new List<(int height, string labelName)> { (4320, "4320p (8K)"), (3840, "3840p (UHD)"), (2160, "2160p (4K)"), (1440, "1440p (2K)"), (1080, "1080p (FHD)"), (720, "720p (HD)"), (480, "480p"), (360, "360p"), (240, "240p"), (144, "144p") };
                foreach (var tier in targetResolutionTiers)
                {
                    if (distinctVideoHeights.Any(h => h >= tier.height) || preMergedFormatItems.Any(pm => (pm.FormatCode.Contains($"{tier.height}") || (pm.Label.Contains($"{tier.height}p")))))
                    {
                        string formatCode = $"bestvideo[height<={tier.height}][ext=mp4]+bestaudio[ext=m4a]/bestvideo[height<={tier.height}]+bestaudio/best[height<={tier.height}]";
                        string label = $"{tier.labelName} (Merged, MP4 Preferred)";
                        if (!availableQualities.Any(q => q.Label.StartsWith(tier.labelName))) availableQualities.Add(new YouTubeQualityItem { Label = label, FormatCode = formatCode, IsAudioOnly = false, SortPriority = tier.height * 10 });
                    }
                }
                availableQualities.AddRange(audioFormatItems.GroupBy(a => a.FormatCode).Select(g => g.First()).OrderByDescending(a => a.SortPriority));
                availableQualities.AddRange(preMergedFormatItems.GroupBy(p => p.FormatCode).Select(g => g.First()).OrderByDescending(p => p.SortPriority));
                availableQualities = availableQualities.GroupBy(q => q.Label).Select(g => g.OrderByDescending(i => i.SortPriority).First()).OrderByDescending(q => q.SortPriority).ToList();

                if (availableQualities.Any())
                {
                    YouTubeQualityComboBox.ItemsSource = availableQualities;
                    if (QualitySection != null) QualitySection.Visibility = Visibility.Visible;
                    await Dispatcher.InvokeAsync(() => { if (YouTubeQualityComboBox.Items.Count > 0) YouTubeQualityComboBox.SelectedIndex = 0; UpdateDownloadButtonState(); }, DispatcherPriority.ContextIdle);
                    StatusTextBlock.Text = "Status: YouTube qualities listed. Select quality to download.";
                }
                else
                {
                    StatusTextBlock.Text = "Status: No downloadable formats could be determined."; FileNameTextBlock.Text = "No YouTube formats available.";
                    if (QualitySection != null) QualitySection.Visibility = Visibility.Collapsed;
                }
            }
            catch (Win32Exception ex) { StatusTextBlock.Text = $"Status: {YtDlpFileName} execution error. {ex.Message.Split('\n')[0]}"; _isYtDlpReady = false; FileNameTextBlock.Text = "yt-dlp Error"; if (QualitySection != null) QualitySection.Visibility = Visibility.Collapsed; }
            catch (Newtonsoft.Json.JsonReaderException jsonEx) { StatusTextBlock.Text = $"Status: Error parsing yt-dlp output: {jsonEx.Message.Split('\n')[0]}."; FileNameTextBlock.Text = "YouTube Info Parse Error"; if (QualitySection != null) QualitySection.Visibility = Visibility.Collapsed; }
            catch (Exception ex) { StatusTextBlock.Text = $"Status: Error processing YouTube info: {ex.Message.Split('\n')[0]}."; FileNameTextBlock.Text = "YouTube Info Error"; if (QualitySection != null) QualitySection.Visibility = Visibility.Collapsed; }
        }

        private async Task DownloadYouTubeVideoWithYtDlp(string videoUrl, string formatCode)
        {
            if (StatusTextBlock == null || FileNameTextBlock == null || DownloadProgressBar == null || YouTubeQualityComboBox == null) return;
            if (!_isYtDlpReady) { StatusTextBlock.Text = $"Status: {YtDlpFileName} is not available. Cannot download YouTube video."; return; }

            _ytDlpCurrentComponentTotalBytes = -1;
            var selectedQualityItem = YouTubeQualityComboBox.SelectedItem as YouTubeQualityItem;
            string outputTemplate = Path.Combine(SelectedDirectory, selectedQualityItem != null && selectedQualityItem.IsAudioOnly ? "%(title)s [%(id)s] (Audio).%(ext)s" : "%(title)s [%(id)s].%(ext)s");
            StatusTextBlock.Text = $"Status: Downloading YouTube (format: {formatCode})...";
            FileNameTextBlock.Text = "Preparing YouTube download...";
            DownloadProgressBar.Value = 0; DownloadProgressBar.Maximum = 100; DownloadProgressBar.IsIndeterminate = true;
            _currentDownloadingComponent = null;

            try
            {
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = _ytDlpExecutablePath,
                    Arguments = $"-f \"{formatCode}\" -o \"{outputTemplate}\" --no-continue --progress --newline --no-warnings --ignore-config \"{videoUrl}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = System.Text.Encoding.UTF8,
                    StandardErrorEncoding = System.Text.Encoding.UTF8
                };
                string actualFileNameFromYtDlp = "downloaded_video"; bool progressStartedForAnyComponent = false; double lastReportedPercentageForComponent = 0;
                using (Process process = new Process())
                {
                    process.StartInfo = psi; process.EnableRaisingEvents = true;
                    process.OutputDataReceived += (s, e) => { if (e.Data != null) Dispatcher.Invoke(() => ParseYtDlpProgress(e.Data, ref actualFileNameFromYtDlp, ref progressStartedForAnyComponent, ref lastReportedPercentageForComponent)); };
                    process.ErrorDataReceived += (s, e) => { if (e.Data != null && !e.Data.ToLower().Contains("warning:") && !e.Data.ToLower().Contains("deprecated")) Dispatcher.Invoke(() => StatusTextBlock.Text = $"yt-dlp Info/Error: {e.Data.Split('\n')[0]}"); };
                    process.Start(); process.BeginOutputReadLine(); process.BeginErrorReadLine();
                    await process.WaitForExitAsync();
                    DownloadProgressBar.IsIndeterminate = false;
                    if (process.ExitCode == 0)
                    {
                        if (_ytDlpCurrentComponentTotalBytes > 0) { if (DownloadProgressBar.Maximum == _ytDlpCurrentComponentTotalBytes || DownloadProgressBar.Maximum == 100) DownloadProgressBar.Value = DownloadProgressBar.Maximum; } else { DownloadProgressBar.Value = DownloadProgressBar.Maximum; }
                        StatusTextBlock.Text = $"Status: YouTube download '{Path.GetFileName(actualFileNameFromYtDlp)}' complete!";
                        FileNameTextBlock.Text = $"Completed: {Path.GetFileName(actualFileNameFromYtDlp)}";
                    }
                    else
                    {
                        DownloadProgressBar.Value = 0;
                        if (!StatusTextBlock.Text.ToLower().Contains("error") && !StatusTextBlock.Text.ToLower().Contains("yt-dlp error")) { StatusTextBlock.Text = $"Status: yt-dlp download failed (code {process.ExitCode})."; FileNameTextBlock.Text = "YouTube Download Failed"; }
                    }
                }
            }
            catch (Win32Exception ex) { StatusTextBlock.Text = $"Status: {YtDlpFileName} execution error. {ex.Message.Split('\n')[0]}"; _isYtDlpReady = false; DownloadProgressBar.Value = 0; DownloadProgressBar.IsIndeterminate = false; }
            catch (Exception ex) { StatusTextBlock.Text = $"Status: Error during YouTube download: {ex.Message.Split('\n')[0]}"; FileNameTextBlock.Text = "YouTube Download Error"; DownloadProgressBar.Value = 0; DownloadProgressBar.IsIndeterminate = false; }
        }

        private void ParseYtDlpProgress(string outputLine, ref string finalFileNameFromYtDlp, ref bool progressStartedForAnyComponent, ref double lastKnownPercentageForComponent)
        {
            if (StatusTextBlock == null || FileNameTextBlock == null || DownloadProgressBar == null || YouTubeQualityComboBox == null) return;
            outputLine = outputLine.Trim();
            var selectedQualityItem = YouTubeQualityComboBox.SelectedItem as YouTubeQualityItem;

            if (outputLine.StartsWith("[download] Destination:"))
            {
                string newComponentFileName = Utilities.SanitizeFileName(outputLine.Substring("[download] Destination:".Length).Trim());
                if (_currentDownloadingComponent != newComponentFileName || !progressStartedForAnyComponent || DownloadProgressBar.Value >= (DownloadProgressBar.Maximum * 0.999) || lastKnownPercentageForComponent >= 99.9)
                {
                    _currentDownloadingComponent = newComponentFileName;
                    FileNameTextBlock.Text = $"Downloading: {Path.GetFileName(_currentDownloadingComponent)}";
                    _ytDlpCurrentComponentTotalBytes = -1; DownloadProgressBar.IsIndeterminate = true; DownloadProgressBar.Value = 0; DownloadProgressBar.Maximum = 100; lastKnownPercentageForComponent = 0;
                    bool isVideoFile = newComponentFileName.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) || newComponentFileName.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase) || newComponentFileName.EndsWith(".webm", StringComparison.OrdinalIgnoreCase);
                    bool isAudioFile = newComponentFileName.EndsWith(".m4a", StringComparison.OrdinalIgnoreCase) || newComponentFileName.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) || newComponentFileName.EndsWith(".opus", StringComparison.OrdinalIgnoreCase) || newComponentFileName.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase);
                    if (selectedQualityItem != null) { if (!selectedQualityItem.IsAudioOnly && isVideoFile) finalFileNameFromYtDlp = _currentDownloadingComponent; else if (selectedQualityItem.IsAudioOnly && isAudioFile) finalFileNameFromYtDlp = _currentDownloadingComponent; else if (finalFileNameFromYtDlp == "downloaded_video" || !Path.HasExtension(finalFileNameFromYtDlp)) finalFileNameFromYtDlp = _currentDownloadingComponent; }
                    else { if (finalFileNameFromYtDlp == "downloaded_video" || !Path.HasExtension(finalFileNameFromYtDlp)) finalFileNameFromYtDlp = _currentDownloadingComponent; }
                }
                progressStartedForAnyComponent = true;
            }
            else if (outputLine.Contains("has already been downloaded"))
            {
                var matchName = Regex.Match(outputLine, @"\[download\]\s+(.*?)\s+has already been downloaded");
                if (matchName.Success) { _currentDownloadingComponent = Utilities.SanitizeFileName(matchName.Groups[1].Value.Trim()); finalFileNameFromYtDlp = _currentDownloadingComponent; FileNameTextBlock.Text = $"File: {Path.GetFileName(finalFileNameFromYtDlp)} (already exists)"; }
                StatusTextBlock.Text = "Status: File already downloaded by yt-dlp."; DownloadProgressBar.IsIndeterminate = false; DownloadProgressBar.Maximum = 100; DownloadProgressBar.Value = 100; lastKnownPercentageForComponent = 100; _ytDlpCurrentComponentTotalBytes = 0; progressStartedForAnyComponent = true;
            }
            else if (outputLine.StartsWith("[Merger]") || outputLine.StartsWith("[ExtractAudio]") || outputLine.StartsWith("[FixupMpegts]") || outputLine.StartsWith("[FixupMfr]") || outputLine.StartsWith("[FixupStretched]"))
            {
                var matchDest = Regex.Match(outputLine, @"Destination:\s*""?([^""\n\r]+)""?|into\s*""?([^""\n\r]+)""?|in\s*""?([^""\n\r]+)""?");
                string tempName = _currentDownloadingComponent ?? finalFileNameFromYtDlp;
                if (matchDest.Groups[1].Success && !string.IsNullOrWhiteSpace(matchDest.Groups[1].Value)) tempName = Utilities.SanitizeFileName(matchDest.Groups[1].Value);
                else if (matchDest.Groups[2].Success && !string.IsNullOrWhiteSpace(matchDest.Groups[2].Value)) tempName = Utilities.SanitizeFileName(matchDest.Groups[2].Value);
                else if (matchDest.Groups[3].Success && !string.IsNullOrWhiteSpace(matchDest.Groups[3].Value)) tempName = Utilities.SanitizeFileName(matchDest.Groups[3].Value);
                if (Path.HasExtension(tempName)) finalFileNameFromYtDlp = tempName;
                StatusTextBlock.Text = $"Status: yt-dlp - {outputLine.Split('\n')[0]}"; FileNameTextBlock.Text = $"Processing: {Path.GetFileName(finalFileNameFromYtDlp)}";
                if (!DownloadProgressBar.IsIndeterminate) DownloadProgressBar.IsIndeterminate = true; _ytDlpCurrentComponentTotalBytes = 0; progressStartedForAnyComponent = true;
            }
            else
            {
                Match progressMatch = Regex.Match(outputLine, @"\[download\]\s+(?<percent>[\d\.]+?)%\s+of\s+(?:~?\s*)?(?<total_size_str>[\d\.]+[KMGT]?i?B|unknown)(?:\s+at\s+(?<speed>[\d\.]+[KMGT]?i?B/s))?(?:\s+ETA\s+(?<eta>[\d:]+))?|\[download\]\s+100%\s+of\s+(?<total_size_full_str>[\d\.]+[KMGT]?i?B)\s+in\s+[\d:]+");
                if (progressMatch.Success)
                {
                    progressStartedForAnyComponent = true; double currentPercent = 0.0; string totalSizeStringForDisplay;
                    if (progressMatch.Groups["percent"].Success) { if (!double.TryParse(progressMatch.Groups["percent"].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out currentPercent)) return; totalSizeStringForDisplay = progressMatch.Groups["total_size_str"].Value; }
                    else { currentPercent = 100.0; totalSizeStringForDisplay = progressMatch.Groups["total_size_full_str"].Value; }

                    if (_ytDlpCurrentComponentTotalBytes == -1)
                    {
                        long parsedTotalBytes = Utilities.ParseYtDlpSizeStringToBytes(totalSizeStringForDisplay);
                        _ytDlpCurrentComponentTotalBytes = parsedTotalBytes > 0 ? parsedTotalBytes : 0;
                        DownloadProgressBar.Maximum = _ytDlpCurrentComponentTotalBytes > 0 ? _ytDlpCurrentComponentTotalBytes : 100;
                        DownloadProgressBar.IsIndeterminate = _ytDlpCurrentComponentTotalBytes <= 0;
                    }
                    string speed = progressMatch.Groups["speed"].Value; string eta = progressMatch.Groups["eta"].Value;
                    string componentNameDisplay = string.IsNullOrWhiteSpace(_currentDownloadingComponent) ? Path.GetFileName(finalFileNameFromYtDlp) : Path.GetFileName(_currentDownloadingComponent);
                    if (_ytDlpCurrentComponentTotalBytes > 0)
                    {
                        if (DownloadProgressBar.IsIndeterminate) DownloadProgressBar.IsIndeterminate = false;
                        if (DownloadProgressBar.Maximum != _ytDlpCurrentComponentTotalBytes) DownloadProgressBar.Maximum = _ytDlpCurrentComponentTotalBytes;
                        long currentDownloadedBytes = (long)((currentPercent / 100.0) * _ytDlpCurrentComponentTotalBytes); DownloadProgressBar.Value = currentDownloadedBytes;
                        StatusTextBlock.Text = $"Downloading ({componentNameDisplay}): {currentPercent:F1}% ({Utilities.FormatBytesOutput(currentDownloadedBytes)} / {Utilities.FormatBytesOutput(_ytDlpCurrentComponentTotalBytes)}) | Speed: {speed} | ETA: {eta}";
                    }
                    else
                    {
                        if (DownloadProgressBar.IsIndeterminate) DownloadProgressBar.IsIndeterminate = false;
                        if (DownloadProgressBar.Maximum != 100) DownloadProgressBar.Maximum = 100; DownloadProgressBar.Value = currentPercent;
                        StatusTextBlock.Text = $"Downloading ({componentNameDisplay}): {currentPercent:F1}% of {totalSizeStringForDisplay} | Speed: {speed} | ETA: {eta}";
                    }
                    lastKnownPercentageForComponent = currentPercent;
                }
            }
        }
    }
}