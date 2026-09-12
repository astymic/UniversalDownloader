using System;
using System.Configuration;
using System.Data;
using System.IO;
using System.Windows;

namespace Downloader
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : System.Windows.Application
    {
        public static string AppTempDirectory { get; private set; }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Clean up any RUNASADMIN shims placed by Windows Program Compatibility Assistant
            CleanupAppCompatFlags();

            // Generate a unique temp folder path for this session
            string tempPath = Path.GetTempPath();
            AppTempDirectory = Path.Combine(tempPath, $"UniversalDownloader_{Guid.NewGuid()}");

            try
            {
                if (!Directory.Exists(AppTempDirectory))
                {
                    Directory.CreateDirectory(AppTempDirectory);
                }
            }
            catch (Exception ex)
            {
                // Fallback to normal temp if access is denied
                AppTempDirectory = tempPath;
                Console.WriteLine($"Failed to create dedicated temp directory: {ex.Message}");
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                if (Directory.Exists(AppTempDirectory) && AppTempDirectory != Path.GetTempPath())
                {
                    Directory.Delete(AppTempDirectory, true);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to cleanup temp directory: {ex.Message}");
            }

            base.OnExit(e);
        }

        private static void CleanupAppCompatFlags()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers", true);
                if (key != null)
                {
                    string[] names = key.GetValueNames();
                    foreach (var name in names)
                    {
                        if (name.IndexOf("Universal Downloader", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            try { key.DeleteValue(name, false); } catch { }
                        }
                    }
                }
            }
            catch { }
        }
    }
}