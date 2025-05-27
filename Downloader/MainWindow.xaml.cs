using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Mime;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Ookii.Dialogs.Wpf;
using Newtonsoft.Json.Linq;
// using System.Windows.Shell; // WindowChrome is part of PresentationFramework, not Shell specifically.

namespace UniversalDownloader
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private string _selectedDirectory;
        private HttpClient _httpClient;

        private const string YtDlpFileName = "yt-dlp.exe";
        private string _ytDlpExecutablePath;
        private const string YtDlpDownloadUrl = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe";
        private bool _isYtDlpReady = false;
        private bool _isAppBusy = false;
        private string _currentDownloadingComponent = null;
        private long _ytDlpCurrentComponentTotalBytes = -1;

        // For pseudo-maximize state
        private bool _isManuallyPseudoMaximized = false;
        private Rect _normalWindowBoundsBeforePseudoMaximize;

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;

            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");
            _httpClient.Timeout = TimeSpan.FromMinutes(10);

            _ytDlpExecutablePath = Path.Combine(AppContext.BaseDirectory, YtDlpFileName);

            // SourceInitialized += MainWindow_SourceInitialized; // Not using this with the pseudo-maximize approach
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await SetAppBusyState(true, "Status: Initializing...");

            if (DirectoryPathTextBox != null && string.IsNullOrEmpty(_selectedDirectory))
            {
                DirectoryPathTextBox.Foreground = (Brush)FindResource("TextSecondaryBrush");
            }
            if (UrlTextBox != null && UrlTextBox.Text == "Paste URL here...")
            {
                UrlTextBox.Foreground = (Brush)FindResource("TextSecondaryBrush");
            }

            await CheckAndEnsureYtDlpExistsAsync();
            await SetAppBusyState(false);

            if (UrlTextBox != null && !string.IsNullOrWhiteSpace(UrlTextBox.Text) && UrlTextBox.Text != "Paste URL here...")
            {
                await ProcessUrlChange(UrlTextBox.Text, true);
            }
            else
            {
                if (StatusTextBlock != null)
                {
                    StatusTextBlock.Text = _isYtDlpReady ? "Status: Ready. Paste a URL." : $"Status: {YtDlpFileName} not ready. YouTube features disabled.";
                }
            }
        }

        // Helper method to find a visual child of a specific type
        public static T FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null)
            {
                return null;
            }

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, i);
                if (child != null && child is T)
                {
                    return (T)child;
                }
                else
                {
                    T childOfChild = FindVisualChild<T>(child);
                    if (childOfChild != null)
                    {
                        return childOfChild;
                    }
                }
            }
            return null;
        }

        #region Window Control and Custom Chrome Handlers

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                this.DragMove();
            }
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        private void MaximizeRestoreButton_Click(object sender, RoutedEventArgs e)
        {
            if (this.WindowState == WindowState.Maximized) // If truly OS maximized by WindowChrome
            {
                this.WindowState = WindowState.Normal; // OS will restore
                _isManuallyPseudoMaximized = false;
                // StateChanged event will handle button icon updates
            }
            else if (_isManuallyPseudoMaximized) // If we are in our pseudo-maximized state
            {
                // Restore from pseudo-maximize
                this.Left = _normalWindowBoundsBeforePseudoMaximize.Left;
                this.Top = _normalWindowBoundsBeforePseudoMaximize.Top;
                this.Width = _normalWindowBoundsBeforePseudoMaximize.Width;
                this.Height = _normalWindowBoundsBeforePseudoMaximize.Height;
                _isManuallyPseudoMaximized = false;

                if (MaximizeRestoreButton != null)
                {
                    MaximizeRestoreButton.Content = ""; // Maximize icon (Segoe MDL2 Assets: )
                    MaximizeRestoreButton.ToolTip = "Maximize";
                }
                if (MainWindowRootBorder != null)
                {
                    MainWindowRootBorder.CornerRadius = new CornerRadius(16); // Restore rounding
                }
            }
            else // Is Normal, go to pseudo-maximize
            {
                _normalWindowBoundsBeforePseudoMaximize = new Rect(this.Left, this.Top, this.Width, this.Height);

                this.Left = SystemParameters.WorkArea.Left;
                this.Top = SystemParameters.WorkArea.Top;
                this.Width = SystemParameters.WorkArea.Width;
                this.Height = SystemParameters.WorkArea.Height;
                _isManuallyPseudoMaximized = true;

                if (MaximizeRestoreButton != null)
                {
                    MaximizeRestoreButton.Content = ""; // Restore icon (Segoe MDL2 Assets: )
                    MaximizeRestoreButton.ToolTip = "Restore";
                }
                if (MainWindowRootBorder != null)
                {
                    // Ensure rounded corners are maintained in pseudo-maximized state
                    MainWindowRootBorder.CornerRadius = new CornerRadius(16);
                }
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void MainWindow_StateChanged(object sender, EventArgs e)
        {
            if (this.WindowState == WindowState.Maximized)
            {
                // OS has maximized the window (likely via WindowChrome interactions like Aero Snap or Win+Up)
                _isManuallyPseudoMaximized = false; // OS took over, clear our flag

                if (MainWindowRootBorder != null)
                {
                    // Keep our designed rounding even when OS maximized
                    MainWindowRootBorder.CornerRadius = new CornerRadius(16);
                    MainWindowRootBorder.Margin = new Thickness(0); // Ensure no extra margin
                }
                if (MaximizeRestoreButton != null)
                {
                    MaximizeRestoreButton.Content = ""; // Restore icon (Segoe MDL2 Assets: )
                    MaximizeRestoreButton.ToolTip = "Restore";
                }
            }
            else if (this.WindowState == WindowState.Normal)
            {
                // If it became normal and we *were* pseudo-maximized, it means something else restored it (e.g. dragging title bar)
                // or we clicked restore from an OS maximized state.
                // If we were pseudo-maximized, dragging title bar will "restore" to the pseudo-maximized size,
                // then clicking our restore button will go to true normal. This is an edge case of this method.
                if (_isManuallyPseudoMaximized)
                {
                    // If OS made it normal while we thought we were pseudo-maximized (e.g., user dragged it)
                    // then we are no longer pseudo-maximized by our definition.
                    // The size will be whatever the user dragged it to.
                    // _isManuallyPseudoMaximized = false; // Let the button click handle this transition if it was initiated by button
                }
                // _isManuallyPseudoMaximized = false; // Generally, if OS sets to normal, clear our flag.
                // Handled more explicitly in MaximizeRestoreButton_Click

                if (MainWindowRootBorder != null)
                {
                    MainWindowRootBorder.CornerRadius = new CornerRadius(16);
                    MainWindowRootBorder.Margin = new Thickness(0);
                }
                if (MaximizeRestoreButton != null && !_isManuallyPseudoMaximized) // Only change to Maximize if not in pseudo-maximized state
                {
                    MaximizeRestoreButton.Content = ""; // Maximize icon (Segoe MDL2 Assets: )
                    MaximizeRestoreButton.ToolTip = "Maximize";
                }
            }
            // Minimized state is handled by the OS, usually no visual changes needed from us here for the border.
        }

        #endregion

        public string SelectedDirectory
        {
            get => _selectedDirectory;
            set
            {
                _selectedDirectory = value;
                if (DirectoryPathTextBox != null)
                {
                    if (string.IsNullOrEmpty(value))
                    {
                        DirectoryPathTextBox.Text = "No directory selected";
                        DirectoryPathTextBox.Foreground = (Brush)FindResource("TextSecondaryBrush");
                    }
                    else
                    {
                        DirectoryPathTextBox.Text = value;
                        DirectoryPathTextBox.Foreground = (Brush)FindResource("TextPrimaryBrush");
                    }
                }
                OnPropertyChanged(nameof(SelectedDirectory));
                UpdateDownloadButtonState();
            }
        }

        private Task SetAppBusyState(bool busy, string statusMessage = null)
        {
            _isAppBusy = busy;

            return Dispatcher.InvokeAsync(() =>
            {
                if (UrlTextBox != null) UrlTextBox.IsEnabled = !busy;
                if (BrowseButton != null) BrowseButton.IsEnabled = !busy;

                if (statusMessage != null && StatusTextBlock != null)
                {
                    StatusTextBlock.Text = statusMessage;
                }
                UpdateDownloadButtonState();
            }).Task;
        }

        private bool CanInitiateDownload()
        {
            if (UrlTextBox == null || string.IsNullOrWhiteSpace(UrlTextBox.Text) || UrlTextBox.Text == "Paste URL here...")
            {
                return false;
            }
            if (string.IsNullOrWhiteSpace(SelectedDirectory) || !Directory.Exists(SelectedDirectory))
            {
                return false;
            }
            return true;
        }

        #region yt-dlp Management
        private async Task CheckAndEnsureYtDlpExistsAsync()
        {
            _isYtDlpReady = false;

            if (FileNameTextBlock != null)
            {
                FileNameTextBlock.Text = "";
            }
            await Task.Delay(10); // Give UI a moment

            if (System.IO.File.Exists(_ytDlpExecutablePath))
            {
                if (StatusTextBlock != null)
                {
                    StatusTextBlock.Text = $"Status: {YtDlpFileName} found.";
                }
                _isYtDlpReady = true;
            }
            else
            {
                if (StatusTextBlock != null)
                {
                    StatusTextBlock.Text = $"Status: {YtDlpFileName} not found. Attempting to download...";
                }
                if (FileNameTextBlock != null)
                {
                    FileNameTextBlock.Text = $"Downloading: {YtDlpFileName}";
                }

                bool downloaded = await TryDownloadYtDlpInternalAsync();

                if (downloaded)
                {
                    if (StatusTextBlock != null)
                    {
                        StatusTextBlock.Text = $"Status: {YtDlpFileName} downloaded successfully. Ready.";
                    }
                    if (FileNameTextBlock != null)
                    {
                        FileNameTextBlock.Text = "";
                    }
                    _isYtDlpReady = true;
                }
                else
                {
                    if (StatusTextBlock != null)
                    {
                        StatusTextBlock.Text = $"Status: Failed to download {YtDlpFileName}. YouTube downloads unavailable. Please place it in the app folder.";
                    }
                    if (FileNameTextBlock != null)
                    {
                        FileNameTextBlock.Text = $"{YtDlpFileName} download failed.";
                    }
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
                    if (StatusTextBlock != null)
                    {
                        StatusTextBlock.Text = $"Status: Downloading {YtDlpFileName} from {YtDlpDownloadUrl}...";
                    }

                    var response = await downloadClient.GetAsync(YtDlpDownloadUrl, HttpCompletionOption.ResponseHeadersRead);
                    response.EnsureSuccessStatusCode();

                    long? totalDownloadSize = response.Content.Headers.ContentLength;
                    long totalBytesRead = 0;
                    int lastPercentage = -1;

                    if (DownloadProgressBar != null)
                    {
                        if (totalDownloadSize.HasValue && totalDownloadSize.Value > 0)
                        {
                            DownloadProgressBar.IsIndeterminate = false;
                            DownloadProgressBar.Maximum = 100;
                        }
                        else
                        {
                            DownloadProgressBar.IsIndeterminate = true;
                        }
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
                                if (DownloadProgressBar != null && DownloadProgressBar.IsIndeterminate)
                                {
                                    DownloadProgressBar.IsIndeterminate = false;
                                }
                                double percentage = (double)totalBytesRead / totalDownloadSize.Value * 100;
                                if ((int)percentage != lastPercentage)
                                {
                                    if (DownloadProgressBar != null)
                                    {
                                        DownloadProgressBar.Value = percentage;
                                    }
                                    if (StatusTextBlock != null)
                                    {
                                        StatusTextBlock.Text = $"Status: Downloading {YtDlpFileName}... {percentage:F0}%";
                                    }
                                    lastPercentage = (int)percentage;
                                }
                            }
                            else
                            {
                                if (DownloadProgressBar != null && !DownloadProgressBar.IsIndeterminate)
                                {
                                    DownloadProgressBar.IsIndeterminate = true;
                                }
                                if (StatusTextBlock != null)
                                {
                                    StatusTextBlock.Text = $"Status: Downloading {YtDlpFileName} ({totalBytesRead / (1024.0):F1} KB)...";
                                }
                            }
                        }
                    }

                    if (DownloadProgressBar != null && !DownloadProgressBar.IsIndeterminate)
                    {
                        DownloadProgressBar.Value = 100;
                    }
                    return true;
                }
            }
            catch (Exception ex)
            {
                if (StatusTextBlock != null)
                {
                    StatusTextBlock.Text = $"Status: Error downloading {YtDlpFileName}: {ex.Message.Split('\n')[0]}";
                }
                if (System.IO.File.Exists(_ytDlpExecutablePath))
                {
                    try { System.IO.File.Delete(_ytDlpExecutablePath); }
                    catch { /* best effort */ }
                }
                return false;
            }
            finally
            {
                if (DownloadProgressBar != null)
                {
                    DownloadProgressBar.IsIndeterminate = false;
                }
            }
        }
        #endregion

        #region UI Event Handlers & URL Processing
        private void UrlTextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (UrlTextBox.Text == "Paste URL here...")
            {
                UrlTextBox.Text = "";
                UrlTextBox.Foreground = (Brush)FindResource("TextPrimaryBrush");
            }
        }

        private void UrlTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(UrlTextBox.Text))
            {
                UrlTextBox.Text = "Paste URL here...";
                UrlTextBox.Foreground = (Brush)FindResource("TextSecondaryBrush");
            }
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isAppBusy)
            {
                return;
            }
            var dialog = new VistaFolderBrowserDialog
            {
                Description = "Select a folder to download files into",
                UseDescriptionForTitle = true
            };

            if (dialog.ShowDialog(this).GetValueOrDefault())
            {
                SelectedDirectory = dialog.SelectedPath;
            }
        }

        private async void UrlTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (YouTubeQualityComboBox == null || StatusTextBlock == null || FileNameTextBlock == null || QualitySection == null)
            {
                return;
            }

            if (_isAppBusy && StatusTextBlock != null && !StatusTextBlock.Text.Contains("Initializing...") && !StatusTextBlock.Text.Contains("Checking for"))
            {
                return;
            }

            string currentUrl = UrlTextBox.Text;
            await ProcessUrlChange(currentUrl);
        }

        private async Task ProcessUrlChange(string url, bool isInitialLoad = false)
        {
            if (_isAppBusy && !isInitialLoad && StatusTextBlock != null && !StatusTextBlock.Text.Contains("Checking for") && !StatusTextBlock.Text.Contains("Initializing..."))
            {
                return;
            }

            await SetAppBusyState(true, "Status: Processing URL...");

            if (YouTubeQualityComboBox != null)
            {
                YouTubeQualityComboBox.ItemsSource = null;
            }
            if (QualitySection != null)
            {
                QualitySection.Visibility = Visibility.Collapsed;
            }

            if (string.IsNullOrWhiteSpace(url) || url == "Paste URL here...")
            {
                if (FileNameTextBlock != null)
                {
                    FileNameTextBlock.Text = string.Empty;
                }
                if (StatusTextBlock != null)
                {
                    StatusTextBlock.Text = _isYtDlpReady ? "Status: Ready. Paste a URL." : $"Status: {YtDlpFileName} not ready. YouTube features disabled.";
                }
                await SetAppBusyState(false);
                return;
            }

            if (IsYouTubeLink(url))
            {
                if (FileNameTextBlock != null)
                {
                    FileNameTextBlock.Text = "Processing YouTube URL...";
                }
                if (!_isYtDlpReady)
                {
                    if (StatusTextBlock != null)
                    {
                        StatusTextBlock.Text = $"Status: {YtDlpFileName} not found. Checking/Downloading...";
                    }
                    await CheckAndEnsureYtDlpExistsAsync();
                }

                if (_isYtDlpReady)
                {
                    await LoadYouTubeQualitiesWithYtDlp(url);
                }
                else
                {
                    if (StatusTextBlock != null)
                    {
                        StatusTextBlock.Text = $"Status: {YtDlpFileName} still not available. YouTube features disabled.";
                    }
                    if (FileNameTextBlock != null)
                    {
                        FileNameTextBlock.Text = "YouTube features disabled.";
                    }
                }
            }
            else // Not a YouTube link
            {
                if (IsGoogleDriveLink(url))
                {
                    if (FileNameTextBlock != null)
                    {
                        FileNameTextBlock.Text = "Google Drive link detected.";
                    }
                    if (StatusTextBlock != null)
                    {
                        StatusTextBlock.Text = "Status: Ready to download Google Drive link.";
                    }
                }
                else // Potentially direct link
                {
                    if (FileNameTextBlock != null)
                    {
                        FileNameTextBlock.Text = "Fetching file info...";
                    }
                    await TrySetFileNameFromUrlHeaders(url);
                }
            }
            await SetAppBusyState(false);
        }

        private void YouTubeQualityComboBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            ComboBox comboBox = sender as ComboBox;
            if (comboBox != null && comboBox.IsEnabled && !comboBox.IsDropDownOpen)
            {
                var toggleButton = FindVisualChild<System.Windows.Controls.Primitives.ToggleButton>(comboBox);

                bool clickIsOnToggleButtonOrChild = false;
                if (toggleButton != null)
                {
                    DependencyObject current = e.OriginalSource as DependencyObject;
                    while (current != null && current != comboBox)
                    {
                        if (current == toggleButton)
                        {
                            clickIsOnToggleButtonOrChild = true;
                            break;
                        }
                        current = VisualTreeHelper.GetParent(current);
                    }
                }

                if (!clickIsOnToggleButtonOrChild)
                {
                    comboBox.IsDropDownOpen = true;
                }
            }
        }


        private void YouTubeQualityComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateDownloadButtonState();
        }

        private async void DownloadButton_Click(object sender, RoutedEventArgs e)
        {
            if (StatusTextBlock == null || FileNameTextBlock == null || DownloadProgressBar == null || UrlTextBox == null)
            {
                return;
            }

            string url = UrlTextBox.Text;
            if (!CanInitiateDownload())
            {
                StatusTextBlock.Text = "Status: Please enter a valid URL and select an existing download directory.";
                return;
            }

            await SetAppBusyState(true, "Status: Preparing to download...");
            if (DownloadProgressBar != null)
            {
                DownloadProgressBar.Value = 0;
                DownloadProgressBar.IsIndeterminate = true;
            }

            try
            {
                if (IsYouTubeLink(url))
                {
                    if (!_isYtDlpReady)
                    {
                        StatusTextBlock.Text = $"Status: {YtDlpFileName} is not available. YouTube download aborted.";
                        await SetAppBusyState(false);
                        return;
                    }
                    if (YouTubeQualityComboBox == null || YouTubeQualityComboBox.SelectedItem == null)
                    {
                        StatusTextBlock.Text = "Status: Please select a YouTube video quality.";
                        await SetAppBusyState(false);
                        return;
                    }
                    var selectedQuality = YouTubeQualityComboBox.SelectedItem as YouTubeQualityItem;
                    if (selectedQuality == null)
                    {
                        StatusTextBlock.Text = "Status: Invalid YouTube video quality selected.";
                        await SetAppBusyState(false);
                        return;
                    }
                    await DownloadYouTubeVideoWithYtDlp(url, selectedQuality.FormatCode);
                }
                else if (IsGoogleDriveLink(url))
                {
                    await DownloadGoogleDriveFile(url);
                }
                else // Direct download
                {
                    await DownloadDirectFile(url);
                }
            }
            catch (Exception ex) // Catch-all for unexpected errors during the download process
            {
                if (StatusTextBlock != null)
                {
                    StatusTextBlock.Text = $"Status: Critical Download Error - {ex.Message.Split('\n')[0]}.";
                }
                if (FileNameTextBlock != null)
                {
                    FileNameTextBlock.Text = "File: (Download Failed)";
                }
            }
            finally
            {
                if (DownloadProgressBar != null && DownloadProgressBar.IsIndeterminate)
                {
                    DownloadProgressBar.IsIndeterminate = false;
                }
                await SetAppBusyState(false); // Ensure busy state is reset
            }
        }
        #endregion

        #region Helper Methods for UI and Logic

        private static long ParseYtDlpSizeStringToBytes(string sizeString)
        {
            if (string.IsNullOrWhiteSpace(sizeString)) { return 0; }
            var match = Regex.Match(sizeString.Trim(), @"^(?<size>[\d\.]+)\s*(?<unit>[KMGT]?i?B)$", RegexOptions.IgnoreCase);
            if (!match.Success) { return 0; }
            if (!double.TryParse(match.Groups["size"].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double sizeValue)) { return 0; }
            string unit = match.Groups["unit"].Value.ToUpperInvariant();
            long multiplier = 1L;
            if (unit == "B") { multiplier = 1L; }
            else if (unit == "KIB" || unit == "KB") { multiplier = 1024L; }
            else if (unit == "MIB" || unit == "MB") { multiplier = 1024L * 1024L; }
            else if (unit == "GIB" || unit == "GB") { multiplier = 1024L * 1024L * 1024L; }
            else if (unit == "TIB" || unit == "TB") { multiplier = 1024L * 1024L * 1024L * 1024L; }
            return (long)(sizeValue * multiplier);
        }

        private static string FormatBytesOutput(long bytes)
        {
            string[] suffixes = { "B", "KiB", "MiB", "GiB", "TiB", "PiB", "EiB" };
            int i = 0;
            double dblBytes = bytes;
            if (bytes == 0) { return "0 B"; }
            while (i < suffixes.Length - 1 && dblBytes >= 1024) { dblBytes /= 1024; i++; }
            return $"{dblBytes:F2} {suffixes[i]}";
        }

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

        private void UpdateDownloadButtonState()
        {
            if (DownloadButton == null || UrlTextBox == null || YouTubeQualityComboBox == null || QualitySection == null)
            {
                return;
            }

            bool canDownloadNow = CanInitiateDownload();
            bool youtubeSpecificConditionsMet = true;

            if (IsYouTubeLink(UrlTextBox.Text))
            {
                youtubeSpecificConditionsMet = _isYtDlpReady &&
                                               YouTubeQualityComboBox.SelectedItem != null &&
                                               QualitySection.Visibility == Visibility.Visible;
            }

            DownloadButton.IsEnabled = canDownloadNow && youtubeSpecificConditionsMet && !_isAppBusy;

            if (YouTubeQualityComboBox != null)
            {
                YouTubeQualityComboBox.IsEnabled = !_isAppBusy && _isYtDlpReady && IsYouTubeLink(UrlTextBox.Text) && YouTubeQualityComboBox.HasItems;
            }
        }

        private string SanitizeFileName(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) { return "downloaded_file"; }
            fileName = fileName.Trim('"');
            string invalidCharsPattern = string.Format("[{0}]", Regex.Escape(new string(Path.GetInvalidFileNameChars())));
            fileName = Regex.Replace(fileName, invalidCharsPattern, "_");
            fileName = Regex.Replace(fileName, @"\.+$|^\.+$", "_");
            fileName = fileName.Trim();
            if (fileName.Length > 150) { fileName = fileName.Substring(0, 150).TrimEnd('_'); }
            return string.IsNullOrWhiteSpace(fileName) ? "downloaded_file" : fileName;
        }

        private string GetExtensionFromMimeType(string mimeType)
        {
            if (string.IsNullOrWhiteSpace(mimeType)) { return ".dat"; }
            var mapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                {"image/jpeg", ".jpg"}, {"image/png", ".png"}, {"image/gif", ".gif"}, {"image/bmp", ".bmp"},
                {"application/pdf", ".pdf"}, {"application/zip", ".zip"}, {"application/x-zip-compressed", ".zip"},
                {"application/msword", ".doc"}, {"application/vnd.openxmlformats-officedocument.wordprocessingml.document", ".docx"},
                {"application/vnd.ms-excel", ".xls"}, {"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", ".xlsx"},
                {"text/plain", ".txt"}, {"text/html", ".html"}, {"text/xml", ".xml"},
                {"video/mp4", ".mp4"}, {"video/mpeg", ".mpeg"}, {"video/quicktime", ".mov"}, {"video/x-msvideo", ".avi"},
                {"video/webm", ".webm"}, {"video/x-matroska", ".mkv"},
                {"audio/mpeg", ".mp3"}, {"audio/wav", ".wav"}, {"audio/ogg", ".ogg"}, {"audio/aac", ".aac"}, {"audio/webm", ".webm"}
            };
            return mapping.TryGetValue(mimeType.Split(';')[0].Trim(), out var extension) ? extension : ".dat";
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
            fileName = SanitizeFileName(Uri.UnescapeDataString(fileName ?? ""));
            if (string.IsNullOrWhiteSpace(fileName) || string.IsNullOrWhiteSpace(Path.GetExtension(fileName)))
            {
                string extension = GetExtensionFromMimeType(response.Content.Headers.ContentType?.MediaType);
                if (string.IsNullOrWhiteSpace(fileName) || fileName == "downloaded_file") { fileName = (IsGoogleDriveLink(url) ? "gdrive_download" : "downloaded_file") + extension; }
                else if (string.IsNullOrWhiteSpace(Path.GetExtension(fileName))) { fileName = Path.ChangeExtension(fileName, extension); }
            }
            return string.IsNullOrWhiteSpace(fileName) ? "unknown_file.dat" : fileName;
        }
        #endregion

        #region YouTube (yt-dlp)
        private async Task LoadYouTubeQualitiesWithYtDlp(string videoUrl)
        {
            // Method implementation with multi-line formatting
            if (QualitySection != null)
            {
                QualitySection.Visibility = Visibility.Collapsed;
            }
            if (YouTubeQualityComboBox == null || StatusTextBlock == null || FileNameTextBlock == null || DownloadButton == null)
            {
                return;
            }

            YouTubeQualityComboBox.ItemsSource = null;

            if (!_isYtDlpReady)
            {
                StatusTextBlock.Text = $"Status: {YtDlpFileName} is not available. Cannot fetch YouTube qualities.";
                FileNameTextBlock.Text = "";
                return;
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
                        StatusTextBlock.Text = $"Status: yt-dlp error: {(errorOutput.Split('\n')[0])?.Trim()}";
                        FileNameTextBlock.Text = "YouTube Info Error";
                        if (QualitySection != null) { QualitySection.Visibility = Visibility.Collapsed; }
                        return;
                    }
                    if (string.IsNullOrWhiteSpace(jsonOutput))
                    {
                        StatusTextBlock.Text = "Status: yt-dlp returned empty info. Video might be unavailable or private.";
                        FileNameTextBlock.Text = "YouTube Info Error (Empty)";
                        if (QualitySection != null) { QualitySection.Visibility = Visibility.Collapsed; }
                        return;
                    }
                    JObject videoInfo = JObject.Parse(jsonOutput);
                    videoTitle = SanitizeFileName(videoInfo["title"]?.ToString() ?? "Unknown Title");
                    FileNameTextBlock.Text = $"Video: {videoTitle}";
                    ytDlpReportedFormats = videoInfo["formats"] as JArray;
                }
                if (ytDlpReportedFormats == null || !ytDlpReportedFormats.HasValues)
                {
                    StatusTextBlock.Text = "Status: No formats reported by yt-dlp for this video.";
                    FileNameTextBlock.Text = "No YouTube formats found.";
                    if (QualitySection != null) { QualitySection.Visibility = Visibility.Collapsed; }
                    return;
                }

                var distinctVideoHeights = new SortedSet<int>();
                var audioFormatItems = new List<YouTubeQualityItem>();
                var preMergedFormatItems = new List<YouTubeQualityItem>();
                foreach (JToken format in ytDlpReportedFormats)
                {
                    string vcodec = format["vcodec"]?.ToString() ?? "none";
                    string acodec = format["acodec"]?.ToString() ?? "none";
                    int height = format["height"]?.ToObject<int?>() ?? 0;
                    if (vcodec != "none" && height > 0) { distinctVideoHeights.Add(height); }
                    if (acodec != "none" && vcodec == "none")
                    {
                        string formatId = format["format_id"]?.ToString();
                        string ext = format["ext"]?.ToString() ?? "N/A";
                        double abr = format["abr"]?.ToObject<double?>() ?? 0;
                        string note = format["format_note"]?.ToString() ?? acodec;
                        long? filesize = format["filesize"]?.ToObject<long?>() ?? format["filesize_approx"]?.ToObject<long?>();
                        string sizeStr = filesize.HasValue ? $" ({(filesize.Value / (1024.0 * 1024.0)):F1} MB)" : "";
                        string label = $"Audio Only: {note.Replace("audio only", "").Trim()} ({ext}, ~{abr:F0}k) [{formatId}]{sizeStr}";
                        if (!string.IsNullOrEmpty(formatId)) { audioFormatItems.Add(new YouTubeQualityItem { Label = label, FormatCode = formatId, IsAudioOnly = true, SortPriority = (int)abr + 20000 }); }
                    }
                    else if (vcodec != "none" && acodec != "none")
                    {
                        string formatId = format["format_id"]?.ToString();
                        string ext = format["ext"]?.ToString() ?? "N/A";
                        string resolutionStr = format["resolution"]?.ToString();
                        string note = format["format_note"]?.ToString() ?? $"{height}p";
                        int fps = format["fps"]?.ToObject<int?>() ?? 0;
                        long? filesize = format["filesize"]?.ToObject<long?>() ?? format["filesize_approx"]?.ToObject<long?>();
                        string sizeStr = filesize.HasValue ? $" ({(filesize.Value / (1024.0 * 1024.0)):F1} MB)" : "";
                        string fpsStr = fps > 0 ? $"@{fps}fps" : "";
                        string displayRes = note.Contains("p") && !note.Contains("DASH") ? note : (resolutionStr ?? (height > 0 ? $"{height}p" : "Video"));
                        string label = $"Pre-merged: {displayRes} {fpsStr} ({ext}) [{formatId}]{sizeStr}";
                        if (!string.IsNullOrEmpty(formatId)) { preMergedFormatItems.Add(new YouTubeQualityItem { Label = label, FormatCode = formatId, IsAudioOnly = false, SortPriority = height + (fps > 30 ? 500 : 0) + 1000 }); }
                    }
                }
                availableQualities.Add(new YouTubeQualityItem { Label = "Best Available (Video+Audio Merged, MP4 H.264 Preferred)", FormatCode = "bestvideo[ext=mp4][vcodec^=avc]+bestaudio[ext=m4a]/bestvideo[ext=mp4]+bestaudio[ext=m4a]/bestvideo[vcodec!*=av01][vcodec!*=vp09]+bestaudio/bestvideo+bestaudio/best", IsAudioOnly = false, SortPriority = 100001 });
                availableQualities.Add(new YouTubeQualityItem { Label = "Audio Only (Best Available)", FormatCode = "bestaudio/best", IsAudioOnly = true, SortPriority = 90000 });
                var targetResolutionTiers = new List<(int height, string labelName)> { (4320, "4320p (8K)"), (3840, "3840p (UHD)"), (2160, "2160p (4K)"), (1440, "1440p (2K)"), (1080, "1080p (FHD)"), (720, "720p (HD)"), (480, "480p"), (360, "360p"), (240, "240p"), (144, "144p") };
                foreach (var tier in targetResolutionTiers)
                {
                    if (distinctVideoHeights.Any(h => h >= tier.height) || preMergedFormatItems.Any(pm => (pm.FormatCode.Contains($"{tier.height}") || (pm.Label.Contains($"{tier.height}p")))))
                    {
                        string formatCode = $"bestvideo[height={tier.height}][ext=mp4]+bestaudio[ext=m4a]/bestvideo[height={tier.height}]+bestaudio/best[height={tier.height}]";
                        string label = $"{tier.labelName} (Merged, MP4 Preferred)";
                        if (!availableQualities.Any(q => q.Label.StartsWith(tier.labelName))) { availableQualities.Add(new YouTubeQualityItem { Label = label, FormatCode = formatCode, IsAudioOnly = false, SortPriority = tier.height * 10 }); }
                    }
                }
                availableQualities.AddRange(audioFormatItems.GroupBy(a => a.FormatCode).Select(g => g.First()).OrderByDescending(a => a.SortPriority));
                availableQualities.AddRange(preMergedFormatItems.GroupBy(p => p.FormatCode).Select(g => g.First()).OrderByDescending(p => p.SortPriority));
                availableQualities = availableQualities.GroupBy(q => q.Label).Select(g => g.OrderByDescending(i => i.SortPriority).First()).OrderByDescending(q => q.SortPriority).ToList();

                if (availableQualities.Any())
                {
                    YouTubeQualityComboBox.ItemsSource = availableQualities;
                    if (QualitySection != null) { QualitySection.Visibility = Visibility.Visible; }
                    await Dispatcher.InvokeAsync(() =>
                    {
                        if (YouTubeQualityComboBox.Items.Count > 0) { YouTubeQualityComboBox.SelectedIndex = 0; }
                        UpdateDownloadButtonState();
                    }, DispatcherPriority.ContextIdle);
                    StatusTextBlock.Text = "Status: YouTube qualities listed. Select quality to download.";
                }
                else
                {
                    StatusTextBlock.Text = "Status: No downloadable formats could be determined.";
                    FileNameTextBlock.Text = "No YouTube formats available.";
                    if (QualitySection != null) { QualitySection.Visibility = Visibility.Collapsed; }
                }
            }
            catch (Win32Exception ex) { StatusTextBlock.Text = $"Status: {YtDlpFileName} execution error. {ex.Message.Split('\n')[0]}"; _isYtDlpReady = false; FileNameTextBlock.Text = "yt-dlp Error"; if (QualitySection != null) { QualitySection.Visibility = Visibility.Collapsed; } }
            catch (Newtonsoft.Json.JsonReaderException jsonEx) { StatusTextBlock.Text = $"Status: Error parsing yt-dlp output: {jsonEx.Message.Split('\n')[0]}."; FileNameTextBlock.Text = "YouTube Info Parse Error"; if (QualitySection != null) { QualitySection.Visibility = Visibility.Collapsed; } }
            catch (Exception ex) { StatusTextBlock.Text = $"Status: Error processing YouTube info: {ex.Message.Split('\n')[0]}."; FileNameTextBlock.Text = "YouTube Info Error"; if (QualitySection != null) { QualitySection.Visibility = Visibility.Collapsed; } }
        }

        private async Task DownloadYouTubeVideoWithYtDlp(string videoUrl, string formatCode)
        {
            // Method implementation with multi-line formatting
            if (StatusTextBlock == null || FileNameTextBlock == null || DownloadProgressBar == null || YouTubeQualityComboBox == null) { return; }
            if (!_isYtDlpReady) { StatusTextBlock.Text = $"Status: {YtDlpFileName} is not available. Cannot download YouTube video."; return; }

            _ytDlpCurrentComponentTotalBytes = -1;
            var selectedQualityItem = YouTubeQualityComboBox.SelectedItem as YouTubeQualityItem;
            string outputTemplate = Path.Combine(SelectedDirectory, selectedQualityItem != null && selectedQualityItem.IsAudioOnly ? "%(title)s [%(id)s] (Audio).%(ext)s" : "%(title)s [%(id)s].%(ext)s");
            StatusTextBlock.Text = $"Status: Downloading YouTube (format: {formatCode})...";
            FileNameTextBlock.Text = "Preparing YouTube download...";
            DownloadProgressBar.Value = 0;
            DownloadProgressBar.Maximum = 100;
            DownloadProgressBar.IsIndeterminate = true;
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
                string actualFileNameFromYtDlp = "downloaded_video";
                bool progressStartedForAnyComponent = false;
                double lastReportedPercentageForComponent = 0;
                using (Process process = new Process())
                {
                    process.StartInfo = psi;
                    process.EnableRaisingEvents = true;
                    process.OutputDataReceived += (s, e) => { if (e.Data != null) { Dispatcher.Invoke(() => ParseYtDlpProgress(e.Data, ref actualFileNameFromYtDlp, ref progressStartedForAnyComponent, ref lastReportedPercentageForComponent)); } };
                    process.ErrorDataReceived += (s, e) => { if (e.Data != null && !e.Data.ToLower().Contains("warning:") && !e.Data.ToLower().Contains("deprecated")) { Dispatcher.Invoke(() => StatusTextBlock.Text = $"yt-dlp Info/Error: {e.Data.Split('\n')[0]}"); } };
                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                    await process.WaitForExitAsync();
                    DownloadProgressBar.IsIndeterminate = false;
                    if (process.ExitCode == 0)
                    {
                        if (_ytDlpCurrentComponentTotalBytes > 0)
                        {
                            if (DownloadProgressBar.Maximum == _ytDlpCurrentComponentTotalBytes) { DownloadProgressBar.Value = DownloadProgressBar.Maximum; }
                            else if (DownloadProgressBar.Maximum == 100) { DownloadProgressBar.Value = 100; }
                            else { DownloadProgressBar.Value = DownloadProgressBar.Maximum; }
                        }
                        else
                        {
                            if (DownloadProgressBar.Maximum == 100) { DownloadProgressBar.Value = 100; }
                            else { DownloadProgressBar.Value = DownloadProgressBar.Maximum; }
                        }
                        StatusTextBlock.Text = $"Status: YouTube download '{Path.GetFileName(actualFileNameFromYtDlp)}' complete!";
                        FileNameTextBlock.Text = $"Completed: {Path.GetFileName(actualFileNameFromYtDlp)}";
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
                }
            }
            catch (Win32Exception ex) { StatusTextBlock.Text = $"Status: {YtDlpFileName} execution error. {ex.Message.Split('\n')[0]}"; _isYtDlpReady = false; DownloadProgressBar.Value = 0; DownloadProgressBar.IsIndeterminate = false; }
            catch (Exception ex) { StatusTextBlock.Text = $"Status: Error during YouTube download: {ex.Message.Split('\n')[0]}"; FileNameTextBlock.Text = "YouTube Download Error"; DownloadProgressBar.Value = 0; DownloadProgressBar.IsIndeterminate = false; }
        }

        private void ParseYtDlpProgress(string outputLine, ref string finalFileNameFromYtDlp, ref bool progressStartedForAnyComponent, ref double lastKnownPercentageForComponent)
        {
            // Method implementation with multi-line formatting
            if (StatusTextBlock == null || FileNameTextBlock == null || DownloadProgressBar == null || YouTubeQualityComboBox == null) { return; }
            outputLine = outputLine.Trim();
            var selectedQualityItem = YouTubeQualityComboBox.SelectedItem as YouTubeQualityItem;

            if (outputLine.StartsWith("[download] Destination:"))
            {
                string newComponentFileName = SanitizeFileName(outputLine.Substring("[download] Destination:".Length).Trim());
                if (_currentDownloadingComponent != newComponentFileName || !progressStartedForAnyComponent || DownloadProgressBar.Value >= (DownloadProgressBar.Maximum * 0.999) || lastKnownPercentageForComponent >= 99.9)
                {
                    _currentDownloadingComponent = newComponentFileName;
                    FileNameTextBlock.Text = $"Downloading: {Path.GetFileName(_currentDownloadingComponent)}";
                    _ytDlpCurrentComponentTotalBytes = -1;
                    DownloadProgressBar.IsIndeterminate = true;
                    DownloadProgressBar.Value = 0;
                    DownloadProgressBar.Maximum = 100;
                    lastKnownPercentageForComponent = 0;
                    bool isVideoFile = newComponentFileName.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) || newComponentFileName.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase) || newComponentFileName.EndsWith(".webm", StringComparison.OrdinalIgnoreCase);
                    bool isAudioFile = newComponentFileName.EndsWith(".m4a", StringComparison.OrdinalIgnoreCase) || newComponentFileName.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) || newComponentFileName.EndsWith(".opus", StringComparison.OrdinalIgnoreCase) || newComponentFileName.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase);
                    if (selectedQualityItem != null)
                    {
                        if (!selectedQualityItem.IsAudioOnly && isVideoFile) { finalFileNameFromYtDlp = _currentDownloadingComponent; }
                        else if (selectedQualityItem.IsAudioOnly && isAudioFile) { finalFileNameFromYtDlp = _currentDownloadingComponent; }
                        else if (finalFileNameFromYtDlp == "downloaded_video" || !Path.HasExtension(finalFileNameFromYtDlp)) { finalFileNameFromYtDlp = _currentDownloadingComponent; }
                    }
                    else
                    {
                        if (finalFileNameFromYtDlp == "downloaded_video" || !Path.HasExtension(finalFileNameFromYtDlp)) { finalFileNameFromYtDlp = _currentDownloadingComponent; }
                    }
                }
                progressStartedForAnyComponent = true;
            }
            else if (outputLine.Contains("has already been downloaded"))
            {
                var matchName = Regex.Match(outputLine, @"\[download\]\s+(.*?)\s+has already been downloaded");
                if (matchName.Success)
                {
                    _currentDownloadingComponent = SanitizeFileName(matchName.Groups[1].Value.Trim());
                    finalFileNameFromYtDlp = _currentDownloadingComponent;
                    FileNameTextBlock.Text = $"File: {Path.GetFileName(finalFileNameFromYtDlp)} (already exists)";
                }
                StatusTextBlock.Text = "Status: File already downloaded by yt-dlp.";
                DownloadProgressBar.IsIndeterminate = false;
                DownloadProgressBar.Maximum = 100;
                DownloadProgressBar.Value = 100;
                lastKnownPercentageForComponent = 100;
                _ytDlpCurrentComponentTotalBytes = 0;
                progressStartedForAnyComponent = true;
            }
            else if (outputLine.StartsWith("[Merger]") || outputLine.StartsWith("[ExtractAudio]") || outputLine.StartsWith("[FixupMpegts]") || outputLine.StartsWith("[FixupMfr]") || outputLine.StartsWith("[FixupStretched]"))
            {
                var matchDest = Regex.Match(outputLine, @"Destination:\s*""?([^""\n\r]+)""?|into\s*""?([^""\n\r]+)""?|in\s*""?([^""\n\r]+)""?");
                string tempName = _currentDownloadingComponent ?? finalFileNameFromYtDlp;
                if (matchDest.Groups[1].Success && !string.IsNullOrWhiteSpace(matchDest.Groups[1].Value)) { tempName = SanitizeFileName(matchDest.Groups[1].Value); }
                else if (matchDest.Groups[2].Success && !string.IsNullOrWhiteSpace(matchDest.Groups[2].Value)) { tempName = SanitizeFileName(matchDest.Groups[2].Value); }
                else if (matchDest.Groups[3].Success && !string.IsNullOrWhiteSpace(matchDest.Groups[3].Value)) { tempName = SanitizeFileName(matchDest.Groups[3].Value); }
                if (Path.HasExtension(tempName)) { finalFileNameFromYtDlp = tempName; }
                StatusTextBlock.Text = $"Status: yt-dlp - {outputLine.Split('\n')[0]}";
                FileNameTextBlock.Text = $"Processing: {Path.GetFileName(finalFileNameFromYtDlp)}";
                if (!DownloadProgressBar.IsIndeterminate) { DownloadProgressBar.IsIndeterminate = true; }
                _ytDlpCurrentComponentTotalBytes = 0;
                progressStartedForAnyComponent = true;
            }
            else
            {
                Match progressMatch = Regex.Match(outputLine, @"\[download\]\s+(?<percent>[\d\.]+?)%\s+of\s+(?:~?\s*)?(?<total_size_str>[\d\.]+[KMGT]?i?B|unknown)(?:\s+at\s+(?<speed>[\d\.]+[KMGT]?i?B/s))?(?:\s+ETA\s+(?<eta>[\d:]+))?|\[download\]\s+100%\s+of\s+(?<total_size_full_str>[\d\.]+[KMGT]?i?B)\s+in\s+[\d:]+");
                if (progressMatch.Success)
                {
                    progressStartedForAnyComponent = true;
                    double currentPercent = 0.0;
                    string totalSizeStringForDisplay;
                    if (progressMatch.Groups["percent"].Success)
                    {
                        if (!double.TryParse(progressMatch.Groups["percent"].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out currentPercent)) { return; }
                        totalSizeStringForDisplay = progressMatch.Groups["total_size_str"].Value;
                    }
                    else
                    {
                        currentPercent = 100.0;
                        totalSizeStringForDisplay = progressMatch.Groups["total_size_full_str"].Value;
                    }
                    if (_ytDlpCurrentComponentTotalBytes == -1)
                    {
                        long parsedTotalBytes = ParseYtDlpSizeStringToBytes(totalSizeStringForDisplay);
                        if (parsedTotalBytes > 0)
                        {
                            _ytDlpCurrentComponentTotalBytes = parsedTotalBytes;
                            DownloadProgressBar.Maximum = _ytDlpCurrentComponentTotalBytes;
                            DownloadProgressBar.IsIndeterminate = false;
                        }
                        else
                        {
                            _ytDlpCurrentComponentTotalBytes = 0;
                            DownloadProgressBar.Maximum = 100;
                            DownloadProgressBar.IsIndeterminate = false;
                        }
                    }
                    string speed = progressMatch.Groups["speed"].Value;
                    string eta = progressMatch.Groups["eta"].Value;
                    string componentNameDisplay = string.IsNullOrWhiteSpace(_currentDownloadingComponent) ? Path.GetFileName(finalFileNameFromYtDlp) : Path.GetFileName(_currentDownloadingComponent);
                    if (_ytDlpCurrentComponentTotalBytes > 0)
                    {
                        if (DownloadProgressBar.IsIndeterminate) { DownloadProgressBar.IsIndeterminate = false; }
                        if (DownloadProgressBar.Maximum != _ytDlpCurrentComponentTotalBytes) { DownloadProgressBar.Maximum = _ytDlpCurrentComponentTotalBytes; }
                        long currentDownloadedBytes = (long)((currentPercent / 100.0) * _ytDlpCurrentComponentTotalBytes);
                        DownloadProgressBar.Value = currentDownloadedBytes;
                        StatusTextBlock.Text = $"Downloading ({componentNameDisplay}): {currentPercent:F1}% ({FormatBytesOutput(currentDownloadedBytes)} / {FormatBytesOutput(_ytDlpCurrentComponentTotalBytes)}) | Speed: {speed} | ETA: {eta}";
                    }
                    else
                    {
                        if (DownloadProgressBar.IsIndeterminate) { DownloadProgressBar.IsIndeterminate = false; }
                        if (DownloadProgressBar.Maximum != 100) { DownloadProgressBar.Maximum = 100; }
                        DownloadProgressBar.Value = currentPercent;
                        StatusTextBlock.Text = $"Downloading ({componentNameDisplay}): {currentPercent:F1}% of {totalSizeStringForDisplay} | Speed: {speed} | ETA: {eta}";
                    }
                    lastKnownPercentageForComponent = currentPercent;
                }
            }
        }
        #endregion

        #region Google Drive & Direct File Download
        private async Task DownloadGoogleDriveFile(string url)
        {
            // Method implementation with multi-line formatting
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
            // Method implementation with multi-line formatting
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
                            StatusTextBlock.Text = "Status: Following Google Drive confirmation link...";
                            FileNameTextBlock.Text = "Google Drive: Confirming...";
                            await Task.Delay(200);
                            await DownloadDirectFile(newUrl, false);
                            return;
                        }
                        else
                        {
                            StatusTextBlock.Text = "Status: Google Drive may require confirmation or file is unavailable. Auto-link not found.";
                            FileNameTextBlock.Text = "Google Drive: Confirmation needed/Error";
                            return;
                        }
                    }
                    response.EnsureSuccessStatusCode();
                    fileName = GetFileNameFromHeaders(response, url);
                    FileNameTextBlock.Text = $"Downloading File: {fileName}";
                    string outputPath = Path.Combine(SelectedDirectory, fileName);
                    StatusTextBlock.Text = $"Status: Downloading '{fileName}'...";
                    long? totalBytes = response.Content.Headers.ContentLength;
                    int lastPercentage = -1;
                    if (DownloadProgressBar != null)
                    {
                        if (totalBytes.HasValue && totalBytes.Value > 0)
                        {
                            DownloadProgressBar.IsIndeterminate = false;
                            DownloadProgressBar.Maximum = 100;
                        }
                        else
                        {
                            DownloadProgressBar.IsIndeterminate = true;
                        }
                    }

                    using (var fileStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
                    using (var stream = await response.Content.ReadAsStreamAsync())
                    {
                        byte[] buffer = new byte[81920];
                        int bytesRead;
                        long totalBytesRead = 0;
                        if (DownloadProgressBar != null && !DownloadProgressBar.IsIndeterminate) { DownloadProgressBar.Value = 0; }
                        while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                        {
                            await fileStream.WriteAsync(buffer, 0, bytesRead);
                            totalBytesRead += bytesRead;
                            if (totalBytes.HasValue && totalBytes.Value > 0)
                            {
                                if (DownloadProgressBar != null && DownloadProgressBar.IsIndeterminate) { DownloadProgressBar.IsIndeterminate = false; }
                                double percentage = (double)totalBytesRead / totalBytes.Value * 100;
                                if ((int)percentage != lastPercentage)
                                {
                                    if (DownloadProgressBar != null) { DownloadProgressBar.Value = percentage; }
                                    StatusTextBlock.Text = $"Downloading: {percentage:F1}% of {FormatBytesOutput(totalBytes.Value)} | File: {fileName}";
                                    lastPercentage = (int)percentage;
                                }
                            }
                            else
                            {
                                if (DownloadProgressBar != null && !DownloadProgressBar.IsIndeterminate) { DownloadProgressBar.IsIndeterminate = true; }
                                StatusTextBlock.Text = $"Status: Downloading '{fileName}' ({FormatBytesOutput(totalBytesRead)})...";
                            }
                        }
                    }
                    if (DownloadProgressBar != null)
                    {
                        DownloadProgressBar.IsIndeterminate = false;
                        if (totalBytes.HasValue && totalBytes.Value > 0) { DownloadProgressBar.Value = 100; }
                    }
                    StatusTextBlock.Text = $"Status: File '{fileName}' downloaded successfully!";
                    FileNameTextBlock.Text = $"Completed: {fileName}";
                }
            }
            catch (HttpRequestException httpEx)
            {
                if (isGoogleDriveInitialAttempt && httpEx.StatusCode == System.Net.HttpStatusCode.Forbidden)
                {
                    StatusTextBlock.Text = "Status: GDrive access forbidden (403). File not public/quota issue.";
                }
                else
                {
                    StatusTextBlock.Text = $"Status: HTTP Error - {httpEx.StatusCode?.ToString() ?? httpEx.Message.Split('\n')[0]}";
                }
                FileNameTextBlock.Text = $"File: (Download Failed - {httpEx.StatusCode?.ToString() ?? "HTTP"})";
            }
            catch (Exception ex)
            {
                if (StatusTextBlock != null) { StatusTextBlock.Text = $"Status: Download Error - {ex.Message.Split('\n')[0]}."; }
                if (FileNameTextBlock.Text != null) { FileNameTextBlock.Text = "File: (Download Failed)"; }
                if (!string.IsNullOrEmpty(fileName) && fileName != "unknown_file.dat" && !string.IsNullOrEmpty(SelectedDirectory) && Directory.Exists(SelectedDirectory))
                {
                    string partialPath = Path.Combine(SelectedDirectory, fileName);
                    if (System.IO.File.Exists(partialPath))
                    {
                        try { System.IO.File.Delete(partialPath); }
                        catch { /* best effort */ }
                    }
                }
            }
        }
        #endregion
    }

    public class YouTubeQualityItem
    {
        public string Label { get; set; }
        public string FormatCode { get; set; }
        public bool IsAudioOnly { get; set; }
        public int SortPriority { get; set; }
    }
}