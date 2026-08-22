using System;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Threading;

namespace UniversalDownloader.Services
{
    public class ClipboardMonitorService
    {
        private readonly DispatcherTimer _timer;
        private string _lastClipboardText = string.Empty;
        private bool _isEnabled = true;

        public event Action<string>? MediaUrlDetected;

        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                _isEnabled = value;
                if (!_isEnabled)
                {
                    _timer.Stop();
                }
                else
                {
                    _timer.Start();
                }
            }
        }

        public ClipboardMonitorService()
        {
            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(800)
            };
            _timer.Tick += OnTimerTick;
        }

        public void Start()
        {
            if (_isEnabled)
            {
                try
                {
                    if (Clipboard.ContainsText())
                    {
                        _lastClipboardText = Clipboard.GetText().Trim();
                    }
                }
                catch { }

                _timer.Start();
            }
        }

        public void Stop()
        {
            _timer.Stop();
        }

        private void OnTimerTick(object? sender, EventArgs e)
        {
            if (!_isEnabled) return;

            try
            {
                if (Clipboard.ContainsText())
                {
                    string currentText = Clipboard.GetText().Trim();
                    if (!string.IsNullOrWhiteSpace(currentText) && currentText != _lastClipboardText)
                    {
                        _lastClipboardText = currentText;
                        if (IsSupportedMediaUrl(currentText))
                        {
                            MediaUrlDetected?.Invoke(currentText);
                        }
                    }
                }
            }
            catch { /* Clipboard may be locked by another application */ }
        }

        public static bool IsSupportedMediaUrl(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            text = text.Trim();

            if (!text.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !text.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return Regex.IsMatch(text, @"(?:youtube\.com|youtu\.be|spotify\.com|instagram\.com|tiktok\.com|douyin\.com|twitter\.com|x\.com|reddit\.com|soundcloud\.com|facebook\.com|fb\.watch|pinterest\.com|vimeo\.com|drive\.google\.com)", RegexOptions.IgnoreCase);
        }
    }
}
