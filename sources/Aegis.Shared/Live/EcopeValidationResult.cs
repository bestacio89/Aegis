namespace Aegis.Shared.Security.Live;

public sealed record ScopeValidationResult(IReadOnlyList<string> Issues)
{
    public bool IsValid => Issues.Count == 0;

    public static ScopeValidationResult Valid { get; } = new(Array.Empty<string>());
}