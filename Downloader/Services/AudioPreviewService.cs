using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Threading;
using UniversalDownloader.Models;

namespace UniversalDownloader.Services
{
    public class AudioPreviewService : IDisposable
    {
        private readonly MediaPlayer _mediaPlayer = new();
        private readonly DispatcherTimer _positionTimer;
        private bool _isDisposed;

        public DownloadHistoryItem? CurrentItem { get; private set; }
        public bool IsPlaying { get; private set; }
        public bool IsPaused { get; private set; }
        public bool IsMuted => _mediaPlayer.IsMuted;

        public double Volume
        {
            get => _mediaPlayer.Volume;
            set => _mediaPlayer.Volume = Math.Clamp(value, 0.0, 1.0);
        }

        public TimeSpan Duration => _mediaPlayer.NaturalDuration.HasTimeSpan ? _mediaPlayer.NaturalDuration.TimeSpan : TimeSpan.Zero;
        public TimeSpan Position => _mediaPlayer.Position;

        public event Action? PlaybackStateChanged;
        public event Action<TimeSpan, TimeSpan>? PositionChanged;
        public event Action? MediaEnded;
        public event Action<string>? MediaFailed;

        public AudioPreviewService()
        {
            _mediaPlayer.Volume = 0.8;
            _mediaPlayer.MediaOpened += MediaPlayer_MediaOpened;
            _mediaPlayer.MediaEnded += MediaPlayer_MediaEnded;
            _mediaPlayer.MediaFailed += MediaPlayer_MediaFailed;

            _positionTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(200)
            };
            _positionTimer.Tick += PositionTimer_Tick;
        }

        private void MediaPlayer_MediaOpened(object? sender, EventArgs e)
        {
            PlaybackStateChanged?.Invoke();
            PositionChanged?.Invoke(Position, Duration);
        }

        private void MediaPlayer_MediaEnded(object? sender, EventArgs e)
        {
            IsPlaying = false;
            IsPaused = false;
            _positionTimer.Stop();
            PlaybackStateChanged?.Invoke();
            MediaEnded?.Invoke();
        }

        private void MediaPlayer_MediaFailed(object? sender, ExceptionEventArgs e)
        {
            IsPlaying = false;
            IsPaused = false;
            _positionTimer.Stop();
            PlaybackStateChanged?.Invoke();
            MediaFailed?.Invoke(e.ErrorException?.Message ?? "Unable to play media file.");
        }

        private void PositionTimer_Tick(object? sender, EventArgs e)
        {
            if (IsPlaying)
            {
                PositionChanged?.Invoke(_mediaPlayer.Position, Duration);
            }
        }

        public bool Play(DownloadHistoryItem item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.FilePath) || !File.Exists(item.FilePath))
            {
                MediaFailed?.Invoke("File does not exist on disk.");
                return false;
            }

            try
            {
                CurrentItem = item;
                _mediaPlayer.Open(new Uri(item.FilePath, UriKind.Absolute));
                _mediaPlayer.Play();
                IsPlaying = true;
                IsPaused = false;
                _positionTimer.Start();
                PlaybackStateChanged?.Invoke();
                return true;
            }
            catch (Exception ex)
            {
                MediaFailed?.Invoke($"Error opening media: {ex.Message}");
                return false;
            }
        }

        public void TogglePlayPause()
        {
            if (CurrentItem == null) return;

            if (IsPlaying)
            {
                _mediaPlayer.Pause();
                IsPlaying = false;
                IsPaused = true;
                _positionTimer.Stop();
            }
            else
            {
                _mediaPlayer.Play();
                IsPlaying = true;
                IsPaused = false;
                _positionTimer.Start();
            }
            PlaybackStateChanged?.Invoke();
        }

        public void Seek(TimeSpan position)
        {
            if (Duration > TimeSpan.Zero)
            {
                if (position < TimeSpan.Zero) position = TimeSpan.Zero;
                if (position > Duration) position = Duration;
                _mediaPlayer.Position = position;
                PositionChanged?.Invoke(position, Duration);
            }
        }

        public void ToggleMute()
        {
            _mediaPlayer.IsMuted = !_mediaPlayer.IsMuted;
            PlaybackStateChanged?.Invoke();
        }

        public void Stop()
        {
            _mediaPlayer.Stop();
            _positionTimer.Stop();
            IsPlaying = false;
            IsPaused = false;
            CurrentItem = null;
            PlaybackStateChanged?.Invoke();
        }

        public void Dispose()
        {
            if (!_isDisposed)
            {
                _isDisposed = true;
                _positionTimer.Stop();
                _mediaPlayer.Close();
            }
        }
    }
}
