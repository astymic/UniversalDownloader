using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using UniversalDownloader.Controls;
using UniversalDownloader.Models;
using UniversalDownloader.Services;

namespace UniversalDownloader
{
    public partial class MainWindow
    {
        private GifWebpService? _gifWebpService;
        private CancellationTokenSource? _gifCts;
        private bool _isGifGenerating = false;

        private string? _currentGifSourceVideo;
        private TimeSpan _gifTotalDuration = TimeSpan.Zero;
        private TimeSpan _gifStartTime = TimeSpan.Zero;
        private TimeSpan _gifEndTime = TimeSpan.FromSeconds(5);

        private DispatcherTimer? _gifPlayerTimer;
        private bool _isGifPlayerSeeking = false;
        private bool _isLoopPreviewActive = false;
        private bool _isGifSettingsExpanded = false;

        private bool _isTimerUpdatingScrubber = false;
        private bool _wasPlayingBeforeSeek = false;
        private CancellationTokenSource? _previewFrameCts;
        private readonly Dictionary<int, BitmapSource> _previewFrameCache = new();
        private bool _isGifPlayerPlaying => _gifPlayerTimer != null && _gifPlayerTimer.IsEnabled;

        private VideoCropRect _cropRect = new VideoCropRect();
        private int _sourceVideoWidth = 0;
        private int _sourceVideoHeight = 0;

        private enum GifEditorTool { Cursor, Text, Crop }
        private GifEditorTool _activeGifTool = GifEditorTool.Cursor;
        private readonly List<GifTextLabelControl> _textLabelControls = new();

        private string _currentFontFamily = "Segoe UI";
        private double _currentFontSize = 28.0;
        private bool _currentIsBold = true;
        private bool _currentIsItalic = false;
        private bool _currentIsUnderline = false;
        private bool _currentIsStrikethrough = false;
        private string _currentTextColor = "#FFFFFF";
        private double _currentH = 0.0;
        private double _currentS = 0.0;
        private double _currentV = 1.0;
        private bool _isUpdatingFormattingUI = false;
        private bool _isUpdatingColorPickerUI = false;

        private string? _previewProxyVideo;
        private CancellationTokenSource? _proxyCts;

        private void CleanupPreviewProxy()
        {
            _proxyCts?.Cancel();
            _proxyCts = null;
            string? oldProxy = _previewProxyVideo;
            _previewProxyVideo = null;
            if (!string.IsNullOrEmpty(oldProxy))
            {
                Task.Run(() =>
                {
                    try
                    {
                        Thread.Sleep(500);
                        if (File.Exists(oldProxy))
                        {
                            File.Delete(oldProxy);
                        }
                    }
                    catch { }
                });
            }
        }

        private void InitializeGifWebp()
        {
            _gifWebpService = new GifWebpService(_dependencyManager);

            _gifPlayerTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(50)
            };
            _gifPlayerTimer.Tick += GifPlayerTimer_Tick;

            InitializeTextFormattingRibbon();
        }

        public void OpenVideoInGifCreator(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return;

            // Navigate to GIF view
            GifCreatorButton_Click(this, new RoutedEventArgs());

            // Load the video
            LoadVideoIntoGifCreator(filePath);
        }

        private void GifCreatorButton_Click(object sender, RoutedEventArgs e)
        {
            CollapseSpotifyDrawer();

            if (GifWebpScrollViewer != null && GifWebpScrollViewer.Visibility == Visibility.Visible)
            {
                // Toggle back to main
                GifWebpScrollViewer.Visibility = Visibility.Collapsed;
                if (MainScrollViewer != null) MainScrollViewer.Visibility = Visibility.Visible;
                return;
            }

            // Collapse all other views
            if (MainScrollViewer != null) MainScrollViewer.Visibility = Visibility.Collapsed;
            if (HistoryScrollViewer != null) HistoryScrollViewer.Visibility = Visibility.Collapsed;
            if (SettingsScrollViewer != null) SettingsScrollViewer.Visibility = Visibility.Collapsed;
            if (QueueScrollViewer != null) QueueScrollViewer.Visibility = Visibility.Collapsed;
            if (SearchScrollViewer != null) SearchScrollViewer.Visibility = Visibility.Collapsed;
            if (LiveStreamScrollViewer != null) LiveStreamScrollViewer.Visibility = Visibility.Collapsed;
            if (ConverterScrollViewer != null) ConverterScrollViewer.Visibility = Visibility.Collapsed;
            if (CompressorScrollViewer != null) CompressorScrollViewer.Visibility = Visibility.Collapsed;

            if (GifWebpScrollViewer != null)
            {
                GifWebpScrollViewer.Visibility = Visibility.Visible;
                ApplyElevatedDragDropFix();
            }
        }

        private void BackFromGifCreator_Click(object sender, RoutedEventArgs e)
        {
            StopGifPlayer();
            CleanupPreviewProxy();
            if (_isGifGenerating)
            {
                _gifCts?.Cancel();
                _gifWebpService?.CancelCurrentProcess();
            }
            if (GifWebpScrollViewer != null) GifWebpScrollViewer.Visibility = Visibility.Collapsed;
            if (MainScrollViewer != null) MainScrollViewer.Visibility = Visibility.Visible;
        }

        private void GifBrowseVideo_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new OpenFileDialog
            {
                Title = "Select Video for GIF / WebP Clip",
                Filter = "Video Files|*.mp4;*.mkv;*.mov;*.webm;*.avi;*.flv;*.wmv;*.ts;*.m4v|All Files|*.*"
            };

            if (ofd.ShowDialog(this) == true)
            {
                LoadVideoIntoGifCreator(ofd.FileName);
            }
        }

        private void LoadVideoIntoGifCreator(string filePath)
        {
            if (!File.Exists(filePath)) return;

            CleanupPreviewProxy();
            _currentGifSourceVideo = filePath;
            var fi = new FileInfo(filePath);

            _previewFrameCache.Clear();
            _previewFrameCts?.Cancel();
            _isTimerUpdatingScrubber = true;
            if (GifPlayerScrubber != null) GifPlayerScrubber.Value = 0;
            _isTimerUpdatingScrubber = false;

            _textLabelControls.Clear();
            if (GifTextCanvas != null) GifTextCanvas.Children.Clear();
            SetActiveGifTool(GifEditorTool.Cursor);

            // Hide empty drop zone, show active studio editor
            if (GifDropZone != null) GifDropZone.Visibility = Visibility.Collapsed;
            if (GifEditorContainer != null) GifEditorContainer.Visibility = Visibility.Visible;
            if (GifCompletionCard != null) GifCompletionCard.Visibility = Visibility.Collapsed;

            if (GifVideoTitleText != null) GifVideoTitleText.Text = fi.Name;
            if (GifVideoMetaText != null) GifVideoMetaText.Text = $"Loading info... • {Utilities.FormatBytesOutput(fi.Length)}";

            // Initialize default destination folder
            string defaultFolder = Path.Combine(Path.GetDirectoryName(filePath) ?? SelectedDirectory ?? "", "Clips");
            if (GifDestinationTextBox != null)
            {
                GifDestinationTextBox.Text = defaultFolder;
            }

            // Load into MediaElement player
            try
            {
                if (GifMediaPlayer != null)
                {
                    GifMediaPlayer.Source = new Uri(filePath, UriKind.Absolute);
                    GifMediaPlayer.Play();
                    GifMediaPlayer.Pause();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GifWebp] Media load error: {ex.Message}");
            }

            // Render initial frame preview immediately
            SeekToPosition(TimeSpan.Zero, forceHighQuality: true);

            // Extract accurate media metadata in background
            Task.Run(async () =>
            {
                if (_gifWebpService != null)
                {
                    var info = await _gifWebpService.GetMediaInfoAsync(filePath);
                    await Dispatcher.InvokeAsync(() =>
                    {
                        _gifTotalDuration = info.Duration > TimeSpan.Zero ? info.Duration : TimeSpan.FromSeconds(10);
                        string res = info.Width > 0 ? $"{info.Width}x{info.Height}" : "Original";
                        string fps = info.Fps > 0 ? $"{info.Fps:F0} fps" : "";
                        if (GifVideoMetaText != null)
                        {
                            GifVideoMetaText.Text = $"{res} • {fps} • {Utilities.FormatBytesOutput(fi.Length)} • Total: {FormatTimeSpan(_gifTotalDuration)}";
                        }

                        // Set initial clip range: 0 to min(5s, duration)
                        _gifStartTime = TimeSpan.Zero;
                        _gifEndTime = _gifTotalDuration > TimeSpan.FromSeconds(5) ? TimeSpan.FromSeconds(5) : _gifTotalDuration;

                        _sourceVideoWidth = info.Width;
                        _sourceVideoHeight = info.Height;
                        _cropRect = new VideoCropRect();

                        UpdateTrimmerUi();
                        UpdateOutputDestinationPreview();
                        UpdateCropOverlay();
                        UpdateTimelineTrimHighlight();

                        // For 4K/high-res or MKV/heavy containers, generate lightweight 480p preview proxy in background
                        if (info.Width > 1920 || info.Height > 1080 || filePath.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase) || filePath.EndsWith(".mov", StringComparison.OrdinalIgnoreCase))
                        {
                            _proxyCts = new CancellationTokenSource();
                            var proxyToken = _proxyCts.Token;
                            Task.Run(async () =>
                            {
                                try
                                {
                                    string? proxy = await _gifWebpService.GeneratePreviewProxyAsync(filePath, Downloader.App.AppTempDirectory, proxyToken);
                                    if (proxy != null && !proxyToken.IsCancellationRequested)
                                    {
                                        await Dispatcher.InvokeAsync(() =>
                                        {
                                            if (proxyToken.IsCancellationRequested || _currentGifSourceVideo != filePath) return;
                                            _previewProxyVideo = proxy;
                                            if (GifMediaPlayer != null)
                                            {
                                                var pos = GifMediaPlayer.Position;
                                                bool playing = _isGifPlayerPlaying;
                                                GifMediaPlayer.Source = new Uri(proxy, UriKind.Absolute);
                                                GifMediaPlayer.Position = pos;
                                                if (playing) GifMediaPlayer.Play();
                                                else GifMediaPlayer.Pause();
                                            }
                                        });
                                    }
                                }
                                catch { }
                            });
                        }
                    });
                }
            });
        }

        private void UpdateTrimmerUi()
        {
            if (GifStartTimeTextBox != null) GifStartTimeTextBox.Text = FormatTimeSpanDetailed(_gifStartTime);
            if (GifEndTimeTextBox != null) GifEndTimeTextBox.Text = FormatTimeSpanDetailed(_gifEndTime);

            var clipLen = _gifEndTime > _gifStartTime ? _gifEndTime - _gifStartTime : TimeSpan.FromSeconds(1);
            int fps = GetSelectedFps();
            int estFrames = (int)(clipLen.TotalSeconds * fps);

            if (GifClipDurationBadge != null)
            {
                GifClipDurationBadge.Text = $"Selected Clip: {clipLen.TotalSeconds:F2}s ({estFrames} frames @ {fps} fps)";
            }

            if (GifPlayerScrubber != null && _gifTotalDuration.TotalSeconds > 0)
            {
                GifPlayerScrubber.Maximum = _gifTotalDuration.TotalSeconds;
            }

            UpdateTelegramDurationWarning();
            UpdateTimelineTrimHighlight();
        }

        private void UpdateOutputDestinationPreview()
        {
            if (string.IsNullOrEmpty(_currentGifSourceVideo)) return;

            string ext = IsTelegramStickerSelected() ? ".webm" :
                         IsWebpSelected() ? ".webp" : ".gif";
            string folder = GifDestinationTextBox?.Text?.Trim() ?? "";

            if (string.IsNullOrEmpty(folder))
            {
                string sourceDir = Path.GetDirectoryName(_currentGifSourceVideo) ?? SelectedDirectory ?? "";
                folder = Path.Combine(sourceDir, "Clips");
            }

            string baseName = Path.GetFileNameWithoutExtension(_currentGifSourceVideo);
            string startStr = _gifStartTime.ToString(@"mm\-ss", CultureInfo.InvariantCulture);
            string endStr = _gifEndTime.ToString(@"mm\-ss", CultureInfo.InvariantCulture);
            string outputName = $"{baseName}_{startStr}_{endStr}{ext}";

            if (GifOutputPreviewText != null)
            {
                GifOutputPreviewText.Text = Path.Combine(folder, outputName);
            }
        }

        // ==================== Player & Preview Loop Controls ====================

        private void GifPlayer_MediaOpened(object sender, RoutedEventArgs e)
        {
            if (GifMediaPlayer != null)
            {
                if (GifMediaPlayer.NaturalDuration.HasTimeSpan && _gifTotalDuration == TimeSpan.Zero)
                {
                    _gifTotalDuration = GifMediaPlayer.NaturalDuration.TimeSpan;
                    UpdateTrimmerUi();
                }

                if (_sourceVideoWidth <= 0 && GifMediaPlayer.NaturalVideoWidth > 0)
                {
                    _sourceVideoWidth = GifMediaPlayer.NaturalVideoWidth;
                    _sourceVideoHeight = GifMediaPlayer.NaturalVideoHeight;
                    UpdateCropOverlay();
                }

                UpdateTimelineTrimHighlight();
            }
        }

        private void GifPlayer_MediaEnded(object sender, RoutedEventArgs e)
        {
            if (GifMediaPlayer != null)
            {
                GifMediaPlayer.Position = _gifStartTime;
                GifMediaPlayer.Play();
            }
        }

        private void GifPlayPause_Click(object sender, RoutedEventArgs e)
        {
            if (GifMediaPlayer == null) return;

            if (_isGifPlayerPlaying)
            {
                StopGifPlayer();
            }
            else
            {
                StartGifPlayer(false);
            }
        }

        private void StartGifPlayer(bool isLoopPreview)
        {
            if (GifMediaPlayer == null) return;

            _isLoopPreviewActive = isLoopPreview;

            // Hide still frame preview so live video renders natively
            if (GifFramePreviewImage != null)
            {
                GifFramePreviewImage.Visibility = Visibility.Collapsed;
            }

            if (isLoopPreview)
            {
                GifMediaPlayer.Position = _gifStartTime;
            }

            GifMediaPlayer.Play();
            _gifPlayerTimer?.Start();

            if (GifPlayPauseIcon != null) GifPlayPauseIcon.Text = "⏸";
        }

        private void StopGifPlayer()
        {
            if (GifMediaPlayer == null) return;

            var currentPos = GifPlayerScrubber != null ? TimeSpan.FromSeconds(GifPlayerScrubber.Value) : GifMediaPlayer.Position;
            GifMediaPlayer.Pause();
            _gifPlayerTimer?.Stop();
            _isLoopPreviewActive = false;

            if (GifPlayPauseIcon != null) GifPlayPauseIcon.Text = "▶";

            // When stopping, refresh with a crisp high-fidelity preview frame
            SeekToPosition(currentPos, forceHighQuality: true);
        }

        private void GifPlayerTimer_Tick(object? sender, EventArgs e)
        {
            if (GifMediaPlayer == null || _isGifPlayerSeeking) return;

            var pos = GifMediaPlayer.Position;

            // In loop preview mode, loop strictly between start and end
            if (_isLoopPreviewActive)
            {
                if (pos >= _gifEndTime || pos < _gifStartTime)
                {
                    GifMediaPlayer.Position = _gifStartTime;
                    pos = _gifStartTime;
                }
            }

            if (GifPlayerScrubber != null && _gifTotalDuration.TotalSeconds > 0)
            {
                _isTimerUpdatingScrubber = true;
                GifPlayerScrubber.Value = pos.TotalSeconds;
                _isTimerUpdatingScrubber = false;
            }

            if (GifPlayerTimeText != null)
            {
                GifPlayerTimeText.Text = $"{FormatTimeSpan(pos)} / {FormatTimeSpan(_gifTotalDuration)}";
            }
        }

        private void SeekToPosition(TimeSpan targetTime, bool forceHighQuality = false)
        {
            if (_gifTotalDuration > TimeSpan.Zero)
            {
                if (targetTime < TimeSpan.Zero) targetTime = TimeSpan.Zero;
                if (targetTime > _gifTotalDuration) targetTime = _gifTotalDuration;
            }

            // Update scrubber value without triggering recursive ValueChanged
            if (GifPlayerScrubber != null && !_isGifPlayerSeeking)
            {
                _isTimerUpdatingScrubber = true;
                GifPlayerScrubber.Value = targetTime.TotalSeconds;
                _isTimerUpdatingScrubber = false;
            }

            // Update time text label immediately
            if (GifPlayerTimeText != null)
            {
                GifPlayerTimeText.Text = $"{FormatTimeSpan(targetTime)} / {FormatTimeSpan(_gifTotalDuration)}";
            }

            // If actively playing, let MediaElement play forward
            if (_isGifPlayerPlaying)
            {
                if (GifMediaPlayer != null)
                {
                    GifMediaPlayer.Position = targetTime;
                }
                if (GifFramePreviewImage != null)
                {
                    GifFramePreviewImage.Visibility = Visibility.Collapsed;
                }
                return;
            }

            // Check frame cache for instant sub-millisecond hit
            int cacheKey = (int)Math.Round(targetTime.TotalSeconds * 4.0); // 250ms buckets
            if (_previewFrameCache.TryGetValue(cacheKey, out var cachedBmp))
            {
                if (GifFramePreviewImage != null)
                {
                    GifFramePreviewImage.Source = cachedBmp;
                    GifFramePreviewImage.Visibility = Visibility.Visible;
                }
                if (GifMediaPlayer != null)
                {
                    GifMediaPlayer.Position = targetTime;
                }
                return;
            }

            // Update MediaElement position as baseline
            if (GifMediaPlayer != null)
            {
                GifMediaPlayer.Position = targetTime;
            }

            // Asynchronously extract and render exact frame via FFmpeg
            if (!string.IsNullOrEmpty(_currentGifSourceVideo) && File.Exists(_currentGifSourceVideo))
            {
                _previewFrameCts?.Cancel();
                _previewFrameCts = new CancellationTokenSource();
                var token = _previewFrameCts.Token;

                _ = RequestFramePreviewAsync(_currentGifSourceVideo, targetTime, cacheKey, token);
            }
        }

        private async Task RequestFramePreviewAsync(string videoPath, TimeSpan targetTime, int cacheKey, CancellationToken token)
        {
            try
            {
                if (_gifWebpService == null) return;

                string sourceToExtract = (!string.IsNullOrEmpty(_previewProxyVideo) && File.Exists(_previewProxyVideo))
                    ? _previewProxyVideo
                    : videoPath;

                byte[]? frameBytes = await _gifWebpService.ExtractFrameBytesAsync(sourceToExtract, targetTime, 640, token);
                if (token.IsCancellationRequested || frameBytes == null || frameBytes.Length == 0)
                    return;

                await Dispatcher.InvokeAsync(() =>
                {
                    if (token.IsCancellationRequested) return;

                    try
                    {
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.StreamSource = new MemoryStream(frameBytes);
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.EndInit();
                        bitmap.Freeze();

                        if (_previewFrameCache.Count > 120)
                        {
                            _previewFrameCache.Clear();
                        }
                        _previewFrameCache[cacheKey] = bitmap;

                        if (GifFramePreviewImage != null && !_isGifPlayerPlaying)
                        {
                            GifFramePreviewImage.Source = bitmap;
                            GifFramePreviewImage.Visibility = Visibility.Visible;
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[GifWebp] Bitmap decode error: {ex.Message}");
                    }
                });
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GifWebp] RequestFramePreviewAsync error: {ex.Message}");
            }
        }

        private void GifPlayerScrubber_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            _isGifPlayerSeeking = true;
            _wasPlayingBeforeSeek = _isGifPlayerPlaying;
            if (_wasPlayingBeforeSeek)
            {
                GifMediaPlayer?.Pause();
                _gifPlayerTimer?.Stop();
                if (GifPlayPauseIcon != null) GifPlayPauseIcon.Text = "▶";
            }
        }

        private void GifPlayerScrubber_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            _isGifPlayerSeeking = false;
            if (GifPlayerScrubber != null)
            {
                var target = TimeSpan.FromSeconds(GifPlayerScrubber.Value);
                SeekToPosition(target, forceHighQuality: true);

                if (_wasPlayingBeforeSeek)
                {
                    StartGifPlayer(_isLoopPreviewActive);
                }
            }
        }

        private void GifPlayerScrubber_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isTimerUpdatingScrubber) return;

            var target = TimeSpan.FromSeconds(e.NewValue);
            SeekToPosition(target, forceHighQuality: !_isGifPlayerSeeking);
        }

        private void GifStepBack_Click(object sender, RoutedEventArgs e)
        {
            StopGifPlayer();
            var current = GifPlayerScrubber != null ? TimeSpan.FromSeconds(GifPlayerScrubber.Value) : (GifMediaPlayer?.Position ?? TimeSpan.Zero);
            var target = current - TimeSpan.FromSeconds(0.1);
            if (target < TimeSpan.Zero) target = TimeSpan.Zero;
            SeekToPosition(target, forceHighQuality: true);
        }

        private void GifStepForward_Click(object sender, RoutedEventArgs e)
        {
            StopGifPlayer();
            var current = GifPlayerScrubber != null ? TimeSpan.FromSeconds(GifPlayerScrubber.Value) : (GifMediaPlayer?.Position ?? TimeSpan.Zero);
            var target = current + TimeSpan.FromSeconds(0.1);
            if (_gifTotalDuration > TimeSpan.Zero && target > _gifTotalDuration) target = _gifTotalDuration;
            SeekToPosition(target, forceHighQuality: true);
        }

        private void GifJumpStart_Click(object sender, RoutedEventArgs e)
        {
            StopGifPlayer();
            SeekToPosition(TimeSpan.Zero, forceHighQuality: true);
        }

        private void GifJumpEnd_Click(object sender, RoutedEventArgs e)
        {
            StopGifPlayer();
            var target = _gifTotalDuration > TimeSpan.Zero ? _gifTotalDuration : TimeSpan.FromSeconds(GifPlayerScrubber?.Maximum ?? 10);
            SeekToPosition(target, forceHighQuality: true);
        }

        private void GifSeekToStartTime_Click(object sender, RoutedEventArgs e)
        {
            StopGifPlayer();
            SeekToPosition(_gifStartTime, forceHighQuality: true);
        }

        private void GifSeekToEndTime_Click(object sender, RoutedEventArgs e)
        {
            StopGifPlayer();
            SeekToPosition(_gifEndTime, forceHighQuality: true);
        }

        private void GifTimelineGrid_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateTimelineTrimHighlight();
        }

        private void UpdateTimelineTrimHighlight()
        {
            if (GifTimelineTrackCanvas == null || GifTimelineTrimHighlight == null || _gifTotalDuration <= TimeSpan.Zero)
                return;

            double canvasWidth = GifTimelineTrackCanvas.ActualWidth;
            if (canvasWidth <= 10 && GifPlayerScrubber != null)
            {
                canvasWidth = GifPlayerScrubber.ActualWidth;
            }
            if (canvasWidth <= 10) return;

            double startFrac = Math.Clamp(_gifStartTime.TotalSeconds / _gifTotalDuration.TotalSeconds, 0, 1.0);
            double endFrac = Math.Clamp(_gifEndTime.TotalSeconds / _gifTotalDuration.TotalSeconds, 0, 1.0);
            if (endFrac < startFrac) endFrac = startFrac;

            double left = startFrac * canvasWidth;
            double width = Math.Max(2, (endFrac - startFrac) * canvasWidth);

            Canvas.SetLeft(GifTimelineTrimHighlight, left);
            GifTimelineTrimHighlight.Width = width;
        }

        private void GifSetCurrentAsStart_Click(object sender, RoutedEventArgs e)
        {
            var pos = GifPlayerScrubber != null ? TimeSpan.FromSeconds(GifPlayerScrubber.Value) : (GifMediaPlayer?.Position ?? TimeSpan.Zero);
            if (pos < _gifEndTime)
            {
                _gifStartTime = pos;
            }
            else
            {
                _gifStartTime = pos;
                _gifEndTime = pos + TimeSpan.FromSeconds(3);
                if (_gifEndTime > _gifTotalDuration && _gifTotalDuration > TimeSpan.Zero)
                    _gifEndTime = _gifTotalDuration;
            }
            UpdateTrimmerUi();
            UpdateOutputDestinationPreview();
        }

        private void GifSetCurrentAsEnd_Click(object sender, RoutedEventArgs e)
        {
            var pos = GifPlayerScrubber != null ? TimeSpan.FromSeconds(GifPlayerScrubber.Value) : (GifMediaPlayer?.Position ?? TimeSpan.Zero);
            if (pos > _gifStartTime)
            {
                _gifEndTime = pos;
            }
            else
            {
                _gifEndTime = pos;
                _gifStartTime = pos > TimeSpan.FromSeconds(3) ? pos - TimeSpan.FromSeconds(3) : TimeSpan.Zero;
            }
            UpdateTrimmerUi();
            UpdateOutputDestinationPreview();
        }

        private void GifPreviewLoop_Click(object sender, RoutedEventArgs e)
        {
            StartGifPlayer(true);
        }

        private void GifStartTimeTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (TryParseTimeSpan(GifStartTimeTextBox?.Text, out TimeSpan parsed))
            {
                if (parsed < _gifEndTime)
                {
                    _gifStartTime = parsed;
                }
            }
            UpdateTrimmerUi();
            UpdateOutputDestinationPreview();
        }

        private void GifEndTimeTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (TryParseTimeSpan(GifEndTimeTextBox?.Text, out TimeSpan parsed))
            {
                if (parsed > _gifStartTime)
                {
                    _gifEndTime = parsed;
                }
            }
            UpdateTrimmerUi();
            UpdateOutputDestinationPreview();
        }

        private void GifTimeTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                if (sender is TextBox tb)
                {
                    if (tb == GifStartTimeTextBox) GifStartTimeTextBox_LostFocus(sender, e);
                    else if (tb == GifEndTimeTextBox) GifEndTimeTextBox_LostFocus(sender, e);
                    Keyboard.ClearFocus();
                }
            }
        }

        private static string FormatTimeSpanDetailed(TimeSpan ts)
        {
            return $"{(int)ts.TotalMinutes:00}:{ts.Seconds:00}.{ts.Milliseconds / 10:00}";
        }

        private static string FormatTimeSpan(TimeSpan ts)
        {
            return $"{(int)ts.TotalMinutes:00}:{ts.Seconds:00}";
        }

        private static bool TryParseTimeSpan(string? input, out TimeSpan result)
        {
            result = TimeSpan.Zero;
            if (string.IsNullOrWhiteSpace(input)) return false;

            input = input.Trim().Replace(',', '.');

            // Format: mm:ss.ff or ss.ff
            if (input.Contains(':'))
            {
                var parts = input.Split(':');
                if (parts.Length == 2 &&
                    double.TryParse(parts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out double mins) &&
                    double.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out double secs))
                {
                    result = TimeSpan.FromMinutes(mins) + TimeSpan.FromSeconds(secs);
                    return true;
                }
            }
            else if (double.TryParse(input, NumberStyles.Any, CultureInfo.InvariantCulture, out double totalSecs))
            {
                result = TimeSpan.FromSeconds(totalSecs);
                return true;
            }

            return false;
        }

        // ==================== Interactive Crop Editor ====================

        private void GifPlayerViewportGrid_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateCropOverlay();
        }

        private (double renderX, double renderY, double renderW, double renderH) GetRenderedVideoBounds()
        {
            if (GifPlayerViewportGrid == null || _sourceVideoWidth <= 0 || _sourceVideoHeight <= 0)
                return (0, 0, 1, 1);

            double containerW = GifPlayerViewportGrid.ActualWidth;
            double containerH = GifPlayerViewportGrid.ActualHeight;
            if (containerW <= 10 || containerH <= 10) return (0, 0, 1, 1);

            double videoAspect = (double)_sourceVideoWidth / _sourceVideoHeight;
            double containerAspect = containerW / containerH;

            if (containerAspect > videoAspect)
            {
                double renderH = containerH;
                double renderW = containerH * videoAspect;
                return ((containerW - renderW) / 2.0, 0, renderW, renderH);
            }
            else
            {
                double renderW = containerW;
                double renderH = containerW / videoAspect;
                return (0, (containerH - renderH) / 2.0, renderW, renderH);
            }
        }

        private void UpdateCropOverlay()
        {
            if (GifPlayerViewportGrid == null || GifCropCanvas == null) return;

            if (_sourceVideoWidth <= 0 || _sourceVideoHeight <= 0)
            {
                GifCropCanvas.Visibility = Visibility.Collapsed;
                return;
            }

            GifCropCanvas.Visibility = Visibility.Visible;

            var (renderX, renderY, renderW, renderH) = GetRenderedVideoBounds();
            if (renderW <= 10 || renderH <= 10) return;

            // Screen pixel position for crop box
            double screenCropX = renderX + _cropRect.X * renderW;
            double screenCropY = renderY + _cropRect.Y * renderH;
            double screenCropW = Math.Max(24, _cropRect.Width * renderW);
            double screenCropH = Math.Max(24, _cropRect.Height * renderH);

            // Crop Box Border
            if (CropBoxBorder != null)
            {
                Canvas.SetLeft(CropBoxBorder, screenCropX);
                Canvas.SetTop(CropBoxBorder, screenCropY);
                CropBoxBorder.Width = screenCropW;
                CropBoxBorder.Height = screenCropH;
            }

            // 4 Dimmed Masks
            if (CropMaskTop != null)
            {
                Canvas.SetLeft(CropMaskTop, renderX);
                Canvas.SetTop(CropMaskTop, renderY);
                CropMaskTop.Width = renderW;
                CropMaskTop.Height = Math.Max(0, screenCropY - renderY);
            }

            if (CropMaskBottom != null)
            {
                Canvas.SetLeft(CropMaskBottom, renderX);
                Canvas.SetTop(CropMaskBottom, screenCropY + screenCropH);
                CropMaskBottom.Width = renderW;
                CropMaskBottom.Height = Math.Max(0, (renderY + renderH) - (screenCropY + screenCropH));
            }

            if (CropMaskLeft != null)
            {
                Canvas.SetLeft(CropMaskLeft, renderX);
                Canvas.SetTop(CropMaskLeft, screenCropY);
                CropMaskLeft.Width = Math.Max(0, screenCropX - renderX);
                CropMaskLeft.Height = screenCropH;
            }

            if (CropMaskRight != null)
            {
                Canvas.SetLeft(CropMaskRight, screenCropX + screenCropW);
                Canvas.SetTop(CropMaskRight, screenCropY);
                CropMaskRight.Width = Math.Max(0, (renderX + renderW) - (screenCropX + screenCropW));
                CropMaskRight.Height = screenCropH;
            }

            // 4 Edge Handles
            if (CropHandleTop != null)
            {
                Canvas.SetLeft(CropHandleTop, screenCropX + (screenCropW - 28) / 2.0);
                Canvas.SetTop(CropHandleTop, screenCropY - 4);
            }

            if (CropHandleBottom != null)
            {
                Canvas.SetLeft(CropHandleBottom, screenCropX + (screenCropW - 28) / 2.0);
                Canvas.SetTop(CropHandleBottom, screenCropY + screenCropH - 4);
            }

            if (CropHandleLeft != null)
            {
                Canvas.SetLeft(CropHandleLeft, screenCropX - 4);
                Canvas.SetTop(CropHandleLeft, screenCropY + (screenCropH - 28) / 2.0);
            }

            if (CropHandleRight != null)
            {
                Canvas.SetLeft(CropHandleRight, screenCropX + screenCropW - 4);
                Canvas.SetTop(CropHandleRight, screenCropY + (screenCropH - 28) / 2.0);
            }

            // 4 Corner Handles
            if (CropHandleTopLeft != null)
            {
                Canvas.SetLeft(CropHandleTopLeft, screenCropX - 6);
                Canvas.SetTop(CropHandleTopLeft, screenCropY - 6);
            }

            if (CropHandleTopRight != null)
            {
                Canvas.SetLeft(CropHandleTopRight, screenCropX + screenCropW - 6);
                Canvas.SetTop(CropHandleTopRight, screenCropY - 6);
            }

            if (CropHandleBottomLeft != null)
            {
                Canvas.SetLeft(CropHandleBottomLeft, screenCropX - 6);
                Canvas.SetTop(CropHandleBottomLeft, screenCropY + screenCropH - 6);
            }

            if (CropHandleBottomRight != null)
            {
                Canvas.SetLeft(CropHandleBottomRight, screenCropX + screenCropW - 6);
                Canvas.SetTop(CropHandleBottomRight, screenCropY + screenCropH - 6);
            }

            // Crop Dimension Badge
            var (pxX, pxY, pxW, pxH) = _cropRect.ToPixelCrop(_sourceVideoWidth, _sourceVideoHeight);
            if (CropDimensionText != null)
            {
                CropDimensionText.Text = $"{pxW} × {pxH}";
            }

            if (GifCropInfoText != null)
            {
                if (_cropRect.IsActive)
                {
                    double ratio = (double)pxW / Math.Max(1, pxH);
                    string ratioLabel = Math.Abs(ratio - 1.0) < 0.04 ? "1:1" :
                                       Math.Abs(ratio - 16.0 / 9.0) < 0.04 ? "16:9" :
                                       Math.Abs(ratio - 9.0 / 16.0) < 0.04 ? "9:16" :
                                       Math.Abs(ratio - 4.0 / 3.0) < 0.04 ? "4:3" : $"{ratio:F2}:1";
                    GifCropInfoText.Text = $"✂️ {pxW} × {pxH} ({ratioLabel})";
                }
                else
                {
                    GifCropInfoText.Text = $"Full frame: {_sourceVideoWidth} × {_sourceVideoHeight}";
                }
            }

            UpdateCropInteractivity();
            UpdateTextLabelsPositions();
        }

        private void CropCenter_DragDelta(object sender, DragDeltaEventArgs e)
        {
            var (_, _, renderW, renderH) = GetRenderedVideoBounds();
            if (renderW <= 0 || renderH <= 0) return;

            double dx = e.HorizontalChange / renderW;
            double dy = e.VerticalChange / renderH;

            _cropRect.X = Math.Clamp(_cropRect.X + dx, 0, Math.Max(0, 1.0 - _cropRect.Width));
            _cropRect.Y = Math.Clamp(_cropRect.Y + dy, 0, Math.Max(0, 1.0 - _cropRect.Height));

            UpdateCropOverlay();
        }

        private void CropEdge_DragDelta(object sender, DragDeltaEventArgs e)
        {
            var (_, _, renderW, renderH) = GetRenderedVideoBounds();
            if (renderW <= 0 || renderH <= 0 || sender is not FrameworkElement fe) return;

            string tag = fe.Tag as string ?? "";
            double dx = e.HorizontalChange / renderW;
            double dy = e.VerticalChange / renderH;
            double minPct = 0.05;

            switch (tag)
            {
                case "Top":
                    double newTop = Math.Clamp(_cropRect.Y + dy, 0, _cropRect.Y + _cropRect.Height - minPct);
                    _cropRect.Height = (_cropRect.Y + _cropRect.Height) - newTop;
                    _cropRect.Y = newTop;
                    break;
                case "Bottom":
                    _cropRect.Height = Math.Clamp(_cropRect.Height + dy, minPct, 1.0 - _cropRect.Y);
                    break;
                case "Left":
                    double newLeft = Math.Clamp(_cropRect.X + dx, 0, _cropRect.X + _cropRect.Width - minPct);
                    _cropRect.Width = (_cropRect.X + _cropRect.Width) - newLeft;
                    _cropRect.X = newLeft;
                    break;
                case "Right":
                    _cropRect.Width = Math.Clamp(_cropRect.Width + dx, minPct, 1.0 - _cropRect.X);
                    break;
            }

            UpdateCropOverlay();
        }

        private void CropCorner_DragDelta(object sender, DragDeltaEventArgs e)
        {
            var (_, _, renderW, renderH) = GetRenderedVideoBounds();
            if (renderW <= 0 || renderH <= 0 || sender is not FrameworkElement fe) return;

            string tag = fe.Tag as string ?? "";
            double dx = e.HorizontalChange / renderW;
            double dy = e.VerticalChange / renderH;
            double minPct = 0.05;

            switch (tag)
            {
                case "TopLeft":
                    double newTopTL = Math.Clamp(_cropRect.Y + dy, 0, _cropRect.Y + _cropRect.Height - minPct);
                    _cropRect.Height = (_cropRect.Y + _cropRect.Height) - newTopTL;
                    _cropRect.Y = newTopTL;

                    double newLeftTL = Math.Clamp(_cropRect.X + dx, 0, _cropRect.X + _cropRect.Width - minPct);
                    _cropRect.Width = (_cropRect.X + _cropRect.Width) - newLeftTL;
                    _cropRect.X = newLeftTL;
                    break;

                case "TopRight":
                    double newTopTR = Math.Clamp(_cropRect.Y + dy, 0, _cropRect.Y + _cropRect.Height - minPct);
                    _cropRect.Height = (_cropRect.Y + _cropRect.Height) - newTopTR;
                    _cropRect.Y = newTopTR;

                    _cropRect.Width = Math.Clamp(_cropRect.Width + dx, minPct, 1.0 - _cropRect.X);
                    break;

                case "BottomLeft":
                    _cropRect.Height = Math.Clamp(_cropRect.Height + dy, minPct, 1.0 - _cropRect.Y);

                    double newLeftBL = Math.Clamp(_cropRect.X + dx, 0, _cropRect.X + _cropRect.Width - minPct);
                    _cropRect.Width = (_cropRect.X + _cropRect.Width) - newLeftBL;
                    _cropRect.X = newLeftBL;
                    break;

                case "BottomRight":
                    _cropRect.Height = Math.Clamp(_cropRect.Height + dy, minPct, 1.0 - _cropRect.Y);
                    _cropRect.Width = Math.Clamp(_cropRect.Width + dx, minPct, 1.0 - _cropRect.X);
                    break;
            }

            UpdateCropOverlay();
        }

        private void GifCropRatioFull_Click(object sender, RoutedEventArgs e)
        {
            _cropRect = new VideoCropRect { X = 0, Y = 0, Width = 1.0, Height = 1.0 };
            UpdateCropOverlay();
        }

        private void GifCropRatio1x1_Click(object sender, RoutedEventArgs e)
        {
            if (_sourceVideoWidth <= 0 || _sourceVideoHeight <= 0) return;

            if (_sourceVideoWidth >= _sourceVideoHeight)
            {
                double normW = (double)_sourceVideoHeight / _sourceVideoWidth;
                double normX = (1.0 - normW) / 2.0;
                _cropRect = new VideoCropRect { X = normX, Y = 0, Width = normW, Height = 1.0 };
            }
            else
            {
                double normH = (double)_sourceVideoWidth / _sourceVideoHeight;
                double normY = (1.0 - normH) / 2.0;
                _cropRect = new VideoCropRect { X = 0, Y = normY, Width = 1.0, Height = normH };
            }
            UpdateCropOverlay();
        }

        private void ApplyAspectRatioCrop(double targetRatio)
        {
            if (_sourceVideoWidth <= 0 || _sourceVideoHeight <= 0 || targetRatio <= 0) return;

            double sourceRatio = (double)_sourceVideoWidth / _sourceVideoHeight;
            if (sourceRatio >= targetRatio)
            {
                double targetW = _sourceVideoHeight * targetRatio;
                double normW = Math.Min(1.0, targetW / _sourceVideoWidth);
                double normX = (1.0 - normW) / 2.0;
                _cropRect = new VideoCropRect { X = normX, Y = 0, Width = normW, Height = 1.0 };
            }
            else
            {
                double targetH = _sourceVideoWidth / targetRatio;
                double normH = Math.Min(1.0, targetH / _sourceVideoHeight);
                double normY = (1.0 - normH) / 2.0;
                _cropRect = new VideoCropRect { X = 0, Y = normY, Width = 1.0, Height = normH };
            }
            UpdateCropOverlay();
        }

        private void GifCropRatio16x9_Click(object sender, RoutedEventArgs e) => ApplyAspectRatioCrop(16.0 / 9.0);
        private void GifCropRatio9x16_Click(object sender, RoutedEventArgs e) => ApplyAspectRatioCrop(9.0 / 16.0);
        private void GifCropRatio4x3_Click(object sender, RoutedEventArgs e) => ApplyAspectRatioCrop(4.0 / 3.0);

        // ==================== Editor Tools & Text Labels Overlay ====================

        private void InitializeTextFormattingRibbon()
        {
            if (GifTextFontComboBox == null || GifTextFontSizeComboBox == null) return;

            _isUpdatingFormattingUI = true;
            try
            {
                var fonts = new[]
                {
                    "Segoe UI", "Impact", "Arial", "Comic Sans MS", "Times New Roman",
                    "Verdana", "Trebuchet MS", "Consolas", "Georgia", "Courier New"
                };
                GifTextFontComboBox.ItemsSource = fonts;
                GifTextFontComboBox.SelectedItem = _currentFontFamily;

                var sizes = new[] { "12", "14", "16", "18", "20", "24", "28", "32", "36", "42", "48", "56", "64", "72", "96", "120" };
                GifTextFontSizeComboBox.ItemsSource = sizes;
                GifTextFontSizeComboBox.SelectedItem = _currentFontSize.ToString("0");
                GifTextFontSizeComboBox.Text = _currentFontSize.ToString("0");

                if (GifTextBoldBtn != null) GifTextBoldBtn.IsChecked = _currentIsBold;
                if (GifTextItalicBtn != null) GifTextItalicBtn.IsChecked = _currentIsItalic;
                if (GifTextUnderlineBtn != null) GifTextUnderlineBtn.IsChecked = _currentIsUnderline;
                if (GifTextStrikethroughBtn != null) GifTextStrikethroughBtn.IsChecked = _currentIsStrikethrough;

                UpdateColorSwatchAndHex(_currentTextColor);
                UpdateTextFormattingBarVisibility();
            }
            finally
            {
                _isUpdatingFormattingUI = false;
            }
        }

        private void UpdateTextFormattingBarVisibility()
        {
            if (GifTextFormattingBar == null) return;
            bool isTextTool = _activeGifTool == GifEditorTool.Text;
            bool hasSelectedLabel = _textLabelControls.Any(c => c.Model.IsSelected);
            bool showTextBar = isTextTool || hasSelectedLabel;

            GifTextFormattingBar.Visibility = showTextBar ? Visibility.Visible : Visibility.Collapsed;
            if (GifEditorDefaultHeader != null)
            {
                GifEditorDefaultHeader.Visibility = showTextBar ? Visibility.Collapsed : Visibility.Visible;
            }
        }

        private void SetActiveGifTool(GifEditorTool tool)
        {
            _activeGifTool = tool;

            var activeBg = new SolidColorBrush(Color.FromRgb(56, 189, 248));
            var activeFg = Brushes.Black;
            var inactiveBg = Brushes.Transparent;
            var inactiveFg = (Brush)FindResource("TextSecondaryBrush");

            if (GifToolCursorBorder != null)
                GifToolCursorBorder.Background = (tool == GifEditorTool.Cursor) ? activeBg : inactiveBg;
            if (GifToolCursorBtn != null)
                GifToolCursorBtn.Foreground = (tool == GifEditorTool.Cursor) ? activeFg : inactiveFg;

            if (GifToolTextBorder != null)
                GifToolTextBorder.Background = (tool == GifEditorTool.Text) ? activeBg : inactiveBg;
            if (GifToolTextBtn != null)
                GifToolTextBtn.Foreground = (tool == GifEditorTool.Text) ? activeFg : inactiveFg;

            if (GifToolCropBorder != null)
                GifToolCropBorder.Background = (tool == GifEditorTool.Crop) ? activeBg : inactiveBg;
            if (GifToolCropBtn != null)
                GifToolCropBtn.Foreground = (tool == GifEditorTool.Crop) ? activeFg : inactiveFg;

            if (GifTextCanvas != null)
            {
                GifTextCanvas.Cursor = (tool == GifEditorTool.Text) ? Cursors.IBeam : Cursors.Arrow;
            }

            if (tool == GifEditorTool.Cursor)
            {
                // When switching to Cursor tool, finish active text editing (close edit box)
                // but keep the selected text label active so resize handles & formatting ribbon remain accessible!
                CommitOrPruneActiveEdit();
            }
            else
            {
                CommitOrPruneActiveEdit();
                DeselectAllTextLabels();
            }

            UpdateCropInteractivity();
            UpdateTextFormattingBarVisibility();
        }

        private void UpdateCropInteractivity()
        {
            bool isCrop = _activeGifTool == GifEditorTool.Crop;

            if (CropCenterThumb != null)
            {
                CropCenterThumb.IsHitTestVisible = isCrop;
            }

            if (CropDimensionBadge != null)
            {
                CropDimensionBadge.Visibility = isCrop ? Visibility.Visible : Visibility.Collapsed;
            }

            Visibility handleVis = isCrop ? Visibility.Visible : Visibility.Collapsed;
            if (CropHandleTop != null) CropHandleTop.Visibility = handleVis;
            if (CropHandleBottom != null) CropHandleBottom.Visibility = handleVis;
            if (CropHandleLeft != null) CropHandleLeft.Visibility = handleVis;
            if (CropHandleRight != null) CropHandleRight.Visibility = handleVis;
            if (CropHandleTopLeft != null) CropHandleTopLeft.Visibility = handleVis;
            if (CropHandleTopRight != null) CropHandleTopRight.Visibility = handleVis;
            if (CropHandleBottomLeft != null) CropHandleBottomLeft.Visibility = handleVis;
            if (CropHandleBottomRight != null) CropHandleBottomRight.Visibility = handleVis;

            if (CropBoxBorder != null)
            {
                CropBoxBorder.BorderBrush = isCrop
                    ? new SolidColorBrush(Color.FromRgb(56, 189, 248))
                    : (_cropRect.IsActive ? new SolidColorBrush(Color.FromArgb(120, 56, 189, 248)) : Brushes.Transparent);
            }

            if (GifTextCanvas != null)
            {
                GifTextCanvas.IsHitTestVisible = !isCrop;
            }
        }

        private void GifToolCursor_Click(object sender, RoutedEventArgs e)
        {
            SetActiveGifTool(GifEditorTool.Cursor);
        }

        private void GifToolText_Click(object sender, RoutedEventArgs e)
        {
            SetActiveGifTool(GifEditorTool.Text);
        }

        private void GifToolCrop_Click(object sender, RoutedEventArgs e)
        {
            SetActiveGifTool(GifEditorTool.Crop);
        }

        private void GifTextCanvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;

            // Commit or prune any currently editing label
            CommitOrPruneActiveEdit();

            if (_activeGifTool == GifEditorTool.Text)
            {
                Point clickPos = e.GetPosition(GifTextCanvas);
                CreateNewTextLabel(clickPos);
                e.Handled = true;
            }
            else if (_activeGifTool == GifEditorTool.Cursor)
            {
                DeselectAllTextLabels();
            }
        }

        private void CommitOrPruneActiveEdit()
        {
            foreach (var ctrl in _textLabelControls.ToList())
            {
                if (ctrl.Model.IsEditing)
                {
                    ctrl.ExitEditMode();
                }
            }
            UpdateTextFormattingBarVisibility();
        }

        private void CreateNewTextLabel(Point clickPos)
        {
            var (renderX, renderY, renderW, renderH) = GetRenderedVideoBounds();
            double normVideoX = renderW > 0 ? (clickPos.X - renderX) / renderW : 0;
            double normVideoY = renderH > 0 ? (clickPos.Y - renderY) / renderH : 0;

            var model = new GifTextLabel
            {
                Text = "",
                X = clickPos.X,
                Y = clickPos.Y,
                NormalizedX = normVideoX,
                NormalizedY = normVideoY,
                FontSize = _currentFontSize,
                FontFamily = _currentFontFamily,
                IsBold = _currentIsBold,
                IsItalic = _currentIsItalic,
                IsUnderline = _currentIsUnderline,
                IsStrikethrough = _currentIsStrikethrough,
                TextColor = _currentTextColor,
                IsDraft = true
            };

            var control = new GifTextLabelControl(model)
            {
                IsCursorToolActive = () => _activeGifTool == GifEditorTool.Cursor,
                IsTextToolActive = () => _activeGifTool == GifEditorTool.Text
            };

            control.DeleteRequested += OnTextLabelDeleteRequested;
            control.Selected += OnTextLabelSelected;
            control.CreateNewRequested += OnTextLabelCreateNewRequested;
            control.PositionChanged += OnTextLabelPositionChanged;
            control.FormattingChanged += (ctrl) =>
            {
                if (ctrl.Model.IsSelected)
                {
                    SyncFormattingBarWithModel(ctrl.Model);
                }
            };

            Canvas.SetLeft(control, clickPos.X);
            Canvas.SetTop(control, clickPos.Y);

            _textLabelControls.Add(control);
            GifTextCanvas?.Children.Add(control);

            DeselectAllTextLabels();
            control.EnterEditMode();
            SyncFormattingBarWithModel(model);
            UpdateTextFormattingBarVisibility();
        }

        private GifTextLabelControl? GetSelectedLabelControl()
        {
            return _textLabelControls.FirstOrDefault(c => c.Model.IsSelected);
        }

        private void SyncFormattingBarWithModel(GifTextLabel model)
        {
            _isUpdatingFormattingUI = true;
            try
            {
                _currentFontFamily = model.FontFamily;
                _currentFontSize = model.FontSize;
                _currentIsBold = model.IsBold;
                _currentIsItalic = model.IsItalic;
                _currentIsUnderline = model.IsUnderline;
                _currentIsStrikethrough = model.IsStrikethrough;
                _currentTextColor = model.TextColor;

                if (GifTextFontComboBox != null)
                {
                    GifTextFontComboBox.SelectedItem = model.FontFamily;
                }
                if (GifTextFontSizeComboBox != null)
                {
                    string szStr = model.FontSize.ToString("0");
                    GifTextFontSizeComboBox.SelectedItem = szStr;
                    GifTextFontSizeComboBox.Text = szStr;
                }
                if (GifTextBoldBtn != null)
                {
                    GifTextBoldBtn.IsChecked = model.IsBold;
                }
                if (GifTextItalicBtn != null)
                {
                    GifTextItalicBtn.IsChecked = model.IsItalic;
                }
                if (GifTextUnderlineBtn != null)
                {
                    GifTextUnderlineBtn.IsChecked = model.IsUnderline;
                }
                if (GifTextStrikethroughBtn != null)
                {
                    GifTextStrikethroughBtn.IsChecked = model.IsStrikethrough;
                }
                UpdateColorSwatchAndHex(model.TextColor);
            }
            finally
            {
                _isUpdatingFormattingUI = false;
            }
        }

        private void GifTextFontComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingFormattingUI || GifTextFontComboBox.SelectedItem is not string font) return;
            _currentFontFamily = font;
            var sel = GetSelectedLabelControl();
            if (sel != null)
            {
                sel.Model.FontFamily = font;
                sel.RefreshFormatting();
            }
        }

        private void GifTextFontSizeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingFormattingUI || GifTextFontSizeComboBox.SelectedItem is not string sizeStr) return;
            if (double.TryParse(sizeStr, NumberStyles.Any, CultureInfo.InvariantCulture, out double sz))
            {
                ApplyFontSizeToCurrent(sz);
            }
        }

        private void GifTextFontSizeComboBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingFormattingUI || GifTextFontSizeComboBox == null) return;
            string text = GifTextFontSizeComboBox.Text?.Trim() ?? "";
            if (double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out double sz) && sz >= 6 && sz <= 200)
            {
                ApplyFontSizeToCurrent(sz);
            }
        }

        private void ApplyFontSizeToCurrent(double size)
        {
            _currentFontSize = size;
            var sel = GetSelectedLabelControl();
            if (sel != null)
            {
                sel.ApplyFontSize(size);
            }
        }

        private void GifTextFormatButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingFormattingUI) return;

            _currentIsBold = GifTextBoldBtn?.IsChecked == true;
            _currentIsItalic = GifTextItalicBtn?.IsChecked == true;
            _currentIsUnderline = GifTextUnderlineBtn?.IsChecked == true;
            _currentIsStrikethrough = GifTextStrikethroughBtn?.IsChecked == true;

            var sel = GetSelectedLabelControl();
            if (sel != null)
            {
                sel.Model.IsBold = _currentIsBold;
                sel.Model.IsItalic = _currentIsItalic;
                sel.Model.IsUnderline = _currentIsUnderline;
                sel.Model.IsStrikethrough = _currentIsStrikethrough;
                sel.RefreshFormatting();
            }
        }

        // ==================== Circular Spectrum Color Picker ====================

        private static BitmapSource? _cachedColorWheelBitmap;

        private static BitmapSource GetColorWheelBitmap()
        {
            if (_cachedColorWheelBitmap != null) return _cachedColorWheelBitmap;

            // Render at 360x360 for crisp, anti-aliased retina display on 180x180 element
            int width = 360;
            int height = 360;
            int stride = width * 4;
            byte[] pixels = new byte[height * stride];

            double cx = width / 2.0;
            double cy = height / 2.0;
            double maxRadius = 170.0;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    double dx = x - cx;
                    double dy = y - cy;
                    double dist = Math.Sqrt(dx * dx + dy * dy);
                    int idx = (y * width + x) * 4;

                    if (dist <= maxRadius)
                    {
                        double edgeFactor = 1.0;
                        if (dist > maxRadius - 2.0)
                        {
                            double t = Math.Clamp((maxRadius - dist) / 2.0, 0.0, 1.0);
                            edgeFactor = t * t * (3.0 - 2.0 * t); // smoothstep
                        }

                        double angleRad = Math.Atan2(dy, dx);
                        double angleDeg = angleRad * (180.0 / Math.PI);
                        if (angleDeg < 0) angleDeg += 360.0;

                        double hue = angleDeg;
                        double sat = Math.Min(1.0, dist / maxRadius);
                        Color c = ColorFromHsv(hue, sat, 1.0);

                        pixels[idx] = c.B;
                        pixels[idx + 1] = c.G;
                        pixels[idx + 2] = c.R;
                        pixels[idx + 3] = (byte)Math.Clamp(Math.Round(255.0 * edgeFactor), 0, 255);
                    }
                    else
                    {
                        pixels[idx] = 0;
                        pixels[idx + 1] = 0;
                        pixels[idx + 2] = 0;
                        pixels[idx + 3] = 0;
                    }
                }
            }

            _cachedColorWheelBitmap = BitmapSource.Create(
                width, height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
            _cachedColorWheelBitmap.Freeze();
            return _cachedColorWheelBitmap;
        }

        private static void RgbToHsv(Color c, out double h, out double s, out double v)
        {
            double r = c.R / 255.0;
            double g = c.G / 255.0;
            double b = c.B / 255.0;

            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            double delta = max - min;

            v = max;
            s = (max == 0) ? 0 : delta / max;

            if (delta == 0)
            {
                h = 0;
            }
            else if (Math.Abs(max - r) < 0.0001)
            {
                h = 60.0 * (((g - b) / delta) % 6);
            }
            else if (Math.Abs(max - g) < 0.0001)
            {
                h = 60.0 * (((b - r) / delta) + 2);
            }
            else
            {
                h = 60.0 * (((r - g) / delta) + 4);
            }

            if (h < 0) h += 360.0;
        }

        private void UpdateWheelThumbPosition()
        {
            if (GifColorWheelThumb == null) return;
            double cx = 90.0;
            double cy = 90.0;
            double maxRadius = 85.0;

            double angleRad = _currentH * (Math.PI / 180.0);
            double dist = _currentS * maxRadius;

            double tx = cx + dist * Math.Cos(angleRad);
            double ty = cy + dist * Math.Sin(angleRad);

            Canvas.SetLeft(GifColorWheelThumb, tx - 7);
            Canvas.SetTop(GifColorWheelThumb, ty - 7);
        }

        private void UpdateColorFromWheelPoint(Point p)
        {
            double cx = 90.0;
            double cy = 90.0;
            double maxRadius = 85.0;

            double dx = p.X - cx;
            double dy = p.Y - cy;
            double dist = Math.Sqrt(dx * dx + dy * dy);
            if (dist > maxRadius) dist = maxRadius;

            double angleRad = Math.Atan2(dy, dx);
            double angleDeg = angleRad * (180.0 / Math.PI);
            if (angleDeg < 0) angleDeg += 360.0;

            _currentH = angleDeg;
            _currentS = Math.Min(1.0, dist / maxRadius);

            UpdateWheelThumbPosition();

            Color col = ColorFromHsv(_currentH, _currentS, _currentV);
            ApplyColorToUI(col, fromWheel: true);
        }

        private void GifColorWheelCanvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;
            GifColorWheelCanvas.CaptureMouse();
            UpdateColorFromWheelPoint(e.GetPosition(GifColorWheelCanvas));
        }

        private void GifColorWheelCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && GifColorWheelCanvas.IsMouseCaptured)
            {
                UpdateColorFromWheelPoint(e.GetPosition(GifColorWheelCanvas));
            }
        }

        private void GifColorWheelCanvas_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (GifColorWheelCanvas.IsMouseCaptured)
            {
                GifColorWheelCanvas.ReleaseMouseCapture();
            }
        }

        private void UpdateBrightnessFromPoint(Point p)
        {
            if (GifColorBrightnessCanvas == null) return;
            double w = GifColorBrightnessCanvas.ActualWidth;
            if (w <= 0) w = 236.0;
            double thumbW = 10.0;
            double trackW = Math.Max(1.0, w - thumbW);

            double val = Math.Clamp((p.X - thumbW / 2.0) / trackW, 0.0, 1.0);
            _currentV = val;

            UpdateBrightnessThumbPosition();

            Color col = ColorFromHsv(_currentH, _currentS, _currentV);
            ApplyColorToUI(col, fromSlider: true);
        }

        private void UpdateBrightnessThumbPosition()
        {
            if (GifColorBrightnessThumb == null || GifColorBrightnessCanvas == null) return;
            double w = GifColorBrightnessCanvas.ActualWidth;
            if (w <= 0) w = 236.0;
            double thumbW = 10.0;
            double trackW = Math.Max(1.0, w - thumbW);

            double x = Math.Clamp(_currentV * trackW, 0.0, trackW);
            Canvas.SetLeft(GifColorBrightnessThumb, x);
            Canvas.SetTop(GifColorBrightnessThumb, 0);
        }

        private void GifColorBrightnessCanvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;
            GifColorBrightnessCanvas.CaptureMouse();
            UpdateBrightnessFromPoint(e.GetPosition(GifColorBrightnessCanvas));
        }

        private void GifColorBrightnessCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && GifColorBrightnessCanvas.IsMouseCaptured)
            {
                UpdateBrightnessFromPoint(e.GetPosition(GifColorBrightnessCanvas));
            }
        }

        private void GifColorBrightnessCanvas_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (GifColorBrightnessCanvas.IsMouseCaptured)
            {
                GifColorBrightnessCanvas.ReleaseMouseCapture();
            }
        }

        private void ApplyColorToUI(Color col, bool fromWheel = false, bool fromSlider = false)
        {
            string hex = $"#{col.R:X2}{col.G:X2}{col.B:X2}";
            _currentTextColor = hex;

            _isUpdatingColorPickerUI = true;
            try
            {
                if (GifColorInputR != null) GifColorInputR.Text = col.R.ToString();
                if (GifColorInputG != null) GifColorInputG.Text = col.G.ToString();
                if (GifColorInputB != null) GifColorInputB.Text = col.B.ToString();
                if (GifColorInputHex != null) GifColorInputHex.Text = $"{col.R:X2}{col.G:X2}{col.B:X2}";
                if (GifColorPreviewLarge != null) GifColorPreviewLarge.Background = new SolidColorBrush(col);
                if (GifTextColorSwatch != null) GifTextColorSwatch.Background = new SolidColorBrush(col);
                if (GifTextColorHexText != null) GifTextColorHexText.Text = hex;

                if (GifColorBrightnessStop != null)
                {
                    GifColorBrightnessStop.Color = ColorFromHsv(_currentH, _currentS, 1.0);
                }

                if (!fromSlider)
                {
                    UpdateBrightnessThumbPosition();
                }
                if (!fromWheel)
                {
                    UpdateWheelThumbPosition();
                }
            }
            finally
            {
                _isUpdatingColorPickerUI = false;
            }

            var sel = GetSelectedLabelControl();
            if (sel != null)
            {
                sel.Model.TextColor = hex;
                sel.RefreshFormatting();
            }
        }

        private void GifTextColorButton_Click(object sender, RoutedEventArgs e)
        {
            if (GifColorPickerPopup == null) return;

            if (GifColorWheelImage != null && GifColorWheelImage.Source == null)
            {
                GifColorWheelImage.Source = GetColorWheelBitmap();
            }

            var sel = GetSelectedLabelControl();
            if (sel != null && !string.IsNullOrEmpty(sel.Model.TextColor))
            {
                _currentTextColor = sel.Model.TextColor;
            }

            Color col = Colors.White;
            try { col = (Color)ColorConverter.ConvertFromString(_currentTextColor); } catch { }

            RgbToHsv(col, out _currentH, out _currentS, out _currentV);
            ApplyColorToUI(col);

            if (GifTextColorButton != null)
            {
                GifColorPickerPopup.PlacementTarget = GifTextColorButton;
            }
            GifColorPickerPopup.IsOpen = true;
            Dispatcher.InvokeAsync(() => UpdateBrightnessThumbPosition(), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private void GifColorRgbInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingColorPickerUI) return;

            if (byte.TryParse(GifColorInputR?.Text?.Trim(), out byte r) &&
                byte.TryParse(GifColorInputG?.Text?.Trim(), out byte g) &&
                byte.TryParse(GifColorInputB?.Text?.Trim(), out byte b))
            {
                Color col = Color.FromRgb(r, g, b);
                RgbToHsv(col, out _currentH, out _currentS, out _currentV);
                ApplyColorToUI(col);
            }
        }

        private void GifColorHexInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingColorPickerUI) return;

            string raw = GifColorInputHex?.Text?.Trim().TrimStart('#') ?? "";
            if (raw.Length == 6 && uint.TryParse(raw, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _))
            {
                try
                {
                    var col = (Color)ColorConverter.ConvertFromString("#" + raw);
                    RgbToHsv(col, out _currentH, out _currentS, out _currentV);
                    ApplyColorToUI(col);
                }
                catch { }
            }
        }

        private void GifColorPreset_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string hex)
            {
                Color col = Colors.White;
                try { col = (Color)ColorConverter.ConvertFromString(hex); } catch { }
                RgbToHsv(col, out _currentH, out _currentS, out _currentV);
                ApplyColorToUI(col);
            }
        }

        private void GifColorPickerDone_Click(object sender, RoutedEventArgs e)
        {
            if (GifColorPickerPopup != null) GifColorPickerPopup.IsOpen = false;
        }

        private void UpdateColorSwatchAndHex(string hex)
        {
            _currentTextColor = hex;
            try
            {
                var col = (Color)ColorConverter.ConvertFromString(hex);
                var brush = new SolidColorBrush(col);
                if (GifTextColorSwatch != null) GifTextColorSwatch.Background = brush;
                if (GifColorPreviewLarge != null) GifColorPreviewLarge.Background = brush;
                if (GifTextColorHexText != null) GifTextColorHexText.Text = hex.ToUpperInvariant();
            }
            catch { }
        }

        private static Color ColorFromHsv(double hue, double saturation, double value)
        {
            saturation = Math.Clamp(saturation, 0.0, 1.0);
            value = Math.Clamp(value, 0.0, 1.0);
            while (hue < 0) hue += 360.0;
            hue = hue % 360.0;

            int hi = Convert.ToInt32(Math.Floor(hue / 60)) % 6;
            double f = hue / 60 - Math.Floor(hue / 60);

            double vVal = value * 255.0;
            byte v = Convert.ToByte(Math.Clamp(vVal, 0, 255));
            byte p = Convert.ToByte(Math.Clamp(vVal * (1 - saturation), 0, 255));
            byte q = Convert.ToByte(Math.Clamp(vVal * (1 - f * saturation), 0, 255));
            byte t = Convert.ToByte(Math.Clamp(vVal * (1 - (1 - f) * saturation), 0, 255));

            return hi switch
            {
                0 => Color.FromRgb(v, t, p),
                1 => Color.FromRgb(q, v, p),
                2 => Color.FromRgb(p, v, t),
                3 => Color.FromRgb(p, q, v),
                4 => Color.FromRgb(t, p, v),
                _ => Color.FromRgb(v, p, q)
            };
        }

        private void OnTextLabelPositionChanged(GifTextLabelControl ctrl)
        {
            var (renderX, renderY, renderW, renderH) = GetRenderedVideoBounds();
            double left = Canvas.GetLeft(ctrl);
            double top = Canvas.GetTop(ctrl);
            if (double.IsNaN(left)) left = ctrl.Model.X;
            if (double.IsNaN(top)) top = ctrl.Model.Y;

            if (renderW > 0 && renderH > 0)
            {
                ctrl.Model.NormalizedX = (left - renderX) / renderW;
                ctrl.Model.NormalizedY = (top - renderY) / renderH;
            }
        }

        private void OnTextLabelDeleteRequested(GifTextLabelControl ctrl)
        {
            _textLabelControls.Remove(ctrl);
            GifTextCanvas?.Children.Remove(ctrl);
            UpdateTextFormattingBarVisibility();
        }

        private void OnTextLabelSelected(GifTextLabelControl selectedCtrl)
        {
            foreach (var c in _textLabelControls)
            {
                if (c != selectedCtrl)
                {
                    c.SetSelected(false);
                }
            }

            SyncFormattingBarWithModel(selectedCtrl.Model);
            UpdateTextFormattingBarVisibility();
        }

        private void OnTextLabelCreateNewRequested(Point clickPos)
        {
            CreateNewTextLabel(clickPos);
        }

        private void DeselectAllTextLabels()
        {
            foreach (var c in _textLabelControls)
            {
                c.SetSelected(false);
            }
            UpdateTextFormattingBarVisibility();
        }

        private void UpdateTextLabelsPositions()
        {
            var (renderX, renderY, renderW, renderH) = GetRenderedVideoBounds();
            if (renderW <= 10 || renderH <= 10) return;

            double screenCropX = renderX + (_cropRect.IsActive ? _cropRect.X * renderW : 0);
            double screenCropY = renderY + (_cropRect.IsActive ? _cropRect.Y * renderH : 0);
            double screenCropW = _cropRect.IsActive ? Math.Max(24, _cropRect.Width * renderW) : renderW;
            double screenCropH = _cropRect.IsActive ? Math.Max(24, _cropRect.Height * renderH) : renderH;

            if (GifTextCanvas != null)
            {
                GifTextCanvas.Clip = new RectangleGeometry(new Rect(screenCropX, screenCropY, screenCropW, screenCropH));
            }

            foreach (var ctrl in _textLabelControls)
            {
                double newLeft = renderX + ctrl.Model.NormalizedX * renderW;
                double newTop = renderY + ctrl.Model.NormalizedY * renderH;
                Canvas.SetLeft(ctrl, newLeft);
                Canvas.SetTop(ctrl, newTop);
                ctrl.Model.X = newLeft;
                ctrl.Model.Y = newTop;
            }
        }

        private string? GenerateTextOverlayPng(GifWebpOptions options)
        {
            if (_textLabelControls.Count == 0) return null;

            int cropPxX = 0;
            int cropPxY = 0;
            int cropPxW = _sourceVideoWidth;
            int cropPxH = _sourceVideoHeight;

            if (options.Crop != null && options.Crop.IsActive && options.OriginalVideoWidth > 0 && options.OriginalVideoHeight > 0)
            {
                var (cx, cy, cw, ch) = options.Crop.ToPixelCrop(options.OriginalVideoWidth, options.OriginalVideoHeight);
                if (cw > 0 && ch > 0)
                {
                    cropPxX = cx;
                    cropPxY = cy;
                    cropPxW = cw;
                    cropPxH = ch;
                }
            }

            if (cropPxW <= 0 || cropPxH <= 0) return null;

            var (renderX, renderY, renderW, renderH) = GetRenderedVideoBounds();
            double fontScale = (renderW > 0 && _sourceVideoWidth > 0) ? ((double)_sourceVideoWidth / renderW) : 1.0;

            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                foreach (var ctrl in _textLabelControls)
                {
                    var model = ctrl.Model;
                    string text = model.Text?.Trim() ?? "";
                    if (string.IsNullOrEmpty(text)) continue;

                    // Absolute position on the full source video in pixels
                    double videoPxX = model.NormalizedX * _sourceVideoWidth;
                    double videoPxY = model.NormalizedY * _sourceVideoHeight;

                    // Position relative to the cropped frame
                    double cropRelativePxX = videoPxX - cropPxX;
                    double cropRelativePxY = videoPxY - cropPxY;

                    double targetFontSize = Math.Max(10.0, model.FontSize * fontScale);

                    string fontName = string.IsNullOrWhiteSpace(model.FontFamily) ? "Segoe UI" : model.FontFamily;
                    var typeface = new Typeface(
                        new FontFamily($"{fontName}, Segoe UI, Segoe UI Emoji, sans-serif"),
                        model.IsItalic ? FontStyles.Italic : FontStyles.Normal,
                        model.IsBold ? FontWeights.Bold : FontWeights.Normal,
                        FontStretches.Normal);

                    // Parse text color
                    Color textColor = Colors.White;
                    try { textColor = (Color)ColorConverter.ConvertFromString(model.TextColor); } catch { }

                    // Foreground text
                    var foregroundText = new FormattedText(
                        text,
                        CultureInfo.InvariantCulture,
                        FlowDirection.LeftToRight,
                        typeface,
                        targetFontSize,
                        new SolidColorBrush(textColor),
                        1.0);

                    if (model.IsUnderline) foregroundText.SetTextDecorations(TextDecorations.Underline);
                    if (model.IsStrikethrough) foregroundText.SetTextDecorations(TextDecorations.Strikethrough);

                    // Shadow / outline for maximum contrast
                    var shadowBrush = new SolidColorBrush(Color.FromArgb(230, 0, 0, 0));
                    var shadowText = new FormattedText(
                        text,
                        CultureInfo.InvariantCulture,
                        FlowDirection.LeftToRight,
                        typeface,
                        targetFontSize,
                        shadowBrush,
                        1.0);
                    if (model.IsUnderline) shadowText.SetTextDecorations(TextDecorations.Underline);
                    if (model.IsStrikethrough) shadowText.SetTextDecorations(TextDecorations.Strikethrough);

                    double shadowOffset = Math.Max(2.0, targetFontSize * 0.05);
                    dc.DrawText(shadowText, new Point(cropRelativePxX + shadowOffset, cropRelativePxY + shadowOffset));
                    dc.DrawText(shadowText, new Point(cropRelativePxX - shadowOffset * 0.5, cropRelativePxY));
                    dc.DrawText(shadowText, new Point(cropRelativePxX + shadowOffset * 0.5, cropRelativePxY));
                    dc.DrawText(shadowText, new Point(cropRelativePxX, cropRelativePxY - shadowOffset * 0.5));
                    dc.DrawText(shadowText, new Point(cropRelativePxX, cropRelativePxY + shadowOffset * 0.5));

                    dc.DrawText(foregroundText, new Point(cropRelativePxX, cropRelativePxY));
                }
            }

            var rtb = new RenderTargetBitmap(cropPxW, cropPxH, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(visual);

            string tempFile = Path.Combine(Downloader.App.AppTempDirectory, $"gif_overlay_{Guid.NewGuid():N}.png");
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));
            using (var fs = new FileStream(tempFile, FileMode.Create, FileAccess.Write))
            {
                encoder.Save(fs);
            }

            return tempFile;
        }

        // ==================== Presets & Format Selection ====================

        private bool IsTelegramStickerSelected()
        {
            return GifFormatTelegramRadio != null && GifFormatTelegramRadio.IsChecked == true;
        }

        private bool IsWebpSelected()
        {
            return GifFormatWebpRadio != null && GifFormatWebpRadio.IsChecked == true;
        }

        private void GifFormat_Checked(object sender, RoutedEventArgs e)
        {
            bool isTelegram = IsTelegramStickerSelected();
            bool isWebp = IsWebpSelected();

            if (GifTelegramNoticeCard != null)
            {
                GifTelegramNoticeCard.Visibility = isTelegram ? Visibility.Visible : Visibility.Collapsed;
            }

            if (GifOptionsPanel != null)
            {
                if (GifDitherPanel != null) GifDitherPanel.Visibility = (!isWebp && !isTelegram) ? Visibility.Visible : Visibility.Collapsed;
                if (GifColorsPanel != null) GifColorsPanel.Visibility = (!isWebp && !isTelegram) ? Visibility.Visible : Visibility.Collapsed;
                if (GifWebpQualityPanel != null) GifWebpQualityPanel.Visibility = isWebp ? Visibility.Visible : Visibility.Collapsed;
            }

            if (GifCreateButton != null)
            {
                GifCreateButton.Content = isTelegram ? "✈️ Create Telegram Sticker" :
                                          isWebp ? "🌐 Create WebP Clip" : "🎞️ Create GIF Clip";
            }

            UpdateTelegramDurationWarning();
            UpdateOutputDestinationPreview();
        }

        private void UpdateTelegramDurationWarning()
        {
            if (GifTelegramDurationWarning == null) return;

            if (IsTelegramStickerSelected())
            {
                var dur = _gifEndTime > _gifStartTime ? _gifEndTime - _gifStartTime : TimeSpan.Zero;
                GifTelegramDurationWarning.Visibility = dur > TimeSpan.FromSeconds(3) ? Visibility.Visible : Visibility.Collapsed;
            }
            else
            {
                GifTelegramDurationWarning.Visibility = Visibility.Collapsed;
            }
        }

        private void GifPresetComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (GifPresetComboBox == null) return;

            int idx = GifPresetComboBox.SelectedIndex;

            // Target size panel visibility
            if (GifTargetSizePanel != null)
            {
                GifTargetSizePanel.Visibility = (idx == 3) ? Visibility.Visible : Visibility.Collapsed;
            }

            // Sync preset defaults
            switch (idx)
            {
                case 0: // 🌟 Maximum Quality
                    SetFpsIndex(1); // 30 fps
                    SetResolutionIndex(0); // Original
                    SetDitherIndex(0); // Sierra2_4a
                    SetMaxColorsIndex(0); // 256
                    if (GifWebpQualitySlider != null) GifWebpQualitySlider.Value = 85;
                    break;
                case 1: // ⚖️ Balanced (Discord & Web)
                    SetFpsIndex(2); // 24 fps
                    SetResolutionIndex(2); // 720p
                    SetDitherIndex(1); // Bayer
                    SetMaxColorsIndex(0); // 256
                    if (GifWebpQualitySlider != null) GifWebpQualitySlider.Value = 70;
                    break;
                case 2: // 🗜️ Maximum Compression (Emoji/Sticker)
                    SetFpsIndex(4); // 15 fps
                    SetResolutionIndex(5); // 256px
                    SetDitherIndex(1); // Bayer
                    SetMaxColorsIndex(1); // 128
                    if (GifWebpQualitySlider != null) GifWebpQualitySlider.Value = 40;
                    break;
                case 3: // 🎯 Target Size
                    SetFpsIndex(3); // 20 fps
                    SetResolutionIndex(3); // 480p
                    SetDitherIndex(1); // Bayer
                    SetMaxColorsIndex(1); // 128
                    if (GifWebpQualitySlider != null) GifWebpQualitySlider.Value = 60;
                    break;
            }

            UpdateTrimmerUi();
        }

        private void SetFpsIndex(int index)
        {
            if (GifFpsComboBox != null && GifFpsComboBox.Items.Count > index)
                GifFpsComboBox.SelectedIndex = index;
        }

        private void SetResolutionIndex(int index)
        {
            if (GifResolutionComboBox != null && GifResolutionComboBox.Items.Count > index)
                GifResolutionComboBox.SelectedIndex = index;
        }

        private void SetDitherIndex(int index)
        {
            if (GifDitherComboBox != null && GifDitherComboBox.Items.Count > index)
                GifDitherComboBox.SelectedIndex = index;
        }

        private void SetMaxColorsIndex(int index)
        {
            if (GifColorsComboBox != null && GifColorsComboBox.Items.Count > index)
                GifColorsComboBox.SelectedIndex = index;
        }

        private int GetSelectedFps()
        {
            if (GifFpsComboBox?.SelectedItem is ComboBoxItem item &&
                int.TryParse(item.Tag?.ToString(), out int parsedFps))
            {
                return parsedFps;
            }
            return 24;
        }

        private int GetSelectedWidth()
        {
            if (GifResolutionComboBox?.SelectedItem is ComboBoxItem item &&
                int.TryParse(item.Tag?.ToString(), out int parsedWidth))
            {
                return parsedWidth;
            }
            return 0; // 0 = original
        }

        private double GetSelectedSpeed()
        {
            if (GifSpeedComboBox?.SelectedItem is ComboBoxItem item &&
                double.TryParse(item.Tag?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double parsedSpeed))
            {
                return parsedSpeed;
            }
            return 1.0;
        }

        private int GetSelectedColors()
        {
            if (GifColorsComboBox?.SelectedItem is ComboBoxItem item &&
                int.TryParse(item.Tag?.ToString(), out int parsedColors))
            {
                return parsedColors;
            }
            return 256;
        }

        private GifDitherMode GetSelectedDither()
        {
            int idx = GifDitherComboBox?.SelectedIndex ?? 0;
            return idx switch
            {
                1 => GifDitherMode.Bayer,
                2 => GifDitherMode.FloydSteinberg,
                3 => GifDitherMode.None,
                _ => GifDitherMode.Sierra2_4a
            };
        }

        private void GifToggleSettings_Click(object sender, RoutedEventArgs e)
        {
            _isGifSettingsExpanded = !_isGifSettingsExpanded;
            if (GifDetailedSettingsPanel != null)
            {
                GifDetailedSettingsPanel.Visibility = _isGifSettingsExpanded ? Visibility.Visible : Visibility.Collapsed;
            }
            if (GifDropdownArrow != null)
            {
                GifDropdownArrow.Text = _isGifSettingsExpanded ? "▲" : "▼";
            }
        }

        private void GifWebpQualitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (GifWebpQualityValueText != null)
            {
                GifWebpQualityValueText.Text = $"Quality: {(int)e.NewValue}%";
            }
        }

        // ==================== Output Path Handling ====================

        private void GifBrowseDestination_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Select destination folder for created clips"
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                if (GifDestinationTextBox != null)
                {
                    GifDestinationTextBox.Text = dialog.SelectedPath;
                }
                UpdateOutputDestinationPreview();
            }
        }

        private void GifResetDestination_Click(object sender, RoutedEventArgs e)
        {
            if (GifDestinationTextBox != null)
            {
                string defaultFolder = !string.IsNullOrEmpty(_currentGifSourceVideo)
                    ? Path.Combine(Path.GetDirectoryName(_currentGifSourceVideo) ?? SelectedDirectory ?? "", "Clips")
                    : (SelectedDirectory ?? "");
                GifDestinationTextBox.Text = defaultFolder;
            }
            UpdateOutputDestinationPreview();
        }

        private void GifDestinationTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateOutputDestinationPreview();
        }

        // ==================== Clip Generation ====================

        private async void GifCreateButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_currentGifSourceVideo) || !File.Exists(_currentGifSourceVideo))
            {
                ModernMessageBox.Show("Please select a valid source video.", "Source Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_gifEndTime <= _gifStartTime)
            {
                ModernMessageBox.Show("End time must be greater than start time.", "Invalid Clip Range", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_isGifGenerating) return;

            StopGifPlayer();

            bool isTelegram = IsTelegramStickerSelected();
            bool isWebp = !isTelegram && IsWebpSelected();
            var format = isTelegram ? GifWebpFormat.TelegramSticker : (isWebp ? GifWebpFormat.Webp : GifWebpFormat.Gif);

            int presetIdx = GifPresetComboBox?.SelectedIndex ?? 0;
            var preset = presetIdx switch
            {
                1 => GifWebpPreset.Balanced,
                2 => GifWebpPreset.MaxCompression,
                3 => GifWebpPreset.TargetSize,
                4 => GifWebpPreset.Custom,
                _ => GifWebpPreset.MaxQuality
            };

            var endTime = _gifEndTime;
            if (isTelegram && (endTime - _gifStartTime > TimeSpan.FromSeconds(3.0)))
            {
                endTime = _gifStartTime + TimeSpan.FromSeconds(3.0);
            }

            var options = new GifWebpOptions
            {
                Format = format,
                Preset = preset,
                StartTime = _gifStartTime,
                EndTime = endTime,
                Fps = isTelegram ? Math.Min(30, GetSelectedFps()) : GetSelectedFps(),
                Width = GetSelectedWidth(),
                SpeedMultiplier = GetSelectedSpeed(),
                MaxColors = GetSelectedColors(),
                Dither = GetSelectedDither(),
                LoopCount = (GifLoopComboBox?.SelectedIndex == 1) ? -1 : 0,
                WebpQuality = (int)(GifWebpQualitySlider?.Value ?? 80),
                WebpLossless = GifWebpLosslessCheckBox?.IsChecked == true,
                Crop = _cropRect,
                OriginalVideoWidth = _sourceVideoWidth,
                OriginalVideoHeight = _sourceVideoHeight
            };

            if (preset == GifWebpPreset.TargetSize &&
                double.TryParse(GifTargetSizeTextBox?.Text?.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out double parsedMb) &&
                parsedMb > 0)
            {
                options.TargetSizeMb = parsedMb;
            }

            string ext = isTelegram ? ".webm" : (isWebp ? ".webp" : ".gif");
            string targetFolder = GifDestinationTextBox?.Text?.Trim() ?? "";
            if (string.IsNullOrEmpty(targetFolder))
            {
                string sourceDir = Path.GetDirectoryName(_currentGifSourceVideo) ?? SelectedDirectory ?? "";
                targetFolder = Path.Combine(sourceDir, "Clips");
            }

            if (!Directory.Exists(targetFolder))
            {
                try { Directory.CreateDirectory(targetFolder); } catch { }
            }

            string baseName = Path.GetFileNameWithoutExtension(_currentGifSourceVideo);
            string startStr = _gifStartTime.ToString(@"mm\-ss", CultureInfo.InvariantCulture);
            string endStr = _gifEndTime.ToString(@"mm\-ss", CultureInfo.InvariantCulture);
            string finalOutputPath = Path.Combine(targetFolder, $"{baseName}_{startStr}_{endStr}{ext}");

            // Handle filename collision
            int counter = 1;
            while (File.Exists(finalOutputPath))
            {
                finalOutputPath = Path.Combine(targetFolder, $"{baseName}_{startStr}_{endStr}_{counter}{ext}");
                counter++;
            }

            _isGifGenerating = true;
            _gifCts = new CancellationTokenSource();

            // UI updates for generating state
            if (GifCreateButton != null) GifCreateButton.IsEnabled = false;
            if (GifProgressPanel != null) GifProgressPanel.Visibility = Visibility.Visible;
            if (GifCompletionCard != null) GifCompletionCard.Visibility = Visibility.Collapsed;
            if (GifProgressBar != null) GifProgressBar.Value = 0;
            if (GifProgressText != null) GifProgressText.Text = "Initializing FFmpeg encoder...";

            var progressReporter = new Progress<GifWebpProgress>(p =>
            {
                if (GifProgressBar != null) GifProgressBar.Value = p.Percentage;
                if (GifProgressText != null)
                {
                    string etaStr = p.EstimatedRemainingTime.HasValue ? $" • ETA: {p.EstimatedRemainingTime.Value:mm\\:ss}" : "";
                    GifProgressText.Text = $"{p.StatusMessage} {p.Percentage:F0}%{etaStr}";
                }
            });

            try
            {
                if (_gifWebpService != null)
                {
                    if (_textLabelControls.Count > 0)
                    {
                        options.OverlayImagePath = GenerateTextOverlayPng(options);
                    }

                    var result = await _gifWebpService.CreateClipAsync(
                        _currentGifSourceVideo,
                        finalOutputPath,
                        options,
                        progressReporter,
                        _gifCts.Token);

                    if (result.IsCancelled || _gifCts?.IsCancellationRequested == true)
                    {
                        // User cancelled clip creation; cleanly exit with no error dialog
                        return;
                    }

                    if (result.Success && File.Exists(finalOutputPath))
                    {
                        // Show completion card
                        if (GifCompletionCard != null) GifCompletionCard.Visibility = Visibility.Visible;
                        if (GifCompletedFileNameText != null) GifCompletedFileNameText.Text = Path.GetFileName(finalOutputPath);
                        if (GifCompletedSizeText != null) GifCompletedSizeText.Text = $"File size: {result.FormattedSize} • Time spent: {result.Duration.TotalSeconds:F1}s";

                        // Store path in button tags for opening
                        if (GifOpenCompletedFileButton != null) GifOpenCompletedFileButton.Tag = finalOutputPath;
                        if (GifOpenCompletedFolderButton != null) GifOpenCompletedFolderButton.Tag = finalOutputPath;

                        // Add to download history for easy retrieval
                        try
                        {
                            var historyItem = new DownloadHistoryItem
                            {
                                Title = Path.GetFileName(finalOutputPath),
                                Url = _currentGifSourceVideo,
                                Platform = isTelegram ? "Telegram Sticker" : (isWebp ? "WebP Clip" : "GIF Clip"),
                                FilePath = finalOutputPath,
                                FileSizeBytes = result.OutputSizeBytes,
                                FormattedSize = result.FormattedSize,
                                DownloadDate = DateTime.Now,
                                IsAudio = false,
                                FormatExtension = ext.TrimStart('.')
                            };
                            await _historyService.AddItemAsync(historyItem);
                        }
                        catch { }
                    }
                    else if (!string.IsNullOrEmpty(result.ErrorMessage))
                    {
                        ModernMessageBox.Show(result.ErrorMessage, "Creation Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Silently exit on cancellation
            }
            catch (Exception ex)
            {
                if (_gifCts?.IsCancellationRequested == true) return;
                ModernMessageBox.Show($"Encoding error: {ex.Message}", "Encoding Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _isGifGenerating = false;
                if (GifCreateButton != null) GifCreateButton.IsEnabled = true;
                if (GifProgressPanel != null) GifProgressPanel.Visibility = Visibility.Collapsed;
            }
        }

        private void GifCancelButton_Click(object sender, RoutedEventArgs e)
        {
            _gifCts?.Cancel();
            _gifWebpService?.CancelCurrentProcess();
        }

        private void GifOpenCompletedFile_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string filePath && File.Exists(filePath))
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = filePath,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to open clip file: {ex.Message}");
                }
            }
        }

        private void GifOpenCompletedFolder_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string filePath && File.Exists(filePath))
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"/select,\"{filePath}\"",
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to open clip folder: {ex.Message}");
                }
            }
        }

        private void GifChangeVideo_Click(object sender, RoutedEventArgs e)
        {
            StopGifPlayer();
            if (GifEditorContainer != null) GifEditorContainer.Visibility = Visibility.Collapsed;
            if (GifDropZone != null) GifDropZone.Visibility = Visibility.Visible;
            if (GifCompletionCard != null) GifCompletionCard.Visibility = Visibility.Collapsed;
            _currentGifSourceVideo = null;
        }

        // ==================== Drag & Drop Handling ====================

        private void GifDropZone_PreviewDragEnter(object sender, DragEventArgs e)
        {
            if (GifDropZone != null)
            {
                GifDropZone.BorderBrush = new SolidColorBrush(Color.FromRgb(0xF4, 0x3F, 0x5E)); // Rose accent
                GifDropZone.Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x14, 0x18));
            }
            GifDropZone_PreviewDragOver(sender, e);
        }

        private void GifDropZone_PreviewDragLeave(object sender, DragEventArgs e)
        {
            if (GifDropZone != null)
            {
                GifDropZone.BorderBrush = new SolidColorBrush(Color.FromRgb(0x3F, 0x3F, 0x46));
                GifDropZone.Background = new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x18));
            }
        }

        private void GifDropZone_PreviewDragOver(object sender, DragEventArgs e)
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

        private void GifDropZone_PreviewDrop(object sender, DragEventArgs e)
        {
            GifDropZone_PreviewDragLeave(sender, e);
            var paths = ExtractDropPaths(e);
            if (paths.Length > 0)
            {
                string firstVideo = paths.FirstOrDefault(p =>
                {
                    string ext = Path.GetExtension(p).ToLowerInvariant();
                    return ext is ".mp4" or ".mkv" or ".mov" or ".webm" or ".avi" or ".flv" or ".wmv" or ".ts" or ".m4v";
                }) ?? paths[0];

                if (File.Exists(firstVideo))
                {
                    LoadVideoIntoGifCreator(firstVideo);
                }
            }
            e.Handled = true;
        }

        private void GifDropZone_Drop(object sender, DragEventArgs e)
        {
            GifDropZone_PreviewDrop(sender, e);
        }

        private void GifDropZone_DragOver(object sender, DragEventArgs e)
        {
            GifDropZone_PreviewDragOver(sender, e);
        }
    }
}
