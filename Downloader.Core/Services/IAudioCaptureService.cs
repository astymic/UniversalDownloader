using System;
using System.Threading;
using System.Threading.Tasks;

namespace UniversalDownloader.Services
{
    public enum AudioCaptureSource
    {
        SystemAudio,
        Microphone
    }

    public interface IAudioCaptureService
    {
        event Action<float>? AudioLevelChanged;
        Task<byte[]> CaptureAudioAsync(int durationSeconds = 5, AudioCaptureSource source = AudioCaptureSource.SystemAudio, CancellationToken cancellationToken = default);
    }
}
