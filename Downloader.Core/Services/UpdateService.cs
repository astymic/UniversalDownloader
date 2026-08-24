using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UniversalDownloader.Models;

namespace UniversalDownloader.Services
{
    public class UpdateService
    {
        private const string GitHubApiLatestReleaseUrl = "https://api.github.com/repos/astymic/UniversalDownloader/releases/latest";
        private const string GitHubReleasesHtmlUrl = "https://github.com/astymic/UniversalDownloader/releases";
        private readonly HttpClient _httpClient;

        public UpdateService(HttpClient? httpClient = null)
        {
            _httpClient = httpClient ?? new HttpClient();
            if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
            {
                _httpClient.DefaultRequestHeaders.Add("User-Agent", "UniversalDownloader-App");
            }
        }

        public static string GetCurrentAppVersion()
        {
            try
            {
                // 1. Check ProductVersion from running executable
                string? exePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrWhiteSpace(exePath) && File.Exists(exePath))
                {
                    var fvi = FileVersionInfo.GetVersionInfo(exePath);
                    if (!string.IsNullOrWhiteSpace(fvi.ProductVersion))
                    {
                        string ver = fvi.ProductVersion.Split('+')[0].Trim();
                        if (Version.TryParse(ver, out var parsed))
                        {
                            return NormalizeVersion(parsed);
                        }
                    }
                    if (!string.IsNullOrWhiteSpace(fvi.FileVersion))
                    {
                        string ver = fvi.FileVersion.Trim();
                        if (Version.TryParse(ver, out var parsed))
                        {
                            return NormalizeVersion(parsed);
                        }
                    }
                }

                // 2. Check AssemblyInformationalVersion
                var infoVerAttr = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
                if (!string.IsNullOrWhiteSpace(infoVerAttr))
                {
                    string clean = infoVerAttr.Split('+')[0].Trim();
                    if (Version.TryParse(clean, out var parsed))
                    {
                        return NormalizeVersion(parsed);
                    }
                }

                // 3. Check Assembly Name Version
                var asmVer = Assembly.GetEntryAssembly()?.GetName().Version;
                if (asmVer != null && asmVer.Major > 0)
                {
                    return NormalizeVersion(asmVer);
                }
            }
            catch { }

            return "1.0.11";
        }

        private static string NormalizeVersion(Version version)
        {
            if (version.Revision > 0)
                return $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}";
            if (version.Build >= 0)
                return $"{version.Major}.{version.Minor}.{version.Build}";
            return $"{version.Major}.{version.Minor}";
        }

        public async Task<UpdateInfo> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
        {
            var updateInfo = new UpdateInfo
            {
                CurrentVersion = GetCurrentAppVersion(),
                ReleaseUrl = GitHubReleasesHtmlUrl
            };

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, GitHubApiLatestReleaseUrl);
                request.Headers.Add("Accept", "application/vnd.github.v3+json");

                using var response = await _httpClient.SendAsync(request, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    return updateInfo;
                }

                string json = await response.Content.ReadAsStringAsync(cancellationToken);
                var releaseObj = JObject.Parse(json);

                string tagName = releaseObj["tag_name"]?.ToString() ?? string.Empty;
                string cleanTag = tagName.TrimStart('v', 'V');
                string releaseName = releaseObj["name"]?.ToString() ?? tagName;
                string body = releaseObj["body"]?.ToString() ?? string.Empty;
                string htmlUrl = releaseObj["html_url"]?.ToString() ?? GitHubReleasesHtmlUrl;

                updateInfo.LatestVersion = cleanTag;
                updateInfo.ReleaseTitle = releaseName;
                updateInfo.ReleaseNotes = body;
                updateInfo.ReleaseUrl = htmlUrl;

                if (DateTime.TryParse(releaseObj["published_at"]?.ToString(), out var pubDate))
                {
                    updateInfo.PublishedAt = pubDate;
                }

                // Parse and compare versions
                if (Version.TryParse(cleanTag, out var remoteVer) &&
                    Version.TryParse(updateInfo.CurrentVersion, out var localVer))
                {
                    updateInfo.IsUpdateAvailable = remoteVer > localVer;
                }
                else
                {
                    updateInfo.IsUpdateAvailable = false;
                }

                // Detect OS and find appropriate release asset
                bool isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
                bool isLinux = RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

                if (releaseObj["assets"] is JArray assets && assets.Count > 0)
                {
                    JToken? matchedAsset = null;

                    if (isWindows)
                    {
                        matchedAsset = assets.FirstOrDefault(a => (a["name"]?.ToString() ?? "").EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && !(a["name"]?.ToString() ?? "").Contains("linux", StringComparison.OrdinalIgnoreCase))
                                     ?? assets.FirstOrDefault(a => (a["name"]?.ToString() ?? "").Contains("win", StringComparison.OrdinalIgnoreCase))
                                     ?? assets.FirstOrDefault(a => (a["name"]?.ToString() ?? "").EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
                    }
                    else if (isLinux)
                    {
                        matchedAsset = assets.FirstOrDefault(a => (a["name"]?.ToString() ?? "").Contains("linux", StringComparison.OrdinalIgnoreCase))
                                     ?? assets.FirstOrDefault(a => (a["name"]?.ToString() ?? "").EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
                                     ?? assets.FirstOrDefault(a => (a["name"]?.ToString() ?? "").EndsWith(".AppImage", StringComparison.OrdinalIgnoreCase));
                    }

                    matchedAsset ??= assets.FirstOrDefault();

                    if (matchedAsset != null)
                    {
                        updateInfo.DownloadUrl = matchedAsset["browser_download_url"]?.ToString();
                        updateInfo.AssetFileName = matchedAsset["name"]?.ToString();
                        if (long.TryParse(matchedAsset["size"]?.ToString(), out var size))
                        {
                            updateInfo.AssetSize = size;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to check for updates: {ex.Message}");
            }

            return updateInfo;
        }

        public async Task<string> DownloadUpdateAssetAsync(string downloadUrl, string destinationFolder, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
        {
            Directory.CreateDirectory(destinationFolder);
            string fileName = Path.GetFileName(new Uri(downloadUrl).LocalPath);
            string destinationPath = Path.Combine(destinationFolder, fileName);

            using var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            long? totalBytes = response.Content.Headers.ContentLength;

            using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);

            byte[] buffer = new byte[81920];
            int bytesRead;
            long totalBytesRead = 0;

            while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
            {
                await fileStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);
                totalBytesRead += bytesRead;

                if (totalBytes.HasValue && totalBytes.Value > 0)
                {
                    progress?.Report((double)totalBytesRead / totalBytes.Value * 100.0);
                }
            }

            return destinationPath;
        }

        public void ApplyUpdateAndRestart(string downloadedFilePath)
        {
            string currentExecutablePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? "";
            int currentPid = Process.GetCurrentProcess().Id;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Write a robust cmd script with admin fallback for Program Files / corporate permission environments
                string cmdScriptPath = Path.Combine(Path.GetTempPath(), $"updater_{Guid.NewGuid():N}.cmd");
                string scriptContent = $@"@echo off
setlocal enabledelayedexpansion
timeout /t 1 /nobreak >nul
:waitloop
tasklist /fi ""PID eq {currentPid}"" 2>nul | find ""{currentPid}"" >nul
if not errorlevel 1 (
    timeout /t 1 /nobreak >nul
    goto waitloop
)
copy /y ""{downloadedFilePath}"" ""{currentExecutablePath}"" >nul 2>&1
if errorlevel 1 (
    powershell -NoProfile -Command ""Start-Process cmd -ArgumentList '/c copy /y \""""{downloadedFilePath}\"""" \""""{currentExecutablePath}\"""" && start \""""\"""" \""""{currentExecutablePath}\""""' -Verb RunAs""
    exit
)
start """" ""{currentExecutablePath}""
del ""%~f0""
";
                File.WriteAllText(cmdScriptPath, scriptContent);

                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c \"{cmdScriptPath}\"",
                    CreateNoWindow = true,
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                Process.Start(psi);
                Environment.Exit(0);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                try
                {
                    File.SetUnixFileMode(downloadedFilePath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
                }
                catch { }

                string shScriptPath = Path.Combine(Path.GetTempPath(), $"updater_{Guid.NewGuid():N}.sh");
                string shContent = $@"#!/bin/bash
sleep 1
while kill -0 {currentPid} 2>/dev/null; do
    sleep 0.5
done
cp -f ""{downloadedFilePath}"" ""{currentExecutablePath}"" 2>/dev/null || sudo cp -f ""{downloadedFilePath}"" ""{currentExecutablePath}""
chmod +x ""{currentExecutablePath}""
nohup ""{currentExecutablePath}"" >/dev/null 2>&1 &
rm -- ""$0""
";
                File.WriteAllText(shScriptPath, shContent);
                try
                {
                    File.SetUnixFileMode(shScriptPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                }
                catch { }

                var psi = new ProcessStartInfo
                {
                    FileName = "/bin/bash",
                    Arguments = $"\"{shScriptPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                Process.Start(psi);
                Environment.Exit(0);
            }
        }
    }
}
