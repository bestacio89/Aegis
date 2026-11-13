package org.aegis.worker;

import com.google.gson.JsonArray;
import com.google.gson.JsonObject;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.io.TempDir;

import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.Comparator;

import static org.junit.jupiter.api.Assertions.*;

final class JdtSemanticAnalysisEngineTests {
    @TempDir
    Path workspace;

    @Test
    void resolves_source_symbols_inheritance_calls_and_annotations() throws Exception {
        Path project = Files.createDirectories(workspace.resolve("project"));
        Path sourceRoot = Files.createDirectories(project.resolve("src/main/java/example"));
        Path annotation = sourceRoot.resolve("Component.java");
        Path contract = sourceRoot.resolve("Contract.java");
        Path service = sourceRoot.resolve("Service.java");

        Files.writeString(annotation, "package example; public @interface Component {}");
        Files.writeString(contract, "package example; public interface Contract { void execute(); }");
        Files.writeString(service, """
            package example;
            @Component
            public final class Service implements Contract {
                public void execute() { helper(); }
                private void helper() {}
            }
            """);

        JsonObject payload = new JsonObject();
        payload.addProperty("projectPath", project.toString());
        payload.add("sourceRoots", paths(sourceRoot.getParent()));
        payload.add("sourceFiles", paths(annotation, contract, service));
        payload.add("classpathEntries", new JsonArray());
        payload.addProperty("sourceLevel", "21");
        payload.addProperty("includeTestCode", false);
        payload.addProperty("includeGeneratedCode", false);

        JsonObject result = new JdtSemanticAnalysisEngine().analyze(payload);
        assertEquals(2, result.get("status").getAsInt());
        assertEquals(3, result.get("analyzedFileCount").getAsInt());

        JsonArray symbols = result.getAsJsonArray("symbols");
        assertTrue(symbols.asList().stream().anyMatch(item ->
            item.getAsJsonObject().get("qualifiedName").getAsString().equals("example.Service")));
        assertTrue(symbols.asList().stream().anyMatch(item ->
            item.getAsJsonObject().get("qualifiedName").getAsString().equals("example.Service#execute()")));

        JsonArray relationships = result.getAsJsonArray("relationships");
        assertTrue(relationships.asList().stream().anyMatch(item ->
            item.getAsJsonObject().get("kind").getAsInt() == 3
                && item.getAsJsonObject().get("resolutionStatus").getAsInt() == 1));
        assertTrue(relationships.asList().stream().anyMatch(item ->
            item.getAsJsonObject().get("kind").getAsInt() == 4
                && item.getAsJsonObject().get("resolutionStatus").getAsInt() == 1));
        assertTrue(relationships.asList().stream().anyMatch(item ->
            item.getAsJsonObject().get("kind").getAsInt() == 7
                && item.getAsJsonObject().get("resolutionStatus").getAsInt() == 1));
        assertTrue(relationships.asList().stream().anyMatch(item ->
            item.getAsJsonObject().get("kind").getAsInt() == 5
                && item.getAsJsonObject().get("resolutionStatus").getAsInt() == 1));
    }

    @Test
    void marks_analysis_partial_when_jdt_reports_coverage_affecting_compiler_errors() throws Exception {
        Path project = Files.createDirectories(workspace.resolve("broken-project"));
        Path sourceRoot = Files.createDirectories(project.resolve("src/main/java/example"));
        Path source = sourceRoot.resolve("Broken.java");
        Files.writeString(source, "package example; public class Broken { MissingType value; }");

        JsonObject payload = new JsonObject();
        payload.addProperty("projectPath", project.toString());
        payload.add("sourceRoots", paths(sourceRoot.getParent()));
        payload.add("sourceFiles", paths(source));
        payload.add("classpathEntries", new JsonArray());

        JsonObject result = new JdtSemanticAnalysisEngine().analyze(payload);

        assertEquals(3, result.get("status").getAsInt());
        assertTrue(result.getAsJsonArray("diagnostics").asList().stream().anyMatch(item ->
            item.getAsJsonObject().get("severity").getAsString().equals("Error")
                && item.getAsJsonObject().get("affectsCoverage").getAsBoolean()));

        JsonObject sourceType = result.getAsJsonArray("symbols").asList().stream()
            .map(item -> item.getAsJsonObject())
            .filter(item -> item.get("name").getAsString().equals("Broken"))
            .findFirst()
            .orElseThrow();

        assertEquals(1, sourceType.get("origin").getAsInt());
        assertEquals("example", sourceType.get("containerName").getAsString());
        assertEquals("Package", sourceType.get("containerKind").getAsString());
        assertEquals(source.toString(), sourceType.get("filePath").getAsString());
    }

    @Test
    void rejects_source_files_outside_the_declared_module_roots() throws Exception {
        Path project = Files.createDirectories(workspace.resolve("project"));
        Path sourceRoot = Files.createDirectories(project.resolve("src/main/java"));
        Path outside = Files.createDirectories(workspace.resolve("outside")).resolve("Leak.java");
        Files.writeString(outside, "class Leak {}");

        JsonObject payload = new JsonObject();
        payload.addProperty("projectPath", project.toString());
        payload.add("sourceRoots", paths(sourceRoot));
        payload.add("sourceFiles", paths(outside));
        payload.add("classpathEntries", new JsonArray());

        assertThrows(IllegalArgumentException.class,
            () -> new JdtSemanticAnalysisEngine().analyze(payload));
    }

    @Test
    void resolves_types_from_only_explicitly_referenced_project_source_roots() throws Exception {
        Path project = Files.createDirectories(workspace.resolve("consumer"));
        Path sourceRoot = Files.createDirectories(project.resolve("src/main/java"));
        Path consumerPackage = Files.createDirectories(sourceRoot.resolve("consumer"));
        Path apiProject = Files.createDirectories(workspace.resolve("api"));
        Path apiSourceRoot = Files.createDirectories(apiProject.resolve("src/main/java"));
        Path apiPackage = Files.createDirectories(apiSourceRoot.resolve("api"));
        Path consumerFile = consumerPackage.resolve("Consumer.java");
        Path apiFile = apiPackage.resolve("PublicApi.java");

        Files.writeString(apiFile, """
            package api;
            public final class PublicApi {
                public String name() { return "api"; }
            }
            """);
        Files.writeString(consumerFile, """
            package consumer;
            import api.PublicApi;
            public final class Consumer {
                public String call(PublicApi api) { return api.name(); }
            }
            """);

        JsonObject unresolvedPayload = new JsonObject();
        unresolvedPayload.addProperty("projectPath", project.toString());
        unresolvedPayload.addProperty("workspaceRoot", workspace.toString());
        unresolvedPayload.add("sourceRoots", paths(sourceRoot));
        unresolvedPayload.add("sourceFiles", paths(consumerFile));
        unresolvedPayload.add("classpathEntries", new JsonArray());

        JsonObject unresolvedResult = new JdtSemanticAnalysisEngine().analyze(unresolvedPayload);
        assertEquals(3, unresolvedResult.get("status").getAsInt());
        assertTrue(unresolvedResult.getAsJsonArray("diagnostics").asList().stream().anyMatch(item ->
            item.getAsJsonObject().get("severity").getAsString().equals("Error")
                && item.getAsJsonObject().get("affectsCoverage").getAsBoolean()));

        assertTrue(unresolvedResult.getAsJsonArray("relationships").asList().stream().anyMatch(item ->
            item.getAsJsonObject().get("kind").getAsInt() == 5
                && item.getAsJsonObject().get("resolutionStatus").getAsInt() == 3));
        assertTrue(unresolvedResult.getAsJsonArray("relationships").asList().stream().anyMatch(item ->
            item.getAsJsonObject().get("kind").getAsInt() == 4
                && item.getAsJsonObject().get("resolutionStatus").getAsInt() == 3));

        JsonObject resolvedPayload = unresolvedPayload.deepCopy();
        resolvedPayload.add("referencedSourceRoots", paths(apiSourceRoot));
        JsonObject resolvedResult = new JdtSemanticAnalysisEngine().analyze(resolvedPayload);

        assertEquals(2, resolvedResult.get("status").getAsInt());
        assertEquals(1, resolvedResult.get("analyzedFileCount").getAsInt());
        assertTrue(resolvedResult.getAsJsonArray("relationships").asList().stream().anyMatch(item ->
            item.getAsJsonObject().get("kind").getAsInt() == 5
                && item.getAsJsonObject().get("resolutionStatus").getAsInt() == 1
                && item.getAsJsonObject().has("targetSymbolId")
                && !item.getAsJsonObject().get("targetSymbolId").isJsonNull()));
        assertTrue(resolvedResult.getAsJsonArray("relationships").asList().stream().anyMatch(item ->
            item.getAsJsonObject().get("kind").getAsInt() == 4
                && item.getAsJsonObject().get("resolutionStatus").getAsInt() == 1));

        JsonObject apiPayload = new JsonObject();
        apiPayload.addProperty("projectPath", apiProject.toString());
        apiPayload.addProperty("workspaceRoot", workspace.toString());
        apiPayload.add("sourceRoots", paths(apiSourceRoot));
        apiPayload.add("sourceFiles", paths(apiFile));
        apiPayload.add("classpathEntries", new JsonArray());
        JsonObject apiResult = new JdtSemanticAnalysisEngine().analyze(apiPayload);
        String declaredApiSymbolId = apiResult.getAsJsonArray("symbols").asList().stream()
            .map(item -> item.getAsJsonObject())
            .filter(item -> item.get("qualifiedName").getAsString().equals("api.PublicApi"))
            .map(item -> item.get("id").getAsString())
            .findFirst()
            .orElseThrow();

        String resolvedApiSymbolId = resolvedResult.getAsJsonArray("relationships").asList().stream()
            .map(item -> item.getAsJsonObject())
            .filter(item -> item.get("kind").getAsInt() == 5
                && item.get("resolutionStatus").getAsInt() == 1
                && item.get("targetSymbolId").getAsString().equals(declaredApiSymbolId))
            .map(item -> item.get("targetSymbolId").getAsString())
            .findFirst()
            .orElseThrow();

        assertEquals(declaredApiSymbolId, resolvedApiSymbolId);
        assertFalse(resolvedResult.getAsJsonArray("symbols").asList().stream().anyMatch(item ->
            item.getAsJsonObject().get("qualifiedName").getAsString().equals("api.PublicApi")));
    }

    @Test
    void rejects_referenced_source_roots_outside_the_workspace_boundary() throws Exception {
        Path project = Files.createDirectories(workspace.resolve("consumer"));
        Path sourceRoot = Files.createDirectories(project.resolve("src/main/java"));
        Path source = sourceRoot.resolve("Consumer.java");
        Files.writeString(source, "package consumer; public final class Consumer {}");

        Path outsideWorkspace = Files.createDirectories(
            workspace.getParent().resolve("aegis-outside-" + java.util.UUID.randomUUID()));
        try {
            Path outsideSourceRoot = Files.createDirectories(outsideWorkspace.resolve("src/main/java"));
            JsonObject payload = new JsonObject();
            payload.addProperty("projectPath", project.toString());
            payload.addProperty("workspaceRoot", workspace.toString());
            payload.add("sourceRoots", paths(sourceRoot));
            payload.add("referencedSourceRoots", paths(outsideSourceRoot));
            payload.add("sourceFiles", paths(source));
            payload.add("classpathEntries", new JsonArray());

            assertThrows(IllegalArgumentException.class,
                () -> new JdtSemanticAnalysisEngine().analyze(payload));
        } finally {
            deleteRecursively(outsideWorkspace);
        }
    }

    @Test
    void does_not_classify_production_sources_as_tests_because_of_ancestor_or_package_names() throws Exception {
        Path project = Files.createDirectories(workspace.resolve("tests").resolve("project"));
        Path sourceRoot = Files.createDirectories(project.resolve("src/main/java/com/test"));
        Path source = sourceRoot.resolve("Service.java");
        Files.writeString(source, "package com.test; public final class Service {}");

        JsonObject payload = new JsonObject();
        payload.addProperty("projectPath", project.toString());
        payload.add("sourceRoots", paths(project.resolve("src/main/java")));
        payload.add("sourceFiles", paths(source));
        payload.add("classpathEntries", new JsonArray());
        payload.addProperty("includeTestCode", false);
        payload.addProperty("includeGeneratedCode", false);

        JsonObject result = new JdtSemanticAnalysisEngine().analyze(payload);

        assertEquals(2, result.get("status").getAsInt());
        assertEquals(1, result.get("analyzedFileCount").getAsInt());
        assertEquals(0, result.get("skippedFileCount").getAsInt());
        assertTrue(result.getAsJsonArray("symbols").asList().stream().anyMatch(item ->
            item.getAsJsonObject().get("qualifiedName").getAsString().equals("com.test.Service")));
    }

    private static void deleteRecursively(Path directory) throws IOException {
        try (var paths = Files.walk(directory)) {
            paths.sorted(Comparator.reverseOrder()).forEach(path -> {
                try { Files.deleteIfExists(path); }
                catch (IOException exception) {
                    throw new java.io.UncheckedIOException(exception);
                }
            });
        }
    }

    private static JsonArray paths(Path... values) {
        JsonArray array = new JsonArray();
        for (Path value : values) array.add(value.toString());
        return array;
    }
}
