using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace UniversalDownloader.Models
{
    public class AnimeSeriesInfo
    {
        public string AnimeId { get; set; } = string.Empty;
        public string Slug { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string OriginalTitle { get; set; } = string.Empty;
        public string PosterUrl { get; set; } = string.Empty;
        public string Year { get; set; } = string.Empty;
        public string Rating { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public int TotalEpisodesCount { get; set; }
        public ObservableCollection<AnimeDubInfo> Dubs { get; set; } = new();
    }

    public class AnimeDubInfo
    {
        public string DubId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public int AvailableEpisodesCount { get; set; }
        public int TotalEpisodesCount { get; set; }
        public string DisplayBadge => TotalEpisodesCount > 0 ? $"{Name} ({AvailableEpisodesCount}/{TotalEpisodesCount})" : Name;
        public ObservableCollection<AnimeEpisodeInfo> Episodes { get; set; } = new();

        public override string ToString() => DisplayBadge;
    }

    public class AnimeEpisodeInfo : INotifyPropertyChanged
    {
        private bool _isSelected = true;

        public int EpisodeNumber { get; set; }
        public string Title { get; set; } = string.Empty;

        public string DisplayTitle
        {
            get
            {
                if (string.IsNullOrWhiteSpace(Title) || Title.Equals($"Серия {EpisodeNumber}", StringComparison.OrdinalIgnoreCase) || Title.Equals($"{EpisodeNumber}", StringComparison.OrdinalIgnoreCase))
                {
                    return $"Серия {EpisodeNumber}";
                }
                return $"Серия {EpisodeNumber}: {Title}";
            }
        }
        
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged();
                }
            }
        }

        public string BestQualityText { get; set; } = "1080p";
        public string BestPlayerName { get; set; } = "CVH";
        public string QualityBadge => $"{BestQualityText} ({BestPlayerName})";
        public bool IsFullHd => BestQualityText.Contains("1080");

        public AnimePlayerInfo? SelectedPlayer { get; set; }
        public List<AnimePlayerInfo> Players { get; set; } = new();

        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class AnimePlayerInfo
    {
        public string PlayerName { get; set; } = string.Empty; // "CVH", "Alloha", "Kodik", etc.
        public string Quality { get; set; } = "1080p"; // "1080p", "720p", etc.
        public int EpisodeNumber { get; set; }
        public string IframeUrl { get; set; } = string.Empty;
        public string StreamUrl { get; set; } = string.Empty;
        public Dictionary<string, string> Headers { get; set; } = new();
    }
}
