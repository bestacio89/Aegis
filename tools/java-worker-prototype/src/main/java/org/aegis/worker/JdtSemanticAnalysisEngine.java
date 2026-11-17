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
        result.add("serverApiEndpoints", new JsonArray());
        result.add("apiClientRequests", new JsonArray());
        result.add("messageIntegrations", new JsonArray());
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
        private final JsonArray serverApiEndpoints = new JsonArray();
        private final JsonArray apiClientRequests = new JsonArray();
        private final JsonArray messageIntegrations = new JsonArray();
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
                    discoverSpringEndpoint(node, file);
                    discoverSpringKafkaListener(node, file);
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
                    discoverApiClientRequest(node, file);
                    discoverSpringKafkaSend(node, file);
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



        private void discoverSpringKafkaListener(MethodDeclaration method, Path file) {
            List<Annotation> listeners = new ArrayList<>();
            for (Object modifier : method.modifiers()) {
                if (modifier instanceof Annotation annotation
                    && "org.springframework.kafka.annotation.KafkaListener".equals(annotationTypeName(annotation))) {
                    listeners.add(annotation);
                }
            }
            if (listeners.isEmpty()) return;

            IMethodBinding handlerBinding = method.resolveBinding();
            String handlerIdentity = handlerBinding == null || handlerBinding.isRecovered()
                ? null : methodIdentity(handlerBinding.getMethodDeclaration());
            String messageType = handlerBinding == null || handlerBinding.isRecovered()
                ? null : listenerPayloadType(handlerBinding);
            for (Annotation listener : listeners) {
                for (String member : List.of("topics", "topicPattern")) {
                    Expression expression = annotationMember(listener, member);
                    if (expression == null) continue;
                    for (Expression value : annotationExpressions(expression)) {
                        addKafkaListenerEvidence(method, file, listener, value, handlerIdentity,
                            messageType, "topicPattern".equals(member));
                    }
                }
                Expression partitions = annotationMember(listener, "topicPartitions");
                if (partitions != null) {
                    for (Expression value : annotationExpressions(partitions)) {
                        if (!(value instanceof Annotation partition)
                            || !"org.springframework.kafka.annotation.TopicPartition".equals(annotationTypeName(partition))) {
                            continue;
                        }
                        Expression topic = annotationMember(partition, "topic");
                        if (topic != null) {
                            addKafkaListenerEvidence(method, file, listener, topic, handlerIdentity,
                                messageType, false);
                        }
                    }
                }
            }
        }

        private void addKafkaListenerEvidence(
            MethodDeclaration method,
            Path file,
            Annotation listener,
            Expression destination,
            String handlerIdentity,
            String messageType,
            boolean pattern) {
            Object constant = destination.resolveConstantExpressionValue();
            String destinationIdentity = !pattern && constant instanceof String value
                && !value.contains("${") && !value.contains("#{") ? value : null;
            int resolutionStatus = destinationIdentity != null ? 0 : 2;
            if (handlerIdentity == null) resolutionStatus = 3;

            JsonObject evidence = new JsonObject();
            evidence.addProperty("evidenceId", file + "#kafka-listener@" + listener.getStartPosition()
                + "#destination@" + destination.getStartPosition());
            evidence.addProperty("language", "Java");
            evidence.addProperty("framework", "Spring Kafka");
            evidence.addProperty("providerId", "eclipse-jdt-spring-kafka");
            evidence.addProperty("modulePath", projectRoot.toString());
            evidence.addProperty("sourceFile", file.toString());
            evidence.addProperty("startLine", line(method));
            evidence.addProperty("startColumn", column(method));
            evidence.addProperty("operation", 1);
            if (messageType != null) evidence.addProperty("messageTypeIdentity", messageType);
            evidence.addProperty("messageTypeExpression", method.parameters().isEmpty()
                ? "" : method.parameters().get(0).toString());
            evidence.addProperty("destinationKind", 1);
            if (destinationIdentity != null) evidence.addProperty("destinationIdentity", destinationIdentity);
            evidence.addProperty("destinationExpression", destination.toString());
            if (handlerIdentity != null) evidence.addProperty("handlerIdentity", handlerIdentity);
            evidence.addProperty("resolutionStatus", resolutionStatus);
            JsonArray resolutionEvidence = new JsonArray();
            resolutionEvidence.add("Resolved annotation binding: org.springframework.kafka.annotation.KafkaListener");
            resolutionEvidence.add(pattern
                ? "topicPattern is retained as a pattern expression; it is not reduced to a single destination."
                : destinationIdentity != null
                    ? "Destination expression resolved to a compile-time string constant."
                    : "Destination expression is retained without runtime evaluation.");
            if (handlerIdentity == null) resolutionEvidence.add("Listener handler method binding is unavailable or recovered.");
            evidence.add("resolutionEvidence", resolutionEvidence);
            evidence.addProperty("declarationEvidence", listener.toString() + " " + method.getName());
            messageIntegrations.add(evidence);
        }

        private void discoverSpringKafkaSend(MethodInvocation invocation, Path file) {
            IMethodBinding binding = invocation.resolveMethodBinding();
            if (binding == null || binding.isRecovered() || !"send".equals(binding.getName())) return;
            ITypeBinding declaringType = binding.getMethodDeclaration().getDeclaringClass();
            if (declaringType == null || declaringType.isRecovered()
                || !"org.springframework.kafka.core.KafkaTemplate".equals(
                    declaringType.getTypeDeclaration().getQualifiedName())) return;

            ITypeBinding[] parameterTypes = binding.getParameterTypes();
            if ((parameterTypes.length != 2 && parameterTypes.length != 3)
                || !"java.lang.String".equals(parameterTypes[0].getErasure().getQualifiedName())
                || invocation.arguments().size() != parameterTypes.length) return;

            Expression destination = argument(invocation, 0);
            Expression payload = argument(invocation, parameterTypes.length - 1);
            if (destination == null || payload == null) return;

            Object constant = destination.resolveConstantExpressionValue();
            String destinationIdentity = constant instanceof String value
                && !value.contains("${") && !value.contains("#{") ? value : null;
            ITypeBinding payloadBinding = payload.resolveTypeBinding();
            String messageType = payloadBinding == null || payloadBinding.isRecovered()
                ? null : payloadBinding.getTypeDeclaration().getQualifiedName();
            ASTNode callerNode = enclosingSymbolNode(invocation);
            String callerIdentity = callerNode instanceof MethodDeclaration caller
                && caller.resolveBinding() != null
                ? methodIdentity(caller.resolveBinding().getMethodDeclaration())
                : null;
            int resolutionStatus = destinationIdentity != null && messageType != null ? 0 : 2;

            JsonObject evidence = new JsonObject();
            evidence.addProperty("evidenceId", file + "#kafka-send@" + invocation.getStartPosition());
            evidence.addProperty("language", "Java");
            evidence.addProperty("framework", "Spring Kafka");
            evidence.addProperty("providerId", "eclipse-jdt-spring-kafka");
            evidence.addProperty("modulePath", projectRoot.toString());
            evidence.addProperty("sourceFile", file.toString());
            evidence.addProperty("startLine", line(invocation));
            evidence.addProperty("startColumn", column(invocation));
            evidence.addProperty("operation", 2);
            if (messageType != null) evidence.addProperty("messageTypeIdentity", messageType);
            evidence.addProperty("messageTypeExpression", payload.toString());
            evidence.addProperty("destinationKind", 1);
            if (destinationIdentity != null) evidence.addProperty("destinationIdentity", destinationIdentity);
            evidence.addProperty("destinationExpression", destination.toString());
            if (callerIdentity != null) evidence.addProperty("handlerIdentity", callerIdentity);
            evidence.addProperty("resolutionStatus", resolutionStatus);
            JsonArray resolutionEvidence = new JsonArray();
            resolutionEvidence.add("Resolved invocation binding: org.springframework.kafka.core.KafkaTemplate#send");
            resolutionEvidence.add(destinationIdentity != null
                ? "Destination expression resolved to a compile-time string constant."
                : "Destination expression is retained without runtime evaluation.");
            if (messageType == null) resolutionEvidence.add("Payload expression type binding is unavailable or recovered.");
            evidence.add("resolutionEvidence", resolutionEvidence);
            evidence.addProperty("declarationEvidence", invocation.toString());
            messageIntegrations.add(evidence);
        }

        private static List<Expression> annotationExpressions(Expression expression) {
            if (expression instanceof ArrayInitializer array) {
                List<Expression> values = new ArrayList<>();
                for (Object item : array.expressions()) values.add((Expression) item);
                return values;
            }
            return List.of(expression);
        }

        private static String listenerPayloadType(IMethodBinding handler) {
            ITypeBinding[] parameters = handler.getParameterTypes();
            if (parameters.length == 0) return null;
            ITypeBinding type = parameters[0];
            String erasedName = type.getErasure().getQualifiedName();
            ITypeBinding[] arguments = type.getTypeArguments();
            if ("org.apache.kafka.clients.consumer.ConsumerRecord".equals(erasedName)
                && arguments.length >= 2) return arguments[1].getTypeDeclaration().getQualifiedName();
            if ("org.springframework.messaging.Message".equals(erasedName)
                && arguments.length >= 1) return arguments[0].getTypeDeclaration().getQualifiedName();
            if (("java.util.List".equals(erasedName) || "java.util.Collection".equals(erasedName))
                && arguments.length >= 1) {
                ITypeBinding element = arguments[0];
                ITypeBinding[] elementArguments = element.getTypeArguments();
                if ("org.apache.kafka.clients.consumer.ConsumerRecord".equals(element.getErasure().getQualifiedName())
                    && elementArguments.length >= 2) return elementArguments[1].getTypeDeclaration().getQualifiedName();
                return element.getTypeDeclaration().getQualifiedName();
            }
            return type.getTypeDeclaration().getQualifiedName();
        }

        private void discoverApiClientRequest(MethodInvocation invocation, Path file) {
            IMethodBinding binding = invocation.resolveMethodBinding();
            if (binding == null || binding.isRecovered()) return;
            ITypeBinding declaringType = binding.getDeclaringClass();
            if (declaringType == null || declaringType.isRecovered()) return;

            String owner = declaringType.getTypeDeclaration().getQualifiedName();
            String name = binding.getName();
            String framework;
            String httpMethod = null;
            Expression uriExpression = null;

            if ("org.springframework.web.client.RestTemplate".equals(owner)) {
                framework = "Spring RestTemplate";
                switch (name) {
                    case "getForObject", "getForEntity" -> { httpMethod = "GET"; uriExpression = argument(invocation, 0); }
                    case "postForObject", "postForEntity", "postForLocation" -> { httpMethod = "POST"; uriExpression = argument(invocation, 0); }
                    case "put" -> { httpMethod = "PUT"; uriExpression = argument(invocation, 0); }
                    case "delete" -> { httpMethod = "DELETE"; uriExpression = argument(invocation, 0); }
                    case "patchForObject" -> { httpMethod = "PATCH"; uriExpression = argument(invocation, 0); }
                    case "exchange", "execute" -> {
                        uriExpression = argument(invocation, 0);
                        Expression methodArgument = name.equals("exchange") ? argument(invocation, 1) : null;
                        httpMethod = enumConstant(methodArgument);
                    }
                    default -> { return; }
                }
            } else if ("org.springframework.web.reactive.function.client.WebClient".equals(owner)) {
                framework = "Spring WebClient";
                httpMethod = switch (name) {
                    case "get" -> "GET";
                    case "post" -> "POST";
                    case "put" -> "PUT";
                    case "patch" -> "PATCH";
                    case "delete" -> "DELETE";
                    default -> null;
                };
                if (httpMethod == null) return;
                MethodInvocation uriCall = enclosingUriCall(invocation);
                uriExpression = uriCall == null ? null : argument(uriCall, 0);
            } else {
                return;
            }

            ASTNode caller = enclosingSymbolNode(invocation);
            IMethodBinding callerBinding = caller instanceof MethodDeclaration method
                ? method.resolveBinding() : null;
            String callerIdentity = callerBinding == null
                ? (caller == null ? "" : caller.toString())
                : methodIdentity(callerBinding.getMethodDeclaration());
            String uriText = uriExpression == null ? null : uriExpression.toString();
            boolean literalUri = uriExpression instanceof StringLiteral
                || uriExpression instanceof TextBlock;
            int resolutionStatus = uriText == null ? 2 : literalUri ? 0 : 2;

            JsonObject evidence = new JsonObject();
            evidence.addProperty("evidenceId", file + "#client#" + invocation.getStartPosition());
            evidence.addProperty("language", "Java");
            evidence.addProperty("framework", framework);
            evidence.addProperty("providerId", "eclipse-jdt-api-client");
            evidence.addProperty("modulePath", projectRoot.toString());
            evidence.addProperty("sourceFile", file.toString());
            evidence.addProperty("startLine", line(invocation));
            evidence.addProperty("startColumn", column(invocation));
            evidence.addProperty("callerIdentity", callerIdentity);
            if (httpMethod != null) evidence.addProperty("httpMethod", httpMethod);
            if (uriText != null) evidence.addProperty("requestUriExpression", uriText);
            evidence.addProperty("resolutionStatus", resolutionStatus);
            JsonArray resolutionEvidence = new JsonArray();
            resolutionEvidence.add("Resolved invocation binding: " + owner + "#" + name);
            if (uriExpression == null) resolutionEvidence.add("No URI argument was statically associated with the client invocation.");
            else if (!literalUri) resolutionEvidence.add("The request URI is an expression and is not reduced to a runtime value.");
            evidence.add("resolutionEvidence", resolutionEvidence);
            evidence.addProperty("declarationEvidence", invocation.toString());
            apiClientRequests.add(evidence);
        }

        private static Expression argument(MethodInvocation invocation, int index) {
            return invocation.arguments().size() > index
                ? (Expression) invocation.arguments().get(index) : null;
        }

        private static String enumConstant(Expression expression) {
            if (!(expression instanceof Name name)) return null;
            IBinding binding = name.resolveBinding();
            if (binding instanceof IVariableBinding variable && variable.isEnumConstant())
                return variable.getName();
            return null;
        }

        private static MethodInvocation enclosingUriCall(MethodInvocation invocation) {
            ASTNode current = invocation.getParent();
            while (current instanceof MethodInvocation chained) {
                IMethodBinding binding = chained.resolveMethodBinding();
                ITypeBinding owner = binding == null ? null : binding.getDeclaringClass();
                if (owner != null && owner.getTypeDeclaration().getQualifiedName().endsWith(".WebClient.UriSpec")
                    && "uri".equals(binding.getName())) return chained;
                if (chained.getExpression() == invocation) {
                    current = chained.getParent();
                    continue;
                }
                break;
            }
            return null;
        }

        private void discoverSpringEndpoint(MethodDeclaration method, Path file) {
            List<Annotation> methodMappings = mappingAnnotations(method.modifiers());
            if (methodMappings.isEmpty()) return;

            ASTNode parent = method.getParent();
            while (parent != null && !(parent instanceof AbstractTypeDeclaration)) parent = parent.getParent();
            List<Annotation> classMappings = parent instanceof AbstractTypeDeclaration type
                ? mappingAnnotations(type.modifiers()) : List.of();

            List<RouteValue> classRoutes = classMappings.isEmpty()
                ? List.of(new RouteValue("", false))
                : classMappings.stream().flatMap(annotation -> routeValues(annotation).stream()).toList();
            List<RouteValue> methodRoutes = methodMappings.stream()
                .flatMap(annotation -> routeValues(annotation).stream()).toList();

            IMethodBinding binding = method.resolveBinding();
            String handler = binding == null
                ? (parent instanceof AbstractTypeDeclaration type ? type.getName().getIdentifier() : "")
                    + "#" + method.getName().getIdentifier()
                : methodIdentity(binding.getMethodDeclaration());
            List<String> methods = methodMappings.stream()
                .flatMap(annotation -> httpMethods(annotation).stream())
                .distinct().sorted().toList();

            int routeIndex = 0;
            for (RouteValue classRoute : classRoutes) {
                for (RouteValue methodRoute : methodRoutes) {
                    JsonObject endpoint = new JsonObject();
                    endpoint.addProperty("evidenceId", file + "#" + method.getStartPosition()
                        + "#" + handler + "#" + routeIndex++);
                    endpoint.addProperty("language", "Java");
                    endpoint.addProperty("framework", "Spring MVC/WebFlux");
                    endpoint.addProperty("providerId", "eclipse-jdt-spring");
                    endpoint.addProperty("modulePath", projectRoot.toString());
                    endpoint.addProperty("sourceFile", file.toString());
                    endpoint.addProperty("startLine", line(method));
                    endpoint.addProperty("startColumn", column(method));
                    endpoint.addProperty("handlerIdentity", handler);
                    JsonArray httpMethods = new JsonArray();
                    methods.forEach(httpMethods::add);
                    endpoint.add("httpMethods", httpMethods);
                    endpoint.addProperty("routeTemplate", combineRoutes(classRoute.value, methodRoute.value));
                    endpoint.addProperty("resolutionStatus", classRoute.dynamic || methodRoute.dynamic ? 3 : 1);
                    JsonArray composition = new JsonArray();
                    classMappings.forEach(annotation -> composition.add(annotation.toString()));
                    methodMappings.forEach(annotation -> composition.add(annotation.toString()));
                    endpoint.add("routeCompositionEvidence", composition);
                    endpoint.addProperty("declarationEvidence", method.toString());
                    serverApiEndpoints.add(endpoint);
                }
            }
        }

        private static List<Annotation> mappingAnnotations(List<?> modifiers) {
            List<Annotation> result = new ArrayList<>();
            for (Object modifier : modifiers) {
                if (!(modifier instanceof Annotation annotation)) continue;
                String name = annotationTypeName(annotation);
                if (name != null && Set.of(
                    "org.springframework.web.bind.annotation.RequestMapping",
                    "org.springframework.web.bind.annotation.GetMapping",
                    "org.springframework.web.bind.annotation.PostMapping",
                    "org.springframework.web.bind.annotation.PutMapping",
                    "org.springframework.web.bind.annotation.DeleteMapping",
                    "org.springframework.web.bind.annotation.PatchMapping").contains(name)) {
                    result.add(annotation);
                }
            }
            return result;
        }

        private static String annotationTypeName(Annotation annotation) {
            ITypeBinding binding = annotation.resolveTypeBinding();
            return binding == null || binding.isRecovered()
                ? null : binding.getTypeDeclaration().getQualifiedName();
        }

        private static List<RouteValue> routeValues(Annotation annotation) {
            Expression expression = annotationMember(annotation, "path");
            if (expression == null) expression = annotationMember(annotation, "value");
            if (expression == null) return List.of(new RouteValue("", false));
            List<Expression> expressions = new ArrayList<>();
            if (expression instanceof ArrayInitializer array) {
                for (Object item : array.expressions()) expressions.add((Expression) item);
            } else expressions.add(expression);
            List<RouteValue> result = new ArrayList<>();
            for (Expression item : expressions) {
                if (item instanceof StringLiteral literal) {
                    String value = literal.getLiteralValue();
                    result.add(new RouteValue(value, value.contains("${") || value.contains("#{")));
                    continue;
                }
                Object constant = item.resolveConstantExpressionValue();
                result.add(constant instanceof String value
                    ? new RouteValue(value, value.contains("${") || value.contains("#{"))
                    : new RouteValue(item.toString(), true));
            }
            return result;
        }

        private static Expression annotationMember(Annotation annotation, String name) {
            if (annotation instanceof SingleMemberAnnotation single && name.equals("value")) return single.getValue();
            if (!(annotation instanceof NormalAnnotation normal)) return null;
            for (Object item : normal.values()) {
                MemberValuePair pair = (MemberValuePair) item;
                if (pair.getName().getIdentifier().equals(name)) return pair.getValue();
            }
            return null;
        }

        private static List<String> httpMethods(Annotation annotation) {
            String annotationName = annotationTypeName(annotation);
            String fixed = switch (annotationName == null ? "" : annotationName) {
                case "org.springframework.web.bind.annotation.GetMapping" -> "GET";
                case "org.springframework.web.bind.annotation.PostMapping" -> "POST";
                case "org.springframework.web.bind.annotation.PutMapping" -> "PUT";
                case "org.springframework.web.bind.annotation.DeleteMapping" -> "DELETE";
                case "org.springframework.web.bind.annotation.PatchMapping" -> "PATCH";
                default -> null;
            };
            if (fixed != null) return List.of(fixed);
            Expression expression = annotationMember(annotation, "method");
            if (expression == null) return List.of();
            List<Expression> values = new ArrayList<>();
            if (expression instanceof ArrayInitializer array) {
                for (Object item : array.expressions()) values.add((Expression) item);
            } else values.add(expression);
            List<String> result = new ArrayList<>();
            for (Expression value : values) {
                String text = value.toString();
                String name = text.substring(text.lastIndexOf('.') + 1);
                if (Set.of("GET", "POST", "PUT", "DELETE", "PATCH", "HEAD", "OPTIONS", "TRACE").contains(name))
                    result.add(name);
            }
            return result;
        }

        private static String combineRoutes(String prefix, String route) {
            String left = prefix == null ? "" : prefix.trim().replaceAll("^/+|/+$", "");
            String right = route == null ? "" : route.trim().replaceAll("^/+|/+$", "");
            if (left.isEmpty()) return right;
            if (right.isEmpty()) return left;
            return "/" + (left + "/" + right).replaceAll("^/+", "").replaceAll("/+$", "");
        }

        private record RouteValue(String value, boolean dynamic) {}

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
            result.add("serverApiEndpoints", serverApiEndpoints);
            result.add("apiClientRequests", apiClientRequests);
            result.add("messageIntegrations", messageIntegrations);
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
            if (!normalized.startsWith(projectRoot)) return false;

            Path relative = projectRoot.relativize(normalized);
            if (relative.getNameCount() >= 2
                && relative.getName(0).toString().equalsIgnoreCase("src")) {
                String sourceSet = relative.getName(1).toString();
                if (sourceSet.equalsIgnoreCase("test") || sourceSet.equalsIgnoreCase("it")) {
                    return true;
                }
            }

            if (relative.getNameCount() > 0) {
                String rootDirectory = relative.getName(0).toString();
                return rootDirectory.equalsIgnoreCase("test")
                    || rootDirectory.equalsIgnoreCase("tests");
            }

            return false;
        }

        private boolean isGeneratedPath(Path file) {
            Path normalized = file.toAbsolutePath().normalize();
            if (generatedSourceRoots.stream().anyMatch(normalized::startsWith)) return true;
            if (!normalized.startsWith(projectRoot)) return false;

            Path relative = projectRoot.relativize(normalized);
            for (int index = 0; index < relative.getNameCount(); index++) {
                String segment = relative.getName(index).toString();
                if (segment.equalsIgnoreCase("generated-sources")) return true;
                if (index + 1 < relative.getNameCount()
                    && (segment.equalsIgnoreCase("build") || segment.equalsIgnoreCase("target"))
                    && relative.getName(index + 1).toString().equalsIgnoreCase("generated")) {
                    return true;
                }
                if (index == 0 && segment.equalsIgnoreCase("generated")) return true;
            }

            return false;
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
