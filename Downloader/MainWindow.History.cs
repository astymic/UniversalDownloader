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
                    string urlToCopy = item.Url;
                    if (string.IsNullOrWhiteSpace(urlToCopy) && !string.IsNullOrWhiteSpace(item.Title))
                    {
                        urlToCopy = $"https://www.youtube.com/results?search_query={Uri.EscapeDataString(item.Title)}";
                    }

                    if (!string.IsNullOrWhiteSpace(urlToCopy))
                    {
                        Clipboard.SetDataObject(urlToCopy, true);

                        string originalContent = btn.Content?.ToString() ?? "📋 Link";
                        btn.Content = "✓ Copied!";
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
                    ModernMessageBox.Show($"Could not copy link to clipboard: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error, this);
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
