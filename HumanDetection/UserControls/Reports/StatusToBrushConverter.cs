using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace UserControls.Reports
{
    public class StatusToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string status = value as string ?? "";

            switch (status)
            {
                case "Success":
                    return new SolidColorBrush(Color.FromRgb(22, 101, 52));
                case "Failed":
                    return new SolidColorBrush(Color.FromRgb(127, 29, 29));
                case "LessScore":
                    return new SolidColorBrush(Color.FromRgb(120, 80, 14));
                default:
                    return new SolidColorBrush(Color.FromRgb(30, 30, 50));
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
