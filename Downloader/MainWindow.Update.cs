using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
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
                    if (SettingsAppVersionText != null)
                    {
                        SettingsAppVersionText.Text = $" (v{info.CurrentVersion})";
                    }

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
                    else
                    {
                        if (UpdateAvailableButton != null)
                        {
                            UpdateAvailableButton.Visibility = Visibility.Collapsed;
                        }

                        if (!silent)
                        {
                            ShowToast($"You're on the latest version (v{info.CurrentVersion})! ✨");
                        }
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
                PopulateMarkdownNotes(UpdateDialogNotesTextBlock, info.ReleaseNotes);

            if (UpdateProgressPanel != null)
                UpdateProgressPanel.Visibility = Visibility.Collapsed;

            if (UpdateDialogApplyButton != null)
            {
                UpdateDialogApplyButton.IsEnabled = true;
                UpdateDialogApplyButton.Content = "⬇ Update & Restart";
            }

            UpdateDialogOverlay.Visibility = Visibility.Visible;
        }

        private static void PopulateMarkdownNotes(TextBlock textBlock, string? markdown)
        {
            textBlock.Inlines.Clear();
            if (string.IsNullOrWhiteSpace(markdown))
            {
                textBlock.Inlines.Add(new Run("Performance enhancements, bug fixes, and general improvements.")
                {
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#CBD5E1"))
                });
                return;
            }

            var lines = markdown.Replace("\r\n", "\n").Split('\n');
            bool hasContent = false;

            foreach (var rawLine in lines)
            {
                string line = rawLine.Trim();
                if (string.IsNullOrWhiteSpace(line))
                {
                    if (hasContent)
                    {
                        textBlock.Inlines.Add(new LineBreak());
                    }
                    continue;
                }

                if (hasContent)
                {
                    textBlock.Inlines.Add(new LineBreak());
                }
                hasContent = true;

                // Heading lines (###, ##, #)
                if (line.StartsWith("#"))
                {
                    string headingText = line.TrimStart('#').Trim();
                    var headingRun = new Run(headingText)
                    {
                        FontWeight = FontWeights.Bold,
                        FontSize = line.StartsWith("###") ? 12.5 : 13.5,
                        Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#38BDF8"))
                    };
                    textBlock.Inlines.Add(headingRun);
                    continue;
                }

                // Bullet point lines (* or -)
                if (line.StartsWith("* ") || line.StartsWith("- "))
                {
                    string bulletContent = line.Substring(2).Trim();
                    textBlock.Inlines.Add(new Run("  • ")
                    {
                        FontWeight = FontWeights.Bold,
                        Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#38BDF8"))
                    });

                    AddFormattedInlines(textBlock, bulletContent);
                    continue;
                }

                // Standard text lines
                AddFormattedInlines(textBlock, line);
            }
        }

        private static void AddFormattedInlines(TextBlock textBlock, string text)
        {
            // Parse **bold** tokens
            var parts = text.Split(new[] { "**" }, StringSplitOptions.None);
            for (int i = 0; i < parts.Length; i++)
            {
                if (string.IsNullOrEmpty(parts[i])) continue;

                bool isBold = (i % 2 == 1);
                var run = new Run(parts[i])
                {
                    FontWeight = isBold ? FontWeights.Bold : FontWeights.Normal,
                    Foreground = isBold 
                        ? new SolidColorBrush(Colors.White) 
                        : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#CBD5E1"))
                };
                textBlock.Inlines.Add(run);
            }
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
