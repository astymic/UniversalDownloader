using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using UniversalDownloader.Models;
using UniversalDownloader.Controls;

namespace UniversalDownloader
{
    public partial class MainWindow
    {
        private void InitializeQueueBindings()
        {
            if (_queueManager != null)
            {
                _queueManager.QueueChanged += () => Dispatcher.Invoke(OnQueueStateUpdated);
                if (QueueItemsControl != null)
                {
                    QueueItemsControl.ItemsSource = _queueManager.Items;
                }
                OnQueueStateUpdated();
            }
        }

        private void OnQueueStateUpdated()
        {
            UpdateQueueBadge();
            UpdateQueueSummary();
        }

        private void UpdateQueueBadge()
        {
            if (QueueBadgeBorder == null || QueueBadgeTextBlock == null || _queueManager == null) return;

            int activeCount = _queueManager.Items.Count(x => x.Status == QueueItemStatus.Queued || x.Status == QueueItemStatus.Downloading);
            if (activeCount > 0)
            {
                QueueBadgeBorder.Visibility = Visibility.Visible;
                QueueBadgeTextBlock.Text = activeCount > 99 ? "99+" : activeCount.ToString();
            }
            else
            {
                QueueBadgeBorder.Visibility = Visibility.Collapsed;
            }
        }

        private void UpdateQueueSummary()
        {
            if (_queueManager == null) return;

            int total = _queueManager.Items.Count;
            int downloading = _queueManager.Items.Count(x => x.Status == QueueItemStatus.Downloading);
            int queued = _queueManager.Items.Count(x => x.Status == QueueItemStatus.Queued);
            int completed = _queueManager.Items.Count(x => x.Status == QueueItemStatus.Completed);

            if (QueueStatusCountTextBlock != null)
            {
                if (total == 0)
                {
                    QueueStatusCountTextBlock.Text = "No active downloads in queue";
                }
                else
                {
                    QueueStatusCountTextBlock.Text = $"{downloading} Downloading • {queued} Queued • {completed} Completed • {total} Total";
                }
            }

            if (QueueEmptyStatePanel != null)
            {
                QueueEmptyStatePanel.Visibility = total == 0 ? Visibility.Visible : Visibility.Collapsed;
            }

            if (QueueItemsControl != null)
            {
                QueueItemsControl.Visibility = total > 0 ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        public void ShowQueueView()
        {
            CollapseSpotifyDrawer();

            if (MainScrollViewer != null) MainScrollViewer.Visibility = Visibility.Collapsed;
            if (SettingsScrollViewer != null) SettingsScrollViewer.Visibility = Visibility.Collapsed;
            if (HistoryScrollViewer != null) HistoryScrollViewer.Visibility = Visibility.Collapsed;
            if (ConverterScrollViewer != null) ConverterScrollViewer.Visibility = Visibility.Collapsed;
            if (SearchScrollViewer != null) SearchScrollViewer.Visibility = Visibility.Collapsed;
            if (LiveStreamScrollViewer != null) LiveStreamScrollViewer.Visibility = Visibility.Collapsed;

            if (QueueScrollViewer != null)
            {
                QueueScrollViewer.Visibility = Visibility.Visible;
                OnQueueStateUpdated();
            }
        }

        private void QueueButton_Click(object sender, RoutedEventArgs e)
        {
            CollapseSpotifyDrawer();

            if (QueueScrollViewer != null && QueueScrollViewer.Visibility == Visibility.Visible)
            {
                // Toggle back to main
                QueueScrollViewer.Visibility = Visibility.Collapsed;
                if (MainScrollViewer != null) MainScrollViewer.Visibility = Visibility.Visible;
                return;
            }

            ShowQueueView();
        }

        private void BackFromQueue_Click(object sender, RoutedEventArgs e)
        {
            if (QueueScrollViewer != null) QueueScrollViewer.Visibility = Visibility.Collapsed;
            if (MainScrollViewer != null) MainScrollViewer.Visibility = Visibility.Visible;
        }

        private void BatchImportFromQueue_Click(object sender, RoutedEventArgs e)
        {
            BatchImportButton_Click(sender, e);
        }

        private void ClearCompletedQueue_Click(object sender, RoutedEventArgs e)
        {
            _queueManager.ClearCompleted();
        }

        private void CancelAllQueue_Click(object sender, RoutedEventArgs e)
        {
            var result = ModernMessageBox.Show("Cancel all queued and active downloads?", "Cancel All Downloads", MessageBoxButton.YesNo, MessageBoxImage.Question, this);
            if (result == MessageBoxResult.Yes)
            {
                _queueManager.CancelAll();
            }
        }

        private void QueueCancelItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string itemId)
            {
                _queueManager.Cancel(itemId);
            }
        }

        private void QueueRetryItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string itemId)
            {
                _queueManager.Retry(itemId);
            }
        }

        private void QueueDeleteItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string itemId)
            {
                _queueManager.Remove(itemId);
            }
        }

        private void QueueOpenFolder_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is DownloadQueueItem item)
            {
                try
                {
                    string target = !string.IsNullOrWhiteSpace(item.DownloadedFilePath) && File.Exists(item.DownloadedFilePath)
                        ? item.DownloadedFilePath
                        : item.DestinationFolder;

                    if (File.Exists(target))
                    {
                        Process.Start("explorer.exe", $"/select,\"{target}\"");
                    }
                    else if (Directory.Exists(target))
                    {
                        Process.Start("explorer.exe", target);
                    }
                    else
                    {
                        Process.Start("explorer.exe", SelectedDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.MyVideos));
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to open queue folder: {ex.Message}");
                }
            }
        }

        private void QueuePlayPreview_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is DownloadQueueItem item && !string.IsNullOrWhiteSpace(item.DownloadedFilePath) && File.Exists(item.DownloadedFilePath))
            {
                PlayHistoryItemPreview(new DownloadHistoryItem
                {
                    Title = item.Title,
                    FilePath = item.DownloadedFilePath,
                    IsAudio = item.IsAudioOnly
                });
            }
        }

        private void AddToQueueButton_Click(object sender, RoutedEventArgs e)
        {
            EnqueueCurrentMainUrl();
        }

        public void EnqueueCurrentMainUrl()
        {
            string url = UrlTextBox?.Text?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(url) || url == "Paste URL here...") return;

            string targetFolder = SelectedDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
            
            bool isSpotify = _downloadService.IsSpotifyLink(url);
            bool isSoundCloud = _downloadService.IsSoundCloudLink(url);

            var selectedQuality = YouTubeQualityComboBox?.SelectedItem as YouTubeQualityItem;
            string formatCode = selectedQuality?.FormatCode ?? (isSpotify || isSoundCloud ? "bestaudio/best" : "bestvideo+bestaudio/best");
            bool isAudioOnly = selectedQuality?.IsAudioOnly ?? (isSpotify || isSoundCloud);
            string audioFormat = selectedQuality?.AudioFormat ?? (isSpotify ? "mp3" : "mp3");

            string title = !string.IsNullOrWhiteSpace(_currentItemTitle) && _currentItemTitle != "Paste URL here..."
                ? _currentItemTitle
                : (isSpotify ? "Spotify Track (Resolving title...)" : "Media Item (Resolving title...)");

            var queueItem = new DownloadQueueItem
            {
                Url = url,
                Title = title,
                DestinationFolder = targetFolder,
                IsAudioOnly = isAudioOnly,
                AudioFormat = audioFormat,
                FormatCode = formatCode,
                Status = QueueItemStatus.Queued,
                StatusText = isSpotify ? "Queued (Spotify Audio)" : "Queued"
            };
            queueItem.UpdatePlatformFromUrl();

            _queueManager.Enqueue(queueItem);

            // Reset main window inputs
            if (UrlTextBox != null)
            {
                UrlTextBox.Text = "Paste URL here...";
                UrlTextBox.Foreground = (System.Windows.Media.Brush)FindResource("TextSecondaryBrush");
            }
            if (DownloadButton != null) DownloadButton.IsEnabled = false;
            if (AddToQueueButton != null) AddToQueueButton.IsEnabled = false;

            ShowQueueView();
        }
    }
}
