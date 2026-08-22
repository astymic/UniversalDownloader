using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
using UniversalDownloader.Models;
using System.Text;
using System.Windows.Media.Animation;
using System.Windows.Input;
using System.Linq;

namespace UniversalDownloader
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private string? _selectedDirectory;
        private readonly DependencyManager _dependencyManager;
        private readonly DownloadService _downloadService;
        private readonly MetadataService _metadataService;
        private readonly HistoryService _historyService;
        private readonly TikTokExtractor _tikTokExtractor;
        private readonly ClipboardMonitorService _clipboardMonitor;
        private readonly DownloadQueueManager _queueManager;

        public ObservableCollection<DownloadHistoryItem> HistoryItems => _historyService.Items;

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
        private string _currentItemTitle = "";
        private ObservableCollection<PlaylistVideoItem> _playlistItems = new ObservableCollection<PlaylistVideoItem>();
        private bool _isPlaylistMode = false;

        private List<PlaylistVideoItem> _spotifyCsvPlaylistItems = new List<PlaylistVideoItem>();
        private string _spotifyCsvPlaylistName = "";
        private bool _isSpotifyDrawerExpanded = false;
        private ObservableCollection<SpotifyImportedPlaylist> _spotifyImports = new ObservableCollection<SpotifyImportedPlaylist>();

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
            _downloadService.FileDownloaded += OnFileDownloaded;

            _metadataService = new MetadataService();
            _historyService = new HistoryService();
            _tikTokExtractor = new TikTokExtractor();
            _clipboardMonitor = new ClipboardMonitorService();
            _queueManager = new DownloadQueueManager(_downloadService, _historyService);

            _clipboardMonitor.MediaUrlDetected += OnClipboardMediaUrlDetected;
            _clipboardMonitor.Start();

            InitializeTrayIcon();
        }

        private async void OnFileDownloaded(string filePath, string sourceUrl)
        {
            try
            {
                if (!File.Exists(filePath)) return;

                var fileInfo = new FileInfo(filePath);
                string ext = fileInfo.Extension.ToLower();
                bool isAudio = ext == ".mp3" || ext == ".m4a" || ext == ".flac" || ext == ".wav" || ext == ".ogg" || ext == ".aac";

                // Metadata injection if enabled
                if (EmbedMetadataEnabled && isAudio && !string.IsNullOrWhiteSpace(_currentItemTitle))
                {
                    string title = _currentItemTitle;
                    string? artist = null;
                    if (title.Contains(" - "))
                    {
                        var parts = title.Split(new[] { " - " }, 2, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length == 2)
                        {
                            artist = parts[0].Trim();
                            title = parts[1].Trim();
                        }
                    }

                    await _metadataService.ApplyAudioMetadataAsync(filePath, title, artist);
                }

                // Determine platform name for history badge
                string platform = "Media";
                if (_downloadService.IsYouTubeLink(sourceUrl) || sourceUrl.StartsWith("ytsearch", StringComparison.OrdinalIgnoreCase)) platform = "YouTube";
                else if (_downloadService.IsSpotifyLink(sourceUrl)) platform = "Spotify";
                else if (_downloadService.IsInstagramLink(sourceUrl)) platform = "Instagram";
                else if (_downloadService.IsTikTokLink(sourceUrl)) platform = "TikTok";
                else if (_downloadService.IsTwitterLink(sourceUrl)) platform = "Twitter/X";
                else if (_downloadService.IsRedditLink(sourceUrl)) platform = "Reddit";
                else if (_downloadService.IsSoundCloudLink(sourceUrl)) platform = "SoundCloud";
                else if (_downloadService.IsGoogleDriveLink(sourceUrl)) platform = "Google Drive";

                var historyItem = new DownloadHistoryItem
                {
                    Title = !string.IsNullOrWhiteSpace(_currentItemTitle) ? _currentItemTitle : Path.GetFileNameWithoutExtension(filePath),
                    Url = sourceUrl,
                    Platform = platform,
                    FilePath = filePath,
                    FileSizeBytes = fileInfo.Length,
                    FormattedSize = Utilities.FormatBytesOutput(fileInfo.Length),
                    DownloadDate = DateTime.Now,
                    IsAudio = isAudio,
                    FormatExtension = ext.TrimStart('.')
                };

                await _historyService.AddItemAsync(historyItem);

                // Show notification if minimized or not active
                if (WindowState == WindowState.Minimized || !IsActive)
                {
                    ShowTrayNotification("Download Completed", $"{historyItem.Title} ({historyItem.FormattedSize})");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to post-process downloaded file '{filePath}': {ex.Message}");
            }
        }

        private void OnClipboardMediaUrlDetected(string url)
        {
            Dispatcher.InvokeAsync(async () =>
            {
                if (AutoDetectClipboardEnabled && UrlTextBox != null)
                {
                    string current = UrlTextBox.Text.Trim();
                    if (string.IsNullOrWhiteSpace(current) || current == "Paste URL here..." || current != url)
                    {
                        UrlTextBox.Text = url;
                        UrlTextBox.Foreground = (Brush)FindResource("TextPrimaryBrush");
                        await ProcessUrlChange(url);
                    }
                }
            });
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
                    FileNameTextBlock.Visibility = string.IsNullOrEmpty(e.Filename) ? Visibility.Collapsed : Visibility.Visible;
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
                UpdateUiElementStates(_dependencyManager.IsYtDlpReady ? "Ready. Paste a URL to get started." : "YouTube features unavailable — yt-dlp missing.");
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

                if (UrlTextBox != null) UrlTextBox.IsEnabled = canInputUrl && !_isSpotifyDrawerExpanded;
                if (BrowseButton != null) BrowseButton.IsEnabled = canBrowse;

                if (CancelDownloadButton != null)
                {
                    CancelDownloadButton.Visibility = _isDownloadingFile ? Visibility.Visible : Visibility.Collapsed;
                }

                if (DownloadButton != null)
                {
                    bool youtubeSpecificConditionsMet = true;
                    string currentUrl = UrlTextBox?.Text ?? string.Empty;
                    bool isYtDlpLink = _downloadService.IsYouTubeLink(currentUrl) || _downloadService.IsInstagramLink(currentUrl) || _downloadService.IsSocialVideoLink(currentUrl);
                    if (isYtDlpLink)
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
                    string currentUrl = UrlTextBox?.Text ?? string.Empty;
                    bool isYtDlpLink = _downloadService.IsYouTubeLink(currentUrl) || _downloadService.IsInstagramLink(currentUrl) || _downloadService.IsSocialVideoLink(currentUrl);
                    YouTubeQualityComboBox.IsEnabled = canInputUrl && _dependencyManager.IsYtDlpReady && isYtDlpLink && YouTubeQualityComboBox.HasItems;
                }

                if (statusMessageUpdate != null && StatusTextBlock != null)
                {
                    StatusTextBlock.Text = statusMessageUpdate;
                }
                else if (StatusTextBlock != null && !isBusy)
                {
                    StatusTextBlock.Text = _dependencyManager.IsYtDlpReady ? "Ready. Paste a URL to get started." : "Media features unavailable.";
                }
            });
        }

        private bool CanInitiateDownload()
        {
            bool hasSpotifyCsvTracks = _playlistItems.Count > 0 && _isPlaylistMode && 
                (_playlistItems[0].VideoUrl.StartsWith("ytsearch1:", StringComparison.OrdinalIgnoreCase));

            bool isUrlValid = UrlTextBox != null && !string.IsNullOrWhiteSpace(UrlTextBox.Text) && UrlTextBox.Text != "Paste URL here...";

            if (!isUrlValid && !hasSpotifyCsvTracks)
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
            if (_isSpotifyDrawerExpanded)
            {
                CollapseSpotifyDrawer();
            }

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
                StatusTextBlock.Text = "Error processing URL.";
                FileNameTextBlock.Text = "";
                FileNameTextBlock.Visibility = Visibility.Collapsed;
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
                if (_spotifyCsvPlaylistItems.Count > 0)
                {
                    // Restore Spotify CSV state!
                    _playlistItems.Clear();
                    foreach (var item in _spotifyCsvPlaylistItems)
                    {
                        _playlistItems.Add(item);
                    }
                    _isPlaylistMode = true;
                    _currentItemTitle = _spotifyCsvPlaylistName;
                    
                    if (FileNameTextBlock != null)
                    {
                        FileNameTextBlock.Text = _spotifyCsvPlaylistName;
                        FileNameTextBlock.Visibility = Visibility.Visible;
                    }
                    if (TrimmingSection != null) TrimmingSection.Visibility = Visibility.Collapsed;
                    if (VideoDurationLabel != null) VideoDurationLabel.Text = "";

                    if (FindName("PlaylistSection") is Border plBorder)
                    {
                        plBorder.Visibility = Visibility.Visible;
                    }
                    if (FindName("PlaylistTitleLabel") is TextBlock plTitle)
                    {
                        string shortPl = _spotifyCsvPlaylistName.Length > 35 ? _spotifyCsvPlaylistName.Substring(0, 32) + "..." : _spotifyCsvPlaylistName;
                        plTitle.Text = shortPl;
                    }
                    if (FindName("PlaylistCountLabel") is TextBlock plCount)
                    {
                        plCount.Text = $"{_playlistItems.Count} tracks";
                    }
                    if (FindName("PlaylistItemsControl") is ItemsControl plItems)
                    {
                        plItems.ItemsSource = null;
                        plItems.ItemsSource = _playlistItems;
                    }

                    StatusTextBlock.Text = $"Imported {_playlistItems.Count} tracks from CSV. Ready to download!";
                }
                else
                {
                    // Normal reset
                    _isPlaylistMode = false;
                    _playlistItems.Clear();
                    if (FindName("PlaylistSection") is Border playlistBorder) playlistBorder.Visibility = Visibility.Collapsed;
                    if (FileNameTextBlock != null) { FileNameTextBlock.Text = ""; FileNameTextBlock.Visibility = Visibility.Collapsed; }
                    if (TrimmingSection != null) TrimmingSection.Visibility = Visibility.Collapsed;
                    if (VideoDurationLabel != null) VideoDurationLabel.Text = "";
                }
                return;
            }

            // Reset playlist section for incoming URL
            _isPlaylistMode = false;
            _playlistItems.Clear();
            if (FindName("PlaylistSection") is Border playlistBorder2) playlistBorder2.Visibility = Visibility.Collapsed;

            // ── Playlist check FIRST (before single video) ──
            if (_downloadService.IsSpotifyPlaylistOrAlbumLink(url))
            {
                FileNameTextBlock.Text = "Loading Spotify playlist...";
                FileNameTextBlock.Visibility = Visibility.Visible;
                StatusTextBlock.Text = "Fetching Spotify track list...";

                try
                {
                    var plMetadata = await _downloadService.GetSpotifyPlaylistMetadataAsync(url, null, null, null);
                    if (plMetadata != null && plMetadata.Tracks.Count > 0)
                    {
                        _currentItemTitle = plMetadata.Name;
                        FileNameTextBlock.Text = plMetadata.Name;
                        FileNameTextBlock.Visibility = Visibility.Visible;

                        var defaultQualities = GetDefaultQualities();

                        _playlistItems.Clear();
                        foreach (var track in plMetadata.Tracks)
                        {
                            var item = new PlaylistVideoItem
                            {
                                IsSelected = true,
                                Title = $"{track.Artist} - {track.Title}",
                                VideoUrl = track.Uri, // e.g., "spotify:track:..."
                                DurationText = "",
                                AvailableQualities = new List<YouTubeQualityItem>(defaultQualities),
                                SelectedQualityIndex = 7 // Default to MP3
                            };
                            _playlistItems.Add(item);
                        }

                        // Show playlist section, hide single-video sections
                        _isPlaylistMode = true;
                        QualitySection.Visibility = Visibility.Collapsed;
                        if (TrimmingSection != null) TrimmingSection.Visibility = Visibility.Collapsed;

                        if (FindName("PlaylistSection") is Border plBorder)
                        {
                            plBorder.Visibility = Visibility.Visible;
                        }
                        if (FindName("PlaylistTitleLabel") is TextBlock plTitle)
                        {
                            string shortPl = plMetadata.Name.Length > 35 ? plMetadata.Name.Substring(0, 32) + "..." : plMetadata.Name;
                            plTitle.Text = shortPl;
                        }
                        if (FindName("PlaylistCountLabel") is TextBlock plCount)
                        {
                            plCount.Text = $"{_playlistItems.Count} tracks";
                        }
                        if (FindName("PlaylistItemsControl") is ItemsControl plItems)
                        {
                            plItems.ItemsSource = _playlistItems;
                        }

                        if (_playlistItems.Count == 100)
                        {
                            StatusTextBlock.Text = "Playlist loaded (limit 100). Use the CSV Import card to load playlists of any size!";
                        }
                        else
                        {
                            StatusTextBlock.Text = $"Spotify playlist loaded — {_playlistItems.Count} tracks. Select items and click Start Download.";
                        }
                        return;
                    }
                    else
                    {
                        StatusTextBlock.Text = "Could not load Spotify playlist or album tracks.";
                        FileNameTextBlock.Text = "Failed to load Spotify items";
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Spotify playlist parse error: {ex.Message}");
                    StatusTextBlock.Text = $"Error parsing Spotify playlist: {ex.Message}";
                }
                return;
            }
            else if (_downloadService.IsYouTubePlaylistLink(url))
            {
                FileNameTextBlock.Text = "Loading playlist...";
                FileNameTextBlock.Visibility = Visibility.Visible;
                StatusTextBlock.Text = "Fetching playlist info...";

                try
                {
                    string? playlistJson = await _downloadService.GetPlaylistInfoAsync(url);
                    if (playlistJson != null)
                    {
                        var playlistInfo = Newtonsoft.Json.Linq.JObject.Parse(playlistJson);
                        string playlistTitle = playlistInfo["title"]?.ToString() ?? "Unknown Playlist";
                        var entries = playlistInfo["entries"] as Newtonsoft.Json.Linq.JArray;

                        if (entries != null && entries.Count > 0)
                        {
                            _currentItemTitle = playlistTitle;
                            FileNameTextBlock.Text = playlistTitle;
                            FileNameTextBlock.Visibility = Visibility.Visible;

                            // Build default quality list for playlist items
                            var defaultQualities = GetDefaultQualities();

                            _playlistItems.Clear();
                            int index = 0;
                            foreach (var entry in entries)
                            {
                                index++;
                                string videoId = entry["id"]?.ToString() ?? "";
                                string videoTitle = entry["title"]?.ToString() ?? $"Video {index}";
                                double? duration = entry["duration"]?.ToObject<double?>();
                                string durationText = duration.HasValue ? SecondsToTimeString(duration.Value) : "";

                                var item = new PlaylistVideoItem
                                {
                                    IsSelected = true,
                                    Title = videoTitle,
                                    VideoUrl = $"https://www.youtube.com/watch?v={videoId}",
                                    DurationText = durationText,
                                    AvailableQualities = new List<YouTubeQualityItem>(defaultQualities),
                                    SelectedQualityIndex = 0
                                };
                                _playlistItems.Add(item);
                            }

                            // Show playlist section, hide single-video sections
                            _isPlaylistMode = true;
                            QualitySection.Visibility = Visibility.Collapsed;
                            if (TrimmingSection != null) TrimmingSection.Visibility = Visibility.Collapsed;

                            if (FindName("PlaylistSection") is Border plBorder)
                            {
                                plBorder.Visibility = Visibility.Visible;
                            }
                            if (FindName("PlaylistTitleLabel") is TextBlock plTitle)
                            {
                                string shortPl = playlistTitle.Length > 35 ? playlistTitle.Substring(0, 32) + "..." : playlistTitle;
                                plTitle.Text = shortPl;
                            }
                            if (FindName("PlaylistCountLabel") is TextBlock plCount)
                            {
                                plCount.Text = $"{_playlistItems.Count} videos";
                            }
                            if (FindName("PlaylistItemsControl") is ItemsControl plItems)
                            {
                                plItems.ItemsSource = _playlistItems;
                            }

                            StatusTextBlock.Text = $"Playlist loaded — {_playlistItems.Count} videos. Select items and click Start Download.";
                            return;
                        }
                    }
                    // Fallback: not a real playlist, treat as single video
                    StatusTextBlock.Text = "Could not parse playlist. Trying as single video...";
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Playlist parse error: {ex.Message}");
                }
            }

            if (_downloadService.IsYouTubeLink(url) || _downloadService.IsInstagramLink(url) || _downloadService.IsSocialVideoLink(url))
            {
                string platformName = _downloadService.IsInstagramLink(url) ? "Instagram" : (_downloadService.IsYouTubeLink(url) ? "YouTube" : "Video");
                FileNameTextBlock.Text = $"Processing {platformName} URL...";
                StatusTextBlock.Text = $"Status: Fetching {platformName} qualities...";
                
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
                            StatusTextBlock.Text = "Select a quality and click Start Download.";

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
                                    // Update duration labels in both sections
                                    if (VideoDurationLabel != null) VideoDurationLabel.Text = MaxVideoTimeText;
                                }
                            } catch { }
                            return;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Format info fetch error: {ex.Message}");
                }

                // Fallback for Instagram / Social video links when format pre-fetching is restricted by Instagram
                if (_downloadService.IsInstagramLink(url) || _downloadService.IsSocialVideoLink(url))
                {
                    var fallbackQualities = new List<YouTubeQualityItem>
                    {
                        new YouTubeQualityItem { Label = "Best Video + Audio", FormatCode = "bestvideo+bestaudio/best", IsAudioOnly = false, AudioFormat = "best", SortPriority = 9999 },
                        new YouTubeQualityItem { Label = "Best Audio Only", FormatCode = "bestaudio/best", IsAudioOnly = true, AudioFormat = "best", SortPriority = 49 },
                        new YouTubeQualityItem { Label = "Download as MP3", FormatCode = "bestaudio/best", IsAudioOnly = true, AudioFormat = "mp3", SortPriority = 48 }
                    };
                    YouTubeQualityComboBox.ItemsSource = fallbackQualities;
                    QualitySection.Visibility = Visibility.Visible;
                    YouTubeQualityComboBox.SelectedIndex = 0;

                    string cleanTitle = _downloadService.IsInstagramLink(url) ? "Instagram Reel / Media" : "Social Video";
                    _currentItemTitle = cleanTitle;
                    FileNameTextBlock.Text = cleanTitle;
                    FileNameTextBlock.Visibility = Visibility.Visible;
                    StatusTextBlock.Text = "Ready to download. Click Start Download.";
                    return;
                }

                StatusTextBlock.Text = "No downloadable formats found.";
                FileNameTextBlock.Text = "Failed to load video info";
                FileNameTextBlock.Visibility = Visibility.Visible;
            }
            else if (_downloadService.IsSpotifyLink(url))
            {
                if (!url.Contains("/track/", StringComparison.OrdinalIgnoreCase))
                {
                    _currentItemTitle = "";
                    FileNameTextBlock.Text = "Spotify Playlist/Album URL";
                    FileNameTextBlock.Visibility = Visibility.Visible;
                    StatusTextBlock.Text = "Only individual Spotify tracks are currently supported.";
                    return;
                }

                FileNameTextBlock.Text = "Fetching Spotify track metadata...";
                FileNameTextBlock.Visibility = Visibility.Visible;
                StatusTextBlock.Text = "Resolving track information...";

                var metadata = await _downloadService.GetSpotifyMetadataAsync(url);
                if (metadata != null)
                {
                    string beautifulTitle = $"{metadata.Artist} - {metadata.Title}";
                    _currentItemTitle = beautifulTitle;
                    FileNameTextBlock.Text = beautifulTitle;
                    StatusTextBlock.Text = "Spotify track resolved. Ready to download.";
                }
                else
                {
                    _currentItemTitle = "Spotify Track";
                    FileNameTextBlock.Text = "Spotify Track";
                    StatusTextBlock.Text = "Ready to download (metadata lookup failed).";
                }
            }
            else if (_downloadService.IsKnownAudioPlatformLink(url))
            {
                string title = await _downloadService.GetTitleWithYtDlpAsync(url);
                string audioTitle = title ?? "Audio Platform Item";
                _currentItemTitle = audioTitle;
                FileNameTextBlock.Text = audioTitle;
                FileNameTextBlock.Visibility = Visibility.Visible;
                StatusTextBlock.Text = "Ready to download audio.";
            }
            else if (_downloadService.IsGoogleDriveLink(url))
            {
                _currentItemTitle = "Google Drive file";
                FileNameTextBlock.Text = "Google Drive file";
                FileNameTextBlock.Visibility = Visibility.Visible;
                StatusTextBlock.Text = "Ready to download from Google Drive.";
            }
            else
            {
                _currentItemTitle = "Direct link detected";
                FileNameTextBlock.Text = "Direct link detected";
                FileNameTextBlock.Visibility = Visibility.Visible;
                StatusTextBlock.Text = "Ready to download.";
            }
        }

        private System.Collections.Generic.List<YouTubeQualityItem> ExtractQualitiesFromYouTubeInfo(string jsonOutput)
        {
             var list = new System.Collections.Generic.List<YouTubeQualityItem>();
             try
             {
                  var videoInfo = Newtonsoft.Json.Linq.JObject.Parse(jsonOutput);
                  var title = videoInfo["title"]?.ToString();
                  if (string.IsNullOrWhiteSpace(title)) title = videoInfo["fulltitle"]?.ToString();
                  if (string.IsNullOrWhiteSpace(title) && videoInfo["uploader"] != null) title = $"Video by {videoInfo["uploader"]}";
                  if (string.IsNullOrWhiteSpace(title)) title = "Media Video";
                 string sanitizedTitle = Utilities.SanitizeFileName(title);
                 // Store for restore after cancel
                 _currentItemTitle = sanitizedTitle;
                 // Show title in the filename block
                 FileNameTextBlock.Text = sanitizedTitle;
                 FileNameTextBlock.Visibility = Visibility.Visible;
                 // Show shortened title in the quality section header
                 if (VideoTitleInfoBlock != null)
                 {
                     string shortTitle = title.Length > 30 ? title.Substring(0, 27) + "..." : title;
                     VideoTitleInfoBlock.Text = shortTitle;
                 }

                 // SortPriority 9999 = always first (above any resolution value)
                 // No [ext=] filter — let yt-dlp pick the absolute best quality stream; --merge-output-format mp4 handles the container
                 list.Add(new YouTubeQualityItem { Label = "Best Video + Best Audio", FormatCode = "bestvideo+bestaudio/best", IsAudioOnly = false, AudioFormat = "best", SortPriority = 9999 });
                 // Audio-only options at the bottom
                 list.Add(new YouTubeQualityItem { Label = "Best Audio Only",   FormatCode = "bestaudio/best", IsAudioOnly = true, AudioFormat = "best", SortPriority = 49 });
                 list.Add(new YouTubeQualityItem { Label = "Download as MP3",    FormatCode = "bestaudio/best", IsAudioOnly = true, AudioFormat = "mp3",  SortPriority = 48 });

                 var formats = videoInfo["formats"] as Newtonsoft.Json.Linq.JArray;
                 if (formats != null)
                 {
                     var uniqueHeights = new System.Collections.Generic.HashSet<int>();
                     foreach (var format in formats)
                     {
                         var vcodec = format["vcodec"]?.ToString();
                         // Accept ANY video stream regardless of container — --merge-output-format mp4 handles remux
                         if (vcodec != null && vcodec != "none")
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
                                 
                                 // No [ext=] filter — let yt-dlp pick the best codec; --merge-output-format mp4 ensures the final output is MP4
                                 list.Add(new YouTubeQualityItem 
                                 { 
                                     Label = label, 
                                     FormatCode = $"bestvideo[height<={height}]+bestaudio/best[height<={height}]/best",
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

            if (_isProcessingUrl)
            {
                StatusTextBlock.Text = "Please wait for URL resolution to complete...";
                return;
            }

            string url = UrlTextBox.Text;
            bool hasSpotifyCsvTracks = _playlistItems.Count > 0 && _isPlaylistMode && 
                (_playlistItems[0].VideoUrl.StartsWith("ytsearch1:", StringComparison.OrdinalIgnoreCase));
            bool isUrlValid = !string.IsNullOrWhiteSpace(url) && url != "Paste URL here...";

            if (hasSpotifyCsvTracks && !isUrlValid)
            {
                _isPlaylistMode = true;
            }

            if (!CanInitiateDownload()) return;

            _isDownloadingFile = true;
            _cancellationTokenSource = new CancellationTokenSource();
            UpdateUiElementStates("Preparing to download...");

            // Restore the video title in case it was overwritten by a previous cancel
            if (!string.IsNullOrEmpty(_currentItemTitle))
            {
                FileNameTextBlock.Text = _currentItemTitle;
                FileNameTextBlock.Visibility = Visibility.Visible;
            }

            DownloadProgressBar.Value = 0;
            DownloadProgressBar.IsIndeterminate = true;

            try
            {
                // ── Playlist download mode ──
                if (_isPlaylistMode && _playlistItems.Count > 0)
                {
                    var selectedItems = new List<PlaylistVideoItem>();
                    foreach (var item in _playlistItems)
                    {
                        if (item.IsSelected) selectedItems.Add(item);
                    }

                    if (selectedItems.Count == 0)
                    {
                        StatusTextBlock.Text = "No videos selected. Check at least one item.";
                        return;
                    }

                    for (int i = 0; i < selectedItems.Count; i++)
                    {
                        _cancellationTokenSource.Token.ThrowIfCancellationRequested();

                        var plItem = selectedItems[i];
                        var quality = plItem.AvailableQualities[plItem.SelectedQualityIndex];

                        _currentItemTitle = plItem.Title;
                        FileNameTextBlock.Text = $"[{i + 1}/{selectedItems.Count}] {plItem.Title}";
                        FileNameTextBlock.Visibility = Visibility.Visible;
                        StatusTextBlock.Text = $"Downloading {i + 1} of {selectedItems.Count}...";
                        DownloadProgressBar.Value = 0;
                        DownloadProgressBar.IsIndeterminate = true;

                        string downloadUrl = plItem.VideoUrl;
                        string? overrideName = null;
                        if (downloadUrl.StartsWith("spotify:", StringComparison.OrdinalIgnoreCase) || downloadUrl.Contains("open.spotify.com") || downloadUrl.StartsWith("ytsearch1:", StringComparison.OrdinalIgnoreCase))
                        {
                            string cleanTitle = plItem.Title.Replace("\"", "").Replace("'", "");
                            downloadUrl = $"ytsearch1:{cleanTitle}";
                            overrideName = plItem.Title;
                        }

                        _downloadService.ProgressPrefix = $"[{i + 1}/{selectedItems.Count}]";
                        try
                        {
                            await _downloadService.DownloadWithYtDlpAsync(
                                downloadUrl, quality.FormatCode,
                                Downloader.App.AppTempDirectory, SelectedDirectory,
                                quality.IsAudioOnly, quality.AudioFormat,
                                false, 0, 0, _cancellationTokenSource.Token, overrideName);
                        }
                        finally
                        {
                            _downloadService.ProgressPrefix = null;
                        }
                    }

                    StatusTextBlock.Text = $"Playlist complete — {selectedItems.Count} videos downloaded.";
                    FileNameTextBlock.Text = _currentItemTitle;
                }
                // ── Single video download mode ──
                else if (_downloadService.IsYouTubeLink(url) || _downloadService.IsInstagramLink(url) || _downloadService.IsSocialVideoLink(url))
                {
                    var selectedQuality = YouTubeQualityComboBox.SelectedItem as YouTubeQualityItem;
                    if (selectedQuality != null)
                    {
                        // Each quality item carries its own AudioFormat ("best" = original, "mp3" = convert)
                        await _downloadService.DownloadWithYtDlpAsync(url, selectedQuality.FormatCode, Downloader.App.AppTempDirectory, SelectedDirectory, selectedQuality.IsAudioOnly, selectedQuality.AudioFormat, IsTrimmingEnabled, TrimStartTimeInSeconds, TrimEndTimeInSeconds, _cancellationTokenSource.Token);
                    }
                    else
                    {
                        await _downloadService.DownloadWithYtDlpAsync(url, "bestaudio/best", Downloader.App.AppTempDirectory, SelectedDirectory, true, "mp3", false, 0, 0, _cancellationTokenSource.Token);
                    }
                }
                else if (_downloadService.IsSpotifyLink(url))
                {
                    string initialArtist = "Unknown Artist";
                    string initialTitle = "Spotify Track";

                    StatusTextBlock.Text = "Получение метаданных трека...";
                    var metadata = await _downloadService.GetSpotifyMetadataAsync(url);
                    if (metadata != null)
                    {
                        initialArtist = metadata.Artist;
                        initialTitle = metadata.Title;
                    }

                    // If metadata lookup failed or returned generic/unknown values, prompt user
                    if (string.IsNullOrWhiteSpace(initialArtist) || initialArtist == "Unknown Artist" || 
                        string.IsNullOrWhiteSpace(initialTitle) || initialTitle == "Spotify Track" ||
                        initialTitle.StartsWith("Unknown Artist", StringComparison.OrdinalIgnoreCase))
                    {
                        var dialog = new SpotifyManualInputDialog(initialArtist, initialTitle)
                        {
                            Owner = this
                        };

                        if (dialog.ShowDialog() == true)
                        {
                            _currentItemTitle = $"{dialog.Artist} - {dialog.TrackTitle}".Trim();
                            if (_currentItemTitle.StartsWith("- "))
                            {
                                _currentItemTitle = _currentItemTitle.Substring(2);
                            }
                            FileNameTextBlock.Text = _currentItemTitle;
                        }
                        else
                        {
                            StatusTextBlock.Text = "Скачивание отменено пользователем.";
                            return;
                        }
                    }
                    else
                    {
                        _currentItemTitle = $"{initialArtist} - {initialTitle}";
                        FileNameTextBlock.Text = _currentItemTitle;
                    }

                    // Remove internal double quotes from query to ensure yt-dlp parses it properly
                    string cleanQueryTitle = _currentItemTitle.Replace("\"", "").Replace("'", "");
                    string query = $"ytsearch1:{cleanQueryTitle}";

                    StatusTextBlock.Text = $"Поиск '{_currentItemTitle}' на YouTube...";
                    await _downloadService.DownloadWithYtDlpAsync(query, "bestaudio/best", Downloader.App.AppTempDirectory, SelectedDirectory, true, "mp3", false, 0, 0, _cancellationTokenSource.Token, _currentItemTitle);
                    
                    StatusTextBlock.Text = $"Скачивание завершено: {_currentItemTitle}";
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
                StatusTextBlock.Text = "Download canceled.";
            }
            catch (Exception ex)
            {
                string msg = ex.Message.Replace("\r", " ").Replace("\n", " ").Trim();
                if (msg.Length > 120) msg = msg.Substring(0, 117) + "...";
                StatusTextBlock.Text = $"Error: {msg}";
                StatusTextBlock.ToolTip = ex.ToString();
                FileNameTextBlock.Text = "Download failed";
                FileNameTextBlock.Visibility = Visibility.Visible;
                Debug.WriteLine($"[DOWNLOAD ERROR] {ex}");
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
                StatusTextBlock.Text = "Canceling...";
                FileNameTextBlock.Text = "Canceling download...";
                FileNameTextBlock.Visibility = Visibility.Visible;
                _cancellationTokenSource?.Cancel();
            }
        }
        private void PlaylistSelectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in _playlistItems) item.IsSelected = true;
        }

        private void PlaylistDeselectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in _playlistItems) item.IsSelected = false;
        }

        /// <summary>Returns the default quality preset list used for each playlist item.</summary>
        private List<YouTubeQualityItem> GetDefaultQualities()
        {
            return new List<YouTubeQualityItem>
            {
                new YouTubeQualityItem { Label = "Best Video + Audio", FormatCode = "bestvideo+bestaudio/best", IsAudioOnly = false, AudioFormat = "best", SortPriority = 9999 },
                new YouTubeQualityItem { Label = "4K", FormatCode = "bestvideo[height<=2160]+bestaudio/best[height<=2160]/best", IsAudioOnly = false, AudioFormat = "best", SortPriority = 2160 },
                new YouTubeQualityItem { Label = "1440p", FormatCode = "bestvideo[height<=1440]+bestaudio/best[height<=1440]/best", IsAudioOnly = false, AudioFormat = "best", SortPriority = 1440 },
                new YouTubeQualityItem { Label = "1080p", FormatCode = "bestvideo[height<=1080]+bestaudio/best[height<=1080]/best", IsAudioOnly = false, AudioFormat = "best", SortPriority = 1080 },
                new YouTubeQualityItem { Label = "720p", FormatCode = "bestvideo[height<=720]+bestaudio/best[height<=720]/best", IsAudioOnly = false, AudioFormat = "best", SortPriority = 720 },
                new YouTubeQualityItem { Label = "480p", FormatCode = "bestvideo[height<=480]+bestaudio/best[height<=480]/best", IsAudioOnly = false, AudioFormat = "best", SortPriority = 480 },
                new YouTubeQualityItem { Label = "Best Audio", FormatCode = "bestaudio/best", IsAudioOnly = true, AudioFormat = "best", SortPriority = 49 },
                new YouTubeQualityItem { Label = "MP3", FormatCode = "bestaudio/best", IsAudioOnly = true, AudioFormat = "mp3", SortPriority = 48 },
            };
        }

        private void OpenExportifyButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://exportify.net",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not open browser: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void ImportSpotifyCsvButton_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                Title = "Select Spotify Playlist CSV"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                string filePath = openFileDialog.FileName;
                try
                {
                    StatusTextBlock.Text = "Importing tracks from CSV...";
                    string playlistName = Path.GetFileNameWithoutExtension(filePath);

                    var tracks = new List<(string Title, string Artist)>();

                    // Read CSV lines
                    var lines = await Task.Run(() => File.ReadAllLines(filePath, Encoding.UTF8));
                    if (lines.Length <= 1)
                    {
                        MessageBox.Show("The selected CSV file is empty.", "Import Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                        StatusTextBlock.Text = "Failed to import CSV: file is empty.";
                        return;
                    }

                    // Parse header to find column indices
                    string header = lines[0];
                    string[] headers = ParseCsvLine(header);

                    int trackNameIdx = -1;
                    int artistNameIdx = -1;

                    for (int i = 0; i < headers.Length; i++)
                    {
                        string h = headers[i].Trim().ToLower();
                        if (h.Contains("track name") || h == "title" || h == "name")
                        {
                            trackNameIdx = i;
                        }
                        else if (h.Contains("artist name") || h == "artist" || h == "artists")
                        {
                            artistNameIdx = i;
                        }
                    }

                    if (trackNameIdx == -1 || artistNameIdx == -1)
                    {
                        // Fallback to default indices if headers aren't clear:
                        // Exportify columns:
                        // [0] Track URI, [1] Track Name, [2] Artist URI(s), [3] Artist Name(s)
                        trackNameIdx = 1;
                        artistNameIdx = 3;
                    }

                    for (int i = 1; i < lines.Length; i++)
                    {
                        string line = lines[i];
                        if (string.IsNullOrWhiteSpace(line)) continue;

                        string[] fields = ParseCsvLine(line);
                        if (fields.Length > Math.Max(trackNameIdx, artistNameIdx))
                        {
                            string title = fields[trackNameIdx].Trim();
                            string artist = fields[artistNameIdx].Trim();
                            if (!string.IsNullOrEmpty(title))
                            {
                                tracks.Add((title, artist));
                            }
                        }
                    }

                    if (tracks.Count > 0)
                    {
                        _currentItemTitle = playlistName;
                        _spotifyCsvPlaylistName = playlistName;
                        FileNameTextBlock.Text = playlistName;
                        FileNameTextBlock.Visibility = Visibility.Visible;

                        var defaultQualities = GetDefaultQualities();

                        _playlistItems.Clear();
                        _spotifyCsvPlaylistItems.Clear();
                        _isPlaylistMode = true;
                        QualitySection.Visibility = Visibility.Collapsed;
                        if (TrimmingSection != null) TrimmingSection.Visibility = Visibility.Collapsed;

                        foreach (var track in tracks)
                        {
                            var item = new PlaylistVideoItem
                            {
                                IsSelected = true,
                                Title = $"{track.Artist} - {track.Title}",
                                VideoUrl = $"ytsearch1:{track.Artist} - {track.Title}", // Search query for yt-dlp
                                DurationText = "",
                                AvailableQualities = new List<YouTubeQualityItem>(defaultQualities),
                                SelectedQualityIndex = 7 // Default to MP3
                            };
                            _playlistItems.Add(item);
                            _spotifyCsvPlaylistItems.Add(item);
                        }

                        if (FindName("PlaylistSection") is Border plBorder)
                        {
                            plBorder.Visibility = Visibility.Visible;
                        }
                        if (FindName("PlaylistTitleLabel") is TextBlock plTitle)
                        {
                            string shortPl = playlistName.Length > 35 ? playlistName.Substring(0, 32) + "..." : playlistName;
                            plTitle.Text = shortPl;
                        }
                        if (FindName("PlaylistCountLabel") is TextBlock plCount)
                        {
                            plCount.Text = $"{_playlistItems.Count} tracks";
                        }
                        if (FindName("PlaylistItemsControl") is ItemsControl plItems)
                        {
                            plItems.ItemsSource = null;
                            plItems.ItemsSource = _playlistItems;
                        }

                        // Update Spotify side drawer elements
                        if (SpotifyCsvInfoBorder != null)
                        {
                            SpotifyCsvInfoBorder.Visibility = Visibility.Visible;
                        }
                        if (SpotifyCsvNameText != null)
                        {
                            SpotifyCsvNameText.Text = playlistName;
                        }
                        if (SpotifyCsvCountText != null)
                        {
                            SpotifyCsvCountText.Text = $"{tracks.Count} tracks loaded";
                        }

                        // Create and add the playlist to history
                        var importedPlaylist = new SpotifyImportedPlaylist
                        {
                            Name = playlistName,
                            Tracks = new List<PlaylistVideoItem>(_playlistItems)
                        };

                        var existing = _spotifyImports.FirstOrDefault(p => p.Name == playlistName);
                        if (existing != null)
                        {
                            _spotifyImports.Remove(existing);
                        }
                        _spotifyImports.Insert(0, importedPlaylist);

                        if (SpotifyImportsItemsControl != null)
                        {
                            SpotifyImportsItemsControl.ItemsSource = null;
                            SpotifyImportsItemsControl.ItemsSource = _spotifyImports;
                        }
                        if (SpotifyImportsBorder != null)
                        {
                            SpotifyImportsBorder.Visibility = Visibility.Visible;
                        }

                        StatusTextBlock.Text = $"Imported {_playlistItems.Count} tracks from CSV. Ready to download!";
                        CollapseSpotifyDrawer();
                        UpdateUiElementStates();
                    }
                    else
                    {
                        MessageBox.Show("No tracks could be parsed from the CSV file.", "Import Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                        StatusTextBlock.Text = "Failed to parse tracks from CSV.";
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to parse CSV file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    StatusTextBlock.Text = "Error importing CSV.";
                }
            }
        }

        private string[] ParseCsvLine(string line)
        {
            var fields = new List<string>();
            bool inQuotes = false;
            var builder = new StringBuilder();

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"')
                {
                    inQuotes = !inQuotes;
                }
                else if (c == ',' && !inQuotes)
                {
                    fields.Add(builder.ToString());
                    builder.Clear();
                }
                else
                {
                    builder.Append(c);
                }
            }
            fields.Add(builder.ToString());
            return fields.ToArray();
        }

        private void SpotifyCollapsedButton_Click(object sender, RoutedEventArgs e)
        {
            ExpandSpotifyDrawer();
        }

        private void SpotifyExpandedHeaderButton_Click(object sender, RoutedEventArgs e)
        {
            CollapseSpotifyDrawer();
        }

        private void ExpandSpotifyDrawer()
        {
            if (_isSpotifyDrawerExpanded) return;
            _isSpotifyDrawerExpanded = true;

            // Block URL text input
            if (UrlTextBox != null)
            {
                UrlTextBox.IsEnabled = false;
            }

            // Show overlay
            if (MainContentOverlayBorder != null)
            {
                MainContentOverlayBorder.Visibility = Visibility.Visible;
                var opacityAnim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250))
                {
                    DecelerationRatio = 0.95
                };
                MainContentOverlayBorder.BeginAnimation(OpacityProperty, opacityAnim);
            }

            // Slide drawer using TranslateTransform (extremely smooth GPU animation!)
            if (DrawerTransform != null)
            {
                var slideAnim = new DoubleAnimation(260, 0, TimeSpan.FromMilliseconds(300))
                {
                    DecelerationRatio = 0.95
                };
                DrawerTransform.BeginAnimation(TranslateTransform.XProperty, slideAnim);
            }

            // Toggle content visibility with fades
            if (SpotifyCollapsedView != null)
            {
                var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(120));
                fadeOut.Completed += (s, e2) => SpotifyCollapsedView.Visibility = Visibility.Collapsed;
                SpotifyCollapsedView.BeginAnimation(OpacityProperty, fadeOut);
            }

            if (SpotifyExpandedView != null)
            {
                SpotifyExpandedView.Visibility = Visibility.Visible;
                var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250))
                {
                    BeginTime = TimeSpan.FromMilliseconds(80),
                    DecelerationRatio = 0.95
                };
                SpotifyExpandedView.BeginAnimation(OpacityProperty, fadeIn);
            }

            UpdateUiElementStates();
        }

        private void CollapseSpotifyDrawer()
        {
            if (!_isSpotifyDrawerExpanded) return;
            _isSpotifyDrawerExpanded = false;

            // Hide overlay
            if (MainContentOverlayBorder != null)
            {
                var opacityAnim = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200))
                {
                    DecelerationRatio = 0.95
                };
                opacityAnim.Completed += (s, e2) => MainContentOverlayBorder.Visibility = Visibility.Collapsed;
                MainContentOverlayBorder.BeginAnimation(OpacityProperty, opacityAnim);
            }

            // Slide drawer back using TranslateTransform
            if (DrawerTransform != null)
            {
                var slideAnim = new DoubleAnimation(0, 260, TimeSpan.FromMilliseconds(300))
                {
                    DecelerationRatio = 0.95
                };
                DrawerTransform.BeginAnimation(TranslateTransform.XProperty, slideAnim);
            }

            // Toggle content visibility back
            if (SpotifyExpandedView != null)
            {
                var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(120));
                fadeOut.Completed += (s, e2) => SpotifyExpandedView.Visibility = Visibility.Collapsed;
                SpotifyExpandedView.BeginAnimation(OpacityProperty, fadeOut);
            }

            if (SpotifyCollapsedView != null)
            {
                SpotifyCollapsedView.Visibility = Visibility.Visible;
                var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250))
                {
                    BeginTime = TimeSpan.FromMilliseconds(80),
                    DecelerationRatio = 0.95
                };
                SpotifyCollapsedView.BeginAnimation(OpacityProperty, fadeIn);
            }

            // Re-enable UrlTextBox if needed
            if (UrlTextBox != null)
            {
                UrlTextBox.IsEnabled = true;
            }

            UpdateUiElementStates();
        }

        private void MainContentOverlayBorder_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            bool overUrlTextBox = UrlTextBox != null && UrlTextBox.IsMouseOver;
            CollapseSpotifyDrawer();
            if (overUrlTextBox && UrlTextBox != null)
            {
                UrlTextBox.Focus();
                if (UrlTextBox.Text == "Paste URL here...")
                {
                    UrlTextBox.Text = "";
                    UrlTextBox.Foreground = (Brush)FindResource("TextPrimaryBrush");
                }
            }
        }

        private void UrlTextBox_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (_isSpotifyDrawerExpanded)
            {
                CollapseSpotifyDrawer();
            }
        }

        private void SpotifyImportItem_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            if (button == null) return;

            var playlist = button.Tag as SpotifyImportedPlaylist;
            if (playlist == null) return;

            // Load playlist details into main application state
            _currentItemTitle = playlist.Name;
            _spotifyCsvPlaylistName = playlist.Name;
            
            if (FileNameTextBlock != null)
            {
                FileNameTextBlock.Text = playlist.Name;
                FileNameTextBlock.Visibility = Visibility.Visible;
            }

            _playlistItems.Clear();
            _spotifyCsvPlaylistItems.Clear();
            _isPlaylistMode = true;
            
            if (QualitySection != null) QualitySection.Visibility = Visibility.Collapsed;
            if (TrimmingSection != null) TrimmingSection.Visibility = Visibility.Collapsed;

            foreach (var item in playlist.Tracks)
            {
                _playlistItems.Add(item);
                _spotifyCsvPlaylistItems.Add(item);
            }

            if (FindName("PlaylistSection") is Border plBorder)
            {
                plBorder.Visibility = Visibility.Visible;
            }
            if (FindName("PlaylistTitleLabel") is TextBlock plTitle)
            {
                string shortPl = playlist.Name.Length > 35 ? playlist.Name.Substring(0, 32) + "..." : playlist.Name;
                plTitle.Text = shortPl;
            }
            if (FindName("PlaylistCountLabel") is TextBlock plCount)
            {
                plCount.Text = $"{_playlistItems.Count} tracks";
            }
            if (FindName("PlaylistItemsControl") is ItemsControl plItems)
            {
                plItems.ItemsSource = null;
                plItems.ItemsSource = _playlistItems;
            }

            // Highlight the active import status panel in the drawer
            if (SpotifyCsvInfoBorder != null)
            {
                SpotifyCsvInfoBorder.Visibility = Visibility.Visible;
            }
            if (SpotifyCsvNameText != null)
            {
                SpotifyCsvNameText.Text = playlist.Name;
            }
            if (SpotifyCsvCountText != null)
            {
                SpotifyCsvCountText.Text = $"{playlist.Tracks.Count} tracks loaded";
            }

            StatusTextBlock.Text = $"Loaded playlist '{playlist.Name}' from previous imports. Ready to download!";
            CollapseSpotifyDrawer();
            UpdateUiElementStates();
        }
    }

    public class SpotifyImportedPlaylist
    {
        public string Name { get; set; } = "";
        public List<PlaylistVideoItem> Tracks { get; set; } = new List<PlaylistVideoItem>();
    }

    public class YouTubeQualityItem
    {
        public required string Label { get; set; }
        public required string FormatCode { get; set; }
        public bool IsAudioOnly { get; set; }
        /// <summary>yt-dlp --audio-format value: "best" keeps original, "mp3" converts to MP3.</summary>
        public string AudioFormat { get; set; } = "best";
        public int SortPriority { get; set; }
    }

    public class PlaylistVideoItem : INotifyPropertyChanged
    {
        private bool _isSelected = true;
        private int _selectedQualityIndex = 0;

        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(nameof(IsSelected)); }
        }

        public string Title { get; set; } = "";
        public string VideoUrl { get; set; } = "";
        public string DurationText { get; set; } = "";
        public List<YouTubeQualityItem> AvailableQualities { get; set; } = new();

        public int SelectedQualityIndex
        {
            get => _selectedQualityIndex;
            set { _selectedQualityIndex = value; OnPropertyChanged(nameof(SelectedQualityIndex)); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class SpotifyManualInputDialog : Window
    {
        private TextBox _artistTextBox;
        private TextBox _titleTextBox;
        
        public string Artist => _artistTextBox.Text.Trim();
        public string TrackTitle => _titleTextBox.Text.Trim();

        public SpotifyManualInputDialog(string initialArtist, string initialTitle)
        {
            Title = "Ввод метаданных Spotify";
            Width = 420;
            Height = 260;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#121212"));
            Foreground = Brushes.White;
            FontFamily = new FontFamily("Segoe UI");
            
            WindowStyle = WindowStyle.None;
            BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1DB954"));
            BorderThickness = new Thickness(2);

            var mainGrid = new Grid();
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(40) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            
            var titleBar = new Border
            {
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#191919")),
                Padding = new Thickness(15, 0, 15, 0)
            };
            titleBar.MouseLeftButtonDown += (s, e) => { if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed) DragMove(); };
            
            var titleBarGrid = new Grid();
            titleBarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            titleBarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            
            var titleText = new TextBlock
            {
                Text = "Метаданные трека Spotify",
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1DB954")),
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 14
            };
            Grid.SetColumn(titleText, 0);
            titleBarGrid.Children.Add(titleText);
            
            var closeButton = new Button
            {
                Content = "×",
                Background = Brushes.Transparent,
                Foreground = Brushes.Gray,
                BorderThickness = new Thickness(0),
                FontSize = 20,
                FontWeight = FontWeights.Bold,
                Cursor = System.Windows.Input.Cursors.Hand,
                Width = 30,
                Height = 30,
                VerticalAlignment = VerticalAlignment.Center
            };
            closeButton.Click += (s, e) => { DialogResult = false; Close(); };
            closeButton.MouseEnter += (s, e) => closeButton.Foreground = Brushes.Red;
            closeButton.MouseLeave += (s, e) => closeButton.Foreground = Brushes.Gray;
            
            Grid.SetColumn(closeButton, 1);
            titleBarGrid.Children.Add(closeButton);
            titleBar.Child = titleBarGrid;
            
            Grid.SetRow(titleBar, 0);
            mainGrid.Children.Add(titleBar);
            
            var contentStack = new StackPanel
            {
                Margin = new Thickness(20)
            };
            
            var infoText = new TextBlock
            {
                Text = "Не удалось автоматически получить информацию о треке.\nПожалуйста, введите название и исполнителя:",
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#B3B3B3")),
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 15),
                TextWrapping = TextWrapping.Wrap
            };
            contentStack.Children.Add(infoText);
            
            var artistLabel = new TextBlock { Text = "Исполнитель:", Foreground = Brushes.White, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4), FontSize = 12 };
            contentStack.Children.Add(artistLabel);
            
            _artistTextBox = new TextBox
            {
                Text = initialArtist == "Unknown Artist" ? "" : initialArtist,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#282828")),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#404040")),
                CaretBrush = Brushes.White,
                Padding = new Thickness(6, 4, 6, 4),
                FontSize = 13,
                Margin = new Thickness(0, 0, 0, 12),
                BorderThickness = new Thickness(1)
            };
            _artistTextBox.GotFocus += (s, e) => _artistTextBox.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1DB954"));
            _artistTextBox.LostFocus += (s, e) => _artistTextBox.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#404040"));
            contentStack.Children.Add(_artistTextBox);
            
            var titleLabel = new TextBlock { Text = "Название трека:", Foreground = Brushes.White, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4), FontSize = 12 };
            contentStack.Children.Add(titleLabel);
            
            _titleTextBox = new TextBox
            {
                Text = initialTitle == "Spotify Track" ? "" : initialTitle,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#282828")),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#404040")),
                CaretBrush = Brushes.White,
                Padding = new Thickness(6, 4, 6, 4),
                FontSize = 13,
                Margin = new Thickness(0, 0, 0, 15),
                BorderThickness = new Thickness(1)
            };
            _titleTextBox.GotFocus += (s, e) => _titleTextBox.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1DB954"));
            _titleTextBox.LostFocus += (s, e) => _titleTextBox.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#404040"));
            contentStack.Children.Add(_titleTextBox);
            
            var buttonsGrid = new Grid();
            buttonsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            buttonsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            
            var cancelButton = new Button
            {
                Content = "Отмена",
                Background = Brushes.Transparent,
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#404040")),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10, 6, 10, 6),
                Margin = new Thickness(0, 0, 10, 0),
                Cursor = System.Windows.Input.Cursors.Hand,
                FontWeight = FontWeights.SemiBold
            };
            cancelButton.Click += (s, e) => { DialogResult = false; Close(); };
            cancelButton.MouseEnter += (s, e) => { cancelButton.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#282828")); };
            cancelButton.MouseLeave += (s, e) => { cancelButton.Background = Brushes.Transparent; };
            Grid.SetColumn(cancelButton, 0);
            buttonsGrid.Children.Add(cancelButton);
            
            var okButton = new Button
            {
                Content = "Скачать",
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1DB954")),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(10, 6, 10, 6),
                Margin = new Thickness(10, 0, 0, 0),
                Cursor = System.Windows.Input.Cursors.Hand,
                FontWeight = FontWeights.Bold
            };
            okButton.Click += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(TrackTitle))
                {
                    MessageBox.Show("Пожалуйста, введите название трека.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                DialogResult = true;
                Close();
            };
            okButton.MouseEnter += (s, e) => { okButton.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1ED760")); };
            okButton.MouseLeave += (s, e) => { okButton.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1DB954")); };
            Grid.SetColumn(okButton, 1);
            buttonsGrid.Children.Add(okButton);
            
            contentStack.Children.Add(buttonsGrid);
            
            Grid.SetRow(contentStack, 1);
            mainGrid.Children.Add(contentStack);
            
            Content = mainGrid;
        }
    }
}