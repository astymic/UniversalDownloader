using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using UniversalDownloader.Models;

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
                        MessageBox.Show("File no longer exists on disk at the saved location.", "File Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Could not open file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
                    if (!string.IsNullOrWhiteSpace(item.Url))
                    {
                        Clipboard.SetText(item.Url);
                    }
                }
                catch { }
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
            var result = MessageBox.Show("Are you sure you want to clear all download history?", "Clear History", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                await _historyService.ClearHistoryAsync();
            }
        }
    }
}
