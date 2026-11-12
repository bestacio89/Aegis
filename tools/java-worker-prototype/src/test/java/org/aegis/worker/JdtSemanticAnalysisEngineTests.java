package org.aegis.worker;

import com.google.gson.JsonArray;
import com.google.gson.JsonObject;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.io.TempDir;

import java.nio.file.Files;
import java.nio.file.Path;

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

    private static JsonArray paths(Path... values) {
        JsonArray array = new JsonArray();
        for (Path value : values) array.add(value.toString());
        return array;
    }
}
