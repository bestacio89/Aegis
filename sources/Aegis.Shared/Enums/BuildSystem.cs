namespace Aegis.Shared.Enums;

/// <summary>
/// Represents the build or package management system detected in the project.
/// </summary>
public enum BuildSystem
{
    Unknown = 0,
    DotNet = 1,      // .NET SDK (csproj/sln)
    Maven = 2,       // Java Maven
    Gradle = 3,      // Java Gradle
    Npm = 4,         // Node.js / JavaScript / TypeScript
    Yarn = 5,        // Alternative JS package manager
    Pnpm = 6,        // Performance-optimized Node package manager
    Pip = 7,         // Python pip
    Poetry = 8,      // Python Poetry
    Cargo = 9,       // Rust Cargo
    Make = 10,       // C/C++ Makefile or CMake
    Bazel = 11,      // Polyglot build system
    GoMod= 12,      // Go modules
    Custom = 99      // Anything non-standard or proprietary

}
