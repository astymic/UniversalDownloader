using System;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Ookii.Dialogs.Wpf;


namespace UniversalDownloader
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        // Core properties, fields not moved to other partial classes
        private string _selectedDirectory;
        private HttpClient _httpClient; // This is used in MainWindow.Downloads.cs too
        private bool _isAppBusy = false; // Used across different parts

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
            await SetAppBusyState(true, "Status: Initializing...");

            if (DirectoryPathTextBox != null && string.IsNullOrEmpty(_selectedDirectory))
            {
                DirectoryPathTextBox.Foreground = (Brush)FindResource("TextSecondaryBrush");
            }
            if (UrlTextBox != null && UrlTextBox.Text == "Paste URL here...")
            {
                UrlTextBox.Foreground = (Brush)FindResource("TextSecondaryBrush");
            }

            await CheckAndEnsureYtDlpExistsAsync(); // This method is in MainWindow.YtDlp.cs
            await SetAppBusyState(false);

            if (UrlTextBox != null && !string.IsNullOrWhiteSpace(UrlTextBox.Text) && UrlTextBox.Text != "Paste URL here...")
            {
                await ProcessUrlChange(UrlTextBox.Text, true); // This method is in MainWindow.Downloads.cs
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
                UseDescriptionForTitle = true
            };

            if (dialog.ShowDialog(this).GetValueOrDefault())
            {
                SelectedDirectory = dialog.SelectedPath;
            }
        }

        private async void UrlTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (YouTubeQualityComboBox == null || StatusTextBlock == null || FileNameTextBlock == null || QualitySection == null) return;
            if (_isAppBusy && StatusTextBlock != null && !StatusTextBlock.Text.Contains("Initializing...") && !StatusTextBlock.Text.Contains("Checking for")) return;

            string currentUrl = UrlTextBox.Text;
            await ProcessUrlChange(currentUrl); // ProcessUrlChange is in MainWindow.Downloads.cs
        }

        private void YouTubeQualityComboBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            ComboBox comboBox = sender as ComboBox;
            if (comboBox != null && comboBox.IsEnabled && !comboBox.IsDropDownOpen)
            {
                var toggleButton = Utilities.FindVisualChild<System.Windows.Controls.Primitives.ToggleButton>(comboBox); // Use Utilities
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

            try
            {
                if (IsYouTubeLink(url)) // In MainWindow.Downloads.cs
                {
                    if (!_isYtDlpReady) // In MainWindow.YtDlp.cs
                    {
                        StatusTextBlock.Text = $"Status: {YtDlpFileName} is not available. YouTube download aborted."; // YtDlpFileName in MainWindow.YtDlp.cs
                        await SetAppBusyState(false); return;
                    }
                    if (YouTubeQualityComboBox == null || YouTubeQualityComboBox.SelectedItem == null) { StatusTextBlock.Text = "Status: Please select a YouTube video quality."; await SetAppBusyState(false); return; }
                    var selectedQuality = YouTubeQualityComboBox.SelectedItem as YouTubeQualityItem;
                    if (selectedQuality == null) { StatusTextBlock.Text = "Status: Invalid YouTube video quality selected."; await SetAppBusyState(false); return; }
                    await DownloadYouTubeVideoWithYtDlp(url, selectedQuality.FormatCode); // In MainWindow.YtDlp.cs
                }
                else if (IsGoogleDriveLink(url)) // In MainWindow.Downloads.cs
                {
                    await DownloadGoogleDriveFile(url); // In MainWindow.Downloads.cs
                }
                else
                {
                    await DownloadDirectFile(url); // In MainWindow.Downloads.cs
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
            }
        }
    }

    // YouTubeQualityItem class can stay here or be moved to its own file if preferred.
    // For simplicity with the current refactoring, let's keep it here.
    public class YouTubeQualityItem
    {
        public string Label { get; set; }
        public string FormatCode { get; set; }
        public bool IsAudioOnly { get; set; }
        public int SortPriority { get; set; }
    }
}