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
        private readonly string _ffprobeFileName;
        private const string FfmpegZipDownloadUrlWindows = "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip";

        public string YtDlpExecutablePath { get; private set; }
        public string FfmpegExecutablePath { get; private set; }
        public string FfprobeExecutablePath { get; private set; }

        public bool IsYtDlpReady { get; private set; }
        public bool IsFfmpegReady { get; private set; }
        public bool IsFfprobeReady { get; private set; }

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

        public static string GetSettingsDirectory()
        {
            string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(appDataPath))
            {
                appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
            }
            string appFolder = Path.Combine(appDataPath, "UniversalDownloader");
            try
            {
                if (!Directory.Exists(appFolder))
                {
                    Directory.CreateDirectory(appFolder);
                }
            }
            catch { }
            return appFolder;
        }

        public DependencyManager()
        {
            _isLinux = RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

            _ytDlpFileName = _isLinux ? "yt-dlp" : "yt-dlp.exe";
            _ytDlpDownloadUrl = _isLinux
                ? "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp_linux"
                : "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe";

            _ffmpegFileName = _isLinux ? "ffmpeg" : "ffmpeg.exe";
            _ffprobeFileName = _isLinux ? "ffprobe" : "ffprobe.exe";

            // Determine target storage directory in user settings folder
            string targetDir = GetSettingsDirectory();
            string baseDir = AppContext.BaseDirectory;

            string targetYtDlp = Path.Combine(targetDir, _ytDlpFileName);
            string targetFfmpeg = Path.Combine(targetDir, _ffmpegFileName);
            string targetFfprobe = Path.Combine(targetDir, _ffprobeFileName);

            // Auto-migrate from baseDir or old bin/ subfolder
            MigrateBinaryIfPresent(Path.Combine(baseDir, _ytDlpFileName), targetYtDlp);
            MigrateBinaryIfPresent(Path.Combine(targetDir, "bin", _ytDlpFileName), targetYtDlp);

            MigrateBinaryIfPresent(Path.Combine(baseDir, _ffmpegFileName), targetFfmpeg);
            MigrateBinaryIfPresent(Path.Combine(targetDir, "bin", _ffmpegFileName), targetFfmpeg);

            MigrateBinaryIfPresent(Path.Combine(baseDir, _ffprobeFileName), targetFfprobe);
            MigrateBinaryIfPresent(Path.Combine(targetDir, "bin", _ffprobeFileName), targetFfprobe);

            try
            {
                string oldBin = Path.Combine(targetDir, "bin");
                if (Directory.Exists(oldBin) && !Directory.EnumerateFileSystemEntries(oldBin).Any())
                {
                    Directory.Delete(oldBin);
                }
            }
            catch { }

            YtDlpExecutablePath = targetYtDlp;
            FfmpegExecutablePath = targetFfmpeg;
            FfprobeExecutablePath = targetFfprobe;

            // If system-wide installed on Linux, use system binary if not present in settings dir
            if (_isLinux)
            {
                if (!File.Exists(YtDlpExecutablePath))
                {
                    string? sysYtDlp = FindSystemBinary("yt-dlp");
                    if (sysYtDlp != null) YtDlpExecutablePath = sysYtDlp;
                }

                if (!File.Exists(FfmpegExecutablePath))
                {
                    string? sysFfmpeg = FindSystemBinary("ffmpeg");
                    if (sysFfmpeg != null) FfmpegExecutablePath = sysFfmpeg;
                }

                if (!File.Exists(FfprobeExecutablePath))
                {
                    string? sysFfprobe = FindSystemBinary("ffprobe");
                    if (sysFfprobe != null) FfprobeExecutablePath = sysFfprobe;
                }
            }

            if (File.Exists(YtDlpExecutablePath))
            {
                EnsureExecutablePermissions(YtDlpExecutablePath);
                IsYtDlpReady = true;
            }
            if (File.Exists(FfmpegExecutablePath))
            {
                EnsureExecutablePermissions(FfmpegExecutablePath);
                IsFfmpegReady = true;
            }
            if (File.Exists(FfprobeExecutablePath))
            {
                EnsureExecutablePermissions(FfprobeExecutablePath);
                IsFfprobeReady = true;
            }
        }

        private static void MigrateBinaryIfPresent(string sourcePath, string destinationPath)
        {
            try
            {
                if (!File.Exists(sourcePath)) return;
                if (string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(destinationPath), StringComparison.OrdinalIgnoreCase)) return;

                if (!File.Exists(destinationPath))
                {
                    try
                    {
                        File.Move(sourcePath, destinationPath);
                        return;
                    }
                    catch
                    {
                        File.Copy(sourcePath, destinationPath, true);
                        try { File.Delete(sourcePath); } catch { }
                    }
                }
                else
                {
                    try { File.Delete(sourcePath); } catch { }
                }
            }
            catch
            {
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
            try
            {
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
            ReportProgress($"Status: Checking {_ytDlpFileName}...");

            bool fileExists = File.Exists(YtDlpExecutablePath);
            string? localVersion = null;

            if (fileExists)
            {
                EnsureExecutablePermissions(YtDlpExecutablePath);
                localVersion = await GetLocalYtDlpVersionFromPathAsync(YtDlpExecutablePath);
                if (localVersion != null)
                {
                    IsYtDlpReady = true;
                }
                else
                {
                    ReportProgress($"Status: Local {_ytDlpFileName} corrupted. Re-downloading...");
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
                string tempPath = YtDlpExecutablePath + ".tmp";
                bool downloaded = await DownloadYtDlpToPathAsync(tempPath, CancellationToken.None);
                if (downloaded)
                {
                    EnsureExecutablePermissions(tempPath);
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

                        EnsureExecutablePermissions(YtDlpExecutablePath);
                        IsYtDlpReady = true;
                        ReportProgress($"Status: {_ytDlpFileName} ready (v{newLocalVersion}).");
                    }
                    else
                    {
                        try { File.Delete(tempPath); } catch { }
                        if (File.Exists(YtDlpExecutablePath)) IsYtDlpReady = true;
                    }
                }
                else
                {
                    if (File.Exists(YtDlpExecutablePath)) IsYtDlpReady = true;
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

        private async Task<bool> DownloadYtDlpToPathAsync(string destinationPath, CancellationToken cancellationToken)
        {
            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.UserAgent.ParseAdd("UniversalDownloader/1.0");

                var response = await client.GetAsync(_ytDlpDownloadUrl, cancellationToken);
                response.EnsureSuccessStatusCode();

                byte[] data = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                await File.WriteAllBytesAsync(destinationPath, data, cancellationToken);
                EnsureExecutablePermissions(destinationPath);
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
            return await GetLocalYtDlpVersionFromPathAsync(YtDlpExecutablePath);
        }

        private static async Task<string?> GetLocalYtDlpVersionFromPathAsync(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;

                var psi = new ProcessStartInfo
                {
                    FileName = path,
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
                }
                string? sysFfprobe = FindSystemBinary("ffprobe");
                if (sysFfprobe != null)
                {
                    FfprobeExecutablePath = sysFfprobe;
                    IsFfprobeReady = true;
                }
                if (IsFfmpegReady) return;
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
                        string ffprobePath = Path.Combine(Path.GetDirectoryName(FfmpegExecutablePath) ?? GetSettingsDirectory(), "ffprobe.exe");
                        ffprobeEntry.ExtractToFile(ffprobePath, true);
                        FfprobeExecutablePath = ffprobePath;
                        IsFfprobeReady = true;
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
