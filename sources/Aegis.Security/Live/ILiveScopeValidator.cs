using Aegis.Shared.Security.Live;

namespace Aegis.Security.Live;

public interface ILiveScopeValidator
{
    /// <summary>Checks that a scope is complete, authorized, and currently in its window.</summary>
    ScopeValidationResult Validate(LiveScanScope scope, DateTimeOffset nowUtc);

    /// <summary>True only when the exact host and port were listed in the scope.</summary>
    bool IsTargetInScope(LiveScanScope scope, LiveTarget target);
}