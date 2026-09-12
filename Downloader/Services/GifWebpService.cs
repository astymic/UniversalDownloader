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
    public enum GifWebpFormat
    {
        Gif,
        Webp
    }

    public enum GifWebpPreset
    {
        MaxQuality,
        Balanced,
        MaxCompression,
        TargetSize,
        Custom
    }

    public enum GifDitherMode
    {
        Sierra2_4a,
        Bayer,
        FloydSteinberg,
        None
    }

    public class GifWebpOptions
    {
        public GifWebpFormat Format { get; set; } = GifWebpFormat.Gif;
        public GifWebpPreset Preset { get; set; } = GifWebpPreset.MaxQuality;
        public TimeSpan StartTime { get; set; } = TimeSpan.Zero;
        public TimeSpan EndTime { get; set; } = TimeSpan.FromSeconds(5);
        public int Fps { get; set; } = 24;
        public int Width { get; set; } = 0; // 0 = original width
        public double SpeedMultiplier { get; set; } = 1.0;
        public int LoopCount { get; set; } = 0; // 0 = infinite, -1 = once
        public int MaxColors { get; set; } = 256;
        public GifDitherMode Dither { get; set; } = GifDitherMode.Sierra2_4a;
        public int WebpQuality { get; set; } = 80; // 1-100
        public bool WebpLossless { get; set; } = false;
        public double? TargetSizeMb { get; set; } = null;

        public TimeSpan ClipDuration => EndTime > StartTime ? EndTime - StartTime : TimeSpan.FromSeconds(1);

        public static GifWebpOptions CreateMaxQuality(GifWebpFormat format = GifWebpFormat.Gif)
        {
            return new GifWebpOptions
            {
                Format = format,
                Preset = GifWebpPreset.MaxQuality,
                Fps = 30,
                Width = 0,
                SpeedMultiplier = 1.0,
                LoopCount = 0,
                MaxColors = 256,
                Dither = GifDitherMode.Sierra2_4a,
                WebpQuality = 85,
                WebpLossless = false
            };
        }

        public static GifWebpOptions CreateBalanced(GifWebpFormat format = GifWebpFormat.Gif)
        {
            return new GifWebpOptions
            {
                Format = format,
                Preset = GifWebpPreset.Balanced,
                Fps = 24,
                Width = 720,
                SpeedMultiplier = 1.0,
                LoopCount = 0,
                MaxColors = 256,
                Dither = GifDitherMode.Bayer,
                WebpQuality = 70,
                WebpLossless = false
            };
        }

        public static GifWebpOptions CreateMaxCompression(GifWebpFormat format = GifWebpFormat.Gif)
        {
            return new GifWebpOptions
            {
                Format = format,
                Preset = GifWebpPreset.MaxCompression,
                Fps = 15,
                Width = 320,
                SpeedMultiplier = 1.0,
                LoopCount = 0,
                MaxColors = 128,
                Dither = GifDitherMode.Bayer,
                WebpQuality = 40,
                WebpLossless = false
            };
        }

        public static GifWebpOptions CreateTargetSize(double targetMb, GifWebpFormat format = GifWebpFormat.Gif)
        {
            return new GifWebpOptions
            {
                Format = format,
                Preset = GifWebpPreset.TargetSize,
                TargetSizeMb = targetMb,
                Fps = 20,
                Width = 480,
                SpeedMultiplier = 1.0,
                LoopCount = 0,
                MaxColors = 192,
                Dither = GifDitherMode.Bayer,
                WebpQuality = 60,
                WebpLossless = false
            };
        }
    }

    public class GifWebpProgress
    {
        public double Percentage { get; set; }
        public string StatusMessage { get; set; } = "";
        public TimeSpan CurrentTime { get; set; }
        public TimeSpan TotalDuration { get; set; }
        public TimeSpan ElapsedTime { get; set; }
        public TimeSpan? EstimatedRemainingTime { get; set; }
    }

    public class GifWebpResult
    {
        public bool Success { get; set; }
        public bool IsCancelled { get; set; }
        public string OutputPath { get; set; } = "";
        public long OutputSizeBytes { get; set; }
        public string FormattedSize { get; set; } = "";
        public TimeSpan Duration { get; set; }
        public string? ErrorMessage { get; set; }
    }

    public class GifWebpMediaInfo
    {
        public TimeSpan Duration { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public double Fps { get; set; } = 30;
        public long SizeBytes { get; set; }
    }

    public class GifWebpService
    {
        private readonly DependencyManager _dependencyManager;
        private Process? _currentProcess;
        private readonly object _processLock = new();

        public GifWebpService(DependencyManager dependencyManager)
        {
            _dependencyManager = dependencyManager;
        }

        public async Task<GifWebpMediaInfo> GetMediaInfoAsync(string filePath)
        {
            var info = new GifWebpMediaInfo();
            if (!File.Exists(filePath)) return info;

            try
            {
                var fi = new FileInfo(filePath);
                info.SizeBytes = fi.Length;

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

                // Duration: 00:01:23.45
                var durMatch = Regex.Match(output, @"Duration:\s*(\d{2}):(\d{2}):(\d{2}\.\d+)");
                if (durMatch.Success)
                {
                    int hours = int.Parse(durMatch.Groups[1].Value);
                    int mins = int.Parse(durMatch.Groups[2].Value);
                    double secs = double.Parse(durMatch.Groups[3].Value, CultureInfo.InvariantCulture);
                    info.Duration = TimeSpan.FromSeconds(hours * 3600 + mins * 60 + secs);
                }

                // Resolution: 1920x1080
                var dimMatch = Regex.Match(output, @"Stream.*Video:.*,\s*(\d{3,5})x(\d{3,5})");
                if (dimMatch.Success)
                {
                    info.Width = int.Parse(dimMatch.Groups[1].Value);
                    info.Height = int.Parse(dimMatch.Groups[2].Value);
                }

                // FPS: 29.97 fps or 30 fps
                var fpsMatch = Regex.Match(output, @"(\d+(?:\.\d+)?)\s*fps");
                if (fpsMatch.Success && double.TryParse(fpsMatch.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double parsedFps))
                {
                    info.Fps = parsedFps;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GifWebpService] Error probing media: {ex.Message}");
            }

            return info;
        }

        public static List<string> BuildFfmpegArguments(string inputPath, string outputPath, GifWebpOptions options)
        {
            var args = new List<string>
            {
                "-hide_banner",
                "-loglevel", "info"
            };

            // Precision seeking before input
            if (options.StartTime > TimeSpan.Zero)
            {
                args.Add("-ss");
                args.Add(options.StartTime.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture));
            }

            if (options.EndTime > options.StartTime)
            {
                args.Add("-to");
                args.Add(options.EndTime.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture));
            }

            args.Add("-i");
            args.Add(inputPath);

            // Compute scaling filter string
            string scaleFilter;
            if (options.Width > 0)
            {
                scaleFilter = $"scale={options.Width}:-2:flags=lanczos";
            }
            else
            {
                // Ensure even dimensions
                scaleFilter = "scale=trunc(iw/2)*2:trunc(ih/2)*2:flags=lanczos";
            }

            // Speed adjustment filter if needed
            string speedFilter = "";
            if (Math.Abs(options.SpeedMultiplier - 1.0) > 0.01 && options.SpeedMultiplier > 0.05)
            {
                double ptsFactor = 1.0 / options.SpeedMultiplier;
                speedFilter = $"setpts={ptsFactor.ToString("0.###", CultureInfo.InvariantCulture)}*PTS,";
            }

            int fps = options.Fps > 0 ? options.Fps : 24;

            if (options.Format == GifWebpFormat.Gif)
            {
                // Dithering option for paletteuse
                string dither = options.Dither switch
                {
                    GifDitherMode.Bayer => "bayer:bayer_scale=3",
                    GifDitherMode.FloydSteinberg => "floyd_steinberg",
                    GifDitherMode.None => "none",
                    _ => "sierra2_4a"
                };

                int colors = Math.Clamp(options.MaxColors, 16, 256);

                // Complex filter combining split, palettegen and paletteuse in single pass
                string filterComplex = $"[0:v] {speedFilter}fps={fps},{scaleFilter},split [a][b];[a] palettegen=max_colors={colors}:stats_mode=diff [p];[b][p] paletteuse=dither={dither}";

                args.Add("-filter_complex");
                args.Add(filterComplex);

                // Loop count: 0 = infinite, -1 = play once, >0 = count
                args.Add("-loop");
                args.Add(options.LoopCount.ToString());
            }
            else // WebP
            {
                string filterComplex = $"[0:v] {speedFilter}fps={fps},{scaleFilter}";
                args.Add("-filter_complex");
                args.Add(filterComplex);

                args.Add("-vcodec");
                args.Add("libwebp");

                if (options.WebpLossless)
                {
                    args.Add("-lossless");
                    args.Add("1");
                    args.Add("-compression_level");
                    args.Add("6");
                }
                else
                {
                    args.Add("-lossless");
                    args.Add("0");
                    args.Add("-q:v");
                    int q = Math.Clamp(options.WebpQuality, 1, 100);
                    args.Add(q.ToString());
                    args.Add("-compression_level");
                    args.Add("5");
                }

                args.Add("-loop");
                args.Add(options.LoopCount.ToString());
            }

            args.Add("-y");
            args.Add(outputPath);

            return args;
        }

        public async Task<GifWebpResult> CreateClipAsync(
            string inputPath,
            string outputPath,
            GifWebpOptions options,
            IProgress<GifWebpProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var result = new GifWebpResult();
            var startTime = DateTime.Now;

            if (cancellationToken.IsCancellationRequested)
            {
                result.IsCancelled = true;
                return result;
            }

            if (!File.Exists(inputPath))
            {
                result.ErrorMessage = $"Input file '{inputPath}' does not exist.";
                return result;
            }

            if (!_dependencyManager.IsFfmpegReady)
            {
                result.ErrorMessage = "FFmpeg is not available. Please verify dependencies.";
                return result;
            }

            // Adjust parameters if TargetSize preset is active
            if (options.Preset == GifWebpPreset.TargetSize && options.TargetSizeMb.HasValue && options.TargetSizeMb.Value > 0)
            {
                ApplyTargetSizeTuning(options);
            }

            string? targetDirectory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(targetDirectory) && !Directory.Exists(targetDirectory))
            {
                Directory.CreateDirectory(targetDirectory);
            }

            var args = BuildFfmpegArguments(inputPath, outputPath, options);

            var psi = new ProcessStartInfo
            {
                FileName = _dependencyManager.FfmpegExecutablePath,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            foreach (var a in args)
            {
                psi.ArgumentList.Add(a);
            }

            TimeSpan clipDuration = options.ClipDuration;
            if (options.SpeedMultiplier > 0.05)
            {
                clipDuration = TimeSpan.FromSeconds(clipDuration.TotalSeconds / options.SpeedMultiplier);
            }

            try
            {
                lock (_processLock)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    _currentProcess = Process.Start(psi);
                }

                if (_currentProcess == null)
                {
                    result.ErrorMessage = "Failed to launch FFmpeg process.";
                    return result;
                }

                var proc = _currentProcess;

                using (cancellationToken.Register(() =>
                {
                    CancelCurrentProcess();
                }))
                {
                    string? line;
                    while ((line = await proc.StandardError.ReadLineAsync(cancellationToken)) != null)
                    {
                        if (cancellationToken.IsCancellationRequested) break;

                        // Parse time progress: time=00:00:03.45
                        var timeMatch = Regex.Match(line, @"time=\s*(\d{2}):(\d{2}):(\d{2}\.\d+)");
                        if (timeMatch.Success)
                        {
                            int h = int.Parse(timeMatch.Groups[1].Value);
                            int m = int.Parse(timeMatch.Groups[2].Value);
                            double s = double.Parse(timeMatch.Groups[3].Value, CultureInfo.InvariantCulture);
                            var currentProgressTime = TimeSpan.FromSeconds(h * 3600 + m * 60 + s);

                            double pct = 0;
                            if (clipDuration.TotalSeconds > 0)
                            {
                                pct = Math.Min(99.0, (currentProgressTime.TotalSeconds / clipDuration.TotalSeconds) * 100.0);
                            }

                            var elapsed = DateTime.Now - startTime;
                            TimeSpan? remaining = null;
                            if (pct > 2 && elapsed.TotalSeconds > 1)
                            {
                                double totalSec = elapsed.TotalSeconds / (pct / 100.0);
                                remaining = TimeSpan.FromSeconds(Math.Max(0, totalSec - elapsed.TotalSeconds));
                            }

                            progress?.Report(new GifWebpProgress
                            {
                                Percentage = pct,
                                StatusMessage = options.Format == GifWebpFormat.Gif ? "Encoding GIF frames..." : "Encoding WebP frames...",
                                CurrentTime = currentProgressTime,
                                TotalDuration = clipDuration,
                                ElapsedTime = elapsed,
                                EstimatedRemainingTime = remaining
                            });
                        }
                    }

                    await proc.WaitForExitAsync(cancellationToken);
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    try { if (File.Exists(outputPath)) File.Delete(outputPath); } catch { }
                    result.IsCancelled = true;
                    result.ErrorMessage = null;
                    return result;
                }

                if (proc.ExitCode == 0 && File.Exists(outputPath))
                {
                    var fi = new FileInfo(outputPath);
                    result.Success = true;
                    result.OutputPath = outputPath;
                    result.OutputSizeBytes = fi.Length;
                    result.FormattedSize = Utilities.FormatBytesOutput(fi.Length);
                    result.Duration = DateTime.Now - startTime;

                    progress?.Report(new GifWebpProgress
                    {
                        Percentage = 100.0,
                        StatusMessage = "Completed",
                        CurrentTime = clipDuration,
                        TotalDuration = clipDuration,
                        ElapsedTime = result.Duration,
                        EstimatedRemainingTime = TimeSpan.Zero
                    });

                    return result;
                }
                else
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        try { if (File.Exists(outputPath)) File.Delete(outputPath); } catch { }
                        result.IsCancelled = true;
                        result.ErrorMessage = null;
                        return result;
                    }

                    result.ErrorMessage = $"FFmpeg exited with error code {proc.ExitCode}.";
                    return result;
                }
            }
            catch (OperationCanceledException)
            {
                try { if (File.Exists(outputPath)) File.Delete(outputPath); } catch { }
                result.IsCancelled = true;
                result.ErrorMessage = null;
                return result;
            }
            catch (Exception ex)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    try { if (File.Exists(outputPath)) File.Delete(outputPath); } catch { }
                    result.IsCancelled = true;
                    result.ErrorMessage = null;
                    return result;
                }

                result.ErrorMessage = $"Error during encoding: {ex.Message}";
                return result;
            }
            finally
            {
                lock (_processLock)
                {
                    _currentProcess = null;
                }
            }
        }

        public void CancelCurrentProcess()
        {
            lock (_processLock)
            {
                if (_currentProcess != null && !_currentProcess.HasExited)
                {
                    try
                    {
                        KillProcessTree(_currentProcess.Id);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[GifWebpService] Kill process exception: {ex.Message}");
                    }
                }
            }
        }

        private static void KillProcessTree(int processId)
        {
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "taskkill",
                        Arguments = $"/F /T /PID {processId}",
                        CreateNoWindow = true,
                        UseShellExecute = false
                    };
                    using var p = Process.Start(psi);
                    p?.WaitForExit(1500);
                }
                else
                {
                    var proc = Process.GetProcessById(processId);
                    proc.Kill();
                }
            }
            catch { }
        }

        public static void ApplyTargetSizeTuning(GifWebpOptions options)
        {
            if (!options.TargetSizeMb.HasValue || options.TargetSizeMb.Value <= 0) return;

            double targetBytes = options.TargetSizeMb.Value * 1024 * 1024 * 0.88; // 12% safety margin
            double durationSec = Math.Max(0.5, options.ClipDuration.TotalSeconds / Math.Max(0.1, options.SpeedMultiplier));
            double bytesPerSec = targetBytes / durationSec;

            if (options.Format == GifWebpFormat.Gif)
            {
                // Tune GIF parameters according to allowed bytes per second
                if (bytesPerSec > 2_000_000) // > 2MB/s allowed
                {
                    options.Width = 640;
                    options.Fps = 24;
                    options.MaxColors = 256;
                    options.Dither = GifDitherMode.Sierra2_4a;
                }
                else if (bytesPerSec > 1_000_000) // ~1-2 MB/s
                {
                    options.Width = 480;
                    options.Fps = 20;
                    options.MaxColors = 192;
                    options.Dither = GifDitherMode.Bayer;
                }
                else if (bytesPerSec > 500_000) // ~500KB - 1MB/s
                {
                    options.Width = 360;
                    options.Fps = 15;
                    options.MaxColors = 128;
                    options.Dither = GifDitherMode.Bayer;
                }
                else // < 500 KB/s - aggressive compaction
                {
                    options.Width = 256;
                    options.Fps = 12;
                    options.MaxColors = 64;
                    options.Dither = GifDitherMode.Bayer;
                }
            }
            else // WebP
            {
                if (bytesPerSec > 2_000_000)
                {
                    options.Width = 720;
                    options.Fps = 30;
                    options.WebpQuality = 80;
                }
                else if (bytesPerSec > 1_000_000)
                {
                    options.Width = 640;
                    options.Fps = 24;
                    options.WebpQuality = 65;
                }
                else if (bytesPerSec > 500_000)
                {
                    options.Width = 480;
                    options.Fps = 20;
                    options.WebpQuality = 50;
                }
                else
                {
                    options.Width = 320;
                    options.Fps = 15;
                    options.WebpQuality = 35;
                }
            }
        }
    }
}
