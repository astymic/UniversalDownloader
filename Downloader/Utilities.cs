using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;

namespace UniversalDownloader
{
    public static class Utilities
    {
        // Helper method to find a visual child of a specific type
        public static T FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null) return null;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, i);
                if (child != null && child is T) return (T)child;
                else
                {
                    T childOfChild = FindVisualChild<T>(child);
                    if (childOfChild != null) return childOfChild;
                }
            }
            return null;
        }

        public static long ParseYtDlpSizeStringToBytes(string sizeString)
        {
            if (string.IsNullOrWhiteSpace(sizeString)) { return 0; }
            var match = Regex.Match(sizeString.Trim(), @"^(?<size>[\d\.]+)\s*(?<unit>[KMGT]?i?B)$", RegexOptions.IgnoreCase);
            if (!match.Success) { return 0; }
            if (!double.TryParse(match.Groups["size"].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double sizeValue)) { return 0; }
            string unit = match.Groups["unit"].Value.ToUpperInvariant();
            long multiplier = 1L;
            if (unit == "B") { multiplier = 1L; }
            else if (unit == "KIB" || unit == "KB") { multiplier = 1024L; }
            else if (unit == "MIB" || unit == "MB") { multiplier = 1024L * 1024L; }
            else if (unit == "GIB" || unit == "GB") { multiplier = 1024L * 1024L * 1024L; }
            else if (unit == "TIB" || unit == "TB") { multiplier = 1024L * 1024L * 1024L * 1024L; }
            return (long)(sizeValue * multiplier);
        }

        public static string FormatBytesOutput(long bytes)
        {
            string[] suffixes = { "B", "KiB", "MiB", "GiB", "TiB", "PiB", "EiB" };
            int i = 0;
            double dblBytes = bytes;
            if (bytes == 0) { return "0 B"; }
            while (i < suffixes.Length - 1 && dblBytes >= 1024) { dblBytes /= 1024; i++; }
            return $"{dblBytes:F2} {suffixes[i]}";
        }

        public static string SanitizeFileName(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) { return "downloaded_file"; }
            fileName = fileName.Trim('"');
            string invalidCharsPattern = string.Format("[{0}]", Regex.Escape(new string(Path.GetInvalidFileNameChars())));
            fileName = Regex.Replace(fileName, invalidCharsPattern, "_");
            fileName = Regex.Replace(fileName, @"\.+$|^\.+$", "_");
            fileName = fileName.Trim();
            if (fileName.Length > 150) { fileName = fileName.Substring(0, 150).TrimEnd('_'); }
            return string.IsNullOrWhiteSpace(fileName) ? "downloaded_file" : fileName;
        }

        public static string GetExtensionFromMimeType(string mimeType)
        {
            if (string.IsNullOrWhiteSpace(mimeType)) { return ".dat"; }
            var mapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                {"image/jpeg", ".jpg"}, {"image/png", ".png"}, {"image/gif", ".gif"}, {"image/bmp", ".bmp"},
                {"application/pdf", ".pdf"}, {"application/zip", ".zip"}, {"application/x-zip-compressed", ".zip"},
                {"application/msword", ".doc"}, {"application/vnd.openxmlformats-officedocument.wordprocessingml.document", ".docx"},
                {"application/vnd.ms-excel", ".xls"}, {"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", ".xlsx"},
                {"text/plain", ".txt"}, {"text/html", ".html"}, {"text/xml", ".xml"},
                {"video/mp4", ".mp4"}, {"video/mpeg", ".mpeg"}, {"video/quicktime", ".mov"}, {"video/x-msvideo", ".avi"},
                {"video/webm", ".webm"}, {"video/x-matroska", ".mkv"},
                {"audio/mpeg", ".mp3"}, {"audio/wav", ".wav"}, {"audio/ogg", ".ogg"}, {"audio/aac", ".aac"}, {"audio/webm", ".webm"}
            };
            return mapping.TryGetValue(mimeType.Split(';')[0].Trim(), out var extension) ? extension : ".dat";
        }
    }
}