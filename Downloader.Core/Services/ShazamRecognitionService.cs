using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace UniversalDownloader.Services
{
    public class ShazamTrackResult
    {
        public bool Success { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Artist { get; set; } = string.Empty;
        public string Album { get; set; } = string.Empty;
        public string CoverArtUrl { get; set; } = string.Empty;
        public string Genre { get; set; } = string.Empty;
        public string ErrorMessage { get; set; } = string.Empty;

        public string QueryString => !string.IsNullOrEmpty(Artist) && !string.IsNullOrEmpty(Title) 
            ? $"{Artist} - {Title}" 
            : Title;
    }

    public class ShazamRecognitionService
    {
        private readonly IAudioCaptureService _audioCaptureService;
        private readonly HttpClient _httpClient;

        public event Action<float>? AudioLevelChanged;

        public ShazamRecognitionService(IAudioCaptureService audioCaptureService)
        {
            _audioCaptureService = audioCaptureService;
            _audioCaptureService.AudioLevelChanged += level => AudioLevelChanged?.Invoke(level);

            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(15)
            };
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        }

        public async Task<ShazamTrackResult> ListenAndIdentifyAsync(
            int durationSeconds = 5, 
            AudioCaptureSource source = AudioCaptureSource.Microphone,
            CancellationToken cancellationToken = default)
        {
            try
            {
                // 1. Record 5 seconds of audio
                byte[] audioPcm = await _audioCaptureService.CaptureAudioAsync(durationSeconds, source, cancellationToken);
                if (audioPcm == null || audioPcm.Length == 0)
                {
                    return new ShazamTrackResult { Success = false, ErrorMessage = "No audio captured from selected device." };
                }

                // 2. Generate Shazam signature URI
                string signatureUri = ShazamSignatureGenerator.CreateSignatureUri(audioPcm);
                if (string.IsNullOrEmpty(signatureUri))
                {
                    return new ShazamTrackResult { Success = false, ErrorMessage = "Could not generate audio fingerprint." };
                }

                // 3. Query Shazam API
                string uuid1 = Guid.NewGuid().ToString().ToUpper();
                string uuid2 = Guid.NewGuid().ToString().ToUpper();
                string url = $"https://amp.shazam.com/discovery/v5/en-US/US/web/-/tag/{uuid1}/{uuid2}?sync=true";

                long timestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                var requestPayload = new
                {
                    geolocation = new
                    {
                        altitude = 300,
                        latitude = 45.0,
                        longitude = 2.0
                    },
                    signature = new
                    {
                        samplems = durationSeconds * 1000,
                        timestamp = timestampMs,
                        uri = signatureUri
                    },
                    timestamp = timestampMs,
                    timezone = "Europe/London"
                };

                string jsonContent = JsonSerializer.Serialize(requestPayload);
                using var request = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new StringContent(jsonContent, Encoding.UTF8, "application/json")
                };

                using var response = await _httpClient.SendAsync(request, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    return new ShazamTrackResult 
                    { 
                        Success = false, 
                        ErrorMessage = $"Shazam recognition service responded with status {response.StatusCode}." 
                    };
                }

                string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

                using var doc = JsonDocument.Parse(responseBody);
                var root = doc.RootElement;

                // Check if track was found
                if (root.TryGetProperty("track", out var trackElement))
                {
                    var result = new ShazamTrackResult
                    {
                        Success = true,
                        Title = trackElement.TryGetProperty("title", out var titleProp) ? titleProp.GetString() ?? "" : "",
                        Artist = trackElement.TryGetProperty("subtitle", out var artistProp) ? artistProp.GetString() ?? "" : ""
                    };

                    if (trackElement.TryGetProperty("images", out var imagesElem) && 
                        imagesElem.TryGetProperty("coverart", out var coverProp))
                    {
                        result.CoverArtUrl = coverProp.GetString() ?? "";
                    }

                    if (trackElement.TryGetProperty("genres", out var genresElem) &&
                        genresElem.TryGetProperty("primary", out var genreProp))
                    {
                        result.Genre = genreProp.GetString() ?? "";
                    }

                    if (trackElement.TryGetProperty("sections", out var sectionsElem) && sectionsElem.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var sec in sectionsElem.EnumerateArray())
                        {
                            if (sec.TryGetProperty("metadata", out var metaElem) && metaElem.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var meta in metaElem.EnumerateArray())
                                {
                                    if (meta.TryGetProperty("title", out var metaTitle) && metaTitle.GetString() == "Album" &&
                                        meta.TryGetProperty("text", out var metaText))
                                    {
                                        result.Album = metaText.GetString() ?? "";
                                    }
                                }
                            }
                        }
                    }

                    return result;
                }

                return new ShazamTrackResult 
                { 
                    Success = false, 
                    ErrorMessage = "No song match found. Please try recording closer or with less background noise." 
                };
            }
            catch (OperationCanceledException)
            {
                return new ShazamTrackResult { Success = false, ErrorMessage = "Recognition was cancelled." };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Shazam recognition exception: {ex}");
                return new ShazamTrackResult { Success = false, ErrorMessage = $"Recognition error: {ex.Message}" };
            }
        }
    }
}
