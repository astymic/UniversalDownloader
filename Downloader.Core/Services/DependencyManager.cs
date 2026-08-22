using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace UniversalDownloader.Services
{
    public class DependencyManager
    {
        private readonly bool _isLinux;
        private readonly string _ytDlpFileName;
        private readonly string _ytDlpDownloadUrl;
        private const string YtDlpVersionApiUrl = "https://api.github.com/repos/yt-dlp/yt-dlp/releases/latest";

        private readonly string _ffmpegFileName;
        private const string FfmpegZipDownloadUrlWindows = "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip";

        public string YtDlpExecutablePath { get; private set; }
        public string FfmpegExecutablePath { get; private set; }

        public bool IsYtDlpReady { get; private set; }
        public bool IsFfmpegReady { get; private set; }

        public event Action<string>? ProgressUpdated;

        public DependencyManager()
        {
            _isLinux = RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

            _ytDlpFileName = _isLinux ? "yt-dlp" : "yt-dlp.exe";
            _ytDlpDownloadUrl = _isLinux
                ? "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp_linux"
                : "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe";

            _ffmpegFileName = _isLinux ? "ffmpeg" : "ffmpeg.exe";

            // Determine base storage directory
            string baseDir = AppContext.BaseDirectory;
            if (_isLinux && !HasWriteAccess(baseDir))
            {
                string localData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share", "UniversalDownloader", "bin");
                Directory.CreateDirectory(localData);
                baseDir = localData;
            }

            YtDlpExecutablePath = Path.Combine(baseDir, _ytDlpFileName);
            FfmpegExecutablePath = Path.Combine(baseDir, _ffmpegFileName);

            // If system-wide installed on Linux, prioritize system binary
            if (_isLinux)
            {
                string? sysYtDlp = FindSystemBinary("yt-dlp");
                if (sysYtDlp != null) YtDlpExecutablePath = sysYtDlp;

                string? sysFfmpeg = FindSystemBinary("ffmpeg");
                if (sysFfmpeg != null) FfmpegExecutablePath = sysFfmpeg;
            }
        }

        private static bool HasWriteAccess(string dir)
        {
            try
            {
                string testFile = Path.Combine(dir, Path.GetRandomFileName());
                using (FileStream fs = File.Create(testFile, 1, FileOptions.DeleteOnClose)) { }
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string? FindSystemBinary(string binaryName)
        {
            string[] commonPaths = {
                $"/usr/bin/{binaryName}",
                $"/usr/local/bin/{binaryName}",
                $"/bin/{binaryName}",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin", binaryName)
            };

            foreach (var p in commonPaths)
            {
                if (File.Exists(p)) return p;
            }

            return null;
        }

        private void ReportProgress(string status)
        {
            ProgressUpdated?.Invoke(status);
        }

        public async Task InitializeDependenciesAsync()
        {
            var ytDlpTask = CheckAndEnsureYtDlpExistsAsync();
            var ffmpegTask = CheckAndEnsureFfmpegExistsAsync();
            await Task.WhenAll(ytDlpTask, ffmpegTask);
        }

        private async Task CheckAndEnsureYtDlpExistsAsync()
        {
            ReportProgress($"Status: Checking {_ytDlpFileName}...");

            bool fileExists = File.Exists(YtDlpExecutablePath);
            string? localVersion = null;

            if (fileExists)
            {
                EnsureExecutablePermissions(YtDlpExecutablePath);
                localVersion = await GetLocalYtDlpVersionAsync();
                if (localVersion == null)
                {
                    ReportProgress($"Status: Local {_ytDlpFileName} corrupted. Re-downloading...");
                    try { File.Delete(YtDlpExecutablePath); } catch { }
                    fileExists = false;
                }
            }

            bool needsDownload = !fileExists;

            if (fileExists)
            {
                string latestVersionTag = await GetLatestYtDlpVersionTagAsync();
                if (!string.IsNullOrWhiteSpace(latestVersionTag) && localVersion != latestVersionTag)
                {
                    ReportProgress($"Status: Updating {_ytDlpFileName} to version {latestVersionTag}...");
                    try { File.Delete(YtDlpExecutablePath); } catch { }
                    needsDownload = true;
                }
                else
                {
                    IsYtDlpReady = true;
                }
            }

            if (needsDownload)
            {
                ReportProgress($"Status: Downloading {_ytDlpFileName}...");
                bool downloaded = await DownloadYtDlpAsync(CancellationToken.None);
                if (downloaded)
                {
                    EnsureExecutablePermissions(YtDlpExecutablePath);
                    string? newLocalVersion = await GetLocalYtDlpVersionAsync();
                    if (newLocalVersion != null)
                    {
                        IsYtDlpReady = true;
                        ReportProgress($"Status: {_ytDlpFileName} ready (v{newLocalVersion}).");
                    }
                }
            }
        }

        private void EnsureExecutablePermissions(string path)
        {
            if (_isLinux && File.Exists(path))
            {
                try
                {
                    File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                                               UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                                               UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
                }
                catch
                {
                    try
                    {
                        Process.Start("chmod", $"+x \"{path}\"")?.WaitForExit();
                    }
                    catch { }
                }
            }
        }

        private async Task<bool> DownloadYtDlpAsync(CancellationToken cancellationToken)
        {
            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.UserAgent.ParseAdd("UniversalDownloader/1.0");

                var response = await client.GetAsync(_ytDlpDownloadUrl, cancellationToken);
                response.EnsureSuccessStatusCode();

                byte[] data = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                await File.WriteAllBytesAsync(YtDlpExecutablePath, data, cancellationToken);
                EnsureExecutablePermissions(YtDlpExecutablePath);
                return true;
            }
            catch (Exception ex)
            {
                ReportProgress($"Error downloading {_ytDlpFileName}: {ex.Message}");
                return false;
            }
        }

        private async Task<string?> GetLocalYtDlpVersionAsync()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = YtDlpExecutablePath,
                    Arguments = "--version",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process == null) return null;

                string output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();

                return process.ExitCode == 0 ? output.Trim() : null;
            }
            catch
            {
                return null;
            }
        }

        private async Task<string> GetLatestYtDlpVersionTagAsync()
        {
            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.UserAgent.ParseAdd("UniversalDownloader/1.0");

                string json = await client.GetStringAsync(YtDlpVersionApiUrl);
                var releaseObj = JObject.Parse(json);
                return releaseObj["tag_name"]?.ToString() ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private async Task CheckAndEnsureFfmpegExistsAsync()
        {
            ReportProgress($"Status: Checking {_ffmpegFileName}...");

            if (File.Exists(FfmpegExecutablePath))
            {
                EnsureExecutablePermissions(FfmpegExecutablePath);
                IsFfmpegReady = true;
                return;
            }

            // On Linux, if not found, notify or attempt to find in PATH
            if (_isLinux)
            {
                string? sysFfmpeg = FindSystemBinary("ffmpeg");
                if (sysFfmpeg != null)
                {
                    FfmpegExecutablePath = sysFfmpeg;
                    IsFfmpegReady = true;
                    return;
                }
            }

            // On Windows, auto-download static build
            if (!_isLinux)
            {
                await DownloadAndExtractFfmpegWindowsAsync();
            }
        }

        private async Task DownloadAndExtractFfmpegWindowsAsync()
        {
            try
            {
                string zipPath = Path.Combine(Path.GetTempPath(), "ffmpeg_download.zip");

                using (var client = new HttpClient())
                {
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("UniversalDownloader/1.0");
                    var data = await client.GetByteArrayAsync(FfmpegZipDownloadUrlWindows);
                    await File.WriteAllBytesAsync(zipPath, data);
                }

                using (var archive = ZipFile.OpenRead(zipPath))
                {
                    var ffmpegEntry = archive.Entries.FirstOrDefault(e => e.Name.Equals("ffmpeg.exe", StringComparison.OrdinalIgnoreCase));
                    if (ffmpegEntry != null)
                    {
                        ffmpegEntry.ExtractToFile(FfmpegExecutablePath, true);
                        IsFfmpegReady = true;
                    }

                    var ffprobeEntry = archive.Entries.FirstOrDefault(e => e.Name.Equals("ffprobe.exe", StringComparison.OrdinalIgnoreCase));
                    if (ffprobeEntry != null)
                    {
                        string ffprobePath = Path.Combine(Path.GetDirectoryName(FfmpegExecutablePath) ?? "", "ffprobe.exe");
                        ffprobeEntry.ExtractToFile(ffprobePath, true);
                    }
                }

                try { File.Delete(zipPath); } catch { }
            }
            catch (Exception ex)
            {
                ReportProgress($"Failed to download FFmpeg: {ex.Message}");
            }
        }
    }
}
