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
                    GoogleDriveDrawerMetaText.Text = $"{result.TotalFoldersCount} folders • {result.TotalFilesCount} files";
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
            GoogleDriveSelectedCountText.Text = $"Selected: {selectedFiles.Count} of {_currentGoogleDriveResult.TotalFilesCount} files";
            GoogleDriveDownloadSelectedButton.IsEnabled = selectedFiles.Count > 0;
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

        private async void GoogleDriveDownloadAll_Click(object sender, RoutedEventArgs e)
        {
            if (_currentGoogleDriveResult == null) return;
            var allFiles = GetAllFiles(_currentGoogleDriveResult.Items);
            await DownloadGoogleDriveFilesAsync(allFiles, isDownloadAll: true);
        }

        private async void GoogleDriveDownloadSelected_Click(object sender, RoutedEventArgs e)
        {
            if (_currentGoogleDriveResult == null) return;
            var selectedFiles = GetSelectedFiles(_currentGoogleDriveResult.Items);
            await DownloadGoogleDriveFilesAsync(selectedFiles, isDownloadAll: false);
        }

        private async Task DownloadGoogleDriveFilesAsync(List<GoogleDriveItem> filesToDownload, bool isDownloadAll)
        {
            if (filesToDownload.Count == 0 || _currentGoogleDriveResult == null) return;

            GoogleDriveDrawerOverlay.Visibility = Visibility.Collapsed;

            if (_isDownloadingFile)
            {
                ShowToast("A download is already in progress.");
                return;
            }

            _isDownloadingFile = true;
            _cancellationTokenSource = new CancellationTokenSource();
            var token = _cancellationTokenSource.Token;

            UpdateUiElementStates("Starting Google Drive download...");
            DownloadProgressBar.Value = 0;
            DownloadProgressBar.IsIndeterminate = false;

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

            int successCount = 0;
            int total = filesToDownload.Count;

            try
            {
                for (int i = 0; i < total; i++)
                {
                    token.ThrowIfCancellationRequested();

                    var fileItem = filesToDownload[i];
                    string relativePathNormalized = fileItem.RelativePath.Replace('/', Path.DirectorySeparatorChar);
                    string targetFilePath = Path.Combine(baseTargetDir, relativePathNormalized);

                    _currentItemTitle = fileItem.Name;
                    _downloadingItemTitle = fileItem.Name;
                    FileNameTextBlock.Text = $"[{i + 1}/{total}] {fileItem.Name}";
                    FileNameTextBlock.Visibility = Visibility.Visible;
                    StatusTextBlock.Text = $"Downloading {i + 1} of {total}: {fileItem.Name}...";
                    DownloadProgressBar.Value = (double)i / total * 100;

                    var progress = new Progress<DownloadProgressArgs>(args =>
                    {
                        Dispatcher.InvokeAsync(() =>
                        {
                            if (!string.IsNullOrWhiteSpace(args.StatusMessage))
                            {
                                StatusTextBlock.Text = $"[{i + 1}/{total}] {args.StatusMessage}";
                            }
                            if (args.Percentage > 0)
                            {
                                DownloadProgressBar.Value = ((double)i / total * 100) + (args.Percentage / total);
                            }
                        });
                    });

                    await _downloadService.DownloadGoogleDriveFileWithStructureAsync(fileItem.Id, targetFilePath, token, progress);
                    successCount++;
                }

                DownloadProgressBar.Value = 100;
                StatusTextBlock.Text = $"Google Drive complete — {successCount} files downloaded.";
                ShowToast($"Successfully downloaded {successCount} files! 📁");
            }
            catch (OperationCanceledException)
            {
                StatusTextBlock.Text = $"Download canceled ({successCount}/{total} completed).";
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"Download error: {ex.Message}";
                Debug.WriteLine($"[GDRIVE DOWNLOAD ERROR] {ex}");
                ShowToast($"Download error: {ex.Message}");
            }
            finally
            {
                _isDownloadingFile = false;
                _downloadingItemTitle = "";
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
                UpdateUiElementStates();
            }
        }

        private string SanitizeSafePath(string name)
        {
            string invalid = new string(Path.GetInvalidFileNameChars()) + new string(Path.GetInvalidPathChars());
            string clean = string.Concat(name.Select(c => invalid.Contains(c) ? '_' : c)).Trim();
            return string.IsNullOrWhiteSpace(clean) ? "Google Drive Download" : clean;
        }
    }
}
