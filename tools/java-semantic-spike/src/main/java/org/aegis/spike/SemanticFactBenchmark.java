package org.aegis.spike;

import com.github.javaparser.JavaParser;
import com.github.javaparser.ParseResult;
import com.github.javaparser.ParserConfiguration;
import com.github.javaparser.ast.CompilationUnit;
import com.github.javaparser.ast.Node;
import com.github.javaparser.ast.body.ClassOrInterfaceDeclaration;
import com.github.javaparser.ast.body.RecordDeclaration;
import com.github.javaparser.ast.expr.AnnotationExpr;
import com.github.javaparser.ast.expr.MethodCallExpr;
import com.github.javaparser.ast.expr.MethodReferenceExpr;
import com.github.javaparser.ast.expr.ObjectCreationExpr;
import com.github.javaparser.ast.type.ClassOrInterfaceType;
import com.github.javaparser.ast.modules.ModuleExportsDirective;
import com.github.javaparser.ast.modules.ModuleRequiresDirective;
import com.github.javaparser.resolution.declarations.ResolvedConstructorDeclaration;
import com.github.javaparser.resolution.declarations.ResolvedMethodDeclaration;
import com.github.javaparser.symbolsolver.JavaSymbolSolver;
import com.github.javaparser.symbolsolver.resolution.typesolvers.CombinedTypeSolver;
import com.github.javaparser.symbolsolver.resolution.typesolvers.JavaParserTypeSolver;
import com.github.javaparser.symbolsolver.resolution.typesolvers.ReflectionTypeSolver;
import org.eclipse.jdt.core.JavaCore;
import org.eclipse.jdt.core.dom.AST;
import org.eclipse.jdt.core.dom.ASTParser;
import org.eclipse.jdt.core.dom.ASTVisitor;
import org.eclipse.jdt.core.dom.Annotation;
import org.eclipse.jdt.core.dom.ClassInstanceCreation;
import org.eclipse.jdt.core.dom.ExportsDirective;
import org.eclipse.jdt.core.dom.IMethodBinding;
import org.eclipse.jdt.core.dom.ITypeBinding;
import org.eclipse.jdt.core.dom.MethodInvocation;
import org.eclipse.jdt.core.dom.ModuleDeclaration;
import org.eclipse.jdt.core.dom.RequiresDirective;
import org.eclipse.jdt.core.dom.SimpleType;
import org.eclipse.jdt.core.dom.TypeDeclaration;

import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.Comparator;
import java.util.HashMap;
import java.util.HashSet;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.Set;
import java.util.stream.Stream;

/**
 * Extracts semantic facts independently with JDT and JavaParser and compares
 * each extraction with the stable, engine-neutral fact oracle.
 */
public final class SemanticFactBenchmark {
    private static final String HEADER =
        "fact_id\tkind\tmodule\tsource_set\tfixture_path\tsource_identity\ttarget_identity\texpectation";

    private SemanticFactBenchmark() {
    }

    public static void main(String[] args) throws Exception {
        if (args.length < 3 || args.length > 5) {
            throw new IllegalArgumentException(
                "Usage: SemanticFactBenchmark <jdt|javaparser> <source-root> <fact-corpus> "
                    + "[--project-root] [--report-only]");
        }
        boolean projectRootMode = args.length >= 4 && args[3].equals("--project-root");
        boolean reportOnly = java.util.Arrays.asList(args).contains("--report-only");
        if ((args.length == 4 && !projectRootMode && !reportOnly)
            || (args.length == 5 && (!projectRootMode || !reportOnly))) {
            throw new IllegalArgumentException("Unsupported benchmark options.");
        }

        String engine = args[0].toLowerCase(Locale.ROOT);
        if (!engine.equals("jdt") && !engine.equals("javaparser")) {
            throw new IllegalArgumentException("Unsupported engine: " + engine);
        }

        Path fixtureRoot = Path.of(args[1]).toAbsolutePath().normalize();
        Path corpus = Path.of(args[2]).toAbsolutePath().normalize();
        List<Fact> facts = readFacts(corpus);
        Path workspace = Files.createTempDirectory("aegis-semantic-facts-");
        try {
            Map<String, Path> sources = projectRootMode
                ? loadProjectSources(fixtureRoot, facts)
                : materializeFactSources(fixtureRoot, facts, workspace);
            Map<String, Observation> observations = engine.equals("jdt")
                ? extractWithJdt(sources)
                : extractWithJavaParser(sources);

            List<String> failures = new ArrayList<>();
            for (Fact fact : facts) {
                Observation observation = observations.getOrDefault(
                    key(fact.fixturePath(), fact.kind(), fact.sourceIdentity(), fact.targetIdentity()),
                    Observation.missing());
                boolean passed = fact.expectation().equals(observation.resolution());
                if (!passed) {
                    String candidates = observations.entrySet().stream()
                        .filter(candidate -> candidate.getKey().startsWith(
                            fact.fixturePath() + "|" + fact.kind() + "|" + fact.sourceIdentity() + "|"))
                        .map(candidate -> candidate.getValue().identity()
                            + "(" + candidate.getValue().resolution() + ")")
                        .sorted()
                        .reduce((left, right) -> left + "," + right)
                        .orElse("<none>");
                    failures.add(fact.factId() + " from " + fact.sourceIdentity()
                        + " expected " + fact.targetIdentity()
                        + " (" + fact.expectation() + ") but observed "
                        + observation.identity() + " (" + observation.resolution()
                        + "); candidates=" + candidates);
                }
                System.out.printf(
                    Locale.ROOT,
                    "{\"engine\":\"%s\",\"factId\":\"%s\",\"kind\":\"%s\",\"sourceIdentity\":\"%s\",\"expected\":\"%s\",\"observed\":\"%s\",\"resolution\":\"%s\",\"passed\":%s}%n",
                    engine,
                    fact.factId(),
                    fact.kind(),
                    jsonEscape(fact.sourceIdentity()),
                    fact.targetIdentity(),
                    observation.identity(),
                    observation.resolution(),
                    passed);
            }

            String status = failures.isEmpty()
                ? "passed"
                : reportOnly ? "completed-with-mismatches" : "failed";
            System.out.printf(
                "{\"engine\":\"%s\",\"status\":\"%s\",\"facts\":%d,\"mismatches\":%d}%n",
                engine,
                status,
                facts.size(),
                failures.size());
            if (!failures.isEmpty() && !reportOnly) {
                throw new IllegalStateException(
                    engine + " failed " + failures.size() + " of " + facts.size()
                        + " semantic facts: " + String.join("; ", failures));
            }
        } finally {
            deleteDirectory(workspace);
        }
    }

    private static List<Fact> readFacts(Path corpus) throws IOException {
        List<String> lines = Files.readAllLines(corpus);
        if (lines.isEmpty() || !HEADER.equals(lines.getFirst())) {
            throw new IllegalStateException("Semantic fact corpus header is invalid.");
        }

        List<Fact> facts = new ArrayList<>();
        for (int index = 1; index < lines.size(); index++) {
            if (lines.get(index).isBlank()) {
                continue;
            }
            String[] fields = lines.get(index).split("\t", -1);
            if (fields.length != 8) {
                throw new IllegalStateException("Invalid fact row at line " + (index + 1));
            }
            if (fields[0].isBlank() || fields[1].isBlank() || fields[4].isBlank()
                || fields[5].isBlank() || fields[6].isBlank() || fields[7].isBlank()) {
                throw new IllegalStateException("Fact fields must not be blank at line " + (index + 1));
            }
            if (facts.stream().anyMatch(existing -> existing.factId().equals(fields[0]))) {
                throw new IllegalStateException("Duplicate semantic fact ID at line " + (index + 1)
                    + ": " + fields[0]);
            }
            facts.add(new Fact(
                fields[0], fields[1], fields[4], fields[5], fields[6], fields[7]));
        }
        if (facts.isEmpty()) {
            throw new IllegalStateException("Semantic fact corpus is empty.");
        }
        return List.copyOf(facts);
    }

    private static Map<String, Path> loadProjectSources(
        Path projectRoot,
        List<Fact> facts) throws IOException {
        Path normalizedRoot = projectRoot.toRealPath();
        Map<String, Path> sources = new HashMap<>();
        for (Fact fact : facts) {
            Path relative = Path.of(fact.fixturePath()).normalize();
            if (relative.isAbsolute() || relative.startsWith("..")) {
                throw new IllegalStateException("Representative fact path escapes its project: "
                    + fact.fixturePath());
            }
            Path realSource = normalizedRoot.resolve(relative).toRealPath();
            if (!realSource.startsWith(normalizedRoot) || !Files.isRegularFile(realSource)) {
                throw new IllegalStateException("Representative fact source is missing or escapes its project: "
                    + fact.fixturePath());
            }
            sources.put(fact.fixturePath(), realSource);
        }
        return Map.copyOf(sources);
    }

    private static Map<String, Path> materializeFactSources(
        Path fixtureRoot,
        List<Fact> facts,
        Path workspace) throws IOException {
        Path moduleFixtureRoot = fixtureRoot.resolve("SemanticFacts").normalize();
        if (!moduleFixtureRoot.startsWith(fixtureRoot)
            || !Files.isDirectory(moduleFixtureRoot)) {
            throw new IllegalStateException("SemanticFacts fixture module is missing.");
        }

        Map<String, Path> sources = new HashMap<>();
        try (Stream<Path> fixtures = Files.walk(moduleFixtureRoot)) {
            for (Path fixture : fixtures
                .filter(Files::isRegularFile)
                .filter(file -> file.getFileName().toString().endsWith(".java.fixture"))
                .sorted()
                .toList()) {
                Path relative = fixtureRoot.relativize(fixture);
                Path destination = workspace.resolve(relative).normalize();
                if (!destination.startsWith(workspace)) {
                    throw new IllegalStateException("Fixture escapes temporary workspace.");
                }
                Files.createDirectories(destination.getParent());
                Path javaFile = destination.resolveSibling(
                    destination.getFileName().toString().replaceFirst("\\.fixture$", ""));
                Files.copy(fixture, javaFile);
                sources.put(relative.toString().replace('\\', '/'), javaFile);
            }
        }

        for (Fact fact : facts) {
            if (!sources.containsKey(fact.fixturePath())) {
                throw new IllegalStateException("Oracle references a missing fixture: "
                    + fact.fixturePath());
            }
        }
        return Map.copyOf(sources);
    }

    private static Map<String, Observation> extractWithJavaParser(Map<String, Path> sources) {
        ParserConfiguration sourceConfiguration = new ParserConfiguration()
            .setLanguageLevel(ParserConfiguration.LanguageLevel.JAVA_21);
        CombinedTypeSolver solver = new CombinedTypeSolver();
        solver.add(new ReflectionTypeSolver());
        for (Path root : discoverSourceRoots(sources.values())) {
            solver.add(new JavaParserTypeSolver(root.toFile(), sourceConfiguration));
        }

        JavaParser parser = new JavaParser(new ParserConfiguration()
            .setLanguageLevel(ParserConfiguration.LanguageLevel.JAVA_21)
            .setSymbolResolver(new JavaSymbolSolver(solver)));
        Map<String, Observation> observations = new HashMap<>();

        for (Map.Entry<String, Path> entry : sources.entrySet()) {
            String fixture = entry.getKey();
            try {
                ParseResult<CompilationUnit> result = parser.parse(entry.getValue());
                if (result.getResult().isEmpty()) {
                    throw new IllegalStateException("JavaParser could not parse " + fixture
                        + ": " + result.getProblems());
                }
                CompilationUnit unit = result.getResult().orElseThrow();

                for (RecordDeclaration declaration : unit.findAll(RecordDeclaration.class)) {
                    String sourceIdentity = declaration.resolve().getQualifiedName();
                    put(observations, fixture, "type-declaration",
                        sourceIdentity, sourceIdentity, "resolved");
                    for (ClassOrInterfaceType type : declaration.getImplementedTypes()) {
                        observeType(observations, fixture, "inheritance", sourceIdentity, type);
                    }
                }
                for (ClassOrInterfaceDeclaration declaration :
                    unit.findAll(ClassOrInterfaceDeclaration.class)) {
                    String sourceIdentity = null;
                    try {
                        sourceIdentity = declaration.resolve().getQualifiedName();
                        if (!declaration.isInterface()) {
                            put(observations, fixture, "type-declaration",
                                sourceIdentity, sourceIdentity, "resolved");
                        }
                    } catch (RuntimeException unresolved) {
                        // Captured as absence for this expected fact.
                    }
                    if (sourceIdentity != null && declaration.isInterface()) {
                        for (ClassOrInterfaceType type : declaration.getExtendedTypes()) {
                            observeType(observations, fixture, "generic-supertype", sourceIdentity, type);
                        }
                    } else if (sourceIdentity != null) {
                        for (ClassOrInterfaceType type : declaration.getExtendedTypes()) {
                            observeType(observations, fixture, "inheritance", sourceIdentity, type);
                        }
                    }
                }
                for (AnnotationExpr annotation : unit.findAll(AnnotationExpr.class)) {
                    try {
                        put(observations, fixture, "annotation",
                            javaParserSourceIdentity(annotation),
                            annotation.resolve().getQualifiedName(), "resolved");
                    } catch (RuntimeException unresolved) {
                        // An unresolved annotation is not a positive semantic fact.
                    }
                }
                for (ObjectCreationExpr creation : unit.findAll(ObjectCreationExpr.class)) {
                    try {
                        ResolvedConstructorDeclaration constructor = creation.resolve();
                        put(observations, fixture, "constructor-invocation",
                            javaParserSourceIdentity(creation),
                            constructorIdentity(constructor), "resolved");
                    } catch (RuntimeException unresolved) {
                        // Captured as absence for this expected fact.
                    }
                }
                for (MethodCallExpr call : unit.findAll(MethodCallExpr.class)) {
                    try {
                        ResolvedMethodDeclaration method = call.resolve();
                        String identity = methodIdentity(method);
                        String sourceIdentity = javaParserSourceIdentity(call);
                        put(observations, fixture, "method-invocation",
                            sourceIdentity, identity, "resolved");
                        put(observations, fixture, "overload-selection",
                            sourceIdentity, identity, "resolved");
                    } catch (RuntimeException unresolved) {
                        // Captured as absence for this expected fact.
                    }
                }
                for (MethodReferenceExpr reference : unit.findAll(MethodReferenceExpr.class)) {
                    try {
                        ResolvedMethodDeclaration method = reference.resolve();
                        String identity = reference.getIdentifier().equals("new")
                            ? constructorReferenceIdentity(method)
                            : methodIdentity(method);
                        put(observations, fixture, "method-reference",
                            javaParserSourceIdentity(reference), identity, "resolved");
                    } catch (RuntimeException unresolved) {
                        // Captured as absence for this expected fact.
                    }
                }
                for (ClassOrInterfaceType type : unit.findAll(ClassOrInterfaceType.class)) {
                    if (!type.getNameAsString().equals("MissingPort")) {
                        continue;
                    }
                    String identity = unit.getImports().stream()
                        .map(importDeclaration -> importDeclaration.getNameAsString())
                        .filter(name -> name.endsWith("." + type.getNameAsString()))
                        .findFirst()
                        .orElseGet(() -> unit.getPackageDeclaration()
                            .map(declaration -> declaration.getNameAsString() + "."
                                + type.getNameAsString())
                            .orElse(type.getNameAsString()));
                    try {
                        type.resolve();
                        put(observations, fixture, "unresolved-type",
                            javaParserSourceIdentity(type), identity, "resolved");
                    } catch (RuntimeException unresolved) {
                        put(observations, fixture, "unresolved-type",
                            javaParserSourceIdentity(type), identity, "unresolved");
                    }
                }
                unit.getModule().ifPresent(module -> {
                    for (var directive : module.getDirectives()) {
                        if (directive instanceof ModuleRequiresDirective requires) {
                            put(observations, fixture, "module-requires",
                                module.getNameAsString(), requires.getNameAsString(), "resolved");
                        } else if (directive instanceof ModuleExportsDirective exports) {
                            put(observations, fixture, "module-exports",
                                module.getNameAsString(), exports.getNameAsString(), "resolved");
                        }
                    }
                });
            } catch (IOException failure) {
                throw new IllegalStateException("Unable to parse " + fixture, failure);
            }
        }
        return observations;
    }

    private static void observeType(
        Map<String, Observation> observations,
        String fixture,
        String kind,
        String sourceIdentity,
        ClassOrInterfaceType type) {
        try {
            put(observations, fixture, kind, sourceIdentity,
                type.resolve().describe(), "resolved");
        } catch (RuntimeException unresolved) {
            // Captured as absence for this expected fact.
        }
    }

    private static Map<String, Observation> extractWithJdt(Map<String, Path> sources)
        throws IOException {
        List<Path> sourceRoots = discoverSourceRoots(sources.values());
        String[] sourcePaths = sourceRoots.stream().map(Path::toString).toArray(String[]::new);
        String[] encodings = sourceRoots.stream().map(ignored -> "UTF-8").toArray(String[]::new);
        Map<String, String> options = JavaCore.getOptions();
        JavaCore.setComplianceOptions(JavaCore.VERSION_21, options);
        Map<String, Observation> observations = new HashMap<>();

        for (Map.Entry<String, Path> entry : sources.entrySet()) {
            String fixture = entry.getKey();
            Path sourceFile = entry.getValue();
            ASTParser parser = ASTParser.newParser(AST.getJLSLatest());
            parser.setKind(ASTParser.K_COMPILATION_UNIT);
            parser.setSource(Files.readString(sourceFile).toCharArray());
            parser.setUnitName(resolveUnitName(sourceFile, sourceRoots));
            parser.setEnvironment(new String[0], sourcePaths, encodings, true);
            parser.setResolveBindings(true);
            parser.setBindingsRecovery(false);
            parser.setStatementsRecovery(true);
            parser.setCompilerOptions(options);
            org.eclipse.jdt.core.dom.CompilationUnit unit =
                (org.eclipse.jdt.core.dom.CompilationUnit) parser.createAST(null);

            unit.accept(new ASTVisitor() {
                @Override
                public boolean visit(org.eclipse.jdt.core.dom.RecordDeclaration declaration) {
                    ITypeBinding binding = declaration.resolveBinding();
                    if (binding != null) {
                        String sourceIdentity = binding.getTypeDeclaration().getQualifiedName();
                        put(observations, fixture, "type-declaration",
                            sourceIdentity, sourceIdentity, "resolved");
                    }
                    for (Object item : declaration.superInterfaceTypes()) {
                        observeJdtType(observations, fixture, "inheritance",
                            binding.getTypeDeclaration().getQualifiedName(),
                            (org.eclipse.jdt.core.dom.Type) item);
                    }
                    return true;
                }

                @Override
                public boolean visit(TypeDeclaration declaration) {
                    ITypeBinding binding = declaration.resolveBinding();
                    if (binding != null && !declaration.isInterface()) {
                        String sourceIdentity = binding.getTypeDeclaration().getQualifiedName();
                        put(observations, fixture, "type-declaration",
                            sourceIdentity, sourceIdentity, "resolved");
                    }
                    if (declaration.isInterface()) {
                        for (Object item : declaration.superInterfaceTypes()) {
                            observeJdtType(observations, fixture, "generic-supertype",
                                binding.getTypeDeclaration().getQualifiedName(),
                                (org.eclipse.jdt.core.dom.Type) item);
                        }
                    } else if (declaration.getSuperclassType() != null) {
                        observeJdtType(observations, fixture, "inheritance",
                            binding == null ? "<unresolved>" : binding.getTypeDeclaration().getQualifiedName(),
                            declaration.getSuperclassType());
                    }
                    return true;
                }

                @Override
                public boolean visit(org.eclipse.jdt.core.dom.MarkerAnnotation annotation) {
                    observeJdtAnnotation(observations, fixture, annotation);
                    return true;
                }

                @Override
                public boolean visit(org.eclipse.jdt.core.dom.NormalAnnotation annotation) {
                    observeJdtAnnotation(observations, fixture, annotation);
                    return true;
                }

                @Override
                public boolean visit(org.eclipse.jdt.core.dom.SingleMemberAnnotation annotation) {
                    observeJdtAnnotation(observations, fixture, annotation);
                    return true;
                }

                @Override
                public boolean visit(ClassInstanceCreation creation) {
                    IMethodBinding binding = creation.resolveConstructorBinding();
                    if (binding != null) {
                        put(observations, fixture, "constructor-invocation",
                            jdtSourceIdentity(creation), jdtMethodIdentity(binding, true), "resolved");
                    }
                    return true;
                }

                @Override
                public boolean visit(MethodInvocation invocation) {
                    IMethodBinding binding = invocation.resolveMethodBinding();
                    if (binding != null) {
                        String identity = jdtMethodIdentity(binding, false);
                        String sourceIdentity = jdtSourceIdentity(invocation);
                        put(observations, fixture, "method-invocation",
                            sourceIdentity, identity, "resolved");
                        put(observations, fixture, "overload-selection",
                            sourceIdentity, identity, "resolved");
                    }
                    return true;
                }

                @Override
                public boolean visit(org.eclipse.jdt.core.dom.CreationReference reference) {
                    IMethodBinding binding = reference.resolveMethodBinding();
                    if (binding != null) {
                        put(observations, fixture, "method-reference",
                            jdtSourceIdentity(reference), jdtMethodIdentity(binding, true), "resolved");
                    }
                    return true;
                }

                @Override
                public boolean visit(SimpleType type) {
                    if (type.getName().getFullyQualifiedName().equals("MissingPort")) {
                        String packageName = unit.getPackage() == null
                            ? "" : unit.getPackage().getName().getFullyQualifiedName();
                        String fallbackIdentity = packageName.isEmpty()
                            ? "MissingPort" : packageName + ".MissingPort";
                        String identity = findImportedTypeName(
                            unit, "MissingPort", fallbackIdentity);
                        ITypeBinding binding = type.resolveBinding();
                        put(observations, fixture, "unresolved-type",
                            jdtSourceIdentity(type),
                            binding == null ? identity : binding.getQualifiedName(),
                            binding == null ? "unresolved" : "resolved");
                    }
                    return true;
                }
            });

            org.eclipse.jdt.core.dom.ModuleDeclaration module = unit.getModule();
            if (module != null) {
                for (Object directive : module.moduleStatements()) {
                    if (directive instanceof RequiresDirective requires) {
                        put(observations, fixture, "module-requires",
                            module.getName().getFullyQualifiedName(),
                            requires.getName().getFullyQualifiedName(), "resolved");
                    } else if (directive instanceof ExportsDirective exports) {
                        put(observations, fixture, "module-exports",
                            module.getName().getFullyQualifiedName(),
                            exports.getName().getFullyQualifiedName(), "resolved");
                    }
                }
            }
        }
        return observations;
    }

    private static List<Path> discoverSourceRoots(Iterable<Path> sources) {
        Set<Path> roots = new HashSet<>();
        for (Path source : sources) {
            Path current = source.getParent();
            while (current != null) {
                if (current.getFileName() != null
                    && current.getFileName().toString().equals("java")) {
                    roots.add(current);
                    break;
                }
                current = current.getParent();
            }
        }
        return roots.stream().sorted(Comparator.comparing(Path::toString)).toList();
    }

    private static String resolveUnitName(Path sourceFile, List<Path> sourceRoots) {
        return sourceRoots.stream()
            .filter(sourceFile::startsWith)
            .max(Comparator.comparingInt(Path::getNameCount))
            .map(root -> root.relativize(sourceFile).toString().replace('\\', '/'))
            .orElseGet(() -> sourceFile.getFileName().toString());
    }

    private static String findImportedTypeName(
        org.eclipse.jdt.core.dom.CompilationUnit unit,
        String simpleName,
        String fallbackIdentity) {
        for (Object item : unit.imports()) {
            org.eclipse.jdt.core.dom.ImportDeclaration importDeclaration =
                (org.eclipse.jdt.core.dom.ImportDeclaration) item;
            String importedName = importDeclaration.getName().getFullyQualifiedName();
            if (importedName.endsWith("." + simpleName)) {
                return importedName;
            }
        }
        return fallbackIdentity;
    }

    private static void observeJdtAnnotation(
        Map<String, Observation> observations,
        String fixture,
        Annotation annotation) {
        ITypeBinding binding = annotation.resolveTypeBinding();
        if (binding != null) {
            put(observations, fixture, "annotation",
                jdtSourceIdentity(annotation),
                binding.getTypeDeclaration().getQualifiedName(), "resolved");
        }
    }

    private static void observeJdtType(
        Map<String, Observation> observations,
        String fixture,
        String kind,
        String sourceIdentity,
        org.eclipse.jdt.core.dom.Type type) {
        ITypeBinding binding = type.resolveBinding();
        if (binding != null) {
            put(observations, fixture, kind, sourceIdentity, binding.getQualifiedName(), "resolved");
        }
    }

    private static String javaParserSourceIdentity(Node node) {
        Node current = node;
        while (current != null) {
            if (current instanceof com.github.javaparser.ast.body.MethodDeclaration method) {
                try {
                    ResolvedMethodDeclaration resolved = method.resolve();
                    return sourceMethodIdentity(resolved);
                } catch (RuntimeException unresolved) {
                    return "<unresolved-source>";
                }
            }
            if (current instanceof com.github.javaparser.ast.body.ConstructorDeclaration constructor) {
                try {
                    return sourceConstructorIdentity(constructor.resolve());
                } catch (RuntimeException unresolved) {
                    return "<unresolved-source>";
                }
            }
            if (current instanceof ClassOrInterfaceDeclaration declaration) {
                try {
                    return declaration.resolve().getQualifiedName();
                } catch (RuntimeException unresolved) {
                    return declaration.getFullyQualifiedName().orElse("<unresolved-source>");
                }
            }
            if (current instanceof RecordDeclaration declaration) {
                try {
                    return declaration.resolve().getQualifiedName();
                } catch (RuntimeException unresolved) {
                    return declaration.getFullyQualifiedName().orElse("<unresolved-source>");
                }
            }
            if (current instanceof ModuleDeclaration module) {
                return module.getNameAsString();
            }
            current = current.getParentNode().orElse(null);
        }
        return "<unknown-source>";
    }

    private static String sourceMethodIdentity(ResolvedMethodDeclaration method) {
        String parameters = java.util.stream.IntStream.range(0, method.getNumberOfParams())
            .mapToObj(index -> method.getParam(index).getType().describe())
            .reduce((left, right) -> left + "," + right).orElse("");
        return method.declaringType().getQualifiedName() + "#" + method.getName()
            + "(" + parameters + ")";
    }

    private static String sourceConstructorIdentity(ResolvedConstructorDeclaration constructor) {
        String parameters = java.util.stream.IntStream.range(0, constructor.getNumberOfParams())
            .mapToObj(index -> constructor.getParam(index).getType().describe())
            .reduce((left, right) -> left + "," + right).orElse("");
        return constructor.declaringType().getQualifiedName() + "#<init>(" + parameters + ")";
    }

    private static String jdtSourceIdentity(org.eclipse.jdt.core.dom.ASTNode node) {
        org.eclipse.jdt.core.dom.ASTNode current = node;
        while (current != null) {
            if (current instanceof org.eclipse.jdt.core.dom.MethodDeclaration method) {
                IMethodBinding binding = method.resolveBinding();
                if (binding != null) {
                    IMethodBinding declaration = binding.getMethodDeclaration();
                    String owner = declaration.getDeclaringClass().getTypeDeclaration().getQualifiedName();
                    String name = declaration.isConstructor() ? "<init>" : declaration.getName();
                    String parameters = Stream.of(declaration.getParameterTypes())
                        .map(ITypeBinding::getQualifiedName)
                        .reduce((left, right) -> left + "," + right).orElse("");
                    return owner + "#" + name + "(" + parameters + ")";
                }
            }
            if (current instanceof org.eclipse.jdt.core.dom.AbstractTypeDeclaration declaration) {
                ITypeBinding binding = declaration.resolveBinding();
                if (binding != null) {
                    return binding.getTypeDeclaration().getQualifiedName();
                }
            }
            if (current instanceof ModuleDeclaration module) {
                return module.getName().getFullyQualifiedName();
            }
            current = current.getParent();
        }
        return "<unknown-source>";
    }

    private static String jdtMethodIdentity(IMethodBinding binding, boolean constructor) {
        IMethodBinding method = binding.getMethodDeclaration();
        String owner = method.getDeclaringClass().getTypeDeclaration().getQualifiedName();
        String name = constructor ? "<init>" : method.getName();
        String parameters = Stream.of(method.getParameterTypes())
            .map(ITypeBinding::getQualifiedName)
            .reduce((left, right) -> left + "," + right).orElse("");
        String returnType = constructor ? "void" : method.getReturnType().getQualifiedName();
        return owner + "#" + name + "(" + parameters + "):" + returnType;
    }

    private static String methodIdentity(ResolvedMethodDeclaration method) {
        String parameters = java.util.stream.IntStream.range(0, method.getNumberOfParams())
            .mapToObj(index -> method.getParam(index).getType().describe())
            .reduce((left, right) -> left + "," + right).orElse("");
        return method.declaringType().getQualifiedName() + "#" + method.getName()
            + "(" + parameters + "):" + method.getReturnType().describe();
    }

    private static String constructorIdentity(ResolvedConstructorDeclaration constructor) {
        String parameters = java.util.stream.IntStream.range(0, constructor.getNumberOfParams())
            .mapToObj(index -> constructor.getParam(index).getType().describe())
            .reduce((left, right) -> left + "," + right).orElse("");
        return constructor.declaringType().getQualifiedName() + "#<init>(" + parameters + "):void";
    }

    private static String constructorReferenceIdentity(ResolvedMethodDeclaration constructor) {
        String parameters = java.util.stream.IntStream.range(0, constructor.getNumberOfParams())
            .mapToObj(index -> constructor.getParam(index).getType().describe())
            .reduce((left, right) -> left + "," + right).orElse("");
        return constructor.declaringType().getQualifiedName() + "#<init>(" + parameters + "):void";
    }

    private static void put(
        Map<String, Observation> observations,
        String fixture,
        String kind,
        String sourceIdentity,
        String identity,
        String resolution) {
        observations.put(key(fixture, kind, sourceIdentity, identity),
            new Observation(sourceIdentity, identity, resolution));
    }

    private static String key(
        String fixture,
        String kind,
        String sourceIdentity,
        String identity) {
        return fixture + "|" + kind + "|" + sourceIdentity + "|" + identity;
    }

    private static void deleteDirectory(Path directory) throws IOException {
        try (Stream<Path> paths = Files.walk(directory)) {
            for (Path path : paths.sorted(Comparator.reverseOrder()).toList()) {
                Files.deleteIfExists(path);
            }
        }
    }

    private record Fact(
        String factId,
        String kind,
        String fixturePath,
        String sourceIdentity,
        String targetIdentity,
        String expectation) {
    }

    private record Observation(String sourceIdentity, String identity, String resolution) {
        private static Observation missing() {
            return new Observation("<unknown-source>", "<not-observed>", "missing");
        }
    }
}
