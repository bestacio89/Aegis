using Aegis.Shared.Models.Rules;
using Franz.Common.DependencyInjection;

namespace Aegis.Shared.Contracts;

/// <summary>
/// Exports the final results of a scan into a chosen format.
/// </summary>
public interface IReportWriter :IScopedDependency
{
    string Format { get; }

    /// <summary>
    /// Writes the provided rule results into a destination file or stream.
    /// </summary>
    Task WriteAsync(IEnumerable<RuleResult> results, string outputPath, CancellationToken token = default);
}
