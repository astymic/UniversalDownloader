using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace UniversalDownloader.Models
{
    public enum QueueItemStatus
    {
        Queued,
        Downloading,
        Completed,
        Failed,
        Canceled
    }

    public class DownloadQueueItem : INotifyPropertyChanged
    {
        private string _id = Guid.NewGuid().ToString("N");
        private string _title = "Loading...";
        private string _url = string.Empty;
        private string _formatCode = "best";
        private bool _isAudioOnly;
        private string _audioFormat = "mp3";
        private double _progress;
        private string _statusText = "Queued";
        private QueueItemStatus _status = QueueItemStatus.Queued;
        private string? _errorMessage;
        private string _destinationFolder = string.Empty;
        private string? _downloadedFilePath;

        public string Id
        {
            get => _id;
            set { _id = value; OnPropertyChanged(); }
        }

        public string Title
        {
            get => _title;
            set { _title = value; OnPropertyChanged(); }
        }

        public string Url
        {
            get => _url;
            set { _url = value; OnPropertyChanged(); }
        }

        public string FormatCode
        {
            get => _formatCode;
            set { _formatCode = value; OnPropertyChanged(); }
        }

        public bool IsAudioOnly
        {
            get => _isAudioOnly;
            set { _isAudioOnly = value; OnPropertyChanged(); }
        }

        public string AudioFormat
        {
            get => _audioFormat;
            set { _audioFormat = value; OnPropertyChanged(); }
        }

        public double Progress
        {
            get => _progress;
            set 
            { 
                _progress = value; 
                OnPropertyChanged(); 
                OnPropertyChanged(nameof(PercentText));
            }
        }

        public string PercentText => $"{Math.Round(Progress)}%";

        public string StatusText
        {
            get => _statusText;
            set { _statusText = value; OnPropertyChanged(); }
        }

        public QueueItemStatus Status
        {
            get => _status;
            set 
            { 
                _status = value; 
                OnPropertyChanged(); 
                OnPropertyChanged(nameof(IsQueued));
                OnPropertyChanged(nameof(IsDownloading));
                OnPropertyChanged(nameof(IsCompleted));
                OnPropertyChanged(nameof(IsFailed));
                OnPropertyChanged(nameof(IsCanceled));
                OnPropertyChanged(nameof(CanCancel));
                OnPropertyChanged(nameof(CanRetry));
            }
        }

        public bool IsQueued => Status == QueueItemStatus.Queued;
        public bool IsDownloading => Status == QueueItemStatus.Downloading;
        public bool IsCompleted => Status == QueueItemStatus.Completed;
        public bool IsFailed => Status == QueueItemStatus.Failed;
        public bool IsCanceled => Status == QueueItemStatus.Canceled;
        public bool CanCancel => Status == QueueItemStatus.Queued || Status == QueueItemStatus.Downloading;
        public bool CanRetry => Status == QueueItemStatus.Failed || Status == QueueItemStatus.Canceled;

        public string? ErrorMessage
        {
            get => _errorMessage;
            set { _errorMessage = value; OnPropertyChanged(); }
        }

        public string DestinationFolder
        {
            get => _destinationFolder;
            set { _destinationFolder = value; OnPropertyChanged(); }
        }

        public string? DownloadedFilePath
        {
            get => _downloadedFilePath;
            set { _downloadedFilePath = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
