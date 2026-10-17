using Aegis.Shared.Security.Live;
using System.Net;

namespace Aegis.Security.Live;

/// <summary>
/// The authorization gate. Deliberately strict: no wildcards, no ranges, no URLs, no implicit
/// targets. If a scope is vague, it is rejected rather than interpreted.
/// </summary>
public sealed class LiveScopeValidator : ILiveScopeValidator
{
    public const int MaxTargets = 50;
    public const int MaxWindowDays = 90;

    public ScopeValidationResult Validate(LiveScanScope scope, DateTimeOffset nowUtc)
    {
        var issues = new List<string>();

        // --- targets ---------------------------------------------------------------------
        if (scope.Targets.Count == 0)
            issues.Add("Add at least one target (host or IP, optionally :port).");

        if (scope.Targets.Count > MaxTargets)
            issues.Add($"A scope may list at most {MaxTargets} targets.");

        foreach (var target in scope.Targets)
        {
            var problem = ValidateTarget(target);
            if (problem is not null)
                issues.Add($"{target.Display}: {problem}");
        }

        // --- authorization ---------------------------------------------------------------
        if (string.IsNullOrWhiteSpace(scope.AuthorizationReference) || scope.AuthorizationReference.Trim().Length < 3)
            issues.Add("Enter the reference of the written authorization (contract, annex or ticket).");

        if (string.IsNullOrWhiteSpace(scope.AuthorizedBy))
            issues.Add("Enter who authorized the test on the client side.");

        if (scope.ConsentGivenAtUtc is null)
            issues.Add("Confirm that you hold written authorization for every listed target.");
        else if (scope.ConsentGivenAtUtc > nowUtc)
            issues.Add("The consent timestamp is in the future.");

        // --- window ----------------------------------------------------------------------
        if (scope.ValidUntilUtc <= scope.ValidFromUtc)
        {
            issues.Add("The testing window must end after it starts.");
        }
        else
        {
            if (scope.ValidUntilUtc - scope.ValidFromUtc > TimeSpan.FromDays(MaxWindowDays))
                issues.Add($"The testing window cannot exceed {MaxWindowDays} days.");

            if (nowUtc < scope.ValidFromUtc)
                issues.Add("The authorized testing window has not started yet.");
            else if (nowUtc > scope.ValidUntilUtc)
                issues.Add("The authorized testing window has ended.");
        }

        // --- intensity -------------------------------------------------------------------
        if (!Enum.IsDefined(scope.MaxIntensity))
            issues.Add("Unknown probe intensity.");

        return new ScopeValidationResult(issues);
    }

    public bool IsTargetInScope(LiveScanScope scope, LiveTarget target) =>
        scope.Targets.Any(t =>
            t.Port == target.Port &&
            string.Equals(t.Host.Trim(), target.Host.Trim(), StringComparison.OrdinalIgnoreCase));

    private static string? ValidateTarget(LiveTarget target)
    {
        var host = target.Host?.Trim() ?? string.Empty;

        if (host.Length == 0)
            return "host is empty.";

        if (host.Any(char.IsWhiteSpace))
            return "host contains whitespace.";

        if (host.Contains('*') || host.Contains('/') || host.Contains('\\') || host.Contains(','))
            return "wildcards, ranges and URLs are not allowed; list each host explicitly.";

        if (target.Port is < 1 or > 65535)
            return "port must be between 1 and 65535.";

        if (IPAddress.TryParse(host, out var ip))
        {
            if (IPAddress.Any.Equals(ip) || IPAddress.IPv6Any.Equals(ip) || IPAddress.Broadcast.Equals(ip))
                return "this address is not a single host.";

            var bytes = ip.GetAddressBytes();
            var isMulticast = ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
                ? bytes[0] is >= 224 and <= 239
                : ip.IsIPv6Multicast;

            if (isMulticast)
                return "multicast addresses are not allowed.";
        }

        return null;
    }
}