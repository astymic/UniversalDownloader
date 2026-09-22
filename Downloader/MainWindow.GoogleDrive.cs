using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using UniversalDownloader.Models;
using UniversalDownloader.Services;

namespace UniversalDownloader
{
    public partial class MainWindow
    {
        private readonly GoogleDriveFolderService _googleDriveFolderService = new();
        private GoogleDriveFolderResult? _currentGoogleDriveResult;
        private CancellationTokenSource? _googleDriveScanCts;

        public async Task LoadGoogleDriveFolderAsync(string url)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                GoogleDriveDrawerTitleText.Text = "Google Drive Folder";
                GoogleDriveDrawerMetaText.Text = "Connecting and scanning contents...";
                GoogleDriveLoadingStatusText.Text = "Connecting to Google Drive...";
                GoogleDriveLoadingPanel.Visibility = Visibility.Visible;
                GoogleDriveTreeBorder.Visibility = Visibility.Collapsed;
                GoogleDriveSelectedCountText.Text = "Selected: 0 files";
                GoogleDriveDownloadSelectedButton.IsEnabled = false;
                GoogleDriveDownloadAllButton.IsEnabled = false;
                GoogleDriveDestinationText.Text = SelectedDirectory;
                GoogleDriveTreeView.ItemsSource = null;
                GoogleDriveDrawerOverlay.Visibility = Visibility.Visible;
            });

            _googleDriveScanCts?.Cancel();
            _googleDriveScanCts = new CancellationTokenSource();
            var token = _googleDriveScanCts.Token;

            var progress = new Progress<string>(status =>
            {
                Dispatcher.InvokeAsync(() =>
                {
                    GoogleDriveLoadingStatusText.Text = status;
                    GoogleDriveDrawerMetaText.Text = status;
                });
            });

            try
            {
                var result = await _googleDriveFolderService.FetchFolderTreeAsync(url, progress, token);
                _currentGoogleDriveResult = result;

                await Dispatcher.InvokeAsync(() =>
                {
                    GoogleDriveDrawerTitleText.Text = result.RootTitle;
                    string totalSizePart = result.TotalBytes > 0 ? $" • Total {result.TotalSizeString}" : "";
                    GoogleDriveDrawerMetaText.Text = $"{result.TotalFoldersCount} folders • {result.TotalFilesCount} files{totalSizePart}";
                    GoogleDriveTreeView.ItemsSource = result.Items;
                    GoogleDriveLoadingPanel.Visibility = Visibility.Collapsed;
                    GoogleDriveTreeBorder.Visibility = Visibility.Visible;
                    GoogleDriveDownloadAllButton.IsEnabled = result.TotalFilesCount > 0;

                    UpdateGoogleDriveSelectionCount();
                });
            }
            catch (OperationCanceledException)
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    GoogleDriveDrawerOverlay.Visibility = Visibility.Collapsed;
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to load Google Drive folder: {ex.Message}");
                await Dispatcher.InvokeAsync(() =>
                {
                    GoogleDriveLoadingPanel.Visibility = Visibility.Collapsed;
                    GoogleDriveTreeBorder.Visibility = Visibility.Visible;
                    GoogleDriveDrawerMetaText.Text = $"Error: {ex.Message}";
                    ShowToast($"Google Drive error: {ex.Message}");
                });
            }
        }

        private void UpdateGoogleDriveSelectionCount()
        {
            if (_currentGoogleDriveResult == null)
            {
                GoogleDriveSelectedCountText.Text = "Selected: 0 files";
                GoogleDriveDownloadSelectedButton.IsEnabled = false;
                return;
            }

            var selectedFiles = GetSelectedFiles(_currentGoogleDriveResult.Items);
            long selectedBytes = selectedFiles.Sum(f => f.TotalSizeBytes);
            string selectedSizeStr = GoogleDriveItem.FormatBytes(selectedBytes);

            long totalBytes = _currentGoogleDriveResult.TotalBytes > 0
                ? _currentGoogleDriveResult.TotalBytes
                : GetAllFiles(_currentGoogleDriveResult.Items).Sum(f => f.TotalSizeBytes);
            string totalSizeStr = GoogleDriveItem.FormatBytes(totalBytes);

            if (selectedBytes > 0)
            {
                GoogleDriveSelectedCountText.Text = $"Selected: {selectedFiles.Count} of {_currentGoogleDriveResult.TotalFilesCount} files • {selectedSizeStr}";
                GoogleDriveDownloadSelectedButton.Content = $"⬇ Download Selected ({selectedSizeStr})";
            }
            else
            {
                GoogleDriveSelectedCountText.Text = $"Selected: {selectedFiles.Count} of {_currentGoogleDriveResult.TotalFilesCount} files";
                GoogleDriveDownloadSelectedButton.Content = "⬇ Download Selected";
            }

            GoogleDriveDownloadSelectedButton.IsEnabled = selectedFiles.Count > 0;
            GoogleDriveDownloadAllButton.Content = totalBytes > 0
                ? $"⬇ Download All ({totalSizeStr})"
                : "⬇ Download All";
        }

        private List<GoogleDriveItem> GetSelectedFiles(IEnumerable<GoogleDriveItem> items)
        {
            var list = new List<GoogleDriveItem>();
            foreach (var item in items)
            {
                if (!item.IsFolder)
                {
                    if (item.IsSelected == true)
                    {
                        list.Add(item);
                    }
                }
                else
                {
                    list.AddRange(GetSelectedFiles(item.Children));
                }
            }
            return list;
        }

        private List<GoogleDriveItem> GetAllFiles(IEnumerable<GoogleDriveItem> items)
        {
            var list = new List<GoogleDriveItem>();
            foreach (var item in items)
            {
                if (!item.IsFolder)
                {
                    list.Add(item);
                }
                else
                {
                    list.AddRange(GetAllFiles(item.Children));
                }
            }
            return list;
        }

        private void GoogleDriveSelectAll_Click(object sender, RoutedEventArgs e)
        {
            if (_currentGoogleDriveResult == null) return;
            foreach (var it in _currentGoogleDriveResult.Items)
            {
                it.IsSelected = true;
            }
            UpdateGoogleDriveSelectionCount();
        }

        private void GoogleDriveDeselectAll_Click(object sender, RoutedEventArgs e)
        {
            if (_currentGoogleDriveResult == null) return;
            foreach (var it in _currentGoogleDriveResult.Items)
            {
                it.IsSelected = false;
            }
            UpdateGoogleDriveSelectionCount();
        }

        private void GoogleDriveExpandAll_Click(object sender, RoutedEventArgs e)
        {
            if (_currentGoogleDriveResult == null) return;
            SetExpandedRecursive(_currentGoogleDriveResult.Items, true);
        }

        private void GoogleDriveCollapseAll_Click(object sender, RoutedEventArgs e)
        {
            if (_currentGoogleDriveResult == null) return;
            SetExpandedRecursive(_currentGoogleDriveResult.Items, false);
        }

        private void SetExpandedRecursive(IEnumerable<GoogleDriveItem> items, bool isExpanded)
        {
            foreach (var it in items)
            {
                it.IsExpanded = isExpanded;
                SetExpandedRecursive(it.Children, isExpanded);
            }
        }

        private void GoogleDriveItemCheckbox_Changed(object sender, RoutedEventArgs e)
        {
            UpdateGoogleDriveSelectionCount();
        }

        private void GoogleDriveDrawerClose_Click(object sender, RoutedEventArgs e)
        {
            _googleDriveScanCts?.Cancel();
            GoogleDriveDrawerOverlay.Visibility = Visibility.Collapsed;
        }

        private void GoogleDriveCancelScan_Click(object sender, RoutedEventArgs e)
        {
            _googleDriveScanCts?.Cancel();
            GoogleDriveDrawerOverlay.Visibility = Visibility.Collapsed;
        }

        private void GoogleDriveDownloadAll_Click(object sender, RoutedEventArgs e)
        {
            if (_currentGoogleDriveResult == null) return;
            var allFiles = GetAllFiles(_currentGoogleDriveResult.Items);
            DownloadGoogleDriveFilesToQueue(allFiles, isDownloadAll: true);
        }

        private void GoogleDriveDownloadSelected_Click(object sender, RoutedEventArgs e)
        {
            if (_currentGoogleDriveResult == null) return;
            var selectedFiles = GetSelectedFiles(_currentGoogleDriveResult.Items);
            DownloadGoogleDriveFilesToQueue(selectedFiles, isDownloadAll: false);
        }

        private void DownloadGoogleDriveFilesToQueue(List<GoogleDriveItem> filesToDownload, bool isDownloadAll)
        {
            if (filesToDownload.Count == 0 || _currentGoogleDriveResult == null) return;

            GoogleDriveDrawerOverlay.Visibility = Visibility.Collapsed;

            // Determine root path rule:
            // Single subfolder selected -> place directly inside SelectedDirectory / Subfolder
            // Multiple subfolders or All -> place inside SelectedDirectory / RootCatalogName / Subfolders...
            string baseTargetDir;

            var topLevelParts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in filesToDownload)
            {
                int slashIdx = file.RelativePath.IndexOf('/');
                if (slashIdx > 0)
                {
                    topLevelParts.Add(file.RelativePath.Substring(0, slashIdx));
                }
                else
                {
                    topLevelParts.Add("");
                }
            }

            bool isSingleSubfolderOnly = !isDownloadAll && topLevelParts.Count == 1 && !topLevelParts.Contains("");

            if (isSingleSubfolderOnly)
            {
                baseTargetDir = SelectedDirectory;
            }
            else
            {
                string safeRootTitle = SanitizeSafePath(string.IsNullOrWhiteSpace(_currentGoogleDriveResult.RootTitle) ? "Google Drive Download" : _currentGoogleDriveResult.RootTitle);
                baseTargetDir = Path.Combine(SelectedDirectory, safeRootTitle);
            }

            var queueItems = new List<DownloadQueueItem>();
            foreach (var fileItem in filesToDownload)
            {
                string relativePathNormalized = fileItem.RelativePath.Replace('/', Path.DirectorySeparatorChar);
                string targetFilePath = Path.Combine(baseTargetDir, relativePathNormalized);

                var qItem = new DownloadQueueItem
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Title = fileItem.Name,
                    Url = $"https://drive.google.com/file/d/{fileItem.Id}/view",
                    Platform = "Google Drive",
                    GoogleDriveFileId = fileItem.Id,
                    TargetFilePath = targetFilePath,
                    RelativePath = fileItem.RelativePath,
                    DestinationFolder = Path.GetDirectoryName(targetFilePath) ?? baseTargetDir,
                    FileSizeString = fileItem.DisplaySizeString,
                    FormatCode = "original",
                    IsAudioOnly = false,
                    Progress = 0,
                    Status = QueueItemStatus.Queued,
                    StatusText = !string.IsNullOrEmpty(fileItem.DisplaySizeString) ? $"Queued ({fileItem.DisplaySizeString})" : "Queued"
                };

                queueItems.Add(qItem);
            }

            _queueManager.EnqueueRange(queueItems);
            ShowQueueView();
            ShowToast($"Added {queueItems.Count} files to Download Queue 📥");
        }

        private string SanitizeSafePath(string name)
        {
            string invalid = new string(Path.GetInvalidFileNameChars()) + new string(Path.GetInvalidPathChars());
            string clean = string.Concat(name.Select(c => invalid.Contains(c) ? '_' : c)).Trim();
            return string.IsNullOrWhiteSpace(clean) ? "Google Drive Download" : clean;
        }
    }
}
