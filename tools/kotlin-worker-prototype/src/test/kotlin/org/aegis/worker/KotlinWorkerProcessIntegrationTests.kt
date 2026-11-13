package org.aegis.worker

import com.google.gson.JsonArray
import com.google.gson.JsonObject
import com.google.gson.JsonParser
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Test
import java.io.BufferedReader
import java.io.BufferedWriter
import java.io.InputStreamReader
import java.io.OutputStreamWriter
import java.nio.charset.StandardCharsets
import java.nio.file.Files
import java.nio.file.Path
import java.util.concurrent.Callable
import java.util.concurrent.Executors
import java.util.concurrent.TimeUnit

class KotlinWorkerProcessIntegrationTests {
    @Test
    fun `worker process handshakes compiles Kotlin and shuts down cleanly`() {
        val workspace = Files.createTempDirectory("aegis-kotlin-worker-process-")
        val sourceRoot = workspace.resolve("src/main/kotlin")
        val source = sourceRoot.resolve("sample/WorkerProbe.kt")
        val manifest = Files.createTempFile("aegis-kotlin-source-manifest-", ".json")
        Files.createDirectories(source.parent)
        Files.writeString(
            source,
            """
            package sample

            class WorkerProbe {
                fun execute(): String = "ready"
            }
            """.trimIndent()
        )
        Files.writeString(
            manifest,
            JsonArray().apply { add(source.toAbsolutePath().toString()) }.toString()
        )
        val standardLibraryPath = Path.of(
            Unit::class.java.protectionDomain.codeSource.location.toURI()
        ).toAbsolutePath().normalize()

        val javaExecutable = Path.of(
            System.getProperty("java.home"),
            "bin",
            if (System.getProperty("os.name").startsWith("Windows", ignoreCase = true)) "java.exe" else "java"
        )
        val classpath = System.getProperty("surefire.test.class.path")
            ?: System.getProperty("java.class.path")
        val process = ProcessBuilder(
            javaExecutable.toString(),
            "-Xms64m",
            "-Xmx1024m",
            "-cp",
            classpath,
            "org.aegis.worker.KotlinWorkerMain"
        ).redirectError(ProcessBuilder.Redirect.DISCARD).start()

        val executor = Executors.newSingleThreadExecutor()
        var compilationOutput: Path? = null
        var oversizedManifest: Path? = null
        var oversizedByteManifest: Path? = null
        var brokenManifest: Path? = null
        var brokenCompilationOutput: Path? = null
        try {
            val writer = BufferedWriter(OutputStreamWriter(process.outputStream, StandardCharsets.UTF_8))
            val reader = BufferedReader(InputStreamReader(process.inputStream, StandardCharsets.UTF_8))

            writer.write(request("handshake", "handshake").toString())
            writer.newLine()
            writer.flush()
            val handshake = readResponse(executor, reader)
            assertEquals("ok", handshake.get("status").asString)
            assertEquals(KotlinWorkerServer.PROTOCOL_VERSION, handshake.getAsJsonObject("result")
                .get("protocolVersion").asInt)

            writer.write(request("analyze", "analyze", analysisPayload(workspace, sourceRoot, manifest, standardLibraryPath)).toString())
            writer.newLine()
            writer.flush()
            val analysis = readResponse(executor, reader)
            assertEquals("ok", analysis.get("status").asString)

            val result = analysis.getAsJsonObject("result")
            assertEquals(2, result.get("status").asInt, "Kotlin worker diagnostics: ${result.getAsJsonArray("diagnostics")}")
            assertTrue(result.getAsJsonArray("symbols").any {
                it.asJsonObject.get("qualifiedName").asString == "sample.WorkerProbe"
            })
            compilationOutput = Path.of(result.get("compilationOutputDirectory").asString)
            assertTrue(Files.isDirectory(compilationOutput), "The worker must retain JVM output for the host interop phase.")

            oversizedManifest = Files.createTempFile("aegis-kotlin-source-manifest-oversized-", ".json")
            Files.writeString(
                oversizedManifest,
                JsonArray().apply {
                    repeat(50_001) { add(source.toAbsolutePath().toString()) }
                }.toString()
            )
            writer.write(
                request(
                    "oversized-manifest",
                    "analyze",
                    analysisPayload(workspace, sourceRoot, oversizedManifest, standardLibraryPath)
                ).toString()
            )
            writer.newLine()
            writer.flush()
            val boundedAnalysis = readResponse(executor, reader)
            assertEquals("ok", boundedAnalysis.get("status").asString)
            val boundedResult = boundedAnalysis.getAsJsonObject("result")
            assertEquals(4, boundedResult.get("status").asInt)
            assertEquals("KotlinWorkerInputInvalid", boundedResult.getAsJsonArray("diagnostics")
                .first().asJsonObject.get("code").asString)
            assertTrue(
                boundedResult.getAsJsonArray("diagnostics").first().asJsonObject.get("message").asString
                    .contains("50000-file limit")
            )

            val excessiveRootsPayload = analysisPayload(workspace, sourceRoot, manifest, standardLibraryPath)
            excessiveRootsPayload.add(
                "sourceRoots",
                JsonArray().apply {
                    repeat(1_025) { add(sourceRoot.toAbsolutePath().toString()) }
                }
            )
            writer.write(request("excessive-roots", "analyze", excessiveRootsPayload).toString())
            writer.newLine()
            writer.flush()
            val excessiveRootsAnalysis = readResponse(executor, reader)
            assertEquals("ok", excessiveRootsAnalysis.get("status").asString)
            val excessiveRootsResult = excessiveRootsAnalysis.getAsJsonObject("result")
            assertEquals(4, excessiveRootsResult.get("status").asInt)
            assertTrue(
                excessiveRootsResult.getAsJsonArray("diagnostics").first().asJsonObject.get("message").asString
                    .contains("source-root limit")
            )

            oversizedByteManifest = Files.createTempFile("aegis-kotlin-source-manifest-bytes-", ".json")
            Files.writeString(oversizedByteManifest, " ".repeat(16 * 1024 * 1024 + 1))
            writer.write(
                request(
                    "oversized-bytes",
                    "analyze",
                    analysisPayload(workspace, sourceRoot, oversizedByteManifest, standardLibraryPath)
                ).toString()
            )
            writer.newLine()
            writer.flush()
            val byteBoundedAnalysis = readResponse(executor, reader)
            assertEquals("ok", byteBoundedAnalysis.get("status").asString)
            val byteBoundedResult = byteBoundedAnalysis.getAsJsonObject("result")
            assertEquals(4, byteBoundedResult.get("status").asInt)
            assertTrue(
                byteBoundedResult.getAsJsonArray("diagnostics").first().asJsonObject.get("message").asString
                    .contains("16777216-byte limit")
            )

            val brokenSourceRoot = workspace.resolve("broken/src/main/kotlin")
            val brokenSource = brokenSourceRoot.resolve("sample/BrokenProbe.kt")
            Files.createDirectories(brokenSource.parent)
            Files.writeString(
                brokenSource,
                """
                package sample

                class BrokenProbe {
                    fun run(): String = missingDependency()
                }
                """.trimIndent()
            )
            brokenManifest = Files.createTempFile("aegis-kotlin-source-manifest-broken-", ".json")
            Files.writeString(
                brokenManifest,
                JsonArray().apply { add(brokenSource.toAbsolutePath().toString()) }.toString()
            )
            writer.write(
                request(
                    "compiler-error-recovery",
                    "analyze",
                    analysisPayload(workspace, brokenSourceRoot, brokenManifest, standardLibraryPath)
                ).toString()
            )
            writer.newLine()
            writer.flush()
            val recoveryAnalysis = readResponse(executor, reader)
            assertEquals("ok", recoveryAnalysis.get("status").asString)
            val recoveryResult = recoveryAnalysis.getAsJsonObject("result")
            assertNotEquals(2, recoveryResult.get("status").asInt)
            assertTrue(
                recoveryResult.getAsJsonArray("diagnostics").any { diagnostic ->
                    diagnostic.asJsonObject.get("severity").asString == "Error"
                        && diagnostic.asJsonObject.get("message").asString
                            .contains("unresolved reference", ignoreCase = true)
                },
                "Compiler errors must remain explicit diagnostics and must not produce a completed analysis."
            )
            recoveryResult.get("compilationOutputDirectory")?.takeIf { !it.isJsonNull }?.asString?.let {
                brokenCompilationOutput = Path.of(it)
            }

            writer.write(request("shutdown", "shutdown").toString())
            writer.newLine()
            writer.flush()
            assertEquals("ok", readResponse(executor, reader).get("status").asString)
            assertTrue(process.waitFor(30, TimeUnit.SECONDS), "The worker must exit after shutdown.")
        } finally {
            if (process.isAlive) process.destroyForcibly()
            executor.shutdownNow()
            compilationOutput?.let(::deleteRecursively)
            deleteRecursively(workspace)
            Files.deleteIfExists(manifest)
            oversizedManifest?.let(Files::deleteIfExists)
            oversizedByteManifest?.let(Files::deleteIfExists)
            brokenManifest?.let(Files::deleteIfExists)
            brokenCompilationOutput?.let(::deleteRecursively)
        }
    }

    private fun analysisPayload(
        workspace: Path,
        sourceRoot: Path,
        manifest: Path,
        standardLibraryPath: Path
    ): JsonObject =
        JsonObject().apply {
            addProperty("projectPath", workspace.toAbsolutePath().toString())
            addProperty("workspaceRoot", workspace.toAbsolutePath().toString())
            addProperty("sourceManifestPath", manifest.toAbsolutePath().toString())
            add("sourceRoots", stringArray(sourceRoot.toAbsolutePath().toString()))
            add("referencedSourceRoots", JsonArray())
            add("testSourceRoots", JsonArray())
            add("generatedSourceRoots", JsonArray())
            add("classpathEntries", JsonArray())
            addProperty("kotlinStandardLibraryPath", standardLibraryPath.toString())
            addProperty("compilerVersion", KotlinWorkerServer.KOTLIN_COMPILER_VERSION)
            addProperty("languageVersion", "2.4")
            addProperty("apiVersion", "2.4")
            addProperty("jvmTarget", "21")
            addProperty("moduleName", "worker-probe")
            addProperty("includeTestCode", false)
        }

    private fun stringArray(value: String): JsonArray = JsonArray().apply { add(value) }

    private fun request(requestId: String, operation: String, payload: JsonObject? = null): JsonObject =
        JsonObject().apply {
            addProperty("protocolVersion", KotlinWorkerServer.PROTOCOL_VERSION)
            addProperty("requestId", requestId)
            addProperty("operation", operation)
            if (payload == null) add("payload", com.google.gson.JsonNull.INSTANCE)
            else add("payload", payload)
        }

    private fun readResponse(
        executor: java.util.concurrent.ExecutorService,
        reader: BufferedReader
    ): JsonObject {
        val line = executor.submit(Callable { reader.readLine() }).get(30, TimeUnit.SECONDS)
            ?: error("The Kotlin worker closed stdout before returning a response.")
        return JsonParser.parseString(line).asJsonObject
    }

    private fun deleteRecursively(path: Path) {
        if (!Files.exists(path)) return
        Files.walk(path).use { paths ->
            paths.sorted(Comparator.reverseOrder()).forEach(Files::deleteIfExists)
        }
    }
}
