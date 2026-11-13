package org.aegis.worker;

import com.google.gson.JsonArray;
import com.google.gson.JsonObject;
import org.eclipse.jdt.core.JavaCore;
import org.eclipse.jdt.core.compiler.IProblem;
import org.eclipse.jdt.core.dom.*;

import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.*;
import java.util.stream.Collectors;

/**
 * Compiler-binding semantic adapter backed by Eclipse JDT Core.
 *
 * The worker receives an explicit module-scoped source/classpath snapshot.
 * It never invokes Maven, Gradle, plugins, annotation processors or project code.
 */
public final class JdtSemanticAnalysisEngine implements WorkerOperationHandler {
    public static final String ENGINE_ID = "eclipse-jdt";
    public static final String ENGINE_VERSION = "3.47.0";
    private static final int PROTOCOL_SYMBOL_CLASS = 4;
    private static final int PROTOCOL_SYMBOL_INTERFACE = 5;
    private static final int PROTOCOL_SYMBOL_RECORD = 6;
    private static final int PROTOCOL_SYMBOL_ENUM = 7;
    private static final int PROTOCOL_SYMBOL_ANNOTATION = 8;
    private static final int PROTOCOL_SYMBOL_METHOD = 9;
    private static final int PROTOCOL_SYMBOL_CONSTRUCTOR = 10;
    private static final int PROTOCOL_SYMBOL_FIELD = 11;
    private static final int PROTOCOL_REL_INHERITS = 2;
    private static final int PROTOCOL_REL_IMPLEMENTS = 3;
    private static final int PROTOCOL_REL_CALLS = 4;
    private static final int PROTOCOL_REL_REFERENCES = 5;
    private static final int PROTOCOL_REL_USES_ANNOTATION = 7;
    private static final int PROTOCOL_RESOLVED = 1;
    private static final int PROTOCOL_UNRESOLVED = 3;
    private static final int PROTOCOL_SOURCE = 1;
    private static final int PROTOCOL_EXTERNAL = 3;
    private static final int PROTOCOL_COMPILER_EVIDENCE = 2;

    @Override
    public boolean isAvailable() {
        return true;
    }

    @Override
    public JsonObject analyze(JsonObject payload) throws Exception {
        Objects.requireNonNull(payload, "payload");
        Path projectRoot = requiredPath(payload, "projectPath").toRealPath();
        String workspaceRootValue = optionalString(payload, "workspaceRoot", null);
        Path workspaceRoot = workspaceRootValue == null || workspaceRootValue.isBlank()
            ? projectRoot
            : Path.of(workspaceRootValue).toAbsolutePath().normalize().toRealPath();
        if (!projectRoot.startsWith(workspaceRoot)) {
            throw new IllegalArgumentException("The declared project module is outside the workspace boundary.");
        }
        List<Path> sourceRoots = readPaths(payload, "sourceRoots", true);
        List<Path> referencedSourceRoots = readPaths(payload, "referencedSourceRoots", true);
        List<Path> sourceFiles = readSourceFiles(payload);
        List<Path> classpath = readPaths(payload, "classpathEntries", false);

        if (sourceRoots.isEmpty()) {
            throw new IllegalArgumentException("At least one module-owned source root is required.");
        }
        for (Path root : sourceRoots) {
            Path realRoot = root.toRealPath();
            if (!realRoot.startsWith(projectRoot) || !Files.isDirectory(realRoot)) {
                throw new IllegalArgumentException("A source root is outside the declared project module.");
            }
        }
        List<Path> realSourceRoots = sourceRoots.stream()
            .map(path -> {
                try { return path.toRealPath(); }
                catch (IOException exception) {
                    throw new IllegalArgumentException("A declared source root cannot be resolved: " + path, exception);
                }
            }).toList();
        List<Path> environmentSourceRoots = new ArrayList<>(realSourceRoots);
        for (Path root : referencedSourceRoots) {
            Path realRoot = root.toRealPath();
            if (!realRoot.startsWith(workspaceRoot) || !Files.isDirectory(realRoot)) {
                throw new IllegalArgumentException("A referenced source root is outside the workspace boundary.");
            }
            if (!environmentSourceRoots.contains(realRoot)) {
                environmentSourceRoots.add(realRoot);
            }
        }
        for (Path source : sourceFiles) {
            Path realSource = source.toRealPath();
            if (!Files.isRegularFile(realSource)
                || realSourceRoots.stream().noneMatch(realSource::startsWith)) {
                throw new IllegalArgumentException("A source file is outside the declared source roots: " + source);
            }
        }
        for (Path entry : classpath) {
            if (!Files.exists(entry) || !(Files.isDirectory(entry) || Files.isRegularFile(entry))) {
                throw new IllegalArgumentException("A classpath entry does not exist: " + entry);
            }
        }

        List<Path> normalizedFiles = sourceFiles.stream()
            .map(path -> path.toAbsolutePath().normalize())
            .distinct()
            .sorted()
            .toList();
        if (normalizedFiles.isEmpty()) {
            return emptyResult(projectRoot, sourceFiles.size());
        }

        String[] sourcePaths = environmentSourceRoots.stream()
            .map(path -> path.toAbsolutePath().normalize().toString())
            .toArray(String[]::new);
        String[] encodings = Arrays.stream(sourcePaths).map(ignored -> "UTF-8").toArray(String[]::new);
        String[] classpathEntries = classpath.stream()
            .map(path -> path.toAbsolutePath().normalize().toString())
            .toArray(String[]::new);

        Map<String, String> options = JavaCore.getOptions();
        String sourceLevel = optionalString(payload, "sourceLevel", JavaCore.VERSION_21);
        JavaCore.setComplianceOptions(sourceLevel, options);
        options.put(JavaCore.COMPILER_SOURCE, sourceLevel);
        options.put(JavaCore.COMPILER_COMPLIANCE, sourceLevel);
        options.put(JavaCore.COMPILER_CODEGEN_TARGET_PLATFORM, sourceLevel);
        options.put(JavaCore.COMPILER_DOC_COMMENT_SUPPORT, JavaCore.ENABLED);

        AnalysisCollector collector = new AnalysisCollector(projectRoot, sourceRoots, payload);
        ASTParser parser = ASTParser.newParser(AST.getJLSLatest());
        parser.setKind(ASTParser.K_COMPILATION_UNIT);
        parser.setResolveBindings(true);
        parser.setBindingsRecovery(true);
        parser.setStatementsRecovery(true);
        parser.setCompilerOptions(options);
        parser.setEnvironment(classpathEntries, sourcePaths, encodings, true);
        parser.createASTs(
            normalizedFiles.stream().map(Path::toString).toArray(String[]::new),
            null,
            new String[0],
            new FileASTRequestor() {
                @Override
                public void acceptAST(String sourceFilePath, CompilationUnit ast) {
                    collector.accept(Path.of(sourceFilePath), ast);
                }
            },
            null);

        return collector.toJson(normalizedFiles.size());
    }

    private static JsonObject emptyResult(Path projectRoot, int skipped) {
        JsonObject result = new JsonObject();
        result.addProperty("engineId", ENGINE_ID);
        result.addProperty("engineVersion", ENGINE_VERSION);
        result.addProperty("projectPath", projectRoot.toString());
        result.addProperty("status", 3);
        result.addProperty("analyzedFileCount", 0);
        result.addProperty("skippedFileCount", skipped);
        result.add("symbols", new JsonArray());
        result.add("relationships", new JsonArray());
        result.add("diagnostics", new JsonArray());
        return result;
    }

    private static Path requiredPath(JsonObject payload, String name) {
        String value = optionalString(payload, name, null);
        if (value == null || value.isBlank()) {
            throw new IllegalArgumentException("The '" + name + "' path is required.");
        }
        return Path.of(value).toAbsolutePath().normalize();
    }

    private static List<Path> readSourceFiles(JsonObject payload) throws IOException {
        if (!payload.has("sourceManifestPath")) return readPaths(payload, "sourceFiles", false);
        Path manifest = Path.of(payload.get("sourceManifestPath").getAsString()).toRealPath();
        Path temporaryDirectory = Path.of(System.getProperty("java.io.tmpdir")).toRealPath();
        if (!manifest.startsWith(temporaryDirectory) || !Files.isRegularFile(manifest)
            || Files.size(manifest) > 32L * 1024 * 1024) {
            throw new IllegalArgumentException("The source manifest must be a regular file inside the system temporary directory and no larger than 32 MiB.");
        }
        var element = com.google.gson.JsonParser.parseString(Files.readString(manifest));
        if (!element.isJsonArray()) throw new IllegalArgumentException("The source manifest must contain a JSON array of paths.");
        List<Path> paths = new ArrayList<>();
        for (var value : element.getAsJsonArray()) {
            if (!value.isJsonPrimitive() || !value.getAsJsonPrimitive().isString())
                throw new IllegalArgumentException("Every source manifest entry must be a path string.");
            paths.add(Path.of(value.getAsString()).toAbsolutePath().normalize());
        }
        return List.copyOf(paths);
    }

    private static List<Path> readPaths(JsonObject payload, String name, boolean directories) {
        JsonArray values = payload.has(name) && payload.get(name).isJsonArray()
            ? payload.getAsJsonArray(name) : new JsonArray();
        List<Path> paths = new ArrayList<>();
        for (var value : values) {
            if (!value.isJsonPrimitive() || !value.getAsJsonPrimitive().isString()) {
                throw new IllegalArgumentException("Every '" + name + "' entry must be a path string.");
            }
            Path path = Path.of(value.getAsString()).toAbsolutePath().normalize();
            if (!Files.exists(path) || (directories && !Files.isDirectory(path))) {
                throw new IllegalArgumentException("The '" + name + "' entry does not exist: " + path);
            }
            paths.add(path);
        }
        return List.copyOf(paths);
    }

    private static String optionalString(JsonObject payload, String name, String fallback) {
        return payload.has(name) && payload.get(name).isJsonPrimitive()
            ? payload.get(name).getAsString() : fallback;
    }

    private static final class AnalysisCollector {
        private final Path projectRoot;
        private final List<Path> sourceRoots;
        private final boolean includeTestCode;
        private final boolean includeGeneratedCode;
        private final Set<Path> testSourceRoots;
        private final Set<Path> generatedSourceRoots;
        private final Map<String, JsonObject> symbols = new LinkedHashMap<>();
        private final Map<String, JsonObject> relationships = new LinkedHashMap<>();
        private final JsonArray diagnostics = new JsonArray();
        private int analyzedFiles;
        private int skippedFiles;

        private AnalysisCollector(Path projectRoot, List<Path> sourceRoots, JsonObject request) {
            this.projectRoot = projectRoot;
            this.sourceRoots = sourceRoots;
            this.includeTestCode = request.has("includeTestCode") && request.get("includeTestCode").getAsBoolean();
            this.includeGeneratedCode = request.has("includeGeneratedCode") && request.get("includeGeneratedCode").getAsBoolean();
            this.testSourceRoots = readPaths(request, "testSourceRoots", true).stream().map(path -> path.toAbsolutePath().normalize()).collect(Collectors.toUnmodifiableSet());
            this.generatedSourceRoots = readPaths(request, "generatedSourceRoots", true).stream().map(path -> path.toAbsolutePath().normalize()).collect(Collectors.toUnmodifiableSet());
        }

        private void accept(Path source, CompilationUnit unit) {
            Path file = source.toAbsolutePath().normalize();
            boolean test = isTestPath(file);
            boolean generated = isGeneratedPath(file);
            if ((test && !includeTestCode) || (generated && !includeGeneratedCode)) {
                skippedFiles++;
                return;
            }
            analyzedFiles++;
            unit.accept(new ASTVisitor() {
                @Override
                public void preVisit(ASTNode node) {
                    if (node instanceof Type type && !type.isPrimitiveType()) {
                        String sourceId = enclosingSymbol(type);
                        if (sourceId != null) {
                            addTypeRelationship(sourceId, type.resolveBinding(), type.toString(),
                                PROTOCOL_REL_REFERENCES, type, file, test);
                        }
                    }
                }

                @Override
                public boolean visit(TypeDeclaration node) {
                    ITypeBinding binding = node.resolveBinding();
                    String id = addType(binding, node, file, test, generated,
                        node.isInterface() ? PROTOCOL_SYMBOL_INTERFACE : PROTOCOL_SYMBOL_CLASS);
                    if (binding != null) {
                        if (node.getSuperclassType() != null)
                            addTypeRelationship(id, node.getSuperclassType().resolveBinding(),
                                node.getSuperclassType().toString(), PROTOCOL_REL_INHERITS, node, file, test);
                        for (Object item : node.superInterfaceTypes()) {
                            Type type = (Type) item;
                            addTypeRelationship(id, type.resolveBinding(), type.toString(),
                                node.isInterface() ? PROTOCOL_REL_INHERITS : PROTOCOL_REL_IMPLEMENTS,
                                type, file, test);
                        }
                    }
                    return true;
                }

                @Override
                public boolean visit(EnumDeclaration node) {
                    String id = addType(node.resolveBinding(), node, file, test, generated, PROTOCOL_SYMBOL_ENUM);
                    for (Object item : node.superInterfaceTypes()) {
                        Type type = (Type) item;
                        addTypeRelationship(id, type.resolveBinding(), type.toString(),
                            PROTOCOL_REL_IMPLEMENTS, type, file, test);
                    }
                    return true;
                }

                @Override
                public boolean visit(AnnotationTypeDeclaration node) {
                    addType(node.resolveBinding(), node, file, test, generated, PROTOCOL_SYMBOL_ANNOTATION);
                    return true;
                }

                @Override
                public boolean visit(RecordDeclaration node) {
                    String id = addType(node.resolveBinding(), node, file, test, generated, PROTOCOL_SYMBOL_RECORD);
                    for (Object item : node.superInterfaceTypes()) {
                        Type type = (Type) item;
                        addTypeRelationship(id, type.resolveBinding(), type.toString(),
                            PROTOCOL_REL_IMPLEMENTS, type, file, test);
                    }
                    return true;
                }

                @Override
                public boolean visit(MethodDeclaration node) {
                    IMethodBinding binding = node.resolveBinding();
                    addMethod(binding, node, file, test, generated);
                    return true;
                }

                @Override
                public boolean visit(FieldDeclaration node) {
                    ITypeBinding owner = enclosingType(node);
                    for (Object item : node.fragments()) {
                        VariableDeclarationFragment fragment = (VariableDeclarationFragment) item;
                        IVariableBinding binding = fragment.resolveBinding();
                        if (binding != null) addVariable(binding, fragment, file, test, generated, owner);
                    }
                    return true;
                }

                @Override
                public boolean visit(MethodInvocation node) {
                    IMethodBinding binding = node.resolveMethodBinding();
                    addCall(enclosingSymbol(node), binding, node.toString(), node, file, test, false);
                    return true;
                }

                @Override
                public boolean visit(SuperMethodInvocation node) {
                    addCall(enclosingSymbol(node), node.resolveMethodBinding(), node.toString(), node, file, test, false);
                    return true;
                }

                @Override
                public boolean visit(ClassInstanceCreation node) {
                    addCall(enclosingSymbol(node), node.resolveConstructorBinding(), node.toString(), node, file, test, true);
                    return true;
                }

                @Override
                public boolean visit(ConstructorInvocation node) {
                    addCall(enclosingSymbol(node), node.resolveConstructorBinding(), node.toString(), node, file, test, true);
                    return true;
                }

                @Override
                public boolean visit(SuperConstructorInvocation node) {
                    addCall(enclosingSymbol(node), node.resolveConstructorBinding(), node.toString(), node, file, test, true);
                    return true;
                }

                @Override
                public boolean visit(CreationReference node) {
                    addCall(enclosingSymbol(node), node.resolveMethodBinding(), node.toString(), node, file, test, true);
                    return true;
                }

                @Override
                public boolean visit(ExpressionMethodReference node) {
                    addCall(enclosingSymbol(node), node.resolveMethodBinding(), node.toString(), node, file, test, false);
                    return true;
                }

                @Override
                public boolean visit(TypeMethodReference node) {
                    addCall(enclosingSymbol(node), node.resolveMethodBinding(), node.toString(), node, file, test, false);
                    return true;
                }

                @Override
                public boolean visit(SuperMethodReference node) {
                    addCall(enclosingSymbol(node), node.resolveMethodBinding(), node.toString(), node, file, test, false);
                    return true;
                }

                @Override
                public boolean visit(MarkerAnnotation node) {
                    addAnnotation(enclosingSymbol(node), node.resolveTypeBinding(), node.toString(), node, file, test);
                    return true;
                }

                @Override
                public boolean visit(NormalAnnotation node) {
                    addAnnotation(enclosingSymbol(node), node.resolveTypeBinding(), node.toString(), node, file, test);
                    return true;
                }

                @Override
                public boolean visit(SingleMemberAnnotation node) {
                    addAnnotation(enclosingSymbol(node), node.resolveTypeBinding(), node.toString(), node, file, test);
                    return true;
                }
            });
            for (IProblem problem : unit.getProblems()) {
                JsonObject diagnostic = new JsonObject();
                diagnostic.addProperty("code", "JDT-" + problem.getID());
                diagnostic.addProperty("component", "EclipseJDT");
                diagnostic.addProperty("message", problem.getMessage());
                diagnostic.addProperty("target", file.toString());
                diagnostic.addProperty("severity", problem.isError() ? "Error" : problem.isWarning() ? "Warning" : "Info");
                diagnostic.addProperty("line", problem.getSourceLineNumber());
                diagnostic.addProperty("affectsCoverage", problem.isError());
                diagnostics.add(diagnostic);
            }
        }

        private String addType(ITypeBinding binding, ASTNode node, Path file, boolean test, boolean generated, int kind) {
            String id = binding == null ? fallbackId(file, node, "type") : bindingId(binding);
            String name = node instanceof AbstractTypeDeclaration declaration
                ? declaration.getName().getIdentifier()
                : binding == null ? node.getClass().getSimpleName() : binding.getName();
            String qualified = binding == null
                ? sourceQualifiedTypeName(node, name)
                : binding.getTypeDeclaration().getQualifiedName();
            if (qualified == null || qualified.isBlank()) {
                qualified = sourceQualifiedTypeName(node, name);
            }

            // The declaration belongs to the source file currently being visited.
            // Binding recovery affects semantic confidence, not source ownership.
            addSymbol(id, name, qualified, kind,
                binding != null && org.eclipse.jdt.core.dom.Modifier.isPublic(binding.getModifiers()),
                true, file, node, test, generated);
            return id;
        }

        private void addMethod(IMethodBinding binding, MethodDeclaration node, Path file, boolean test, boolean generated) {
            if (binding == null) return;
            IMethodBinding declaration = binding.getMethodDeclaration();
            String id = bindingId(declaration);
            String name = declaration.isConstructor() ? "<init>" : declaration.getName();
            String qualified = methodIdentity(declaration);
            addSymbol(id, name, qualified,
                declaration.isConstructor() ? PROTOCOL_SYMBOL_CONSTRUCTOR : PROTOCOL_SYMBOL_METHOD,
                org.eclipse.jdt.core.dom.Modifier.isPublic(declaration.getModifiers()),
                true, file, node, test, generated);
        }

        private void addVariable(IVariableBinding binding, ASTNode node, Path file, boolean test, boolean generated, ITypeBinding owner) {
            String id = binding.getVariableDeclaration().getKey();
            if (id == null || id.isBlank()) id = fallbackId(file, node, "field");
            String qualified = owner == null ? binding.getName() : owner.getTypeDeclaration().getQualifiedName() + "#" + binding.getName();
            addSymbol(id, binding.getName(), qualified, PROTOCOL_SYMBOL_FIELD,
                org.eclipse.jdt.core.dom.Modifier.isPublic(binding.getModifiers()),
                true, file, node, test, generated);
        }

        private void addTypeRelationship(String sourceId, ITypeBinding target, String unresolved,
                                         int kind, ASTNode node, Path file, boolean test) {
            if (sourceId == null) return;
            ITypeBinding declaration = target == null ? null : target.getTypeDeclaration();
            boolean resolved = target != null
                && !target.isRecovered()
                && declaration != null
                && !declaration.isRecovered();
            String targetId = resolved ? bindingId(declaration) : null;
            if (resolved) addExternalTypeIfNeeded(declaration, file, node, test);
            addRelationship(
                sourceId,
                targetId,
                resolved ? null : unresolvedTarget(target, unresolved),
                kind,
                resolved ? PROTOCOL_RESOLVED : PROTOCOL_UNRESOLVED,
                node,
                file,
                test);
        }

        private void addCall(String sourceId, IMethodBinding target, String unresolved,
                             ASTNode node, Path file, boolean test, boolean constructor) {
            if (sourceId == null) return;
            IMethodBinding declaration = target == null ? null : target.getMethodDeclaration();
            ITypeBinding declaringClass = declaration == null ? null : declaration.getDeclaringClass();
            boolean resolved = target != null
                && !target.isRecovered()
                && declaration != null
                && !declaration.isRecovered()
                && (declaringClass == null || !declaringClass.isRecovered());
            if (!resolved) {
                addRelationship(
                    sourceId,
                    null,
                    unresolvedTarget(target, unresolved),
                    PROTOCOL_REL_CALLS,
                    PROTOCOL_UNRESOLVED,
                    node,
                    file,
                    test);
                return;
            }

            String targetId = bindingId(declaration);
            if (declaringClass != null) {
                addExternalTypeIfNeeded(declaringClass, file, node, test);
                if (!declaringClass.isFromSource()) {
                    addSymbol(targetId, declaration.isConstructor() ? "<init>" : declaration.getName(),
                        methodIdentity(declaration),
                        declaration.isConstructor() ? PROTOCOL_SYMBOL_CONSTRUCTOR : PROTOCOL_SYMBOL_METHOD,
                        org.eclipse.jdt.core.dom.Modifier.isPublic(declaration.getModifiers()),
                        false, file, node, test, false);
                }
            }
            addRelationship(sourceId, targetId, null, PROTOCOL_REL_CALLS, PROTOCOL_RESOLVED, node, file, test);
        }

        private void addAnnotation(String sourceId, ITypeBinding target, String unresolved,
                                   ASTNode node, Path file, boolean test) {
            if (sourceId == null) return;
            ITypeBinding declaration = target == null ? null : target.getTypeDeclaration();
            boolean resolved = target != null
                && !target.isRecovered()
                && declaration != null
                && !declaration.isRecovered();
            String targetId = resolved ? bindingId(declaration) : null;
            if (resolved) addExternalTypeIfNeeded(declaration, file, node, test);
            addRelationship(
                sourceId,
                targetId,
                resolved ? null : unresolvedTarget(target, unresolved),
                PROTOCOL_REL_USES_ANNOTATION,
                resolved ? PROTOCOL_RESOLVED : PROTOCOL_UNRESOLVED,
                node,
                file,
                test);
        }

        private void addExternalTypeIfNeeded(ITypeBinding binding, Path file, ASTNode node, boolean test) {
            ITypeBinding declaration = binding.getTypeDeclaration();
            String id = bindingId(declaration);
            if (declaration.isRecovered() || declaration.isFromSource() || symbols.containsKey(id)) return;
            addSymbol(id, declaration.getName(), declaration.getQualifiedName(),
                declaration.isAnnotation() ? PROTOCOL_SYMBOL_ANNOTATION :
                    declaration.isEnum() ? PROTOCOL_SYMBOL_ENUM :
                    declaration.isInterface() ? PROTOCOL_SYMBOL_INTERFACE :
                    declaration.isRecord() ? PROTOCOL_SYMBOL_RECORD : PROTOCOL_SYMBOL_CLASS,
                org.eclipse.jdt.core.dom.Modifier.isPublic(declaration.getModifiers()),
                false, file, node, test, false);
        }

        private static String unresolvedTarget(ITypeBinding target, String fallback) {
            String qualifiedName = target == null ? null : target.getQualifiedName();
            return qualifiedName == null || qualifiedName.isBlank() ? fallback : qualifiedName;
        }

        private static String unresolvedTarget(IMethodBinding target, String fallback) {
            if (target == null) return fallback;
            ITypeBinding declaringClass = target.getDeclaringClass();
            String qualifiedType = declaringClass == null ? null : declaringClass.getQualifiedName();
            String methodName = target.getName();
            return qualifiedType == null || qualifiedType.isBlank() || methodName == null || methodName.isBlank()
                ? fallback
                : qualifiedType + "." + methodName;
        }

        private void addSymbol(String id, String name, String qualified, int kind, boolean isPublic,
                               boolean fromSource, Path file, ASTNode node, boolean test, boolean generated) {
            if (id == null || id.isBlank() || symbols.containsKey(id)) return;
            JsonObject symbol = new JsonObject();
            symbol.addProperty("id", id);
            symbol.addProperty("kind", kind);
            symbol.addProperty("name", name == null ? "" : name);
            symbol.addProperty("qualifiedName", qualified == null ? "" : qualified);
            if (fromSource) {
                symbol.addProperty("containerName", packageName(node));
                symbol.addProperty("containerKind", "Package");
            }
            symbol.addProperty("language", "Java");
            symbol.addProperty("projectPath", projectRoot.toString());
            symbol.addProperty("filePath", fromSource ? file.toString() : null);
            symbol.addProperty("startLine", line(node));
            symbol.addProperty("startColumn", column(node));
            symbol.addProperty("containingSymbolId", fromSource ? enclosingSymbol(node) : null);
            symbol.addProperty("origin", fromSource ? (generated ? 2 : PROTOCOL_SOURCE) : PROTOCOL_EXTERNAL);
            symbol.addProperty("isTestCode", test);
            symbol.addProperty("isPublic", isPublic);
            JsonArray provenance = new JsonArray();
            provenance.add(provenance(file, node));
            symbol.add("provenance", provenance);
            symbols.put(id, symbol);
        }

        private static String sourceQualifiedTypeName(ASTNode node, String name) {
            List<String> enclosingTypes = new ArrayList<>();
            ASTNode current = node;
            while (current != null) {
                if (current instanceof AbstractTypeDeclaration declaration) {
                    enclosingTypes.add(0, declaration.getName().getIdentifier());
                }
                current = current.getParent();
            }

            String typeName = enclosingTypes.isEmpty() ? name : String.join(".", enclosingTypes);
            String packageName = packageName(node);
            return packageName.isBlank() ? typeName : packageName + "." + typeName;
        }

        private static String packageName(ASTNode node) {
            ASTNode root = node.getRoot();
            if (!(root instanceof CompilationUnit unit) || unit.getPackage() == null) return "";
            return unit.getPackage().getName().getFullyQualifiedName();
        }

        private void addRelationship(String sourceId, String targetId, String unresolved, int kind,
                                     int status, ASTNode node, Path file, boolean test) {
            String id = sourceId + "|" + kind + "|" + (targetId == null ? unresolved : targetId)
                + "|" + file + "|" + node.getStartPosition();
            if (relationships.containsKey(id)) return;
            JsonObject relationship = new JsonObject();
            relationship.addProperty("id", id);
            relationship.addProperty("sourceSymbolId", sourceId);
            relationship.addProperty("targetSymbolId", targetId);
            relationship.addProperty("unresolvedTarget", targetId == null ? unresolved : null);
            relationship.addProperty("kind", kind);
            relationship.addProperty("resolutionStatus", status);
            relationship.addProperty("isTestCode", test);
            JsonArray provenance = new JsonArray();
            provenance.add(provenance(file, node));
            relationship.add("provenance", provenance);
            relationships.put(id, relationship);
        }

        private JsonObject provenance(Path file, ASTNode node) {
            JsonObject value = new JsonObject();
            value.addProperty("analyzerId", ENGINE_ID);
            value.addProperty("source", PROTOCOL_COMPILER_EVIDENCE);
            value.addProperty("filePath", file.toString());
            value.addProperty("startLine", line(node));
            value.addProperty("startColumn", column(node));
            value.addProperty("confidence", 1.0);
            return value;
        }

        private JsonObject toJson(int requestedFiles) {
            JsonObject result = new JsonObject();
            result.addProperty("engineId", ENGINE_ID);
            result.addProperty("engineVersion", ENGINE_VERSION);
            result.addProperty("projectPath", projectRoot.toString());
            boolean hasCoverageErrors = false;
            for (var item : diagnostics) {
                if (item.getAsJsonObject().has("affectsCoverage")
                    && item.getAsJsonObject().get("affectsCoverage").getAsBoolean()) {
                    hasCoverageErrors = true;
                    break;
                }
            }
            result.addProperty("status", skippedFiles == 0 && !hasCoverageErrors ? 2 : 3);
            result.addProperty("analyzedFileCount", analyzedFiles);
            result.addProperty("skippedFileCount", skippedFiles + Math.max(0, requestedFiles - analyzedFiles - skippedFiles));
            JsonArray symbolArray = new JsonArray();
            symbols.values().forEach(symbolArray::add);
            JsonArray relationshipArray = new JsonArray();
            relationships.values().forEach(relationshipArray::add);
            result.add("symbols", symbolArray);
            result.add("relationships", relationshipArray);
            result.add("diagnostics", diagnostics);
            return result;
        }

        private String enclosingSymbol(ASTNode node) {
            ASTNode current = node.getParent();
            while (current != null) {
                if (current instanceof MethodDeclaration method && method.resolveBinding() != null)
                    return bindingId(method.resolveBinding().getMethodDeclaration());
                if (current instanceof AbstractTypeDeclaration type) {
                    ITypeBinding binding = resolveType(type);
                    if (binding != null) return bindingId(binding.getTypeDeclaration());
                }
                current = current.getParent();
            }
            return null;
        }

        private ASTNode enclosingSymbolNode(ASTNode node) {
            ASTNode current = node.getParent();
            while (current != null) {
                if (current instanceof MethodDeclaration || current instanceof AbstractTypeDeclaration) return current;
                current = current.getParent();
            }
            return null;
        }

        private ITypeBinding enclosingType(ASTNode node) {
            ASTNode current = node.getParent();
            while (current != null) {
                if (current instanceof AbstractTypeDeclaration type) return resolveType(type);
                current = current.getParent();
            }
            return null;
        }

        private boolean isTestPath(Path file) {
            Path normalized = file.toAbsolutePath().normalize();
            if (testSourceRoots.stream().anyMatch(normalized::startsWith)) return true;
            String value = file.toString().replace('\\', '/').toLowerCase(Locale.ROOT);
            return value.contains("/src/test/") || value.contains("/src/it/")
                || value.contains("/test/") || value.contains("/tests/");
        }

        private boolean isGeneratedPath(Path file) {
            Path normalized = file.toAbsolutePath().normalize();
            if (generatedSourceRoots.stream().anyMatch(normalized::startsWith)) return true;
            String value = file.toString().replace('\\', '/').toLowerCase(Locale.ROOT);
            return value.contains("/generated/") || value.contains("/generated-sources/")
                || value.contains("/build/generated/") || value.contains("/target/generated/");
        }

        private static ITypeBinding resolveType(AbstractTypeDeclaration type) {
            if (type instanceof TypeDeclaration value) return value.resolveBinding();
            if (type instanceof EnumDeclaration value) return value.resolveBinding();
            if (type instanceof AnnotationTypeDeclaration value) return value.resolveBinding();
            if (type instanceof RecordDeclaration value) return value.resolveBinding();
            return null;
        }

        private static String bindingId(IBinding binding) {
            String key = binding.getKey();
            if (key != null && !key.isBlank()) return key;
            if (binding instanceof ITypeBinding type) return type.getTypeDeclaration().getQualifiedName();
            if (binding instanceof IMethodBinding method) return methodIdentity(method);
            return binding.getName();
        }

        private static String methodIdentity(IMethodBinding binding) {
            String owner = binding.getDeclaringClass() == null ? "" : binding.getDeclaringClass().getTypeDeclaration().getQualifiedName();
            String parameters = Arrays.stream(binding.getParameterTypes())
                .map(type -> type.getErasure().getQualifiedName())
                .collect(Collectors.joining(","));
            return owner + "#" + (binding.isConstructor() ? "<init>" : binding.getName()) + "(" + parameters + ")";
        }

        private static String fallbackId(Path file, ASTNode node, String kind) {
            return file + "#" + kind + "@" + node.getStartPosition();
        }

        private static int line(ASTNode node) {
            ASTNode root = node.getRoot();
            return root instanceof CompilationUnit unit ? unit.getLineNumber(node.getStartPosition()) : -1;
        }

        private static int column(ASTNode node) {
            ASTNode root = node.getRoot();
            return root instanceof CompilationUnit unit ? unit.getColumnNumber(node.getStartPosition()) : -1;
        }
    }
}
