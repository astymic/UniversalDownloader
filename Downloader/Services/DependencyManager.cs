using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace UniversalDownloader.Services
{
    public class DependencyManager
    {
        private const string YtDlpFileName = "yt-dlp.exe";
        private const string YtDlpDownloadUrl = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe";
        private const string YtDlpVersionApiUrl = "https://api.github.com/repos/yt-dlp/yt-dlp/releases/latest";

        private const string FfmpegFileName = "ffmpeg.exe";
        private const string FfmpegZipDownloadUrl = "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip";

        public string YtDlpExecutablePath { get; private set; }
        public string FfmpegExecutablePath { get; private set; }

        public bool IsYtDlpReady { get; private set; }
        public bool IsFfmpegReady { get; private set; }

        private readonly TaskCompletionSource<bool> _initializationTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task InitializationTask => _initializationTcs.Task;
        public bool IsInitialized => _initializationTcs.Task.IsCompleted;

        public async Task<bool> WaitForInitializationAsync(CancellationToken cancellationToken = default)
        {
            if (IsInitialized) return IsYtDlpReady;
            try
            {
                await _initializationTcs.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                return IsYtDlpReady;
            }
            catch (OperationCanceledException)
            {
                return IsYtDlpReady;
            }
            catch (Exception)
            {
                return IsYtDlpReady;
            }
        }

        public event Action<string>? ProgressUpdated;

        public DependencyManager()
        {
            YtDlpExecutablePath = Path.Combine(AppContext.BaseDirectory, YtDlpFileName);
            FfmpegExecutablePath = Path.Combine(AppContext.BaseDirectory, FfmpegFileName);

            if (File.Exists(YtDlpExecutablePath))
            {
                IsYtDlpReady = true;
            }
            if (File.Exists(FfmpegExecutablePath))
            {
                IsFfmpegReady = true;
            }
        }

        private void ReportProgress(string status)
        {
            ProgressUpdated?.Invoke(status);
        }

        public async Task InitializeDependenciesAsync()
        {
            try
            {
                // Run checks simultaneously silently
                var ytDlpTask = CheckAndEnsureYtDlpExistsAsync();
                var ffmpegTask = CheckAndEnsureFfmpegExistsAsync();

                await Task.WhenAll(ytDlpTask, ffmpegTask);
            }
            catch (Exception ex)
            {
                ReportProgress($"Dependencies initialization error: {ex.Message}");
            }
            finally
            {
                _initializationTcs.TrySetResult(true);
            }
        }

        private async Task CheckAndEnsureYtDlpExistsAsync()
        {
            ReportProgress($"Status: Checking {YtDlpFileName}...");

            bool fileExists = File.Exists(YtDlpExecutablePath);
            string? localVersion = null;

            if (fileExists)
            {
                localVersion = await GetLocalYtDlpVersionFromPathAsync(YtDlpExecutablePath);
                if (localVersion != null)
                {
                    IsYtDlpReady = true;
                }
                else
                {
                    ReportProgress($"Status: Local {YtDlpFileName} seems corrupted. Re-downloading...");
                    fileExists = false;
                }
            }

            bool needsDownload = !fileExists;

            if (fileExists)
            {
                string latestVersionTag = await GetLatestYtDlpVersionTagAsync();
                
                if (!string.IsNullOrWhiteSpace(latestVersionTag) && localVersion != latestVersionTag)
                {
                    // Auto-update silently!
                    ReportProgress($"Status: Updating {YtDlpFileName} to version {latestVersionTag}...");
                    needsDownload = true;
                }
                else
                {
                    IsYtDlpReady = true;
                }
            }

            if (needsDownload)
            {
                if (!fileExists)
                {
                    ReportProgress($"Status: {YtDlpFileName} not found. Attempting to download...");
                }

                string tempPath = YtDlpExecutablePath + ".tmp";
                bool downloaded = await DownloadYtDlpToPathAsync(tempPath, CancellationToken.None);

                if (downloaded)
                {
                    string? newLocalVersion = await GetLocalYtDlpVersionFromPathAsync(tempPath);
                    if (newLocalVersion != null)
                    {
                        try
                        {
                            if (File.Exists(YtDlpExecutablePath))
                            {
                                File.Delete(YtDlpExecutablePath);
                            }
                            File.Move(tempPath, YtDlpExecutablePath);
                        }
                        catch
                        {
                            try { File.Copy(tempPath, YtDlpExecutablePath, true); File.Delete(tempPath); } catch { }
                        }

                        IsYtDlpReady = true;
                        ReportProgress($"Status: {YtDlpFileName} ready.");
                    }
                    else
                    {
                        try { File.Delete(tempPath); } catch { }
                        if (File.Exists(YtDlpExecutablePath)) IsYtDlpReady = true;
                        ReportProgress($"Status: Downloaded {YtDlpFileName} appears corrupted.");
                    }
                }
                else
                {
                    if (File.Exists(YtDlpExecutablePath)) IsYtDlpReady = true;
                    ReportProgress($"Status: Failed to download {YtDlpFileName}.");
                }
            }
        }

        private async Task CheckAndEnsureFfmpegExistsAsync()
        {
            if (File.Exists(FfmpegExecutablePath))
            {
                IsFfmpegReady = true;
                return;
            }

            ReportProgress($"Status: Downloading {FfmpegFileName}...");
            IsFfmpegReady = await TryDownloadAndExtractFfmpegAsync(CancellationToken.None);
        }

        private async Task<bool> TryDownloadAndExtractFfmpegAsync(CancellationToken cancellationToken)
        {
            string tempZipPath = Path.Combine(Path.GetTempPath(), "ffmpeg_download_" + Guid.NewGuid().ToString("N") + ".zip");
            string tempExtractPath = Path.Combine(Path.GetTempPath(), "ffmpeg_extract_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempExtractPath);

            try
            {
                using (var httpClient = new HttpClient())
                {
                    httpClient.Timeout = TimeSpan.FromMinutes(15);
                    var response = await httpClient.GetAsync(FfmpegZipDownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                    response.EnsureSuccessStatusCode();

                    using (var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken))
                    using (var fileStream = new FileStream(tempZipPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
                    {
                        await contentStream.CopyToAsync(fileStream, cancellationToken);
                    }
                }

                cancellationToken.ThrowIfCancellationRequested();
                ReportProgress($"Status: Extracting {FfmpegFileName}...");

                await Task.Run(() => ZipFile.ExtractToDirectory(tempZipPath, tempExtractPath), cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();

                string? ffmpegSourcePath = Directory.EnumerateFiles(tempExtractPath, FfmpegFileName, SearchOption.AllDirectories).FirstOrDefault();
                if (string.IsNullOrEmpty(ffmpegSourcePath)) return false;

                File.Copy(ffmpegSourcePath, FfmpegExecutablePath, true);

                string? ffprobeSourcePath = Directory.EnumerateFiles(tempExtractPath, "ffprobe.exe", SearchOption.AllDirectories).FirstOrDefault();
                if (!string.IsNullOrEmpty(ffprobeSourcePath))
                {
                    File.Copy(ffprobeSourcePath, Path.Combine(AppContext.BaseDirectory, "ffprobe.exe"), true);
                }

                return File.Exists(FfmpegExecutablePath);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error during FFmpeg setup: {ex}");
                return false;
            }
            finally
            {
                if (File.Exists(tempZipPath)) { try { File.Delete(tempZipPath); } catch { } }
                if (Directory.Exists(tempExtractPath)) { try { Directory.Delete(tempExtractPath, true); } catch { } }
            }
        }

        private async Task<bool> DownloadYtDlpToPathAsync(string destinationPath, CancellationToken cancellationToken)
        {
            try
            {
                using (var httpClient = new HttpClient())
                {
                    httpClient.Timeout = TimeSpan.FromMinutes(15);
                    var response = await httpClient.GetAsync(YtDlpDownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                    response.EnsureSuccessStatusCode();

                    using (var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken))
                    using (var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
                    {
                        await contentStream.CopyToAsync(fileStream, cancellationToken);
                    }
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        private async Task<string?> GetLocalYtDlpVersionAsync()
        {
            return await GetLocalYtDlpVersionFromPathAsync(YtDlpExecutablePath);
        }

        private static async Task<string?> GetLocalYtDlpVersionFromPathAsync(string path)
        {
            if (!File.Exists(path)) return null;

            try
            {
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = path,
                    Arguments = "--version",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = System.Text.Encoding.UTF8
                };

                using (Process process = Process.Start(psi))
                {
                    if (process == null) return null;
                    string versionOutput = await process.StandardOutput.ReadToEndAsync();
                    await process.WaitForExitAsync();
                    if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(versionOutput))
                    {
                        return versionOutput.Trim();
                    }
                }
            }
            catch
            {
            }
            return null;
        }

        private async Task<string?> GetLatestYtDlpVersionTagAsync()
        {
            try
            {
                using (var tempHttpClient = new HttpClient())
                {
                    tempHttpClient.DefaultRequestHeaders.UserAgent.ParseAdd("UniversalDownloaderApp/1.0");
                    HttpResponseMessage response = await tempHttpClient.GetAsync(YtDlpVersionApiUrl);
                    response.EnsureSuccessStatusCode();
                    string jsonResponse = await response.Content.ReadAsStringAsync();
                    JObject releaseInfo = JObject.Parse(jsonResponse);
                    return releaseInfo["tag_name"]?.ToString()?.Trim();
                }
            }
            catch
            {
                return null;
            }
        }
    }
}
