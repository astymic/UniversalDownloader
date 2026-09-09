using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace UniversalDownloader.Models
{
    public class VideoCompressorItem : INotifyPropertyChanged
    {
        private string _inputPath = "";
        private string _outputPath = "";
        private string _fileName = "";
        private string _originalSizeFormatted = "";
        private long _originalSizeBytes = 0;
        private string _compressedSizeFormatted = "";
        private long _compressedSizeBytes = 0;
        private string _savingsFormatted = "";
        private double _progress = 0;
        private string _status = "Ready to compress";
        private bool _isCompressing = false;
        private bool _isCompleted = false;
        private string _durationFormatted = "";
        private string _timeRemainingFormatted = "";
        private double _elapsedSeconds = 0;

        public string InputPath
        {
            get => _inputPath;
            set { _inputPath = value; OnPropertyChanged(); }
        }

        public string OutputPath
        {
            get => _outputPath;
            set { _outputPath = value; OnPropertyChanged(); }
        }

        public string FileName
        {
            get => _fileName;
            set { _fileName = value; OnPropertyChanged(); }
        }

        public string OriginalSizeFormatted
        {
            get => _originalSizeFormatted;
            set { _originalSizeFormatted = value; OnPropertyChanged(); }
        }

        public long OriginalSizeBytes
        {
            get => _originalSizeBytes;
            set { _originalSizeBytes = value; OnPropertyChanged(); }
        }

        public string CompressedSizeFormatted
        {
            get => _compressedSizeFormatted;
            set { _compressedSizeFormatted = value; OnPropertyChanged(); }
        }

        public long CompressedSizeBytes
        {
            get => _compressedSizeBytes;
            set { _compressedSizeBytes = value; OnPropertyChanged(); }
        }

        public string SavingsFormatted
        {
            get => _savingsFormatted;
            set { _savingsFormatted = value; OnPropertyChanged(); }
        }

        public double Progress
        {
            get => _progress;
            set { _progress = value; OnPropertyChanged(); }
        }

        public string Status
        {
            get => _status;
            set { _status = value; OnPropertyChanged(); }
        }

        public bool IsCompressing
        {
            get => _isCompressing;
            set { _isCompressing = value; OnPropertyChanged(); }
        }

        public bool IsCompleted
        {
            get => _isCompleted;
            set { _isCompleted = value; OnPropertyChanged(); }
        }

        public string DurationFormatted
        {
            get => _durationFormatted;
            set { _durationFormatted = value; OnPropertyChanged(); }
        }

        public string TimeRemainingFormatted
        {
            get => _timeRemainingFormatted;
            set { _timeRemainingFormatted = value; OnPropertyChanged(); }
        }

        public double ElapsedSeconds
        {
            get => _elapsedSeconds;
            set { _elapsedSeconds = value; OnPropertyChanged(); }
        }

        private System.Threading.CancellationTokenSource? _cts;

        [System.Text.Json.Serialization.JsonIgnore]
        public System.Threading.CancellationTokenSource? Cts
        {
            get => _cts;
            set => _cts = value;
        }

        public void Cancel()
        {
            try
            {
                _cts?.Cancel();
            }
            catch { }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
