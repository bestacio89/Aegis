using Xunit;

namespace Aegis.Architecture.Tests;

/// <summary>
/// Runs the real Kotlin worker integration only when the private JVM worker
/// package has been built and exposed through AEGIS_JAVA_WORKER_PACKAGE.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class RequiresKotlinWorkerPackageAttribute : FactAttribute
{
    public RequiresKotlinWorkerPackageAttribute()
    {
        var packageDirectory = Environment.GetEnvironmentVariable("AEGIS_JAVA_WORKER_PACKAGE");
        if (string.IsNullOrWhiteSpace(packageDirectory))
        {
            Skip = "Set AEGIS_JAVA_WORKER_PACKAGE to the packaged Java/Kotlin worker directory to run this integration test.";
            return;
        }

        var kotlinDirectory = Path.Combine(packageDirectory, "Kotlin");
        if (!File.Exists(Path.Combine(kotlinDirectory, "kotlin-worker.jar"))
            || !File.Exists(Path.Combine(kotlinDirectory, "lib", "kotlin-stdlib-2.4.21.jar"))
            || !File.Exists(Path.Combine(kotlinDirectory, "lib", "kotlin-compiler-embeddable-2.4.21.jar"))
            || !File.Exists(Path.Combine(kotlinDirectory, "lib", "gson-2.13.2.jar"))
            || !File.Exists(Path.Combine(kotlinDirectory, "sbom.cdx.json"))
            || !File.Exists(Path.Combine(kotlinDirectory, "THIRD-PARTY-NOTICES.md"))
            || !File.Exists(Path.Combine(kotlinDirectory, "worker-manifest.json")))
        {
            Skip = "The Kotlin worker package is incomplete; build it with the repository JVM worker packaging action.";
        }
    }
}
