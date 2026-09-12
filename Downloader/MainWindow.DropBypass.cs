using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace UniversalDownloader
{
    public partial class MainWindow
    {
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ChangeWindowMessageFilter(uint message, uint dwFlag);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ChangeWindowMessageFilterEx(IntPtr hWnd, uint msg, uint action, IntPtr pChangeFilterStruct);

        [DllImport("shell32.dll")]
        private static extern void DragAcceptFiles(IntPtr hWnd, [MarshalAs(UnmanagedType.Bool)] bool fAccept);

        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        private static extern uint DragQueryFile(IntPtr hDrop, uint iFile, StringBuilder? lpszFile, uint cch);

        [DllImport("shell32.dll")]
        private static extern void DragFinish(IntPtr hDrop);

        [DllImport("shell32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DragQueryPoint(IntPtr hDrop, out POINT lppt);

        [DllImport("ole32.dll")]
        private static extern int RevokeDragDrop(IntPtr hWnd);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        internal const uint WM_DROPFILES = 0x0233;
        private const uint WM_COPYDATA = 0x004A;
        private const uint WM_COPYGLOBALDATA = 0x0049;
        private const uint MSGFLT_ADD = 1;
        private const uint MSGFLT_ALLOW = 1;

        /// <summary>
        /// Checks if the current process is running with elevated Administrator privileges.
        /// </summary>
        public static bool IsProcessElevated()
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Configures Win32 UIPI filters and shell drag-accept flags so that Windows Explorer
        /// can send WM_DROPFILES across integrity levels even when Universal Downloader is
        /// launched with elevated Administrator permissions in corporate environments.
        /// </summary>
        private void InitializeElevatedDragDropBypass(IntPtr hWnd)
        {
            try
            {
                // Unblock Drag & Drop messages in process-wide message filter (Windows Vista+)
                ChangeWindowMessageFilter(WM_DROPFILES, MSGFLT_ADD);
                ChangeWindowMessageFilter(WM_COPYDATA, MSGFLT_ADD);
                ChangeWindowMessageFilter(WM_COPYGLOBALDATA, MSGFLT_ADD);
            }
            catch { }

            try
            {
                // Unblock Drag & Drop messages for this specific window handle (Windows 7+)
                ChangeWindowMessageFilterEx(hWnd, WM_DROPFILES, MSGFLT_ALLOW, IntPtr.Zero);
                ChangeWindowMessageFilterEx(hWnd, WM_COPYDATA, MSGFLT_ALLOW, IntPtr.Zero);
                ChangeWindowMessageFilterEx(hWnd, WM_COPYGLOBALDATA, MSGFLT_ALLOW, IntPtr.Zero);

                // Register window with Windows Shell to receive WM_DROPFILES messages
                DragAcceptFiles(hWnd, true);
            }
            catch { }

            ApplyElevatedDragDropFix();
        }

        /// <summary>
        /// When running elevated, Windows UIPI blocks OLE Drag & Drop (showing the 🚫 cursor).
        /// To bypass this, we revoke the OLE IDropTarget from the HWND and disable AllowDrop
        /// on WPF elements, forcing Windows Shell to use Win32 DragAcceptFiles + WM_DROPFILES.
        /// </summary>
        public void ApplyElevatedDragDropFix()
        {
            if (!IsProcessElevated()) return;

            var handle = new WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero) return;

            try
            {
                // Disable WPF AllowDrop on controls so WPF does not re-register OLE IDropTarget
                if (ConverterDropZone != null) ConverterDropZone.AllowDrop = false;
                if (CompressorDropZone != null) CompressorDropZone.AllowDrop = false;
                if (GifDropZone != null) GifDropZone.AllowDrop = false;
                if (ConverterScrollViewer != null) ConverterScrollViewer.AllowDrop = false;
                if (CompressorScrollViewer != null) CompressorScrollViewer.AllowDrop = false;
                if (GifWebpScrollViewer != null) GifWebpScrollViewer.AllowDrop = false;
            }
            catch { }

            try
            {
                // Revoke OLE drop target on this HWND
                RevokeDragDrop(handle);
            }
            catch { }

            try
            {
                // Ensure shell DragAcceptFiles is active
                DragAcceptFiles(handle, true);
            }
            catch { }
        }

        private void HandleNativeDropFiles(IntPtr hDrop)
        {
            try
            {
                uint fileCount = DragQueryFile(hDrop, 0xFFFFFFFF, null, 0);
                if (fileCount == 0)
                {
                    DragFinish(hDrop);
                    return;
                }

                var files = new List<string>((int)fileCount);
                for (uint i = 0; i < fileCount; i++)
                {
                    var sb = new StringBuilder(1024);
                    if (DragQueryFile(hDrop, i, sb, (uint)sb.Capacity) > 0)
                    {
                        string path = sb.ToString();
                        if (!string.IsNullOrWhiteSpace(path))
                        {
                            files.Add(path);
                        }
                    }
                }

                DragQueryPoint(hDrop, out POINT pt);
                DragFinish(hDrop);

                if (files.Count > 0)
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        RouteNativeDroppedFiles(files, pt);
                    }));
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DropBypass] Error handling native drop files: {ex}");
            }
        }

        private void RouteNativeDroppedFiles(List<string> files, POINT pt)
        {
            if (files == null || files.Count == 0) return;

            // Convert physical device coordinates to WPF DIPs based on display scaling
            double dpiX = 1.0;
            double dpiY = 1.0;
            try
            {
                PresentationSource source = PresentationSource.FromVisual(this);
                if (source?.CompositionTarget != null)
                {
                    dpiX = source.CompositionTarget.TransformToDevice.M11;
                    dpiY = source.CompositionTarget.TransformToDevice.M22;
                }
            }
            catch { }

            // 1. Try HitTest first to see if dropped directly over a specific drop zone or view
            try
            {
                var wpfPoint = new System.Windows.Point(pt.X / dpiX, pt.Y / dpiY);
                HitTestResult? hit = VisualTreeHelper.HitTest(this, wpfPoint);
                if (hit != null && hit.VisualHit is DependencyObject hitObj)
                {
                    if (IsDescendantOf(hitObj, CompressorDropZone) || IsDescendantOf(hitObj, CompressorScrollViewer))
                    {
                        AddFilesToCompressor(files);
                        return;
                    }
                    if (IsDescendantOf(hitObj, ConverterDropZone) || IsDescendantOf(hitObj, ConverterScrollViewer))
                    {
                        AddFilesToConverter(files);
                        return;
                    }
                    if (IsDescendantOf(hitObj, GifDropZone) || IsDescendantOf(hitObj, GifWebpScrollViewer))
                    {
                        RouteToGifStudio(files);
                        return;
                    }
                }
            }
            catch { }

            // 2. Fallback based on which view/tab is currently open
            if (CompressorScrollViewer != null && CompressorScrollViewer.Visibility == Visibility.Visible)
            {
                AddFilesToCompressor(files);
                return;
            }

            if (ConverterScrollViewer != null && ConverterScrollViewer.Visibility == Visibility.Visible)
            {
                AddFilesToConverter(files);
                return;
            }

            if (GifWebpScrollViewer != null && GifWebpScrollViewer.Visibility == Visibility.Visible)
            {
                RouteToGifStudio(files);
                return;
            }

            // 3. If on Main or other view: check file types
            string firstExt = Path.GetExtension(files[0])?.ToLowerInvariant() ?? "";
            if (SupportedCompressorExtensions.Contains(firstExt))
            {
                CompressorButton_Click(this, new RoutedEventArgs());
                AddFilesToCompressor(files);
            }
            else if (SupportedConverterExtensions.Contains(firstExt))
            {
                ConverterButton_Click(this, new RoutedEventArgs());
                AddFilesToConverter(files);
            }
        }

        private void RouteToGifStudio(List<string> files)
        {
            string? firstVideo = files.FirstOrDefault(p =>
            {
                string ext = Path.GetExtension(p).ToLowerInvariant();
                return ext is ".mp4" or ".mkv" or ".mov" or ".webm" or ".avi" or ".flv" or ".wmv" or ".ts" or ".m4v";
            }) ?? files[0];

            if (File.Exists(firstVideo))
            {
                LoadVideoIntoGifCreator(firstVideo);
            }
        }

        private static bool IsDescendantOf(DependencyObject obj, DependencyObject? targetAncestor)
        {
            if (targetAncestor == null) return false;
            DependencyObject? current = obj;
            while (current != null)
            {
                if (ReferenceEquals(current, targetAncestor)) return true;
                current = VisualTreeHelper.GetParent(current) ?? LogicalTreeHelper.GetParent(current);
            }
            return false;
        }
    }
}
