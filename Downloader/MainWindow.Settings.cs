using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Newtonsoft.Json.Linq;

namespace UniversalDownloader
{
    public partial class MainWindow
    {
        private const string SettingsKeyLastDownloadPath = "LastDownloadPath";
        private const string SettingsKeySpotifyClientId = "SpotifyClientId";
        private const string SettingsKeySpotifyClientSecret = "SpotifyClientSecret";
        private const string SettingsKeySpotifyUserToken = "SpotifyUserToken";
        private const string SettingsKeySpotifyUserTokenExpiration = "SpotifyUserTokenExpiration";

        private string? _spotifyClientId;
        private string? _spotifyClientSecret;
        private string? _spotifyUserToken;
        private DateTime? _spotifyUserTokenExpiration;

        public string? SpotifyClientId => _spotifyClientId;
        public string? SpotifyClientSecret => _spotifyClientSecret;
        public string? SpotifyUserToken => _spotifyUserToken;
        public DateTime? SpotifyUserTokenExpiration => _spotifyUserTokenExpiration;

        private string GetSettingsFilePath()
        {
            string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string appFolder = Path.Combine(appDataPath, "UniversalDownloader");
            Directory.CreateDirectory(appFolder);
            return Path.Combine(appFolder, "settings.json");
        }

        private void LoadSettings()
        {
            string settingsFile = GetSettingsFilePath();
            if (File.Exists(settingsFile))
            {
                try
                {
                    JObject settings = JObject.Parse(File.ReadAllText(settingsFile));
                    
                    // Last download path
                    if (settings.TryGetValue(SettingsKeyLastDownloadPath, out JToken? pathToken))
                    {
                        string? savedPath = pathToken?.ToString();
                        if (savedPath != null && Directory.Exists(savedPath))
                        {
                            SelectedDirectory = savedPath;
                            if (SettingsDirectoryPathTextBox != null)
                            {
                                SettingsDirectoryPathTextBox.Text = savedPath;
                            }
                        }
                        else
                        {
                            SelectedDirectory = null;
                        }
                    }

                    // Spotify developer credentials
                    if (settings.TryGetValue(SettingsKeySpotifyClientId, out JToken? cidToken))
                    {
                        _spotifyClientId = cidToken?.ToString();
                        if (SpotifyClientIdTextBox != null)
                        {
                            SpotifyClientIdTextBox.Text = _spotifyClientId;
                        }
                    }
                    if (settings.TryGetValue(SettingsKeySpotifyClientSecret, out JToken? secToken))
                    {
                        _spotifyClientSecret = secToken?.ToString();
                        if (SpotifyClientSecretTextBox != null)
                        {
                            SpotifyClientSecretTextBox.Text = _spotifyClientSecret;
                        }
                    }

                    // Spotify user account tokens
                    if (settings.TryGetValue(SettingsKeySpotifyUserToken, out JToken? tokenToken))
                    {
                        _spotifyUserToken = tokenToken?.ToString();
                    }
                    if (settings.TryGetValue(SettingsKeySpotifyUserTokenExpiration, out JToken? expToken))
                    {
                        if (DateTime.TryParse(expToken?.ToString(), out DateTime expiration))
                        {
                            _spotifyUserTokenExpiration = expiration;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error loading settings: {ex.Message}");
                    SelectedDirectory = null;
                }
            }

            UpdateSpotifyStatusUi();
        }

        private async Task SaveSettingAsync(string key, string value)
        {
            try
            {
                string settingsFile = GetSettingsFilePath(); JObject settings;
                if (File.Exists(settingsFile))
                {
                    try
                    {
                        string jsonContent = await Task.Run(() => File.ReadAllText(settingsFile));
                        settings = JObject.Parse(jsonContent);
                    }
                    catch { settings = new JObject(); }
                }
                else { settings = new JObject(); }
                settings[key] = value;
                string outputJson = settings.ToString(Newtonsoft.Json.Formatting.Indented);
                await Task.Run(() => File.WriteAllText(settingsFile, outputJson));
                Debug.WriteLine($"Setting '{key}' saved asynchronously.");
            }
            catch (Exception ex) { Debug.WriteLine($"Failed to write setting '{key}' asynchronously: {ex.Message}"); }
        }

        private void SaveSetting(string key, string value)
        {
            string settingsFile = GetSettingsFilePath(); JObject settings;
            if (File.Exists(settingsFile))
            {
                try { settings = JObject.Parse(File.ReadAllText(settingsFile)); } catch { settings = new JObject(); }
            }
            else { settings = new JObject(); }
            settings[key] = value;
            try { File.WriteAllText(settingsFile, settings.ToString(Newtonsoft.Json.Formatting.Indented)); }
            catch (Exception ex) { Debug.WriteLine($"Failed to write setting '{key}': {ex.Message}"); }
        }

        private bool IsFirstRunToday(string settingKey)
        {
            string settingsFile = GetSettingsFilePath(); JObject settings;
            if (File.Exists(settingsFile))
            {
                try { settings = JObject.Parse(File.ReadAllText(settingsFile)); } catch { settings = new JObject(); }
            }
            else { settings = new JObject(); }
            string lastRunDateKey = $"{settingKey}_LastCheckDate";
            if (settings.TryGetValue(lastRunDateKey, out JToken? lastRunToken))
            {
                if (DateTime.TryParse(lastRunToken?.ToString(), out DateTime lastRunDate)) { return lastRunDate.Date < DateTime.UtcNow.Date; }
            }
            return true;
        }

        private void SetLastRunTimestamp(string settingKey)
        {
            string lastRunDateKey = $"{settingKey}_LastCheckDate";
            SaveSetting(lastRunDateKey, DateTime.UtcNow.ToString("o"));
        }

        public void UpdateSpotifyStatusUi()
        {
            // Update Method A (Browser Account)
            if (!string.IsNullOrEmpty(_spotifyUserToken) && _spotifyUserTokenExpiration.HasValue && _spotifyUserTokenExpiration.Value > DateTime.UtcNow)
            {
                if (SpotifyAccountStatusTextBlock != null)
                {
                    SpotifyAccountStatusTextBlock.Text = $"Status: Connected (Expires {_spotifyUserTokenExpiration.Value.ToLocalTime():g})";
                    SpotifyAccountStatusTextBlock.Foreground = (Brush)FindResource("SuccessBrush");
                }
                if (DisconnectAccountButton != null) DisconnectAccountButton.Visibility = Visibility.Visible;
                if (ConnectAccountBrowserButton != null) ConnectAccountBrowserButton.Content = "Reconnect Account";
            }
            else
            {
                if (SpotifyAccountStatusTextBlock != null)
                {
                    SpotifyAccountStatusTextBlock.Text = "Status: Not Connected";
                    SpotifyAccountStatusTextBlock.Foreground = (Brush)FindResource("TextSecondaryBrush");
                }
                if (DisconnectAccountButton != null) DisconnectAccountButton.Visibility = Visibility.Collapsed;
                if (ConnectAccountBrowserButton != null) ConnectAccountBrowserButton.Content = "Connect Account";
            }

            // Update Method B (Developer API Credentials)
            if (!string.IsNullOrEmpty(_spotifyClientId) && !string.IsNullOrEmpty(_spotifyClientSecret))
            {
                if (SpotifyApiStatusTextBlock != null)
                {
                    SpotifyApiStatusTextBlock.Text = "Developer credentials configured. Click Test Connection to verify.";
                    SpotifyApiStatusTextBlock.Foreground = (Brush)FindResource("PrimaryBrush");
                }
            }
            else
            {
                if (SpotifyApiStatusTextBlock != null)
                {
                    SpotifyApiStatusTextBlock.Text = "Credentials not configured.";
                    SpotifyApiStatusTextBlock.Foreground = (Brush)FindResource("TextSecondaryBrush");
                }
            }
        }

        private void SpotifyCredentials_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isInitializing) return;
            _spotifyClientId = SpotifyClientIdTextBox?.Text?.Trim();
            _spotifyClientSecret = SpotifyClientSecretTextBox?.Text?.Trim();
            
            SaveSetting(SettingsKeySpotifyClientId, _spotifyClientId ?? "");
            SaveSetting(SettingsKeySpotifyClientSecret, _spotifyClientSecret ?? "");
            
            UpdateSpotifyStatusUi();
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            if (MainScrollViewer != null) MainScrollViewer.Visibility = Visibility.Collapsed;
            if (SettingsScrollViewer != null) SettingsScrollViewer.Visibility = Visibility.Visible;
            if (SettingsDirectoryPathTextBox != null && !string.IsNullOrEmpty(SelectedDirectory))
            {
                SettingsDirectoryPathTextBox.Text = SelectedDirectory;
            }
        }

        private void BackToDownloader_Click(object sender, RoutedEventArgs e)
        {
            if (SettingsScrollViewer != null) SettingsScrollViewer.Visibility = Visibility.Collapsed;
            if (MainScrollViewer != null) MainScrollViewer.Visibility = Visibility.Visible;
        }

        private void ConnectAccountBrowserButton_Click(object sender, RoutedEventArgs e)
        {
            // Default Spotify Client ID for standard desktop/implicit authorization
            string clientId = "ab9ad0d96a624805a7d51e8868df1f97";
            string authUrl = $"https://accounts.spotify.com/authorize?client_id={clientId}&response_type=token&redirect_uri=https://open.spotify.com/&scope=playlist-read-private%20playlist-read-collaborative%20user-library-read";
            
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = authUrl,
                    UseShellExecute = true
                });
                
                if (SpotifyTokenPastePanel != null) SpotifyTokenPastePanel.Visibility = Visibility.Visible;
                if (SpotifyRedirectUrlTextBox != null) SpotifyRedirectUrlTextBox.Text = "";
                if (SpotifyTokenErrorTextBlock != null) SpotifyTokenErrorTextBlock.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to open web browser: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void DisconnectAccountButton_Click(object sender, RoutedEventArgs e)
        {
            _spotifyUserToken = null;
            _spotifyUserTokenExpiration = null;
            
            SaveSetting(SettingsKeySpotifyUserToken, "");
            SaveSetting(SettingsKeySpotifyUserTokenExpiration, "");
            
            UpdateSpotifyStatusUi();
            
            if (SpotifyTokenPastePanel != null) SpotifyTokenPastePanel.Visibility = Visibility.Collapsed;
        }

        private void ConfirmTokenButton_Click(object sender, RoutedEventArgs e)
        {
            string url = SpotifyRedirectUrlTextBox?.Text?.Trim() ?? "";
            if (string.IsNullOrEmpty(url)) return;

            // Extract token and expires_in
            var tokenMatch = Regex.Match(url, @"access_token=([^&]+)");
            var expiresMatch = Regex.Match(url, @"expires_in=([^&]+)");

            if (tokenMatch.Success)
            {
                _spotifyUserToken = tokenMatch.Groups[1].Value;
                int seconds = 3600;
                if (expiresMatch.Success && int.TryParse(expiresMatch.Groups[1].Value, out int parsedSec))
                {
                    seconds = parsedSec;
                }
                
                _spotifyUserTokenExpiration = DateTime.UtcNow.AddSeconds(seconds - 60); // 1 minute buffer
                
                SaveSetting(SettingsKeySpotifyUserToken, _spotifyUserToken);
                SaveSetting(SettingsKeySpotifyUserTokenExpiration, _spotifyUserTokenExpiration.Value.ToString("o"));
                
                if (SpotifyTokenPastePanel != null) SpotifyTokenPastePanel.Visibility = Visibility.Collapsed;
                if (SpotifyTokenErrorTextBlock != null) SpotifyTokenErrorTextBlock.Visibility = Visibility.Collapsed;
                
                UpdateSpotifyStatusUi();
                MessageBox.Show("Successfully connected to Spotify!", "Connected", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                if (SpotifyTokenErrorTextBlock != null)
                {
                    SpotifyTokenErrorTextBlock.Text = "Invalid URL. Please make sure the URL contains 'access_token=...'.";
                    SpotifyTokenErrorTextBlock.Visibility = Visibility.Visible;
                }
            }
        }

        private async void TestApiConnectionButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_spotifyClientId) || string.IsNullOrEmpty(_spotifyClientSecret))
            {
                MessageBox.Show("Please enter both Spotify Client ID and Client Secret first.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (TestApiConnectionButton != null) TestApiConnectionButton.IsEnabled = false;
            if (SpotifyApiStatusTextBlock != null)
            {
                SpotifyApiStatusTextBlock.Text = "Testing connection...";
                SpotifyApiStatusTextBlock.Foreground = (Brush)FindResource("PrimaryBrush");
            }

            try
            {
                string? token = await _downloadService.GetSpotifyDeveloperTokenAsync(_spotifyClientId, _spotifyClientSecret);
                if (!string.IsNullOrEmpty(token))
                {
                    // Test by trying to load a known public playlist
                    bool testOk = await _downloadService.TestSpotifyApiConnectionAsync(token);
                    if (testOk)
                    {
                        if (SpotifyApiStatusTextBlock != null)
                        {
                            SpotifyApiStatusTextBlock.Text = "Connection successful! Credentials verified.";
                            SpotifyApiStatusTextBlock.Foreground = (Brush)FindResource("SuccessBrush");
                        }
                        MessageBox.Show("Connection to Spotify API was successful!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        if (SpotifyApiStatusTextBlock != null)
                        {
                            SpotifyApiStatusTextBlock.Text = "API verification failed. Check permissions or network.";
                            SpotifyApiStatusTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(239, 68, 68)); // red
                        }
                    }
                }
                else
                {
                    if (SpotifyApiStatusTextBlock != null)
                    {
                        SpotifyApiStatusTextBlock.Text = "Authentication failed. Check your Client ID and Client Secret.";
                        SpotifyApiStatusTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(239, 68, 68)); // red
                    }
                }
            }
            catch (Exception ex)
            {
                if (SpotifyApiStatusTextBlock != null)
                {
                    SpotifyApiStatusTextBlock.Text = $"Error: {ex.Message}";
                    SpotifyApiStatusTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(239, 68, 68)); // red
                }
                MessageBox.Show($"Failed to connect: {ex.Message}", "Connection Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                if (TestApiConnectionButton != null) TestApiConnectionButton.IsEnabled = true;
            }
        }
    }
}