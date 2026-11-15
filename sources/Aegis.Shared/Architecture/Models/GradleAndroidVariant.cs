namespace Aegis.Shared.Architecture.Models;

/// <summary>
/// A statically derived Android build variant. This represents the Cartesian
/// product of declared product flavors and build types; it is not proof that
/// Gradle successfully created or selected the variant.
/// </summary>
public sealed record GradleAndroidVariant(
    string Name,
    List<string> ProductFlavors,
    string BuildType);
