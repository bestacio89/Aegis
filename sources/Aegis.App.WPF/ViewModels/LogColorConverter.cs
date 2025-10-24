using System;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows.Data;
using System.Windows.Media;
using Aegis.Shared.Architecture.Enums;

namespace Aegis.App.Wpf.ViewModels;

public class LogColorConverter : IValueConverter
{
    private static readonly Regex SeverityPattern =
        new(@"\b(Info|Low|Medium|High|Critical|Blocker)\b", RegexOptions.IgnoreCase);

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string text)
            return Brushes.White;

        // 1️⃣  Directly detect explicit RuleSeverity names inside the log
        var match = SeverityPattern.Match(text);
        if (match.Success)
        {
            var severityName = match.Value;
            if (Enum.TryParse<ArchitectureRuleSeverity>(severityName, true, out var severity))
                return MapSeverityToBrush(severity);
        }

        // 2️⃣  Fallback to keywords / emoji if present
        if (text.Contains("❌", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("error", StringComparison.OrdinalIgnoreCase))
            return Brushes.IndianRed;

        if (text.Contains("⚠️", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("warn", StringComparison.OrdinalIgnoreCase))
            return Brushes.Goldenrod;

        if (text.Contains("✅", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("success", StringComparison.OrdinalIgnoreCase))
            return Brushes.LimeGreen;

        if (text.Contains("🚀", StringComparison.OrdinalIgnoreCase))
            return Brushes.DeepSkyBlue;

        return Brushes.White;
    }

    private static Brush MapSeverityToBrush(ArchitectureRuleSeverity severity) => severity switch
    {
        ArchitectureRuleSeverity.Info => Brushes.LightGray,
        ArchitectureRuleSeverity.Low => Brushes.LightGreen,
        ArchitectureRuleSeverity.Medium => Brushes.Gold,
        ArchitectureRuleSeverity.High => Brushes.Orange,
        ArchitectureRuleSeverity.Critical => Brushes.IndianRed,
        ArchitectureRuleSeverity.Blocker => Brushes.MediumVioletRed,
        _ => Brushes.White
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
