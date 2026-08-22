using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace UniversalDownloader.Services
{
    public enum AudioCaptureSource
    {
        Microphone,
        SystemAudio
    }

    public class AudioCaptureService
    {
        public event Action<float>? AudioLevelChanged;

        public async Task<byte[]> CaptureAudioAsync(int durationSeconds = 5, AudioCaptureSource source = AudioCaptureSource.Microphone, CancellationToken cancellationToken = default)
        {
            var memoryStream = new MemoryStream();
            IWaveIn? waveIn = null;

            try
            {
                if (source == AudioCaptureSource.SystemAudio)
                {
                    waveIn = new WasapiLoopbackCapture();
                }
                else
                {
                    waveIn = new WaveInEvent
                    {
                        WaveFormat = new WaveFormat(16000, 16, 1),
                        BufferMilliseconds = 50
                    };
                }

                var rawCapturedStream = new MemoryStream();

                waveIn.DataAvailable += (s, e) =>
                {
                    if (e.BytesRecorded > 0)
                    {
                        rawCapturedStream.Write(e.Buffer, 0, e.BytesRecorded);

                        // Calculate RMS level for UI waveform animation
                        float max = 0;
                        for (int i = 0; i < e.BytesRecorded; i += 2)
                        {
                            short sample = (short)((e.Buffer[i + 1] << 8) | e.Buffer[i]);
                            float sample32 = Math.Abs(sample / 32768f);
                            if (sample32 > max) max = sample32;
                        }
                        AudioLevelChanged?.Invoke(Math.Min(1.0f, max * 2.5f));
                    }
                };

                waveIn.StartRecording();

                // Record for requested duration (e.g. 5 seconds)
                int totalMs = durationSeconds * 1000;
                int elapsed = 0;
                while (elapsed < totalMs && !cancellationToken.IsCancellationRequested)
                {
                    await Task.Delay(100, cancellationToken);
                    elapsed += 100;
                }

                waveIn.StopRecording();

                // Convert captured audio to 16kHz 16-bit Mono PCM
                rawCapturedStream.Position = 0;
                return ConvertTo16kHzMonoPcm(rawCapturedStream, waveIn.WaveFormat);
            }
            finally
            {
                waveIn?.Dispose();
            }
        }

        private byte[] ConvertTo16kHzMonoPcm(Stream inputAudioStream, WaveFormat inputFormat)
        {
            var outStream = new MemoryStream();

            using (var rawProvider = new RawSourceWaveStream(inputAudioStream, inputFormat))
            {
                var targetFormat = new WaveFormat(16000, 16, 1);

                ISampleProvider sampleProvider = rawProvider.ToSampleProvider();

                // Downmix to mono if stereo/multichannel
                if (inputFormat.Channels > 1)
                {
                    sampleProvider = sampleProvider.ToMono();
                }

                // Resample to 16000 Hz if different
                if (inputFormat.SampleRate != 16000)
                {
                    var resampler = new WdlResamplingSampleProvider(sampleProvider, 16000);
                    sampleProvider = resampler;
                }

                var pcm16 = sampleProvider.ToWaveProvider16();

                byte[] buffer = new byte[4096];
                int bytesRead;
                while ((bytesRead = pcm16.Read(buffer, 0, buffer.Length)) > 0)
                {
                    outStream.Write(buffer, 0, bytesRead);
                }
            }

            return outStream.ToArray();
        }
    }
}
