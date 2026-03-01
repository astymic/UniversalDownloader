using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace UniversalDownloader
{
    public partial class MainWindow
    {
        private const string SettingsKeyLastDownloadPath = "LastDownloadPath";

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
                    if (settings.TryGetValue(SettingsKeyLastDownloadPath, out JToken? pathToken))
                    {
                        string? savedPath = pathToken?.ToString();
                        if (savedPath != null && Directory.Exists(savedPath)) SelectedDirectory = savedPath;
                        else SelectedDirectory = null;
                    }
                }
                catch (Exception ex) { Debug.WriteLine($"Error loading settings: {ex.Message}"); SelectedDirectory = null; }
            }
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
    }
}