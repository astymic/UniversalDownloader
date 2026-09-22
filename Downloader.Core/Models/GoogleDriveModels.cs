using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;

namespace UniversalDownloader.Models
{
    public class GoogleDriveItem : INotifyPropertyChanged
    {
        private bool? _isSelected = true;
        private bool _isExpanded = true;
        private bool _isUpdatingSelection = false;

        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public bool IsFolder { get; set; }
        public string? MimeType { get; set; }
        public string? SizeString { get; set; }
        public long SizeBytes { get; set; }
        public string RelativePath { get; set; } = string.Empty;

        public long TotalSizeBytes
        {
            get
            {
                if (!IsFolder) return SizeBytes;
                return Children.Sum(c => c.TotalSizeBytes);
            }
        }

        public string DisplaySizeString
        {
            get
            {
                if (!IsFolder)
                {
                    if (!string.IsNullOrWhiteSpace(SizeString)) return SizeString;
                    return SizeBytes > 0 ? FormatBytes(SizeBytes) : "";
                }
                else
                {
                    long total = TotalSizeBytes;
                    return total > 0 ? FormatBytes(total) : "";
                }
            }
        }

        public GoogleDriveItem? Parent { get; set; }
        public ObservableCollection<GoogleDriveItem> Children { get; set; } = new();

        public bool? IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged();

                    if (!_isUpdatingSelection)
                    {
                        _isUpdatingSelection = true;
                        try
                        {
                            if (value.HasValue && IsFolder)
                            {
                                PropagateSelectionDown(value.Value);
                            }
                            Parent?.PropagateSelectionUp();
                        }
                        finally
                        {
                            _isUpdatingSelection = false;
                        }
                    }
                }
            }
        }

        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (_isExpanded != value)
                {
                    _isExpanded = value;
                    OnPropertyChanged();
                }
            }
        }

        public string Icon
        {
            get
            {
                if (IsFolder) return "📁";
                string ext = Path.GetExtension(Name).ToLowerInvariant();
                string mime = (MimeType ?? "").ToLowerInvariant();

                if (mime.StartsWith("video/") || ext is ".mp4" or ".mkv" or ".mov" or ".avi" or ".mxf" or ".webm" or ".flv")
                    return "🎬";
                if (mime.StartsWith("audio/") || ext is ".mp3" or ".wav" or ".flac" or ".aac" or ".m4a" or ".aiff" or ".ogg")
                    return "🎵";
                if (mime.StartsWith("image/") || ext is ".jpg" or ".jpeg" or ".png" or ".gif" or ".webp" or ".bmp" or ".tiff")
                    return "🖼️";
                if (ext is ".zip" or ".rar" or ".7z" or ".tar" or ".gz")
                    return "📦";
                if (ext is ".pdf" or ".doc" or ".docx" or ".txt" or ".xml" or ".cif")
                    return "📄";

                return "📄";
            }
        }

        public int TotalFileCount
        {
            get
            {
                if (!IsFolder) return 1;
                return Children.Sum(c => c.TotalFileCount);
            }
        }

        public void PropagateSelectionDown(bool isSelected)
        {
            foreach (var child in Children)
            {
                child._isUpdatingSelection = true;
                child.IsSelected = isSelected;
                if (child.IsFolder)
                {
                    child.PropagateSelectionDown(isSelected);
                }
                child._isUpdatingSelection = false;
            }
        }

        public void PropagateSelectionUp()
        {
            if (Children.Count == 0) return;

            bool allSelected = Children.All(c => c.IsSelected == true);
            bool allDeselected = Children.All(c => c.IsSelected == false);

            _isUpdatingSelection = true;
            try
            {
                if (allSelected)
                {
                    IsSelected = true;
                }
                else if (allDeselected)
                {
                    IsSelected = false;
                }
                else
                {
                    IsSelected = null; // Indeterminate
                }
            }
            finally
            {
                _isUpdatingSelection = false;
            }

            Parent?.PropagateSelectionUp();
        }

        public static long ParseSizeToBytes(string? sizeStr)
        {
            if (string.IsNullOrWhiteSpace(sizeStr)) return 0;

            var match = System.Text.RegularExpressions.Regex.Match(sizeStr.Trim(), @"^([\d\.,\s]+)\s*([a-zA-Z]+)?$");
            if (!match.Success) return 0;

            string numPart = match.Groups[1].Value.Replace(" ", "").Trim();
            string unit = (match.Groups[2].Value ?? "").ToUpperInvariant();

            if (numPart.Contains(',') && !numPart.Contains('.'))
            {
                numPart = numPart.Replace(',', '.');
            }
            else if (numPart.Contains(',') && numPart.Contains('.'))
            {
                if (numPart.IndexOf(',') < numPart.IndexOf('.'))
                {
                    numPart = numPart.Replace(",", "");
                }
                else
                {
                    numPart = numPart.Replace(".", "").Replace(',', '.');
                }
            }

            if (!double.TryParse(numPart, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double val))
            {
                return 0;
            }

            return unit switch
            {
                "B" or "BYTES" or "BYTE" => (long)Math.Round(val),
                "KB" or "K" => (long)Math.Round(val * 1024.0),
                "MB" or "M" => (long)Math.Round(val * 1024.0 * 1024.0),
                "GB" or "G" => (long)Math.Round(val * 1024.0 * 1024.0 * 1024.0),
                "TB" or "T" => (long)Math.Round(val * 1024.0 * 1024.0 * 1024.0 * 1024.0),
                _ => (long)Math.Round(val)
            };
        }

        public static string FormatBytes(long bytes)
        {
            if (bytes <= 0) return "0 B";
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int counter = 0;
            double dBytes = bytes;
            while (dBytes >= 1024 && counter < suffixes.Length - 1)
            {
                dBytes /= 1024;
                counter++;
            }
            return counter == 0 ? $"{dBytes:0} {suffixes[counter]}" : $"{dBytes:0.##} {suffixes[counter]}";
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class GoogleDriveFolderResult
    {
        public string RootFolderId { get; set; } = string.Empty;
        public string RootTitle { get; set; } = string.Empty;
        public ObservableCollection<GoogleDriveItem> Items { get; set; } = new();
        public int TotalFilesCount { get; set; }
        public int TotalFoldersCount { get; set; }
        public long TotalBytes { get; set; }
        public string TotalSizeString => GoogleDriveItem.FormatBytes(TotalBytes);
    }
}
