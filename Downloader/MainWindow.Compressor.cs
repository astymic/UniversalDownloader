using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using UniversalDownloader.Controls;
using UniversalDownloader.Models;
using UniversalDownloader.Services;

namespace UniversalDownloader
{

    public partial class MainWindow
    {
        private VideoCompressorService? _videoCompressorService;
        public ObservableCollection<VideoCompressorItem> CompressorItems { get; } = new();
        private CancellationTokenSource? _compressorCts;
        private bool _isCompressingActive = false;
        private string? _manualDestinationFolder = null;

        private void InitializeVideoCompressor()
        {
            _videoCompressorService = new VideoCompressorService(_dependencyManager);
        }

        private void CompressorButton_Click(object sender, RoutedEventArgs e)
        {
            CollapseSpotifyDrawer();

            if (CompressorScrollViewer != null && CompressorScrollViewer.Visibility == Visibility.Visible)
            {
                // Toggle back to main view
                CompressorScrollViewer.Visibility = Visibility.Collapsed;
                if (MainScrollViewer != null) MainScrollViewer.Visibility = Visibility.Visible;
                return;
            }

            if (MainScrollViewer != null) MainScrollViewer.Visibility = Visibility.Collapsed;
            if (HistoryScrollViewer != null) HistoryScrollViewer.Visibility = Visibility.Collapsed;
            if (SettingsScrollViewer != null) SettingsScrollViewer.Visibility = Visibility.Collapsed;
            if (QueueScrollViewer != null) QueueScrollViewer.Visibility = Visibility.Collapsed;
            if (SearchScrollViewer != null) SearchScrollViewer.Visibility = Visibility.Collapsed;
            if (LiveStreamScrollViewer != null) LiveStreamScrollViewer.Visibility = Visibility.Collapsed;
            if (ConverterScrollViewer != null) ConverterScrollViewer.Visibility = Visibility.Collapsed;

            if (CompressorScrollViewer != null)
            {
                CompressorScrollViewer.Visibility = Visibility.Visible;
                UpdateCompressorDestinationText();
            }
        }

        private void BackFromCompressor_Click(object sender, RoutedEventArgs e)
        {
            if (CompressorScrollViewer != null) CompressorScrollViewer.Visibility = Visibility.Collapsed;
            if (MainScrollViewer != null) MainScrollViewer.Visibility = Visibility.Visible;
        }

        private void CompressorBrowseFiles_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new OpenFileDialog
            {
                Title = "Select Videos to Compress",
                Multiselect = true,
                Filter = "Video Files|*.mp4;*.mkv;*.mov;*.webm;*.avi;*.flv;*.wmv;*.ts;*.m4v|All Files|*.*"
            };

            if (ofd.ShowDialog(this) == true)
            {
                AddFilesToCompressor(ofd.FileNames);
            }
        }

        private void CompressorDropZone_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[]? files = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (files != null && files.Length > 0)
                {
                    AddFilesToCompressor(files);
                }
            }
        }

        private void CompressorDropZone_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
        }

        private void AddFilesToCompressor(string[] filePaths)
        {
            foreach (var path in filePaths)
            {
                if (File.Exists(path))
                {
                    var fi = new FileInfo(path);
                    string ext = fi.Extension.ToLowerInvariant();
                    bool isVideo = ext is ".mp4" or ".mkv" or ".mov" or ".webm" or ".avi" or ".flv" or ".wmv" or ".ts" or ".m4v";
                    if (!isVideo) continue;

                    string formattedSize = Utilities.FormatBytesOutput(fi.Length);
                    CompressorItems.Add(new VideoCompressorItem
                    {
                        InputPath = path,
                        FileName = fi.Name,
                        OriginalSizeBytes = fi.Length,
                        OriginalSizeFormatted = formattedSize,
                        Status = "Ready to compress"
                    });
                }
            }

            UpdateCompressorDestinationText();
            UpdateCompressorUiStates();
        }

        private void CompressorRemoveItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is VideoCompressorItem item)
            {
                if (item.IsCompressing || item.Cts != null)
                {
                    item.Cancel();
                    _videoCompressorService?.CancelCurrentProcess();

                    if (!string.IsNullOrEmpty(item.OutputPath))
                    {
                        try
                        {
                            if (File.Exists(item.OutputPath))
                            {
                                File.Delete(item.OutputPath);
                            }
                        }
                        catch { }
                    }
                }

                CompressorItems.Remove(item);
                UpdateCompressorDestinationText();
                UpdateCompressorUiStates();
            }
        }

        private void CompressorClearAll_Click(object sender, RoutedEventArgs e)
        {
            if (_isCompressingActive)
            {
                _compressorCts?.Cancel();
                _videoCompressorService?.CancelCurrentProcess();
                foreach (var itm in CompressorItems)
                {
                    itm.Cancel();
                    if (!string.IsNullOrEmpty(itm.OutputPath) && itm.IsCompressing)
                    {
                        try
                        {
                            if (File.Exists(itm.OutputPath))
                            {
                                File.Delete(itm.OutputPath);
                            }
                        }
                        catch { }
                    }
                }
            }

            CompressorItems.Clear();
            UpdateCompressorDestinationText();
            UpdateCompressorUiStates();
        }

        private void CompressorBrowseDestination_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Ookii.Dialogs.Wpf.VistaFolderBrowserDialog
            {
                Description = "Select Compressed Videos Output Folder",
                UseDescriptionForTitle = true,
                SelectedPath = _manualDestinationFolder ?? (CompressorItems.Count > 0 ? Path.GetDirectoryName(CompressorItems[0].InputPath) ?? "" : SelectedDirectory ?? "")
            };
            if (dialog.ShowDialog(this) == true && !string.IsNullOrWhiteSpace(dialog.SelectedPath))
            {
                _manualDestinationFolder = dialog.SelectedPath;
                if (CompressorAutoSubfolderCheckBox != null)
                {
                    CompressorAutoSubfolderCheckBox.IsChecked = false;
                }
                UpdateCompressorDestinationText();
            }
        }

        private void CompressorResetDestination_Click(object sender, RoutedEventArgs e)
        {
            _manualDestinationFolder = null;
            if (CompressorAutoSubfolderCheckBox != null)
            {
                CompressorAutoSubfolderCheckBox.IsChecked = true;
            }
            UpdateCompressorDestinationText();
        }

        private void CompressorAutoSubfolderCheckBox_Click(object sender, RoutedEventArgs e)
        {
            if (CompressorAutoSubfolderCheckBox?.IsChecked == true)
            {
                _manualDestinationFolder = null;
            }
            UpdateCompressorDestinationText();
        }

        private void UpdateCompressorDestinationText()
        {
            if (CompressorDestinationTextBox == null) return;

            bool useAuto = CompressorAutoSubfolderCheckBox?.IsChecked ?? true;
            if (!useAuto && !string.IsNullOrWhiteSpace(_manualDestinationFolder))
            {
                CompressorDestinationTextBox.Text = _manualDestinationFolder;
            }
            else if (CompressorItems.Count > 0)
            {
                string firstDir = Path.GetDirectoryName(CompressorItems[0].InputPath) ?? "";
                CompressorDestinationTextBox.Text = !string.IsNullOrEmpty(firstDir)
                    ? Path.Combine(firstDir, "Compressed")
                    : "Automatic: [Source Directory]\\Compressed";
            }
            else
            {
                CompressorDestinationTextBox.Text = "Automatic: [Source Directory]\\Compressed";
            }
        }

        private void UpdateCompressorUiStates()
        {
            if (CompressorFilesListBorder != null)
            {
                CompressorFilesListBorder.Visibility = CompressorItems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            if (StartCompressionButton != null)
            {
                StartCompressionButton.IsEnabled = CompressorItems.Count > 0 && !_isCompressingActive;
            }
            if (CompressorItems.Count == 0 && CompressorTotalEtaTextBlock != null)
            {
                CompressorTotalEtaTextBlock.Text = string.Empty;
            }
        }

        private void UpdateCompressorBatchEta(int currentFileIndex, double currentPercentage, TimeSpan batchElapsed, long totalInputBytes)
        {
            if (CompressorTotalEtaTextBlock == null || CompressorItems.Count == 0) return;

            if (CompressorItems.Count == 1)
            {
                if (currentPercentage > 1 && batchElapsed.TotalSeconds > 1)
                {
                    double remainingSec = (batchElapsed.TotalSeconds / (currentPercentage / 100.0)) - batchElapsed.TotalSeconds;
                    if (remainingSec > 0)
                    {
                        CompressorTotalEtaTextBlock.Text = $"Total ETA: {VideoCompressorService.FormatDurationShort(TimeSpan.FromSeconds(remainingSec))}";
                    }
                }
                return;
            }

            double totalCompletedSec = batchElapsed.TotalSeconds;
            if (totalCompletedSec <= 1) return;

            if (totalInputBytes > 0)
            {
                long completedBytes = 0;
                for (int j = 0; j < currentFileIndex; j++)
                {
                    completedBytes += CompressorItems[j].OriginalSizeBytes;
                }
                var currentItem = CompressorItems[currentFileIndex];
                completedBytes += (long)(currentItem.OriginalSizeBytes * (currentPercentage / 100.0));

                long remainingBytes = Math.Max(0, totalInputBytes - completedBytes);
                if (completedBytes > 0)
                {
                    double bytesPerSec = completedBytes / totalCompletedSec;
                    if (bytesPerSec > 0)
                    {
                        double remainingSec = remainingBytes / bytesPerSec;
                        var remainingTime = TimeSpan.FromSeconds(remainingSec);
                        CompressorTotalEtaTextBlock.Text = $"Total ETA: {VideoCompressorService.FormatDurationShort(remainingTime)} (File {currentFileIndex + 1}/{CompressorItems.Count})";
                        return;
                    }
                }
            }

            double overallFraction = (currentFileIndex + (currentPercentage / 100.0)) / CompressorItems.Count;
            if (overallFraction > 0.01)
            {
                double totalEstimatedSec = totalCompletedSec / overallFraction;
                double remainingSec = Math.Max(0, totalEstimatedSec - totalCompletedSec);
                CompressorTotalEtaTextBlock.Text = $"Total ETA: {VideoCompressorService.FormatDurationShort(TimeSpan.FromSeconds(remainingSec))} (File {currentFileIndex + 1}/{CompressorItems.Count})";
            }
        }

        private void CompressorPresetComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CompressorPresetComboBox == null) return;

            int idx = CompressorPresetComboBox.SelectedIndex;
            // 0: Visually Lossless (Default)
            // 1: Balanced (Web / Discord)
            // 2: Max Compression (Smallest Size)
            // 3: Target File Size (e.g. 25MB)
            // 4: Custom Parameters

            bool isTargetSize = idx == 3;
            bool isCustom = idx == 4;

            if (CompressorTargetSizePanel != null)
            {
                CompressorTargetSizePanel.Visibility = isTargetSize ? Visibility.Visible : Visibility.Collapsed;
            }

            if (CompressorCrfPanel != null)
            {
                CompressorCrfPanel.Visibility = !isTargetSize ? Visibility.Visible : Visibility.Collapsed;
            }

            if (CompressorCrfSlider != null)
            {
                CompressorCrfSlider.IsEnabled = isCustom || idx == 0;
                if (idx == 0) CompressorCrfSlider.Value = 20;
                else if (idx == 1) CompressorCrfSlider.Value = 24;
                else if (idx == 2) CompressorCrfSlider.Value = 28;
            }

            if (CompressorCodecComboBox != null)
            {
                if (idx == 2) CompressorCodecComboBox.SelectedIndex = 1; // H.265 for max compression
                else if (idx == 0 || idx == 1 || idx == 3) CompressorCodecComboBox.SelectedIndex = 0; // H.264
            }

            if (CompressorAudioComboBox != null)
            {
                if (idx == 0) CompressorAudioComboBox.SelectedIndex = 0; // Copy stream (lossless audio)
                else if (idx == 1 || idx == 2 || idx == 3) CompressorAudioComboBox.SelectedIndex = 1; // AAC 128k
            }

            if (CompressorPresetSpeedComboBox != null)
            {
                if (idx == 0 || idx == 2) CompressorPresetSpeedComboBox.SelectedIndex = 1; // Slow
                else CompressorPresetSpeedComboBox.SelectedIndex = 0; // Medium
            }

            if (isTargetSize || isCustom)
            {
                SetCompressorDropdownExpanded(true);
            }
        }

        private void CompressorToggleSettings_Click(object sender, RoutedEventArgs e)
        {
            bool isVisible = CompressorDetailedSettingsPanel?.Visibility == Visibility.Visible;
            SetCompressorDropdownExpanded(!isVisible);
        }

        private void SetCompressorDropdownExpanded(bool expand)
        {
            if (CompressorDetailedSettingsPanel != null)
            {
                CompressorDetailedSettingsPanel.Visibility = expand ? Visibility.Visible : Visibility.Collapsed;
            }
            if (CompressorDropdownArrow != null)
            {
                CompressorDropdownArrow.Text = expand ? "▲" : "▼";
            }
        }

        private void CompressorCrfSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (CompressorCrfValueTextBlock != null)
            {
                int crfVal = (int)e.NewValue;
                string label = crfVal switch
                {
                    <= 18 => $"{crfVal} (Near Lossless / Master)",
                    <= 21 => $"{crfVal} (Visually Lossless - Recommended)",
                    <= 25 => $"{crfVal} (Balanced)",
                    _ => $"{crfVal} (High Compression)"
                };
                CompressorCrfValueTextBlock.Text = label;
            }
        }

        private void TargetSizePresetButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tagStr && double.TryParse(tagStr, out double mb))
            {
                if (CompressorTargetSizeTextBox != null)
                {
                    CompressorTargetSizeTextBox.Text = mb.ToString("F0");
                }
            }
        }

        private async void StartCompressionButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isCompressingActive)
            {
                _compressorCts?.Cancel();
                _videoCompressorService?.CancelCurrentProcess();
                if (StartCompressionButton != null) StartCompressionButton.Content = "Stopping...";
                return;
            }

            if (CompressorItems.Count == 0) return;

            bool useAuto = CompressorAutoSubfolderCheckBox?.IsChecked ?? true;
            string targetFolder = "";

            if (!useAuto && !string.IsNullOrWhiteSpace(_manualDestinationFolder))
            {
                targetFolder = _manualDestinationFolder;
            }
            else if (CompressorItems.Count > 0)
            {
                string sourceDir = Path.GetDirectoryName(CompressorItems[0].InputPath) ?? "";
                targetFolder = Path.Combine(sourceDir, "Compressed");
            }

            if (string.IsNullOrWhiteSpace(targetFolder))
            {
                targetFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "Compressed");
            }

            try
            {
                if (!Directory.Exists(targetFolder))
                {
                    Directory.CreateDirectory(targetFolder);
                }
            }
            catch (Exception ex)
            {
                ModernMessageBox.Show($"Could not create output folder '{targetFolder}': {ex.Message}", "Folder Error", MessageBoxButton.OK, MessageBoxImage.Error, this);
                return;
            }

            if (_videoCompressorService == null)
            {
                _videoCompressorService = new VideoCompressorService(_dependencyManager);
            }

            // Gather compression options from UI
            int presetIdx = CompressorPresetComboBox?.SelectedIndex ?? 0;
            var preset = presetIdx switch
            {
                1 => VideoCompressionPreset.Balanced,
                2 => VideoCompressionPreset.MaxCompression,
                3 => VideoCompressionPreset.TargetSize,
                4 => VideoCompressionPreset.Custom,
                _ => VideoCompressionPreset.VisuallyLossless
            };

            int codecIdx = CompressorCodecComboBox?.SelectedIndex ?? 0;
            var codec = codecIdx switch
            {
                1 => VideoCodec.H265,
                2 => VideoCodec.AV1,
                _ => VideoCodec.H264
            };

            int crf = (int)(CompressorCrfSlider?.Value ?? 20);

            double? targetSizeMb = null;
            if (preset == VideoCompressionPreset.TargetSize)
            {
                if (double.TryParse(CompressorTargetSizeTextBox?.Text?.Trim(), out double parsedMb) && parsedMb > 0)
                {
                    targetSizeMb = parsedMb;
                }
                else
                {
                    targetSizeMb = 25.0; // default to Discord limit
                }
            }

            string resolution = (CompressorResolutionComboBox?.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Original";
            string fps = (CompressorFpsComboBox?.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Original";
            string encoderPreset = (CompressorPresetSpeedComboBox?.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "slow";

            int audioIdx = CompressorAudioComboBox?.SelectedIndex ?? 0;
            var audioMode = audioIdx switch
            {
                1 => VideoAudioMode.Aac128,
                2 => VideoAudioMode.Aac192,
                3 => VideoAudioMode.Opus96,
                4 => VideoAudioMode.Mute,
                _ => VideoAudioMode.Copy
            };

            var device = (CompressorDeviceComboBox?.SelectedIndex == 1)
                ? VideoEncodingDevice.Cpu
                : VideoEncodingDevice.Gpu;

            var options = new VideoCompressorOptions
            {
                Device = device,
                Preset = preset,
                Codec = codec,
                Crf = crf,
                TargetSizeMb = targetSizeMb,
                Resolution = resolution,
                Fps = fps,
                EncoderPreset = encoderPreset,
                AudioMode = audioMode
            };

            _compressorCts = new CancellationTokenSource();
            _isCompressingActive = true;

            if (StartCompressionButton != null)
            {
                StartCompressionButton.Content = "✕ Cancel Compression";
                StartCompressionButton.Style = (Style)FindResource("DangerButton");
                StartCompressionButton.IsEnabled = true;
            }

            var batchWatch = Stopwatch.StartNew();
            long totalInputBytes = 0;
            foreach (var itm in CompressorItems)
            {
                totalInputBytes += itm.OriginalSizeBytes;
            }

            try
            {
                while (true)
                {
                    _compressorCts.Token.ThrowIfCancellationRequested();

                    var item = System.Linq.Enumerable.FirstOrDefault(CompressorItems, it => !it.IsCompleted && !it.IsCompressing);
                    if (item == null)
                    {
                        break;
                    }

                    using var itemCts = CancellationTokenSource.CreateLinkedTokenSource(_compressorCts.Token);
                    item.Cts = itemCts;

                    string originalBaseName = Path.GetFileNameWithoutExtension(item.InputPath);
                    string extension = ".mp4";
                    string outputFileName = $"{originalBaseName}{extension}";
                    string finalOutputPath = Path.Combine(targetFolder, outputFileName);

                    // If target path happens to collide with original input file, append _compressed to avoid self-overwrite
                    if (string.Equals(Path.GetFullPath(finalOutputPath), Path.GetFullPath(item.InputPath), StringComparison.OrdinalIgnoreCase))
                    {
                        outputFileName = $"{originalBaseName}_compressed{extension}";
                        finalOutputPath = Path.Combine(targetFolder, outputFileName);
                    }
                    else
                    {
                        int counter = 1;
                        while (File.Exists(finalOutputPath))
                        {
                            finalOutputPath = Path.Combine(targetFolder, $"{originalBaseName}_{counter}{extension}");
                            counter++;
                        }
                    }

                    item.OutputPath = finalOutputPath;
                    item.IsCompressing = true;
                    item.Status = "Starting compression...";
                    item.Progress = 0;
                    item.TimeRemainingFormatted = string.Empty;

                    var fileWatch = Stopwatch.StartNew();

                    var progress = new Progress<VideoCompressionProgress>(p =>
                    {
                        Dispatcher.Invoke(() =>
                        {
                            if (!CompressorItems.Contains(item)) return;

                            item.Progress = p.Percentage;
                            item.Status = p.StatusMessage;

                            if (p.EstimatedRemainingTime.HasValue)
                            {
                                item.TimeRemainingFormatted = $"ETA: {VideoCompressorService.FormatDurationShort(p.EstimatedRemainingTime.Value)}";
                            }
                            else
                            {
                                item.TimeRemainingFormatted = string.Empty;
                            }

                            int currentIndex = CompressorItems.IndexOf(item);
                            if (currentIndex >= 0)
                            {
                                UpdateCompressorBatchEta(currentIndex, p.Percentage, batchWatch.Elapsed, totalInputBytes);
                            }
                        });
                    });

                    bool success = false;
                    try
                    {
                        success = await _videoCompressorService.CompressVideoAsync(
                            item.InputPath,
                            finalOutputPath,
                            options,
                            progress,
                            itemCts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        if (_compressorCts.IsCancellationRequested)
                        {
                            throw;
                        }

                        // Just this individual file was removed or cancelled
                        item.IsCompressing = false;
                        item.Cts = null;
                        try
                        {
                            if (File.Exists(finalOutputPath))
                            {
                                File.Delete(finalOutputPath);
                            }
                        }
                        catch { }
                        continue;
                    }
                    finally
                    {
                        item.Cts = null;
                    }

                    fileWatch.Stop();
                    item.IsCompressing = false;
                    item.ElapsedSeconds = fileWatch.Elapsed.TotalSeconds;
                    string elapsedStr = VideoCompressorService.FormatDurationShort(fileWatch.Elapsed);
                    item.DurationFormatted = $"⏱️ {elapsedStr}";
                    item.TimeRemainingFormatted = string.Empty;

                    if (!CompressorItems.Contains(item))
                    {
                        continue;
                    }

                    if (success && File.Exists(finalOutputPath))
                    {
                        var outFi = new FileInfo(finalOutputPath);
                        item.CompressedSizeBytes = outFi.Length;
                        item.CompressedSizeFormatted = Utilities.FormatBytesOutput(outFi.Length);
                        item.Progress = 100;
                        item.IsCompleted = true;

                        double savings = 0;
                        if (item.OriginalSizeBytes > 0)
                        {
                            savings = ((double)(item.OriginalSizeBytes - outFi.Length) / item.OriginalSizeBytes) * 100.0;
                        }

                        if (savings >= 0)
                        {
                            item.SavingsFormatted = $"-{savings:F0}%";
                            item.Status = $"Completed (-{savings:F0}% in {elapsedStr})";
                        }
                        else
                        {
                            item.SavingsFormatted = $"+{Math.Abs(savings):F0}%";
                            item.Status = $"Completed in {elapsedStr}";
                        }

                        // Add to download history
                        try
                        {
                            await _historyService.AddItemAsync(new DownloadHistoryItem
                            {
                                Title = Path.GetFileNameWithoutExtension(finalOutputPath),
                                Url = item.InputPath,
                                FileSizeBytes = outFi.Length,
                                FormattedSize = item.CompressedSizeFormatted,
                                Platform = "Video Compressor",
                                FormatExtension = Path.GetExtension(finalOutputPath).TrimStart('.'),
                                FilePath = finalOutputPath,
                                DownloadDate = DateTime.Now,
                                IsAudio = false
                            });
                        }
                        catch { }
                    }
                    else
                    {
                        item.Status = "Compression failed.";
                    }
                }

                batchWatch.Stop();
                string totalElapsed = VideoCompressorService.FormatDurationShort(batchWatch.Elapsed);
                if (CompressorTotalEtaTextBlock != null && CompressorItems.Count > 0)
                {
                    CompressorTotalEtaTextBlock.Text = $"✓ Completed {CompressorItems.Count} files in {totalElapsed}";
                }

                if (CompressorItems.Count > 0)
                {
                    ModernMessageBox.Show("All videos have been processed and compressed successfully!", "Compression Completed", MessageBoxButton.OK, MessageBoxImage.Information, this);
                }
            }
            catch (OperationCanceledException)
            {
                if (CompressorTotalEtaTextBlock != null)
                {
                    CompressorTotalEtaTextBlock.Text = "Canceled";
                }
                foreach (var it in CompressorItems)
                {
                    if (it.IsCompressing)
                    {
                        it.IsCompressing = false;
                        it.Status = "Canceled";
                        it.TimeRemainingFormatted = string.Empty;
                    }
                }
            }
            catch (Exception ex)
            {
                ModernMessageBox.Show($"Compression encountered an error: {ex.Message}", "Compression Error", MessageBoxButton.OK, MessageBoxImage.Error, this);
            }
            finally
            {
                _isCompressingActive = false;
                if (StartCompressionButton != null)
                {
                    StartCompressionButton.Content = "🗜️ Start Video Compression";
                    StartCompressionButton.Style = (Style)FindResource("ModernButton");
                }
                UpdateCompressorUiStates();
            }
        }

        private void CompressorPlayItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is VideoCompressorItem item && !string.IsNullOrEmpty(item.OutputPath))
            {
                if (File.Exists(item.OutputPath))
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = item.OutputPath,
                            UseShellExecute = true
                        });
                    }
                    catch { }
                }
            }
        }

        private void CompressorOpenItemFolder_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is VideoCompressorItem item && !string.IsNullOrEmpty(item.OutputPath) && File.Exists(item.OutputPath))
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"/select,\"{item.OutputPath}\"",
                        UseShellExecute = true
                    });
                }
                catch { }
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            try
            {
                _compressorCts?.Cancel();
                _videoCompressorService?.CancelCurrentProcess();
            }
            catch { }
            base.OnClosed(e);
        }
    }
}
