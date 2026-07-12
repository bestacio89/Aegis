using Aegis.Shared.Architecture.Enums;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Aegis.Wpf.Converters;

/// <summary>
/// Maps ArchitectureRuleSeverity to a status chip color. Deliberately separate from the
/// brand/structural accent (AccentStructural) so severity signal never gets diluted by brand color.
/// </summary>
public sealed class SeverityToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush Info = new(Color.FromRgb(0x89, 0x93, 0xA6));
    private static readonly SolidColorBrush Low = new(Color.FromRgb(0x4F, 0xA8, 0xD8));
    private static readonly SolidColorBrush Medium = new(Color.FromRgb(0xE8, 0xA2, 0x3D));
    private static readonly SolidColorBrush High = new(Color.FromRgb(0xE5, 0x84, 0x4D));
    private static readonly SolidColorBrush Critical = new(Color.FromRgb(0xE5, 0x48, 0x4D));
    private static readonly SolidColorBrush Blocker = new(Color.FromRgb(0xFF, 0x3B, 0x5C));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not ArchitectureRuleSeverity severity)
            return Info;

        return severity switch
        {
            ArchitectureRuleSeverity.Info => Info,
            ArchitectureRuleSeverity.Low => Low,
            ArchitectureRuleSeverity.Medium => Medium,
            ArchitectureRuleSeverity.High => High,
            ArchitectureRuleSeverity.Critical => Critical,
            ArchitectureRuleSeverity.Blocker => Blocker,
            _ => Info
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException("One-way converter.");
}