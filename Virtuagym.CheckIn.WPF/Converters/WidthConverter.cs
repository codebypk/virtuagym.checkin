using System;
using System.Globalization;
using System.Windows.Data;

namespace Virtuagym.CheckIn.WPF.Converters
{
    /// <summary>
    /// Konvertiert die ListView-Breite in eine Spaltenbreite (abzüglich Scrollbar und Padding)
    /// </summary>
    public class WidthConverter : IValueConverter
    {
        public static readonly WidthConverter Instance = new WidthConverter();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double width && width > 0)
            {
                return width - 30; // Platz für Scrollbar und Rahmen
            }
            return 600;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
