using Xunit;

namespace Aegis.Architecture.Tests;

/// <summary>
/// Runs the packaged JDT integration test only when CI or a developer supplies
/// the private worker package directory through AEGIS_JAVA_WORKER_PACKAGE.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class RequiresJavaWorkerPackageAttribute : FactAttribute
{
    public RequiresJavaWorkerPackageAttribute()
    {
        if (string.IsNullOrWhiteSpace(
                Environment.GetEnvironmentVariable("AEGIS_JAVA_WORKER_PACKAGE")))
        {
            Skip = "Set AEGIS_JAVA_WORKER_PACKAGE to the packaged Java worker directory to run this integration test.";
        }
    }
}
