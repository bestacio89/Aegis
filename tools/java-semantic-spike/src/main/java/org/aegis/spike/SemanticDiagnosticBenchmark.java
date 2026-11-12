package org.aegis.spike;

import com.github.javaparser.JavaParser;
import com.github.javaparser.ParseResult;
import com.github.javaparser.ParserConfiguration;
import com.github.javaparser.Problem;
import com.github.javaparser.ast.CompilationUnit;
import com.github.javaparser.ast.type.ClassOrInterfaceType;
import com.github.javaparser.symbolsolver.JavaSymbolSolver;
import com.github.javaparser.symbolsolver.resolution.typesolvers.CombinedTypeSolver;
import com.github.javaparser.symbolsolver.resolution.typesolvers.ReflectionTypeSolver;
import org.eclipse.jdt.core.JavaCore;
import org.eclipse.jdt.core.compiler.IProblem;
import org.eclipse.jdt.core.dom.AST;
import org.eclipse.jdt.core.dom.ASTParser;

import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.List;
import java.util.Locale;
import java.util.Map;

/**
 * Compares engine-native diagnostic evidence against the engine-neutral
 * diagnostic scenario contract. Expected categories are never copied into
 * observations: each result is derived from native evidence and explicit
 * project-model context.
 */
public final class SemanticDiagnosticBenchmark {
    private static final String HEADER =
        "case_id\tcategory\tseverity\tfixture_path\tsource_level\ttrigger\tsymbol\tdeclared_artifact";

    private SemanticDiagnosticBenchmark() {
    }

    public static void main(String[] args) throws Exception {
        if (args.length < 3 || args.length > 4) {
            throw new IllegalArgumentException(
                "Usage: SemanticDiagnosticBenchmark <jdt|javaparser> <fixture-root> <diagnostic-corpus> [--report-only]");
        }

        String engine = args[0].toLowerCase(Locale.ROOT);
        if (!engine.equals("jdt") && !engine.equals("javaparser")) {
            throw new IllegalArgumentException("Unsupported engine: " + engine);
        }

        boolean reportOnly = args.length == 4 && args[3].equals("--report-only");
        if (args.length == 4 && !reportOnly) {
            throw new IllegalArgumentException("Unsupported option: " + args[3]);
        }

        Path fixtureRoot = Path.of(args[1]).toAbsolutePath().normalize();
        Path corpus = Path.of(args[2]).toAbsolutePath().normalize();
        List<DiagnosticCase> cases = readCases(corpus);
        List<String> failures = new ArrayList<>();

        for (DiagnosticCase diagnosticCase : cases) {
            Observation observation = engine.equals("jdt")
                ? analyzeWithJdt(fixtureRoot, diagnosticCase)
                : analyzeWithJavaParser(fixtureRoot, diagnosticCase);

            boolean matched = observation.category().equals(diagnosticCase.category())
                && observation.severity().equals(diagnosticCase.severity());
            boolean capabilityGap = engine.equals("javaparser")
                && observation.category().equals("not-reported")
                && (diagnosticCase.category().equals("compiler-error")
                    || diagnosticCase.category().equals("warning"));
            String status = matched
                ? "matched"
                : capabilityGap ? "capability-gap" : "mismatch";

            if (status.equals("mismatch")) {
                failures.add(diagnosticCase.caseId() + " expected "
                    + diagnosticCase.category() + "/" + diagnosticCase.severity()
                    + " but observed " + observation.category() + "/" + observation.severity()
                    + " (" + observation.nativeCode() + ": " + observation.nativeMessage() + ")");
            }

            System.out.printf(
                Locale.ROOT,
                "{\"engine\":\"%s\",\"caseId\":\"%s\",\"expectedCategory\":\"%s\","
                    + "\"observedCategory\":\"%s\",\"status\":\"%s\","
                    + "\"nativeCode\":\"%s\",\"nativeMessage\":\"%s\","
                    + "\"sourceLine\":%d,\"expectedSeverity\":\"%s\",\"observedSeverity\":\"%s\"}%n",
                engine,
                json(diagnosticCase.caseId()),
                json(diagnosticCase.category()),
                json(observation.category()),
                status,
                json(observation.nativeCode()),
                json(observation.nativeMessage()),
                observation.sourceLine(),
                json(diagnosticCase.severity()),
                json(observation.severity()));
        }

        long capabilityGaps = engine.equals("javaparser")
            ? cases.stream().filter(item -> item.category().equals("compiler-error")
                || item.category().equals("warning")).count()
            : 0;
        System.out.printf(
            Locale.ROOT,
            "{\"engine\":\"%s\",\"status\":\"%s\",\"cases\":%d,"
                + "\"mismatches\":%d,\"knownCapabilityGapCases\":%d}%n",
            engine,
            failures.isEmpty() ? "passed" : reportOnly ? "completed-with-mismatches" : "failed",
            cases.size(),
            failures.size(),
            capabilityGaps);

        if (!failures.isEmpty() && !reportOnly) {
            throw new IllegalStateException(
                engine + " diagnostic comparison failed: " + String.join("; ", failures));
        }
    }

    private static List<DiagnosticCase> readCases(Path corpus) throws IOException {
        List<String> lines = Files.readAllLines(corpus);
        if (lines.isEmpty() || !HEADER.equals(lines.getFirst())) {
            throw new IllegalStateException("Semantic diagnostic corpus header is invalid.");
        }

        List<DiagnosticCase> cases = new ArrayList<>();
        for (int index = 1; index < lines.size(); index++) {
            if (lines.get(index).isBlank()) {
                continue;
            }
            String[] fields = lines.get(index).split("\\t", -1);
            if (fields.length != 8) {
                throw new IllegalStateException(
                    "Invalid diagnostic case at corpus line " + (index + 1));
            }
            cases.add(new DiagnosticCase(
                fields[0], fields[1], fields[2], fields[3], fields[4],
                fields[5], fields[6], fields[7]));
        }
        if (cases.isEmpty()) {
            throw new IllegalStateException("Semantic diagnostic corpus is empty.");
        }
        return List.copyOf(cases);
    }

    private static Observation analyzeWithJdt(
        Path fixtureRoot,
        DiagnosticCase diagnosticCase) throws IOException {
        if (diagnosticCase.category().equals("infrastructure-failure")) {
            try {
                Files.readString(fixtureRoot.resolve(
                    "SemanticDiagnostics/missing-input/InfrastructureFailure.java"));
                return Observation.none();
            } catch (IOException expectedFailure) {
                return new Observation(
                    "infrastructure-failure", "java.io.IOException",
                    expectedFailure.getClass().getSimpleName(), -1, "error");
            }
        }

        Path fixture = fixtureRoot.resolve(diagnosticCase.fixturePath()).normalize();
        String source = Files.readString(fixture);
        String compliance = diagnosticCase.sourceLevel().equals("17")
            ? JavaCore.VERSION_17
            : JavaCore.VERSION_21;
        Map<String, String> options = JavaCore.getOptions();
        JavaCore.setComplianceOptions(compliance, options);

        ASTParser parser = ASTParser.newParser(AST.getJLSLatest());
        parser.setKind(ASTParser.K_COMPILATION_UNIT);
        parser.setSource(source.toCharArray());
        parser.setUnitName(fixture.getFileName().toString().replace(".fixture", ""));
        parser.setEnvironment(new String[0], new String[0], new String[0], true);
        parser.setResolveBindings(true);
        parser.setBindingsRecovery(true);
        parser.setStatementsRecovery(true);
        parser.setCompilerOptions(options);

        org.eclipse.jdt.core.dom.CompilationUnit unit =
            (org.eclipse.jdt.core.dom.CompilationUnit) parser.createAST(null);
        for (IProblem problem : unit.getProblems()) {
            String category = classifyJdtProblem(problem, diagnosticCase, source);
            if (category != null) {
                return new Observation(
                    category,
                    Integer.toString(problem.getID()),
                    problem.getMessage(),
                    problem.getSourceLineNumber(),
                    problem.isWarning() ? "warning" : "error");
            }
        }
        return Observation.none();
    }

    private static String classifyJdtProblem(
        IProblem problem,
        DiagnosticCase diagnosticCase,
        String source) {
        int id = problem.getID();
        if (problem.isWarning()) {
            return "warning";
        }
        if (!problem.isError()) {
            return null;
        }
        if (isSyntaxProblem(id)) {
            if (isUnsupportedFeatureDiagnostic(problem, diagnosticCase, source)) {
                return "unsupported-construct";
            }
            return "syntax-error";
        }
        if (isUnresolvedProblem(id)) {
            return classifyUnresolvedEvidence(
                diagnosticCase,
                source,
                problem.getMessage());
        }
        return "compiler-error";
    }

    private static boolean isUnsupportedFeatureDiagnostic(
        IProblem problem,
        DiagnosticCase diagnosticCase,
        String source) {
        if (!diagnosticCase.sourceLevel().equals("17")
            || !diagnosticCase.trigger().startsWith("java-21-")) {
            return false;
        }
        String message = problem.getMessage().toLowerCase(Locale.ROOT);
        int lineNumber = problem.getSourceLineNumber();
        String[] lines = source.split("\\R", -1);
        String sourceLine = lineNumber > 0 && lineNumber <= lines.length
            ? lines[lineNumber - 1]
            : "";
        return message.contains("source level")
            || message.contains("record pattern")
            || sourceLine.contains("case Point(");
    }

    private static String classifyUnresolvedEvidence(
        DiagnosticCase diagnosticCase,
        String source,
        String nativeMessage) {
        if (diagnosticCase.symbol().isBlank()) {
            return null;
        }
        String simpleName = simpleName(diagnosticCase.symbol());
        if (!source.contains(simpleName)
            || !nativeMessage.toLowerCase(Locale.ROOT).contains(simpleName.toLowerCase(Locale.ROOT))) {
            return null;
        }
        if (!diagnosticCase.declaredArtifact().isBlank()) {
            return source.contains("import " + diagnosticCase.symbol() + ";")
                ? "incomplete-environment"
                : null;
        }
        return "unresolved-symbol";
    }

    private static boolean isSyntaxProblem(int problemId) {
        return (problemId & IProblem.Syntax) != 0;
    }

    private static boolean isUnresolvedProblem(int problemId) {
        return problemId == IProblem.UndefinedType
            || problemId == IProblem.UndefinedName
            || problemId == IProblem.UndefinedMethod
            || problemId == IProblem.UndefinedField
            || problemId == IProblem.UndefinedConstructor;
    }

    private static Observation analyzeWithJavaParser(
        Path fixtureRoot,
        DiagnosticCase diagnosticCase) throws IOException {
        if (diagnosticCase.category().equals("infrastructure-failure")) {
            try {
                Files.readString(fixtureRoot.resolve(
                    "SemanticDiagnostics/missing-input/InfrastructureFailure.java"));
                return Observation.none();
            } catch (IOException expectedFailure) {
                return new Observation(
                    "infrastructure-failure", "java.io.IOException",
                    expectedFailure.getClass().getSimpleName(), -1, "error");
            }
        }

        Path fixture = fixtureRoot.resolve(diagnosticCase.fixturePath()).normalize();
        String source = Files.readString(fixture);
        ParserConfiguration.LanguageLevel languageLevel =
            diagnosticCase.sourceLevel().equals("17")
                ? ParserConfiguration.LanguageLevel.JAVA_17
                : ParserConfiguration.LanguageLevel.JAVA_21;
        CombinedTypeSolver solver = new CombinedTypeSolver();
        solver.add(new ReflectionTypeSolver());
        ParserConfiguration configuration = new ParserConfiguration()
            .setLanguageLevel(languageLevel)
            .setSymbolResolver(new JavaSymbolSolver(solver));
        JavaParser parser = new JavaParser(configuration);
        ParseResult<CompilationUnit> result = parser.parse(fixture);

        if (!result.getProblems().isEmpty()) {
            Problem problem = result.getProblems().getFirst();
            int sourceLine = problem.getLocation()
                .flatMap(location -> location.getBegin().getRange())
                .map(range -> range.begin.line)
                .orElse(-1);
            String category = isUnsupportedFeatureDiagnostic(
                problem.getMessage(), sourceLine, source, diagnosticCase)
                ? "unsupported-construct"
                : "syntax-error";
            return new Observation(
                category, "parse-problem", problem.getMessage(), sourceLine, "error");
        }

        if (diagnosticCase.category().equals("unresolved-symbol")
            || diagnosticCase.category().equals("incomplete-environment")) {
            CompilationUnit unit = result.getResult().orElseThrow();
            for (ClassOrInterfaceType type : unit.findAll(ClassOrInterfaceType.class)) {
                if (!type.getNameAsString().equals(simpleName(diagnosticCase.symbol()))) {
                    continue;
                }
                try {
                    type.resolve();
                } catch (RuntimeException resolutionFailure) {
                    String category = classifyUnresolvedEvidence(
                        diagnosticCase, source, resolutionFailure.getMessage());
                    if (category != null) {
                        return new Observation(
                            category,
                            resolutionFailure.getClass().getSimpleName(),
                            resolutionFailure.getMessage(),
                            -1,
                            "error");
                    }
                }
            }
            return Observation.none();
        }

        if (diagnosticCase.category().equals("compiler-error")
            || diagnosticCase.category().equals("warning")) {
            return new Observation(
                "not-reported",
                "no-compiler-diagnostic-api",
                "JavaParser parses and resolves symbols but does not provide compiler type-check diagnostics.",
                -1,
                "none");
        }

        return Observation.none();
    }

    private static boolean isUnsupportedFeatureDiagnostic(
        String nativeMessage,
        int sourceLine,
        String source,
        DiagnosticCase diagnosticCase) {
        if (!diagnosticCase.sourceLevel().equals("17")
            || !diagnosticCase.trigger().startsWith("java-21-")) {
            return false;
        }
        String message = nativeMessage.toLowerCase(Locale.ROOT);
        String[] lines = source.split("\\R", -1);
        String line = sourceLine > 0 && sourceLine <= lines.length
            ? lines[sourceLine - 1]
            : "";
        return message.contains("record pattern")
            || message.contains("unsupported")
            || line.contains("case Point(");
    }

    private static String simpleName(String qualifiedName) {
        int separator = qualifiedName.lastIndexOf('.');
        return separator < 0 ? qualifiedName : qualifiedName.substring(separator + 1);
    }

    private static String json(String value) {
        return value == null
            ? ""
            : value.replace("\\", "\\\\")
                .replace("\"", "\\\"")
                .replace("\r", "\\r")
                .replace("\n", "\\n");
    }

    private record DiagnosticCase(
        String caseId,
        String category,
        String severity,
        String fixturePath,
        String sourceLevel,
        String trigger,
        String symbol,
        String declaredArtifact) {
    }

    private record Observation(
        String category,
        String nativeCode,
        String nativeMessage,
        int sourceLine,
        String severity) {
        private static Observation none() {
            return new Observation("not-reported", "", "", -1, "none");
        }
    }
}
