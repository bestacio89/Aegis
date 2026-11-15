namespace Aegis.Architecture.Analysis.Semantics;

/// <summary>
/// Compile-time dependency scope selected for one Android build variant.
/// </summary>
public enum AndroidVariantDependencyScope
{
    Production,
    UnitTest,
    AndroidTest,
    TestFixtures
}
