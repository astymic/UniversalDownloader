using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using UniversalDownloader.Models;
using UniversalDownloader.Services;

namespace UniversalDownloader
{
    public partial class MainWindow
    {
        private readonly UpdateService _updateService = new();
        private UpdateInfo? _latestUpdateInfo;
        private CancellationTokenSource? _updateCts;

        private void InitializeAutoUpdater()
        {
            // Run background update check with 3-second startup delay
            Task.Run(async () =>
            {
                await Task.Delay(3000);
                await CheckForAppUpdatesAsync(silent: true);
            });
        }

        public async Task CheckForAppUpdatesAsync(bool silent = true)
        {
            try
            {
                var info = await _updateService.CheckForUpdatesAsync();
                _latestUpdateInfo = info;

                await Dispatcher.InvokeAsync(() =>
                {
                    if (info.IsUpdateAvailable)
                    {
                        if (UpdateAvailableButton != null)
                        {
                            if (UpdateBadgeTextBlock != null)
                            {
                                UpdateBadgeTextBlock.Text = $" v{info.LatestVersion}";
                            }
                            UpdateAvailableButton.Visibility = Visibility.Visible;
                        }

                        if (!silent)
                        {
                            ShowUpdateDialog(info);
                        }
                    }
                    else if (!silent)
                    {
                        ShowToast($"You're on the latest version (v{info.CurrentVersion})! ✨");
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Update check failed: {ex.Message}");
                if (!silent)
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        ShowToast("Could not reach update server. Check your internet connection.");
                    });
                }
            }
        }

        private void UpdateAvailableButton_Click(object sender, RoutedEventArgs e)
        {
            if (_latestUpdateInfo != null && _latestUpdateInfo.IsUpdateAvailable)
            {
                ShowUpdateDialog(_latestUpdateInfo);
            }
            else
            {
                _ = CheckForAppUpdatesAsync(silent: false);
            }
        }

        private void ShowUpdateDialog(UpdateInfo info)
        {
            if (UpdateDialogOverlay == null) return;

            if (UpdateDialogCurrentVersionTextBlock != null)
                UpdateDialogCurrentVersionTextBlock.Text = $"v{info.CurrentVersion}";

            if (UpdateDialogLatestVersionTextBlock != null)
                UpdateDialogLatestVersionTextBlock.Text = $"v{info.LatestVersion}";

            if (UpdateDialogReleaseTitleTextBlock != null)
                UpdateDialogReleaseTitleTextBlock.Text = !string.IsNullOrWhiteSpace(info.ReleaseTitle) ? info.ReleaseTitle : $"Universal Downloader v{info.LatestVersion}";

            if (UpdateDialogReleaseDateTextBlock != null)
                UpdateDialogReleaseDateTextBlock.Text = info.PublishedAt.HasValue ? $"Published: {info.PublishedAt.Value:yyyy-MM-dd • hh:mm tt}" : string.Empty;

            if (UpdateDialogNotesTextBlock != null)
                UpdateDialogNotesTextBlock.Text = !string.IsNullOrWhiteSpace(info.ReleaseNotes) ? info.ReleaseNotes.Trim() : "Performance enhancements, bug fixes, and general improvements.";

            if (UpdateProgressPanel != null)
                UpdateProgressPanel.Visibility = Visibility.Collapsed;

            if (UpdateDialogApplyButton != null)
            {
                UpdateDialogApplyButton.IsEnabled = true;
                UpdateDialogApplyButton.Content = "⬇ Update & Restart";
            }

            UpdateDialogOverlay.Visibility = Visibility.Visible;
        }

        private void UpdateDialogClose_Click(object sender, RoutedEventArgs e)
        {
            _updateCts?.Cancel();
            if (UpdateDialogOverlay != null)
            {
                UpdateDialogOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private void UpdateDialogViewGitHub_Click(object sender, RoutedEventArgs e)
        {
            string url = _latestUpdateInfo?.ReleaseUrl ?? "https://github.com/astymic/UniversalDownloader/releases";
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to open browser: {ex.Message}");
            }
        }

        private async void UpdateDialogApply_Click(object sender, RoutedEventArgs e)
        {
            if (_latestUpdateInfo == null) return;

            if (string.IsNullOrWhiteSpace(_latestUpdateInfo.DownloadUrl))
            {
                // Fallback to opening release page in browser
                UpdateDialogViewGitHub_Click(sender, e);
                return;
            }

            if (UpdateProgressPanel != null)
                UpdateProgressPanel.Visibility = Visibility.Visible;

            if (UpdateDialogApplyButton != null)
            {
                UpdateDialogApplyButton.IsEnabled = false;
                UpdateDialogApplyButton.Content = "Downloading...";
            }

            _updateCts = new CancellationTokenSource();
            var progress = new Progress<double>(percent =>
            {
                Dispatcher.Invoke(() =>
                {
                    if (UpdateDownloadProgressBar != null)
                        UpdateDownloadProgressBar.Value = percent;

                    if (UpdateProgressPercentTextBlock != null)
                        UpdateProgressPercentTextBlock.Text = $"{percent:F0}%";

                    if (UpdateProgressStatusTextBlock != null)
                        UpdateProgressStatusTextBlock.Text = percent >= 100 ? "Finalizing update..." : $"Downloading v{_latestUpdateInfo.LatestVersion}... ({percent:F0}%)";
                });
            });

            try
            {
                string tempDir = Path.Combine(Path.GetTempPath(), "UniversalDownloader_Update");
                string downloadedFile = await _updateService.DownloadUpdateAssetAsync(_latestUpdateInfo.DownloadUrl, tempDir, progress, _updateCts.Token);

                if (File.Exists(downloadedFile))
                {
                    if (UpdateProgressStatusTextBlock != null)
                        UpdateProgressStatusTextBlock.Text = "Restarting to apply update...";

                    await Task.Delay(800);
                    _updateService.ApplyUpdateAndRestart(downloadedFile);
                }
            }
            catch (OperationCanceledException)
            {
                if (UpdateProgressStatusTextBlock != null)
                    UpdateProgressStatusTextBlock.Text = "Update canceled.";
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Update download failed: {ex.Message}");
                if (UpdateProgressStatusTextBlock != null)
                    UpdateProgressStatusTextBlock.Text = $"Update failed: {ex.Message}";

                if (UpdateDialogApplyButton != null)
                {
                    UpdateDialogApplyButton.IsEnabled = true;
                    UpdateDialogApplyButton.Content = "Retry Update";
                }
            }
        }
    }
}
