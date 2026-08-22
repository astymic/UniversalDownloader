using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using TagLib;

namespace UniversalDownloader.Services
{
    public class MetadataService
    {
        private readonly HttpClient _httpClient;

        public MetadataService(HttpClient? httpClient = null)
        {
            _httpClient = httpClient ?? new HttpClient();
        }

        /// <summary>
        /// Applies ID3 / media metadata tags and embeds cover art into an audio file (.mp3, .m4a, .flac, etc.).
        /// </summary>
        public async Task<bool> ApplyAudioMetadataAsync(
            string filePath,
            string title,
            string? artist = null,
            string? album = null,
            uint? year = null,
            string? coverArtUrl = null,
            byte[]? coverArtBytes = null)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !System.IO.File.Exists(filePath))
            {
                return false;
            }

            try
            {
                // Fetch cover art bytes if URL is provided
                if (coverArtBytes == null && !string.IsNullOrWhiteSpace(coverArtUrl))
                {
                    try
                    {
                        using var req = new HttpRequestMessage(HttpMethod.Get, coverArtUrl);
                        req.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
                        using var res = await _httpClient.SendAsync(req);
                        if (res.IsSuccessStatusCode)
                        {
                            coverArtBytes = await res.Content.ReadAsByteArrayAsync();
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Failed to fetch cover art from {coverArtUrl}: {ex.Message}");
                    }
                }

                // TagLib file processing
                using (var tagFile = TagLib.File.Create(filePath))
                {
                    if (!string.IsNullOrWhiteSpace(title))
                    {
                        tagFile.Tag.Title = title.Trim();
                    }

                    if (!string.IsNullOrWhiteSpace(artist))
                    {
                        var artistList = artist.Split(new[] { ", ", " feat. ", " ft. ", " / ", " & " }, StringSplitOptions.RemoveEmptyEntries);
                        tagFile.Tag.Performers = artistList.Length > 0 ? artistList : new[] { artist.Trim() };
                        tagFile.Tag.AlbumArtists = new[] { artistList.Length > 0 ? artistList[0] : artist.Trim() };
                    }

                    if (!string.IsNullOrWhiteSpace(album))
                    {
                        tagFile.Tag.Album = album.Trim();
                    }

                    if (year.HasValue && year.Value > 1900 && year.Value < 2100)
                    {
                        tagFile.Tag.Year = year.Value;
                    }

                    if (coverArtBytes != null && coverArtBytes.Length > 0)
                    {
                        var pic = new TagLib.Picture(new TagLib.ByteVector(coverArtBytes))
                        {
                            Type = PictureType.FrontCover,
                            Description = "Cover",
                            MimeType = DetectImageMimeType(coverArtBytes)
                        };
                        tagFile.Tag.Pictures = new IPicture[] { pic };
                    }

                    tagFile.Save();
                }

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to apply audio metadata to '{filePath}': {ex.Message}");
                return false;
            }
        }

        private static string DetectImageMimeType(byte[] bytes)
        {
            if (bytes.Length > 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
            {
                return "image/jpeg";
            }
            if (bytes.Length > 4 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
            {
                return "image/png";
            }
            if (bytes.Length > 3 && bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46)
            {
                return "image/gif";
            }
            if (bytes.Length > 4 && bytes[0] == 'R' && bytes[1] == 'I' && bytes[2] == 'F' && bytes[3] == 'F')
            {
                return "image/webp";
            }
            return "image/jpeg";
        }
    }
}
