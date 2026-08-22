using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace UniversalDownloader.Services
{
    public class FfmpegAudioCaptureService : IAudioCaptureService
    {
        private readonly DependencyManager _dependencyManager;
        public event Action<float>? AudioLevelChanged;

        public FfmpegAudioCaptureService(DependencyManager dependencyManager)
        {
            _dependencyManager = dependencyManager;
        }

        public async Task<byte[]> CaptureAudioAsync(int durationSeconds = 5, AudioCaptureSource source = AudioCaptureSource.SystemAudio, CancellationToken cancellationToken = default)
        {
            if (!_dependencyManager.IsFfmpegReady)
            {
                return Array.Empty<byte>();
            }

            var pcmStream = new MemoryStream();

            var psi = new ProcessStartInfo
            {
                FileName = _dependencyManager.FfmpegExecutablePath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            bool isLinux = RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

            if (isLinux)
            {
                // Linux PulseAudio / PipeWire capture
                psi.ArgumentList.Add("-f");
                psi.ArgumentList.Add("pulse");
                psi.ArgumentList.Add("-i");
                psi.ArgumentList.Add(source == AudioCaptureSource.SystemAudio ? "default" : "default");
            }
            else
            {
                // Fallback for Windows
                psi.ArgumentList.Add("-f");
                psi.ArgumentList.Add("dshow");
                psi.ArgumentList.Add("-i");
                psi.ArgumentList.Add("audio=virtual-audio-capturer");
            }

            psi.ArgumentList.Add("-t");
            psi.ArgumentList.Add(durationSeconds.ToString());
            psi.ArgumentList.Add("-ac");
            psi.ArgumentList.Add("1");
            psi.ArgumentList.Add("-ar");
            psi.ArgumentList.Add("16000");
            psi.ArgumentList.Add("-f");
            psi.ArgumentList.Add("s16le");
            psi.ArgumentList.Add("pipe:1");

            try
            {
                using var process = new Process { StartInfo = psi };
                process.Start();

                var readTask = Task.Run(async () =>
                {
                    byte[] buffer = new byte[4096];
                    int read;
                    while ((read = await process.StandardOutput.BaseStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                    {
                        pcmStream.Write(buffer, 0, read);

                        // Calculate RMS level
                        float max = 0;
                        for (int i = 0; i < read - 1; i += 2)
                        {
                            short sample = (short)((buffer[i + 1] << 8) | buffer[i]);
                            float sample32 = Math.Abs(sample / 32768f);
                            if (sample32 > max) max = sample32;
                        }
                        AudioLevelChanged?.Invoke(Math.Min(1.0f, max * 2.5f));
                    }
                }, cancellationToken);

                await Task.WhenAll(process.WaitForExitAsync(cancellationToken), readTask);
                return pcmStream.ToArray();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"FFmpeg audio capture error: {ex.Message}");
                return Array.Empty<byte>();
            }
        }
    }
}
