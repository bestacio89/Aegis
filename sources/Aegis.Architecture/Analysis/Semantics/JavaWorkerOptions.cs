namespace Aegis.Architecture.Analysis.Semantics;

/// <summary>Deployment and resource limits for the isolated Eclipse JDT worker.</summary>
public sealed class JavaWorkerOptions
{
    public string JavaExecutablePath { get; set; } = ResolveDefaultJavaExecutable();

    public string WorkerClassesDirectory { get; set; } =
        Path.Combine(AppContext.BaseDirectory, "Java", "worker", "classes");

    public string WorkerLibrariesDirectory { get; set; } =
        Path.Combine(AppContext.BaseDirectory, "Java", "worker", "lib");

    public string SourceLevel { get; set; } = "21";

    public TimeSpan StartupTimeout { get; set; } = TimeSpan.FromSeconds(30);

    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromMinutes(2);

    public int MaxFrameBytes { get; set; } = 1_048_576;

    public int MaxResponseBytes { get; set; } = 67_108_864;

    private static string ResolveDefaultJavaExecutable()
    {
        var executableName = OperatingSystem.IsWindows() ? "java.exe" : "java";
        var packagedRuntime = Path.Combine(AppContext.BaseDirectory, "Java", "runtime", "bin", executableName);
        return File.Exists(packagedRuntime) ? packagedRuntime : "java";
    }

    public bool IsConfigured =>
        Directory.Exists(WorkerClassesDirectory)
        && File.Exists(Path.Combine(
            WorkerClassesDirectory,
            "org", "aegis", "worker", "WorkerMain.class"))
        && Directory.Exists(WorkerLibrariesDirectory)
        && File.Exists(Path.Combine(
            WorkerLibrariesDirectory,
            "org.eclipse.jdt.core-3.47.0.jar"))
        && File.Exists(Path.Combine(
            WorkerLibrariesDirectory,
            "gson-2.13.2.jar"));
}
