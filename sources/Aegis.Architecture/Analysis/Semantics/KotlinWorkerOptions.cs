namespace Aegis.Architecture.Analysis.Semantics;

/// <summary>Deployment and resource limits for the isolated Kotlin K2 worker.</summary>
public sealed class KotlinWorkerOptions
{
    public string JavaExecutablePath { get; set; } = ResolveDefaultJavaExecutable();

    public string WorkerJarPath { get; set; } =
        Path.Combine(ResolveJavaPackageDirectory(), "Kotlin", "kotlin-worker.jar");

    public string WorkerLibrariesDirectory { get; set; } =
        Path.Combine(ResolveJavaPackageDirectory(), "Kotlin", "lib");

    public string KotlinStandardLibraryPath { get; set; } =
        Path.Combine(ResolveJavaPackageDirectory(), "Kotlin", "lib", "kotlin-stdlib-2.4.21.jar");

    public string MaxHeapSize { get; set; } = "1024m";

    public TimeSpan StartupTimeout { get; set; } = TimeSpan.FromSeconds(30);

    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromMinutes(2);

    public int MaxFrameBytes { get; set; } = 1_048_576;

    public int MaxResponseBytes { get; set; } = 67_108_864;

    public bool IsConfigured =>
        File.Exists(WorkerJarPath)
        && Directory.Exists(WorkerLibrariesDirectory)
        && File.Exists(KotlinStandardLibraryPath)
        && File.Exists(Path.Combine(
            WorkerLibrariesDirectory,
            "kotlin-compiler-embeddable-2.4.21.jar"))
        && File.Exists(Path.Combine(
            WorkerLibrariesDirectory,
            "gson-2.13.2.jar"));

    private static bool IsValidHeapSize(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length < 2)
            return false;

        var suffix = char.ToLowerInvariant(value[^1]);
        if (suffix is not ('k' or 'm' or 'g'))
            return false;

        return long.TryParse(value.AsSpan(0, value.Length - 1), out var size) && size > 0;
    }

    private static string ResolveDefaultJavaExecutable()
    {
        var packageDirectory = ResolveJavaPackageDirectory();
        var executableName = OperatingSystem.IsWindows() ? "java.exe" : "java";
        var packagedRuntime = Path.Combine(packageDirectory, "runtime", "bin", executableName);
        return File.Exists(packagedRuntime) ? packagedRuntime : "java";
    }

    private static string ResolveJavaPackageDirectory()
    {
        var configuredDirectory = Environment.GetEnvironmentVariable("AEGIS_JAVA_WORKER_PACKAGE");
        return Path.GetFullPath(string.IsNullOrWhiteSpace(configuredDirectory)
            ? Path.Combine(AppContext.BaseDirectory, "Java")
            : configuredDirectory);
    }
}
