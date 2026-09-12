using System;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using UniversalDownloader.Models;

namespace UniversalDownloader.Services
{
    public class DownloadQueueManager
    {
        private readonly DownloadService _downloadService;
        private readonly HistoryService _historyService;
        private readonly ObservableCollection<DownloadQueueItem> _items = new();
        private readonly ConcurrentDictionary<string, CancellationTokenSource> _cancellationTokens = new();
        private readonly SemaphoreSlim _semaphore = new(1, 1); // 1 download at a time for stability and UI isolation
        private bool _isProcessing;

        public ObservableCollection<DownloadQueueItem> Items => _items;

        public event Action<DownloadQueueItem>? ItemCompleted;
        public event Action<DownloadQueueItem>? ItemFailed;
        public event Action? QueueChanged;
        public event Action? QueueCompleted;

        public DownloadQueueManager(DownloadService downloadService, HistoryService historyService)
        {
            _downloadService = downloadService;
            _historyService = historyService;
        }

        public void Enqueue(DownloadQueueItem item)
        {
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                item.UpdatePlatformFromUrl();
                _items.Add(item);
                QueueChanged?.Invoke();
            });

            ResolveItemTitleAsync(item);
            _ = ProcessQueueAsync();
        }

        public void EnqueueRange(System.Collections.Generic.IEnumerable<DownloadQueueItem> items)
        {
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                foreach (var item in items)
                {
                    item.UpdatePlatformFromUrl();
                    _items.Add(item);
                    ResolveItemTitleAsync(item);
                }
                QueueChanged?.Invoke();
            });

            _ = ProcessQueueAsync();
        }

        private void ResolveItemTitleAsync(DownloadQueueItem item)
        {
            // Only resolve title if it's currently a placeholder
            if (!string.IsNullOrWhiteSpace(item.Title) &&
                !item.Title.Contains("Resolving title") &&
                !item.Title.StartsWith("YouTube Video #") &&
                !item.Title.StartsWith("Media Item #") &&
                !item.Title.StartsWith("http://") &&
                !item.Title.StartsWith("https://"))
            {
                return;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    string? title = await _downloadService.GetTitleWithYtDlpAsync(item.Url);
                    if (!string.IsNullOrWhiteSpace(title) && title.Trim().Length > 1)
                    {
                        Application.Current?.Dispatcher?.Invoke(() =>
                        {
                            item.Title = title.Trim();
                        });
                    }
                    else if (item.Title.Contains("Resolving title"))
                    {
                        Application.Current?.Dispatcher?.Invoke(() =>
                        {
                            item.Title = item.Platform == "Spotify" ? "Spotify Track" : "Media Item";
                        });
                    }
                }
                catch { }
            });
        }

        public void Cancel(string itemId)
        {
            if (_cancellationTokens.TryGetValue(itemId, out var cts))
            {
                cts.Cancel();
            }

            var item = _items.FirstOrDefault(x => x.Id == itemId);
            if (item != null && item.Status == QueueItemStatus.Queued)
            {
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    item.Status = QueueItemStatus.Canceled;
                    item.StatusText = "Canceled";
                    QueueChanged?.Invoke();
                });
            }
        }

        public void CancelAll()
        {
            foreach (var cts in _cancellationTokens.Values)
            {
                try { cts.Cancel(); } catch { }
            }

            Application.Current?.Dispatcher?.Invoke(() =>
            {
                foreach (var item in _items.Where(x => x.Status == QueueItemStatus.Queued))
                {
                    item.Status = QueueItemStatus.Canceled;
                    item.StatusText = "Canceled";
                }
                QueueChanged?.Invoke();
            });
        }

        public void Retry(string itemId)
        {
            var item = _items.FirstOrDefault(x => x.Id == itemId);
            if (item != null && (item.Status == QueueItemStatus.Failed || item.Status == QueueItemStatus.Canceled))
            {
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    item.Status = QueueItemStatus.Queued;
                    item.StatusText = "Queued";
                    item.Progress = 0;
                    item.ErrorMessage = null;
                    QueueChanged?.Invoke();
                });
                ResolveItemTitleAsync(item);
                _ = ProcessQueueAsync();
            }
        }

        public void Remove(string itemId)
        {
            Cancel(itemId);
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                var item = _items.FirstOrDefault(x => x.Id == itemId);
                if (item != null)
                {
                    _items.Remove(item);
                    QueueChanged?.Invoke();
                }
            });
        }

        public void ClearCompleted()
        {
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                var finished = _items.Where(x => x.Status == QueueItemStatus.Completed || x.Status == QueueItemStatus.Canceled || x.Status == QueueItemStatus.Failed).ToList();
                foreach (var item in finished)
                {
                    _items.Remove(item);
                }
                QueueChanged?.Invoke();
            });
        }

        private async Task ProcessQueueAsync()
        {
            if (_isProcessing) return;
            _isProcessing = true;

            try
            {
                while (true)
                {
                    DownloadQueueItem? nextItem = null;
                    lock (_items)
                    {
                        nextItem = _items.FirstOrDefault(x => x.Status == QueueItemStatus.Queued);
                    }

                    if (nextItem == null) break;

                    await _semaphore.WaitAsync();
                    try
                    {
                        var cts = new CancellationTokenSource();
                        _cancellationTokens[nextItem.Id] = cts;

                        try
                        {
                            Application.Current?.Dispatcher?.Invoke(() =>
                            {
                                nextItem.Status = QueueItemStatus.Downloading;
                                nextItem.StatusText = "Downloading...";
                                QueueChanged?.Invoke();
                            });

                            string tempDir = Path.Combine(Path.GetTempPath(), "UD_Queue_" + Guid.NewGuid().ToString("N"));
                            Directory.CreateDirectory(tempDir);

                            // Capture existing files in destination directory before downloading
                            var existingFiles = Directory.Exists(nextItem.DestinationFolder)
                                ? new System.Collections.Generic.HashSet<string>(Directory.GetFiles(nextItem.DestinationFolder))
                                : new System.Collections.Generic.HashSet<string>();

                            bool isSpotify = _downloadService.IsSpotifyLink(nextItem.Url);
                            string downloadUrl = nextItem.Url;
                            string? overrideTitle = null;

                            if (isSpotify)
                            {
                                if (nextItem.Title.Contains("Resolving title") || nextItem.Title.StartsWith("Spotify Track #") || nextItem.Title == "Spotify Track")
                                {
                                    var meta = await _downloadService.GetSpotifyMetadataAsync(nextItem.Url);
                                    if (meta != null && !string.IsNullOrWhiteSpace(meta.Title))
                                    {
                                        Application.Current?.Dispatcher?.Invoke(() =>
                                        {
                                            nextItem.Title = $"{meta.Artist} - {meta.Title}".Trim();
                                        });
                                    }
                                }

                                string cleanTitle = nextItem.Title.Replace("\"", "").Replace("'", "");
                                downloadUrl = $"ytsearch1:{cleanTitle}";
                                overrideTitle = nextItem.Title;
                                nextItem.IsAudioOnly = true;
                                nextItem.AudioFormat = "mp3";
                            }
                            else if (nextItem.Title.Contains("Resolving title") || nextItem.Title.StartsWith("YouTube Video #") || nextItem.Title.StartsWith("Media Item #"))
                            {
                                string? resolved = await _downloadService.GetTitleWithYtDlpAsync(nextItem.Url);
                                if (!string.IsNullOrWhiteSpace(resolved) && resolved.Trim().Length > 1)
                                {
                                    Application.Current?.Dispatcher?.Invoke(() =>
                                    {
                                        nextItem.Title = resolved.Trim();
                                    });
                                }
                            }

                            if (!isSpotify && !string.IsNullOrWhiteSpace(nextItem.Title) && !nextItem.Title.Contains("Resolving title"))
                            {
                                overrideTitle = nextItem.Title;
                            }

                            var itemProgress = new Progress<DownloadProgressArgs>(args =>
                            {
                                if (nextItem.Status != QueueItemStatus.Downloading) return;

                                if (args.Percentage >= 0)
                                {
                                    nextItem.Progress = args.Percentage;
                                }
                                if (!string.IsNullOrWhiteSpace(args.StatusMessage))
                                {
                                    nextItem.StatusText = args.StatusMessage;
                                }
                            });

                            string effectiveFormat = nextItem.IsAudioOnly ? "bestaudio/best" : nextItem.FormatCode;
                            if (!nextItem.IsAudioOnly && (string.IsNullOrWhiteSpace(effectiveFormat) || effectiveFormat == "best" || effectiveFormat == "bestvideo+bestaudio/best"))
                            {
                                if (nextItem.Title.Contains("[Eng", StringComparison.OrdinalIgnoreCase) ||
                                    nextItem.Title.Contains("[English", StringComparison.OrdinalIgnoreCase) ||
                                    nextItem.Title.Contains("[Original", StringComparison.OrdinalIgnoreCase) ||
                                    nextItem.Title.Contains("[Англ", StringComparison.OrdinalIgnoreCase))
                                {
                                    effectiveFormat = "bestvideo+bestaudio[language^=en]/bestvideo+bestaudio[format_id*=eng]/bestvideo+bestaudio/best";
                                }
                                else if (nextItem.Title.Contains("[Рус", StringComparison.OrdinalIgnoreCase) ||
                                         nextItem.Title.Contains("[Дубл", StringComparison.OrdinalIgnoreCase) ||
                                         nextItem.Title.Contains("[Rus", StringComparison.OrdinalIgnoreCase))
                                {
                                    effectiveFormat = "bestvideo+bestaudio[language^=ru]/bestvideo+bestaudio[format_id*=rus]/bestvideo+bestaudio/best";
                                }
                            }

                            bool success = await _downloadService.DownloadWithYtDlpAsync(
                                downloadUrl,
                                effectiveFormat,
                                tempDir,
                                nextItem.DestinationFolder,
                                nextItem.IsAudioOnly,
                                nextItem.AudioFormat,
                                false, 0, 0,
                                cts.Token,
                                overrideTitle,
                                itemProgress);

                            if (success)
                            {
                                Application.Current?.Dispatcher?.Invoke(() =>
                                {
                                    nextItem.Status = QueueItemStatus.Completed;
                                    nextItem.StatusText = "Completed";
                                    nextItem.Progress = 100;

                                    // Detect the newly created file in destination folder
                                    if (Directory.Exists(nextItem.DestinationFolder))
                                    {
                                        var currentFiles = Directory.GetFiles(nextItem.DestinationFolder);
                                        var newFile = currentFiles.FirstOrDefault(f => !existingFiles.Contains(f));
                                        if (newFile != null)
                                        {
                                            nextItem.DownloadedFilePath = newFile;
                                            if ((nextItem.Title.Contains("Resolving title") || nextItem.Title.StartsWith("Media Item") || nextItem.Title.Length <= 1) && File.Exists(newFile))
                                            {
                                                string fileName = Path.GetFileNameWithoutExtension(newFile);
                                                if (fileName.Length > 1)
                                                {
                                                    nextItem.Title = fileName;
                                                }
                                            }
                                        }
                                    }

                                    QueueChanged?.Invoke();
                                });

                                // Download subtitles if requested
                                if (nextItem.DownloadSubtitles && nextItem.SubtitleTracks != null && nextItem.SubtitleTracks.Count > 0)
                                {
                                    try
                                    {
                                        string baseFileName = !string.IsNullOrWhiteSpace(nextItem.DownloadedFilePath) && File.Exists(nextItem.DownloadedFilePath)
                                            ? Path.GetFileNameWithoutExtension(nextItem.DownloadedFilePath)
                                            : SanitizeFileName(nextItem.Title);

                                        using var subClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
                                        subClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

                                        foreach (var track in nextItem.SubtitleTracks)
                                        {
                                            if (string.IsNullOrWhiteSpace(track.Url)) continue;
                                            try
                                            {
                                                string ext = Path.GetExtension(track.Url).Split('?')[0];
                                                if (string.IsNullOrWhiteSpace(ext)) ext = ".vtt";
                                                string langSuffix = !string.IsNullOrWhiteSpace(track.Language) ? $".{SanitizeFileName(track.Language)}" : "";
                                                string subFileName = $"{baseFileName}{langSuffix}{ext}";
                                                string subPath = Path.Combine(nextItem.DestinationFolder, subFileName);

                                                var subBytes = await subClient.GetByteArrayAsync(track.Url, cts.Token);
                                                await File.WriteAllBytesAsync(subPath, subBytes, cts.Token);
                                            }
                                            catch (Exception subEx)
                                            {
                                                Debug.WriteLine($"[DownloadQueueManager] Subtitle download error for {track.Language}: {subEx.Message}");
                                            }
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        Debug.WriteLine($"[DownloadQueueManager] Subtitles overall download error: {ex.Message}");
                                    }
                                }

                                ItemCompleted?.Invoke(nextItem);
                            }
                            else
                            {
                                Application.Current?.Dispatcher?.Invoke(() =>
                                {
                                    nextItem.Status = QueueItemStatus.Failed;
                                    nextItem.StatusText = "Download failed";
                                    QueueChanged?.Invoke();
                                });
                                ItemFailed?.Invoke(nextItem);
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            Application.Current?.Dispatcher?.Invoke(() =>
                            {
                                nextItem.Status = QueueItemStatus.Canceled;
                                nextItem.StatusText = "Canceled";
                                QueueChanged?.Invoke();
                            });
                        }
                        catch (Exception ex)
                        {
                            Application.Current?.Dispatcher?.Invoke(() =>
                            {
                                nextItem.Status = QueueItemStatus.Failed;
                                nextItem.StatusText = "Error";
                                nextItem.ErrorMessage = ex.Message;
                                QueueChanged?.Invoke();
                            });
                            ItemFailed?.Invoke(nextItem);
                        }
                        finally
                        {
                            _cancellationTokens.TryRemove(nextItem.Id, out _);
                        }
                    }
                    finally
                    {
                        _semaphore.Release();
                    }
                }
            }
            finally
            {
                _isProcessing = false;
                bool hasUnfinished = _items.Any(i => i.Status == QueueItemStatus.Queued || i.Status == QueueItemStatus.Downloading);
                if (!hasUnfinished && _items.Count > 0)
                {
                    QueueCompleted?.Invoke();
                }
            }
        }

        private static string SanitizeFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "subtitle";
            char[] invalid = Path.GetInvalidFileNameChars();
            return string.Concat(name.Select(c => invalid.Contains(c) ? '_' : c)).Trim();
        }
    }
}

