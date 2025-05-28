using System;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows; 
using System.Windows.Controls; 

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

        private async Task ProcessUrlChange(string url, bool isInitialLoad = false)
        {
            if (_isAppBusy && !isInitialLoad && StatusTextBlock != null && !StatusTextBlock.Text.Contains("Checking for") && !StatusTextBlock.Text.Contains("Initializing..."))
            {
                return;
            }

            await SetAppBusyState(true, "Status: Processing URL...");

            if (YouTubeQualityComboBox != null) YouTubeQualityComboBox.ItemsSource = null;
            if (QualitySection != null) QualitySection.Visibility = Visibility.Collapsed;

            if (string.IsNullOrWhiteSpace(url) || url == "Paste URL here...")
            {
                if (FileNameTextBlock != null) FileNameTextBlock.Text = string.Empty;
                if (StatusTextBlock != null) StatusTextBlock.Text = _isYtDlpReady ? "Status: Ready. Paste a URL." : $"Status: {YtDlpFileName} not ready. YouTube features disabled.";
                await SetAppBusyState(false);
                return;
            }

            if (IsYouTubeLink(url))
            {
                if (FileNameTextBlock != null) FileNameTextBlock.Text = "Processing YouTube URL...";
                if (!_isYtDlpReady)
                {
                    if (StatusTextBlock != null) StatusTextBlock.Text = $"Status: {YtDlpFileName} not found. Checking/Downloading...";
                    await CheckAndEnsureYtDlpExistsAsync(); // Calls method in MainWindow.YtDlp.cs
                }

                if (_isYtDlpReady) await LoadYouTubeQualitiesWithYtDlp(url); // Calls method in MainWindow.YtDlp.cs
                else
                {
                    if (StatusTextBlock != null) StatusTextBlock.Text = $"Status: {YtDlpFileName} still not available. YouTube features disabled.";
                    if (FileNameTextBlock != null) FileNameTextBlock.Text = "YouTube features disabled.";
                }
            }
            else
            {
                if (IsGoogleDriveLink(url))
                {
                    if (FileNameTextBlock != null) FileNameTextBlock.Text = "Google Drive link detected.";
                    if (StatusTextBlock != null) StatusTextBlock.Text = "Status: Ready to download Google Drive link.";
                }
                else
                {
                    if (FileNameTextBlock != null) FileNameTextBlock.Text = "Fetching file info...";
                    await TrySetFileNameFromUrlHeaders(url);
                }
            }
            await SetAppBusyState(false);
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
                    FileNameTextBlock.Text = $"Expected File: {tempFileName}";
                    StatusTextBlock.Text = "Status: File info retrieved. Ready to download.";
                }
            }
            catch (HttpRequestException httpEx)
            {
                FileNameTextBlock.Text = "Filename: (unable to determine - HTTP error)";
                StatusTextBlock.Text = $"Status: Could not get file info (HTTP: {httpEx.StatusCode}). URL might be invalid or inaccessible.";
            }
            catch (Exception)
            {
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
                    if (!string.IsNullOrWhiteSpace(pathFileName) && (pathFileName.Contains(".") || !string.IsNullOrEmpty(Path.GetExtension(pathFileName)))) { fileName = pathFileName; }
                }
            }
            fileName = Utilities.SanitizeFileName(Uri.UnescapeDataString(fileName ?? "")); // Use Utilities
            if (string.IsNullOrWhiteSpace(fileName) || string.IsNullOrWhiteSpace(Path.GetExtension(fileName)))
            {
                string extension = Utilities.GetExtensionFromMimeType(response.Content.Headers.ContentType?.MediaType); // Use Utilities
                if (string.IsNullOrWhiteSpace(fileName) || fileName == "downloaded_file") { fileName = (IsGoogleDriveLink(url) ? "gdrive_download" : "downloaded_file") + extension; }
                else if (string.IsNullOrWhiteSpace(Path.GetExtension(fileName))) { fileName = Path.ChangeExtension(fileName, extension); }
            }
            return string.IsNullOrWhiteSpace(fileName) ? "unknown_file.dat" : fileName;
        }

        private async Task DownloadGoogleDriveFile(string url)
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
            await DownloadDirectFile(directDownloadUrl, true);
        }

        private async Task DownloadDirectFile(string url, bool isGoogleDriveInitialAttempt = false)
        {
            if (StatusTextBlock == null || FileNameTextBlock == null || DownloadProgressBar == null) { return; }
            StatusTextBlock.Text = "Status: Starting direct download...";
            FileNameTextBlock.Text = "Connecting for direct download...";
            string fileName = "unknown_file.dat";
            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Get, url))
                using (var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead))
                {
                    if (isGoogleDriveInitialAttempt && (response.Content.Headers.ContentType?.MediaType?.Contains("text/html") ?? false) && response.RequestMessage.RequestUri.Host.Contains("google.com"))
                    {
                        string htmlContent = await response.Content.ReadAsStringAsync();
                        var confirmLinkMatch = Regex.Match(htmlContent, @"<form\s+id=""downloadForm""[\s\S]*?action=""([^""]+)""");
                        if (confirmLinkMatch.Success)
                        {
                            string newUrl = System.Net.WebUtility.HtmlDecode(confirmLinkMatch.Groups[1].Value);
                            if (!newUrl.StartsWith("http")) { newUrl = new Uri(response.RequestMessage.RequestUri, newUrl).ToString(); }
                            StatusTextBlock.Text = "Status: Following Google Drive confirmation link..."; FileNameTextBlock.Text = "Google Drive: Confirming...";
                            await Task.Delay(200); await DownloadDirectFile(newUrl, false); return;
                        }
                        else { StatusTextBlock.Text = "Status: Google Drive may require confirmation or file is unavailable. Auto-link not found."; FileNameTextBlock.Text = "Google Drive: Confirmation needed/Error"; return; }
                    }
                    response.EnsureSuccessStatusCode();
                    fileName = GetFileNameFromHeaders(response, url);
                    FileNameTextBlock.Text = $"Downloading File: {fileName}";
                    string outputPath = Path.Combine(SelectedDirectory, fileName);
                    StatusTextBlock.Text = $"Status: Downloading '{fileName}'...";
                    long? totalBytes = response.Content.Headers.ContentLength; int lastPercentage = -1;
                    if (DownloadProgressBar != null)
                    {
                        DownloadProgressBar.IsIndeterminate = !(totalBytes.HasValue && totalBytes.Value > 0);
                        if (!DownloadProgressBar.IsIndeterminate) DownloadProgressBar.Maximum = 100;
                    }

                    using (var fileStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
                    using (var stream = await response.Content.ReadAsStreamAsync())
                    {
                        byte[] buffer = new byte[81920]; int bytesRead; long totalBytesRead = 0;
                        if (DownloadProgressBar != null && !DownloadProgressBar.IsIndeterminate) DownloadProgressBar.Value = 0;
                        while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                        {
                            await fileStream.WriteAsync(buffer, 0, bytesRead); totalBytesRead += bytesRead;
                            if (totalBytes.HasValue && totalBytes.Value > 0)
                            {
                                if (DownloadProgressBar != null && DownloadProgressBar.IsIndeterminate) DownloadProgressBar.IsIndeterminate = false;
                                double percentage = (double)totalBytesRead / totalBytes.Value * 100;
                                if ((int)percentage != lastPercentage)
                                {
                                    if (DownloadProgressBar != null) DownloadProgressBar.Value = percentage;
                                    StatusTextBlock.Text = $"Downloading: {percentage:F1}% of {Utilities.FormatBytesOutput(totalBytes.Value)} | File: {fileName}"; // Use Utilities
                                    lastPercentage = (int)percentage;
                                }
                            }
                            else
                            {
                                if (DownloadProgressBar != null && !DownloadProgressBar.IsIndeterminate) DownloadProgressBar.IsIndeterminate = true;
                                StatusTextBlock.Text = $"Status: Downloading '{fileName}' ({Utilities.FormatBytesOutput(totalBytesRead)})..."; // Use Utilities
                            }
                        }
                    }
                    if (DownloadProgressBar != null) { DownloadProgressBar.IsIndeterminate = false; if (totalBytes.HasValue && totalBytes.Value > 0) DownloadProgressBar.Value = 100; }
                    StatusTextBlock.Text = $"Status: File '{fileName}' downloaded successfully!"; FileNameTextBlock.Text = $"Completed: {fileName}";
                }
            }
            catch (HttpRequestException httpEx)
            {
                if (isGoogleDriveInitialAttempt && httpEx.StatusCode == System.Net.HttpStatusCode.Forbidden) StatusTextBlock.Text = "Status: GDrive access forbidden (403). File not public/quota issue.";
                else StatusTextBlock.Text = $"Status: HTTP Error - {httpEx.StatusCode?.ToString() ?? httpEx.Message.Split('\n')[0]}";
                FileNameTextBlock.Text = $"File: (Download Failed - {httpEx.StatusCode?.ToString() ?? "HTTP"})";
            }
            catch (Exception ex)
            {
                if (StatusTextBlock != null) StatusTextBlock.Text = $"Status: Download Error - {ex.Message.Split('\n')[0]}.";
                if (FileNameTextBlock != null) FileNameTextBlock.Text = "File: (Download Failed)";
                if (!string.IsNullOrEmpty(fileName) && fileName != "unknown_file.dat" && !string.IsNullOrEmpty(SelectedDirectory) && Directory.Exists(SelectedDirectory))
                {
                    string partialPath = Path.Combine(SelectedDirectory, fileName);
                    if (File.Exists(partialPath)) { try { File.Delete(partialPath); } catch { /* best effort */ } }
                }
            }
        }
    }
}