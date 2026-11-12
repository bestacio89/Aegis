using System.Security.Cryptography;
using System.Text;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;

namespace Aegis.Architecture.Analysis.Semantics;

internal static class JavaSemanticFrameworkDetector
{
    public static void Analyze(ProjectArchitectureContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        foreach (var run in context.SemanticAnalysisRuns.Where(run =>
                     run.Language.Equals("Java", StringComparison.OrdinalIgnoreCase)
                     && run.Status is SemanticAnalysisStatus.Completed or SemanticAnalysisStatus.PartiallyCompleted))
        {
            var module = context.Modules.FirstOrDefault(candidate => PathsEqual(candidate.Path, run.ProjectPath));
            if (module is null)
                continue;

            var symbols = run.Symbols
                .Where(symbol => !string.IsNullOrWhiteSpace(symbol.Id))
                .GroupBy(symbol => symbol.Id, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

            foreach (var relationship in run.Relationships.Where(relationship =>
                         relationship.Kind == SemanticRelationshipKind.UsesAnnotation
                         && relationship.ResolutionStatus == SemanticResolutionStatus.Resolved
                         && !string.IsNullOrWhiteSpace(relationship.TargetSymbolId)))
            {
                if (!symbols.TryGetValue(relationship.TargetSymbolId!, out var annotationSymbol))
                    continue;

                var classification = Classify(annotationSymbol.QualifiedName);
                if (classification is null)
                    continue;

                var (framework, kind) = classification.Value;
                var provenance = relationship.Provenance.Select(item => new SemanticEvidenceProvenance
                {
                    AnalyzerId = run.EngineId,
                    Source = item.Source == SemanticEvidenceSource.Unknown
                        ? SemanticEvidenceSource.Compiler
                        : item.Source,
                    FilePath = item.FilePath,
                    StartLine = item.StartLine,
                    StartColumn = item.StartColumn,
                    Confidence = item.Confidence > 0 ? item.Confidence : 1.0
                }).ToList();

                var evidenceKey = string.Join(
                    "|",
                    module.Path,
                    framework,
                    annotationSymbol.QualifiedName,
                    string.Join(",", provenance.Select(item =>
                        $"{item.FilePath}:{item.StartLine}:{item.StartColumn}")));
                var evidence = new FrameworkEvidence
                {
                    Id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(evidenceKey))).ToLowerInvariant(),
                    Name = framework,
                    Kind = kind,
                    ProjectPath = Path.GetFullPath(module.Path),
                    IsTestOnly = kind == FrameworkEvidenceKind.Testing
                        || relationship.IsTestCode
                        || module.IsTestProject
                        || module.IsTestSupport,
                    Provenance = provenance
                };

                if (module.FrameworkEvidence.All(existing => !existing.Id.Equals(evidence.Id, StringComparison.Ordinal)))
                    module.FrameworkEvidence.Add(evidence);
                if (context.FrameworkEvidence.All(existing => !existing.Id.Equals(evidence.Id, StringComparison.Ordinal)))
                    context.FrameworkEvidence.Add(evidence);

                AddDistinct(module.Frameworks, framework);
                AddDistinct(context.DetectedFrameworks, framework);
            }

            if (string.Equals(module.Framework, "Java SE", StringComparison.OrdinalIgnoreCase))
            {
                module.Framework = module.Frameworks
                    .Where(IsJavaFramework)
                    .OrderBy(GetFrameworkPriority)
                    .ThenBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault() ?? module.Framework;
            }
        }

        AnalyzeBuildTestFrameworks(context);

        if (string.Equals(context.Language, "Java", StringComparison.OrdinalIgnoreCase)
            && string.Equals(context.Framework, "Java SE", StringComparison.OrdinalIgnoreCase))
        {
            var frameworks = context.ProductionModules
                .SelectMany(module => module.Frameworks)
                .Where(IsJavaFramework)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            context.Framework = frameworks.Length switch
            {
                0 => context.Framework,
                1 => frameworks[0],
                _ => "Multi-Framework"
            };
        }
    }

    private static void AnalyzeBuildTestFrameworks(ProjectArchitectureContext context)
    {
        foreach (var module in context.Modules)
        {
            if (module.MavenModel is { } maven)
            {
                foreach (var dependency in maven.Dependencies)
                {
                    var framework = ClassifyTestDependency(dependency.GroupId, dependency.ArtifactId);
                    if (framework is null)
                        continue;

                    AddBuildEvidence(
                        context,
                        module,
                        framework,
                        dependency.Version,
                        $"{dependency.GroupId}:{dependency.ArtifactId}",
                        maven.ProjectFile,
                        "maven-static-model",
                        string.Equals(dependency.Scope, "test", StringComparison.OrdinalIgnoreCase)
                            || module.IsTestProject
                            || module.IsTestSupport);
                }
            }

            if (module.GradleBuildModel is { } gradle)
            {
                foreach (var dependency in gradle.Dependencies)
                {
                    var framework = ClassifyTestDependency(dependency.GroupId, dependency.ArtifactId);
                    if (framework is null)
                        continue;

                    AddBuildEvidence(
                        context,
                        module,
                        framework,
                        dependency.Version,
                        $"{dependency.GroupId}:{dependency.ArtifactId}",
                        gradle.BuildFile,
                        "gradle-static-model",
                        dependency.Configuration.Contains("test", StringComparison.OrdinalIgnoreCase)
                            || module.IsTestProject
                            || module.IsTestSupport);
                }
            }
        }
    }

    private static string? ClassifyTestDependency(string? groupId, string? artifactId)
    {
        if (string.IsNullOrWhiteSpace(groupId) || string.IsNullOrWhiteSpace(artifactId))
            return null;

        if (groupId == "org.junit.jupiter" && artifactId.StartsWith("junit-jupiter", StringComparison.Ordinal))
            return "JUnit 5";
        if (groupId == "junit" && artifactId == "junit")
            return "JUnit 4";
        if (groupId == "org.testng" && artifactId == "testng")
            return "TestNG";
        if (groupId == "org.mockito" && artifactId.StartsWith("mockito-", StringComparison.Ordinal))
            return "Mockito";
        if (groupId == "org.assertj" && artifactId.StartsWith("assertj-", StringComparison.Ordinal))
            return "AssertJ";
        if (groupId == "org.springframework.boot" && artifactId == "spring-boot-starter-test")
            return "Spring Boot Test";
        if (groupId == "io.quarkus" && artifactId.StartsWith("quarkus-junit", StringComparison.Ordinal))
            return "Quarkus Test";
        if (groupId == "io.micronaut.test")
            return "Micronaut Test";

        return null;
    }

    private static void AddBuildEvidence(
        ProjectArchitectureContext context,
        ProjectModuleContext module,
        string framework,
        string? version,
        string dependencyIdentity,
        string projectFile,
        string analyzerId,
        bool isTestOnly)
    {
        var evidenceKey = string.Join("|", module.Path, framework, dependencyIdentity, projectFile, isTestOnly);
        var evidence = new FrameworkEvidence
        {
            Id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(evidenceKey))).ToLowerInvariant(),
            Name = framework,
            Version = version,
            Kind = FrameworkEvidenceKind.Testing,
            ProjectPath = Path.GetFullPath(module.Path),
            IsTestOnly = isTestOnly,
            Provenance =
            [
                new SemanticEvidenceProvenance
                {
                    AnalyzerId = analyzerId,
                    Source = SemanticEvidenceSource.BuildModel,
                    FilePath = projectFile,
                    Confidence = 1.0
                }
            ]
        };

        if (module.FrameworkEvidence.All(existing => !existing.Id.Equals(evidence.Id, StringComparison.Ordinal)))
            module.FrameworkEvidence.Add(evidence);
        if (context.FrameworkEvidence.All(existing => !existing.Id.Equals(evidence.Id, StringComparison.Ordinal)))
            context.FrameworkEvidence.Add(evidence);

        AddDistinct(module.Frameworks, framework);
        AddDistinct(context.DetectedFrameworks, framework);
    }

    private static (string Name, FrameworkEvidenceKind Kind)? Classify(string qualifiedName)
    {
        if (qualifiedName.StartsWith("org.junit.jupiter.api.", StringComparison.Ordinal))
            return ("JUnit 5", FrameworkEvidenceKind.Testing);
        if (qualifiedName.StartsWith("org.junit.", StringComparison.Ordinal))
            return ("JUnit 4", FrameworkEvidenceKind.Testing);
        if (qualifiedName.StartsWith("org.testng.annotations.", StringComparison.Ordinal))
            return ("TestNG", FrameworkEvidenceKind.Testing);
        if (qualifiedName.StartsWith("org.mockito.", StringComparison.Ordinal))
            return ("Mockito", FrameworkEvidenceKind.Testing);
        if (qualifiedName.StartsWith("org.springframework.boot.test.", StringComparison.Ordinal))
            return ("Spring Boot Test", FrameworkEvidenceKind.Testing);
        if (qualifiedName.StartsWith("io.quarkus.test.", StringComparison.Ordinal))
            return ("Quarkus Test", FrameworkEvidenceKind.Testing);
        if (qualifiedName.StartsWith("io.micronaut.test.", StringComparison.Ordinal))
            return ("Micronaut Test", FrameworkEvidenceKind.Testing);

        if (qualifiedName.StartsWith("org.springframework.boot.autoconfigure.", StringComparison.Ordinal))
            return ("Spring Boot", FrameworkEvidenceKind.Application);
        if (qualifiedName.StartsWith("org.springframework.web.bind.annotation.", StringComparison.Ordinal))
            return ("Spring Framework", FrameworkEvidenceKind.Web);
        if (qualifiedName.StartsWith("org.springframework.kafka.annotation.", StringComparison.Ordinal)
            || qualifiedName.StartsWith("org.springframework.amqp.rabbit.annotation.", StringComparison.Ordinal))
            return ("Spring Framework", FrameworkEvidenceKind.Messaging);
        if (qualifiedName.StartsWith("org.springframework.data.", StringComparison.Ordinal))
            return ("Spring Data", FrameworkEvidenceKind.Persistence);
        if (qualifiedName.StartsWith("org.springframework.transaction.annotation.", StringComparison.Ordinal))
            return ("Spring Framework", FrameworkEvidenceKind.Persistence);
        if (qualifiedName.StartsWith("org.springframework.stereotype.", StringComparison.Ordinal)
            || qualifiedName.StartsWith("org.springframework.context.annotation.", StringComparison.Ordinal))
            return ("Spring Framework", FrameworkEvidenceKind.Application);

        if (qualifiedName.StartsWith("jakarta.ws.rs.", StringComparison.Ordinal))
            return ("Jakarta EE", FrameworkEvidenceKind.Web);
        if (qualifiedName.StartsWith("jakarta.persistence.", StringComparison.Ordinal))
            return ("Jakarta EE", FrameworkEvidenceKind.Persistence);
        if (qualifiedName.StartsWith("jakarta.inject.", StringComparison.Ordinal)
            || qualifiedName.StartsWith("jakarta.enterprise.", StringComparison.Ordinal))
            return ("Jakarta EE", FrameworkEvidenceKind.Application);

        if (qualifiedName.StartsWith("javax.ws.rs.", StringComparison.Ordinal))
            return ("Java EE", FrameworkEvidenceKind.Web);
        if (qualifiedName.StartsWith("javax.persistence.", StringComparison.Ordinal))
            return ("Java EE", FrameworkEvidenceKind.Persistence);
        if (qualifiedName.StartsWith("javax.inject.", StringComparison.Ordinal)
            || qualifiedName.StartsWith("javax.enterprise.", StringComparison.Ordinal))
            return ("Java EE", FrameworkEvidenceKind.Application);

        if (qualifiedName.StartsWith("io.quarkus.", StringComparison.Ordinal))
            return ("Quarkus", FrameworkEvidenceKind.Application);
        if (qualifiedName.StartsWith("io.micronaut.http.annotation.", StringComparison.Ordinal))
            return ("Micronaut", FrameworkEvidenceKind.Web);
        if (qualifiedName.StartsWith("io.micronaut.data.", StringComparison.Ordinal))
            return ("Micronaut Data", FrameworkEvidenceKind.Persistence);
        if (qualifiedName.StartsWith("io.micronaut.context.annotation.", StringComparison.Ordinal))
            return ("Micronaut", FrameworkEvidenceKind.Application);
        if (qualifiedName.StartsWith("org.hibernate.annotations.", StringComparison.Ordinal))
            return ("Hibernate", FrameworkEvidenceKind.Persistence);
        if (qualifiedName.StartsWith("org.eclipse.microprofile.", StringComparison.Ordinal))
            return ("MicroProfile", FrameworkEvidenceKind.Runtime);

        return null;
    }

    private static bool IsJavaFramework(string name) =>
        name is "Spring Boot" or "Spring Framework" or "Spring Data"
            or "Jakarta EE" or "Java EE" or "Quarkus" or "Micronaut"
            or "Micronaut Data" or "Hibernate" or "MicroProfile";

    private static int GetFrameworkPriority(string name) => name switch
    {
        "Spring Boot" => 0,
        "Quarkus" => 1,
        "Micronaut" => 2,
        "Jakarta EE" => 3,
        "Java EE" => 4,
        "Spring Framework" => 5,
        "Spring Data" => 6,
        "Hibernate" => 7,
        "MicroProfile" => 8,
        _ => int.MaxValue
    };

    private static void AddDistinct(List<string> values, string value)
    {
        if (!values.Contains(value, StringComparer.OrdinalIgnoreCase))
            values.Add(value);
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
