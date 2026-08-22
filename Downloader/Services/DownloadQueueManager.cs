using System;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
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
        private readonly SemaphoreSlim _semaphore = new(2, 2); // Max 2 concurrent downloads
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
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                _items.Add(item);
                QueueChanged?.Invoke();
            });

            _ = ProcessQueueAsync();
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
                item.Status = QueueItemStatus.Canceled;
                item.StatusText = "Canceled";
                QueueChanged?.Invoke();
            }
        }

        public void CancelAll()
        {
            foreach (var cts in _cancellationTokens.Values)
            {
                try { cts.Cancel(); } catch { }
            }

            foreach (var item in _items.Where(x => x.Status == QueueItemStatus.Queued))
            {
                item.Status = QueueItemStatus.Canceled;
                item.StatusText = "Canceled";
            }
            QueueChanged?.Invoke();
        }

        public void Retry(string itemId)
        {
            var item = _items.FirstOrDefault(x => x.Id == itemId);
            if (item != null && (item.Status == QueueItemStatus.Failed || item.Status == QueueItemStatus.Canceled))
            {
                item.Status = QueueItemStatus.Queued;
                item.StatusText = "Queued";
                item.Progress = 0;
                item.ErrorMessage = null;
                QueueChanged?.Invoke();
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
                    _ = Task.Run(async () =>
                    {
                        var cts = new CancellationTokenSource();
                        _cancellationTokens[nextItem.Id] = cts;

                        try
                        {
                            nextItem.Status = QueueItemStatus.Downloading;
                            nextItem.StatusText = "Downloading...";
                            QueueChanged?.Invoke();

                            string tempDir = Path.Combine(Path.GetTempPath(), "UD_Queue_" + Guid.NewGuid().ToString("N"));
                            Directory.CreateDirectory(tempDir);

                            void OnProgress(object? sender, DownloadProgressArgs args)
                            {
                                nextItem.Progress = args.Percentage;
                                if (!string.IsNullOrWhiteSpace(args.StatusMessage))
                                {
                                    nextItem.StatusText = args.StatusMessage;
                                }
                            }

                            void OnFile(string path, string url)
                            {
                                if (url == nextItem.Url || string.IsNullOrWhiteSpace(nextItem.DownloadedFilePath))
                                {
                                    nextItem.DownloadedFilePath = path;
                                    if (nextItem.Title.StartsWith("Batch Item #") && File.Exists(path))
                                    {
                                        nextItem.Title = Path.GetFileNameWithoutExtension(path);
                                    }
                                }
                            }

                            _downloadService.ProgressChanged += OnProgress;
                            _downloadService.FileDownloaded += OnFile;

                            try
                            {
                                bool success = await _downloadService.DownloadWithYtDlpAsync(
                                    nextItem.Url,
                                    nextItem.FormatCode,
                                    tempDir,
                                    nextItem.DestinationFolder,
                                    nextItem.IsAudioOnly,
                                    nextItem.AudioFormat,
                                    false, 0, 0,
                                    cts.Token);

                                if (success)
                                {
                                    nextItem.Status = QueueItemStatus.Completed;
                                    nextItem.StatusText = "Completed";
                                    nextItem.Progress = 100;
                                    ItemCompleted?.Invoke(nextItem);
                                }
                                else
                                {
                                    nextItem.Status = QueueItemStatus.Failed;
                                    nextItem.StatusText = "Download failed";
                                    ItemFailed?.Invoke(nextItem);
                                }
                            }
                            finally
                            {
                                _downloadService.ProgressChanged -= OnProgress;
                                _downloadService.FileDownloaded -= OnFile;
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            nextItem.Status = QueueItemStatus.Canceled;
                            nextItem.StatusText = "Canceled";
                        }
                        catch (Exception ex)
                        {
                            nextItem.Status = QueueItemStatus.Failed;
                            nextItem.StatusText = "Error";
                            nextItem.ErrorMessage = ex.Message;
                            ItemFailed?.Invoke(nextItem);
                        }
                        finally
                        {
                            _cancellationTokens.TryRemove(nextItem.Id, out _);
                            _semaphore.Release();
                            QueueChanged?.Invoke();
                        }
                    });
                }
            }
            finally
            {
                _isProcessing = false;
            }
        }
    }
}
