using Aegis.Shared.Security.Live;

namespace Aegis.Security.Live;

/// <summary>
/// Turns the text an assessor types ("host", "host:port", "[ipv6]:port", one per line) into targets.
/// Parsing is syntactic only; whether a target is acceptable is decided by the scope validator.
/// </summary>
public static class LiveTargetParser
{
    public const int DefaultPort = 443;

    public static (IReadOnlyList<LiveTarget> Targets, IReadOnlyList<string> Errors) Parse(string? text)
    {
        var targets = new List<LiveTarget>();
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(text))
            return (targets, errors);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var raw in text.Split(new[] { '\r', '\n', ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            if (!TryParseLine(line, out var target, out var error))
            {
                errors.Add($"'{line}': {error}");
                continue;
            }

            if (seen.Add(target.Display))
                targets.Add(target);
        }

        return (targets, errors);
    }

    private static bool TryParseLine(string line, out LiveTarget target, out string error)
    {
        target = new LiveTarget(string.Empty);
        error = string.Empty;

        string host;
        string? portText = null;

        if (line.StartsWith('['))
        {
            // [ipv6] or [ipv6]:port
            var close = line.IndexOf(']');
            if (close < 0)
            {
                error = "missing closing ']'.";
                return false;
            }

            host = line[1..close];
            var rest = line[(close + 1)..];

            if (rest.Length > 0)
            {
                if (!rest.StartsWith(':'))
                {
                    error = "unexpected text after ']'.";
                    return false;
                }

                portText = rest[1..];
            }
        }
        else if (line.Count(c => c == ':') == 1)
        {
            var idx = line.IndexOf(':');
            host = line[..idx];
            portText = line[(idx + 1)..];
        }
        else
        {
            // plain hostname/IPv4, or a bare IPv6 literal with default port
            host = line;
        }

        var port = DefaultPort;
        if (portText is not null && !int.TryParse(portText, out port))
        {
            error = "the port is not a number.";
            return false;
        }

        target = new LiveTarget(host.Trim(), port);
        return true;
    }
}