using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace UniversalDownloader.Services
{
    public enum AudioCaptureSource
    {
        SystemAudio,
        Microphone
    }

    public class AudioCaptureService
    {
        public event Action<float>? AudioLevelChanged;

        public async Task<byte[]> CaptureAudioAsync(int durationSeconds = 5, AudioCaptureSource source = AudioCaptureSource.SystemAudio, CancellationToken cancellationToken = default)
        {
            var rawCapturedStream = new MemoryStream();
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
                        WaveFormat = new WaveFormat(44100, 16, 1),
                        BufferMilliseconds = 50
                    };
                }

                var format = waveIn.WaveFormat;
                bool isFloat32 = format.Encoding == WaveFormatEncoding.IeeeFloat && format.BitsPerSample == 32;

                waveIn.DataAvailable += (s, e) =>
                {
                    if (e.BytesRecorded > 0)
                    {
                        rawCapturedStream.Write(e.Buffer, 0, e.BytesRecorded);

                        float max = 0;
                        if (isFloat32)
                        {
                            for (int i = 0; i < e.BytesRecorded - 3; i += 4)
                            {
                                float sample = BitConverter.ToSingle(e.Buffer, i);
                                float abs = Math.Abs(sample);
                                if (abs > max) max = abs;
                            }
                            AudioLevelChanged?.Invoke(Math.Min(1.0f, max * 2.0f));
                        }
                        else
                        {
                            for (int i = 0; i < e.BytesRecorded - 1; i += 2)
                            {
                                short sample = (short)((e.Buffer[i + 1] << 8) | e.Buffer[i]);
                                float abs = Math.Abs(sample / 32768f);
                                if (abs > max) max = abs;
                            }
                            AudioLevelChanged?.Invoke(Math.Min(1.0f, max * 2.5f));
                        }
                    }
                };

                waveIn.StartRecording();

                int totalMs = durationSeconds * 1000;
                int elapsed = 0;
                while (elapsed < totalMs && !cancellationToken.IsCancellationRequested)
                {
                    await Task.Delay(100, cancellationToken);
                    elapsed += 100;
                }

                waveIn.StopRecording();

                if (rawCapturedStream.Length == 0)
                {
                    return Array.Empty<byte>();
                }

                rawCapturedStream.Position = 0;
                return ConvertTo16kHzMonoPcm(rawCapturedStream, format);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Audio capture error: {ex.Message}");
                throw;
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
                ISampleProvider sampleProvider = rawProvider.ToSampleProvider();

                // Downmix to mono if multi-channel
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
