package org.aegis.spike;

import com.github.javaparser.JavaParser;
import com.github.javaparser.ParserConfiguration;
import com.github.javaparser.ast.CompilationUnit;
import com.github.javaparser.ast.body.AnnotationDeclaration;
import com.github.javaparser.ast.body.ClassOrInterfaceDeclaration;
import com.github.javaparser.ast.body.ConstructorDeclaration;
import com.github.javaparser.ast.body.EnumDeclaration;
import com.github.javaparser.ast.body.MethodDeclaration;
import com.github.javaparser.ast.body.RecordDeclaration;
import com.github.javaparser.ast.expr.MethodCallExpr;
import com.github.javaparser.ast.type.ClassOrInterfaceType;
import com.github.javaparser.symbolsolver.JavaSymbolSolver;
import com.github.javaparser.symbolsolver.resolution.typesolvers.CombinedTypeSolver;
import com.github.javaparser.symbolsolver.resolution.typesolvers.JarTypeSolver;
import com.github.javaparser.symbolsolver.resolution.typesolvers.JavaParserTypeSolver;
import com.github.javaparser.symbolsolver.resolution.typesolvers.ReflectionTypeSolver;
import org.eclipse.jdt.core.JavaCore;
import org.eclipse.jdt.core.dom.AST;
import org.eclipse.jdt.core.dom.ASTParser;
import org.eclipse.jdt.core.dom.ASTVisitor;
import org.eclipse.jdt.core.dom.ITypeBinding;
import org.eclipse.jdt.core.dom.MethodInvocation;
import org.eclipse.jdt.core.dom.SimpleType;
import org.eclipse.jdt.core.compiler.IProblem;

import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.Comparator;
import java.util.List;
import java.util.Locale;
import java.util.Set;
import java.util.TreeSet;
import java.util.stream.Stream;

/**
 * Measures parser robustness and source-local binding coverage against pinned,
 * representative Java projects. Source-roots-plus-JDK remains the baseline;
 * an optional curated external classpath measures framework API resolution
 * without executing the representative project's build.
 */
public final class RepresentativeProjectBenchmark {
    private RepresentativeProjectBenchmark() {
    }

    public static void main(String[] args) throws Exception {
        if (args.length < 7 || args.length > 9) {
            throw new IllegalArgumentException(
                "Usage: RepresentativeProjectBenchmark <jdt|javaparser> <project-id> "
                    + "<main|test> <source-level> <project-root> "
                    + "<resolution-roots-csv> <parse-roots-csv> "
                    + "[--classpath-file=<path>] [--report-only]");
        }

        String engine = args[0].toLowerCase(Locale.ROOT);
        if (!engine.equals("jdt") && !engine.equals("javaparser")) {
            throw new IllegalArgumentException("Unsupported engine: " + engine);
        }
        String projectId = requireText(args[1], "project-id");
        String sourceSet = requireText(args[2], "source-set");
        if (!sourceSet.equals("main") && !sourceSet.equals("test")) {
            throw new IllegalArgumentException("Source set must be main or test.");
        }
        int sourceLevel = Integer.parseInt(args[3]);
        boolean reportOnly = false;
        Path classpathFile = null;
        for (int index = 7; index < args.length; index++) {
            String option = args[index];
            if (option.equals("--report-only")) {
                if (reportOnly) {
                    throw new IllegalArgumentException("--report-only must not be repeated.");
                }
                reportOnly = true;
            } else if (option.startsWith("--classpath-file=")) {
                if (classpathFile != null) {
                    throw new IllegalArgumentException("--classpath-file must not be repeated.");
                }
                String value = option.substring("--classpath-file=".length());
                if (value.isBlank()) {
                    throw new IllegalArgumentException("--classpath-file requires a path.");
                }
                classpathFile = Path.of(value).toAbsolutePath().normalize();
            } else {
                throw new IllegalArgumentException("Unsupported option: " + option);
            }
        }

        Path projectRoot = Path.of(args[4]).toAbsolutePath().normalize();
        if (!Files.isDirectory(projectRoot)) {
            throw new IllegalStateException("Representative project root is missing: " + projectRoot);
        }
        List<Path> resolutionRoots = resolveRoots(projectRoot, args[5]);
        List<Path> parseRoots = resolveRoots(projectRoot, args[6]);
        if (!resolutionRoots.containsAll(parseRoots)) {
            throw new IllegalStateException("Every parsed source root must be present in the resolution roots.");
        }

        List<Path> externalClasspath = classpathFile == null
            ? List.of()
            : ClasspathSnapshot.read(classpathFile);
        ClasspathSnapshot.rejectCandidateEngines(externalClasspath);

        List<Path> sourceFiles = discoverSourceFiles(projectRoot, parseRoots);
        if (sourceFiles.isEmpty()) {
            throw new IllegalStateException("No Java source files were discovered for " + projectId + "/" + sourceSet);
        }

        long startedAt = System.nanoTime();
        Metrics metrics = engine.equals("jdt")
            ? analyzeWithJdt(sourceFiles, resolutionRoots, externalClasspath, sourceLevel)
            : analyzeWithJavaParser(sourceFiles, resolutionRoots, externalClasspath, sourceLevel);
        metrics.filesDiscovered = sourceFiles.size();
        metrics.elapsedMillis = (System.nanoTime() - startedAt) / 1_000_000;

        String environment = externalClasspath.isEmpty()
            ? "source-roots+jdk-only"
            : "source-roots+jdk+curated-external-classpath";
        printResult(engine, projectId, sourceSet, sourceLevel,
            environment, externalClasspath.size(), metrics);
        if (!reportOnly && (metrics.parseFailures > 0 || metrics.syntaxErrors > 0)) {
            throw new IllegalStateException(
                engine + " failed to parse " + metrics.parseFailures + " of "
                    + metrics.filesDiscovered + " files and reported "
                    + metrics.syntaxErrors + " syntax errors in "
                    + projectId + "/" + sourceSet);
        }
    }

    private static Metrics analyzeWithJavaParser(
        List<Path> sourceFiles,
        List<Path> resolutionRoots,
        List<Path> externalClasspath,
        int sourceLevel) throws IOException {

        ParserConfiguration sourceConfiguration = new ParserConfiguration()
            .setLanguageLevel(javaParserLanguageLevel(sourceLevel))
            .setAttributeComments(false);
        CombinedTypeSolver typeSolver = new CombinedTypeSolver();
        typeSolver.add(new ReflectionTypeSolver());
        for (Path sourceRoot : resolutionRoots) {
            typeSolver.add(new JavaParserTypeSolver(sourceRoot.toFile(), sourceConfiguration));
        }
        for (Path classpathEntry : externalClasspath) {
            typeSolver.add(new JarTypeSolver(classpathEntry));
        }
        ParserConfiguration parserConfiguration = new ParserConfiguration()
            .setLanguageLevel(javaParserLanguageLevel(sourceLevel))
            .setAttributeComments(false)
            .setSymbolResolver(new JavaSymbolSolver(typeSolver));
        JavaParser parser = new JavaParser(parserConfiguration);
        Metrics metrics = new Metrics();

        for (Path sourceFile : sourceFiles) {
            try {
                var parseResult = parser.parse(sourceFile);
                // JavaParser ParseResult problems are parser errors; the API exposes no severity field.
                metrics.syntaxErrors += parseResult.getProblems().size();
                if (parseResult.getResult().isEmpty()) {
                    metrics.parseFailures++;
                    continue;
                }

                CompilationUnit unit = parseResult.getResult().orElseThrow();
                metrics.filesParsed++;
                metrics.typeDeclarations += unit.findAll(ClassOrInterfaceDeclaration.class).size()
                    + unit.findAll(EnumDeclaration.class).size()
                    + unit.findAll(RecordDeclaration.class).size()
                    + unit.findAll(AnnotationDeclaration.class).size();
                metrics.methodDeclarations += unit.findAll(MethodDeclaration.class).size()
                    + unit.findAll(ConstructorDeclaration.class).size();

                for (MethodCallExpr call : unit.findAll(MethodCallExpr.class)) {
                    metrics.methodCalls++;
                    try {
                        call.resolve();
                        metrics.resolvedMethodCalls++;
                    } catch (RuntimeException unresolved) {
                        int line = call.getBegin().map(position -> position.line).orElse(0);
                        metrics.recordUnresolvedMethodCall(
                            sourceFile,
                            line,
                            call.toString(),
                            unresolved.getClass().getSimpleName() + ": " + unresolved.getMessage());
                    }
                }

                for (ClassOrInterfaceType type : unit.findAll(ClassOrInterfaceType.class)) {
                    if (isQualifiedTypeScope(type)) {
                        continue;
                    }
                    metrics.typeReferences++;
                    try {
                        type.resolve();
                        metrics.resolvedTypeReferences++;
                    } catch (RuntimeException unresolved) {
                        // Missing external dependencies remain visible as unresolved evidence.
                    }
                }
            } catch (IOException exception) {
                throw new IllegalStateException("Unable to read representative source " + sourceFile, exception);
            }
        }
        return metrics;
    }

    private static Metrics analyzeWithJdt(
        List<Path> sourceFiles,
        List<Path> resolutionRoots,
        List<Path> externalClasspath,
        int sourceLevel) throws IOException {

        String[] sourcePaths = resolutionRoots.stream().map(Path::toString).toArray(String[]::new);
        String[] encodings = resolutionRoots.stream().map(ignored -> "UTF-8").toArray(String[]::new);
        String[] classpathPaths = externalClasspath.stream().map(Path::toString).toArray(String[]::new);
        var compilerOptions = JavaCore.getOptions();
        JavaCore.setComplianceOptions(jdtLanguageLevel(sourceLevel), compilerOptions);
        Metrics metrics = new Metrics();

        for (Path sourceFile : sourceFiles) {
            ASTParser parser = ASTParser.newParser(AST.getJLSLatest());
            parser.setKind(ASTParser.K_COMPILATION_UNIT);
            parser.setSource(Files.readString(sourceFile).toCharArray());
            parser.setUnitName(resolveUnitName(sourceFile, resolutionRoots));
            parser.setEnvironment(classpathPaths, sourcePaths, encodings, true);
            parser.setResolveBindings(true);
            parser.setBindingsRecovery(false);
            parser.setStatementsRecovery(true);
            parser.setCompilerOptions(compilerOptions);

            org.eclipse.jdt.core.dom.CompilationUnit unit =
                (org.eclipse.jdt.core.dom.CompilationUnit) parser.createAST(null);
            metrics.filesParsed++;
            for (IProblem problem : unit.getProblems()) {
                if (problem.isError() && (problem.getID() & IProblem.Syntax) != 0) {
                    metrics.syntaxErrors++;
                }
            }

            unit.accept(new ASTVisitor() {
                @Override
                public boolean visit(org.eclipse.jdt.core.dom.TypeDeclaration declaration) {
                    metrics.typeDeclarations++;
                    return true;
                }

                @Override
                public boolean visit(org.eclipse.jdt.core.dom.EnumDeclaration declaration) {
                    metrics.typeDeclarations++;
                    return true;
                }

                @Override
                public boolean visit(org.eclipse.jdt.core.dom.RecordDeclaration declaration) {
                    metrics.typeDeclarations++;
                    return true;
                }

                @Override
                public boolean visit(org.eclipse.jdt.core.dom.AnnotationTypeDeclaration declaration) {
                    metrics.typeDeclarations++;
                    return true;
                }

                @Override
                public boolean visit(org.eclipse.jdt.core.dom.MethodDeclaration declaration) {
                    metrics.methodDeclarations++;
                    return true;
                }

                @Override
                public boolean visit(MethodInvocation invocation) {
                    metrics.methodCalls++;
                    if (invocation.resolveMethodBinding() != null) {
                        metrics.resolvedMethodCalls++;
                    } else {
                        int line = unit.getLineNumber(invocation.getStartPosition());
                        metrics.recordUnresolvedMethodCall(
                            sourceFile,
                            line,
                            invocation.toString(),
                            "JDT binding recovery returned no method binding");
                    }
                    return true;
                }

                @Override
                public boolean visit(SimpleType type) {
                    recordTypeReference(type, type.resolveBinding(), metrics);
                    return true;
                }

                @Override
                public boolean visit(org.eclipse.jdt.core.dom.QualifiedType type) {
                    recordTypeReference(type, type.resolveBinding(), metrics);
                    return true;
                }

                @Override
                public boolean visit(org.eclipse.jdt.core.dom.NameQualifiedType type) {
                    recordTypeReference(type, type.resolveBinding(), metrics);
                    return true;
                }
            });
        }
        return metrics;
    }

    private static boolean isQualifiedTypeScope(ClassOrInterfaceType type) {
        return type.getParentNode()
            .filter(ClassOrInterfaceType.class::isInstance)
            .map(ClassOrInterfaceType.class::cast)
            .flatMap(ClassOrInterfaceType::getScope)
            .filter(scope -> scope == type)
            .isPresent();
    }

    private static void recordTypeReference(
        org.eclipse.jdt.core.dom.Type type,
        ITypeBinding binding,
        Metrics metrics) {
        if (type.getParent() instanceof org.eclipse.jdt.core.dom.QualifiedType parent
            && parent.getQualifier() == type) {
            return;
        }
        metrics.typeReferences++;
        if (binding != null) {
            metrics.resolvedTypeReferences++;
        }
    }

    private static List<Path> resolveRoots(Path projectRoot, String value) {
        List<Path> roots = new ArrayList<>();
        for (String item : value.split(",", -1)) {
            if (item.isBlank()) {
                throw new IllegalArgumentException("Source root entries must not be empty.");
            }
            Path root = projectRoot.resolve(item).toAbsolutePath().normalize();
            if (!root.startsWith(projectRoot) || !Files.isDirectory(root)) {
                throw new IllegalStateException("Source root is missing or escapes the project: " + item);
            }
            if (!roots.contains(root)) {
                roots.add(root);
            }
        }
        return List.copyOf(roots);
    }

    private static List<Path> discoverSourceFiles(Path projectRoot, List<Path> parseRoots)
        throws IOException {
        Set<Path> sourceFiles = new TreeSet<>(Comparator.comparing(Path::toString));
        for (Path root : parseRoots) {
            try (Stream<Path> paths = Files.walk(root)) {
                paths.filter(Files::isRegularFile)
                    .filter(path -> path.getFileName().toString().endsWith(".java"))
                    .map(path -> path.toAbsolutePath().normalize())
                    .filter(path -> path.startsWith(projectRoot))
                    .forEach(sourceFiles::add);
            }
        }
        return List.copyOf(sourceFiles);
    }

    private static String resolveUnitName(Path sourceFile, List<Path> sourceRoots) {
        return sourceRoots.stream()
            .filter(sourceFile::startsWith)
            .max(Comparator.comparingInt(Path::getNameCount))
            .map(root -> root.relativize(sourceFile).toString().replace('\\', '/'))
            .orElseGet(() -> sourceFile.getFileName().toString());
    }

    private static ParserConfiguration.LanguageLevel javaParserLanguageLevel(int sourceLevel) {
        return switch (sourceLevel) {
            case 11 -> ParserConfiguration.LanguageLevel.JAVA_11;
            case 17 -> ParserConfiguration.LanguageLevel.JAVA_17;
            case 21 -> ParserConfiguration.LanguageLevel.JAVA_21;
            default -> throw new IllegalArgumentException("Unsupported Java source level: " + sourceLevel);
        };
    }

    private static String jdtLanguageLevel(int sourceLevel) {
        return switch (sourceLevel) {
            case 11 -> JavaCore.VERSION_11;
            case 17 -> JavaCore.VERSION_17;
            case 21 -> JavaCore.VERSION_21;
            default -> throw new IllegalArgumentException("Unsupported Java source level: " + sourceLevel);
        };
    }

    private static String requireText(String value, String name) {
        if (value == null || value.isBlank()) {
            throw new IllegalArgumentException(name + " must not be blank.");
        }
        return value;
    }

    private static void printResult(
        String engine,
        String projectId,
        String sourceSet,
        int sourceLevel,
        String environment,
        int externalClasspathEntries,
        Metrics metrics) {

        System.out.printf(
            Locale.ROOT,
            "{\"engine\":\"%s\",\"project\":\"%s\",\"sourceSet\":\"%s\","
                + "\"sourceLevel\":%d,\"environment\":\"%s\",\"externalClasspathEntries\":%d,"
                + "\"filesDiscovered\":%d,\"filesParsed\":%d,\"parseFailures\":%d,"
                + "\"syntaxErrors\":%d,\"typeDeclarations\":%d,\"methodDeclarations\":%d,"
                + "\"methodCalls\":%d,\"resolvedMethodCalls\":%d,\"unresolvedMethodCalls\":%d,"
                + "\"unresolvedMethodCallSamples\":%s,"
                + "\"typeReferenceMetric\":\"outermost-reference-type-nodes-v1\","
                + "\"typeReferences\":%d,\"resolvedTypeReferences\":%d,\"unresolvedTypeReferences\":%d,"
                + "\"methodCallResolutionPercent\":%.2f,\"typeResolutionPercent\":%.2f,"
                + "\"elapsedMillis\":%d}%n",
            jsonEscape(engine),
            jsonEscape(projectId),
            jsonEscape(sourceSet),
            sourceLevel,
            jsonEscape(environment),
            externalClasspathEntries,
            metrics.filesDiscovered,
            metrics.filesParsed,
            metrics.parseFailures,
            metrics.syntaxErrors,
            metrics.typeDeclarations,
            metrics.methodDeclarations,
            metrics.methodCalls,
            metrics.resolvedMethodCalls,
            metrics.methodCalls - metrics.resolvedMethodCalls,
            jsonArray(metrics.unresolvedMethodCallSamples),
            metrics.typeReferences,
            metrics.resolvedTypeReferences,
            metrics.typeReferences - metrics.resolvedTypeReferences,
            percentage(metrics.resolvedMethodCalls, metrics.methodCalls),
            percentage(metrics.resolvedTypeReferences, metrics.typeReferences),
            metrics.elapsedMillis);
    }

    private static String jsonArray(List<String> values) {
        StringBuilder json = new StringBuilder("[");
        for (int index = 0; index < values.size(); index++) {
            if (index > 0) {
                json.append(',');
            }
            json.append('"').append(jsonEscape(values.get(index))).append('"');
        }
        return json.append(']').toString();
    }

    private static double percentage(int resolved, int total) {
        return total == 0 ? 100.0 : (resolved * 100.0) / total;
    }

    private static String jsonEscape(String value) {
        return value.replace("\\", "\\\\")
            .replace("\"", "\\\"")
            .replace("\n", "\\n")
            .replace("\r", "\\r");
    }

    private static final class Metrics {
        private static final int MAX_UNRESOLVED_METHOD_CALL_SAMPLES = 20;

        private final List<String> unresolvedMethodCallSamples = new ArrayList<>();
        private int filesDiscovered;
        private int filesParsed;
        private int parseFailures;
        private int syntaxErrors;
        private int typeDeclarations;
        private int methodDeclarations;
        private int methodCalls;
        private int resolvedMethodCalls;
        private int typeReferences;
        private int resolvedTypeReferences;
        private long elapsedMillis;

        private void recordUnresolvedMethodCall(
            Path sourceFile,
            int line,
            String expression,
            String diagnostic) {
            if (unresolvedMethodCallSamples.size() >= MAX_UNRESOLVED_METHOD_CALL_SAMPLES) {
                return;
            }

            String normalizedDiagnostic = diagnostic == null
                ? "unresolved"
                : diagnostic.replaceAll("\\s+", " ").trim();
            if (normalizedDiagnostic.length() > 300) {
                normalizedDiagnostic = normalizedDiagnostic.substring(0, 300);
            }

            unresolvedMethodCallSamples.add(
                sourceFile + ":" + line + ": " + expression + " => " + normalizedDiagnostic);
        }
    }
}
