using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using UniversalDownloader.Models;
using UniversalDownloader.Controls;
using UniversalDownloader.Services;

namespace UniversalDownloader
{
    public partial class MainWindow
    {
        private SearchService? _searchService;
        private AudioCaptureService? _audioCaptureService;
        private ShazamRecognitionService? _shazamService;
        private CancellationTokenSource? _shazamCts;

        public ObservableCollection<SearchResultItem> SearchResults { get; } = new();
        private string _activeSearchPlatformFilter = "All";
        private CancellationTokenSource? _searchCts;

        private void InitializeSearchBindings()
        {
            _searchService = new SearchService(_dependencyManager);
            _audioCaptureService = new AudioCaptureService();
            _shazamService = new ShazamRecognitionService(_audioCaptureService);

            _shazamService.AudioLevelChanged += level =>
            {
                Dispatcher.InvokeAsync(() =>
                {
                    if (ShazamAudioLevelProgressBar != null)
                    {
                        ShazamAudioLevelProgressBar.Value = level;
                    }
                });
            };

            if (SearchResultsItemsControl != null)
            {
                SearchResultsItemsControl.ItemsSource = SearchResults;
            }
        }

        private void SearchNavButton_Click(object sender, RoutedEventArgs e)
        {
            CollapseSpotifyDrawer();

            if (SearchScrollViewer != null && SearchScrollViewer.Visibility == Visibility.Visible)
            {
                // Toggle back to Main view
                SearchScrollViewer.Visibility = Visibility.Collapsed;
                if (MainScrollViewer != null) MainScrollViewer.Visibility = Visibility.Visible;
                return;
            }

            if (MainScrollViewer != null) MainScrollViewer.Visibility = Visibility.Collapsed;
            if (SettingsScrollViewer != null) SettingsScrollViewer.Visibility = Visibility.Collapsed;
            if (ConverterScrollViewer != null) ConverterScrollViewer.Visibility = Visibility.Collapsed;
            if (QueueScrollViewer != null) QueueScrollViewer.Visibility = Visibility.Collapsed;
            if (HistoryScrollViewer != null) HistoryScrollViewer.Visibility = Visibility.Collapsed;

            if (SearchScrollViewer != null)
            {
                SearchScrollViewer.Visibility = Visibility.Visible;
                if (SearchQueryTextBox != null && (string.IsNullOrWhiteSpace(SearchQueryTextBox.Text) || SearchQueryTextBox.Text == "Search track, artist, music name or album..."))
                {
                    SearchQueryTextBox.Text = "Search track, artist, music name or album...";
                    SearchQueryTextBox.Foreground = (System.Windows.Media.Brush)FindResource("TextSecondaryBrush");
                }
            }
        }

        private void ShazamNavButton_Click(object sender, RoutedEventArgs e)
        {
            CollapseSpotifyDrawer();

            if (MainScrollViewer != null) MainScrollViewer.Visibility = Visibility.Collapsed;
            if (SettingsScrollViewer != null) SettingsScrollViewer.Visibility = Visibility.Collapsed;
            if (ConverterScrollViewer != null) ConverterScrollViewer.Visibility = Visibility.Collapsed;
            if (QueueScrollViewer != null) QueueScrollViewer.Visibility = Visibility.Collapsed;
            if (HistoryScrollViewer != null) HistoryScrollViewer.Visibility = Visibility.Collapsed;

            if (SearchScrollViewer != null)
            {
                SearchScrollViewer.Visibility = Visibility.Visible;
            }

            ShazamIdentifyButton_Click(sender, e);
        }

        private void BackFromSearch_Click(object sender, RoutedEventArgs e)
        {
            if (SearchScrollViewer != null) SearchScrollViewer.Visibility = Visibility.Collapsed;
            if (MainScrollViewer != null) MainScrollViewer.Visibility = Visibility.Visible;
        }

        private async void ExecuteSearchButton_Click(object sender, RoutedEventArgs e)
        {
            await PerformSearchAsync();
        }

        private async void SearchQueryTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                await PerformSearchAsync();
            }
        }

        private void SearchQueryTextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (SearchQueryTextBox != null && SearchQueryTextBox.Text == "Search track, artist, music name or album...")
            {
                SearchQueryTextBox.Text = string.Empty;
                SearchQueryTextBox.Foreground = (System.Windows.Media.Brush)FindResource("TextPrimaryBrush");
            }
        }

        private void SearchQueryTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (SearchQueryTextBox != null)
            {
                if (string.IsNullOrWhiteSpace(SearchQueryTextBox.Text))
                {
                    SearchQueryTextBox.Text = "Search track, artist, music name or album...";
                    SearchQueryTextBox.Foreground = (System.Windows.Media.Brush)FindResource("TextSecondaryBrush");
                }
                else
                {
                    SearchQueryTextBox.Foreground = (System.Windows.Media.Brush)FindResource("TextPrimaryBrush");
                }
            }
        }

        private void SearchQueryTextBox_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (SearchQueryTextBox != null && SearchQueryTextBox.Text == "Search track, artist, music name or album...")
            {
                SearchQueryTextBox.Text = string.Empty;
                SearchQueryTextBox.Foreground = (System.Windows.Media.Brush)FindResource("TextPrimaryBrush");
            }
        }

        private void SearchQueryTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (SearchQueryTextBox == null) return;
            if (SearchQueryTextBox.Text == "Search track, artist, music name or album...")
            {
                SearchQueryTextBox.Foreground = (System.Windows.Media.Brush)FindResource("TextSecondaryBrush");
            }
            else if (!string.IsNullOrEmpty(SearchQueryTextBox.Text))
            {
                SearchQueryTextBox.Foreground = (System.Windows.Media.Brush)FindResource("TextPrimaryBrush");
            }
        }

        private async void QuickSearchTag_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Content is string tag)
            {
                string cleanTag = tag.Replace("🔥", "").Replace("🎧", "").Replace("☕", "").Replace("🎹", "").Replace("🎸", "").Trim();
                if (SearchQueryTextBox != null)
                {
                    SearchQueryTextBox.Text = cleanTag;
                    SearchQueryTextBox.Foreground = System.Windows.Media.Brushes.White;
                }
                await PerformSearchAsync();
            }
        }

        private async void SearchFilter_Click(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb)
            {
                if (rb == SearchFilterAll) _activeSearchPlatformFilter = "All";
                else if (rb == SearchFilterYouTube) _activeSearchPlatformFilter = "YouTube";
                else if (rb == SearchFilterSoundCloud) _activeSearchPlatformFilter = "SoundCloud";

                if (SearchQueryTextBox != null && !string.IsNullOrWhiteSpace(SearchQueryTextBox.Text) && SearchQueryTextBox.Text != "Search track, artist, music name or album...")
                {
                    await PerformSearchAsync();
                }
            }
        }

        private async Task PerformSearchAsync()
        {
            if (SearchQueryTextBox == null || _searchService == null) return;
            string query = SearchQueryTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(query) || query == "Search track, artist, music name or album...") return;
            SearchQueryTextBox.Foreground = System.Windows.Media.Brushes.White;

            _searchCts?.Cancel();
            _searchCts = new CancellationTokenSource();

            if (SearchLoadingBorder != null) SearchLoadingBorder.Visibility = Visibility.Visible;
            if (SearchEmptyStateBorder != null) SearchEmptyStateBorder.Visibility = Visibility.Collapsed;
            if (SearchStatsTextBlock != null) SearchStatsTextBlock.Text = $"Searching '{query}'...";

            SearchResults.Clear();

            try
            {
                var batch = await _searchService.SearchAsync(query, _activeSearchPlatformFilter, _searchCts.Token);

                foreach (var item in batch.Items)
                {
                    SearchResults.Add(item);
                }

                if (SearchStatsTextBlock != null)
                {
                    if (batch.IsClosestFallback && !string.IsNullOrWhiteSpace(batch.FallbackQuery))
                    {
                        SearchStatsTextBlock.Text = $"Showing closest results for \"{batch.FallbackQuery}\" ({SearchResults.Count} found)";
                    }
                    else
                    {
                        SearchStatsTextBlock.Text = $"Found {SearchResults.Count} result{(SearchResults.Count == 1 ? "" : "s")} for \"{query}\"";
                    }
                }

                if (SearchResults.Count == 0 && SearchEmptyStateBorder != null)
                {
                    SearchEmptyStateBorder.Visibility = Visibility.Visible;
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (SearchStatsTextBlock != null) SearchStatsTextBlock.Text = $"Search failed: {ex.Message}";
            }
            finally
            {
                if (SearchLoadingBorder != null) SearchLoadingBorder.Visibility = Visibility.Collapsed;
            }
        }

        private void SearchItemOptionsToggle_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is SearchResultItem item)
            {
                item.IsOptionsExpanded = !item.IsOptionsExpanded;
            }
        }

        private void SearchItemAddToQueue_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is SearchResultItem item)
            {
                var queueItem = new DownloadQueueItem
                {
                    Title = item.Title,
                    Url = item.SourceUrl,
                    DestinationFolder = SelectedDirectory,
                    Platform = item.Platform,
                    IsAudioOnly = item.IsSoundCloud,
                    AudioFormat = item.IsSoundCloud ? "mp3" : "best",
                    FormatCode = item.IsSoundCloud ? "bestaudio/best" : "bestvideo+bestaudio/best"
                };

                _queueManager.Enqueue(queueItem);
                ShowToast($"Added to Queue: {item.Title}");
            }
        }

        private void SearchItemAddToQueueWithOptions_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is SearchResultItem item)
            {
                var selectedQuality = item.SelectedQuality;
                var queueItem = new DownloadQueueItem
                {
                    Title = item.Title,
                    Url = item.SourceUrl,
                    DestinationFolder = SelectedDirectory,
                    Platform = item.Platform,
                    IsAudioOnly = selectedQuality?.IsAudioOnly ?? item.IsSoundCloud,
                    AudioFormat = selectedQuality?.AudioFormat ?? "mp3",
                    FormatCode = selectedQuality?.FormatCode ?? "bestvideo+bestaudio/best"
                };

                _queueManager.Enqueue(queueItem);
                item.IsOptionsExpanded = false;
                ShowToast($"Added to Queue: {item.Title}");
            }
        }

        private async void SearchItemStartDownload_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is SearchResultItem item)
            {
                item.IsOptionsExpanded = false;

                // If main downloader is already downloading something, enqueue instead!
                if (_isDownloadingFile)
                {
                    SearchItemAddToQueueWithOptions_Click(sender, e);
                    return;
                }

                // Switch to main view and initiate download
                if (SearchScrollViewer != null) SearchScrollViewer.Visibility = Visibility.Collapsed;
                if (MainScrollViewer != null) MainScrollViewer.Visibility = Visibility.Visible;

                if (UrlTextBox != null)
                {
                    UrlTextBox.Text = item.SourceUrl;
                }

                _previewItemTitle = item.Title;
                _currentItemTitle = item.Title;

                if (FileNameTextBlock != null)
                {
                    FileNameTextBlock.Text = item.Title;
                    FileNameTextBlock.Visibility = Visibility.Visible;
                }

                DownloadButton_Click(this, new RoutedEventArgs());
            }
        }

        private void ShowToast(string message)
        {
            if (SearchStatsTextBlock != null)
            {
                SearchStatsTextBlock.Text = message;
            }
        }

        private async void ShazamIdentifyButton_Click(object sender, RoutedEventArgs e)
        {
            if (_shazamService == null) return;

            // Cancel any ongoing search or listen
            _shazamCts?.Cancel();
            _shazamCts = new CancellationTokenSource();

            var token = _shazamCts.Token;

            if (ShazamListeningBorder != null) ShazamListeningBorder.Visibility = Visibility.Visible;
            if (ShazamIdentifyButton != null) ShazamIdentifyButton.IsEnabled = false;

            var source = (ShazamSourceSystem != null && ShazamSourceSystem.IsChecked == true)
                ? AudioCaptureSource.SystemAudio
                : AudioCaptureSource.Microphone;

            try
            {
                // Start a visual countdown in the status text
                _ = Task.Run(async () =>
                {
                    for (int sec = 5; sec >= 1; sec--)
                    {
                        if (token.IsCancellationRequested) break;
                        int currentSec = sec;
                        await Dispatcher.InvokeAsync(() =>
                        {
                            if (ShazamStatusTextBlock != null)
                                ShazamStatusTextBlock.Text = $"Listening ({ (source == AudioCaptureSource.SystemAudio ? "PC Audio" : "Microphone") })... {currentSec}s";
                        });
                        await Task.Delay(1000, token);
                    }

                    if (!token.IsCancellationRequested)
                    {
                        await Dispatcher.InvokeAsync(() =>
                        {
                            if (ShazamStatusTextBlock != null)
                                ShazamStatusTextBlock.Text = "⚡ Identifying song with Shazam...";
                        });
                    }
                }, token);

                var trackResult = await _shazamService.ListenAndIdentifyAsync(5, source, token);

                if (trackResult.Success)
                {
                    if (ShazamStatusTextBlock != null)
                        ShazamStatusTextBlock.Text = $"✨ Identified: {trackResult.Artist} - {trackResult.Title}";

                    if (SearchQueryTextBox != null)
                    {
                        SearchQueryTextBox.Text = trackResult.QueryString;
                        SearchQueryTextBox.Foreground = System.Windows.Media.Brushes.White;
                    }

                    // Auto-execute search to present download choices immediately
                    await PerformSearchAsync();

                    await Task.Delay(3000, CancellationToken.None);
                    if (ShazamListeningBorder != null) ShazamListeningBorder.Visibility = Visibility.Collapsed;
                }
                else
                {
                    if (ShazamStatusTextBlock != null)
                        ShazamStatusTextBlock.Text = $"⚠️ {trackResult.ErrorMessage}";

                    await Task.Delay(3500, CancellationToken.None);
                    if (ShazamListeningBorder != null && !token.IsCancellationRequested)
                        ShazamListeningBorder.Visibility = Visibility.Collapsed;
                }
            }
            catch (OperationCanceledException)
            {
                if (ShazamListeningBorder != null) ShazamListeningBorder.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                if (ShazamStatusTextBlock != null)
                    ShazamStatusTextBlock.Text = $"Error: {ex.Message}";
            }
            finally
            {
                if (ShazamIdentifyButton != null) ShazamIdentifyButton.IsEnabled = true;
                if (ShazamAudioLevelProgressBar != null) ShazamAudioLevelProgressBar.Value = 0;
            }
        }

        private void ShazamCancel_Click(object sender, RoutedEventArgs e)
        {
            _shazamCts?.Cancel();
            if (ShazamListeningBorder != null) ShazamListeningBorder.Visibility = Visibility.Collapsed;
            if (ShazamIdentifyButton != null) ShazamIdentifyButton.IsEnabled = true;
        }
    }
}
