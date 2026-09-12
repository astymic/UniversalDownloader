using System;
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

        private VideoCropRect _cropRect = new VideoCropRect();
        private int _sourceVideoWidth = 0;
        private int _sourceVideoHeight = 0;

        private void InitializeGifWebp()
        {
            _gifWebpService = new GifWebpService(_dependencyManager);

            _gifPlayerTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(50)
            };
            _gifPlayerTimer.Tick += GifPlayerTimer_Tick;
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
            }
        }

        private void BackFromGifCreator_Click(object sender, RoutedEventArgs e)
        {
            StopGifPlayer();
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

            _currentGifSourceVideo = filePath;
            var fi = new FileInfo(filePath);

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

            if (_gifPlayerTimer != null && _gifPlayerTimer.IsEnabled)
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

            GifMediaPlayer.Pause();
            _gifPlayerTimer?.Stop();
            _isLoopPreviewActive = false;

            if (GifPlayPauseIcon != null) GifPlayPauseIcon.Text = "▶";
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
                GifPlayerScrubber.Value = pos.TotalSeconds;
            }

            if (GifPlayerTimeText != null)
            {
                GifPlayerTimeText.Text = $"{FormatTimeSpan(pos)} / {FormatTimeSpan(_gifTotalDuration)}";
            }
        }

        private void GifPlayerScrubber_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            _isGifPlayerSeeking = true;
        }

        private void GifPlayerScrubber_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            _isGifPlayerSeeking = false;
            if (GifMediaPlayer != null && GifPlayerScrubber != null)
            {
                GifMediaPlayer.Position = TimeSpan.FromSeconds(GifPlayerScrubber.Value);
            }
        }

        private void GifPlayerScrubber_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isGifPlayerSeeking && GifMediaPlayer != null)
            {
                GifMediaPlayer.Position = TimeSpan.FromSeconds(e.NewValue);
                if (GifPlayerTimeText != null)
                {
                    GifPlayerTimeText.Text = $"{FormatTimeSpan(TimeSpan.FromSeconds(e.NewValue))} / {FormatTimeSpan(_gifTotalDuration)}";
                }
            }
        }

        private void GifSetCurrentAsStart_Click(object sender, RoutedEventArgs e)
        {
            if (GifMediaPlayer == null) return;
            var pos = GifMediaPlayer.Position;
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
            if (GifMediaPlayer == null) return;
            var pos = GifMediaPlayer.Position;
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
