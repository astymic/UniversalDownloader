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
        public ObservableCollection<SearchResultItem> SearchResults { get; } = new();
        private string _activeSearchPlatformFilter = "All";
        private CancellationTokenSource? _searchCts;

        private void InitializeSearchBindings()
        {
            _searchService = new SearchService(_dependencyManager);
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
                SearchQueryTextBox?.Focus();
            }
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

        private void SearchQueryTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (SearchPlaceholderTextBlock != null && SearchQueryTextBox != null)
            {
                SearchPlaceholderTextBlock.Visibility = string.IsNullOrEmpty(SearchQueryTextBox.Text)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
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

                if (SearchQueryTextBox != null && !string.IsNullOrWhiteSpace(SearchQueryTextBox.Text))
                {
                    await PerformSearchAsync();
                }
            }
        }

        private async Task PerformSearchAsync()
        {
            if (SearchQueryTextBox == null || _searchService == null) return;
            string query = SearchQueryTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(query)) return;

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
    }
}
