using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace UniversalDownloader.Models
{
    public class YouTubeQualityItem
    {
        public required string Label { get; set; }
        public required string FormatCode { get; set; }
        public bool IsAudioOnly { get; set; }
        /// <summary>yt-dlp --audio-format value: "best" keeps original, "mp3" converts to MP3.</summary>
        public string AudioFormat { get; set; } = "best";
        public int SortPriority { get; set; }
    }

    public class PlaylistVideoItem : INotifyPropertyChanged
    {
        private bool _isSelected = true;
        private int _selectedQualityIndex = 0;

        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(nameof(IsSelected)); }
        }

        public string Title { get; set; } = "";
        public string VideoUrl { get; set; } = "";
        public string DurationText { get; set; } = "";
        public List<YouTubeQualityItem> AvailableQualities { get; set; } = new();

        public int SelectedQualityIndex
        {
            get => _selectedQualityIndex;
            set { _selectedQualityIndex = value; OnPropertyChanged(nameof(SelectedQualityIndex)); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
