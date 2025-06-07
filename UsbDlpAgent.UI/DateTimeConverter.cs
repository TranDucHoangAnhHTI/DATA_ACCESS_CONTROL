using System;
using System.Globalization;
using System.Windows.Data;

namespace UsbDlpAgent.UI
{
    public class DateTimeConverter : IValueConverter
    {
        private static readonly TimeZoneInfo VietnamTimeZone = TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
        private static readonly CultureInfo ViCulture = new CultureInfo("vi-VN");

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is DateTime utcTime)
            {
                try
                {
                    var localTime = TimeZoneInfo.ConvertTimeFromUtc(utcTime, VietnamTimeZone);
                    return localTime.ToString("dd/MM/yyyy HH:mm:ss.fff", ViCulture);
                }
                catch
                {
                    // Fallback to UTC if conversion fails
                    return utcTime.ToString("dd/MM/yyyy HH:mm:ss.fff", ViCulture) + " UTC";
                }
            }
            return value?.ToString() ?? string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
} 