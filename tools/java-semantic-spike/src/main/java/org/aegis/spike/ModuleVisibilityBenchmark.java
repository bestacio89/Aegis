package org.aegis.spike;

import com.github.javaparser.JavaParser;
import com.github.javaparser.ParserConfiguration;
import com.github.javaparser.ast.CompilationUnit;
import com.github.javaparser.ast.type.ClassOrInterfaceType;
import com.github.javaparser.symbolsolver.JavaSymbolSolver;
import com.github.javaparser.symbolsolver.resolution.typesolvers.CombinedTypeSolver;
import com.github.javaparser.symbolsolver.resolution.typesolvers.JavaParserTypeSolver;
import com.github.javaparser.symbolsolver.resolution.typesolvers.JarTypeSolver;
import com.github.javaparser.symbolsolver.resolution.typesolvers.ReflectionTypeSolver;
import org.eclipse.jdt.core.JavaCore;
import org.eclipse.jdt.core.dom.AST;
import org.eclipse.jdt.core.dom.ASTParser;
import org.eclipse.jdt.core.dom.ASTVisitor;
import org.eclipse.jdt.core.dom.ITypeBinding;
import org.eclipse.jdt.core.dom.SimpleType;

import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.Comparator;
import java.util.HashSet;
import java.util.List;
import java.util.Locale;
import java.util.Properties;
import java.util.Set;
import java.util.stream.Stream;

/**
 * Validates project and test dependency visibility using an explicit projection
 * of the statically inspected J1 module model. This remains an isolated spike.
 */
public final class ModuleVisibilityBenchmark {
    private static final String FIXTURE_SUFFIX = ".java.fixture";

    private ModuleVisibilityBenchmark() {
    }

    public static void main(String[] args) throws Exception {
        if (args.length != 4) {
            throw new IllegalArgumentException(
                "Usage: ModuleVisibilityBenchmark <jdt|javaparser> <fixture-root> <module-manifest> <classpath-root>");
        }

        String engine = args[0].toLowerCase(Locale.ROOT);
        if (!engine.equals("jdt") && !engine.equals("javaparser")) {
            throw new IllegalArgumentException("Unsupported engine: " + engine);
        }

        Path fixtureRoot = Path.of(args[1]).toAbsolutePath().normalize();
        Path manifestPath = Path.of(args[2]).toAbsolutePath().normalize();
        Path classpathRoot = Path.of(args[3]).toAbsolutePath().normalize();
        if (!Files.isDirectory(fixtureRoot) || !Files.isRegularFile(manifestPath)) {
            throw new IllegalArgumentException("Fixture root or module manifest does not exist.");
        }

        Properties manifest = new Properties();
        try (var reader = Files.newBufferedReader(manifestPath)) {
            manifest.load(reader);
        }

        Path workspace = Files.createTempDirectory("aegis-java-module-visibility-");
        try {
            materializeSources(fixtureRoot, workspace);
            List<Module> modules = readModules(manifest, workspace, classpathRoot);
            validateClasspathAssertions(modules);
            List<Expectation> expectations = readExpectations(manifest);
            List<Observation> observations = engine.equals("jdt")
                ? analyzeWithJdt(modules)
                : analyzeWithJavaParser(modules);
            validateExpectations(engine, expectations, observations);
            printResults(engine, expectations, observations);
        } finally {
            deleteDirectory(workspace);
        }
    }

    private static void materializeSources(Path fixtureRoot, Path workspace) throws IOException {
        try (Stream<Path> paths = Files.walk(fixtureRoot)) {
            for (Path fixture : paths
                .filter(Files::isRegularFile)
                .filter(path -> path.getFileName().toString().endsWith(FIXTURE_SUFFIX))
                .sorted()
                .toList()) {
                Path relative = fixtureRoot.relativize(fixture);
                String fixtureName = relative.getFileName().toString();
                String javaName = fixtureName.substring(0, fixtureName.length() - ".fixture".length());
                Path destination = workspace.resolve(relative).getParent().resolve(javaName);
                Files.createDirectories(destination.getParent());
                Files.copy(fixture, destination);
            }
        }
    }

    private static List<Module> readModules(Properties manifest, Path workspace, Path classpathRoot) throws IOException {
        String[] moduleIds = required(manifest, "modules").split(",");
        List<Module> modules = new ArrayList<>();
        for (String rawId : moduleIds) {
            String id = rawId.trim();
            List<Path> productionRoots = resolveRoots(workspace, manifest.getProperty("module." + id + ".production", ""));
            List<Path> testRoots = resolveRoots(workspace, manifest.getProperty("module." + id + ".test", ""));
            Set<String> dependencies = parseSet(manifest.getProperty("module." + id + ".dependencies", ""));
            Set<String> testDependencies = parseSet(manifest.getProperty("module." + id + ".testDependencies", ""));
            if (productionRoots.isEmpty() && testRoots.isEmpty()) {
                throw new IllegalStateException("Module has no declared source roots: " + id);
            }
            modules.add(new Module(id, productionRoots, testRoots, dependencies, testDependencies,
                readClasspath(manifest, classpathRoot, "module." + id + ".compileClasspath"),
                readClasspath(manifest, classpathRoot, "module." + id + ".testClasspath"),
                readClasspath(manifest, classpathRoot, "module." + id + ".runtimeClasspath"),
                parseSet(manifest.getProperty("module." + id + ".requiredArtifacts", "")),
                parseSet(manifest.getProperty("module." + id + ".forbiddenArtifacts", ""))));
        }

        Set<String> knownIds = modules.stream().map(Module::id).collect(java.util.stream.Collectors.toSet());
        for (Module module : modules) {
            Set<String> references = new HashSet<>(module.dependencies());
            references.addAll(module.testDependencies());
            for (String dependency : references) {
                if (!knownIds.contains(dependency)) {
                    throw new IllegalStateException(
                        "Module " + module.id() + " references unknown module " + dependency);
                }
            }
        }
        return List.copyOf(modules);
    }

    private static List<Path> readClasspath(
        Properties manifest, Path classpathRoot, String property) throws IOException {
        String fileName = manifest.getProperty(property);
        if (fileName == null || fileName.isBlank()) {
            return List.of();
        }
        Path classpathFile = classpathRoot.resolve(fileName.trim()).normalize();
        if (!classpathFile.startsWith(classpathRoot) || !Files.isRegularFile(classpathFile)) {
            throw new IllegalStateException("Declared module classpath file is missing or outside its root: "
                + property + "=" + fileName);
        }
        String content = Files.readString(classpathFile).trim();
        if (content.isEmpty()) {
            return List.of();
        }
        List<Path> entries = Stream.of(content.split(java.util.regex.Pattern.quote(
                java.io.File.pathSeparator)))
            .map(String::trim)
            .filter(value -> !value.isEmpty())
            .map(Path::of)
            .map(Path::toAbsolutePath)
            .map(Path::normalize)
            .toList();
        for (Path entry : entries) {
            if (!Files.isRegularFile(entry)) {
                throw new IllegalStateException("Module classpath entry does not exist: " + entry);
            }
        }
        if (entries.size() != entries.stream().distinct().count()) {
            throw new IllegalStateException("Duplicate entries in module classpath: " + property);
        }
        return entries;
    }

    private static void validateClasspathAssertions(List<Module> modules) {
        for (Module module : modules) {
            List<String> names = Stream.of(
                    module.compileClasspath(), module.testClasspath(), module.runtimeClasspath())
                .flatMap(List::stream)
                .map(path -> path.getFileName().toString())
                .toList();
            for (String required : module.requiredArtifacts()) {
                long count = names.stream().filter(name -> name.contains(required)).count();
                if (count != 1) {
                    throw new IllegalStateException("Module " + module.id()
                        + " expected exactly one artifact matching '" + required
                        + "' but found " + count + ": " + names);
                }
            }
            for (String forbidden : module.forbiddenArtifacts()) {
                if (names.stream().anyMatch(name -> name.contains(forbidden))) {
                    throw new IllegalStateException("Module " + module.id()
                        + " must not contain excluded/conflicting artifact '" + forbidden
                        + "': " + names);
                }
            }
        }
    }

    private static List<Path> resolveRoots(Path workspace, String value) {
        if (value.isBlank()) {
            return List.of();
        }
        return Stream.of(value.split(";"))
            .map(String::trim)
            .filter(item -> !item.isEmpty())
            .map(workspace::resolve)
            .map(Path::normalize)
            .peek(path -> {
                if (!Files.isDirectory(path)) {
                    throw new IllegalStateException("Declared source root does not exist: " + path);
                }
            })
            .toList();
    }

    private static Set<String> parseSet(String value) {
        if (value.isBlank()) {
            return Set.of();
        }
        return Stream.of(value.split(","))
            .map(String::trim)
            .filter(item -> !item.isEmpty())
            .collect(java.util.stream.Collectors.toUnmodifiableSet());
    }

    private static List<Expectation> readExpectations(Properties manifest) {
        return Stream.of(required(manifest, "expectations").split(";"))
            .map(String::trim)
            .filter(item -> !item.isEmpty())
            .map(item -> {
                String[] fields = item.split(",", -1);
                if (fields.length != 4) {
                    throw new IllegalStateException("Invalid visibility expectation: " + item);
                }
                return new Expectation(fields[0].trim(), fields[1].trim(),
                    fields[2].trim(), fields[3].trim().equalsIgnoreCase("resolved"));
            })
            .toList();
    }

    private static List<Observation> analyzeWithJavaParser(List<Module> modules) throws IOException {
        List<Observation> observations = new ArrayList<>();
        for (Module module : modules) {
            analyzeJavaParserSet(module, "production", module.productionRoots(),
                visibleProductionRoots(module, modules), observations);
            if (!module.testRoots().isEmpty()) {
                List<Path> visibleTestRoots = new ArrayList<>(module.productionRoots());
                visibleTestRoots.addAll(visibleProductionRoots(module, modules));
                visibleTestRoots.addAll(visibleTestDependencyRoots(module, modules));
                visibleTestRoots.addAll(module.testRoots());
                analyzeJavaParserSet(module, "test", module.testRoots(), distinctRoots(visibleTestRoots), observations);
            }
        }
        return observations;
    }

    private static void analyzeJavaParserSet(
        Module module, String sourceSet, List<Path> ownedRoots, List<Path> visibleRoots,
        List<Observation> observations) throws IOException {
        CombinedTypeSolver solver = new CombinedTypeSolver();
        solver.add(new ReflectionTypeSolver());
        for (Path root : visibleRoots) {
            solver.add(new JavaParserTypeSolver(root.toFile()));
        }
        for (Path artifact : visibleClasspath(module, sourceSet)) {
            solver.add(new JarTypeSolver(artifact));
        }
        ParserConfiguration configuration = new ParserConfiguration()
            .setLanguageLevel(ParserConfiguration.LanguageLevel.JAVA_21)
            .setSymbolResolver(new JavaSymbolSolver(solver));
        JavaParser parser = new JavaParser(configuration);

        for (Path source : sourceFiles(ownedRoots)) {
            var parsed = parser.parse(source);
            if (parsed.getResult().isEmpty()) {
                continue;
            }
            CompilationUnit unit = parsed.getResult().orElseThrow();
            for (ClassOrInterfaceType type : unit.findAll(ClassOrInterfaceType.class)) {
                String qualifiedName;
                boolean resolved;
                try {
                    qualifiedName = type.resolve().asReferenceType().getQualifiedName();
                    resolved = true;
                } catch (RuntimeException unresolved) {
                    qualifiedName = resolveJavaParserSourceName(unit, type);
                    resolved = false;
                }
                observations.add(new Observation(module.id(), sourceSet, qualifiedName, resolved));
            }
        }
    }

    private static List<Observation> analyzeWithJdt(List<Module> modules) throws IOException {
        List<Observation> observations = new ArrayList<>();
        for (Module module : modules) {
            analyzeJdtSet(module, "production", module.productionRoots(),
                visibleProductionRoots(module, modules), observations);
            if (!module.testRoots().isEmpty()) {
                List<Path> visibleTestRoots = new ArrayList<>(module.productionRoots());
                visibleTestRoots.addAll(visibleProductionRoots(module, modules));
                visibleTestRoots.addAll(visibleTestDependencyRoots(module, modules));
                visibleTestRoots.addAll(module.testRoots());
                analyzeJdtSet(module, "test", module.testRoots(), distinctRoots(visibleTestRoots), observations);
            }
        }
        return observations;
    }

    private static void analyzeJdtSet(
        Module module, String sourceSet, List<Path> ownedRoots, List<Path> visibleRoots,
        List<Observation> observations) throws IOException {
        String[] sourcePaths = visibleRoots.stream().map(Path::toString).toArray(String[]::new);
        String[] encodings = visibleRoots.stream().map(ignored -> "UTF-8").toArray(String[]::new);
        String[] classpathEntries = visibleClasspath(module, sourceSet).stream()
            .map(Path::toString).toArray(String[]::new);
        var options = JavaCore.getOptions();
        JavaCore.setComplianceOptions(JavaCore.VERSION_21, options);

        for (Path source : sourceFiles(ownedRoots)) {
            ASTParser parser = ASTParser.newParser(AST.getJLSLatest());
            parser.setKind(ASTParser.K_COMPILATION_UNIT);
            parser.setSource(Files.readString(source).toCharArray());
            parser.setUnitName(resolveUnitName(source, ownedRoots));
            parser.setEnvironment(classpathEntries, sourcePaths, encodings, true);
            parser.setResolveBindings(true);
            parser.setBindingsRecovery(true);
            parser.setStatementsRecovery(true);
            parser.setCompilerOptions(options);
            org.eclipse.jdt.core.dom.CompilationUnit unit =
                (org.eclipse.jdt.core.dom.CompilationUnit) parser.createAST(null);
            unit.accept(new ASTVisitor() {
                @Override
                public boolean visit(SimpleType node) {
                    ITypeBinding binding = node.resolveBinding();
                    boolean resolved = binding != null && !binding.isRecovered();
                    String name = resolved
                        ? binding.getQualifiedName()
                        : resolveJdtSourceName(unit, node);
                    observations.add(new Observation(module.id(), sourceSet, name, resolved));
                    return true;
                }
            });
        }
    }

    private static List<Path> visibleClasspath(Module module, String sourceSet) {
        List<Path> entries = new ArrayList<>(module.compileClasspath());
        if (sourceSet.equals("test")) {
            entries.addAll(module.testClasspath());
        }
        return entries.stream().distinct().sorted(Comparator.comparing(Path::toString)).toList();
    }

    private static List<Path> visibleProductionRoots(Module module, List<Module> modules) {
        Set<String> visited = new HashSet<>();
        List<Path> roots = new ArrayList<>();
        collectDependencies(module.dependencies(), modules, visited, roots);
        roots.addAll(module.productionRoots());
        return distinctRoots(roots);
    }

    private static List<Path> visibleTestDependencyRoots(Module module, List<Module> modules) {
        Set<String> visited = new HashSet<>();
        List<Path> roots = new ArrayList<>();
        collectDependencies(module.testDependencies(), modules, visited, roots);
        return distinctRoots(roots);
    }

    private static void collectDependencies(
        Set<String> dependencyIds, List<Module> modules, Set<String> visited, List<Path> roots) {
        for (String dependencyId : dependencyIds) {
            if (!visited.add(dependencyId)) {
                continue;
            }
            Module dependency = modules.stream()
                .filter(candidate -> candidate.id().equals(dependencyId))
                .findFirst()
                .orElseThrow();
            roots.addAll(dependency.productionRoots());
            collectDependencies(dependency.dependencies(), modules, visited, roots);
        }
    }

    private static List<Path> distinctRoots(List<Path> roots) {
        return roots.stream().distinct().sorted(Comparator.comparing(Path::toString)).toList();
    }

    private static List<Path> sourceFiles(List<Path> roots) throws IOException {
        List<Path> files = new ArrayList<>();
        for (Path root : roots) {
            try (Stream<Path> paths = Files.walk(root)) {
                files.addAll(paths.filter(Files::isRegularFile)
                    .filter(path -> path.getFileName().toString().endsWith(".java"))
                    .sorted().toList());
            }
        }
        return files;
    }

    private static String resolveUnitName(Path source, List<Path> roots) {
        return roots.stream()
            .filter(source::startsWith)
            .max(Comparator.comparingInt(Path::getNameCount))
            .map(root -> root.relativize(source).toString().replace('\\', '/'))
            .orElseGet(() -> source.getFileName().toString());
    }

    private static String resolveJavaParserSourceName(
        CompilationUnit unit, ClassOrInterfaceType type) {
        String simpleName = type.getNameAsString();
        for (var declaration : unit.getImports()) {
            String importedName = declaration.getNameAsString();
            if (!declaration.isAsterisk() && importedName.endsWith("." + simpleName)) {
                return importedName;
            }
            if (declaration.isAsterisk()) {
                return importedName + "." + simpleName;
            }
        }
        return unit.getPackageDeclaration()
            .map(packageDeclaration -> packageDeclaration.getNameAsString() + "." + simpleName)
            .orElse(simpleName);
    }

    private static String resolveJdtSourceName(
        org.eclipse.jdt.core.dom.CompilationUnit unit, SimpleType type) {
        String simpleName = type.getName().getFullyQualifiedName();
        for (Object item : unit.imports()) {
            org.eclipse.jdt.core.dom.ImportDeclaration declaration =
                (org.eclipse.jdt.core.dom.ImportDeclaration) item;
            String importedName = declaration.getName().getFullyQualifiedName();
            if (!declaration.isOnDemand() && importedName.endsWith("." + simpleName)) {
                return importedName;
            }
            if (declaration.isOnDemand()) {
                return importedName + "." + simpleName;
            }
        }
        if (unit.getPackage() != null) {
            return unit.getPackage().getName().getFullyQualifiedName() + "." + simpleName;
        }
        return simpleName;
    }

    private static void validateExpectations(
        String engine, List<Expectation> expectations, List<Observation> observations) {
        List<String> failures = new ArrayList<>();
        for (Expectation expectation : expectations) {
            boolean observedResolved = observations.stream().anyMatch(observation ->
                observation.moduleId().equals(expectation.moduleId())
                    && observation.sourceSet().equals(expectation.sourceSet())
                    && observation.typeName().equals(expectation.typeName())
                    && observation.resolved());
            boolean observedUnresolved = observations.stream().anyMatch(observation ->
                observation.moduleId().equals(expectation.moduleId())
                    && observation.sourceSet().equals(expectation.sourceSet())
                    && observation.typeName().equals(expectation.typeName())
                    && !observation.resolved());
            boolean passed = expectation.expectedResolved() ? observedResolved : observedUnresolved;
            if (!passed) {
                failures.add(expectation.moduleId() + "/" + expectation.sourceSet()
                    + " expected " + expectation.typeName() + " to be "
                    + (expectation.expectedResolved() ? "resolved" : "unresolved"));
            }
        }
        if (!failures.isEmpty()) {
            throw new IllegalStateException(engine + " module visibility failures: " + failures);
        }
    }

    private static void printResults(
        String engine, List<Expectation> expectations, List<Observation> observations) {
        System.out.printf(Locale.ROOT,
            "{\"engine\":\"%s\",\"expectations\":%d,\"observations\":%d,"
                + "\"resolved\":%d,\"unresolved\":%d,\"status\":\"passed\"}%n",
            engine, expectations.size(), observations.size(),
            observations.stream().filter(Observation::resolved).count(),
            observations.stream().filter(observation -> !observation.resolved()).count());
    }

    private static String required(Properties properties, String key) {
        String value = properties.getProperty(key);
        if (value == null || value.isBlank()) {
            throw new IllegalStateException("Missing required manifest property: " + key);
        }
        return value.trim();
    }

    private static void deleteDirectory(Path directory) throws IOException {
        if (!Files.exists(directory)) {
            return;
        }
        try (Stream<Path> paths = Files.walk(directory)) {
            for (Path path : paths.sorted(Comparator.reverseOrder()).toList()) {
                Files.deleteIfExists(path);
            }
        }
    }

    private record Module(
        String id, List<Path> productionRoots, List<Path> testRoots,
        Set<String> dependencies, Set<String> testDependencies,
        List<Path> compileClasspath, List<Path> testClasspath, List<Path> runtimeClasspath,
        Set<String> requiredArtifacts, Set<String> forbiddenArtifacts) {
    }

    private record Expectation(
        String moduleId, String sourceSet, String typeName, boolean expectedResolved) {
    }

    private record Observation(
        String moduleId, String sourceSet, String typeName, boolean resolved) {
    }
}
