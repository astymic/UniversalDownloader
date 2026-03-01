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
    }
}