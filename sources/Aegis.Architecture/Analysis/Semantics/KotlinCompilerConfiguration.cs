namespace Aegis.Architecture.Analysis.Semantics;

/// <summary>
/// Validated compiler settings passed to the pinned Kotlin worker.
/// </summary>
public sealed record KotlinCompilerConfiguration(
    string CompilerVersion,
    string LanguageVersion,
    string ApiVersion,
    string JvmTarget,
    string ModuleName);
