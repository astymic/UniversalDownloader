using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Newtonsoft.Json.Linq;

namespace UniversalDownloader
{
    public partial class MainWindow
    {
        private const string SettingsKeyLastDownloadPath = "LastDownloadPath";
        private const string SettingsKeyAutoDetectClipboard = "AutoDetectClipboard";
        private const string SettingsKeyEmbedMetadata = "EmbedMetadata";
        private const string SettingsKeyDownloadSubtitles = "DownloadSubtitles";
        private const string SettingsKeyMinimizeToTray = "MinimizeToTray";
        private const string SettingsKeyMultiConnectionAcceleration = "MultiConnectionAcceleration";
        private const string SettingsKeyDownloadLyrics = "DownloadLyrics";
        private const string SettingsKeyFilenameTemplate = "FilenameTemplate";

        public bool AutoDetectClipboardEnabled { get; set; } = true;
        public bool EmbedMetadataEnabled { get; set; } = true;
        public bool DownloadSubtitlesEnabled { get; set; } = false;
        public bool MultiConnectionAccelerationEnabled { get; set; } = true;
        public bool DownloadLyricsEnabled { get; set; } = true;
        public string FilenameTemplate { get; set; } = "{title}";

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

                    if (settings.TryGetValue(SettingsKeyAutoDetectClipboard, out JToken? clipToken) && clipToken != null)
                    {
                        AutoDetectClipboardEnabled = clipToken.ToObject<bool>();
                        if (_clipboardMonitor != null) _clipboardMonitor.IsEnabled = AutoDetectClipboardEnabled;
                    }

                    if (settings.TryGetValue(SettingsKeyEmbedMetadata, out JToken? metaToken) && metaToken != null)
                    {
                        EmbedMetadataEnabled = metaToken.ToObject<bool>();
                    }

                    if (settings.TryGetValue(SettingsKeyDownloadSubtitles, out JToken? subToken) && subToken != null)
                    {
                        DownloadSubtitlesEnabled = subToken.ToObject<bool>();
                    }

                    if (settings.TryGetValue(SettingsKeyMinimizeToTray, out JToken? trayToken) && trayToken != null)
                    {
                        MinimizeToTrayEnabled = trayToken.ToObject<bool>();
                    }

                    if (settings.TryGetValue(SettingsKeyMultiConnectionAcceleration, out JToken? accelToken) && accelToken != null)
                    {
                        MultiConnectionAccelerationEnabled = accelToken.ToObject<bool>();
                        if (_downloadService != null) _downloadService.EnableMultiConnectionAcceleration = MultiConnectionAccelerationEnabled;
                    }

                    if (settings.TryGetValue(SettingsKeyDownloadLyrics, out JToken? lyricsToken) && lyricsToken != null)
                    {
                        DownloadLyricsEnabled = lyricsToken.ToObject<bool>();
                    }

                    if (settings.TryGetValue(SettingsKeyFilenameTemplate, out JToken? tmplToken) && tmplToken != null)
                    {
                        string? t = tmplToken.ToString();
                        if (!string.IsNullOrWhiteSpace(t))
                        {
                            FilenameTemplate = t;
                        }
                    }

                    if (_downloadService != null)
                    {
                        _downloadService.CustomFilenameTemplate = FilenameTemplate;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error loading settings: {ex.Message}");
                    SelectedDirectory = null;
                }
            }
        }

        private async Task SaveSettingAsync(string key, string value)
        {
            try
            {
                string settingsFile = GetSettingsFilePath();
                JObject settings;
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
            string settingsFile = GetSettingsFilePath();
            JObject settings;
            if (File.Exists(settingsFile))
            {
                try { settings = JObject.Parse(File.ReadAllText(settingsFile)); } catch { settings = new JObject(); }
            }
            else { settings = new JObject(); }
            settings[key] = value;
            try { File.WriteAllText(settingsFile, settings.ToString(Newtonsoft.Json.Formatting.Indented)); }
            catch (Exception ex) { Debug.WriteLine($"Failed to write setting '{key}': {ex.Message}"); }
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            if (MainScrollViewer != null) MainScrollViewer.Visibility = Visibility.Collapsed;
            if (HistoryScrollViewer != null) HistoryScrollViewer.Visibility = Visibility.Collapsed;
            if (ConverterScrollViewer != null) ConverterScrollViewer.Visibility = Visibility.Collapsed;
            if (QueueScrollViewer != null) QueueScrollViewer.Visibility = Visibility.Collapsed;
            if (SearchScrollViewer != null) SearchScrollViewer.Visibility = Visibility.Collapsed;
            if (SettingsScrollViewer != null) SettingsScrollViewer.Visibility = Visibility.Visible;
            if (SettingsDirectoryPathTextBox != null && !string.IsNullOrEmpty(SelectedDirectory))
            {
                SettingsDirectoryPathTextBox.Text = SelectedDirectory;
            }

            if (AutoDetectClipboardCheckBox != null) AutoDetectClipboardCheckBox.IsChecked = AutoDetectClipboardEnabled;
            if (EmbedMetadataCheckBox != null) EmbedMetadataCheckBox.IsChecked = EmbedMetadataEnabled;
            if (DownloadSubtitlesCheckBox != null) DownloadSubtitlesCheckBox.IsChecked = DownloadSubtitlesEnabled;
            if (MinimizeToTrayCheckBox != null) MinimizeToTrayCheckBox.IsChecked = MinimizeToTrayEnabled;
            if (MultiConnectionAccelerationCheckBox != null) MultiConnectionAccelerationCheckBox.IsChecked = MultiConnectionAccelerationEnabled;
            if (DownloadLyricsCheckBox != null) DownloadLyricsCheckBox.IsChecked = DownloadLyricsEnabled;
            if (FilenameTemplateTextBox != null) FilenameTemplateTextBox.Text = FilenameTemplate;
        }

        private void BackToDownloader_Click(object sender, RoutedEventArgs e)
        {
            if (SettingsScrollViewer != null) SettingsScrollViewer.Visibility = Visibility.Collapsed;
            if (HistoryScrollViewer != null) HistoryScrollViewer.Visibility = Visibility.Collapsed;
            if (ConverterScrollViewer != null) ConverterScrollViewer.Visibility = Visibility.Collapsed;
            if (QueueScrollViewer != null) QueueScrollViewer.Visibility = Visibility.Collapsed;
            if (MainScrollViewer != null) MainScrollViewer.Visibility = Visibility.Visible;
        }

        private void SettingCheckbox_Changed(object sender, RoutedEventArgs e)
        {
            if (AutoDetectClipboardCheckBox != null)
            {
                AutoDetectClipboardEnabled = AutoDetectClipboardCheckBox.IsChecked == true;
                SaveSetting(SettingsKeyAutoDetectClipboard, AutoDetectClipboardEnabled.ToString().ToLower());
                if (_clipboardMonitor != null) _clipboardMonitor.IsEnabled = AutoDetectClipboardEnabled;
            }

            if (EmbedMetadataCheckBox != null)
            {
                EmbedMetadataEnabled = EmbedMetadataCheckBox.IsChecked == true;
                SaveSetting(SettingsKeyEmbedMetadata, EmbedMetadataEnabled.ToString().ToLower());
            }

            if (DownloadSubtitlesCheckBox != null)
            {
                DownloadSubtitlesEnabled = DownloadSubtitlesCheckBox.IsChecked == true;
                SaveSetting(SettingsKeyDownloadSubtitles, DownloadSubtitlesEnabled.ToString().ToLower());
            }

            if (MinimizeToTrayCheckBox != null)
            {
                MinimizeToTrayEnabled = MinimizeToTrayCheckBox.IsChecked == true;
                SaveSetting(SettingsKeyMinimizeToTray, MinimizeToTrayEnabled.ToString().ToLower());
            }

            if (MultiConnectionAccelerationCheckBox != null)
            {
                MultiConnectionAccelerationEnabled = MultiConnectionAccelerationCheckBox.IsChecked == true;
                SaveSetting(SettingsKeyMultiConnectionAcceleration, MultiConnectionAccelerationEnabled.ToString().ToLower());
                if (_downloadService != null) _downloadService.EnableMultiConnectionAcceleration = MultiConnectionAccelerationEnabled;
            }

            if (DownloadLyricsCheckBox != null)
            {
                DownloadLyricsEnabled = DownloadLyricsCheckBox.IsChecked == true;
                SaveSetting(SettingsKeyDownloadLyrics, DownloadLyricsEnabled.ToString().ToLower());
            }
        }

        private void FilenameTemplateTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (FilenameTemplateTextBox != null)
            {
                string tmpl = FilenameTemplateTextBox.Text?.Trim() ?? "{title}";
                if (string.IsNullOrWhiteSpace(tmpl)) tmpl = "{title}";
                FilenameTemplate = tmpl;
                if (_downloadService != null) _downloadService.CustomFilenameTemplate = FilenameTemplate;
                SaveSetting(SettingsKeyFilenameTemplate, FilenameTemplate);
            }
        }

        private void TemplateChip_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag != null && FilenameTemplateTextBox != null)
            {
                string token = btn.Tag.ToString() ?? "";
                if (!string.IsNullOrEmpty(token))
                {
                    int caret = FilenameTemplateTextBox.CaretIndex;
                    string current = FilenameTemplateTextBox.Text ?? "";
                    FilenameTemplateTextBox.Text = current.Insert(caret, token);
                    FilenameTemplateTextBox.CaretIndex = caret + token.Length;
                    FilenameTemplateTextBox.Focus();
                }
            }
        }

        private void ResetTemplate_Click(object sender, RoutedEventArgs e)
        {
            FilenameTemplate = "{title}";
            if (FilenameTemplateTextBox != null)
            {
                FilenameTemplateTextBox.Text = "{title}";
            }
            if (_downloadService != null) _downloadService.CustomFilenameTemplate = "{title}";
            SaveSetting(SettingsKeyFilenameTemplate, "{title}");
        }

        private async void UpdateYtDlp_Click(object sender, RoutedEventArgs e)
        {
            if (SettingsYtDlpStatus != null)
            {
                SettingsYtDlpStatus.Text = "Checking for updates...";
            }
            try
            {
                await _dependencyManager.InitializeDependenciesAsync();
                if (SettingsYtDlpStatus != null)
                {
                    SettingsYtDlpStatus.Text = "Ready (Updated)";
                }
            }
            catch (Exception ex)
            {
                if (SettingsYtDlpStatus != null)
                {
                    SettingsYtDlpStatus.Text = $"Update failed: {ex.Message}";
                }
            }
        }
    }
}
