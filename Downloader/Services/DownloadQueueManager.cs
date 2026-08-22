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
            }
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

                            string tempDir = Path.Combine(Path.GetTempPath(), "UD_Queue_" + Guid.NewGuid().ToString("N"));
                            Directory.CreateDirectory(tempDir);

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
