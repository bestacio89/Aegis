namespace Aegis.Shared.Extensions;

/// <summary>
/// Provides utility methods for common LINQ operations.
/// </summary>
public static class EnumerableExtensions
{
    public static double AverageOrDefault(this IEnumerable<double> source)
        => source.Any() ? source.Average() : 0.0;

    public static double MedianOrDefault(this IEnumerable<double> source)
    {
        var sorted = source.OrderBy(x => x).ToArray();
        if (sorted.Length == 0) return 0;
        var mid = sorted.Length / 2;
        return sorted.Length % 2 == 0 ? (sorted[mid - 1] + sorted[mid]) / 2.0 : sorted[mid];
    }
}
