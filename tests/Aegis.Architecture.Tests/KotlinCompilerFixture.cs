using System.Diagnostics;
using System.IO.Compression;

namespace Aegis.Architecture.Tests;

/// <summary>Builds compiler-only Kotlin fixtures against the pinned packaged Kotlin compiler.</summary>
internal static class KotlinCompilerFixture
{
    public static async Task CompileToJarAsync(
        string packageDirectory,
        IReadOnlyCollection<string> sourceFiles,
        string jarPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageDirectory);
        ArgumentNullException.ThrowIfNull(sourceFiles);
        ArgumentException.ThrowIfNullOrWhiteSpace(jarPath);
        if (sourceFiles.Count == 0)
            throw new ArgumentException("At least one Kotlin compiler fixture source is required.", nameof(sourceFiles));

        var librariesDirectory = Path.Combine(packageDirectory, "Kotlin", "lib");
        var compilerClasspath = Path.Combine(librariesDirectory, "*");
        var standardLibraryPath = Path.Combine(librariesDirectory, "kotlin-stdlib-2.4.21.jar");
        var runtimeExecutable = Path.Combine(
            packageDirectory,
            "runtime",
            "bin",
            OperatingSystem.IsWindows() ? "java.exe" : "java");
        var javaExecutable = File.Exists(runtimeExecutable) ? runtimeExecutable : "java";
        var outputDirectory = Path.Combine(Path.GetDirectoryName(jarPath)!, "kotlin-compiler-fixtures");

        if (!File.Exists(standardLibraryPath))
            throw new FileNotFoundException("The packaged Kotlin standard library was not found.", standardLibraryPath);

        Directory.CreateDirectory(outputDirectory);
        var startInfo = new ProcessStartInfo
        {
            FileName = javaExecutable,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-cp");
        startInfo.ArgumentList.Add(compilerClasspath);
        startInfo.ArgumentList.Add("org.jetbrains.kotlin.cli.jvm.K2JVMCompiler");
        startInfo.ArgumentList.Add("-no-stdlib");
        startInfo.ArgumentList.Add("-no-reflect");
        startInfo.ArgumentList.Add("-jvm-target");
        startInfo.ArgumentList.Add("21");
        startInfo.ArgumentList.Add("-classpath");
        startInfo.ArgumentList.Add(standardLibraryPath);
        startInfo.ArgumentList.Add("-module-name");
        startInfo.ArgumentList.Add("aegis_ktor_test_stubs");
        startInfo.ArgumentList.Add("-d");
        startInfo.ArgumentList.Add(outputDirectory);

        foreach (var sourceFile in sourceFiles.OrderBy(path => path, StringComparer.Ordinal))
        {
            if (!File.Exists(sourceFile))
                throw new FileNotFoundException("A Kotlin compiler fixture source was not found.", sourceFile);
            startInfo.ArgumentList.Add(sourceFile);
        }

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
            throw new InvalidOperationException("The packaged Kotlin compiler process could not be started.");

        var standardOutputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardErrorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));

        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }

        var standardOutput = await standardOutputTask.ConfigureAwait(false);
        var standardError = await standardErrorTask.ConfigureAwait(false);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"The packaged Kotlin compiler could not build compiler-only test stubs. Exit code: {process.ExitCode}. "
                + $"stdout: {standardOutput} stderr: {standardError}");
        }

        if (File.Exists(jarPath))
            File.Delete(jarPath);
        ZipFile.CreateFromDirectory(outputDirectory, jarPath);
    }
}
