package org.aegis.worker

import com.google.gson.Gson
import com.google.gson.JsonArray
import com.google.gson.JsonObject

import org.jetbrains.kotlin.cli.common.ExitCode
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Test
import java.nio.file.Files
import java.nio.file.Path

class KotlinSemanticAnalysisEngineTests {
    @Test
    fun `rejects source files over the per-file byte budget`(@org.junit.jupiter.api.io.TempDir temporaryDirectory: Path) {
        val project = Files.createDirectories(temporaryDirectory.resolve("project"))
        val sourceRoot = Files.createDirectories(project.resolve("src/main/kotlin"))
        val sourceFile = sourceRoot.resolve("Oversized.kt")
        Files.newByteChannel(sourceFile, java.nio.file.StandardOpenOption.CREATE_NEW, java.nio.file.StandardOpenOption.WRITE).use { channel ->
            channel.position(16L * 1024 * 1024)
            channel.write(java.nio.ByteBuffer.wrap(byteArrayOf(1)))
        }
        val artifactRoot = Files.createDirectories(temporaryDirectory.resolve("artifacts"))
        val manifest = artifactRoot.resolve("source-manifest.json")
        Files.writeString(manifest, Gson().toJson(JsonArray().apply { add(sourceFile.toString()) }))
        val standardLibrary = temporaryDirectory.resolve("kotlin-stdlib-2.4.21.jar")
        Files.write(standardLibrary, byteArrayOf(1))
        val payload = JsonObject().apply {
            addProperty("projectPath", project.toString())
            addProperty("workspaceRoot", temporaryDirectory.toString())
            addProperty("sourceManifestPath", manifest.toString())
            addProperty("artifactRoot", artifactRoot.toString())
            add("sourceRoots", com.google.gson.JsonArray().apply { add(sourceRoot.toString()) })
            add("sharedSourceRoots", com.google.gson.JsonArray())
            add("referencedSourceRoots", com.google.gson.JsonArray())
            add("testSourceRoots", com.google.gson.JsonArray())
            add("generatedSourceRoots", com.google.gson.JsonArray())
            add("classpathEntries", com.google.gson.JsonArray())
            addProperty("kotlinStandardLibraryPath", standardLibrary.toString())
            addProperty("compilerVersion", KotlinWorkerServer.KOTLIN_COMPILER_VERSION)
            addProperty("languageVersion", "2.4")
            addProperty("apiVersion", "2.4")
            addProperty("jvmTarget", "21")
            addProperty("moduleName", "resource-budget-test")
        }
        val result = KotlinSemanticAnalysisEngine().analyze(payload)
        assertEquals(4, result.get("status").asInt)
        assertTrue(result.getAsJsonArray("diagnostics").first().asJsonObject.get("message").asString.contains("per-file limit"))
    }

    @Test
    fun `rejects source manifests over the 16 mib byte budget`(@org.junit.jupiter.api.io.TempDir temporaryDirectory: Path) {
        val project = Files.createDirectories(temporaryDirectory.resolve("project"))
        val sourceRoot = Files.createDirectories(project.resolve("src/main/kotlin"))
        val artifactRoot = Files.createDirectories(temporaryDirectory.resolve("artifacts"))
        val manifest = artifactRoot.resolve("source-manifest.json")
        Files.newByteChannel(manifest, java.nio.file.StandardOpenOption.CREATE_NEW, java.nio.file.StandardOpenOption.WRITE).use { channel ->
            channel.position(16L * 1024 * 1024)
            channel.write(java.nio.ByteBuffer.wrap(byteArrayOf(1)))
        }
        val standardLibrary = temporaryDirectory.resolve("kotlin-stdlib-2.4.21.jar")
        Files.write(standardLibrary, byteArrayOf(1))
        val payload = JsonObject().apply {
            addProperty("projectPath", project.toString())
            addProperty("workspaceRoot", temporaryDirectory.toString())
            addProperty("sourceManifestPath", manifest.toString())
            addProperty("artifactRoot", artifactRoot.toString())
            add("sourceRoots", com.google.gson.JsonArray().apply { add(sourceRoot.toString()) })
            add("sharedSourceRoots", com.google.gson.JsonArray())
            add("referencedSourceRoots", com.google.gson.JsonArray())
            add("testSourceRoots", com.google.gson.JsonArray())
            add("generatedSourceRoots", com.google.gson.JsonArray())
            add("classpathEntries", com.google.gson.JsonArray())
            addProperty("kotlinStandardLibraryPath", standardLibrary.toString())
            addProperty("compilerVersion", KotlinWorkerServer.KOTLIN_COMPILER_VERSION)
            addProperty("languageVersion", "2.4")
            addProperty("apiVersion", "2.4")
            addProperty("jvmTarget", "21")
            addProperty("moduleName", "manifest-budget-test")
        }
        val result = KotlinSemanticAnalysisEngine().analyze(payload)
        assertEquals(4, result.get("status").asInt)
        assertTrue(result.getAsJsonArray("diagnostics").first().asJsonObject.get("message").asString.contains("byte limit"))
    }


    @Test
    fun `successful compilation is completed`() {
        assertEquals(
            2,
            KotlinSemanticAnalysisEngine().statusForExitCode(ExitCode.OK)
        )
    }

    @Test
    fun `source compilation errors remain partially completed`() {
        assertEquals(
            3,
            KotlinSemanticAnalysisEngine().statusForExitCode(ExitCode.COMPILATION_ERROR)
        )
    }

    @Test
    fun `compiler infrastructure failures are failed`() {
        assertEquals(
            4,
            KotlinSemanticAnalysisEngine().statusForExitCode(ExitCode.INTERNAL_ERROR)
        )
    }
}
