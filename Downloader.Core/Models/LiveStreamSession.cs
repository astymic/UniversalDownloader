using System;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace UniversalDownloader.Models
{
    public class LiveDetectedTrackItem : INotifyPropertyChanged
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Title { get; set; } = string.Empty;
        public string Artist { get; set; } = string.Empty;
        public string Album { get; set; } = string.Empty;
        public string CoverArtUrl { get; set; } = string.Empty;
        public DateTime DetectedAt { get; set; } = DateTime.Now;

        public string FormattedTime => DetectedAt.ToString("hh:mm:ss tt");

        public string QueryString => !string.IsNullOrWhiteSpace(Artist) && !string.IsNullOrWhiteSpace(Title)
            ? $"{Artist} - {Title}"
            : Title;

        private bool _isDownloading;
        public bool IsDownloading
        {
            get => _isDownloading;
            set { _isDownloading = value; OnPropertyChanged(nameof(IsDownloading)); }
        }

        private bool _isDownloaded;
        public bool IsDownloaded
        {
            get => _isDownloaded;
            set { _isDownloaded = value; OnPropertyChanged(nameof(IsDownloaded)); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class LiveStreamSession : INotifyPropertyChanged
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public DateTime StartTime { get; set; } = DateTime.Now;
        public DateTime? EndTime { get; set; }

        public string FormattedDate => StartTime.ToString("yyyy-MM-dd • hh:mm tt");

        public string DurationString
        {
            get
            {
                var end = EndTime ?? DateTime.Now;
                var span = end - StartTime;
                return span.TotalHours >= 1 ? span.ToString(@"hh\:mm\:ss") : span.ToString(@"mm\:ss");
            }
        }

        public ObservableCollection<LiveDetectedTrackItem> Tracks { get; set; } = new();

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
