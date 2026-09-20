using System;
using System.Windows;

namespace UniversalDownloader.Models
{
    public class GifTextLabel
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Text { get; set; } = "";
        public double X { get; set; }
        public double Y { get; set; }
        public double NormalizedX { get; set; }
        public double NormalizedY { get; set; }
        public double FontSize { get; set; } = 28.0;
        public string TextColor { get; set; } = "#FFFFFF";
        public string FontFamily { get; set; } = "Segoe UI";
        public bool IsBold { get; set; } = true;
        public bool IsItalic { get; set; } = false;
        public bool IsUnderline { get; set; } = false;
        public bool IsStrikethrough { get; set; } = false;
        public TextAlignment Alignment { get; set; } = TextAlignment.Center;
        public bool IsSelected { get; set; } = false;
        public bool IsEditing { get; set; } = false;
        public bool IsDraft { get; set; } = false;
    }
}
