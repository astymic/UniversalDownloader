using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace UniversalDownloader.Services
{
    public class MediaConversionProgress
    {
        public double Percentage { get; set; }
        public string StatusMessage { get; set; } = "";
        public string Speed { get; set; } = "";
        public TimeSpan CurrentTime { get; set; }
        public TimeSpan TotalDuration { get; set; }
    }

    public class MediaConverterService
    {
        private readonly DependencyManager _dependencyManager;

        public MediaConverterService(DependencyManager dependencyManager)
        {
            _dependencyManager = dependencyManager;
        }

        public async Task<bool> ConvertMediaAsync(
            string inputFilePath,
            string outputFilePath,
            string targetFormat,
            string? audioBitrate = "320k",
            string? videoResolution = "Original",
            IProgress<MediaConversionProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (!_dependencyManager.IsFfmpegReady || !File.Exists(inputFilePath))
            {
                throw new FileNotFoundException("Input file or FFmpeg executable was not found.");
            }

            TimeSpan totalDuration = await GetMediaDurationAsync(inputFilePath);

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

            // Common flags
            psi.ArgumentList.Add("-y"); // overwrite output
            psi.ArgumentList.Add("-hide_banner");
            psi.ArgumentList.Add("-i");
            psi.ArgumentList.Add(inputFilePath);

            string formatLower = targetFormat.Trim().ToLowerInvariant().TrimStart('.');
            bool isAudioTarget = formatLower is "mp3" or "m4a" or "flac" or "wav" or "aac" or "ogg" or "opus";

            if (isAudioTarget)
            {
                psi.ArgumentList.Add("-vn"); // strip video
                switch (formatLower)
                {
                    case "mp3":
                        psi.ArgumentList.Add("-c:a");
                        psi.ArgumentList.Add("libmp3lame");
                        psi.ArgumentList.Add("-b:a");
                        psi.ArgumentList.Add(audioBitrate ?? "320k");
                        break;
                    case "m4a" or "aac":
                        psi.ArgumentList.Add("-c:a");
                        psi.ArgumentList.Add("aac");
                        psi.ArgumentList.Add("-b:a");
                        psi.ArgumentList.Add(audioBitrate ?? "256k");
                        break;
                    case "flac":
                        psi.ArgumentList.Add("-c:a");
                        psi.ArgumentList.Add("flac");
                        break;
                    case "wav":
                        psi.ArgumentList.Add("-c:a");
                        psi.ArgumentList.Add("pcm_s16le");
                        break;
                    case "ogg":
                        psi.ArgumentList.Add("-c:a");
                        psi.ArgumentList.Add("libvorbis");
                        psi.ArgumentList.Add("-q:a");
                        psi.ArgumentList.Add("6");
                        break;
                    case "opus":
                        psi.ArgumentList.Add("-c:a");
                        psi.ArgumentList.Add("libopus");
                        psi.ArgumentList.Add("-b:a");
                        psi.ArgumentList.Add("160k");
                        break;
                }
            }
            else
            {
                // Video target
                if (formatLower == "gif")
                {
                    psi.ArgumentList.Add("-vf");
                    psi.ArgumentList.Add("fps=15,scale=480:-1:flags=lanczos,split[s0][s1];[s0]palettegen[p];[s1][p]paletteuse");
                }
                else
                {
                    // Scale filter if specified
                    string scaleFilter = videoResolution switch
                    {
                        "1080p" => "scale=-2:1080",
                        "720p" => "scale=-2:720",
                        "480p" => "scale=-2:480",
                        _ => ""
                    };

                    if (!string.IsNullOrEmpty(scaleFilter))
                    {
                        psi.ArgumentList.Add("-vf");
                        psi.ArgumentList.Add(scaleFilter);
                    }

                    switch (formatLower)
                    {
                        case "webm":
                            psi.ArgumentList.Add("-c:v");
                            psi.ArgumentList.Add("libvpx-vp9");
                            psi.ArgumentList.Add("-crf");
                            psi.ArgumentList.Add("30");
                            psi.ArgumentList.Add("-b:v");
                            psi.ArgumentList.Add("0");
                            psi.ArgumentList.Add("-c:a");
                            psi.ArgumentList.Add("libopus");
                            break;
                        default: // mp4, mkv, avi, mov
                            psi.ArgumentList.Add("-c:v");
                            psi.ArgumentList.Add("libx264");
                            psi.ArgumentList.Add("-preset");
                            psi.ArgumentList.Add("fast");
                            psi.ArgumentList.Add("-crf");
                            psi.ArgumentList.Add("22");
                            psi.ArgumentList.Add("-c:a");
                            psi.ArgumentList.Add("aac");
                            psi.ArgumentList.Add("-b:a");
                            psi.ArgumentList.Add("192k");
                            break;
                    }
                }
            }

            psi.ArgumentList.Add(outputFilePath);

            using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            
            var tcs = new TaskCompletionSource<bool>();
            using var reg = cancellationToken.Register(() =>
            {
                try
                {
                    if (!process.HasExited) process.Kill(true);
                }
                catch { }
                tcs.TrySetCanceled(cancellationToken);
            });

            process.ErrorDataReceived += (s, e) =>
            {
                if (string.IsNullOrEmpty(e.Data)) return;

                // Parse time=HH:MM:SS.ms
                var timeMatch = Regex.Match(e.Data, @"time=(\d{2}):(\d{2}):(\d{2}\.\d+)");
                var speedMatch = Regex.Match(e.Data, @"speed=\s*([\d\.]+)x");

                if (timeMatch.Success && totalDuration.TotalSeconds > 0)
                {
                    int hours = int.Parse(timeMatch.Groups[1].Value);
                    int mins = int.Parse(timeMatch.Groups[2].Value);
                    double secs = double.Parse(timeMatch.Groups[3].Value, CultureInfo.InvariantCulture);

                    TimeSpan currentTime = TimeSpan.FromSeconds(hours * 3600 + mins * 60 + secs);
                    double pct = Math.Min(100.0, (currentTime.TotalSeconds / totalDuration.TotalSeconds) * 100.0);
                    string speed = speedMatch.Success ? $"{speedMatch.Groups[1].Value}x" : "";

                    progress?.Report(new MediaConversionProgress
                    {
                        Percentage = pct,
                        CurrentTime = currentTime,
                        TotalDuration = totalDuration,
                        Speed = speed,
                        StatusMessage = $"Converting... {pct:F0}% ({speed})"
                    });
                }
            };

            process.Start();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync(cancellationToken);
            return process.ExitCode == 0 && File.Exists(outputFilePath);
        }

        private async Task<TimeSpan> GetMediaDurationAsync(string filePath)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = _dependencyManager.FfmpegExecutablePath,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                psi.ArgumentList.Add("-i");
                psi.ArgumentList.Add(filePath);
                psi.ArgumentList.Add("-hide_banner");

                using var proc = Process.Start(psi)!;
                string output = await proc.StandardError.ReadToEndAsync();
                await proc.WaitForExitAsync();

                var durMatch = Regex.Match(output, @"Duration:\s*(\d{2}):(\d{2}):(\d{2}\.\d+)");
                if (durMatch.Success)
                {
                    int hours = int.Parse(durMatch.Groups[1].Value);
                    int mins = int.Parse(durMatch.Groups[2].Value);
                    double secs = double.Parse(durMatch.Groups[3].Value, CultureInfo.InvariantCulture);
                    return TimeSpan.FromSeconds(hours * 3600 + mins * 60 + secs);
                }
            }
            catch { }

            return TimeSpan.Zero;
        }
    }
}
