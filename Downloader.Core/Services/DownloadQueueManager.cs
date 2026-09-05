using System;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UniversalDownloader.Models;

namespace UniversalDownloader.Services
{
    public class DownloadQueueManager
    {
        public static Action<Action>? DispatcherInvoker { get; set; }

        private static void Dispatch(Action action)
        {
            if (DispatcherInvoker != null)
            {
                DispatcherInvoker(action);
            }
            else if (SynchronizationContext.Current != null)
            {
                SynchronizationContext.Current.Post(_ => action(), null);
            }
            else
            {
                action();
            }
        }

        private readonly DownloadService _downloadService;
        private readonly HistoryService _historyService;
        private readonly ObservableCollection<DownloadQueueItem> _items = new();
        private readonly ConcurrentDictionary<string, CancellationTokenSource> _cancellationTokens = new();
        private readonly SemaphoreSlim _semaphore = new(1, 1);
        private bool _isProcessing;

        public ObservableCollection<DownloadQueueItem> Items => _items;

        public event Action<DownloadQueueItem>? ItemCompleted;
        public event Action<DownloadQueueItem>? ItemFailed;
        public event Action? QueueChanged;

        public DownloadQueueManager(DownloadService downloadService, HistoryService historyService)
        {
            _downloadService = downloadService;
            _historyService = historyService;
        }

        public void Enqueue(DownloadQueueItem item)
        {
            Dispatch(() =>
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
            Dispatch(() =>
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
                        Dispatch(() =>
                        {
                            item.Title = title.Trim();
                        });
                    }
                    else if (item.Title.Contains("Resolving title"))
                    {
                        Dispatch(() =>
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
                Dispatch(() =>
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

            Dispatch(() =>
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
                Dispatch(() =>
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
            Dispatch(() =>
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
            Dispatch(() =>
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
                            Dispatch(() =>
                            {
                                nextItem.Status = QueueItemStatus.Downloading;
                                nextItem.StatusText = "Downloading...";
                                QueueChanged?.Invoke();
                            });

                            string tempDir = Path.Combine(Path.GetTempPath(), "UD_Queue_" + Guid.NewGuid().ToString("N"));
                            Directory.CreateDirectory(tempDir);

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
                                        Dispatch(() =>
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
                                    Dispatch(() =>
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

                                if (args.Percentage > 0)
                                {
                                    nextItem.Progress = args.Percentage;
                                }
                                if (!string.IsNullOrWhiteSpace(args.StatusMessage))
                                {
                                    nextItem.StatusText = args.StatusMessage;
                                }
                            });

                            bool success = await _downloadService.DownloadWithYtDlpAsync(
                                downloadUrl,
                                nextItem.IsAudioOnly ? "bestaudio/best" : nextItem.FormatCode,
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
                                Dispatch(() =>
                                {
                                    nextItem.Status = QueueItemStatus.Completed;
                                    nextItem.StatusText = "Completed";
                                    nextItem.Progress = 100;

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

                                ItemCompleted?.Invoke(nextItem);
                            }
                            else
                            {
                                Dispatch(() =>
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
                            Dispatch(() =>
                            {
                                nextItem.Status = QueueItemStatus.Canceled;
                                nextItem.StatusText = "Canceled";
                                QueueChanged?.Invoke();
                            });
                        }
                        catch (Exception ex)
                        {
                            Dispatch(() =>
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
            }
        }
    }
}
