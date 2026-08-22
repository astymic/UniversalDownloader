using System;
using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Forms;
using Application = System.Windows.Application;

namespace UniversalDownloader
{
    public partial class MainWindow
    {
        private NotifyIcon? _notifyIcon;
        public bool MinimizeToTrayEnabled { get; set; } = false;

        private void InitializeTrayIcon()
        {
            try
            {
                _notifyIcon = new NotifyIcon();

                System.Drawing.Icon? appIcon = null;
                // 1. Try loading from embedded executable application icon
                try
                {
                    string? procPath = Environment.ProcessPath ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                    if (!string.IsNullOrEmpty(procPath) && File.Exists(procPath))
                    {
                        appIcon = System.Drawing.Icon.ExtractAssociatedIcon(procPath);
                    }
                }
                catch { }

                // 2. Try loading from WPF application resource stream
                if (appIcon == null)
                {
                    try
                    {
                        var resStream = Application.GetResourceStream(new Uri("pack://application:,,,/akashi.ico"))?.Stream;
                        if (resStream != null)
                        {
                            appIcon = new System.Drawing.Icon(resStream);
                        }
                    }
                    catch { }
                }

                // 3. Try loading from disk
                if (appIcon == null)
                {
                    string iconPath = Path.Combine(AppContext.BaseDirectory, "akashi.ico");
                    if (File.Exists(iconPath))
                    {
                        appIcon = new Icon(iconPath);
                    }
                }

                _notifyIcon.Icon = appIcon ?? SystemIcons.Application;
                _notifyIcon.Text = "Universal Downloader";
                _notifyIcon.Visible = true;

                var contextMenu = new ContextMenuStrip();
                contextMenu.Items.Add("Open Universal Downloader", null, (s, e) => ShowAndRestoreWindow());
                contextMenu.Items.Add(new ToolStripSeparator());
                contextMenu.Items.Add("Exit", null, (s, e) => ExitApplication());

                _notifyIcon.ContextMenuStrip = contextMenu;
                _notifyIcon.DoubleClick += (s, e) => ShowAndRestoreWindow();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to initialize system tray icon: {ex.Message}");
            }
        }

        public void ShowTrayNotification(string title, string message)
        {
            try
            {
                if (_notifyIcon != null && _notifyIcon.Visible)
                {
                    _notifyIcon.ShowBalloonTip(3000, title, message, ToolTipIcon.Info);
                }
            }
            catch { }
        }

        private void ShowAndRestoreWindow()
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        }

        private void ExitApplication()
        {
            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
                _notifyIcon = null;
            }

            Application.Current.Shutdown();
        }

        private void DisposeTrayIcon()
        {
            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
                _notifyIcon = null;
            }
        }
    }
}
