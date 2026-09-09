using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace UniversalDownloader.Services
{
    public enum VideoCompressionPreset
    {
        VisuallyLossless,
        Balanced,
        MaxCompression,
        TargetSize,
        Custom
    }

    public enum VideoEncodingDevice
    {
        Gpu, // Default - Hardware Acceleration (NVENC, AMF, QSV)
        Cpu  // Software Processing (libx264, libx265, libsvtav1)
    }

    public enum VideoCodec
    {
        H264,
        H265,
        AV1
    }

    public enum VideoAudioMode
    {
        Copy,
        Aac128,
        Aac192,
        Opus96,
        Mute
    }

    public class VideoCompressorOptions
    {
        public VideoCompressionPreset Preset { get; set; } = VideoCompressionPreset.VisuallyLossless;
        public VideoCodec Codec { get; set; } = VideoCodec.H264;
        public VideoEncodingDevice Device { get; set; } = VideoEncodingDevice.Gpu;
        public int Crf { get; set; } = 20;
        public double? TargetSizeMb { get; set; } = null;
        public string Resolution { get; set; } = "Original";
        public string Fps { get; set; } = "Original";
        public string EncoderPreset { get; set; } = "slow";
        public VideoAudioMode AudioMode { get; set; } = VideoAudioMode.Copy;

        public static VideoCompressorOptions CreateVisuallyLossless()
        {
            return new VideoCompressorOptions
            {
                Preset = VideoCompressionPreset.VisuallyLossless,
                Codec = VideoCodec.H264,
                Device = VideoEncodingDevice.Gpu,
                Crf = 20,
                EncoderPreset = "slow",
                Resolution = "Original",
                Fps = "Original",
                AudioMode = VideoAudioMode.Copy
            };
        }

        public static VideoCompressorOptions CreateBalanced()
        {
            return new VideoCompressorOptions
            {
                Preset = VideoCompressionPreset.Balanced,
                Codec = VideoCodec.H264,
                Device = VideoEncodingDevice.Gpu,
                Crf = 24,
                EncoderPreset = "medium",
                Resolution = "Original",
                Fps = "Original",
                AudioMode = VideoAudioMode.Aac128
            };
        }

        public static VideoCompressorOptions CreateMaxCompression()
        {
            return new VideoCompressorOptions
            {
                Preset = VideoCompressionPreset.MaxCompression,
                Codec = VideoCodec.H265,
                Device = VideoEncodingDevice.Gpu,
                Crf = 28,
                EncoderPreset = "slow",
                Resolution = "Original",
                Fps = "Original",
                AudioMode = VideoAudioMode.Aac128
            };
        }

        public static VideoCompressorOptions CreateTargetSize(double targetMb)
        {
            return new VideoCompressorOptions
            {
                Preset = VideoCompressionPreset.TargetSize,
                Codec = VideoCodec.H264,
                Device = VideoEncodingDevice.Gpu,
                TargetSizeMb = targetMb,
                EncoderPreset = "medium",
                Resolution = "Original",
                Fps = "Original",
                AudioMode = VideoAudioMode.Aac128
            };
        }
    }

    public class VideoCompressionProgress
    {
        public double Percentage { get; set; }
        public string StatusMessage { get; set; } = "";
        public string Speed { get; set; } = "";
        public TimeSpan CurrentTime { get; set; }
        public TimeSpan TotalDuration { get; set; }
        public TimeSpan? EstimatedRemainingTime { get; set; }
        public int Pass { get; set; } = 1;
        public int TotalPasses { get; set; } = 1;
    }

    public class VideoMediaInfo
    {
        public TimeSpan Duration { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public string VideoCodec { get; set; } = "";
        public string AudioCodec { get; set; } = "";
        public long SizeBytes { get; set; }
    }

    public class VideoCompressorService
    {
        private readonly DependencyManager _dependencyManager;

        public VideoCompressorService(DependencyManager dependencyManager)
        {
            _dependencyManager = dependencyManager;
        }

        public async Task<VideoMediaInfo> GetVideoInfoAsync(string filePath)
        {
            var info = new VideoMediaInfo();
            if (!File.Exists(filePath)) return info;

            try
            {
                var fileInfo = new FileInfo(filePath);
                info.SizeBytes = fileInfo.Length;

                if (!_dependencyManager.IsFfmpegReady) return info;

                var psi = new ProcessStartInfo
                {
                    FileName = _dependencyManager.FfmpegExecutablePath,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                psi.ArgumentList.Add("-hide_banner");
                psi.ArgumentList.Add("-i");
                psi.ArgumentList.Add(filePath);

                using var proc = Process.Start(psi);
                if (proc == null) return info;

                string output = await proc.StandardError.ReadToEndAsync();
                await proc.WaitForExitAsync();

                // Parse duration
                var durMatch = Regex.Match(output, @"Duration:\s*(\d{2}):(\d{2}):(\d{2}\.\d+)");
                if (durMatch.Success)
                {
                    int hours = int.Parse(durMatch.Groups[1].Value);
                    int mins = int.Parse(durMatch.Groups[2].Value);
                    double secs = double.Parse(durMatch.Groups[3].Value, CultureInfo.InvariantCulture);
                    info.Duration = TimeSpan.FromSeconds(hours * 3600 + mins * 60 + secs);
                }

                // Parse dimensions (e.g. 1920x1080)
                var dimMatch = Regex.Match(output, @"Stream.*Video:.*,\s*(\d{3,5})x(\d{3,5})");
                if (dimMatch.Success)
                {
                    info.Width = int.Parse(dimMatch.Groups[1].Value);
                    info.Height = int.Parse(dimMatch.Groups[2].Value);
                }

                // Parse video codec
                var vCodecMatch = Regex.Match(output, @"Stream.*Video:\s*([a-zA-Z0-9_\-]+)");
                if (vCodecMatch.Success)
                {
                    info.VideoCodec = vCodecMatch.Groups[1].Value;
                }

                // Parse audio codec
                var aCodecMatch = Regex.Match(output, @"Stream.*Audio:\s*([a-zA-Z0-9_\-]+)");
                if (aCodecMatch.Success)
                {
                    info.AudioCodec = aCodecMatch.Groups[1].Value;
                }
            }
            catch { }

            return info;
        }

        public static (int videoBitrateKbps, int audioBitrateKbps) CalculateTargetBitrates(double targetSizeMb, TimeSpan duration, VideoAudioMode audioMode)
        {
            if (duration.TotalSeconds <= 0)
            {
                return (1500, 128);
            }

            // 5% safety margin for MP4/MKV container overhead (atoms, headers, timestamps)
            double totalBits = targetSizeMb * 8192.0 * 0.95;
            double totalBitrateKbps = totalBits / duration.TotalSeconds;

            int audioBitrateKbps = audioMode switch
            {
                VideoAudioMode.Mute => 0,
                VideoAudioMode.Opus96 => 96,
                VideoAudioMode.Aac128 => 128,
                VideoAudioMode.Aac192 => 192,
                VideoAudioMode.Copy => 128, // estimate allocation for passthrough
                _ => 128
            };

            // If file is very short or target size is tight, ensure video gets at least 64 kbps
            int videoBitrateKbps = Math.Max(64, (int)(totalBitrateKbps - audioBitrateKbps));
            if (videoBitrateKbps < 100 && audioBitrateKbps > 64 && audioMode != VideoAudioMode.Copy)
            {
                audioBitrateKbps = 64;
                videoBitrateKbps = Math.Max(48, (int)(totalBitrateKbps - audioBitrateKbps));
            }

            return (videoBitrateKbps, audioBitrateKbps);
        }

        public static List<string> BuildFfmpegArguments(
            string inputFilePath,
            string outputFilePath,
            VideoCompressorOptions options,
            TimeSpan duration,
            int pass = 0,
            string? passLogFile = null,
            string? encoderOverride = null)
        {
            var args = new List<string>
            {
                "-y",
                "-hide_banner",
                "-i", inputFilePath
            };

            // Resolution downscaling filter
            string scaleFilter = options.Resolution switch
            {
                "1080p" => "scale=-2:1080",
                "720p" => "scale=-2:720",
                "480p" => "scale=-2:480",
                "360p" => "scale=-2:360",
                _ => ""
            };

            if (!string.IsNullOrEmpty(scaleFilter))
            {
                args.Add("-vf");
                args.Add(scaleFilter);
            }

            // Frame rate cap
            if (options.Fps is "60" or "30" or "24")
            {
                args.Add("-r");
                args.Add(options.Fps);
            }

            // Video codec
            string vcodec = encoderOverride ?? (options.Codec switch
            {
                VideoCodec.H265 => "libx265",
                VideoCodec.AV1 => "libsvtav1",
                _ => "libx264"
            });

            args.Add("-c:v");
            args.Add(vcodec);

            bool isNvenc = vcodec.EndsWith("_nvenc", StringComparison.OrdinalIgnoreCase);
            bool isAmf = vcodec.EndsWith("_amf", StringComparison.OrdinalIgnoreCase);
            bool isQsv = vcodec.EndsWith("_qsv", StringComparison.OrdinalIgnoreCase);
            bool isVideoToolbox = vcodec.EndsWith("_videotoolbox", StringComparison.OrdinalIgnoreCase);

            string speedPreset = string.IsNullOrWhiteSpace(options.EncoderPreset) ? "slow" : options.EncoderPreset.ToLowerInvariant();

            if (isNvenc)
            {
                string nvencPreset = speedPreset switch
                {
                    "veryslow" => "p7",
                    "slow" => "p5",
                    "fast" => "p2",
                    _ => "p4"
                };
                args.Add("-preset");
                args.Add(nvencPreset);
            }
            else if (!isAmf && !isVideoToolbox)
            {
                args.Add("-preset");
                args.Add(speedPreset);
            }

            bool isTargetSize = options.Preset == VideoCompressionPreset.TargetSize || (options.TargetSizeMb.HasValue && options.TargetSizeMb.Value > 0);

            if (isTargetSize && options.TargetSizeMb.HasValue)
            {
                var (videoBitrate, audioBitrate) = CalculateTargetBitrates(options.TargetSizeMb.Value, duration, options.AudioMode);

                args.Add("-b:v");
                args.Add($"{videoBitrate}k");
                args.Add("-maxrate");
                args.Add($"{(int)(videoBitrate * 1.5)}k");
                args.Add("-bufsize");
                args.Add($"{videoBitrate * 2}k");

                if (pass == 1)
                {
                    args.Add("-pass");
                    args.Add("1");
                    if (!string.IsNullOrEmpty(passLogFile))
                    {
                        args.Add("-passlogfile");
                        args.Add(passLogFile);
                    }
                    args.Add("-an");
                    args.Add("-f");
                    args.Add("null");
                    args.Add(OperatingSystem.IsWindows() ? "NUL" : "/dev/null");
                    return args;
                }
                else if (pass == 2)
                {
                    args.Add("-pass");
                    args.Add("2");
                    if (!string.IsNullOrEmpty(passLogFile))
                    {
                        args.Add("-passlogfile");
                        args.Add(passLogFile);
                    }
                }
            }
            else
            {
                // CRF based encoding
                int crf = options.Crf;
                if (options.Preset == VideoCompressionPreset.VisuallyLossless)
                {
                    crf = options.Codec == VideoCodec.H265 ? 24 : (options.Codec == VideoCodec.AV1 ? 26 : 20);
                }
                else if (options.Preset == VideoCompressionPreset.Balanced)
                {
                    crf = options.Codec == VideoCodec.H265 ? 28 : (options.Codec == VideoCodec.AV1 ? 30 : 24);
                }
                else if (options.Preset == VideoCompressionPreset.MaxCompression)
                {
                    crf = options.Codec == VideoCodec.H265 ? 32 : (options.Codec == VideoCodec.AV1 ? 34 : 28);
                }

                if (isNvenc)
                {
                    args.Add("-rc:v");
                    args.Add("vbr");
                    args.Add("-cq");
                    args.Add(crf.ToString());
                    args.Add("-b:v");
                    args.Add("0");
                }
                else if (isQsv)
                {
                    args.Add("-global_quality");
                    args.Add(crf.ToString());
                }
                else if (isAmf)
                {
                    args.Add("-rc");
                    args.Add("cqp");
                    args.Add("-qp_i");
                    args.Add(crf.ToString());
                    args.Add("-qp_p");
                    args.Add(crf.ToString());
                }
                else if (isVideoToolbox)
                {
                    args.Add("-q:v");
                    args.Add(crf.ToString());
                }
                else
                {
                    args.Add("-crf");
                    args.Add(crf.ToString());
                }
            }

            // Audio configuration
            switch (options.AudioMode)
            {
                case VideoAudioMode.Mute:
                    args.Add("-an");
                    break;
                case VideoAudioMode.Aac128:
                    args.Add("-c:a");
                    args.Add("aac");
                    args.Add("-b:a");
                    args.Add("128k");
                    break;
                case VideoAudioMode.Aac192:
                    args.Add("-c:a");
                    args.Add("aac");
                    args.Add("-b:a");
                    args.Add("192k");
                    break;
                case VideoAudioMode.Opus96:
                    args.Add("-c:a");
                    args.Add("libopus");
                    args.Add("-b:a");
                    args.Add("96k");
                    break;
                case VideoAudioMode.Copy:
                default:
                    args.Add("-c:a");
                    args.Add("copy");
                    break;
            }

            // Faststart container optimization for instant streaming/playback
            args.Add("-movflags");
            args.Add("+faststart");

            args.Add(outputFilePath);
            return args;
        }

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> _hwEncoderCache = new(StringComparer.OrdinalIgnoreCase);

        public async Task<bool> IsHardwareEncoderSupportedAsync(string encoderName)
        {
            if (_hwEncoderCache.TryGetValue(encoderName, out bool cached))
            {
                return cached;
            }

            if (!_dependencyManager.IsFfmpegReady) return false;

            try
            {
                string nullOut = OperatingSystem.IsWindows() ? "NUL" : "/dev/null";
                var psi = new ProcessStartInfo
                {
                    FileName = _dependencyManager.FfmpegExecutablePath,
                    Arguments = $"-hide_banner -f lavfi -i nullsrc=s=256x256:d=0.04 -c:v {encoderName} -f null {nullOut}",
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var proc = Process.Start(psi);
                if (proc == null) return false;

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
                await proc.WaitForExitAsync(cts.Token);
                bool supported = proc.ExitCode == 0;
                _hwEncoderCache[encoderName] = supported;
                return supported;
            }
            catch
            {
                _hwEncoderCache[encoderName] = false;
                return false;
            }
        }

        public async Task<string> ResolveVideoEncoderAsync(VideoCompressorOptions options)
        {
            if (options.Device == VideoEncodingDevice.Cpu)
            {
                return options.Codec switch
                {
                    VideoCodec.H265 => "libx265",
                    VideoCodec.AV1 => "libsvtav1",
                    _ => "libx264"
                };
            }

            string[] candidates = options.Codec switch
            {
                VideoCodec.H265 => new[] { "hevc_nvenc", "hevc_amf", "hevc_qsv", "hevc_videotoolbox" },
                VideoCodec.AV1 => new[] { "av1_nvenc", "av1_amf", "av1_qsv" },
                _ => new[] { "h264_nvenc", "h264_amf", "h264_qsv", "h264_videotoolbox" }
            };

            foreach (var candidate in candidates)
            {
                if (await IsHardwareEncoderSupportedAsync(candidate))
                {
                    return candidate;
                }
            }

            // Fallback to CPU software encoder if no hardware encoder was capable
            return options.Codec switch
            {
                VideoCodec.H265 => "libx265",
                VideoCodec.AV1 => "libsvtav1",
                _ => "libx264"
            };
        }

        public static string FormatDurationShort(TimeSpan time)
        {
            if (time.TotalSeconds < 1) return "0s";
            if (time.TotalHours >= 1) return $"{(int)time.TotalHours}h {time.Minutes:D2}m";
            if (time.TotalMinutes >= 1) return $"{(int)time.TotalMinutes}m {time.Seconds:D2}s";
            return $"{(int)time.TotalSeconds}s";
        }

        public async Task<bool> CompressVideoAsync(
            string inputFilePath,
            string outputFilePath,
            VideoCompressorOptions options,
            IProgress<VideoCompressionProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (!_dependencyManager.IsFfmpegReady || !File.Exists(inputFilePath))
            {
                throw new FileNotFoundException("Input video file or FFmpeg binary was not found.");
            }

            string? outputDir = Path.GetDirectoryName(outputFilePath);
            if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }

            var mediaInfo = await GetVideoInfoAsync(inputFilePath);
            TimeSpan duration = mediaInfo.Duration;

            string encoder = await ResolveVideoEncoderAsync(options);
            bool success = false;
            try
            {
                success = await RunCompressionInternalAsync(inputFilePath, outputFilePath, options, duration, encoder, progress, cancellationToken);

                // If GPU encoder failed, automatically retry with CPU fallback
                if (!success && encoder != "libx264" && encoder != "libx265" && encoder != "libsvtav1" && !cancellationToken.IsCancellationRequested)
                {
                    _hwEncoderCache[encoder] = false;
                    string fallbackCpu = options.Codec switch
                    {
                        VideoCodec.H265 => "libx265",
                        VideoCodec.AV1 => "libsvtav1",
                        _ => "libx264"
                    };
                    success = await RunCompressionInternalAsync(inputFilePath, outputFilePath, options, duration, fallbackCpu, progress, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                try
                {
                    if (File.Exists(outputFilePath))
                    {
                        File.Delete(outputFilePath);
                    }
                }
                catch { }
                throw;
            }

            return success && File.Exists(outputFilePath);
        }

        private async Task<bool> RunCompressionInternalAsync(
            string inputFilePath,
            string outputFilePath,
            VideoCompressorOptions options,
            TimeSpan duration,
            string encoder,
            IProgress<VideoCompressionProgress>? progress,
            CancellationToken cancellationToken)
        {
            bool isTargetSize = options.Preset == VideoCompressionPreset.TargetSize || (options.TargetSizeMb.HasValue && options.TargetSizeMb.Value > 0);

            if (isTargetSize && options.TargetSizeMb.HasValue && duration.TotalSeconds > 0)
            {
                // 2-pass encoding
                string tempLogPrefix = Path.Combine(Path.GetTempPath(), $"ffmpeg2pass_{Guid.NewGuid():N}");

                try
                {
                    // Pass 1
                    var pass1Args = BuildFfmpegArguments(inputFilePath, outputFilePath, options, duration, pass: 1, passLogFile: tempLogPrefix, encoderOverride: encoder);
                    bool pass1Success = await ExecuteFfmpegCommandAsync(pass1Args, duration, 1, 2, progress, cancellationToken);
                    if (!pass1Success) return false;

                    // Pass 2
                    var pass2Args = BuildFfmpegArguments(inputFilePath, outputFilePath, options, duration, pass: 2, passLogFile: tempLogPrefix, encoderOverride: encoder);
                    bool pass2Success = await ExecuteFfmpegCommandAsync(pass2Args, duration, 2, 2, progress, cancellationToken);
                    return pass2Success && File.Exists(outputFilePath);
                }
                finally
                {
                    try
                    {
                        string log1 = tempLogPrefix + "-0.log";
                        string log2 = tempLogPrefix + "-0.log.mbtree";
                        if (File.Exists(log1)) File.Delete(log1);
                        if (File.Exists(log2)) File.Delete(log2);
                    }
                    catch { }
                }
            }
            else
            {
                // Single-pass CRF encoding
                var args = BuildFfmpegArguments(inputFilePath, outputFilePath, options, duration, pass: 0, encoderOverride: encoder);
                bool success = await ExecuteFfmpegCommandAsync(args, duration, 1, 1, progress, cancellationToken);
                return success && File.Exists(outputFilePath);
            }
        }

        private readonly object _processLock = new();
        private Process? _currentProcess;

        public void CancelCurrentProcess()
        {
            lock (_processLock)
            {
                if (_currentProcess != null)
                {
                    ForceKillProcess(_currentProcess);
                }
            }
        }

        private static void ForceKillProcess(Process process)
        {
            try
            {
                if (!process.HasExited)
                {
                    int pid = 0;
                    try { pid = process.Id; } catch { }

                    try
                    {
                        process.Kill(entireProcessTree: true);
                    }
                    catch { }

                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && pid > 0)
                    {
                        try
                        {
                            if (!process.HasExited)
                            {
                                using var killer = Process.Start(new ProcessStartInfo
                                {
                                    FileName = "taskkill",
                                    Arguments = $"/F /T /PID {pid}",
                                    CreateNoWindow = true,
                                    UseShellExecute = false
                                });
                                killer?.WaitForExit(1000);
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }
        }

        private async Task<bool> ExecuteFfmpegCommandAsync(
            List<string> arguments,
            TimeSpan totalDuration,
            int currentPass,
            int totalPasses,
            IProgress<VideoCompressionProgress>? progress,
            CancellationToken cancellationToken)
        {
            var psi = new ProcessStartInfo
            {
                FileName = _dependencyManager.FfmpegExecutablePath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                StandardErrorEncoding = System.Text.Encoding.UTF8
            };

            foreach (var arg in arguments)
            {
                psi.ArgumentList.Add(arg);
            }

            using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

            using var reg = cancellationToken.Register(() =>
            {
                ForceKillProcess(process);
            });

            lock (_processLock)
            {
                _currentProcess = process;
            }

            var passWatch = Stopwatch.StartNew();

            process.ErrorDataReceived += (s, e) =>
            {
                if (string.IsNullOrEmpty(e.Data)) return;

                var timeMatch = Regex.Match(e.Data, @"time=(\d{2}):(\d{2}):(\d{2}\.\d+)");
                var speedMatch = Regex.Match(e.Data, @"speed=\s*([\d\.]+)x");

                if (timeMatch.Success && totalDuration.TotalSeconds > 0)
                {
                    int hours = int.Parse(timeMatch.Groups[1].Value);
                    int mins = int.Parse(timeMatch.Groups[2].Value);
                    double secs = double.Parse(timeMatch.Groups[3].Value, CultureInfo.InvariantCulture);

                    TimeSpan currentTime = TimeSpan.FromSeconds(hours * 3600 + mins * 60 + secs);
                    double passFraction = Math.Min(1.0, currentTime.TotalSeconds / totalDuration.TotalSeconds);

                    double overallPct;
                    if (totalPasses == 2)
                    {
                        overallPct = (currentPass == 1)
                            ? (passFraction * 50.0)
                            : (50.0 + passFraction * 50.0);
                    }
                    else
                    {
                        overallPct = passFraction * 100.0;
                    }

                    string speed = speedMatch.Success ? $"{speedMatch.Groups[1].Value}x" : "";
                    string passInfo = totalPasses > 1 ? $"[Pass {currentPass}/{totalPasses}] " : "";

                    TimeSpan? estRemaining = null;
                    if (passWatch.Elapsed.TotalSeconds > 1.5 && overallPct > 1.0 && overallPct < 99.5)
                    {
                        double totalEstSec = passWatch.Elapsed.TotalSeconds / (overallPct / 100.0);
                        double remSec = Math.Max(0, totalEstSec - passWatch.Elapsed.TotalSeconds);
                        estRemaining = TimeSpan.FromSeconds(remSec);
                    }
                    else if (speedMatch.Success && double.TryParse(speedMatch.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double spdVal) && spdVal > 0.05)
                    {
                        double remainingSec = Math.Max(0, totalDuration.TotalSeconds - currentTime.TotalSeconds) / spdVal;
                        estRemaining = TimeSpan.FromSeconds(remainingSec);
                    }

                    string etaStr = estRemaining.HasValue ? $" • {FormatDurationShort(estRemaining.Value)} left" : "";

                    progress?.Report(new VideoCompressionProgress
                    {
                        Percentage = overallPct,
                        CurrentTime = currentTime,
                        TotalDuration = totalDuration,
                        EstimatedRemainingTime = estRemaining,
                        Speed = speed,
                        Pass = currentPass,
                        TotalPasses = totalPasses,
                        StatusMessage = $"{passInfo}Compressing... {overallPct:F0}% {(!string.IsNullOrEmpty(speed) ? $"({speed})" : "")}{etaStr}"
                    });
                }
            };

            process.Start();
            process.BeginErrorReadLine();

            try
            {
                await process.WaitForExitAsync(cancellationToken);
                return process.ExitCode == 0;
            }
            catch (OperationCanceledException)
            {
                ForceKillProcess(process);
                throw;
            }
            finally
            {
                lock (_processLock)
                {
                    if (_currentProcess == process)
                    {
                        _currentProcess = null;
                    }
                }
            }
        }
    }
}
