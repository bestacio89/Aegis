namespace Aegis.Shared.Architecture.Models;

/// <summary>
/// Source scope represented by an explicitly declared JVM compiler output.
/// </summary>
public enum ProjectCompilationOutputScope
{
    Production,
    UnitTest,
    AndroidTest,
    TestFixtures
}
