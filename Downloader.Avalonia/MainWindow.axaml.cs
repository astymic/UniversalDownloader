using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using UniversalDownloader.Models;
using UniversalDownloader.Services;

namespace UniversalDownloader.Avalonia
{
    public partial class MainWindow : Window
    {
        private readonly DependencyManager _dependencyManager;
        private readonly DownloadService _downloadService;
        private readonly HistoryService _historyService;
        private readonly DownloadQueueManager _queueManager;
        private readonly SearchService _searchService;
        private readonly FfmpegAudioCaptureService _audioCaptureService;
        private readonly ShazamRecognitionService _shazamService;

        private readonly LiveStreamRecognitionService _liveStreamService;

        public ObservableCollection<SearchResultItem> SearchResults { get; } = new();
        public ObservableCollection<DownloadHistoryItem> HistoryItems { get; } = new();

        private string _downloadFolder;
        private SearchMode _currentSearchMode = SearchMode.SmartMusic;
        private string _searchPlatformFilter = "All";
        private Task<SearchResultBatch>? _smartMusicSearchTask;
        private Task<SearchResultBatch>? _realYouTubeSearchTask;
        private string _lastExecutedQuery = string.Empty;
        private CancellationTokenSource? _searchCts;
        private CancellationTokenSource? _downloadCts;

        public MainWindow()
        {
            InitializeComponent();

            _downloadFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            if (DownloadPathTextBox != null) DownloadPathTextBox.Text = _downloadFolder;

            _dependencyManager = new DependencyManager();
            _historyService = new HistoryService();
            _downloadService = new DownloadService(_dependencyManager);
            _queueManager = new DownloadQueueManager(_downloadService, _historyService);
            _searchService = new SearchService(_dependencyManager);
            _audioCaptureService = new FfmpegAudioCaptureService(_dependencyManager);
            _shazamService = new ShazamRecognitionService(_audioCaptureService);
            _liveStreamService = new LiveStreamRecognitionService(_audioCaptureService, _shazamService);

            _liveStreamService.ListeningStateChanged += isListening => Dispatcher.UIThread.Post(() =>
            {
                if (LiveStreamToggleButton != null)
                {
                    LiveStreamToggleButton.Content = isListening ? "⏹️ Stop Listening" : "🎙️ Start Listening (PC Audio)";
                }
                UpdateLiveStreamUI();
            });

            _liveStreamService.TrackDetected += track => Dispatcher.UIThread.Post(UpdateLiveStreamUI);

            _liveStreamService.AudioLevelChanged += level => Dispatcher.UIThread.Post(() =>
            {
                if (LiveStreamAudioProgressBar != null && _liveStreamService.IsListening)
                {
                    LiveStreamAudioProgressBar.Value = Math.Min(1.0, level * 2.5);
                }
            });

            _liveStreamService.StatusUpdated += status => Dispatcher.UIThread.Post(() =>
            {
                if (LiveStreamStatusTextBlock != null) LiveStreamStatusTextBlock.Text = status;
            });

            KeyDown += MainWindow_KeyDown;

            // Hook up cross-platform UI dispatcher for DownloadQueueManager & LiveStreamRecognitionService
            DownloadQueueManager.DispatcherInvoker = action => Dispatcher.UIThread.Post(action);
            LiveStreamRecognitionService.DispatcherInvoker = action => Dispatcher.UIThread.Post(action);

            SearchResultsItemsControl.ItemsSource = SearchResults;
            QueueItemsControl.ItemsSource = _queueManager.Items;
            HistoryItemsControl.ItemsSource = _historyService.Items;

            Loaded += MainWindow_Loaded;
        }

        private void MainWindow_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.F9)
            {
                _ = _liveStreamService.ToggleListeningAsync();
                e.Handled = true;
            }
        }

        private async void MainWindow_Loaded(object? sender, RoutedEventArgs e)
        {
            _dependencyManager.ProgressUpdated += status =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (StatusBadgeTextBlock != null) StatusBadgeTextBlock.Text = status;
                });
            };

            await _dependencyManager.InitializeDependenciesAsync();
            if (StatusBadgeTextBlock != null) StatusBadgeTextBlock.Text = "Ready";
        }

        #region Navigation
        private void HideAllViews()
        {
            MainScrollViewer.IsVisible = false;
            SearchScrollViewer.IsVisible = false;
            QueueScrollViewer.IsVisible = false;
            HistoryScrollViewer.IsVisible = false;
            SettingsScrollViewer.IsVisible = false;
            LiveStreamScrollViewer.IsVisible = false;
        }

        private void NavMain_Click(object? sender, RoutedEventArgs e) { HideAllViews(); MainScrollViewer.IsVisible = true; }
        private void NavSearch_Click(object? sender, RoutedEventArgs e) { HideAllViews(); SearchScrollViewer.IsVisible = true; }
        private void NavQueue_Click(object? sender, RoutedEventArgs e) { HideAllViews(); QueueScrollViewer.IsVisible = true; }
        private void NavHistory_Click(object? sender, RoutedEventArgs e) { _historyService.LoadHistory(); HideAllViews(); HistoryScrollViewer.IsVisible = true; }
        private void NavSettings_Click(object? sender, RoutedEventArgs e) { HideAllViews(); SettingsScrollViewer.IsVisible = true; }
        private void NavLiveStream_Click(object? sender, RoutedEventArgs e) { HideAllViews(); LiveStreamScrollViewer.IsVisible = true; UpdateLiveStreamUI(); }
        private void NavSpotify_Click(object? sender, RoutedEventArgs e) { HideAllViews(); SearchScrollViewer.IsVisible = true; }
        private void NavShazam_Click(object? sender, RoutedEventArgs e) { HideAllViews(); SearchScrollViewer.IsVisible = true; ShazamIdentifyButton_Click(sender, e); }
        private void BackToMain_Click(object? sender, RoutedEventArgs e) { HideAllViews(); MainScrollViewer.IsVisible = true; }
        #endregion

        #region Downloader
        private async void PasteButton_Click(object? sender, RoutedEventArgs e)
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard != null)
            {
                string? text = await clipboard.GetTextAsync();
                if (!string.IsNullOrWhiteSpace(text)) UrlTextBox.Text = text.Trim();
            }
        }

        private void UrlTextBox_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) StartDownloadButton_Click(sender, e);
        }

        private void EnableTrimCheckBox_CheckedChanged(object? sender, RoutedEventArgs e)
        {
            bool isChecked = EnableTrimCheckBox.IsChecked ?? false;
            TrimStartTextBox.IsVisible = isChecked;
            TrimEndTextBox.IsVisible = isChecked;
        }

        private async void StartDownloadButton_Click(object? sender, RoutedEventArgs e)
        {
            string url = UrlTextBox.Text?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(url)) return;

            _downloadCts?.Cancel();
            _downloadCts = new CancellationTokenSource();

            ProgressBorder.IsVisible = true;
            ProgressStatusTextBlock.Text = "Starting download...";
            ProgressPercentTextBlock.Text = "0%";
            DownloadProgressBar.Value = 0;

            int formatIdx = FormatComboBox.SelectedIndex;
            bool isAudio = formatIdx == 1 || formatIdx == 2 || formatIdx == 3;
            string audioFormat = formatIdx == 1 ? "mp3" : (formatIdx == 2 ? "flac" : (formatIdx == 3 ? "m4a" : "best"));
            string formatCode = formatIdx switch
            {
                4 => "bestvideo[height<=1080]+bestaudio/best[height<=1080]/best",
                5 => "bestvideo[height<=720]+bestaudio/best[height<=720]/best",
                _ => isAudio ? "bestaudio/best" : "bestvideo+bestaudio/best"
            };

            var progress = new Progress<DownloadProgressArgs>(args =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (args.Percentage > 0) DownloadProgressBar.Value = args.Percentage;
                    ProgressPercentTextBlock.Text = $"{args.Percentage:F1}%";
                    if (!string.IsNullOrWhiteSpace(args.StatusMessage)) ProgressStatusTextBlock.Text = args.StatusMessage;
                });
            });

            string tempDir = Path.Combine(Path.GetTempPath(), "UD_Temp_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                bool success = await _downloadService.DownloadWithYtDlpAsync(
                    url, formatCode, tempDir, _downloadFolder, isAudio, audioFormat,
                    false, 0, 0, _downloadCts.Token, null, progress);

                if (success)
                {
                    ProgressStatusTextBlock.Text = "✅ Download Completed!";
                    ProgressPercentTextBlock.Text = "100%";
                    DownloadProgressBar.Value = 100;
                    _historyService.LoadHistory();
                }
                else
                {
                    ProgressStatusTextBlock.Text = "❌ Download failed.";
                }
            }
            catch (Exception ex)
            {
                ProgressStatusTextBlock.Text = $"Error: {ex.Message}";
            }
        }

        private void AddToQueueButton_Click(object? sender, RoutedEventArgs e)
        {
            string url = UrlTextBox.Text?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(url)) return;

            int formatIdx = FormatComboBox.SelectedIndex;
            bool isAudio = formatIdx == 1 || formatIdx == 2 || formatIdx == 3;
            string audioFormat = formatIdx == 1 ? "mp3" : "best";
            string formatCode = isAudio ? "bestaudio/best" : "bestvideo+bestaudio/best";

            var item = new DownloadQueueItem
            {
                Url = url,
                Title = "Resolving media item...",
                FormatCode = formatCode,
                IsAudioOnly = isAudio,
                AudioFormat = audioFormat,
                DestinationFolder = _downloadFolder
            };

            _queueManager.Enqueue(item);
            UrlTextBox.Text = "";
            NavQueue_Click(sender, e);
        }
        #endregion

        #region Search & Shazam
        private async void SearchModeTab_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && rb.Tag is string modeStr)
            {
                _currentSearchMode = string.Equals(modeStr, "RealYouTube", StringComparison.OrdinalIgnoreCase)
                    ? SearchMode.RealYouTube
                    : SearchMode.SmartMusic;

                SearchPlatformFilterPanel.IsVisible = _currentSearchMode == SearchMode.SmartMusic;
                if (!string.IsNullOrWhiteSpace(SearchQueryTextBox.Text)) await PerformSearchAsync();
            }
        }

        private async void SearchFilter_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && rb.Tag is string tag)
            {
                _searchPlatformFilter = tag;
                _lastExecutedQuery = string.Empty;
                if (!string.IsNullOrWhiteSpace(SearchQueryTextBox.Text)) await PerformSearchAsync();
            }
        }

        private async void ExecuteSearchButton_Click(object? sender, RoutedEventArgs e) => await PerformSearchAsync();
        private async void SearchQueryTextBox_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) await PerformSearchAsync();
        }

        private async Task PerformSearchAsync()
        {
            string query = SearchQueryTextBox.Text?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(query)) return;

            bool isSameQuery = string.Equals(_lastExecutedQuery, query, StringComparison.OrdinalIgnoreCase);
            if (!isSameQuery || _smartMusicSearchTask == null || _realYouTubeSearchTask == null)
            {
                _searchCts?.Cancel();
                _searchCts = new CancellationTokenSource();
                var token = _searchCts.Token;

                _lastExecutedQuery = query;

                // Fire BOTH searches simultaneously in parallel
                _smartMusicSearchTask = _searchService.SearchAsync(query, _searchPlatformFilter, token);
                _realYouTubeSearchTask = _searchService.SearchRealYouTubeAsync(query, token);
            }

            var targetTask = _currentSearchMode == SearchMode.RealYouTube ? _realYouTubeSearchTask : _smartMusicSearchTask;
            SearchStatsTextBlock.Text = $"Searching '{query}'...";

            try
            {
                var batch = await targetTask;
                SearchResults.Clear();
                foreach (var item in batch.Items) SearchResults.Add(item);

                SearchStatsTextBlock.Text = _currentSearchMode == SearchMode.RealYouTube
                    ? $"Found {SearchResults.Count} YouTube videos for \"{query}\""
                    : $"Found {SearchResults.Count} results for \"{query}\"";
            }
            catch (Exception ex)
            {
                SearchStatsTextBlock.Text = $"Search error: {ex.Message}";
            }
        }

        private async void ShazamIdentifyButton_Click(object? sender, RoutedEventArgs e)
        {
            SearchStatsTextBlock.Text = "🎙️ Listening to audio (5s)...";
            var result = await _shazamService.ListenAndIdentifyAsync(5, AudioCaptureSource.SystemAudio);
            if (result.Success)
            {
                SearchQueryTextBox.Text = result.QueryString;
                await PerformSearchAsync();
            }
            else
            {
                SearchStatsTextBlock.Text = $"Recognition failed: {result.ErrorMessage}";
            }
        }

        private void SearchItemDownload_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is SearchResultItem item)
            {
                UrlTextBox.Text = item.SourceUrl;
                NavMain_Click(sender, e);
                StartDownloadButton_Click(sender, e);
            }
        }

        private void SearchItemAddToQueue_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is SearchResultItem item)
            {
                var qItem = new DownloadQueueItem
                {
                    Url = item.SourceUrl,
                    Title = item.Title,
                    FormatCode = "bestvideo+bestaudio/best",
                    DestinationFolder = _downloadFolder
                };
                _queueManager.Enqueue(qItem);
            }
        }
        #endregion

        #region Queue & History & Settings
        private void QueueClearFinished_Click(object? sender, RoutedEventArgs e) => _queueManager.ClearCompleted();
        private void QueueCancelItem_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is DownloadQueueItem item) _queueManager.Remove(item.Id);
        }

        private void HistoryOpenFolder_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is DownloadHistoryItem item && !string.IsNullOrWhiteSpace(item.FilePath))
            {
                string folder = Path.GetDirectoryName(item.FilePath) ?? _downloadFolder;
                OpenDirectory(folder);
            }
        }

        private async void BrowseDownloadPath_Click(object? sender, RoutedEventArgs e)
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Select Download Folder",
                AllowMultiple = false
            });

            if (folders.Count > 0)
            {
                _downloadFolder = folders[0].Path.LocalPath;
                DownloadPathTextBox.Text = _downloadFolder;
            }
        }

        private static void OpenDirectory(string path)
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    Process.Start("xdg-open", $"\"{path}\"");
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    Process.Start("explorer.exe", $"\"{path}\"");
                }
            }
            catch { }
        }
        #endregion

        #region Live Stream / DJ Scraper
        private async void LiveStreamToggle_Click(object? sender, RoutedEventArgs e)
        {
            await _liveStreamService.ToggleListeningAsync();
        }

        private void UpdateLiveStreamUI()
        {
            var session = _liveStreamService.CurrentSession;
            int count = session?.Tracks.Count ?? 0;

            if (LiveStreamItemsControl != null)
            {
                LiveStreamItemsControl.ItemsSource = session?.Tracks;
            }

            if (LiveStreamCountTextBlock != null)
            {
                LiveStreamCountTextBlock.Text = $"{count} track{(count == 1 ? "" : "s")}";
            }

            if (LiveStreamDownloadAllButton != null)
            {
                LiveStreamDownloadAllButton.Content = $"⬇ Download All ({count})";
                LiveStreamDownloadAllButton.IsEnabled = count > 0;
            }
        }

        private async void LiveStreamDownloadSingle_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is LiveDetectedTrackItem track)
            {
                var qItem = new DownloadQueueItem
                {
                    Url = $"ytsearch1:{track.QueryString}",
                    Title = track.QueryString,
                    FormatCode = "bestaudio/best",
                    DestinationFolder = _downloadFolder
                };
                _queueManager.Enqueue(qItem);
                btn.Content = "✓ Queued";
                btn.IsEnabled = false;
            }
        }

        private void LiveStreamDownloadAll_Click(object? sender, RoutedEventArgs e)
        {
            var session = _liveStreamService.CurrentSession;
            if (session == null || session.Tracks.Count == 0) return;

            foreach (var track in session.Tracks)
            {
                var qItem = new DownloadQueueItem
                {
                    Url = $"ytsearch1:{track.QueryString}",
                    Title = track.QueryString,
                    FormatCode = "bestaudio/best",
                    DestinationFolder = _downloadFolder
                };
                _queueManager.Enqueue(qItem);
            }

            LiveStreamDownloadAllButton.Content = $"✓ Queued ({session.Tracks.Count})";
            LiveStreamDownloadAllButton.IsEnabled = false;
        }
        #endregion
    }
}
