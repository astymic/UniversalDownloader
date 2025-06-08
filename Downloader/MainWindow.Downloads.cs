using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;

namespace UniversalDownloader
{
    public partial class MainWindow
    {
        private bool IsYouTubeLink(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) { return false; }
            return Regex.IsMatch(url, @"(youtube\.com\/(watch\?v=|embed\/|shorts\/)|youtu\.be\/)", RegexOptions.IgnoreCase);
        }

        private bool IsGoogleDriveLink(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) { return false; }
            return Regex.IsMatch(url, @"drive\.google\.com/(file/d/|open\?id=|uc\?id=)", RegexOptions.IgnoreCase);
        }

        private bool IsSpotifyLink(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) { return false; }
            return Regex.IsMatch(url, @"open\.spotify\.com/(track|album|playlist)/", RegexOptions.IgnoreCase);
        }

        private bool IsSoundCloudLink(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) { return false; }
            return Regex.IsMatch(url, @"soundcloud\.com/", RegexOptions.IgnoreCase);
        }

        private bool IsKnownAudioPlatformLink(string url)
        {
            return IsSpotifyLink(url) || IsSoundCloudLink(url); // Add more here like Bandcamp, Deezer etc.
        }


        private async Task ProcessUrlChange(string url, bool isInitialLoad = false)
        {
            if (YouTubeQualityComboBox != null) YouTubeQualityComboBox.ItemsSource = null;
            if (QualitySection != null) QualitySection.Visibility = Visibility.Collapsed; // Default to hidden

            if (string.IsNullOrWhiteSpace(url) || url == "Paste URL here...")
            {
                if (FileNameTextBlock != null) FileNameTextBlock.Text = string.Empty;
                return;
            }

            if (IsYouTubeLink(url) || IsKnownAudioPlatformLink(url))
            {
                if (!_isYtDlpReady)
                {
                    if (StatusTextBlock != null) StatusTextBlock.Text = $"Status: {YtDlpFileName} dependency check...";
                    await CheckAndEnsureYtDlpExistsAsync();
                    if (!_isYtDlpReady) // If still not ready after check
                    {
                        if (StatusTextBlock != null) StatusTextBlock.Text = $"Status: {YtDlpFileName} not available. Platform features disabled.";
                        if (FileNameTextBlock != null) FileNameTextBlock.Text = "Required tool missing.";
                        return; // Can't proceed with these platforms
                    }
                }
            }


            if (IsYouTubeLink(url))
            {
                if (FileNameTextBlock != null) FileNameTextBlock.Text = "Processing YouTube URL...";
                if (StatusTextBlock != null) StatusTextBlock.Text = "Status: Fetching YouTube qualities...";
                await LoadYouTubeQualitiesWithYtDlp(url); // This shows QualitySection if successful
            }
            else if (IsKnownAudioPlatformLink(url))
            {
                string platformName = IsSpotifyLink(url) ? "Spotify" : IsSoundCloudLink(url) ? "SoundCloud" : "Audio Platform";
                if (FileNameTextBlock != null) FileNameTextBlock.Text = $"Processing {platformName} link...";
                if (StatusTextBlock != null) StatusTextBlock.Text = $"Status: Ready to download audio from {platformName}.";
                await TrySetAudioTitleFromYtDlp(url, platformName);
            }
            else if (IsGoogleDriveLink(url))
            {
                if (FileNameTextBlock != null) FileNameTextBlock.Text = "Google Drive link detected.";
                if (StatusTextBlock != null) StatusTextBlock.Text = "Status: Ready to download Google Drive link.";
            }
            else // Potentially direct link or other yt-dlp supported (non-audio-specific UI)
            {
                if (FileNameTextBlock != null) FileNameTextBlock.Text = "Fetching file info...";
                await TrySetFileNameFromUrlHeaders(url);
            }
        }

        private async Task TrySetAudioTitleFromYtDlp(string url, string platformName)
        {
            if (!_isYtDlpReady || FileNameTextBlock == null) return;

            FileNameTextBlock.Text = $"Fetching title from {platformName}...";
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = _ytDlpExecutablePath,
                    Arguments = $"--get-title --no-warnings \"{url}\"",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = System.Text.Encoding.UTF8
                };

                using (Process process = Process.Start(psi))
                {
                    string titleOutput = await process.StandardOutput.ReadToEndAsync();
                    await process.WaitForExitAsync();
                    if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(titleOutput))
                    {
                        FileNameTextBlock.Text = Utilities.SanitizeFileName(titleOutput.Trim());
                        if (StatusTextBlock != null) StatusTextBlock.Text = $"Status: Ready to download audio: {FileNameTextBlock.Text}";
                    }
                    else
                    {
                        FileNameTextBlock.Text = $"{platformName} item (title unavailable)";
                        if (StatusTextBlock != null) StatusTextBlock.Text = $"Status: Ready to download from {platformName}. Could not fetch title.";
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error fetching audio title with yt-dlp: {ex.Message}");
                FileNameTextBlock.Text = $"{platformName} item (error fetching title)";
                if (StatusTextBlock != null) StatusTextBlock.Text = $"Status: Ready to download from {platformName}. Error fetching title.";
            }
        }

        private async Task TrySetFileNameFromUrlHeaders(string url)
        {
            if (FileNameTextBlock == null || StatusTextBlock == null) { return; }
            FileNameTextBlock.Text = "Fetching file info...";
            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Get, url))
                using (var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();
                    string tempFileName = GetFileNameFromHeaders(response, url);
                    FileNameTextBlock.Text = Utilities.SanitizeFileName(Path.GetFileNameWithoutExtension(tempFileName));
                    StatusTextBlock.Text = "Status: File info retrieved. Ready to download.";
                }
            }
            catch (HttpRequestException httpEx)
            {
                FileNameTextBlock.Text = "Filename: (unable to determine - HTTP error)";
                StatusTextBlock.Text = $"Status: Could not get file info (HTTP: {httpEx.StatusCode}). URL might be invalid or inaccessible.";
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in TrySetFileNameFromUrlHeaders: {ex.Message}");
                FileNameTextBlock.Text = "Filename: (unable to determine before download)";
                StatusTextBlock.Text = "Status: Could not get file info. Proceed with download if URL is direct.";
            }
        }

        private string GetFileNameFromHeaders(HttpResponseMessage response, string url)
        {
            string fileName = null;
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

        private async Task DownloadGoogleDriveFile(string url, string tempDownloadFolderPath, CancellationToken cancellationToken)
        {
            if (StatusTextBlock == null || FileNameTextBlock == null || DownloadProgressBar == null) { return; }
            StatusTextBlock.Text = "Status: Preparing Google Drive download...";
            FileNameTextBlock.Text = "Preparing Google Drive...";
            string fileId = null;
            var matchFileD = Regex.Match(url, @"/file/d/([a-zA-Z0-9_-]+)");
            if (matchFileD.Success) { fileId = matchFileD.Groups[1].Value; }
            else
            {
                var matchOpenId = Regex.Match(url, @"[?&]id=([a-zA-Z0-9_-]+)");
                if (matchOpenId.Success) { fileId = matchOpenId.Groups[1].Value; }
            }
            if (string.IsNullOrEmpty(fileId))
            {
                StatusTextBlock.Text = "Status: Could not extract Google Drive File ID from URL.";
                FileNameTextBlock.Text = "Google Drive: Invalid URL";
                return;
            }
            string directDownloadUrl = $"https://drive.google.com/uc?export=download&confirm=t&id={fileId}";
            await DownloadDirectFile(directDownloadUrl, tempDownloadFolderPath, cancellationToken, true);
        }

        private async Task DownloadDirectFile(string url, string tempDownloadFolderPath, CancellationToken cancellationToken, bool isGoogleDriveInitialAttempt = false)
        {
            if (StatusTextBlock == null || FileNameTextBlock == null || DownloadProgressBar == null) { return; }

            if (!isGoogleDriveInitialAttempt)
            {
                StatusTextBlock.Text = "Status: Starting direct download...";
            }
            else
            {
                FileNameTextBlock.Text = "Preparing download...";
            }

            string tempFileName = "unknown_file.dat";
            string tempFilePath = null;

            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Get, url))
                using (var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (isGoogleDriveInitialAttempt && (response.Content.Headers.ContentType?.MediaType?.Contains("text/html") ?? false) &&
                        (response.RequestMessage.RequestUri.Host.Contains("google.com") || response.RequestMessage.RequestUri.Host.Contains("drive.usercontent.google.com")))
                    {
                        string htmlContent = await response.Content.ReadAsStringAsync();
                        var confirmLinkMatch = Regex.Match(htmlContent, @"<form[^>]*id=[""']downloadForm[""'][^>]*action=[""']([^""']+)[""']", RegexOptions.IgnoreCase);
                        if (!confirmLinkMatch.Success)
                        {
                            confirmLinkMatch = Regex.Match(htmlContent, @"href=[""'](https?://drive\.google\.com/uc\?export=download[^""']+)[""']", RegexOptions.IgnoreCase);
                        }

                        if (confirmLinkMatch.Success)
                        {
                            string newUrl = System.Net.WebUtility.HtmlDecode(confirmLinkMatch.Groups[1].Value);
                            if (!newUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                            {
                                newUrl = new Uri(response.RequestMessage.RequestUri, newUrl).ToString();
                            }
                            StatusTextBlock.Text = "Status: Following Google Drive confirmation link...";
                            await Task.Delay(200);
                            await DownloadDirectFile(newUrl, tempDownloadFolderPath, cancellationToken, false);
                            return;
                        }
                        else
                        {
                            StatusTextBlock.Text = "Status: Google Drive may require confirmation, or file is too large/unavailable for direct download. Auto-link not found.";
                            FileNameTextBlock.Text = "Google Drive: Confirmation needed/Error";
                            return;
                        }
                    }
                    response.EnsureSuccessStatusCode();

                    tempFileName = GetFileNameFromHeaders(response, url);
                    tempFilePath = Path.Combine(tempDownloadFolderPath, tempFileName);
                    if (FileNameTextBlock != null)
                    {
                        string cleanDisplayTitle = Path.GetFileNameWithoutExtension(tempFileName);
                        FileNameTextBlock.Text = Utilities.SanitizeFileName(cleanDisplayTitle);
                    }
                    StatusTextBlock.Text = $"Status: Downloading...";

                    long? totalBytes = response.Content.Headers.ContentLength;
                    int lastPercentage = -1;

                    if (DownloadProgressBar != null)
                    {
                        DownloadProgressBar.IsIndeterminate = !(totalBytes.HasValue && totalBytes.Value > 0);
                        if (!DownloadProgressBar.IsIndeterminate) DownloadProgressBar.Maximum = 100; else DownloadProgressBar.Maximum = 0;
                        DownloadProgressBar.Value = 0;
                    }

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
                                if (DownloadProgressBar != null && DownloadProgressBar.IsIndeterminate) DownloadProgressBar.IsIndeterminate = false;
                                double percentage = (double)totalBytesRead / totalBytes.Value * 100;
                                if ((int)percentage != lastPercentage)
                                {
                                    if (DownloadProgressBar != null) DownloadProgressBar.Value = Math.Min(percentage, 100.0);
                                    StatusTextBlock.Text = $"Downloading: {percentage:F1}% of {Utilities.FormatBytesOutput(totalBytes.Value)}";
                                    lastPercentage = (int)percentage;
                                }
                            }
                            else
                            {
                                if (DownloadProgressBar != null && !DownloadProgressBar.IsIndeterminate) DownloadProgressBar.IsIndeterminate = true;
                                StatusTextBlock.Text = $"Status: Downloading ({Utilities.FormatBytesOutput(totalBytesRead)})...";
                            }
                        }
                    }

                    if (DownloadProgressBar != null)
                    {
                        DownloadProgressBar.IsIndeterminate = false;
                        if (totalBytes.HasValue && totalBytes.Value > 0) DownloadProgressBar.Value = 100;
                    }

                    if (!Directory.Exists(SelectedDirectory))
                    {
                        Debug.WriteLine($"Destination directory does not exist: {SelectedDirectory}");
                        StatusTextBlock.Text = $"Status: Error - Destination directory '{SelectedDirectory}' not found.";
                        FileNameTextBlock.Text = "Move Error";
                        goto EndDirectDownloadLogic;
                    }

                    string targetPath;
                    try
                    {
                        targetPath = Utilities.GetUniqueFilePath(SelectedDirectory, tempFileName);
                    }
                    catch (IOException ex)
                    {
                        StatusTextBlock.Text = "Status: Too many existing files with similar names. Could not move.";
                        FileNameTextBlock.Text = "Move Error";
                        Debug.WriteLine($"GetUniqueFilePath failed: {ex.Message}");
                        goto EndDirectDownloadLogic;
                    }


                    try
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        File.Move(tempFilePath, targetPath);
                        tempFilePath = null;
                        if (FileNameTextBlock != null)
                        {
                            string finalCleanTitle = Path.GetFileNameWithoutExtension(targetPath);
                            FileNameTextBlock.Text = Utilities.SanitizeFileName(finalCleanTitle);
                        }
                        StatusTextBlock.Text = $"Status: Download complete! Saved as '{Path.GetFileName(targetPath)}'";
                    }
                    catch (Exception moveEx)
                    {
                        Debug.WriteLine($"Error moving file: {moveEx.ToString()}");
                        StatusTextBlock.Text = $"Status: Download complete to temp, but failed to move: {moveEx.Message.Split('\n')[0]}";
                        FileNameTextBlock.Text = "File Move Error";
                    }
                EndDirectDownloadLogic:;
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (HttpRequestException httpEx)
            {
                StatusTextBlock.Text = $"Status: HTTP Error - {httpEx.StatusCode?.ToString() ?? httpEx.Message.Split('\n')[0]}";
                FileNameTextBlock.Text = $"File: (Download Failed - {httpEx.StatusCode?.ToString() ?? "HTTP"})";
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in DownloadDirectFile: {ex.ToString()}");
                if (StatusTextBlock != null) StatusTextBlock.Text = $"Status: Download Error - {ex.Message.Split('\n')[0]}.";
                if (FileNameTextBlock != null) FileNameTextBlock.Text = "File: (Download Failed)";
            }
            finally
            {
                if (!string.IsNullOrEmpty(tempFilePath) && File.Exists(tempFilePath))
                {
                    try
                    {
                        Debug.WriteLine($"File left in temp (move failed or skipped): {tempFilePath}");
                    }
                    catch (Exception delEx) { Debug.WriteLine($"Error deleting temp file {tempFilePath} after failed move: {delEx.Message}"); }
                }
                CleanUpTempFolder(tempDownloadFolderPath);
            }
        }
    }
}