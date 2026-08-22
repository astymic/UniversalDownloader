using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using UniversalDownloader.Models;
using UniversalDownloader.Controls;
using System.Windows.Media;

namespace UniversalDownloader
{
    public partial class MainWindow
    {
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
            if (HistoryScrollViewer != null)
            {
                HistoryScrollViewer.Visibility = Visibility.Visible;
                _historyService.LoadHistory();
            }
        }

        private void BackFromHistory_Click(object sender, RoutedEventArgs e)
        {
            if (HistoryScrollViewer != null) HistoryScrollViewer.Visibility = Visibility.Collapsed;
            if (MainScrollViewer != null) MainScrollViewer.Visibility = Visibility.Visible;
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
                await _historyService.RemoveItemAsync(item.Id);
            }
        }

        private async void ClearAllHistory_Click(object sender, RoutedEventArgs e)
        {
            var result = ModernMessageBox.Show("Are you sure you want to clear all download history?", "Clear History", MessageBoxButton.YesNo, MessageBoxImage.Question, this);
            if (result == MessageBoxResult.Yes)
            {
                await _historyService.ClearHistoryAsync();
            }
        }
    }
}
