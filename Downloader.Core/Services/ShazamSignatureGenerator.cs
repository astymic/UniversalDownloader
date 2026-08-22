using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace UniversalDownloader.Services
{
    public struct ShazamPeak
    {
        public uint FftPassNumber;
        public ushort PeakMagnitude;
        public ushort CorrectedFrequencyBin;
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
        private const string DataUriPrefix = "data:audio/vnd.shazam.sig;base64,";

        private static readonly float[] HanningMultipliers = InitializeHanningWindow();

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

            var rawPcm = new short[numSamples];
            for (int i = 0; i < numSamples; i++)
            {
                rawPcm[i] = (short)(pcm16Mono16kHz[i * 2] | (pcm16Mono16kHz[i * 2 + 1] << 8));
            }

            var generator = new ShazamSignatureGenerator();
            var bandPeaks = generator.ProcessAudio(rawPcm);

            byte[] binarySignature = EncodeToBinary(numSamples, bandPeaks);
            return DataUriPrefix + Convert.ToBase64String(binarySignature);
        }

        private readonly short[] _ringBuffer = new short[2048];
        private int _ringBufferIndex = 0;
        private readonly float[] _reorderedBuffer = new float[2048];
        private readonly float[][] _fftOutputs = new float[256][];
        private int _fftOutputsIndex = 0;
        private readonly float[][] _spreadFftOutputs = new float[256][];
        private int _spreadFftOutputsIndex = 0;
        private uint _numSpreadFftsDone = 0;
        private readonly Dictionary<ShazamBand, List<ShazamPeak>> _bandPeaks = new();

        public ShazamSignatureGenerator()
        {
            for (int i = 0; i < 256; i++)
            {
                _fftOutputs[i] = new float[1025];
                _spreadFftOutputs[i] = new float[1025];
            }

            _bandPeaks[ShazamBand.Hz250_520] = new List<ShazamPeak>();
            _bandPeaks[ShazamBand.Hz520_1450] = new List<ShazamPeak>();
            _bandPeaks[ShazamBand.Hz1450_3500] = new List<ShazamPeak>();
            _bandPeaks[ShazamBand.Hz3500_5500] = new List<ShazamPeak>();
        }

        public Dictionary<ShazamBand, List<ShazamPeak>> ProcessAudio(short[] samples)
        {
            int chunkCount = samples.Length / 128;
            for (int c = 0; c < chunkCount; c++)
            {
                DoFft(samples, c * 128);
                DoPeakSpreading();
                _numSpreadFftsDone++;

                if (_numSpreadFftsDone >= 46)
                {
                    DoPeakRecognition();
                }
            }

            return _bandPeaks;
        }

        private void DoFft(short[] samples, int offset)
        {
            Array.Copy(samples, offset, _ringBuffer, _ringBufferIndex, 128);
            _ringBufferIndex = (_ringBufferIndex + 128) & 2047;

            for (int i = 0; i < 2048; i++)
            {
                _reorderedBuffer[i] = _ringBuffer[(i + _ringBufferIndex) & 2047] * HanningMultipliers[i];
            }

            double[] real = new double[2048];
            double[] imag = new double[2048];
            for (int i = 0; i < 2048; i++)
            {
                real[i] = _reorderedBuffer[i];
                imag[i] = 0;
            }

            Fft(real, imag);

            float[] currentFft = _fftOutputs[_fftOutputsIndex];
            for (int i = 0; i <= 1024; i++)
            {
                float power = (float)((real[i] * real[i] + imag[i] * imag[i]) / (1 << 17));
                currentFft[i] = Math.Max(0.0000000001f, power);
            }

            _fftOutputsIndex = (_fftOutputsIndex + 1) & 255;
        }

        private void DoPeakSpreading()
        {
            float[] realFft = _fftOutputs[(_fftOutputsIndex - 1) & 255];
            float[] spreadFft = _spreadFftOutputs[_spreadFftOutputsIndex];

            Array.Copy(realFft, spreadFft, 1025);

            for (int pos = 0; pos <= 1022; pos++)
            {
                spreadFft[pos] = Math.Max(spreadFft[pos], Math.Max(spreadFft[pos + 1], spreadFft[pos + 2]));
            }

            float[] spreadCopy = (float[])spreadFft.Clone();

            int[] formerOffsets = { 1, 3, 6 };
            foreach (int former in formerOffsets)
            {
                float[] formerFft = _spreadFftOutputs[(_spreadFftOutputsIndex - former) & 255];
                for (int pos = 0; pos <= 1024; pos++)
                {
                    formerFft[pos] = Math.Max(formerFft[pos], spreadCopy[pos]);
                }
            }

            _spreadFftOutputsIndex = (_spreadFftOutputsIndex + 1) & 255;
        }

        private void DoPeakRecognition()
        {
            float[] fftMinus46 = _fftOutputs[(_fftOutputsIndex - 46) & 255];
            float[] fftMinus49 = _spreadFftOutputs[(_spreadFftOutputsIndex - 49) & 255];

            int[] neighborOffsets = { -10, -7, -4, -3, 1, 2, 5, 8 };
            int[] otherOffsets = { -53, -45, 165, 172, 179, 186, 193, 200, 214, 221, 228, 235, 242, 249 };

            for (int bin = 10; bin <= 1014; bin++)
            {
                if (fftMinus46[bin] >= 1.0f / 64.0f && fftMinus46[bin] >= fftMinus49[bin - 1])
                {
                    float maxNeighbor49 = 0.0f;
                    foreach (int offset in neighborOffsets)
                    {
                        int nPos = bin + offset;
                        if (nPos >= 0 && nPos <= 1024)
                        {
                            maxNeighbor49 = Math.Max(maxNeighbor49, fftMinus49[nPos]);
                        }
                    }

                    if (fftMinus46[bin] > maxNeighbor49)
                    {
                        float maxOther = maxNeighbor49;
                        foreach (int otherOffset in otherOffsets)
                        {
                            float[] otherFft = _spreadFftOutputs[(_spreadFftOutputsIndex + otherOffset) & 255];
                            maxOther = Math.Max(maxOther, otherFft[bin - 1]);
                        }

                        if (fftMinus46[bin] > maxOther)
                        {
                            uint passNumber = _numSpreadFftsDone - 46;

                            float peakMag = (float)(Math.Log(Math.Max(1.0f / 64.0f, fftMinus46[bin])) * 1477.3 + 6144.0);
                            float peakMagBefore = (float)(Math.Log(Math.Max(1.0f / 64.0f, fftMinus46[bin - 1])) * 1477.3 + 6144.0);
                            float peakMagAfter = (float)(Math.Log(Math.Max(1.0f / 64.0f, fftMinus46[bin + 1])) * 1477.3 + 6144.0);

                            float variation1 = peakMag * 2.0f - peakMagBefore - peakMagAfter;
                            float variation2 = variation1 > 0 ? (peakMagAfter - peakMagBefore) * 32.0f / variation1 : 0;

                            ushort correctedBin = (ushort)Math.Max(0, Math.Min(ushort.MaxValue, (int)(bin * 64 + variation2)));
                            float freqHz = correctedBin * (16000.0f / 2.0f / 1024.0f / 64.0f);

                            ShazamBand band;
                            if (freqHz >= 250 && freqHz < 520) band = ShazamBand.Hz250_520;
                            else if (freqHz >= 520 && freqHz < 1450) band = ShazamBand.Hz520_1450;
                            else if (freqHz >= 1450 && freqHz < 3500) band = ShazamBand.Hz1450_3500;
                            else if (freqHz >= 3500 && freqHz <= 5500) band = ShazamBand.Hz3500_5500;
                            else continue;

                            _bandPeaks[band].Add(new ShazamPeak
                            {
                                FftPassNumber = passNumber,
                                PeakMagnitude = (ushort)Math.Max(0, Math.Min(ushort.MaxValue, (int)peakMag)),
                                CorrectedFrequencyBin = correctedBin
                            });
                        }
                    }
                }
            }
        }

        private static byte[] EncodeToBinary(int numberSamples, Dictionary<ShazamBand, List<ShazamPeak>> bandPeaks)
        {
            using var payloadStream = new MemoryStream();
            using var payloadWriter = new BinaryWriter(payloadStream);

            // Pre-calculate band peaks data buffers
            var encodedBands = new Dictionary<ShazamBand, byte[]>();
            for (int b = 0; b < 4; b++)
            {
                var band = (ShazamBand)b;
                encodedBands[band] = EncodeBandPeaks(bandPeaks[band]);
            }

            int totalBandsSize = 0;
            for (int b = 0; b < 4; b++)
            {
                int len = encodedBands[(ShazamBand)b].Length;
                int pad = (4 - (len % 4)) % 4;
                totalBandsSize += 8 + len + pad;
            }

            uint sizeMinusHeader = (uint)(8 + totalBandsSize); // 8 for 0x40000000 chunk + bands

            // Write 0x40000000 chunk
            payloadWriter.Write(0x40000000u);
            payloadWriter.Write(sizeMinusHeader);

            uint[] bandIds = { 0x60030040u, 0x60030041u, 0x60030042u, 0x60030043u };

            for (int b = 0; b < 4; b++)
            {
                var band = (ShazamBand)b;
                byte[] bandData = encodedBands[band];
                int pad = (4 - (bandData.Length % 4)) % 4;

                payloadWriter.Write(bandIds[b]);
                payloadWriter.Write((uint)bandData.Length);
                payloadWriter.Write(bandData);
                for (int p = 0; p < pad; p++)
                {
                    payloadWriter.Write((byte)0);
                }
            }

            byte[] payloadBytes = payloadStream.ToArray();

            // Construct Header (48 bytes + 8 byte preamble)
            using var fullStream = new MemoryStream();
            using var writer = new BinaryWriter(fullStream);

            writer.Write(0xcafe2580u); // magic1
            writer.Write(0u); // CRC placeholder

            long headerStart = fullStream.Position;
            writer.Write(sizeMinusHeader); // size_minus_header
            writer.Write(0x94119c00u); // magic2
            writer.Write(0u); // void1[0]
            writer.Write(0u); // void1[1]
            writer.Write(0u); // void1[2]
            writer.Write(3u << 27); // shifted_sample_rate_id (16000Hz)
            writer.Write(0u); // void2[0]
            writer.Write(0u); // void2[1]

            uint numSamplesCalc = (uint)(numberSamples + (SampleRate * 0.24));
            writer.Write(numSamplesCalc);
            writer.Write((15u << 19) + 0x40000u); // fixed_value (0x007c0000)

            writer.Write(payloadBytes);

            byte[] fullBytes = fullStream.ToArray();

            // Calculate CRC32 of all bytes excluding first 8 bytes
            uint crc = CalculateCrc32(fullBytes, 8, fullBytes.Length - 8);

            fullStream.Position = 4;
            writer.Write(crc);

            return fullStream.ToArray();
        }

        private static byte[] EncodeBandPeaks(List<ShazamPeak> peaks)
        {
            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);

            uint lastPassNumber = 0;

            foreach (var peak in peaks)
            {
                uint delta = peak.FftPassNumber - lastPassNumber;
                if (delta >= 255)
                {
                    bw.Write((byte)0xFF);
                    bw.Write(peak.FftPassNumber);
                }
                else
                {
                    bw.Write((byte)delta);
                    bw.Write(peak.PeakMagnitude);
                    bw.Write(peak.CorrectedFrequencyBin);
                    lastPassNumber = peak.FftPassNumber;
                }
            }

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
