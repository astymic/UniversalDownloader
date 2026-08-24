using System;

namespace UniversalDownloader.Models
{
    public class UpdateInfo
    {
        public bool IsUpdateAvailable { get; set; }
        public string CurrentVersion { get; set; } = string.Empty;
        public string LatestVersion { get; set; } = string.Empty;
        public string ReleaseTitle { get; set; } = string.Empty;
        public string ReleaseNotes { get; set; } = string.Empty;
        public string ReleaseUrl { get; set; } = string.Empty;
        public DateTime? PublishedAt { get; set; }
        public string? DownloadUrl { get; set; }
        public string? AssetFileName { get; set; }
        public long AssetSize { get; set; }

        public string FormattedSize
        {
            get
            {
                if (AssetSize <= 0) return string.Empty;
                double mb = (double)AssetSize / (1024 * 1024);
                return $"{mb:F1} MB";
            }
        }
    }
}
