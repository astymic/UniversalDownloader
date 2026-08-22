using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace UniversalDownloader.Services
{
    public class ShazamPeak
    {
        public int FftPassNumber { get; set; }
        public ushort PeakMagnitude { get; set; }
        public ushort CorrectedFrequencyBin { get; set; }
    }

    public enum ShazamBand
    {
        Hz250_520 = 0,
        Hz520_1450 = 1,
        Hz1450_3500 = 2,
        Hz3500_5500 = 3
    }

    public class ShazamSignatureGenerator
    {
        private const int SampleRate = 16000;
        private const int WindowSize = 2048;
        private const int HopSize = 128;
        private const string DataUriPrefix = "data:audio/vnd.shazam.sig;base64,";

        private static readonly float[] HanningWindow = InitializeHanningWindow();

        private static float[] InitializeHanningWindow()
        {
            var window = new float[WindowSize];
            for (int i = 0; i < WindowSize; i++)
            {
                window[i] = (float)(0.5 * (1.0 - Math.Cos(2.0 * Math.PI * i / (WindowSize - 1))));
            }
            return window;
        }

        public static string CreateSignatureUri(byte[] pcm16Mono16kHz)
        {
            int numSamples = pcm16Mono16kHz.Length / 2;
            if (numSamples < WindowSize) return string.Empty;

            var samples = new float[numSamples];
            for (int i = 0; i < numSamples; i++)
            {
                short sample = (short)(pcm16Mono16kHz[i * 2] | (pcm16Mono16kHz[i * 2 + 1] << 8));
                samples[i] = sample;
            }

            var bandPeaks = new Dictionary<ShazamBand, List<ShazamPeak>>
            {
                [ShazamBand.Hz250_520] = new(),
                [ShazamBand.Hz520_1450] = new(),
                [ShazamBand.Hz1450_3500] = new(),
                [ShazamBand.Hz3500_5500] = new()
            };

            int numFrames = (numSamples - WindowSize) / HopSize;
            var windowBuffer = new float[WindowSize];
            var real = new double[WindowSize];
            var imag = new double[WindowSize];
            var magnitudes = new float[WindowSize / 2 + 1];

            for (int frame = 0; frame < numFrames; frame++)
            {
                int offset = frame * HopSize;

                for (int i = 0; i < WindowSize; i++)
                {
                    real[i] = samples[offset + i] * HanningWindow[i];
                    imag[i] = 0;
                }

                Fft(real, imag);

                for (int i = 0; i < magnitudes.Length; i++)
                {
                    magnitudes[i] = (float)Math.Sqrt(real[i] * real[i] + imag[i] * imag[i]);
                }

                // Extract peaks for each frequency band
                // Bin frequency = i * 16000 / 2048 = i * 7.8125 Hz
                ExtractBandPeak(magnitudes, 32, 67, frame, ShazamBand.Hz250_520, bandPeaks[ShazamBand.Hz250_520]);
                ExtractBandPeak(magnitudes, 67, 186, frame, ShazamBand.Hz520_1450, bandPeaks[ShazamBand.Hz520_1450]);
                ExtractBandPeak(magnitudes, 186, 448, frame, ShazamBand.Hz1450_3500, bandPeaks[ShazamBand.Hz1450_3500]);
                ExtractBandPeak(magnitudes, 448, 704, frame, ShazamBand.Hz3500_5500, bandPeaks[ShazamBand.Hz3500_5500]);
            }

            byte[] binarySignature = EncodeToBinary(numSamples, bandPeaks);
            return DataUriPrefix + Convert.ToBase64String(binarySignature);
        }

        private static void ExtractBandPeak(float[] magnitudes, int minBin, int maxBin, int frame, ShazamBand band, List<ShazamPeak> peaksList)
        {
            float maxMag = 0;
            int bestBin = -1;

            for (int i = minBin; i < maxBin && i < magnitudes.Length - 1; i++)
            {
                float mag = magnitudes[i];
                if (mag > maxMag && mag > magnitudes[i - 1] && mag > magnitudes[i + 1])
                {
                    maxMag = mag;
                    bestBin = i;
                }
            }

            if (bestBin != -1 && maxMag > 200f)
            {
                // Convert magnitude to Shazam log scale
                float logMag = Math.Max(0f, (float)(Math.Log(Math.Max(1f, maxMag)) * 1477.3 + 6144.0));
                ushort encodedMag = (ushort)Math.Min(ushort.MaxValue, (int)logMag);
                ushort encodedBin = (ushort)(bestBin * 64);

                peaksList.Add(new ShazamPeak
                {
                    FftPassNumber = frame,
                    PeakMagnitude = encodedMag,
                    CorrectedFrequencyBin = encodedBin
                });
            }
        }

        private static byte[] EncodeToBinary(int numberSamples, Dictionary<ShazamBand, List<ShazamPeak>> bandPeaks)
        {
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);

            // Reserve 8 bytes for Magic1 (0xCAFE2580) + CRC32
            writer.Write(0xCAFE2580);
            writer.Write(0u); // CRC32 placeholder

            long headerStartPos = ms.Position;

            // 48-byte header structure
            writer.Write(0u); // size_minus_header placeholder
            writer.Write(0x94119c00); // magic2
            writer.Write(0u); // void1[0]
            writer.Write(0u); // void1[1]
            writer.Write(0u); // void1[2]
            writer.Write(3u << 27); // shifted_sample_rate_id (3 for 16kHz)
            writer.Write(0u); // void2[0]
            writer.Write(0u); // void2[1]

            uint sampleCalc = (uint)(numberSamples + (SampleRate * 0.24));
            writer.Write(sampleCalc);
            writer.Write((15u << 19) + 0x40000u); // fixed_value (0x007c0000)

            // Band Peaks Payloads
            uint[] bandIds = { 0x60030040, 0x60030041, 0x60030042, 0x60030043 };

            for (int b = 0; b < 4; b++)
            {
                var band = (ShazamBand)b;
                var peaks = bandPeaks[band];

                writer.Write(bandIds[b]);
                writer.Write((uint)(peaks.Count * 8)); // 8 bytes per peak

                foreach (var peak in peaks)
                {
                    writer.Write((uint)peak.FftPassNumber);
                    writer.Write(peak.PeakMagnitude);
                    writer.Write(peak.CorrectedFrequencyBin);
                }
            }

            long totalLength = ms.Length;
            uint sizeMinusHeader = (uint)(totalLength - headerStartPos - 48);

            // Update size_minus_header
            ms.Position = headerStartPos;
            writer.Write(sizeMinusHeader);

            // Calculate CRC32 of all bytes after the first 8 bytes
            byte[] allBytes = ms.ToArray();
            uint crc = CalculateCrc32(allBytes, 8, allBytes.Length - 8);

            // Write CRC32 at offset 4
            ms.Position = 4;
            writer.Write(crc);

            return ms.ToArray();
        }

        private static uint CalculateCrc32(byte[] buffer, int offset, int length)
        {
            uint crc = 0xFFFFFFFF;
            for (int i = offset; i < offset + length; i++)
            {
                byte b = buffer[i];
                crc ^= b;
                for (int j = 0; j < 8; j++)
                {
                    if ((crc & 1) != 0)
                        crc = (crc >> 1) ^ 0xEDB88320;
                    else
                        crc >>= 1;
                }
            }
            return ~crc;
        }

        private static void Fft(double[] real, double[] imag)
        {
            int n = real.Length;
            int j = 0;
            for (int i = 0; i < n - 1; i++)
            {
                if (i < j)
                {
                    (real[i], real[j]) = (real[j], real[i]);
                    (imag[i], imag[j]) = (imag[j], imag[i]);
                }
                int k = n / 2;
                while (k <= j)
                {
                    j -= k;
                    k /= 2;
                }
                j += k;
            }

            for (int len = 2; len <= n; len <<= 1)
            {
                double angle = -2.0 * Math.PI / len;
                double wReal = Math.Cos(angle);
                double wImag = Math.Sin(angle);

                for (int i = 0; i < n; i += len)
                {
                    double curWReal = 1.0;
                    double curWImag = 0.0;

                    for (int k = 0; k < len / 2; k++)
                    {
                        double uReal = real[i + k];
                        double uImag = imag[i + k];

                        double vReal = real[i + k + len / 2] * curWReal - imag[i + k + len / 2] * curWImag;
                        double vImag = real[i + k + len / 2] * curWImag + imag[i + k + len / 2] * curWReal;

                        real[i + k] = uReal + vReal;
                        imag[i + k] = uImag + vImag;
                        real[i + k + len / 2] = uReal - vReal;
                        imag[i + k + len / 2] = uImag - vImag;

                        double nextWReal = curWReal * wReal - curWImag * wImag;
                        double nextWImag = curWReal * wImag + curWImag * wReal;
                        curWReal = nextWReal;
                        curWImag = nextWImag;
                    }
                }
            }
        }
    }
}
