using System.Text.RegularExpressions;

namespace Aegis.Architecture.Evaluators.Analysis;

/// <summary>
/// Masks comments and string literals while preserving source offsets and line endings.
/// Lexical detectors can use the masked text for token discovery and the original
/// source for exact evidence extraction.
/// </summary>
internal static class SourceCodeMasker
{
    private static readonly Regex NonCodeRegex = new(
        """"(?s)(?:\x22{3}.*?\x22{3}|\x27{3}.*?\x27{3}|\x60(?:\\.|[^\x60\\])*\x60|@?"(?:""|\\.|[^"\\])*"|'(?:\\.|[^'\\])*'|//[^\r\n]*|/\*.*?\*/|\#[^\r\n]*)"""",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string Mask(string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        return NonCodeRegex.Replace(content, match =>
            string.Create(
                match.Length,
                match.Value,
                static (span, value) =>
                {
                    for (var index = 0; index < value.Length; index++)
                    {
                        span[index] = value[index] is '\r' or '\n'
                            ? value[index]
                            : ' ';
                    }
                }));
    }
}
