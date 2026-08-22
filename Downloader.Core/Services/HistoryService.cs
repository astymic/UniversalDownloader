using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UniversalDownloader.Models;

namespace UniversalDownloader.Services
{
    public class HistoryService
    {
        private readonly string _historyFilePath;
        private readonly ObservableCollection<DownloadHistoryItem> _items = new();
        private readonly object _lock = new();

        public ObservableCollection<DownloadHistoryItem> Items => _items;

        public event Action? HistoryChanged;

        public HistoryService()
        {
            string appDataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "UniversalDownloader");
            
            Directory.CreateDirectory(appDataDir);
            _historyFilePath = Path.Combine(appDataDir, "download_history.json");

            LoadHistory();
        }

        public void LoadHistory()
        {
            lock (_lock)
            {
                try
                {
                    _items.Clear();
                    if (File.Exists(_historyFilePath))
                    {
                        string json = File.ReadAllText(_historyFilePath);
                        var loaded = JsonConvert.DeserializeObject<List<DownloadHistoryItem>>(json);
                        if (loaded != null)
                        {
                            foreach (var item in loaded.OrderByDescending(x => x.DownloadDate))
                            {
                                _items.Add(item);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to load download history: {ex.Message}");
                }
            }
        }

        public async Task AddItemAsync(DownloadHistoryItem item)
        {
            if (item == null) return;

            lock (_lock)
            {
                // Insert at top of list
                _items.Insert(0, item);
                // Keep max 200 items in history
                while (_items.Count > 200)
                {
                    _items.RemoveAt(_items.Count - 1);
                }
            }

            await SaveHistoryAsync();
            HistoryChanged?.Invoke();
        }

        public async Task RemoveItemAsync(string id)
        {
            lock (_lock)
            {
                var existing = _items.FirstOrDefault(x => x.Id == id);
                if (existing != null)
                {
                    _items.Remove(existing);
                }
            }

            await SaveHistoryAsync();
            HistoryChanged?.Invoke();
        }

        public async Task ClearHistoryAsync()
        {
            lock (_lock)
            {
                _items.Clear();
            }

            await SaveHistoryAsync();
            HistoryChanged?.Invoke();
        }

        private async Task SaveHistoryAsync()
        {
            try
            {
                List<DownloadHistoryItem> snapshot;
                lock (_lock)
                {
                    snapshot = _items.ToList();
                }

                string json = JsonConvert.SerializeObject(snapshot, Formatting.Indented);
                await File.WriteAllTextAsync(_historyFilePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save download history: {ex.Message}");
            }
        }
    }
}
