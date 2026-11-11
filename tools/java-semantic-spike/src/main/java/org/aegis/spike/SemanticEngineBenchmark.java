package org.aegis.spike;

import com.github.javaparser.JavaParser;
import com.github.javaparser.ParseResult;
import com.github.javaparser.ParserConfiguration;
import com.github.javaparser.ast.CompilationUnit;
import com.github.javaparser.ast.expr.MethodCallExpr;
import com.github.javaparser.ast.expr.ObjectCreationExpr;
import com.github.javaparser.ast.type.ClassOrInterfaceType;
import com.github.javaparser.symbolsolver.JavaSymbolSolver;
import com.github.javaparser.symbolsolver.resolution.typesolvers.CombinedTypeSolver;
import com.github.javaparser.symbolsolver.resolution.typesolvers.JavaParserTypeSolver;
import com.github.javaparser.symbolsolver.resolution.typesolvers.ReflectionTypeSolver;
import org.eclipse.jdt.core.JavaCore;
import org.eclipse.jdt.core.compiler.IProblem;
import org.eclipse.jdt.core.dom.AST;
import org.eclipse.jdt.core.dom.ASTParser;
import org.eclipse.jdt.core.dom.ASTVisitor;
import org.eclipse.jdt.core.dom.ClassInstanceCreation;
import org.eclipse.jdt.core.dom.MethodInvocation;
import org.eclipse.jdt.core.dom.SimpleType;

import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.Comparator;
import java.util.HashSet;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.Set;
import java.util.stream.Stream;

/**
 * Runs one semantic engine over the checked-in Java fixture corpus.
 * This is an isolated comparison tool, not a production Aegis component.
 */
public final class SemanticEngineBenchmark {
    private static final String FIXTURE_SUFFIX = ".fixture";

    private SemanticEngineBenchmark() {
    }

    public static void main(String[] args) throws Exception {
        if (args.length < 2) {
            throw new IllegalArgumentException(
                "Usage: SemanticEngineBenchmark <jdt|javaparser> <fixture-root>");
        }

        String engine = args[0].toLowerCase(Locale.ROOT);
        if (!engine.equals("jdt") && !engine.equals("javaparser")) {
            throw new IllegalArgumentException("Unsupported engine: " + engine);
        }

        Path fixtureRoot = Path.of(args[1]).toAbsolutePath().normalize();
        if (!Files.isDirectory(fixtureRoot)) {
            throw new IllegalArgumentException("Fixture root does not exist: " + fixtureRoot);
        }

        Path workspace = Files.createTempDirectory("aegis-java-semantic-spike-");
        try {
            List<Path> sourceFiles = materializeSources(fixtureRoot, workspace);
            List<Path> sourceRoots = discoverSourceRoots(workspace, sourceFiles);

            Metrics metrics = engine.equals("jdt")
                ? analyzeWithJdt(sourceFiles, sourceRoots)
                : analyzeWithJavaParser(sourceFiles, sourceRoots);

            printJson(engine, metrics, sourceRoots.size());
        } finally {
            deleteDirectory(workspace);
        }
    }

    private static List<Path> materializeSources(Path fixtureRoot, Path workspace)
        throws IOException {
        List<Path> sources = new ArrayList<>();

        try (Stream<Path> paths = Files.walk(fixtureRoot)) {
            for (Path fixture : paths
                .filter(Files::isRegularFile)
                .filter(path -> path.getFileName().toString().endsWith(".java" + FIXTURE_SUFFIX))
                .sorted()
                .toList()) {
                Path relative = fixtureRoot.relativize(fixture);
                String fixtureName = relative.getFileName().toString();
                String javaName = fixtureName.substring(
                    0,
                    fixtureName.length() - FIXTURE_SUFFIX.length());
                Path destination = workspace.resolve(relative).getParent().resolve(javaName);
                Files.createDirectories(destination.getParent());
                Files.copy(fixture, destination);
                sources.add(destination);
            }
        }

        if (sources.isEmpty()) {
            throw new IllegalStateException("No .java.fixture files found under " + fixtureRoot);
        }

        return sources;
    }

    private static List<Path> discoverSourceRoots(Path workspace, List<Path> sourceFiles) {
        Set<Path> roots = new HashSet<>();

        for (Path sourceFile : sourceFiles) {
            Path current = sourceFile.getParent();
            while (current != null && current.startsWith(workspace)) {
                String directoryName = current.getFileName().toString();
                if (directoryName.equals("java") || directoryName.equals("annotations")) {
                    roots.add(current);
                }
                current = current.getParent();
            }
        }

        return roots.stream()
            .sorted(Comparator.comparing(Path::toString))
            .toList();
    }

    private static Metrics analyzeWithJavaParser(
        List<Path> sourceFiles,
        List<Path> sourceRoots) {

        ParserConfiguration sourceParserConfiguration = new ParserConfiguration()
            .setLanguageLevel(ParserConfiguration.LanguageLevel.JAVA_21);
        CombinedTypeSolver typeSolver = new CombinedTypeSolver();
        typeSolver.add(new ReflectionTypeSolver());
        for (Path sourceRoot : sourceRoots) {
            typeSolver.add(new JavaParserTypeSolver(
                sourceRoot.toFile(),
                sourceParserConfiguration));
        }

        ParserConfiguration configuration = new ParserConfiguration()
            .setLanguageLevel(ParserConfiguration.LanguageLevel.JAVA_21)
            .setSymbolResolver(new JavaSymbolSolver(typeSolver));
        JavaParser parser = new JavaParser(configuration);

        int parsedFiles = 0;
        int diagnostics = 0;
        int resolutionAttempts = 0;
        int resolvedBindings = 0;
        long started = System.nanoTime();

        for (Path sourceFile : sourceFiles) {
            ParseResult<CompilationUnit> parseResult;
            try {
                parseResult = parser.parse(sourceFile);
            } catch (IOException parseFailure) {
                diagnostics++;
                continue;
            }

            diagnostics += parseResult.getProblems().size();
            if (parseResult.getResult().isEmpty()) {
                continue;
            }

            parsedFiles++;
            CompilationUnit unit = parseResult.getResult().orElseThrow();

            for (ClassOrInterfaceType type : unit.findAll(ClassOrInterfaceType.class)) {
                resolutionAttempts++;
                try {
                    type.resolve();
                    resolvedBindings++;
                } catch (RuntimeException unresolvedOrUnsupported) {
                    // Resolution failures are evidence; they do not abort the corpus.
                }
            }

            for (MethodCallExpr invocation : unit.findAll(MethodCallExpr.class)) {
                resolutionAttempts++;
                try {
                    invocation.resolve();
                    resolvedBindings++;
                } catch (RuntimeException unresolvedOrUnsupported) {
                    // Resolution failures are evidence; they do not abort the corpus.
                }
            }

            for (ObjectCreationExpr creation : unit.findAll(ObjectCreationExpr.class)) {
                resolutionAttempts++;
                try {
                    creation.resolve();
                    resolvedBindings++;
                } catch (RuntimeException unresolvedOrUnsupported) {
                    // Resolution failures are evidence; they do not abort the corpus.
                }
            }
        }

        long elapsedMilliseconds = (System.nanoTime() - started) / 1_000_000;
        long heapUsedBytes = Runtime.getRuntime().totalMemory()
            - Runtime.getRuntime().freeMemory();

        return new Metrics(
            sourceFiles.size(),
            parsedFiles,
            diagnostics,
            resolutionAttempts,
            resolvedBindings,
            elapsedMilliseconds,
            heapUsedBytes);
    }

    private static Metrics analyzeWithJdt(
        List<Path> sourceFiles,
        List<Path> sourceRoots) {

        String[] sourcePaths = sourceRoots.stream()
            .map(Path::toString)
            .toArray(String[]::new);
        String[] encodings = sourceRoots.stream()
            .map(ignored -> "UTF-8")
            .toArray(String[]::new);

        Map<String, String> compilerOptions = JavaCore.getOptions();
        JavaCore.setComplianceOptions(JavaCore.VERSION_21, compilerOptions);

        int parsedFiles = 0;
        int diagnostics = 0;
        int resolutionAttempts = 0;
        int resolvedBindings = 0;
        long started = System.nanoTime();

        for (Path sourceFile : sourceFiles) {
            try {
                ASTParser parser = ASTParser.newParser(AST.getJLSLatest());
                parser.setKind(ASTParser.K_COMPILATION_UNIT);
                parser.setSource(Files.readString(sourceFile).toCharArray());
                parser.setUnitName(resolveUnitName(sourceFile, sourceRoots));
                parser.setEnvironment(new String[0], sourcePaths, encodings, true);
                parser.setResolveBindings(true);
                parser.setBindingsRecovery(true);
                parser.setStatementsRecovery(true);
                parser.setCompilerOptions(compilerOptions);

                org.eclipse.jdt.core.dom.CompilationUnit unit =
                    (org.eclipse.jdt.core.dom.CompilationUnit) parser.createAST(null);
                parsedFiles++;

                for (IProblem problem : unit.getProblems()) {
                    if (problem.isError()) {
                        diagnostics++;
                    }
                }

                final int[] attempts = { 0 };
                final int[] resolved = { 0 };

                unit.accept(new ASTVisitor() {
                    @Override
                    public boolean visit(SimpleType node) {
                        attempts[0]++;
                        try {
                            if (node.resolveBinding() != null) {
                                resolved[0]++;
                            }
                        } catch (RuntimeException unresolvedOrUnsupported) {
                            // Resolution failures are evidence; they do not abort the corpus.
                        }
                        return true;
                    }

                    @Override
                    public boolean visit(MethodInvocation node) {
                        attempts[0]++;
                        try {
                            if (node.resolveMethodBinding() != null) {
                                resolved[0]++;
                            }
                        } catch (RuntimeException unresolvedOrUnsupported) {
                            // Resolution failures are evidence; they do not abort the corpus.
                        }
                        return true;
                    }

                    @Override
                    public boolean visit(ClassInstanceCreation node) {
                        attempts[0]++;
                        try {
                            if (node.resolveConstructorBinding() != null) {
                                resolved[0]++;
                            }
                        } catch (RuntimeException unresolvedOrUnsupported) {
                            // Resolution failures are evidence; they do not abort the corpus.
                        }
                        return true;
                    }
                });

                resolutionAttempts += attempts[0];
                resolvedBindings += resolved[0];
            } catch (IOException | RuntimeException parseFailure) {
                diagnostics++;
            }
        }

        long elapsedMilliseconds = (System.nanoTime() - started) / 1_000_000;
        long heapUsedBytes = Runtime.getRuntime().totalMemory()
            - Runtime.getRuntime().freeMemory();

        return new Metrics(
            sourceFiles.size(),
            parsedFiles,
            diagnostics,
            resolutionAttempts,
            resolvedBindings,
            elapsedMilliseconds,
            heapUsedBytes);
    }

    private static String resolveUnitName(Path sourceFile, List<Path> sourceRoots) {
        return sourceRoots.stream()
            .filter(sourceFile::startsWith)
            .max(Comparator.comparingInt(Path::getNameCount))
            .map(root -> root.relativize(sourceFile).toString().replace('\\', '/'))
            .orElseGet(() -> sourceFile.getFileName().toString());
    }

    private static void printJson(String engine, Metrics metrics, int sourceRootCount) {
        System.out.printf(
            Locale.ROOT,
            "{\"engine\":\"%s\",\"sourceFiles\":%d,\"parsedFiles\":%d,"
                + "\"diagnostics\":%d,\"sourceRoots\":%d,"
                + "\"resolutionAttempts\":%d,\"resolvedBindings\":%d,"
                + "\"resolutionRatio\":%.4f,\"elapsedMilliseconds\":%d,"
                + "\"heapUsedBytes\":%d,\"externalClasspathEnabled\":false}%n",
            engine,
            metrics.sourceFiles(),
            metrics.parsedFiles(),
            metrics.diagnostics(),
            sourceRootCount,
            metrics.resolutionAttempts(),
            metrics.resolvedBindings(),
            metrics.resolutionAttempts() == 0
                ? 0d
                : (double) metrics.resolvedBindings() / metrics.resolutionAttempts(),
            metrics.elapsedMilliseconds(),
            metrics.heapUsedBytes());
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

    private record Metrics(
        int sourceFiles,
        int parsedFiles,
        int diagnostics,
        int resolutionAttempts,
        int resolvedBindings,
        long elapsedMilliseconds,
        long heapUsedBytes) {
    }
}
