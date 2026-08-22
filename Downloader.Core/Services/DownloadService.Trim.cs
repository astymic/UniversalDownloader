using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace UniversalDownloader.Services
{
    public partial class DownloadService
    {
        private async Task<bool> TrimLocalVideoAsync(string inputFilePath, string finalDestinationFolder, bool extractAudio, string audioFormat, double trimStartSeconds, double trimEndSeconds, CancellationToken cancellationToken)
        {
            if (!_dependencyManager.IsFfmpegReady)
            {
                throw new Exception("FFmpeg is required for trimming but is not available.");
            }

            ReportProgress("Trimming video...", null, 0, true);

            string videoTitle = Path.GetFileNameWithoutExtension(inputFilePath);
            string tempDownloadFolder = Path.GetDirectoryName(inputFilePath) ?? "";
            
            string outputExtension;
            if (extractAudio)
            {
                outputExtension = (audioFormat?.ToLower() ?? "m4a") switch
                {
                    "mp3" => ".mp3",
                    "wav" => ".wav",
                    "flac" => ".flac",
                    _ => ".m4a"
                };
            }
            else
            {
                outputExtension = Path.GetExtension(inputFilePath);
                if (string.IsNullOrEmpty(outputExtension)) outputExtension = ".mp4";
            }
            
            // Add a temporary suffix to prevent FFmpeg from trying to overwrite its own input stream safely
            string tempFileName = $"{videoTitle}_trimmed{outputExtension}";
            string tempFilePath = Path.Combine(tempDownloadFolder, tempFileName);

            double trimDuration = trimEndSeconds - trimStartSeconds;
            var ffmpegArgs = new List<string>();

            // Fast local file seeking
            ffmpegArgs.Add($"-ss {trimStartSeconds.ToString("F3", CultureInfo.InvariantCulture)}");
            ffmpegArgs.Add($"-i \"{inputFilePath}\"");
            ffmpegArgs.Add($"-t {trimDuration.ToString("F3", CultureInfo.InvariantCulture)}");

            if (extractAudio)
            {
                ffmpegArgs.Add("-vn"); // No video
                switch (audioFormat?.ToLower() ?? "m4a")
                {
                    case "mp3":
                        ffmpegArgs.Add("-c:a libmp3lame");
                        ffmpegArgs.Add("-q:a 2");
                        break;
                    case "wav":
                        ffmpegArgs.Add("-c:a pcm_s16le");
                        break;
                    case "flac":
                        ffmpegArgs.Add("-c:a flac");
                        break;
                    default:
                        ffmpegArgs.Add("-c:a aac");
                        ffmpegArgs.Add("-b:a 192k");
                        break;
                }
            }
            else
            {
                // Native stream copy for blazing fast parsing!
                ffmpegArgs.Add("-c copy");
                ffmpegArgs.Add("-movflags +faststart");
            }

            ffmpegArgs.Add("-avoid_negative_ts make_zero");
            ffmpegArgs.Add("-y"); // Overwrite output file
            ffmpegArgs.Add($"\"{tempFilePath}\"");

            string arguments = string.Join(" ", ffmpegArgs);
            ProcessStartInfo psiFfmpeg = new ProcessStartInfo
            {
                FileName = _dependencyManager.FfmpegExecutablePath,
                Arguments = arguments,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardErrorEncoding = System.Text.Encoding.UTF8
            };

            var ffmpegErrorOutput = new System.Text.StringBuilder();
            double lastPercentage = 0;
            
            ReportProgress("Trimming video segment...", tempFileName, 0, false);
            
            using (var process = new Process { StartInfo = psiFfmpeg, EnableRaisingEvents = true })
            {
                process.ErrorDataReceived += (sender, e) =>
                {
                    if (e.Data != null)
                    {
                        ffmpegErrorOutput.AppendLine(e.Data);
                        ParseFfmpegProgress(e.Data, trimStartSeconds, trimEndSeconds, ref lastPercentage);
                    }
                };

                process.Start();
                process.BeginErrorReadLine(); // Reads asynchronously to prevent deadlock

                try
                {
                    await process.WaitForExitAsync(cancellationToken);
                }
                catch (TaskCanceledException)
                {
                    if (!process.HasExited) process.Kill(true);
                    CleanDirectory(tempDownloadFolder);
                    throw new OperationCanceledException();
                }
                catch (Exception)
                {
                    if (!process.HasExited) process.Kill(true);
                    CleanDirectory(tempDownloadFolder);
                    throw;
                }

                if (process.ExitCode != 0)
                {
                    string error = ffmpegErrorOutput.ToString();
                    Debug.WriteLine($"FFMPEG FAILED. Exit Code: {process.ExitCode}\nOutput:\n{error}");
                    CleanDirectory(tempDownloadFolder);
                    
                    var errorLines = error.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                                          .Where(line => line.ToLower().Contains("error") || line.ToLower().Contains("failed") || line.ToLower().Contains("invalid"))
                                          .Take(5)
                                          .ToArray();
                    string errorMessage = errorLines.Length > 0 ? string.Join("\n", errorLines) : "Unknown FFmpeg error occurred";
                    throw new Exception($"FFmpeg extraction failed: {errorMessage}");
                }
            }

            // Cleanup the original full video file before copying the trimmed segment to destination
            try { File.Delete(inputFilePath); } catch (Exception ex) { Debug.WriteLine($"Failed to delete full cached file: {ex.Message}"); }

            // Rename the trimmed file to the expected final name without the suffix
            string finalFileName = $"{videoTitle}{outputExtension}";
            string finalFilePath = Path.Combine(tempDownloadFolder, finalFileName);
            try { File.Move(tempFilePath, finalFilePath, true); } catch (Exception ex) { Debug.WriteLine($"Failed to rename trimmed file: {ex.Message}"); }

            CopyToFinalDestinationAndClean(tempDownloadFolder, finalDestinationFolder);
            ReportProgress($"Trim complete — saved as '{finalFileName}'", finalFileName, 100, false);
            return true;
        }
    }
}
