using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Aegis.Wpf.Converters;

/// <summary>
/// Maps SectionDashboardItem.ComplianceStatus ("Compliant" / "Non-Compliant") to a status color.
/// </summary>
public sealed class ComplianceStatusToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush Compliant = new(Color.FromRgb(0x3D, 0xDC, 0x8B));
    private static readonly SolidColorBrush NonCompliant = new(Color.FromRgb(0xE5, 0x48, 0x4D));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var status = value as string;
        return string.Equals(status, "Compliant", StringComparison.OrdinalIgnoreCase)
            ? Compliant
            : NonCompliant;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException("One-way converter.");
}