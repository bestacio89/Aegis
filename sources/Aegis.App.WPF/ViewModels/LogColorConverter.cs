using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Aegis.Wpf.ViewModels;

public sealed class LogColorConverter : IValueConverter
{
    public object Convert(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture)
    {
        if (value is not string text)
            return Brushes.White;


        if (text.Contains(
                "[FATAL]",
                StringComparison.OrdinalIgnoreCase))
        {
            return Brushes.DarkRed;
        }


        if (text.Contains(
                "[ERROR]",
                StringComparison.OrdinalIgnoreCase))
        {
            return Brushes.IndianRed;
        }


        if (text.Contains(
                "[WARN]",
                StringComparison.OrdinalIgnoreCase))
        {
            return Brushes.Goldenrod;
        }


        if (text.Contains(
                "[SUCCESS]",
                StringComparison.OrdinalIgnoreCase))
        {
            return Brushes.LimeGreen;
        }


        if (text.Contains(
                "[EXEC]",
                StringComparison.OrdinalIgnoreCase))
        {
            return Brushes.DeepSkyBlue;
        }


        if (text.Contains(
                "[DEBUG]",
                StringComparison.OrdinalIgnoreCase))
        {
            return Brushes.Gray;
        }


        return Brushes.White;
    }


    public object ConvertBack(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture)
        => throw new NotSupportedException();
}