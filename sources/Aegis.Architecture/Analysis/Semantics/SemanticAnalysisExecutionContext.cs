using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Aegis.Architecture.Analysis.Semantics;

/// <summary>
/// Owns transient compiler artifacts for one semantic pipeline invocation.
/// Filesystem paths remain execution-local and are never added to report contracts.
/// </summary>
public sealed class SemanticAnalysisExecutionContext : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, byte> _artifactRoots = new(PathComparer);
    private readonly ConcurrentDictionary<string, string> _productionOutputs = new(PathComparer);
    private readonly ConcurrentDictionary<string, string> _testOutputs = new(PathComparer);
    private readonly ILogger _logger;
    private int _disposed;

    public SemanticAnalysisExecutionContext(ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <summary>Creates and tracks a unique host-owned temporary artifact root.</summary>
    public string CreateArtifactRoot()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        var root = Path.GetFullPath(Directory.CreateTempSubdirectory("aegis-semantic-").FullName);
        if (!_artifactRoots.TryAdd(root, 0))
        {
            Directory.Delete(root, recursive: true);
            throw new InvalidOperationException("A duplicate semantic artifact root was allocated.");
        }

        return root;
    }

    /// <summary>
    /// Registers a completed compiler output only when it is contained by an
    /// artifact root created and owned by this execution context.
    /// </summary>
    public void RegisterCompilationOutput(
        string modulePath,
        bool isTestCode,
        string artifactRoot,
        string outputDirectory)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(modulePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);

        var normalizedRoot = Path.GetFullPath(artifactRoot);
        var normalizedOutput = Path.GetFullPath(outputDirectory);
        if (!_artifactRoots.ContainsKey(normalizedRoot))
            throw new InvalidOperationException("The compiler artifact root is not owned by this execution context.");

        var relative = Path.GetRelativePath(normalizedRoot, normalizedOutput);
        if (Path.IsPathRooted(relative)
            || relative == "."
            || relative == ".."
            || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            || relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The compiler output directory is outside its host-owned artifact root.");
        }

        if (!Directory.Exists(normalizedOutput)
            || (File.GetAttributes(normalizedOutput) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidOperationException("The compiler output directory is missing or is a reparse point.");
        }

        var key = NormalizeModulePath(modulePath);
        (isTestCode ? _testOutputs : _productionOutputs)[key] = normalizedOutput;
    }

    /// <summary>
    /// Returns successful same-module and project-dependency compiler outputs
    /// suitable for the requested production/test scope.
    /// </summary>
    public IReadOnlyList<string> GetCompilationOutputs(string modulePath, bool includeTestCode) =>
        GetCompilationOutputs(
            modulePath,
            Array.Empty<string>(),
            Array.Empty<string>(),
            includeTestCode);

    /// <summary>
    /// Returns this module's outputs and the outputs of its explicit project
    /// dependencies. Test outputs are included only for test-scoped dependencies.
    /// </summary>
    public IReadOnlyList<string> GetCompilationOutputs(
        string modulePath,
        IReadOnlyCollection<string> productionDependencyPaths,
        IReadOnlyCollection<string> testDependencyPaths,
        bool includeTestCode)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(modulePath);
        ArgumentNullException.ThrowIfNull(productionDependencyPaths);
        ArgumentNullException.ThrowIfNull(testDependencyPaths);

        var outputs = new HashSet<string>(PathComparer);
        AddOutput(_productionOutputs, modulePath, outputs);

        foreach (var dependencyPath in productionDependencyPaths)
            AddOutput(_productionOutputs, dependencyPath, outputs);

        if (includeTestCode)
        {
            AddOutput(_testOutputs, modulePath, outputs);
            foreach (var dependencyPath in testDependencyPaths)
            {
                AddOutput(_productionOutputs, dependencyPath, outputs);
                AddOutput(_testOutputs, dependencyPath, outputs);
            }
        }

        return outputs.OrderBy(path => path, PathComparer).ToArray();
    }

    private static void AddOutput(
        ConcurrentDictionary<string, string> outputsByModule,
        string modulePath,
        ISet<string> outputs)
    {
        if (string.IsNullOrWhiteSpace(modulePath))
            return;

        if (outputsByModule.TryGetValue(NormalizeModulePath(modulePath), out var output))
            outputs.Add(output);
    }

    private static string NormalizeModulePath(string modulePath) =>
        Path.GetFullPath(modulePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return ValueTask.CompletedTask;

        foreach (var root in _artifactRoots.Keys.OrderByDescending(path => path.Length))
        {
            try
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(
                    exception,
                    "Could not clean semantic compiler artifact directory {ArtifactRoot}.",
                    root);
            }
        }

        _artifactRoots.Clear();
        _productionOutputs.Clear();
        _testOutputs.Clear();
        return ValueTask.CompletedTask;
    }

    private static StringComparer PathComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}
