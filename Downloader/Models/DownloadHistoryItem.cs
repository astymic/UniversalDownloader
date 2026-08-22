using System;

namespace UniversalDownloader.Models
{
    public class DownloadHistoryItem
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Title { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public string Platform { get; set; } = "Media";
        public string FilePath { get; set; } = string.Empty;
        public long FileSizeBytes { get; set; }
        public string FormattedSize { get; set; } = string.Empty;
        public DateTime DownloadDate { get; set; } = DateTime.Now;
        public bool IsAudio { get; set; }
        public string FormatExtension { get; set; } = string.Empty;
        public string? ThumbnailUrl { get; set; }
    }
}
