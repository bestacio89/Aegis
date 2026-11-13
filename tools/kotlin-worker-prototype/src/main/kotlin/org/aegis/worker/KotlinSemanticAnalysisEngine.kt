package org.aegis.worker

import com.google.gson.JsonArray
import com.google.gson.JsonObject
import com.google.gson.JsonParser
import org.jetbrains.kotlin.cli.common.ExitCode
import org.jetbrains.kotlin.cli.common.arguments.K2JVMCompilerArguments
import org.jetbrains.kotlin.cli.jvm.K2JVMCompiler
import org.jetbrains.kotlin.config.Services
import java.io.File
import java.nio.file.Files
import java.nio.file.LinkOption
import java.nio.file.Path
import java.nio.file.StandardCopyOption

class KotlinSemanticAnalysisEngine : KotlinWorkerOperationHandler {
    override fun isAvailable(): Boolean = true

    override fun analyze(payload: JsonObject): JsonObject {
        val request = try {
            KotlinWorkerAnalysisRequest.from(payload)
        } catch (exception: Exception) {
            return failureResult(
                "KotlinWorkerRequestInvalid",
                exception.message ?: "The Kotlin analysis request is invalid."
            )
        }

        val unsupported = validateCompilerVersion(request)
        if (unsupported != null) return unsupported

        val paths = try {
            validatePaths(request)
        } catch (exception: Exception) {
            return failureResult(
                "KotlinWorkerInputInvalid",
                exception.message ?: "The Kotlin analysis inputs are invalid."
            )
        }

        val temporaryDirectory = Files.createTempDirectory("aegis-kotlin-analysis-")
        val classesDirectory = temporaryDirectory.resolve("classes")
        val semanticOutput = temporaryDirectory.resolve("semantic.json")
        Files.createDirectories(classesDirectory)

        val collector = KotlinCompilerMessageCollector()
        val previousOutput = System.getProperty(OUTPUT_PATH_PROPERTY)
        val previousProject = System.getProperty(PROJECT_PATH_PROPERTY)
        val previousTestScope = System.getProperty(TEST_SCOPE_PROPERTY)
        val previousGeneratedRoots = System.getProperty(GENERATED_ROOTS_PROPERTY)

        val exitCode = try {
            System.setProperty(OUTPUT_PATH_PROPERTY, semanticOutput.toString())
            System.setProperty(PROJECT_PATH_PROPERTY, paths.projectRoot.toString())
            System.setProperty(TEST_SCOPE_PROPERTY, request.includeTestCode.toString())
            System.setProperty(
                GENERATED_ROOTS_PROPERTY,
                paths.generatedRoots.joinToString(File.pathSeparator) { it.toString() }
            )

            val compilerArguments = createCompilerArguments(request, paths, classesDirectory)
            K2JVMCompiler().exec(collector, Services.EMPTY, compilerArguments)
        } catch (exception: Exception) {
            collector.report(
                org.jetbrains.kotlin.cli.common.messages.CompilerMessageSeverity.EXCEPTION,
                exception.message ?: exception.javaClass.simpleName,
                null
            )
            ExitCode.INTERNAL_ERROR
        } finally {
            restoreProperty(OUTPUT_PATH_PROPERTY, previousOutput)
            restoreProperty(PROJECT_PATH_PROPERTY, previousProject)
            restoreProperty(TEST_SCOPE_PROPERTY, previousTestScope)
            restoreProperty(GENERATED_ROOTS_PROPERTY, previousGeneratedRoots)
        }

        if (!Files.isRegularFile(semanticOutput)) {
            deleteTemporaryDirectory(temporaryDirectory)
            return failureResult(
                "KotlinSemanticExtractionUnavailable",
                "The Kotlin compiler completed without producing Aegis semantic evidence.",
                collector.diagnostics()
            )
        }

        val result = try {
            JsonParser.parseString(Files.readString(semanticOutput)).asJsonObject
        } catch (exception: Exception) {
            deleteTemporaryDirectory(temporaryDirectory)
            return failureResult(
                "KotlinSemanticEvidenceInvalid",
                "The Kotlin compiler extension produced invalid semantic evidence: ${exception.javaClass.simpleName}.",
                collector.diagnostics()
            )
        }

        result.addProperty("engineId", ENGINE_ID)
        result.addProperty("engineVersion", KotlinWorkerServer.KOTLIN_COMPILER_VERSION)
        result.addProperty("projectPath", paths.projectRoot.toString())
        result.addProperty("compilerVersion", request.compilerVersion)
        result.addProperty("languageVersion", request.languageVersion)
        result.addProperty("apiVersion", request.apiVersion)
        result.addProperty("jvmTarget", request.jvmTarget)
        result.addProperty("moduleName", request.moduleName)
        result.addProperty("compilationSucceeded", exitCode == ExitCode.OK)
        result.addProperty("compilerExitCode", exitCode.name)
        result.addProperty("compilationOutputDirectory", classesDirectory.toString())
        result.add("diagnostics", collector.diagnostics())
        result.add("capabilities", JsonArray().apply {
            add("symbols")
            add("relationships")
            add("compiler-diagnostics")
            add("jvm-interop")
        })
        result.addProperty(
            "status",
            if (exitCode == ExitCode.OK) STATUS_COMPLETED else STATUS_PARTIALLY_COMPLETED
        )
        return result
    }

    private fun createCompilerArguments(
        request: KotlinWorkerAnalysisRequest,
        paths: ValidatedPaths,
        classesDirectory: Path
    ): K2JVMCompilerArguments {
        val compilerPluginPath = Path.of(
            KotlinWorkerPluginRegistrar::class.java.protectionDomain.codeSource.location.toURI()
        ).toAbsolutePath().normalize()
        val standardLibraryPath = Path.of(
            Unit::class.java.protectionDomain.codeSource.location.toURI()
        ).toAbsolutePath().normalize()

        val classpath = (paths.classpathEntries + standardLibraryPath)
            .distinct()
            .joinToString(File.pathSeparator) { it.toString() }

        return K2JVMCompilerArguments().apply {
            destination = classesDirectory.toString()
            this.classpath = classpath
            noStdlib = true
            noReflect = true
            jvmTarget = request.jvmTarget
            languageVersion = request.languageVersion
            apiVersion = request.apiVersion
            moduleName = request.moduleName
            pluginClasspaths = arrayOf(compilerPluginPath.toString())
            javaSourceRoots = paths.referencedSourceRoots.map(Path::toString).toTypedArray()
            freeArgs = paths.sourceFiles.map(Path::toString)
        }
    }

    private fun validateCompilerVersion(request: KotlinWorkerAnalysisRequest): JsonObject? {
        if (request.compilerVersion != KotlinWorkerServer.KOTLIN_COMPILER_VERSION) {
            return failureResult(
                "KotlinCompilerVersionUnsupported",
                "This worker supports Kotlin compiler ${KotlinWorkerServer.KOTLIN_COMPILER_VERSION}; the module declares ${request.compilerVersion}."
            ).apply { addProperty("status", STATUS_UNSUPPORTED) }
        }

        val supportedLanguageVersions = setOf("2.0", "2.1", "2.2", "2.3", "2.4")
        if (request.languageVersion !in supportedLanguageVersions) {
            return failureResult(
                "KotlinLanguageVersionUnsupported",
                "Kotlin language version ${request.languageVersion} is outside the validated K2 range (2.0–2.4)."
            ).apply { addProperty("status", STATUS_UNSUPPORTED) }
        }

        if (request.apiVersion !in supportedLanguageVersions) {
            return failureResult(
                "KotlinApiVersionUnsupported",
                "Kotlin API version ${request.apiVersion} is outside the validated K2 range (2.0–2.4)."
            ).apply { addProperty("status", STATUS_UNSUPPORTED) }
        }

        return null
    }

    private fun validatePaths(request: KotlinWorkerAnalysisRequest): ValidatedPaths {
        val workspaceRoot = requireDirectory(request.workspaceRoot, "workspace root")
        val projectRoot = requireDirectory(request.projectPath, "project module")
        require(isWithin(projectRoot, workspaceRoot)) {
            "The Kotlin project module is outside the analyzed workspace."
        }

        val sourceRoots = request.sourceRoots.map { value ->
            val root = requireDirectory(value, "Kotlin source root")
            require(isWithin(root, projectRoot)) {
                "A Kotlin source root is outside its owning module: $value"
            }
            root
        }.distinct()

        require(sourceRoots.isNotEmpty()) { "The Kotlin module has no owned source roots." }

        val referencedRoots = request.referencedSourceRoots.map { value ->
            val root = requireDirectory(value, "referenced JVM source root")
            require(isWithin(root, workspaceRoot)) {
                "A referenced JVM source root is outside the analyzed workspace: $value"
            }
            root
        }.distinct()

        val testRoots = request.testSourceRoots.map { value ->
            val root = requireDirectory(value, "test source root")
            require(isWithin(root, projectRoot)) {
                "A Kotlin test source root is outside its owning module: $value"
            }
            root
        }.distinct()

        val generatedRoots = request.generatedSourceRoots.map { value ->
            val root = requireDirectory(value, "generated Kotlin source root")
            require(isWithin(root, projectRoot)) {
                "A generated Kotlin source root is outside its owning module: $value"
            }
            root
        }.distinct()

        val manifest = Path.of(request.sourceManifestPath).toAbsolutePath().normalize()
        val temporaryRoot = Path.of(System.getProperty("java.io.tmpdir")).toRealPath()
        require(Files.isRegularFile(manifest, LinkOption.NOFOLLOW_LINKS) && !Files.isSymbolicLink(manifest)) {
            "The Kotlin source manifest is not a regular, non-symbolic file."
        }
        val manifestRealPath = manifest.toRealPath()
        require(isWithin(manifestRealPath, temporaryRoot)) {
            "The Kotlin source manifest must be located under the worker temporary directory."
        }

        val manifestElement = JsonParser.parseString(Files.readString(manifestRealPath))
        require(manifestElement.isJsonArray) { "The Kotlin source manifest must be a JSON array." }
        val sourceFiles = manifestElement.asJsonArray.map { element ->
            require(element.isJsonPrimitive && element.asJsonPrimitive.isString) {
                "The Kotlin source manifest must contain only file paths."
            }
            val candidate = Path.of(element.asString).toAbsolutePath().normalize()
            require(Files.isRegularFile(candidate, LinkOption.NOFOLLOW_LINKS) && !Files.isSymbolicLink(candidate)) {
                "A Kotlin source file is missing or is a symbolic link: $candidate"
            }
            val realPath = candidate.toRealPath()
            require(sourceRoots.any { root -> isWithin(realPath, root) }) {
                "A Kotlin source file is outside the module's owned source roots: $candidate"
            }
            require(realPath.fileName.toString().endsWith(".kt", ignoreCase = true)) {
                "Only Kotlin source files with the .kt extension are supported by this worker: $candidate"
            }
            realPath
        }.distinct().sortedBy(Path::toString)

        require(sourceFiles.isNotEmpty()) { "The Kotlin source manifest contains no Kotlin source files." }

        val classpathEntries = request.classpathEntries.map { value ->
            val candidate = Path.of(value).toAbsolutePath().normalize()
            require(Files.exists(candidate)) { "A resolved classpath entry does not exist: $candidate" }
            candidate.toRealPath()
        }.distinct()

        return ValidatedPaths(
            workspaceRoot,
            projectRoot,
            sourceRoots,
            referencedRoots,
            testRoots,
            generatedRoots,
            sourceFiles,
            classpathEntries
        )
    }

    private fun requireDirectory(value: String, description: String): Path {
        val candidate = Path.of(value).toAbsolutePath().normalize()
        require(Files.isDirectory(candidate, LinkOption.NOFOLLOW_LINKS) && !Files.isSymbolicLink(candidate)) {
            "The $description does not exist or is a symbolic link: $candidate"
        }
        return candidate.toRealPath()
    }

    private fun isWithin(candidate: Path, root: Path): Boolean =
        candidate.normalize().startsWith(root.normalize())

    private fun failureResult(
        code: String,
        message: String,
        diagnostics: JsonArray = JsonArray()
    ): JsonObject {
        val result = JsonObject()
        result.addProperty("engineId", ENGINE_ID)
        result.addProperty("engineVersion", KotlinWorkerServer.KOTLIN_COMPILER_VERSION)
        result.addProperty("status", STATUS_FAILED)
        result.addProperty("analyzedFileCount", 0)
        result.addProperty("skippedFileCount", 0)
        result.add("symbols", JsonArray())
        result.add("relationships", JsonArray())
        result.add("capabilities", JsonArray())
        if (diagnostics.size() > 0) {
            result.add("diagnostics", diagnostics)
        } else {
            result.add("diagnostics", JsonArray().apply {
                add(JsonObject().apply {
                    addProperty("code", code)
                    addProperty("component", "KotlinSemanticAnalysisEngine")
                    addProperty("message", message.take(MAX_DIAGNOSTIC_LENGTH))
                    addProperty("severity", "Error")
                    addProperty("affectsCoverage", true)
                })
            })
        }
        return result
    }

    private fun restoreProperty(name: String, value: String?) {
        if (value == null) System.clearProperty(name)
        else System.setProperty(name, value)
    }

    private fun deleteTemporaryDirectory(directory: Path) {
        try {
            Files.walk(directory).use { paths ->
                paths.sorted(Comparator.reverseOrder()).forEach { Files.deleteIfExists(it) }
            }
        } catch (_: Exception) {
            System.err.println("Could not clean a temporary Kotlin analysis directory.")
        }
    }

    private data class ValidatedPaths(
        val workspaceRoot: Path,
        val projectRoot: Path,
        val sourceRoots: List<Path>,
        val referencedSourceRoots: List<Path>,
        val testRoots: List<Path>,
        val generatedRoots: List<Path>,
        val sourceFiles: List<Path>,
        val classpathEntries: List<Path>
    )

    companion object {
        private const val ENGINE_ID = "kotlin-k2-ir"
        private const val MAX_DIAGNOSTIC_LENGTH = 8_192
        private const val OUTPUT_PATH_PROPERTY = "aegis.kotlin.semantic.output"
        private const val PROJECT_PATH_PROPERTY = "aegis.kotlin.semantic.project"
        private const val TEST_SCOPE_PROPERTY = "aegis.kotlin.semantic.test"
        private const val GENERATED_ROOTS_PROPERTY = "aegis.kotlin.semantic.generated"
        private const val STATUS_UNSUPPORTED = 1
        private const val STATUS_COMPLETED = 2
        private const val STATUS_PARTIALLY_COMPLETED = 3
        private const val STATUS_FAILED = 4
    }
}
