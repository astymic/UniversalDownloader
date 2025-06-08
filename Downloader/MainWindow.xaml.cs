using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Newtonsoft.Json.Linq;
using Ookii.Dialogs.Wpf;


namespace UniversalDownloader
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        // Core properties
        private string _selectedDirectory;
        private HttpClient _httpClient;

        // Specific Busy Flags 
        private bool _isInitializing = false;
        private bool _isProcessingUrl = false;
        private bool _isManagingYtDlp = false;
        private bool _isDownloadingFile = false;

        private const string SettingsKeyLastDownloadPath = "LastDownloadPath";

        private CancellationTokenSource _cancellationTokenSource;
        private Process _currentYtDlpProcess;

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
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            _isInitializing = true;
            UpdateUiElementStates("Status: Initializing...");

            LoadSettings();

            if (DirectoryPathTextBox != null && string.IsNullOrEmpty(SelectedDirectory))
            {
                DirectoryPathTextBox.Foreground = (Brush)FindResource("TextSecondaryBrush");
                DirectoryPathTextBox.Text = "No directory selected";
            }

            if (UrlTextBox != null && UrlTextBox.Text == "Paste URL here...")
            {
                UrlTextBox.Foreground = (Brush)FindResource("TextSecondaryBrush");
            }

            await CheckAndEnsureYtDlpExistsAsync();

            _isInitializing = false;
            UpdateUiElementStates();

            if (UrlTextBox != null && !string.IsNullOrWhiteSpace(UrlTextBox.Text) && UrlTextBox.Text != "Paste URL here...")
            {
                await ProcessUrlChange(UrlTextBox.Text, true);
            }
            else
            {
                UpdateUiElementStates(_isYtDlpReady ? "Status: Ready. Paste a URL." : $"Status: {YtDlpFileName} not ready. YouTube features disabled.");
            }
        }

        public string SelectedDirectory
        {
            get => _selectedDirectory;
            set
            {
                if (_selectedDirectory != value)
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
                    UpdateUiElementStates();
                    _ = SaveSettingAsync(SettingsKeyLastDownloadPath, value);
                }
            }
        }

        private string GetSettingsFilePath()
        {
            string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string appFolder = Path.Combine(appDataPath, "UniversalDownloader");
            Directory.CreateDirectory(appFolder);
            return Path.Combine(appFolder, "settings.json");
        }

        private void LoadSettings()
        {
            string settingsFile = GetSettingsFilePath();
            if (File.Exists(settingsFile))
            {
                try
                {
                    JObject settings = JObject.Parse(File.ReadAllText(settingsFile));
                    if (settings.TryGetValue(SettingsKeyLastDownloadPath, out JToken pathToken))
                    {
                        string savedPath = pathToken.ToString();
                        if (Directory.Exists(savedPath)) SelectedDirectory = savedPath;
                        else SelectedDirectory = null;
                    }
                }
                catch (Exception ex) { Debug.WriteLine($"Error loading settings: {ex.Message}"); SelectedDirectory = null; }
            }
        }

        private async Task SaveSettingAsync(string key, string value)
        {
            try
            {
                string settingsFile = GetSettingsFilePath(); JObject settings;
                if (File.Exists(settingsFile))
                {
                    try
                    {
                        string jsonContent = await Task.Run(() => File.ReadAllText(settingsFile));
                        settings = JObject.Parse(jsonContent);
                    }
                    catch { settings = new JObject(); }
                }
                else { settings = new JObject(); }
                settings[key] = value;
                string outputJson = settings.ToString(Newtonsoft.Json.Formatting.Indented);
                await Task.Run(() => File.WriteAllText(settingsFile, outputJson));
                Debug.WriteLine($"Setting '{key}' saved asynchronously.");
            }
            catch (Exception ex) { Debug.WriteLine($"Failed to write setting '{key}' asynchronously: {ex.Message}"); }
        }

        private void SaveSetting(string key, string value)
        {
            string settingsFile = GetSettingsFilePath(); JObject settings;
            if (File.Exists(settingsFile))
            {
                try { settings = JObject.Parse(File.ReadAllText(settingsFile)); } catch { settings = new JObject(); }
            }
            else { settings = new JObject(); }
            settings[key] = value;
            try { File.WriteAllText(settingsFile, settings.ToString(Newtonsoft.Json.Formatting.Indented)); }
            catch (Exception ex) { Debug.WriteLine($"Failed to write setting '{key}': {ex.Message}"); }
        }

        private bool IsFirstRunToday(string settingKey)
        {
            string settingsFile = GetSettingsFilePath(); JObject settings;
            if (File.Exists(settingsFile))
            {
                try { settings = JObject.Parse(File.ReadAllText(settingsFile)); } catch { settings = new JObject(); }
            }
            else { settings = new JObject(); }
            string lastRunDateKey = $"{settingKey}_LastCheckDate";
            if (settings.TryGetValue(lastRunDateKey, out JToken lastRunToken))
            {
                if (DateTime.TryParse(lastRunToken.ToString(), out DateTime lastRunDate)) { return lastRunDate.Date < DateTime.UtcNow.Date; }
            }
            return true;
        }

        private void SetLastRunTimestamp(string settingKey)
        {
            string lastRunDateKey = $"{settingKey}_LastCheckDate";
            SaveSetting(lastRunDateKey, DateTime.UtcNow.ToString("o"));
        }


        // UI state updater
        private void UpdateUiElementStates(string statusMessageUpdate = null)
        {
            Dispatcher.InvokeAsync(() =>
            {
                bool canBrowse = !_isInitializing && !_isDownloadingFile;
                bool canInputUrl = !_isInitializing && !_isProcessingUrl && !_isManagingYtDlp && !_isDownloadingFile;
                bool canDownloadAction = CanInitiateDownload() && !_isInitializing && !_isProcessingUrl && !_isManagingYtDlp && !_isDownloadingFile;

                if (UrlTextBox != null) UrlTextBox.IsEnabled = canInputUrl;
                if (BrowseButton != null) BrowseButton.IsEnabled = canBrowse;

                if (CancelDownloadButton != null)
                {
                    CancelDownloadButton.Visibility = (_isDownloadingFile || (_isManagingYtDlp && FileNameTextBlock.Text.Contains("Downloading:")))
                                                      ? Visibility.Visible
                                                      : Visibility.Collapsed;

                    if (DownloadButton != null)
                    {
                        bool youtubeSpecificConditionsMet = true;
                        if (IsYouTubeLink(UrlTextBox?.Text ?? string.Empty))
                        {
                            youtubeSpecificConditionsMet = _isYtDlpReady && (YouTubeQualityComboBox?.SelectedItem != null) && (QualitySection?.Visibility == Visibility.Visible);
                        }
                        DownloadButton.IsEnabled = canDownloadAction && youtubeSpecificConditionsMet;
                    }
                }

                if (YouTubeQualityComboBox != null)
                {
                    YouTubeQualityComboBox.IsEnabled = canInputUrl && _isYtDlpReady && IsYouTubeLink(UrlTextBox?.Text ?? string.Empty) && YouTubeQualityComboBox.HasItems;
                }

                if (statusMessageUpdate != null && StatusTextBlock != null)
                {
                    StatusTextBlock.Text = statusMessageUpdate;
                }
                else if (StatusTextBlock != null && !IsAnyOperationInProgress())
                {
                    StatusTextBlock.Text = _isYtDlpReady ? "Status: Ready. Paste a URL." : $"Status: {YtDlpFileName} not ready. YouTube features disabled.";
                }

            });
        }

        private bool IsAnyOperationInProgress()
        {
            return _isInitializing || _isProcessingUrl || _isManagingYtDlp || _isDownloadingFile;
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


        // UI Event Handlers 
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
            var dialog = new VistaFolderBrowserDialog
            {
                Description = "Select a folder to download files into",
                UseDescriptionForTitle = true,
                SelectedPath = Directory.Exists(SelectedDirectory) ? SelectedDirectory : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            };

            if (dialog.ShowDialog(this).GetValueOrDefault())
            {
                SelectedDirectory = dialog.SelectedPath;
            }
        }

        private async void UrlTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (YouTubeQualityComboBox == null || StatusTextBlock == null || FileNameTextBlock == null || QualitySection == null) return;
            if (_isProcessingUrl || _isDownloadingFile || _isManagingYtDlp) return;

            _isProcessingUrl = true;
            UpdateUiElementStates("Status: Processing URL...");

            string currentUrl = UrlTextBox.Text;
            try
            {
                await ProcessUrlChange(currentUrl);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error during ProcessUrlChange: {ex}");
                if (StatusTextBlock != null) StatusTextBlock.Text = "Status: Error processing URL.";
                if (FileNameTextBlock != null) FileNameTextBlock.Text = "";
                if (QualitySection != null) QualitySection.Visibility = Visibility.Collapsed;
                if (TrimmingSection != null) TrimmingSection.Visibility = Visibility.Collapsed;
                if (YouTubeQualityComboBox != null) YouTubeQualityComboBox.ItemsSource = null;
            }
            finally
            {
                _isProcessingUrl = false;
                UpdateUiElementStates();
            }
        }

        private void YouTubeQualityComboBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            ComboBox comboBox = sender as ComboBox;
            if (comboBox != null && comboBox.IsEnabled && !comboBox.IsDropDownOpen)
            {
                var toggleButton = Utilities.FindVisualChild<System.Windows.Controls.Primitives.ToggleButton>(comboBox);
                bool clickIsOnToggleButtonOrChild = false;
                if (toggleButton != null)
                {
                    DependencyObject current = e.OriginalSource as DependencyObject;
                    while (current != null && current != comboBox) { if (current == toggleButton) { clickIsOnToggleButtonOrChild = true; break; } current = VisualTreeHelper.GetParent(current); }
                }
                if (!clickIsOnToggleButtonOrChild) comboBox.IsDropDownOpen = true;
            }
        }

        private void YouTubeQualityComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateUiElementStates();
        }

        private async void DownloadButton_Click(object sender, RoutedEventArgs e)
        {
            if (StatusTextBlock == null || FileNameTextBlock == null || DownloadProgressBar == null || UrlTextBox == null) return;

            string url = UrlTextBox.Text;
            if (!CanInitiateDownload() || (IsYouTubeLink(url) && !IsKnownAudioPlatformLink(url) && YouTubeQualityComboBox.SelectedItem == null))
            {
                UpdateUiElementStates("Status: Please enter a valid URL, select directory, and quality (if applicable).");
                return;
            }

            _isDownloadingFile = true;
            _cancellationTokenSource = new CancellationTokenSource();
            UpdateUiElementStates("Status: Preparing to download...");

            if (DownloadProgressBar != null)
            {
                DownloadProgressBar.Value = 0;
                DownloadProgressBar.IsIndeterminate = true;
            }

            string tempDownloadFolderPath = CreateTempDownloadFolder();
            if (string.IsNullOrEmpty(tempDownloadFolderPath))
            {
                _isDownloadingFile = false;
                UpdateUiElementStates("Status: Could not create temp folder.");
                return;
            }

            try
            {
                if (IsYouTubeLink(url))
                {
                    if (!_isYtDlpReady)
                    {
                        _isDownloadingFile = false;
                        UpdateUiElementStates($"Status: {YtDlpFileName} is not available. YouTube download aborted.");
                        return;
                    }
                    var selectedQuality = YouTubeQualityComboBox.SelectedItem as YouTubeQualityItem;
                    await DownloadWithYtDlpAsync(url, selectedQuality.FormatCode, tempDownloadFolderPath, _cancellationTokenSource.Token, extractAudio: selectedQuality.IsAudioOnly);
                }
                else if (IsKnownAudioPlatformLink(url))
                {
                    if (!_isYtDlpReady) { _isDownloadingFile = false; UpdateUiElementStates($"Status: {YtDlpFileName} is not available."); return; }
                    await DownloadWithYtDlpAsync(url, "bestaudio/best", tempDownloadFolderPath, _cancellationTokenSource.Token, extractAudio: true, audioFormat: "mp3");
                }
                else if (IsGoogleDriveLink(url))
                {
                    await DownloadGoogleDriveFile(url, tempDownloadFolderPath, _cancellationTokenSource.Token);
                }
                else
                {
                    await DownloadDirectFile(url, tempDownloadFolderPath, _cancellationTokenSource.Token);
                }
            }
            catch (OperationCanceledException)
            {
                if (StatusTextBlock != null) StatusTextBlock.Text = "Status: Download canceled by user.";
                if (FileNameTextBlock != null) FileNameTextBlock.Text = "Download Canceled";
                Debug.WriteLine("Download operation was canceled.");
            }
            catch (Exception ex)
            {
                if (StatusTextBlock != null) StatusTextBlock.Text = $"Status: Critical Download Error - {ex.Message.Split('\n')[0]}.";
                if (FileNameTextBlock != null) FileNameTextBlock.Text = "File: (Download Failed)";
                Debug.WriteLine($"DownloadButton_Click Critical Error: {ex.ToString()}");
            }
            finally
            {
                _isDownloadingFile = false;
                _currentYtDlpProcess = null;
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;

                if (DownloadProgressBar != null && DownloadProgressBar.IsIndeterminate) DownloadProgressBar.IsIndeterminate = false;
                UpdateUiElementStates();

                bool wasMoveError = StatusTextBlock.Text.Contains("File Move Error") || StatusTextBlock.Text.Contains("failed to move");
                bool wasDownloadError = StatusTextBlock.Text.Contains("Download Failed") || StatusTextBlock.Text.Contains("download aborted") || StatusTextBlock.Text.Contains("download corrupted");

                if (!wasMoveError || wasDownloadError)
                {
                    CleanUpTempFolder(tempDownloadFolderPath);
                }
                else
                {
                    Debug.WriteLine($"Skipping cleanup of {tempDownloadFolderPath} due to potential move error. File might be there.");
                    if (StatusTextBlock != null) StatusTextBlock.Text += $" File may be in {tempDownloadFolderPath}.";
                }
            }
        }

        private void CancelDownloadButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isDownloadingFile)
            {
                StatusTextBlock.Text = "Status: Canceling download...";
                FileNameTextBlock.Text = "Canceling...";

                _cancellationTokenSource?.Cancel();

                if (_currentYtDlpProcess != null && !_currentYtDlpProcess.HasExited)
                {
                    try
                    {
                        _currentYtDlpProcess.Kill(true);
                        Debug.WriteLine("yt-dlp process killed for cancellation.");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error killing yt-dlp process: {ex.Message}");
                    }
                    _currentYtDlpProcess = null;
                }
            }
            else if (_isManagingYtDlp && FileNameTextBlock.Text.Contains("Downloading:"))
            {
                StatusTextBlock.Text = "Status: Canceling yt-dlp download...";
                Debug.WriteLine("Cancellation for yt-dlp executable download is not fully implemented via this button yet.");
            }
        }

        private string CreateTempDownloadFolder()
        {
            try
            {
                string baseTempPath = Path.GetTempPath();
                string appTempFolder = Path.Combine(baseTempPath, "UniversalDownloader_TempDownloads");
                Directory.CreateDirectory(appTempFolder);
                string uniqueDownloadFolder = Path.Combine(appTempFolder, Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(uniqueDownloadFolder);
                return uniqueDownloadFolder;
            }
            catch (Exception ex) { Debug.WriteLine($"Error creating temp download folder: {ex.Message}"); return null; }
        }

        private void CleanUpTempFolder(string tempFolderPath)
        {
            if (string.IsNullOrEmpty(tempFolderPath) || !Directory.Exists(tempFolderPath)) return;
            try
            {
                Directory.Delete(tempFolderPath, true);
                Debug.WriteLine($"Cleaned up temp folder: {tempFolderPath}");
            }
            catch (Exception ex) { Debug.WriteLine($"Error cleaning up temp folder {tempFolderPath}: {ex.Message}"); }
        }

        private void StartTimeTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            var textBox = sender as TextBox;
            if (textBox == null) return;

            if (TimeStringToSeconds(textBox.Text, out double newStartTime))
            {
                if (newStartTime > TrimEndTimeInSeconds)
                {
                    TrimStartTimeInSeconds = TrimEndTimeInSeconds;
                }
                else
                {
                    TrimStartTimeInSeconds = newStartTime;
                }
            }
            else
            {
                // Revert to last valid value if parsing fails
                textBox.Text = SecondsToTimeString(TrimStartTimeInSeconds);
            }
        }

        private void EndTimeTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            var textBox = sender as TextBox;
            if (textBox == null) return;

            if (TimeStringToSeconds(textBox.Text, out double newEndTime))
            {
                if (newEndTime < TrimStartTimeInSeconds)
                {
                    TrimEndTimeInSeconds = TrimStartTimeInSeconds;
                }
                else if (newEndTime > VideoDurationInSeconds)
                {
                    TrimEndTimeInSeconds = VideoDurationInSeconds;
                }
                else
                {
                    TrimEndTimeInSeconds = newEndTime;
                }
            }
            else
            {
                // Revert to last valid value if parsing fails
                textBox.Text = SecondsToTimeString(TrimEndTimeInSeconds);
            }
        }
    }

    public class YouTubeQualityItem
    {
        public string Label { get; set; }
        public string FormatCode { get; set; }
        public bool IsAudioOnly { get; set; }
        public int SortPriority { get; set; }
    }
}