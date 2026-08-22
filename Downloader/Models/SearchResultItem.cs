using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace UniversalDownloader.Models
{
    public class SearchResultItem : INotifyPropertyChanged
    {
        private string _id = string.Empty;
        private string _title = string.Empty;
        private string _artistOrChannel = string.Empty;
        private string _durationString = string.Empty;
        private string _thumbnailUrl = string.Empty;
        private string _sourceUrl = string.Empty;
        private string _platform = "YouTube";
        private bool _isOptionsExpanded;
        private YouTubeQualityItem? _selectedQuality;
        private List<YouTubeQualityItem> _availableQualities = new();

        public string Id
        {
            get => _id;
            set { if (_id != value) { _id = value; OnPropertyChanged(); } }
        }

        public string Title
        {
            get => _title;
            set { if (_title != value) { _title = value; OnPropertyChanged(); } }
        }

        public string ArtistOrChannel
        {
            get => _artistOrChannel;
            set { if (_artistOrChannel != value) { _artistOrChannel = value; OnPropertyChanged(); } }
        }

        public string DurationString
        {
            get => _durationString;
            set { if (_durationString != value) { _durationString = value; OnPropertyChanged(); } }
        }

        public string ThumbnailUrl
        {
            get => _thumbnailUrl;
            set { if (_thumbnailUrl != value) { _thumbnailUrl = value; OnPropertyChanged(); } }
        }

        public string SourceUrl
        {
            get => _sourceUrl;
            set { if (_sourceUrl != value) { _sourceUrl = value; OnPropertyChanged(); } }
        }

        public string Platform
        {
            get => _platform;
            set
            {
                if (_platform != value)
                {
                    _platform = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsYouTube));
                    OnPropertyChanged(nameof(IsSoundCloud));
                    OnPropertyChanged(nameof(PlatformBadgeColor));
                    OnPropertyChanged(nameof(PlatformBadgeBackground));
                }
            }
        }

        public bool IsYouTube => string.Equals(Platform, "YouTube", System.StringComparison.OrdinalIgnoreCase);
        public bool IsSoundCloud => string.Equals(Platform, "SoundCloud", System.StringComparison.OrdinalIgnoreCase);

        public string PlatformBadgeColor => IsYouTube ? "#EF4444" : "#F97316";
        public string PlatformBadgeBackground => IsYouTube ? "#2A1515" : "#2A1C15";

        public bool IsOptionsExpanded
        {
            get => _isOptionsExpanded;
            set { if (_isOptionsExpanded != value) { _isOptionsExpanded = value; OnPropertyChanged(); } }
        }

        public List<YouTubeQualityItem> AvailableQualities
        {
            get => _availableQualities;
            set { if (_availableQualities != value) { _availableQualities = value; OnPropertyChanged(); } }
        }

        public YouTubeQualityItem? SelectedQuality
        {
            get => _selectedQuality;
            set { if (_selectedQuality != value) { _selectedQuality = value; OnPropertyChanged(); } }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
