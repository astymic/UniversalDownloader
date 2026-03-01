using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Ookii.Dialogs.Wpf;
using UniversalDownloader.Services;

namespace UniversalDownloader
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private string? _selectedDirectory;
        private readonly DependencyManager _dependencyManager;
        private readonly DownloadService _downloadService;

        private bool _isInitializing = false;
        private bool _isProcessingUrl = false;
        private bool _isDownloadingFile = false;

        private double _videoDurationInSeconds;
        private double _trimStartTimeInSeconds;
        private double _trimEndTimeInSeconds;
        private string _trimStartTimeText = "00:00:00";
        private string _trimEndTimeText = "00:00:00";
        private string _maxVideoTimeText = "00:00:00";
        private bool _isTrimmingEnabled = false;

        private bool _isDraggingStartThumb = false;
        private bool _isDraggingEndThumb = false;

        private CancellationTokenSource? _cancellationTokenSource;

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;

            _dependencyManager = new DependencyManager();
            _dependencyManager.ProgressUpdated += DependencyManager_ProgressUpdated;

            _downloadService = new DownloadService(_dependencyManager);
            _downloadService.ProgressChanged += DownloadService_ProgressChanged;
        }

        private void DependencyManager_ProgressUpdated(string status)
        {
            Dispatcher.InvokeAsync(() =>
            {
                if (StatusTextBlock != null) StatusTextBlock.Text = status;
            });
        }

        private void DownloadService_ProgressChanged(object? sender, DownloadProgressArgs e)
        {
            Dispatcher.InvokeAsync(() =>
            {
                if (StatusTextBlock != null && e.StatusMessage != null)
                {
                    StatusTextBlock.Text = e.StatusMessage;
                }
                
                if (FileNameTextBlock != null && e.Filename != null)
                {
                    FileNameTextBlock.Text = e.Filename;
                }

                if (DownloadProgressBar != null)
                {
                    DownloadProgressBar.IsIndeterminate = e.IsIndeterminate;
                    if (!e.IsIndeterminate)
                    {
                        DownloadProgressBar.Maximum = 100;
                        DownloadProgressBar.Value = e.Percentage;
                    }
                }
            });
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            _isInitializing = true;
            UpdateUiElementStates("Status: Initializing dependencies silently...");

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

            try
            {
                await _dependencyManager.InitializeDependenciesAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to initialize dependencies: {ex.Message}");
            }

            _isInitializing = false;
            UpdateUiElementStates();

            if (UrlTextBox != null && !string.IsNullOrWhiteSpace(UrlTextBox.Text) && UrlTextBox.Text != "Paste URL here...")
            {
                await ProcessUrlChange(UrlTextBox.Text);
            }
            else
            {
                UpdateUiElementStates(_dependencyManager.IsYtDlpReady ? "Status: Ready. Paste a URL." : "Status: YouTube features disabled (tool missing).");
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

        public double VideoDurationInSeconds
        {
            get => _videoDurationInSeconds;
            set { if (_videoDurationInSeconds != value) { _videoDurationInSeconds = value; OnPropertyChanged(nameof(VideoDurationInSeconds)); } }
        }

        public double TrimStartTimeInSeconds
        {
            get => _trimStartTimeInSeconds;
            set
            {
                if (_trimStartTimeInSeconds != value)
                {
                    _trimStartTimeInSeconds = value;
                    OnPropertyChanged(nameof(TrimStartTimeInSeconds));
                    TrimStartTimeText = SecondsToTimeString(value);
                }
            }
        }

        public double TrimEndTimeInSeconds
        {
            get => _trimEndTimeInSeconds;
            set
            {
                if (_trimEndTimeInSeconds != value)
                {
                    _trimEndTimeInSeconds = value;
                    OnPropertyChanged(nameof(TrimEndTimeInSeconds));
                    TrimEndTimeText = SecondsToTimeString(value);
                }
            }
        }

        public string TrimStartTimeText
        {
            get => _trimStartTimeText;
            set { if (_trimStartTimeText != value) { _trimStartTimeText = value; OnPropertyChanged(nameof(TrimStartTimeText)); if (StartTimeTextBox != null && !StartTimeTextBox.IsFocused) StartTimeTextBox.Text = value; } }
        }

        public string TrimEndTimeText
        {
            get => _trimEndTimeText;
            set { if (_trimEndTimeText != value) { _trimEndTimeText = value; OnPropertyChanged(nameof(TrimEndTimeText)); if (EndTimeTextBox != null && !EndTimeTextBox.IsFocused) EndTimeTextBox.Text = value; } }
        }

        public string MaxVideoTimeText
        {
            get => _maxVideoTimeText;
            set { if (_maxVideoTimeText != value) { _maxVideoTimeText = value; OnPropertyChanged(nameof(MaxVideoTimeText)); } }
        }

        public bool IsTrimmingEnabled
        {
            get => _isTrimmingEnabled;
            set { if (_isTrimmingEnabled != value) { _isTrimmingEnabled = value; OnPropertyChanged(nameof(IsTrimmingEnabled)); } }
        }

        private string SecondsToTimeString(double totalSeconds)
        {
            TimeSpan time = TimeSpan.FromSeconds(Math.Floor(totalSeconds));
            return time.ToString(@"hh\:mm\:ss", System.Globalization.CultureInfo.InvariantCulture);
        }

        private bool TimeStringToSeconds(string timeString, out double seconds)
        {
            if (string.IsNullOrWhiteSpace(timeString))
            {
                seconds = 0;
                return false;
            }

            if (timeString.Contains(":"))
            {
                if (TimeSpan.TryParse(timeString, System.Globalization.CultureInfo.InvariantCulture, out TimeSpan parsedTime))
                {
                    seconds = parsedTime.TotalSeconds;
                    return true;
                }
            }
            else
            {
                if (double.TryParse(timeString, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double parsedSeconds))
                {
                    seconds = parsedSeconds;
                    return true;
                }
            }

            seconds = 0;
            return false;
        }

        private void UpdateUiElementStates(string statusMessageUpdate = null)
        {
            Dispatcher.InvokeAsync(() =>
            {
                bool isBusy = _isInitializing || _isProcessingUrl || _isDownloadingFile;
                bool canBrowse = !isBusy;
                bool canInputUrl = !isBusy;
                bool canDownloadAction = CanInitiateDownload() && !isBusy;

                if (UrlTextBox != null) UrlTextBox.IsEnabled = canInputUrl;
                if (BrowseButton != null) BrowseButton.IsEnabled = canBrowse;

                if (CancelDownloadButton != null)
                {
                    CancelDownloadButton.Visibility = _isDownloadingFile ? Visibility.Visible : Visibility.Collapsed;
                }

                if (DownloadButton != null)
                {
                    bool youtubeSpecificConditionsMet = true;
                    if (_downloadService.IsYouTubeLink(UrlTextBox?.Text ?? string.Empty))
                    {
                        youtubeSpecificConditionsMet = _dependencyManager.IsYtDlpReady && (YouTubeQualityComboBox?.SelectedItem != null) && (QualitySection?.Visibility == Visibility.Visible);
                    }
                    else if (TrimmingSection != null && TrimmingSection.Visibility == Visibility.Visible)
                    {
                        TrimmingSection.Visibility = Visibility.Collapsed;
                    }
                    DownloadButton.IsEnabled = canDownloadAction && youtubeSpecificConditionsMet;
                }

                if (YouTubeQualityComboBox != null)
                {
                    YouTubeQualityComboBox.IsEnabled = canInputUrl && _dependencyManager.IsYtDlpReady && _downloadService.IsYouTubeLink(UrlTextBox?.Text ?? string.Empty) && YouTubeQualityComboBox.HasItems;
                }

                if (statusMessageUpdate != null && StatusTextBlock != null)
                {
                    StatusTextBlock.Text = statusMessageUpdate;
                }
                else if (StatusTextBlock != null && !isBusy)
                {
                    StatusTextBlock.Text = _dependencyManager.IsYtDlpReady ? "Status: Ready. Paste a URL." : "Status: Youtube features disabled.";
                }
            });
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
            if (_isProcessingUrl || _isDownloadingFile) return;

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
                StatusTextBlock.Text = "Status: Error processing URL.";
                FileNameTextBlock.Text = "";
                QualitySection.Visibility = Visibility.Collapsed;
                if (TrimmingSection != null) TrimmingSection.Visibility = Visibility.Collapsed;
                YouTubeQualityComboBox.ItemsSource = null;
            }
            finally
            {
                _isProcessingUrl = false;
                UpdateUiElementStates();
            }
        }

        private async Task ProcessUrlChange(string url)
        {
            YouTubeQualityComboBox.ItemsSource = null;
            QualitySection.Visibility = Visibility.Collapsed;

            if (string.IsNullOrWhiteSpace(url) || url == "Paste URL here...")
            {
                FileNameTextBlock.Text = string.Empty;
                return;
            }

            if (_downloadService.IsYouTubeLink(url))
            {
                FileNameTextBlock.Text = "Processing YouTube URL...";
                StatusTextBlock.Text = "Status: Fetching YouTube qualities...";
                
                try
                {
                    var info = await _downloadService.GetYouTubeInfoAsync(url);
                    if (info.FormatsJson != null)
                    {
                        var qualities = ExtractQualitiesFromYouTubeInfo(info.FormatsJson);
                        if (qualities.Count > 0)
                        {
                            YouTubeQualityComboBox.ItemsSource = qualities;
                            QualitySection.Visibility = Visibility.Visible;
                            YouTubeQualityComboBox.SelectedIndex = 0;
                            StatusTextBlock.Text = "Status: YouTube qualities listed. Select quality to download.";

                            try 
                            { 
                                var videoInfo = Newtonsoft.Json.Linq.JObject.Parse(info.FormatsJson);
                                double? duration = videoInfo["duration"]?.ToObject<double?>();
                                if (duration.HasValue && duration > 0)
                                {
                                    VideoDurationInSeconds = duration.Value;
                                    TrimStartTimeInSeconds = 0;
                                    TrimEndTimeInSeconds = duration.Value;
                                    MaxVideoTimeText = SecondsToTimeString(duration.Value);
                                    IsTrimmingEnabled = false; 
                                    if (EnableTrimmingCheckBox != null) EnableTrimmingCheckBox.IsChecked = false;
                                    if (TrimmingSection != null) TrimmingSection.Visibility = Visibility.Visible;
                                    if (StartTimeTextBox != null) StartTimeTextBox.Text = "00:00:00"; 
                                    if (EndTimeTextBox != null) EndTimeTextBox.Text = MaxVideoTimeText; 
                                }
                            } catch { }
                        }
                        else
                        {
                            StatusTextBlock.Text = "Status: No downloadable formats could be determined.";
                            FileNameTextBlock.Text = "No YouTube formats found.";
                        }
                    }
                }
                catch (Exception ex)
                {
                    StatusTextBlock.Text = $"Status: {ex.Message}";
                    FileNameTextBlock.Text = "YouTube Info Error";
                }
            }
            else if (_downloadService.IsKnownAudioPlatformLink(url))
            {
                string title = await _downloadService.GetTitleWithYtDlpAsync(url);
                FileNameTextBlock.Text = title ?? "Audio Platform Item";
                StatusTextBlock.Text = "Status: Ready to download audio.";
            }
            else if (_downloadService.IsGoogleDriveLink(url))
            {
                FileNameTextBlock.Text = "Google Drive link detected.";
                StatusTextBlock.Text = "Status: Ready to download Google Drive link.";
            }
            else
            {
                FileNameTextBlock.Text = "Direct Link";
                StatusTextBlock.Text = "Status: Ready to download direct file.";
            }
        }

        private System.Collections.Generic.List<YouTubeQualityItem> ExtractQualitiesFromYouTubeInfo(string jsonOutput)
        {
             var list = new System.Collections.Generic.List<YouTubeQualityItem>();
             try
             {
                 var videoInfo = Newtonsoft.Json.Linq.JObject.Parse(jsonOutput);
                 var title = videoInfo["title"]?.ToString() ?? "Unknown Video";
                 FileNameTextBlock.Text = Utilities.SanitizeFileName(title);

                 list.Add(new YouTubeQualityItem { Label = "Best Video + Best Audio", FormatCode = "bestvideo+bestaudio/best", IsAudioOnly = false, SortPriority = 100 });
                 list.Add(new YouTubeQualityItem { Label = "Best Audio Only", FormatCode = "bestaudio/best", IsAudioOnly = true, SortPriority = 50 });

                 var formats = videoInfo["formats"] as Newtonsoft.Json.Linq.JArray;
                 if (formats != null)
                 {
                     var uniqueHeights = new System.Collections.Generic.HashSet<int>();
                     foreach (var format in formats)
                     {
                         var vcodec = format["vcodec"]?.ToString();
                         var acodec = format["acodec"]?.ToString();
                         var ext = format["ext"]?.ToString();
                         if (vcodec != "none" && ext == "mp4")
                         {
                             int height = format["height"]?.ToObject<int>() ?? 0;
                             int width = format["width"]?.ToObject<int>() ?? 0;
                             
                             if (height >= 360 && uniqueHeights.Add(height))
                             {
                                 int maxDim = Math.Max(width, height);
                                 string label = $"{height}p Quality";
                                 
                                 if (maxDim >= 3840) label = "4K Quality";
                                 else if (maxDim >= 2560) label = "1440p Quality";
                                 else if (maxDim >= 1920) label = "1080p Quality";
                                 else if (maxDim >= 1280) label = "720p Quality";
                                 else if (maxDim >= 854) label = "480p Quality";
                                 else if (maxDim >= 640) label = "360p Quality";
                                 
                                 list.Add(new YouTubeQualityItem 
                                 { 
                                     Label = label, 
                                     FormatCode = $"bestvideo[height<={height}][ext=mp4]+bestaudio[ext=m4a]/best[height<={height}][ext=mp4]/best",
                                     IsAudioOnly = false, 
                                     SortPriority = height 
                                 });
                             }
                         }
                     }
                 }
                 list.Sort((x, y) => y.SortPriority.CompareTo(x.SortPriority));
             } 
             catch { }
             return list;
        }

        private void EnableTrimmingCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            if (EnableTrimmingCheckBox != null)
            {
                IsTrimmingEnabled = EnableTrimmingCheckBox.IsChecked == true;
                if (TrimControlsPanel != null)
                {
                    TrimControlsPanel.Visibility = IsTrimmingEnabled ? Visibility.Visible : Visibility.Collapsed;
                    if (IsTrimmingEnabled) UpdateSliderUI();
                }
            }
        }

        private void SliderCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateSliderUI();

        private void SliderCanvas_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (SliderCanvas == null || VideoDurationInSeconds <= 0) return;
            Point clickPoint = e.GetPosition(SliderCanvas);
            double clickedSeconds = (clickPoint.X / SliderCanvas.ActualWidth) * VideoDurationInSeconds;
            
            double distToStart = Math.Abs(clickedSeconds - TrimStartTimeInSeconds);
            double distToEnd = Math.Abs(clickedSeconds - TrimEndTimeInSeconds);

            if (distToStart < distToEnd) { TrimStartTimeInSeconds = Math.Max(0, Math.Min(clickedSeconds, TrimEndTimeInSeconds - 0.1)); }
            else { TrimEndTimeInSeconds = Math.Max(TrimStartTimeInSeconds + 0.1, Math.Min(clickedSeconds, VideoDurationInSeconds)); }
            
            UpdateSliderUI();
            e.Handled = true;
        }

        private void StartThumb_DragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
        {
            if (SliderCanvas == null || VideoDurationInSeconds <= 0) return;
            _isDraggingStartThumb = true;
            double pixelsPerSecond = SliderCanvas.ActualWidth / VideoDurationInSeconds;
            double newSeconds = TrimStartTimeInSeconds + (e.HorizontalChange / pixelsPerSecond);
            TrimStartTimeInSeconds = Math.Max(0, Math.Min(newSeconds, TrimEndTimeInSeconds - 0.1));
            UpdateSliderUI();
            _isDraggingStartThumb = false;
        }

        private void EndThumb_DragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
        {
            if (SliderCanvas == null || VideoDurationInSeconds <= 0) return;
            _isDraggingEndThumb = true;
            double pixelsPerSecond = SliderCanvas.ActualWidth / VideoDurationInSeconds;
            double newSeconds = TrimEndTimeInSeconds + (e.HorizontalChange / pixelsPerSecond);
            TrimEndTimeInSeconds = Math.Max(TrimStartTimeInSeconds + 0.1, Math.Min(newSeconds, VideoDurationInSeconds));
            UpdateSliderUI();
            _isDraggingEndThumb = false;
        }

        private void UpdateSliderUI()
        {
            if (SliderCanvas == null || StartThumb == null || EndThumb == null || SliderFill == null || SliderTrack == null) return;
            if (VideoDurationInSeconds <= 0 || SliderCanvas.ActualWidth <= 0) return;

            SliderTrack.Width = SliderCanvas.ActualWidth;
            double pixelsPerSecond = SliderCanvas.ActualWidth / VideoDurationInSeconds;
            
            double startX = TrimStartTimeInSeconds * pixelsPerSecond;
            double endX = TrimEndTimeInSeconds * pixelsPerSecond;
            
            Canvas.SetLeft(StartThumb, startX);
            Canvas.SetLeft(EndThumb, endX);
            
            Canvas.SetLeft(SliderFill, startX);
            SliderFill.Width = Math.Max(0, endX - startX);
        }

        private void TimeTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isDraggingStartThumb || _isDraggingEndThumb) return;
            TextBox tb = sender as TextBox;
            if (tb != null && TimeStringToSeconds(tb.Text, out double seconds))
            {
                if (tb == StartTimeTextBox && seconds < TrimEndTimeInSeconds) { _trimStartTimeInSeconds = seconds; OnPropertyChanged(nameof(TrimStartTimeInSeconds)); UpdateSliderUI(); }
                else if (tb == EndTimeTextBox && seconds > TrimStartTimeInSeconds && seconds <= VideoDurationInSeconds) { _trimEndTimeInSeconds = seconds; OnPropertyChanged(nameof(TrimEndTimeInSeconds)); UpdateSliderUI(); }
            }
        }

        private void TimeTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            TextBox tb = sender as TextBox;
            if (tb == StartTimeTextBox) TrimStartTimeText = SecondsToTimeString(TrimStartTimeInSeconds);
            else if (tb == EndTimeTextBox) TrimEndTimeText = SecondsToTimeString(TrimEndTimeInSeconds);
        }

        private void TimeTextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e) { if (e.Key == System.Windows.Input.Key.Enter) { TimeTextBox_LostFocus(sender, null); e.Handled = true; } }


        private void YouTubeQualityComboBox_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
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
            if (!CanInitiateDownload()) return;

            _isDownloadingFile = true;
            _cancellationTokenSource = new CancellationTokenSource();
            UpdateUiElementStates("Status: Preparing to download...");

            DownloadProgressBar.Value = 0;
            DownloadProgressBar.IsIndeterminate = true;

            try
            {
                if (_downloadService.IsYouTubeLink(url))
                {
                    var selectedQuality = YouTubeQualityComboBox.SelectedItem as YouTubeQualityItem;
                    if (selectedQuality != null && selectedQuality.FormatCode != "audio_only")
                    {
                        await _downloadService.DownloadWithYtDlpAsync(url, selectedQuality.FormatCode, Downloader.App.AppTempDirectory, SelectedDirectory, selectedQuality.IsAudioOnly, "best", IsTrimmingEnabled, TrimStartTimeInSeconds, TrimEndTimeInSeconds, _cancellationTokenSource.Token);
                    }
                    else
                    {
                        await _downloadService.DownloadWithYtDlpAsync(url, "bestaudio/best", Downloader.App.AppTempDirectory, SelectedDirectory, true, "mp3", false, 0, 0, _cancellationTokenSource.Token);
                    }
                }
                else if (_downloadService.IsKnownAudioPlatformLink(url))
                {
                    await _downloadService.DownloadWithYtDlpAsync(url, "bestaudio/best", Downloader.App.AppTempDirectory, SelectedDirectory, true, "mp3", false, 0, 0, _cancellationTokenSource.Token);
                }
                else if (_downloadService.IsGoogleDriveLink(url))
                {
                    string fileId = null;
                    var matchFileD = System.Text.RegularExpressions.Regex.Match(url, @"/file/d/([a-zA-Z0-9_-]+)");
                    if (matchFileD.Success) fileId = matchFileD.Groups[1].Value;
                    string directDownloadUrl = $"https://drive.google.com/uc?export=download&confirm=t&id={fileId}";
                    await _downloadService.DownloadDirectFileAsync(directDownloadUrl, Downloader.App.AppTempDirectory, SelectedDirectory, _cancellationTokenSource.Token);
                }
                else
                {
                    await _downloadService.DownloadDirectFileAsync(url, Downloader.App.AppTempDirectory, SelectedDirectory, _cancellationTokenSource.Token);
                }
            }
            catch (OperationCanceledException)
            {
                StatusTextBlock.Text = "Status: Download canceled by user.";
                // We do NOT change the FileNameTextBlock to "Download Canceled"
                // so that it doesn't get stuck if they click download again immediately.
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"Status: Error - {ex.Message.Split('\n')[0]}.";
                FileNameTextBlock.Text = "Download Failed";
            }
            finally
            {
                _isDownloadingFile = false;
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;

                DownloadProgressBar.IsIndeterminate = false;
                UpdateUiElementStates();
            }
        }

        private void CancelDownloadButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isDownloadingFile)
            {
                StatusTextBlock.Text = "Status: Canceling download...";
                FileNameTextBlock.Text = "Canceling...";
                _cancellationTokenSource?.Cancel();
            }
        }
    }

    public class YouTubeQualityItem
    {
        public required string Label { get; set; }
        public required string FormatCode { get; set; }
        public bool IsAudioOnly { get; set; }
        public int SortPriority { get; set; }
    }
}