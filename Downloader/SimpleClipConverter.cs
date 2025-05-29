using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace UniversalDownloader // Ensure namespace matches
{
    public class SimpleClipConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length < 3 ||
                !(values[0] is double actualWidth) ||
                !(values[1] is double actualHeight) ||
                !(values[2] is Orientation orientation))
            {
                return new Rect(0, 0, 0, 0); // Default to empty rect if bindings fail
            }

            if (orientation == Orientation.Vertical)
            {
                // Clip a few pixels from the bottom to avoid overlap with rounded corner
                // The amount to clip (e.g., 5) depends on the corner radius and scrollbar thickness.
                // This creates a rectangle slightly shorter than the full scrollbar height.
                double clipAmount = 5; // Adjust this value
                return new Rect(0, 0, actualWidth, Math.Max(0, actualHeight - clipAmount));
            }
            else // Horizontal
            {
                // Similarly, clip a few pixels from the right for a horizontal scrollbar
                double clipAmount = 5; // Adjust this value
                return new Rect(0, 0, Math.Max(0, actualWidth - clipAmount), actualHeight);
            }
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}