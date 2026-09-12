using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using UniversalDownloader.Controls;
using UniversalDownloader.Models;
using UniversalDownloader.Services;

namespace UniversalDownloader
{
    public class MediaConverterItem : INotifyPropertyChanged
    {
        private string _inputPath = "";
        private string _outputPath = "";
        private string _fileName = "";
        private string _fileSize = "";
        private double _progress = 0;
        private string _status = "Ready";
        private bool _isConverting = false;
        private bool _isCompleted = false;

        public string InputPath
        {
            get => _inputPath;
            set { _inputPath = value; OnPropertyChanged(); }
        }

        public string OutputPath
        {
            get => _outputPath;
            set { _outputPath = value; OnPropertyChanged(); }
        }

        public string FileName
        {
            get => _fileName;
            set { _fileName = value; OnPropertyChanged(); }
        }

        public string FileSize
        {
            get => _fileSize;
            set { _fileSize = value; OnPropertyChanged(); }
        }

        public double Progress
        {
            get => _progress;
            set { _progress = value; OnPropertyChanged(); }
        }

        public string Status
        {
            get => _status;
            set { _status = value; OnPropertyChanged(); }
        }

        public bool IsConverting
        {
            get => _isConverting;
            set { _isConverting = value; OnPropertyChanged(); }
        }

        public bool IsCompleted
        {
            get => _isCompleted;
            set { _isCompleted = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public partial class MainWindow
    {
        private MediaConverterService? _mediaConverterService;
        public ObservableCollection<MediaConverterItem> ConverterItems { get; } = new();
        private CancellationTokenSource? _converterCts;
        private bool _isConvertingActive = false;

        private void InitializeMediaConverter()
        {
            _mediaConverterService = new MediaConverterService(_dependencyManager);
            if (ConverterItemsControl != null)
            {
                ConverterItemsControl.ItemsSource = ConverterItems;
            }
        }

        private void ConverterButton_Click(object sender, RoutedEventArgs e)
        {
            CollapseSpotifyDrawer();

            if (ConverterScrollViewer != null && ConverterScrollViewer.Visibility == Visibility.Visible)
            {
                // Toggle back to main
                ConverterScrollViewer.Visibility = Visibility.Collapsed;
                if (MainScrollViewer != null) MainScrollViewer.Visibility = Visibility.Visible;
                return;
            }

            if (MainScrollViewer != null) MainScrollViewer.Visibility = Visibility.Collapsed;
            if (HistoryScrollViewer != null) HistoryScrollViewer.Visibility = Visibility.Collapsed;
            if (SettingsScrollViewer != null) SettingsScrollViewer.Visibility = Visibility.Collapsed;
            if (QueueScrollViewer != null) QueueScrollViewer.Visibility = Visibility.Collapsed;
            if (SearchScrollViewer != null) SearchScrollViewer.Visibility = Visibility.Collapsed;
            if (LiveStreamScrollViewer != null) LiveStreamScrollViewer.Visibility = Visibility.Collapsed;
            if (CompressorScrollViewer != null) CompressorScrollViewer.Visibility = Visibility.Collapsed;
            if (GifWebpScrollViewer != null) GifWebpScrollViewer.Visibility = Visibility.Collapsed;
            if (ConverterScrollViewer != null)
            {
                ConverterScrollViewer.Visibility = Visibility.Visible;
                if (ConverterDestinationTextBox != null && !string.IsNullOrEmpty(SelectedDirectory))
                {
                    ConverterDestinationTextBox.Text = SelectedDirectory;
                }
            }
        }

        private void BackFromConverter_Click(object sender, RoutedEventArgs e)
        {
            if (ConverterScrollViewer != null) ConverterScrollViewer.Visibility = Visibility.Collapsed;
            if (QueueScrollViewer != null) QueueScrollViewer.Visibility = Visibility.Collapsed;
            if (MainScrollViewer != null) MainScrollViewer.Visibility = Visibility.Visible;
        }

        private void ConverterBrowseFiles_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new OpenFileDialog
            {
                Title = "Select Media Files to Convert",
                Multiselect = true,
                Filter = "Media Files|*.mp4;*.mkv;*.avi;*.mov;*.webm;*.flv;*.wmv;*.mp3;*.m4a;*.flac;*.wav;*.aac;*.ogg;*.opus;*.wma|All Files|*.*"
            };

            if (ofd.ShowDialog(this) == true)
            {
                AddFilesToConverter(ofd.FileNames);
            }
        }

        private static readonly HashSet<string> SupportedConverterExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".mp4", ".mkv", ".avi", ".mov", ".webm", ".flv", ".wmv", ".m4v", ".ts", ".3gp", ".mts", ".m2ts", ".vob", ".ogv",
            ".mp3", ".m4a", ".flac", ".wav", ".aac", ".ogg", ".opus", ".wma", ".aiff", ".alac", ".ape", ".m4b"
        };

        internal static string[] ExtractDropPaths(DragEventArgs e)
        {
            try
            {
                if (e.Data.GetDataPresent(DataFormats.FileDrop, true))
                {
                    if (e.Data.GetData(DataFormats.FileDrop, true) is string[] files && files.Length > 0)
                    {
                        return files;
                    }
                    if (e.Data.GetData(DataFormats.FileDrop, true) is IEnumerable<string> enumerable)
                    {
                        var list = enumerable.ToArray();
                        if (list.Length > 0) return list;
                    }
                }

                if (e.Data.GetDataPresent("FileNameW", true))
                {
                    if (e.Data.GetData("FileNameW", true) is string singleFile && (File.Exists(singleFile) || Directory.Exists(singleFile)))
                    {
                        return new[] { singleFile };
                    }
                }

                if (e.Data.GetDataPresent("FileName", true))
                {
                    if (e.Data.GetData("FileName", true) is string singleFile && (File.Exists(singleFile) || Directory.Exists(singleFile)))
                    {
                        return new[] { singleFile };
                    }
                }

                if (e.Data.GetDataPresent(DataFormats.UnicodeText, true))
                {
                    if (e.Data.GetData(DataFormats.UnicodeText, true) is string text && !string.IsNullOrWhiteSpace(text))
                    {
                        var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                                        .Select(l => l.Trim().Trim('"'))
                                        .Where(l => File.Exists(l) || Directory.Exists(l))
                                        .ToArray();
                        if (lines.Length > 0) return lines;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DragDrop] Error extracting drop paths: {ex.Message}");
            }

            return Array.Empty<string>();
        }

        private void ConverterDropZone_PreviewDragEnter(object sender, DragEventArgs e)
        {
            if (ConverterDropZone != null)
            {
                ConverterDropZone.BorderBrush = (Brush)FindResource("PrimaryBrush");
                ConverterDropZone.Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x26));
            }
            ConverterDropZone_PreviewDragOver(sender, e);
        }

        private void ConverterDropZone_PreviewDragLeave(object sender, DragEventArgs e)
        {
            if (ConverterDropZone != null)
            {
                ConverterDropZone.BorderBrush = new SolidColorBrush(Color.FromRgb(0x3F, 0x3F, 0x46));
                ConverterDropZone.Background = new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x18));
            }
        }

        private void ConverterDropZone_PreviewDragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop, true) ||
                e.Data.GetDataPresent("FileNameW", true) ||
                e.Data.GetDataPresent("FileName", true) ||
                e.Data.GetDataPresent(DataFormats.UnicodeText, true))
            {
                e.Effects = DragDropEffects.Copy;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void ConverterDropZone_PreviewDrop(object sender, DragEventArgs e)
        {
            ConverterDropZone_PreviewDragLeave(sender, e);
            var paths = ExtractDropPaths(e);
            if (paths.Length > 0)
            {
                AddFilesToConverter(paths);
            }
            e.Handled = true;
        }

        private void ConverterDropZone_Drop(object sender, DragEventArgs e)
        {
            ConverterDropZone_PreviewDrop(sender, e);
        }

        private void ConverterDropZone_DragOver(object sender, DragEventArgs e)
        {
            ConverterDropZone_PreviewDragOver(sender, e);
        }

        private void AddFilesToConverter(IEnumerable<string> filePaths)
        {
            var filesToAdd = new List<string>();

            foreach (var path in filePaths)
            {
                if (string.IsNullOrWhiteSpace(path)) continue;

                if (File.Exists(path))
                {
                    filesToAdd.Add(path);
                }
                else if (Directory.Exists(path))
                {
                    try
                    {
                        var dirFiles = Directory.EnumerateFiles(path, "*.*", SearchOption.AllDirectories)
                            .Where(f => SupportedConverterExtensions.Contains(Path.GetExtension(f)))
                            .OrderBy(f => f);
                        filesToAdd.AddRange(dirFiles);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[Converter] Error enumerating folder '{path}': {ex.Message}");
                    }
                }
            }

            foreach (var filePath in filesToAdd)
            {
                if (ConverterItems.Any(i => i.InputPath.Equals(filePath, StringComparison.OrdinalIgnoreCase)))
                    continue;

                try
                {
                    var fi = new FileInfo(filePath);
                    if (!SupportedConverterExtensions.Contains(fi.Extension))
                        continue;

                    string formattedSize = Utilities.FormatBytesOutput(fi.Length);
                    ConverterItems.Add(new MediaConverterItem
                    {
                        InputPath = filePath,
                        FileName = fi.Name,
                        FileSize = formattedSize,
                        Status = "Ready to convert"
                    });
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Converter] Error adding file '{filePath}': {ex.Message}");
                }
            }

            UpdateConverterUiStates();
        }

        private void ConverterRemoveItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is MediaConverterItem item)
            {
                ConverterItems.Remove(item);
                UpdateConverterUiStates();
            }
        }

        private void ConverterClearAll_Click(object sender, RoutedEventArgs e)
        {
            ConverterItems.Clear();
            UpdateConverterUiStates();
        }

        private void ConverterBrowseDestination_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Ookii.Dialogs.Wpf.VistaFolderBrowserDialog
            {
                Description = "Select Converted Files Output Folder",
                UseDescriptionForTitle = true,
                SelectedPath = ConverterDestinationTextBox?.Text ?? SelectedDirectory ?? ""
            };
            if (dialog.ShowDialog(this) == true)
            {
                if (ConverterDestinationTextBox != null)
                {
                    ConverterDestinationTextBox.Text = dialog.SelectedPath;
                }
            }
        }

        private void UpdateConverterUiStates()
        {
            if (ConverterDropZone != null)
            {
                ConverterDropZone.Visibility = Visibility.Visible;
            }
            if (ConverterFilesListBorder != null)
            {
                ConverterFilesListBorder.Visibility = ConverterItems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            if (StartConversionButton != null)
            {
                StartConversionButton.IsEnabled = ConverterItems.Count > 0 && !_isConvertingActive;
            }
        }

        private async void StartConversionButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isConvertingActive)
            {
                _converterCts?.Cancel();
                return;
            }

            if (ConverterItems.Count == 0) return;

            string targetFolder = ConverterDestinationTextBox?.Text?.Trim() ?? SelectedDirectory ?? "";
            if (string.IsNullOrWhiteSpace(targetFolder) || !Directory.Exists(targetFolder))
            {
                ModernMessageBox.Show("Please select a valid output folder for converted files.", "Output Folder Required", MessageBoxButton.OK, MessageBoxImage.Warning, this);
                return;
            }

            string targetFormat = (ConverterTargetFormatComboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "MP3";
            // extract extension e.g. "MP3 Audio (.mp3)" -> "mp3"
            string ext = "mp3";
            if (targetFormat.Contains("."))
            {
                ext = targetFormat.Substring(targetFormat.IndexOf('.') + 1).TrimEnd(')').ToLower();
            }
            else
            {
                ext = targetFormat.ToLower();
            }

            string bitrate = (ConverterAudioBitrateComboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "320k";
            string bitrateVal = bitrate.Contains("320") ? "320k" : (bitrate.Contains("256") ? "256k" : (bitrate.Contains("192") ? "192k" : "128k"));

            string resolution = (ConverterVideoResolutionComboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Original";
            string resolutionVal = resolution.Contains("1080") ? "1080p" : (resolution.Contains("720") ? "720p" : (resolution.Contains("480") ? "480p" : "Original"));

            _isConvertingActive = true;
            _converterCts = new CancellationTokenSource();
            var token = _converterCts.Token;

            if (StartConversionButton != null)
            {
                StartConversionButton.Content = "⏹ Stop Conversion";
            }

            if (_mediaConverterService == null)
            {
                _mediaConverterService = new MediaConverterService(_dependencyManager);
            }

            int successCount = 0;
            try
            {
                for (int i = 0; i < ConverterItems.Count; i++)
                {
                    if (token.IsCancellationRequested) break;

                    var item = ConverterItems[i];
                    item.IsConverting = true;
                    item.Status = "Converting...";

                    string baseName = Path.GetFileNameWithoutExtension(item.InputPath);
                    string outPath = Path.Combine(targetFolder, $"{baseName}.{ext}");

                    // Avoid overwriting source if same
                    if (string.Equals(outPath, item.InputPath, StringComparison.OrdinalIgnoreCase))
                    {
                        outPath = Path.Combine(targetFolder, $"{baseName}_converted.{ext}");
                    }

                    item.OutputPath = outPath;

                    var progress = new Progress<MediaConversionProgress>(p =>
                    {
                        Dispatcher.Invoke(() =>
                        {
                            item.Progress = p.Percentage;
                            item.Status = p.StatusMessage;
                        });
                    });

                    bool ok = await _mediaConverterService.ConvertMediaAsync(
                        item.InputPath,
                        outPath,
                        ext,
                        bitrateVal,
                        resolutionVal,
                        progress,
                        token);

                    if (ok)
                    {
                        item.Progress = 100;
                        item.Status = "✓ Converted successfully";
                        item.IsCompleted = true;
                        item.IsConverting = false;
                        successCount++;
                    }
                    else
                    {
                        item.Status = "✕ Conversion failed";
                        item.IsConverting = false;
                    }
                }

                if (!token.IsCancellationRequested)
                {
                    bool shouldTriggerPower = ConverterAutoPowerCheckBox?.IsChecked == true;
                    int selectedIdx = ConverterAutoPowerActionComboBox?.SelectedIndex ?? 0;
                    var action = SystemPowerService.ParseFromIndex(selectedIdx);

                    if (shouldTriggerPower)
                    {
                        if (ConverterAutoPowerCheckBox != null) ConverterAutoPowerCheckBox.IsChecked = false;
                        ShutdownCountdownDialog.Show(action, "Media Converter", 60, this);
                    }
                    else
                    {
                        ModernMessageBox.Show($"Converted {successCount} of {ConverterItems.Count} files successfully.", "Conversion Complete", MessageBoxButton.OK, MessageBoxImage.Information, this);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                ModernMessageBox.Show("Media conversion was cancelled.", "Cancelled", MessageBoxButton.OK, MessageBoxImage.Information, this);
            }
            catch (Exception ex)
            {
                ModernMessageBox.Show($"Conversion error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error, this);
            }
            finally
            {
                _isConvertingActive = false;
                if (StartConversionButton != null)
                {
                    StartConversionButton.Content = "⚡ Start Conversion";
                }
                UpdateConverterUiStates();
            }
        }

        private void ConverterOpenItemFolder_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is MediaConverterItem item && !string.IsNullOrEmpty(item.OutputPath))
            {
                try
                {
                    if (File.Exists(item.OutputPath))
                    {
                        Process.Start("explorer.exe", $"/select,\"{item.OutputPath}\"");
                    }
                    else if (Directory.Exists(Path.GetDirectoryName(item.OutputPath)))
                    {
                        Process.Start("explorer.exe", Path.GetDirectoryName(item.OutputPath)!);
                    }
                }
                catch { }
            }
        }

        private void ConverterPlayItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is MediaConverterItem item && !string.IsNullOrEmpty(item.OutputPath) && File.Exists(item.OutputPath))
            {
                var historyItem = new DownloadHistoryItem
                {
                    Title = item.FileName,
                    FilePath = item.OutputPath,
                    Platform = "Converted",
                    FormatExtension = Path.GetExtension(item.OutputPath).TrimStart('.'),
                    IsAudio = item.OutputPath.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) ||
                              item.OutputPath.EndsWith(".m4a", StringComparison.OrdinalIgnoreCase) ||
                              item.OutputPath.EndsWith(".flac", StringComparison.OrdinalIgnoreCase) ||
                              item.OutputPath.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)
                };
                PlayHistoryItemPreview(historyItem);
            }
        }
    }
}
