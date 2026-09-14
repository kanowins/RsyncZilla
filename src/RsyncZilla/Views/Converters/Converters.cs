using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using RsyncZilla.Models;

namespace RsyncZilla.Views.Converters
{
    public class InverseBooleanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b) return !b;
            return true;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b) return !b;
            return false;
        }
    }

    public class StatusToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is TransferStatus status)
            {
                return status switch
                {
                    TransferStatus.Completed => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2E7D32")),
                    TransferStatus.Running => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0066CC")),
                    TransferStatus.Failed => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#C62828")),
                    TransferStatus.Cancelled => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E65100")),
                    _ => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#616161"))
                };
            }
            return new SolidColorBrush(Colors.Gray);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }
}
