using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using UniversalDownloader.Models;
using UniversalDownloader.Services;

namespace UniversalDownloader
{
    public partial class MainWindow
    {
        private LiveStreamRecognitionService? _liveStreamService;
        private DispatcherTimer? _liveStreamSessionTimer;

        private void InitializeLiveStreamFeature()
        {
            try
            {
                if (_audioCaptureService != null && _shazamService != null)
                {
                    _liveStreamService = new LiveStreamRecognitionService(_audioCaptureService, _shazamService);
                    _liveStreamService.ListeningStateChanged += OnLiveStreamListeningStateChanged;
                    _liveStreamService.TrackDetected += OnLiveStreamTrackDetected;
                    _liveStreamService.AudioLevelChanged += OnLiveStreamAudioLevelChanged;
                    _liveStreamService.StatusUpdated += OnLiveStreamStatusUpdated;

                    if (LiveStreamSessionsItemsControl != null)
                    {
                        LiveStreamSessionsItemsControl.ItemsSource = _liveStreamService.SessionHistory;
                    }
                }

                _liveStreamSessionTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(1)
                };
                _liveStreamSessionTimer.Tick += LiveStreamSessionTimer_Tick;

                UpdateLiveStreamHotkeyDisplay();

                // Hook window-level hotkey preview
                this.PreviewKeyDown += MainWindow_LiveStream_PreviewKeyDown;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to initialize LiveStream feature: {ex.Message}");
            }
        }

        public void UpdateLiveStreamHotkeyDisplay()
        {
            if (LiveStreamHotkeyBadgeTextBlock != null)
            {
                LiveStreamHotkeyBadgeTextBlock.Text = $"[{LiveStreamHotkey}]";
            }
            if (LiveStreamNavButton != null)
            {
                LiveStreamNavButton.ToolTip = $"Live Stream / DJ Scraper (Continuous PC Audio Recognition) [{LiveStreamHotkey}]";
            }
        }

        private void MainWindow_LiveStream_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Do not trigger hotkey if user is actively typing in a TextBox
            if (e.OriginalSource is TextBox || e.OriginalSource is PasswordBox)
                return;

            if (IsLiveStreamHotkeyMatch(e))
            {
                e.Handled = true;
                if (_liveStreamService != null)
                {
                    _ = _liveStreamService.ToggleListeningAsync();
                }
            }
        }

        private bool IsLiveStreamHotkeyMatch(KeyEventArgs e)
        {
            string hk = LiveStreamHotkey?.Trim().ToUpperInvariant() ?? "F9";

            if (hk == "F9" && e.Key == Key.F9) return true;
            if (hk == "F8" && e.Key == Key.F8) return true;
            if (hk == "F10" && e.Key == Key.F10) return true;
            if (hk == "F11" && e.Key == Key.F11) return true;
            if (hk == "F12" && e.Key == Key.F12) return true;

            if (hk.Contains("CTRL") && hk.Contains("SHIFT"))
            {
                bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
                bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;

                if (ctrl && shift)
                {
                    if (hk.EndsWith("S") && e.Key == Key.S) return true;
                    if (hk.EndsWith("L") && e.Key == Key.L) return true;
                }
            }

            return false;
        }

        private void LiveStreamNavButton_Click(object sender, RoutedEventArgs e)
        {
            if (MainScrollViewer != null) MainScrollViewer.Visibility = Visibility.Collapsed;
            if (HistoryScrollViewer != null) HistoryScrollViewer.Visibility = Visibility.Collapsed;
            if (ConverterScrollViewer != null) ConverterScrollViewer.Visibility = Visibility.Collapsed;
            if (QueueScrollViewer != null) QueueScrollViewer.Visibility = Visibility.Collapsed;
            if (SearchScrollViewer != null) SearchScrollViewer.Visibility = Visibility.Collapsed;
            if (SettingsScrollViewer != null) SettingsScrollViewer.Visibility = Visibility.Collapsed;
            if (LiveStreamScrollViewer != null) LiveStreamScrollViewer.Visibility = Visibility.Visible;

            UpdateLiveStreamUI();
        }

        private void BackFromLiveStream_Click(object sender, RoutedEventArgs e)
        {
            if (LiveStreamScrollViewer != null) LiveStreamScrollViewer.Visibility = Visibility.Collapsed;
            if (MainScrollViewer != null) MainScrollViewer.Visibility = Visibility.Visible;
        }

        private async void LiveStreamToggleButton_Click(object sender, RoutedEventArgs e)
        {
            if (_liveStreamService == null) return;
            await _liveStreamService.ToggleListeningAsync();
        }

        private void OnLiveStreamListeningStateChanged(bool isListening)
        {
            Dispatcher.Invoke(() =>
            {
                if (isListening)
                {
                    if (LiveStreamToggleButton != null)
                    {
                        LiveStreamToggleButton.Content = "⏹️ Stop Listening";
                        LiveStreamToggleButton.Background = new SolidColorBrush(Color.FromRgb(239, 68, 68)); // Red-500
                    }
                    if (LiveStreamStatusPulseDot != null)
                    {
                        LiveStreamStatusPulseDot.Fill = new SolidColorBrush(Color.FromRgb(16, 185, 129)); // Green
                    }
                    _liveStreamSessionTimer?.Start();
                }
                else
                {
                    if (LiveStreamToggleButton != null)
                    {
                        LiveStreamToggleButton.Content = "🎙️ Start Listening (PC Audio)";
                        LiveStreamToggleButton.Background = new SolidColorBrush(Color.FromRgb(99, 102, 241)); // Indigo-500
                    }
                    if (LiveStreamStatusPulseDot != null)
                    {
                        LiveStreamStatusPulseDot.Fill = new SolidColorBrush(Color.FromRgb(113, 113, 122)); // Gray
                    }
                    _liveStreamSessionTimer?.Stop();
                    if (LiveStreamAudioLevelProgressBar != null) LiveStreamAudioLevelProgressBar.Value = 0;
                }

                UpdateLiveStreamUI();
            });
        }

        private void OnLiveStreamTrackDetected(LiveDetectedTrackItem track)
        {
            Dispatcher.Invoke(() =>
            {
                UpdateLiveStreamUI();
            });
        }

        private void OnLiveStreamAudioLevelChanged(float level)
        {
            Dispatcher.Invoke(() =>
            {
                if (LiveStreamAudioLevelProgressBar != null && _liveStreamService?.IsListening == true)
                {
                    LiveStreamAudioLevelProgressBar.Value = Math.Min(1.0, level * 2.5);
                }
            });
        }

        private void OnLiveStreamStatusUpdated(string status)
        {
            Dispatcher.Invoke(() =>
            {
                if (LiveStreamStatusTextBlock != null)
                {
                    LiveStreamStatusTextBlock.Text = status;
                }
            });
        }

        private void LiveStreamSessionTimer_Tick(object? sender, EventArgs e)
        {
            if (_liveStreamService?.CurrentSession != null && LiveStreamSessionTimeTextBlock != null)
            {
                LiveStreamSessionTimeTextBlock.Text = $"Session: {_liveStreamService.CurrentSession.DurationString}";
            }
        }

        private void UpdateLiveStreamUI()
        {
            var session = _liveStreamService?.CurrentSession;
            int count = session?.Tracks.Count ?? 0;

            if (LiveStreamTracksItemsControl != null)
            {
                LiveStreamTracksItemsControl.ItemsSource = session?.Tracks;
            }

            if (LiveStreamEmptyStateBorder != null)
            {
                LiveStreamEmptyStateBorder.Visibility = count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }

            if (LiveStreamTrackCountTextBlock != null)
            {
                LiveStreamTrackCountTextBlock.Text = $"{count} track{(count == 1 ? "" : "s")} detected";
            }

            if (LiveStreamDownloadAllButton != null)
            {
                LiveStreamDownloadAllButton.Content = $"⬇ Download All ({count})";
                LiveStreamDownloadAllButton.IsEnabled = count > 0;
            }
        }

        private void LiveStreamClearCurrentList_Click(object sender, RoutedEventArgs e)
        {
            if (_liveStreamService?.CurrentSession != null)
            {
                _liveStreamService.CurrentSession.Tracks.Clear();
                UpdateLiveStreamUI();
            }
        }

        private void LiveStreamToggleSessionsButton_Click(object sender, RoutedEventArgs e)
        {
            if (LiveStreamSessionsBorder != null)
            {
                bool isVisible = LiveStreamSessionsBorder.Visibility == Visibility.Visible;
                LiveStreamSessionsBorder.Visibility = isVisible ? Visibility.Collapsed : Visibility.Visible;
                if (LiveStreamSessionsItemsControl != null && _liveStreamService != null)
                {
                    LiveStreamSessionsItemsControl.ItemsSource = _liveStreamService.SessionHistory;
                }
            }
        }

        private void LiveStreamCloseSessions_Click(object sender, RoutedEventArgs e)
        {
            if (LiveStreamSessionsBorder != null)
            {
                LiveStreamSessionsBorder.Visibility = Visibility.Collapsed;
            }
        }

        private void LiveStreamLoadPastSession_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is LiveStreamSession pastSession)
            {
                if (LiveStreamTracksItemsControl != null)
                {
                    LiveStreamTracksItemsControl.ItemsSource = pastSession.Tracks;
                }
                if (LiveStreamEmptyStateBorder != null)
                {
                    LiveStreamEmptyStateBorder.Visibility = pastSession.Tracks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                }
                if (LiveStreamTrackCountTextBlock != null)
                {
                    LiveStreamTrackCountTextBlock.Text = $"{pastSession.Tracks.Count} tracks (Loaded Session)";
                }
                if (LiveStreamDownloadAllButton != null)
                {
                    LiveStreamDownloadAllButton.Content = $"⬇ Download All ({pastSession.Tracks.Count})";
                    LiveStreamDownloadAllButton.IsEnabled = pastSession.Tracks.Count > 0;
                }
                if (LiveStreamSessionsBorder != null)
                {
                    LiveStreamSessionsBorder.Visibility = Visibility.Collapsed;
                }
            }
        }

        private async void LiveStreamDownloadPastSession_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is LiveStreamSession pastSession)
            {
                foreach (var track in pastSession.Tracks)
                {
                    await EnqueueLiveTrackAsync(track);
                }
                ShowToast($"Queued {pastSession.Tracks.Count} tracks for download!");
            }
        }

        private async void LiveStreamTrackDownload_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is LiveDetectedTrackItem track)
            {
                btn.IsEnabled = false;
                btn.Content = "⏳ Downloading...";
                await DownloadLiveTrackDirectAsync(track);
                btn.Content = "✓ Downloaded";
                btn.IsEnabled = true;
            }
        }

        private async void LiveStreamTrackAddToQueue_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is LiveDetectedTrackItem track)
            {
                await EnqueueLiveTrackAsync(track);
                btn.Content = "✓ Queued";
                btn.IsEnabled = false;
                ShowToast($"Queued: {track.QueryString}");
            }
        }

        private void LiveStreamTrackSearch_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is LiveDetectedTrackItem track)
            {
                SearchNavButton_Click(sender, e);
                if (SearchQueryTextBox != null)
                {
                    SearchQueryTextBox.Text = track.QueryString;
                    SearchQueryTextBox.Foreground = System.Windows.Media.Brushes.White;
                    _ = PerformSearchAsync();
                }
            }
        }

        private async void LiveStreamDownloadAllButton_Click(object sender, RoutedEventArgs e)
        {
            var session = _liveStreamService?.CurrentSession;
            if (session == null || session.Tracks.Count == 0) return;

            LiveStreamDownloadAllButton.IsEnabled = false;
            LiveStreamDownloadAllButton.Content = "⏳ Queuing...";

            int queuedCount = 0;
            foreach (var track in session.Tracks)
            {
                await EnqueueLiveTrackAsync(track);
                queuedCount++;
            }

            ShowToast($"Queued {queuedCount} detected tracks to Download Queue!");
            LiveStreamDownloadAllButton.Content = $"✓ Queued ({queuedCount})";
            await Task.Delay(2000);
            UpdateLiveStreamUI();
        }

        private async Task DownloadLiveTrackDirectAsync(LiveDetectedTrackItem track)
        {
            try
            {
                string searchUrl = $"ytsearch1:{track.QueryString}";
                string savePath = SelectedDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);

                if (_downloadService != null)
                {
                    await _downloadService.DownloadWithYtDlpAsync(
                        url: searchUrl,
                        formatSelection: "bestaudio/best",
                        tempDownloadFolder: Downloader.App.AppTempDirectory,
                        finalDestinationFolder: savePath,
                        extractAudio: true,
                        audioFormat: "mp3",
                        useTrimming: false,
                        trimStartSeconds: 0,
                        trimEndSeconds: 0,
                        cancellationToken: CancellationToken.None);
                }
                ShowToast($"Downloaded: {track.QueryString}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to download live track: {ex.Message}");
                ShowToast($"Download failed: {ex.Message}");
            }
        }

        private Task EnqueueLiveTrackAsync(LiveDetectedTrackItem track)
        {
            try
            {
                string searchUrl = $"ytsearch1:{track.QueryString}";
                string savePath = SelectedDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);

                var queueItem = new DownloadQueueItem
                {
                    Title = track.QueryString,
                    Url = searchUrl,
                    DestinationFolder = savePath,
                    Platform = "YouTube",
                    IsAudioOnly = true,
                    AudioFormat = "mp3",
                    FormatCode = "bestaudio/best"
                };

                _queueManager.Enqueue(queueItem);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to enqueue live track: {ex.Message}");
            }
            return Task.CompletedTask;
        }
    }
}
