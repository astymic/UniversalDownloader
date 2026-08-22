using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UniversalDownloader.Models;

namespace UniversalDownloader.Services
{
    public class LiveStreamRecognitionService : IDisposable
    {
        private readonly IAudioCaptureService _audioCaptureService;
        private readonly ShazamRecognitionService _shazamService;
        private readonly string _sessionsFilePath;
        private readonly ObservableCollection<LiveStreamSession> _sessionHistory = new();
        private readonly object _lock = new();

        private CancellationTokenSource? _listeningCts;
        private bool _isListening;

        public bool IsListening => _isListening;
        public LiveStreamSession? CurrentSession { get; private set; }
        public ObservableCollection<LiveStreamSession> SessionHistory => _sessionHistory;

        public event Action<bool>? ListeningStateChanged;
        public event Action<LiveDetectedTrackItem>? TrackDetected;
        public event Action<float>? AudioLevelChanged;
        public event Action<string>? StatusUpdated;

        public LiveStreamRecognitionService(IAudioCaptureService audioCaptureService, ShazamRecognitionService shazamService)
        {
            _audioCaptureService = audioCaptureService;
            _shazamService = shazamService;

            _shazamService.AudioLevelChanged += level => AudioLevelChanged?.Invoke(level);

            string appDataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "UniversalDownloader");
            Directory.CreateDirectory(appDataDir);
            _sessionsFilePath = Path.Combine(appDataDir, "live_stream_sessions.json");

            LoadSessionHistory();
        }

        public async Task ToggleListeningAsync()
        {
            if (_isListening)
            {
                StopListening();
            }
            else
            {
                await StartListeningAsync();
            }
        }

        public async Task StartListeningAsync()
        {
            if (_isListening) return;

            _isListening = true;
            _listeningCts?.Cancel();
            _listeningCts = new CancellationTokenSource();
            var token = _listeningCts.Token;

            // Start new session
            CurrentSession = new LiveStreamSession
            {
                StartTime = DateTime.Now
            };

            lock (_lock)
            {
                _sessionHistory.Insert(0, CurrentSession);
            }

            ListeningStateChanged?.Invoke(true);
            StatusUpdated?.Invoke("🔴 Listening to PC Audio in real-time...");

            _ = Task.Run(() => ListeningLoopAsync(token), token);
        }

        public void StopListening()
        {
            if (!_isListening) return;

            _isListening = false;
            _listeningCts?.Cancel();
            _listeningCts = null;

            if (CurrentSession != null)
            {
                CurrentSession.EndTime = DateTime.Now;
                _ = SaveSessionHistoryAsync();
            }

            ListeningStateChanged?.Invoke(false);
            StatusUpdated?.Invoke("⚪ Listening stopped.");
            AudioLevelChanged?.Invoke(0f);
        }

        private async Task ListeningLoopAsync(CancellationToken token)
        {
            string lastIdentifiedKey = string.Empty;
            DateTime lastIdentifiedTime = DateTime.MinValue;

            while (!token.IsCancellationRequested && _isListening)
            {
                try
                {
                    StatusUpdated?.Invoke("🎙️ Sampling PC Audio (5s)...");

                    // 1. Identify 5-second sample from PC Audio
                    var result = await _shazamService.ListenAndIdentifyAsync(
                        durationSeconds: 5,
                        source: AudioCaptureSource.SystemAudio,
                        cancellationToken: token);

                    if (token.IsCancellationRequested || !_isListening) break;

                    if (result.Success && !string.IsNullOrWhiteSpace(result.Title))
                    {
                        string currentTrackKey = $"{result.Artist.Trim()} - {result.Title.Trim()}".ToLowerInvariant();

                        bool isSameAsCurrentSong = string.Equals(lastIdentifiedKey, currentTrackKey, StringComparison.OrdinalIgnoreCase);

                        // Check if already in current session (Deduplication)
                        bool alreadyInSession = false;
                        if (CurrentSession != null)
                        {
                            lock (CurrentSession.Tracks)
                            {
                                alreadyInSession = CurrentSession.Tracks.Any(t =>
                                    string.Equals(t.Title.Trim(), result.Title.Trim(), StringComparison.OrdinalIgnoreCase) &&
                                    string.Equals(t.Artist.Trim(), result.Artist.Trim(), StringComparison.OrdinalIgnoreCase));
                            }
                        }

                        if (!alreadyInSession && CurrentSession != null)
                        {
                            var trackItem = new LiveDetectedTrackItem
                            {
                                Title = result.Title.Trim(),
                                Artist = result.Artist.Trim(),
                                Album = result.Album.Trim(),
                                CoverArtUrl = result.CoverArtUrl,
                                DetectedAt = DateTime.Now
                            };

                            lock (CurrentSession.Tracks)
                            {
                                CurrentSession.Tracks.Insert(0, trackItem);
                            }

                            lastIdentifiedKey = currentTrackKey;
                            lastIdentifiedTime = DateTime.Now;

                            TrackDetected?.Invoke(trackItem);
                            StatusUpdated?.Invoke($"✨ Detected: {trackItem.QueryString}");

                            _ = SaveSessionHistoryAsync();
                        }
                        else
                        {
                            // Track is already logged in session
                            StatusUpdated?.Invoke($"🎵 Playing: {result.Artist} - {result.Title}");
                        }

                        // Smart cooldown: wait 25 seconds before sampling next song since track is actively playing
                        for (int i = 0; i < 25 && !token.IsCancellationRequested && _isListening; i++)
                        {
                            await Task.Delay(1000, token);
                        }
                    }
                    else
                    {
                        // No track found or silence
                        StatusUpdated?.Invoke("🎧 Listening for next song...");
                        // Wait 7 seconds before next sampling cycle
                        for (int i = 0; i < 7 && !token.IsCancellationRequested && _isListening; i++)
                        {
                            await Task.Delay(1000, token);
                        }
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Live Stream Scraper loop error: {ex.Message}");
                    try { await Task.Delay(5000, token); } catch { }
                }
            }
        }

        public void LoadSessionHistory()
        {
            lock (_lock)
            {
                try
                {
                    _sessionHistory.Clear();
                    if (File.Exists(_sessionsFilePath))
                    {
                        string json = File.ReadAllText(_sessionsFilePath);
                        var loaded = JsonConvert.DeserializeObject<List<LiveStreamSession>>(json);
                        if (loaded != null)
                        {
                            foreach (var s in loaded.OrderByDescending(x => x.StartTime))
                            {
                                if (s.Tracks.Count > 0)
                                {
                                    _sessionHistory.Add(s);
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to load live sessions: {ex.Message}");
                }
            }
        }

        private async Task SaveSessionHistoryAsync()
        {
            try
            {
                List<LiveStreamSession> snapshot;
                lock (_lock)
                {
                    snapshot = _sessionHistory.Where(s => s.Tracks.Count > 0).Take(50).ToList();
                }

                string json = JsonConvert.SerializeObject(snapshot, Formatting.Indented);
                await File.WriteAllTextAsync(_sessionsFilePath, json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to save live stream sessions: {ex.Message}");
            }
        }

        public void Dispose()
        {
            StopListening();
        }
    }
}
