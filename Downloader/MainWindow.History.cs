using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using UniversalDownloader.Models;
using UniversalDownloader.Controls;
using System.Windows.Media;

namespace UniversalDownloader
{
    public partial class MainWindow
    {
        public ObservableCollection<DownloadHistoryItem> FilteredHistoryItems { get; } = new();
        private string _activeHistoryFilter = "All";
        private string _historySearchQuery = string.Empty;

        private void InitializeHistoryBindings()
        {
            _historyService.HistoryChanged += () => Dispatcher.Invoke(ApplyHistoryFilter);
            ApplyHistoryFilter();
        }

        private void HistoryButton_Click(object sender, RoutedEventArgs e)
        {
            CollapseSpotifyDrawer();

            if (HistoryScrollViewer != null && HistoryScrollViewer.Visibility == Visibility.Visible)
            {
                // Toggle back to Main view
                HistoryScrollViewer.Visibility = Visibility.Collapsed;
                if (MainScrollViewer != null) MainScrollViewer.Visibility = Visibility.Visible;
                return;
            }

            if (MainScrollViewer != null) MainScrollViewer.Visibility = Visibility.Collapsed;
            if (SettingsScrollViewer != null) SettingsScrollViewer.Visibility = Visibility.Collapsed;
            if (ConverterScrollViewer != null) ConverterScrollViewer.Visibility = Visibility.Collapsed;
            if (QueueScrollViewer != null) QueueScrollViewer.Visibility = Visibility.Collapsed;
            if (HistoryScrollViewer != null)
            {
                HistoryScrollViewer.Visibility = Visibility.Visible;
                _historyService.LoadHistory();
                ApplyHistoryFilter();
            }
        }

        private void BackFromHistory_Click(object sender, RoutedEventArgs e)
        {
            if (HistoryScrollViewer != null) HistoryScrollViewer.Visibility = Visibility.Collapsed;
            if (MainScrollViewer != null) MainScrollViewer.Visibility = Visibility.Visible;
        }

        public void ApplyHistoryFilter()
        {
            if (HistoryItemsControl == null) return;

            var items = _historyService.Items.AsEnumerable();

            // 1. Platform / Media Type Filter
            if (!string.IsNullOrWhiteSpace(_activeHistoryFilter) && _activeHistoryFilter != "All")
            {
                if (_activeHistoryFilter == "Audio")
                {
                    items = items.Where(x => x.IsAudio);
                }
                else if (_activeHistoryFilter == "Video")
                {
                    items = items.Where(x => !x.IsAudio);
                }
                else
                {
                    items = items.Where(x => x.Platform.Equals(_activeHistoryFilter, StringComparison.OrdinalIgnoreCase));
                }
            }

            // 2. Search Query Filter
            if (!string.IsNullOrWhiteSpace(_historySearchQuery))
            {
                string q = _historySearchQuery.Trim();
                items = items.Where(x => 
                    (!string.IsNullOrEmpty(x.Title) && x.Title.Contains(q, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrEmpty(x.FilePath) && x.FilePath.Contains(q, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrEmpty(x.Url) && x.Url.Contains(q, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrEmpty(x.Platform) && x.Platform.Contains(q, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrEmpty(x.FormatExtension) && x.FormatExtension.Contains(q, StringComparison.OrdinalIgnoreCase)));
            }

            var resultList = items.ToList();

            FilteredHistoryItems.Clear();
            foreach (var item in resultList)
            {
                FilteredHistoryItems.Add(item);
            }

            // Update match count
            if (HistoryMatchCountTextBlock != null)
            {
                int total = _historyService.Items.Count;
                HistoryMatchCountTextBlock.Text = resultList.Count == total
                    ? $"{total} download{(total == 1 ? "" : "s")}"
                    : $"Showing {resultList.Count} of {total}";
            }

            // Update empty state
            if (HistoryEmptyStatePanel != null)
            {
                HistoryEmptyStatePanel.Visibility = (resultList.Count == 0 && _historyService.Items.Count > 0)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
        }

        private void HistorySearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (HistorySearchTextBox != null)
            {
                _historySearchQuery = HistorySearchTextBox.Text;
                ApplyHistoryFilter();
            }
        }

        private void HistoryClearSearchButton_Click(object sender, RoutedEventArgs e)
        {
            if (HistorySearchTextBox != null)
            {
                HistorySearchTextBox.Text = string.Empty;
                HistorySearchTextBox.Focus();
            }
        }

        private void HistoryFilterChip_Click(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton radio && radio.Tag != null)
            {
                _activeHistoryFilter = radio.Tag.ToString() ?? "All";
                ApplyHistoryFilter();
            }
        }

        private void HistoryResetFilters_Click(object sender, RoutedEventArgs e)
        {
            _activeHistoryFilter = "All";
            _historySearchQuery = string.Empty;

            if (HistorySearchTextBox != null)
            {
                HistorySearchTextBox.Text = string.Empty;
            }

            if (FilterChipAll != null)
            {
                FilterChipAll.IsChecked = true;
            }

            ApplyHistoryFilter();
        }

        private void HistoryPreviewPlay_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is DownloadHistoryItem item)
            {
                PlayHistoryItemPreview(item);
            }
        }

        private void HistoryOpenFile_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is DownloadHistoryItem item)
            {
                try
                {
                    if (File.Exists(item.FilePath))
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = item.FilePath,
                            UseShellExecute = true
                        });
                    }
                    else
                    {
                        ModernMessageBox.Show("File no longer exists on disk at the saved location.", "File Not Found", MessageBoxButton.OK, MessageBoxImage.Warning, this);
                    }
                }
                catch (Exception ex)
                {
                    ModernMessageBox.Show($"Could not open file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error, this);
                }
            }
        }

        private void HistoryOpenFolder_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is DownloadHistoryItem item)
            {
                try
                {
                    if (File.Exists(item.FilePath))
                    {
                        Process.Start("explorer.exe", $"/select,\"{item.FilePath}\"");
                    }
                    else if (Directory.Exists(Path.GetDirectoryName(item.FilePath)))
                    {
                        Process.Start("explorer.exe", Path.GetDirectoryName(item.FilePath)!);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to open folder: {ex.Message}");
                }
            }
        }

        private void HistoryCopyUrl_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is DownloadHistoryItem item)
            {
                try
                {
                    string urlToOpen = item.Url;
                    if (string.IsNullOrWhiteSpace(urlToOpen) && !string.IsNullOrWhiteSpace(item.Title))
                    {
                        urlToOpen = $"https://www.youtube.com/results?search_query={Uri.EscapeDataString(item.Title)}";
                    }
                    else if (urlToOpen.StartsWith("ytsearch", StringComparison.OrdinalIgnoreCase))
                    {
                        string query = urlToOpen.Substring(urlToOpen.IndexOf(':') + 1);
                        urlToOpen = $"https://www.youtube.com/results?search_query={Uri.EscapeDataString(query)}";
                    }

                    if (!string.IsNullOrWhiteSpace(urlToOpen))
                    {
                        // 1. Copy link to clipboard
                        try
                        {
                            Clipboard.SetDataObject(urlToOpen, true);
                        }
                        catch (Exception cbEx)
                        {
                            Debug.WriteLine($"Failed to copy to clipboard: {cbEx.Message}");
                        }

                        // 2. Open link in default browser
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = urlToOpen,
                            UseShellExecute = true
                        });

                        // 3. Visual button feedback
                        string originalContent = btn.Content?.ToString() ?? "🔗 Link";
                        btn.Content = "✓ Opened!";
                        btn.Foreground = (Brush)FindResource("SuccessBrush");

                        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
                        timer.Tick += (s, ev) =>
                        {
                            btn.Content = originalContent;
                            btn.Foreground = (Brush)FindResource("TextPrimaryBrush");
                            timer.Stop();
                        };
                        timer.Start();
                    }
                    else
                    {
                        ModernMessageBox.Show("No source link was recorded for this item.", "Link Unavailable", MessageBoxButton.OK, MessageBoxImage.Information, this);
                    }
                }
                catch (Exception ex)
                {
                    ModernMessageBox.Show($"Could not open link in browser: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error, this);
                }
            }
        }

        private async void HistoryDeleteItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is DownloadHistoryItem item)
            {
                // If this item is currently playing in the preview player, stop it
                if (_audioPreviewService?.CurrentItem?.Id == item.Id)
                {
                    _audioPreviewService.Stop();
                    if (BottomPlayerBar != null) BottomPlayerBar.Visibility = Visibility.Collapsed;
                }

                await _historyService.RemoveItemAsync(item.Id);
            }
        }

        private async void ClearAllHistory_Click(object sender, RoutedEventArgs e)
        {
            var result = ModernMessageBox.Show("Are you sure you want to clear all download history?", "Clear History", MessageBoxButton.YesNo, MessageBoxImage.Question, this);
            if (result == MessageBoxResult.Yes)
            {
                if (_audioPreviewService != null)
                {
                    _audioPreviewService.Stop();
                    if (BottomPlayerBar != null) BottomPlayerBar.Visibility = Visibility.Collapsed;
                }
                await _historyService.ClearHistoryAsync();
            }
        }

        private void HistoryItemTitle_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is TextBlock tb && tb.DataContext is DownloadHistoryItem item)
            {
                string titleToCopy = item.Title;
                if (!string.IsNullOrWhiteSpace(titleToCopy))
                {
                    try
                    {
                        Clipboard.SetDataObject(titleToCopy, true);

                        string originalText = item.Title;
                        tb.Text = "✓ Copied to clipboard!";
                        tb.Foreground = (Brush)FindResource("SuccessBrush");

                        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
                        timer.Tick += (s, ev) =>
                        {
                            tb.Text = originalText;
                            tb.Foreground = (Brush)FindResource("TextPrimaryBrush");
                            timer.Stop();
                        };
                        timer.Start();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Failed to copy title to clipboard: {ex.Message}");
                    }
                }
            }
        }
    }
}
