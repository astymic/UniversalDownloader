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
        // Core properties, fields not moved to other partial classes
        private string _selectedDirectory;
        private HttpClient _httpClient; 
        private bool _isAppBusy = false;

        private const string SettingsKeyLastDownloadPath = "LastDownloadPath";


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

            // _ytDlpExecutablePath is initialized in MainWindow.YtDlp.cs via its constructor logic, but path defined here
            _ytDlpExecutablePath = Path.Combine(AppContext.BaseDirectory, YtDlpFileName); // YtDlpFileName is in MainWindow.YtDlp.cs
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            LoadSettings(); 

            await SetAppBusyState(true, "Status: Initializing...");

            if (DirectoryPathTextBox != null && string.IsNullOrEmpty(SelectedDirectory)) // Check SelectedDirectory property
            {
                DirectoryPathTextBox.Foreground = (Brush)FindResource("TextSecondaryBrush");
                DirectoryPathTextBox.Text = "No directory selected"; // Ensure placeholder if still null/empty
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
                    UpdateDownloadButtonState();
                    SaveSetting(SettingsKeyLastDownloadPath, value); 
                }
            }
        }

        private void LoadSettings()
        {
            string settingsFile = GetSettingsFilePath(); // This method is in MainWindow.YtDlp.cs, consider moving it to Utilities or this file
            if (File.Exists(settingsFile))
            {
                try
                {
                    JObject settings = JObject.Parse(File.ReadAllText(settingsFile));
                    if (settings.TryGetValue(SettingsKeyLastDownloadPath, out JToken pathToken))
                    {
                        string savedPath = pathToken.ToString();
                        if (Directory.Exists(savedPath)) // Check if saved path is still valid
                        {
                            SelectedDirectory = savedPath; // Use the property to trigger UI updates and saving
                        }
                        else
                        {
                            SelectedDirectory = null; // Path invalid, reset
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error loading settings: {ex.Message}");
                    SelectedDirectory = null; // Reset on error
                }
            }
        }

        private string GetSettingsFilePath()
        {
            string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string appFolder = Path.Combine(appDataPath, "UniversalDownloader");
            Directory.CreateDirectory(appFolder); // Ensure it exists
            return Path.Combine(appFolder, "settings.json");
        }

        private void SaveSetting(string key, string value)
        {
            string settingsFile = GetSettingsFilePath();
            JObject settings;
            if (File.Exists(settingsFile))
            {
                try
                {
                    settings = JObject.Parse(File.ReadAllText(settingsFile));
                }
                catch { settings = new JObject(); /* Corrupted file, start fresh */ }
            }
            else
            {
                settings = new JObject();
            }
            settings[key] = value;
            try
            {
                File.WriteAllText(settingsFile, settings.ToString(Newtonsoft.Json.Formatting.Indented));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to write setting '{key}': {ex.Message}");
            }
        }

        private bool IsFirstRunToday(string settingKey)
        {
            string settingsFile = GetSettingsFilePath();
            JObject settings;
            if (File.Exists(settingsFile))
            {
                try
                {
                    settings = JObject.Parse(File.ReadAllText(settingsFile));
                }
                catch { settings = new JObject(); }
            }
            else
            {
                settings = new JObject();
            }

            string lastRunDateKey = $"{settingKey}_LastCheckDate"; // More specific key for the date
            if (settings.TryGetValue(lastRunDateKey, out JToken lastRunToken))
            {
                if (DateTime.TryParse(lastRunToken.ToString(), out DateTime lastRunDate))
                {
                    return lastRunDate.Date < DateTime.UtcNow.Date;
                }
            }
            return true;
        }

        private void SetLastRunTimestamp(string settingKey)
        {
            string settingsFile = GetSettingsFilePath();
            JObject settings;
            if (File.Exists(settingsFile))
            {
                try
                {
                    settings = JObject.Parse(File.ReadAllText(settingsFile));
                }
                catch { settings = new JObject(); }
            }
            else
            {
                settings = new JObject();
            }
            string lastRunDateKey = $"{settingKey}_LastCheckDate";
            settings[lastRunDateKey] = DateTime.UtcNow.ToString("o"); // ISO 8601 format for the date
            try
            {
                File.WriteAllText(settingsFile, settings.ToString(Newtonsoft.Json.Formatting.Indented));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to write timestamp for '{settingKey}': {ex.Message}");
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
            if (string.IsNullOrWhiteSpace(SelectedDirectory) || !Directory.Exists(SelectedDirectory)) // Check property
            {
                return false;
            }
            return true;
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


        // UI Event Handlers directly tied to simple input or actions
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
            if (_isAppBusy) return;
            var dialog = new VistaFolderBrowserDialog
            {
                Description = "Select a folder to download files into",
                UseDescriptionForTitle = true,
                SelectedPath = Directory.Exists(SelectedDirectory) ? SelectedDirectory : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) // Start from current or default
            };

            if (dialog.ShowDialog(this).GetValueOrDefault())
            {
                SelectedDirectory = dialog.SelectedPath; // Property setter handles UI and saving
            }
        }

        private async void UrlTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (YouTubeQualityComboBox == null || StatusTextBlock == null || FileNameTextBlock == null || QualitySection == null) return;
            if (_isAppBusy && StatusTextBlock != null && !StatusTextBlock.Text.Contains("Initializing...") && !StatusTextBlock.Text.Contains("Checking for")) return;

            string currentUrl = UrlTextBox.Text;
            await ProcessUrlChange(currentUrl);
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
            UpdateDownloadButtonState();
        }

        private async void DownloadButton_Click(object sender, RoutedEventArgs e)
        {
            if (StatusTextBlock == null || FileNameTextBlock == null || DownloadProgressBar == null || UrlTextBox == null) return;

            string url = UrlTextBox.Text;
            if (!CanInitiateDownload())
            {
                StatusTextBlock.Text = "Status: Please enter a valid URL and select an existing download directory.";
                return;
            }

            await SetAppBusyState(true, "Status: Preparing to download...");
            if (DownloadProgressBar != null) { DownloadProgressBar.Value = 0; DownloadProgressBar.IsIndeterminate = true; }

            string tempDownloadFolderPath = CreateTempDownloadFolder();
            if (string.IsNullOrEmpty(tempDownloadFolderPath))
            {
                StatusTextBlock.Text = "Status: Could not create temporary download folder.";
                await SetAppBusyState(false);
                return;
            }

            try
            {
                if (IsYouTubeLink(url))
                {
                    if (!_isYtDlpReady)
                    {
                        StatusTextBlock.Text = $"Status: {YtDlpFileName} is not available. YouTube download aborted.";
                        await SetAppBusyState(false); return;
                    }
                    if (YouTubeQualityComboBox == null || YouTubeQualityComboBox.SelectedItem == null) { StatusTextBlock.Text = "Status: Please select a YouTube video quality."; await SetAppBusyState(false); return; }
                    var selectedQuality = YouTubeQualityComboBox.SelectedItem as YouTubeQualityItem;
                    if (selectedQuality == null) { StatusTextBlock.Text = "Status: Invalid YouTube video quality selected."; await SetAppBusyState(false); return; }

                    // Pass tempDownloadFolderPath to the yt-dlp download method
                    await DownloadYouTubeVideoWithYtDlp(url, selectedQuality.FormatCode, tempDownloadFolderPath);
                }
                else if (IsGoogleDriveLink(url))
                {
                    await DownloadGoogleDriveFile(url, tempDownloadFolderPath);
                }
                else
                {
                    await DownloadDirectFile(url, tempDownloadFolderPath);
                }
            }
            catch (Exception ex)
            {
                if (StatusTextBlock != null) StatusTextBlock.Text = $"Status: Critical Download Error - {ex.Message.Split('\n')[0]}.";
                if (FileNameTextBlock != null) FileNameTextBlock.Text = "File: (Download Failed)";
            }
            finally
            {
                if (DownloadProgressBar != null && DownloadProgressBar.IsIndeterminate) DownloadProgressBar.IsIndeterminate = false;
                await SetAppBusyState(false);

                // Smart cleanup: only clean if no "File Move Error" status is present,
                // or always clean. This is a UX decision.
                bool wasMoveError = StatusTextBlock.Text.Contains("File Move Error") || StatusTextBlock.Text.Contains("failed to move");
                if (!wasMoveError) // Or some other condition to decide if cleanup is safe/desired
                {
                    //CleanUpTempFolder(tempDownloadFolderPath);
                }
                else
                {
                    Debug.WriteLine($"Skipping cleanup of {tempDownloadFolderPath} due to potential move error. File might be there.");
                    // Optionally inform user:
                    // StatusTextBlock.Text += $" File may be in {tempDownloadFolderPath}.";
                }
                //if (DownloadProgressBar != null && DownloadProgressBar.IsIndeterminate) DownloadProgressBar.IsIndeterminate = false;
                //await SetAppBusyState(false);
                // CleanUpTempFolder(tempDownloadFolderPath); // Optionally cleanup, or leave for user to see partials if desired
            }
        }

        private string CreateTempDownloadFolder()
        {
            try
            {
                string baseTempPath = Path.GetTempPath(); // AppData\Local\Temp
                string appTempFolder = Path.Combine(baseTempPath, "UniversalDownloader_TempDownloads");
                Directory.CreateDirectory(appTempFolder); // Ensure base app temp folder exists

                string uniqueDownloadFolder = Path.Combine(appTempFolder, Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(uniqueDownloadFolder);
                return uniqueDownloadFolder;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error creating temp download folder: {ex.Message}");
                return null;
            }
        }

        private void CleanUpTempFolder(string tempFolderPath)
        {
            if (string.IsNullOrEmpty(tempFolderPath) || !Directory.Exists(tempFolderPath))
            {
                return;
            }
            try
            {
                Directory.Delete(tempFolderPath, true); // true for recursive delete
                Debug.WriteLine($"Cleaned up temp folder: {tempFolderPath}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error cleaning up temp folder {tempFolderPath}: {ex.Message}");
                // Log or handle as needed; often, it's okay if cleanup fails silently for temp files
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