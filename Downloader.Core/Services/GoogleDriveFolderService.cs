using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UniversalDownloader.Models;

namespace UniversalDownloader.Services
{
    public class GoogleDriveFolderService
    {
        private static readonly HttpClient _httpClient = new(new HttpClientHandler
        {
            AllowAutoRedirect = true,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        })
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        public static bool IsGoogleDriveFolderUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            return Regex.IsMatch(url, @"drive\.google\.com/(?:drive/(?:u/\d+/)?folders/|open\?id=)", RegexOptions.IgnoreCase);
        }

        public static string? ExtractFolderId(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;

            var matchFolders = Regex.Match(url, @"folders/([a-zA-Z0-9_-]{10,})", RegexOptions.IgnoreCase);
            if (matchFolders.Success) return matchFolders.Groups[1].Value;

            var matchId = Regex.Match(url, @"[?&]id=([a-zA-Z0-9_-]{10,})", RegexOptions.IgnoreCase);
            if (matchId.Success) return matchId.Groups[1].Value;

            // If a bare ID was passed
            if (Regex.IsMatch(url.Trim(), @"^[a-zA-Z0-9_-]{15,45}$"))
            {
                return url.Trim();
            }

            return null;
        }

        public async Task<GoogleDriveFolderResult> FetchFolderTreeAsync(string urlOrFolderId, IProgress<string>? statusProgress = null, CancellationToken cancellationToken = default)
        {
            string? folderId = ExtractFolderId(urlOrFolderId);
            if (string.IsNullOrWhiteSpace(folderId))
            {
                throw new ArgumentException("Invalid Google Drive folder URL or ID.");
            }

            statusProgress?.Report("Connecting to Google Drive...");

            string folderUrl = $"https://drive.google.com/drive/folders/{folderId}";
            string rootHtml = await FetchHtmlAsync(folderUrl, cancellationToken);

            string rootTitle = ExtractRootTitle(rootHtml, folderId);
            statusProgress?.Report($"Found: {rootTitle}. Scanning contents...");

            var rootResult = new GoogleDriveFolderResult
            {
                RootFolderId = folderId,
                RootTitle = rootTitle
            };

            var visitedFolders = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
            visitedFolders.TryAdd(folderId, 0);

            var items = ParseItemsFromHtml(rootHtml, "", null);
            foreach (var item in items)
            {
                rootResult.Items.Add(item);
            }

            // Recursively scan any subfolders found with concurrency (up to 6 parallel requests)
            using var semaphore = new SemaphoreSlim(6, 6);
            await ScanSubfoldersRecursivelyAsync(rootResult.Items, visitedFolders, semaphore, statusProgress, cancellationToken);

            rootResult.TotalFilesCount = CountTotalFiles(rootResult.Items);
            rootResult.TotalFoldersCount = CountTotalFolders(rootResult.Items);
            rootResult.TotalBytes = CountTotalBytes(rootResult.Items);

            return rootResult;
        }

        private async Task ScanSubfoldersRecursivelyAsync(IEnumerable<GoogleDriveItem> items, ConcurrentDictionary<string, byte> visitedFolders, SemaphoreSlim semaphore, IProgress<string>? statusProgress, CancellationToken cancellationToken)
        {
            var tasks = new List<Task>();

            foreach (var item in items)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (item.IsFolder && !string.IsNullOrWhiteSpace(item.Id) && visitedFolders.TryAdd(item.Id, 0))
                {
                    tasks.Add(Task.Run(async () =>
                    {
                        await semaphore.WaitAsync(cancellationToken);
                        try
                        {
                            statusProgress?.Report($"Scanning subfolder: {item.Name}...");

                            string subUrl = $"https://drive.google.com/drive/folders/{item.Id}";
                            string subHtml = await FetchHtmlAsync(subUrl, cancellationToken);
                            var subItems = ParseItemsFromHtml(subHtml, item.RelativePath, item);

                            lock (item.Children)
                            {
                                foreach (var subItem in subItems)
                                {
                                    item.Children.Add(subItem);
                                }
                            }

                            if (item.Children.Any(c => c.IsFolder))
                            {
                                await ScanSubfoldersRecursivelyAsync(item.Children, visitedFolders, semaphore, statusProgress, cancellationToken);
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Failed to scan subfolder '{item.Name}': {ex.Message}");
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    }, cancellationToken));
                }
            }

            if (tasks.Count > 0)
            {
                await Task.WhenAll(tasks);
            }
        }

        private async Task<string> FetchHtmlAsync(string url, CancellationToken cancellationToken)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
            req.Headers.Add("Accept-Language", "en-US,en;q=0.9");

            using var resp = await _httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            resp.EnsureSuccessStatusCode();
            return await resp.Content.ReadAsStringAsync(cancellationToken);
        }

        private string ExtractRootTitle(string html, string folderId)
        {
            // 1. Try page title
            var titleMatch = Regex.Match(html, @"<title>(.*?)</title>", RegexOptions.IgnoreCase);
            if (titleMatch.Success)
            {
                string raw = WebUtility.HtmlDecode(titleMatch.Groups[1].Value).Trim();
                raw = Regex.Replace(raw, @"\s*[-–—]\s*Google\s*Drive\s*$", "", RegexOptions.IgnoreCase).Trim();
                if (!string.IsNullOrWhiteSpace(raw) && !raw.Equals("Google Drive", StringComparison.OrdinalIgnoreCase))
                {
                    return SanitizeName(raw);
                }
            }

            // 2. Try JSON folder payload: ["folderId", null, "Folder Title", "application/vnd.google-apps.folder", ...
            var folderPayloadMatch = Regex.Match(html, $@"\[""{Regex.Escape(folderId)}"",null,""([^""]+)"",""application/vnd\.google-apps\.folder""", RegexOptions.IgnoreCase);
            if (folderPayloadMatch.Success)
            {
                string raw = Regex.Unescape(folderPayloadMatch.Groups[1].Value);
                if (!string.IsNullOrWhiteSpace(raw))
                {
                    return SanitizeName(raw);
                }
            }

            return "Google Drive Folder";
        }

        private List<GoogleDriveItem> ParseItemsFromHtml(string html, string currentPath, GoogleDriveItem? parentItem)
        {
            var results = new List<GoogleDriveItem>();
            var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Find all AF_initDataCallback script blocks
            var matches = Regex.Matches(html, @"AF_initDataCallback\((?<json>\{.*?\})\);</script>", RegexOptions.Singleline);
            foreach (Match match in matches)
            {
                string block = match.Groups["json"].Value;
                var dataMatch = Regex.Match(block, @"data:\s*(?<array>\[.*\])\s*,\s*sideChannel:", RegexOptions.Singleline);
                if (!dataMatch.Success) continue;

                string arrayJson = dataMatch.Groups["array"].Value;
                try
                {
                    var token = JToken.Parse(arrayJson);
                    FindItemsInJToken(token, results, seenIds, currentPath, parentItem);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to parse JSON callback: {ex.Message}");
                }
            }

            return results;
        }

        private void FindItemsInJToken(JToken token, List<GoogleDriveItem> results, HashSet<string> seenIds, string currentPath, GoogleDriveItem? parentItem)
        {
            if (token is JArray arr)
            {
                if (arr.Count > 4 &&
                    arr[0] is JArray idArr && idArr.Count == 2 && idArr[0].Type == JTokenType.Null && idArr[1].Type == JTokenType.String &&
                    arr[4].Type == JTokenType.String)
                {
                    string id = idArr[1].ToString();
                    string mime = arr[4].ToString();

                    if (!string.IsNullOrWhiteSpace(id) && id.Length >= 10 && !seenIds.Contains(id))
                    {
                        seenIds.Add(id);
                        bool isFolder = mime.Equals("application/vnd.google-apps.folder", StringComparison.OrdinalIgnoreCase);

                        string name = ExtractItemName(arr);
                        if (string.IsNullOrWhiteSpace(name))
                        {
                            name = isFolder ? "Folder" : "File";
                        }

                        string? sizeStr = ExtractSizeString(arr);

                        string relPath = string.IsNullOrEmpty(currentPath) ? name : $"{currentPath}/{name}";

                        long sizeBytes = !isFolder ? GoogleDriveItem.ParseSizeToBytes(sizeStr) : 0;

                        var driveItem = new GoogleDriveItem
                        {
                            Id = id,
                            Name = name,
                            IsFolder = isFolder,
                            MimeType = mime,
                            SizeString = sizeStr,
                            SizeBytes = sizeBytes,
                            RelativePath = relPath,
                            Parent = parentItem,
                            IsSelected = true,
                            IsExpanded = true
                        };

                        results.Add(driveItem);
                    }
                }

                foreach (var child in arr)
                {
                    FindItemsInJToken(child, results, seenIds, currentPath, parentItem);
                }
            }
        }

        private string ExtractItemName(JArray arr)
        {
            if (arr.Count > 35 && arr[35] is JArray a35 && a35.Count > 0 && a35[0] is JArray a35_0 && a35_0.Count > 0 && a35_0[0] is JArray a35_0_0 && a35_0_0.Count > 0)
            {
                string? val = a35_0_0[0]?.ToString();
                if (!string.IsNullOrWhiteSpace(val)) return val.Trim();
            }

            try
            {
                var token = arr[24]?[2]?[0]?[2]?[1]?[0]?[0]?[0];
                if (token != null && !string.IsNullOrWhiteSpace(token.ToString()))
                {
                    return token.ToString().Trim();
                }
            }
            catch { }

            return "";
        }

        private string? ExtractSizeString(JArray arr)
        {
            try
            {
                var token = arr[24]?[2]?[2]?[2]?[1]?[0]?[0]?[0];
                if (token != null)
                {
                    string s = token.ToString().Trim();
                    if (!string.IsNullOrWhiteSpace(s) && s != "—")
                    {
                        return s;
                    }
                }
            }
            catch { }

            return null;
        }

        private string SanitizeName(string name)
        {
            string invalid = new string(Path.GetInvalidFileNameChars()) + new string(Path.GetInvalidPathChars());
            string clean = string.Concat(name.Select(c => invalid.Contains(c) ? '_' : c)).Trim();
            return string.IsNullOrWhiteSpace(clean) ? "Google Drive Folder" : clean;
        }

        private int CountTotalFiles(IEnumerable<GoogleDriveItem> items)
        {
            int count = 0;
            foreach (var it in items)
            {
                if (!it.IsFolder) count++;
                else count += CountTotalFiles(it.Children);
            }
            return count;
        }

        private int CountTotalFolders(IEnumerable<GoogleDriveItem> items)
        {
            int count = 0;
            foreach (var it in items)
            {
                if (it.IsFolder)
                {
                    count++;
                    count += CountTotalFolders(it.Children);
                }
            }
            return count;
        }

        private long CountTotalBytes(IEnumerable<GoogleDriveItem> items)
        {
            long total = 0;
            foreach (var it in items)
            {
                if (!it.IsFolder) total += it.SizeBytes;
                else total += CountTotalBytes(it.Children);
            }
            return total;
        }
    }
}
