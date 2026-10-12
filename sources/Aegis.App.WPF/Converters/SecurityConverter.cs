using Aegis.Shared.Security.Enums;
using Aegis.Wpf.Models.Security;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Aegis.Wpf.Converters;

/// <summary>SecuritySeverity -> chip colour (same palette the charts use).</summary>
public sealed class SecuritySeverityToBrushConverter : IValueConverter
{
    private static readonly Dictionary<SecuritySeverity, SolidColorBrush> Cache =
        Enum.GetValues<SecuritySeverity>().ToDictionary(s => s, s => Create(SecuritySeverityPalette.Rgb(s)));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is SecuritySeverity s && Cache.TryGetValue(s, out var brush) ? brush : Cache[SecuritySeverity.Info];

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("One-way converter.");

    private static SolidColorBrush Create(int rgb)
    {
        var brush = new SolidColorBrush(Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
        brush.Freeze();
        return brush;
    }
}

/// <summary>bool (IsCompliant) -> green / red.</summary>
public sealed class BoolToComplianceBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush Compliant = Frozen(0x3D, 0xDC, 0x8B);
    private static readonly SolidColorBrush NonCompliant = Frozen(0xE5, 0x48, 0x4D);

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Compliant : NonCompliant;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("One-way converter.");

    private static SolidColorBrush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}