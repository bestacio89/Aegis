package org.aegis.worker

import androidx.compose.compiler.plugins.kotlin.ComposePluginRegistrar
import androidx.compose.runtime.Composer
import com.google.gson.JsonParser
import org.jetbrains.kotlin.cli.common.ExitCode
import org.jetbrains.kotlin.cli.common.messages.MessageRenderer
import org.jetbrains.kotlin.cli.common.messages.PrintingMessageCollector
import org.jetbrains.kotlin.cli.common.arguments.K2JVMCompilerArguments
import org.jetbrains.kotlin.cli.jvm.K2JVMCompiler
import org.jetbrains.kotlin.config.Services
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertFalse
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Test
import java.io.File
import java.net.URLClassLoader
import java.nio.file.Files
import java.nio.file.Path

class KotlinComposeCompilerIntegrationTests {
    @Test
    fun `Compose compiler preserves source declarations and composable call identity`() {
        val workspace = Files.createTempDirectory("aegis-kotlin-compose-")
        val sourceRoot = Files.createDirectories(workspace.resolve("src"))
        val source = sourceRoot.resolve("Screen.kt")
        val output = workspace.resolve("semantic.json")
        val classes = Files.createDirectories(workspace.resolve("classes"))
        val sourceText = """
            package sample

            import androidx.compose.runtime.Composable

            @Composable
            fun Child(label: String) {}

            @Composable
            fun Screen() {
                Child("hello")
            }
        """.trimIndent()

        val previousOutput = System.getProperty("aegis.kotlin.semantic.output")
        val previousProject = System.getProperty("aegis.kotlin.semantic.project")
        val previousTest = System.getProperty("aegis.kotlin.semantic.test")
        val previousGenerated = System.getProperty("aegis.kotlin.semantic.generated")

        try {
            Files.writeString(source, sourceText)
            val workerPluginPath = Path.of(
                KotlinWorkerPluginRegistrar::class.java.protectionDomain.codeSource.location.toURI()
            ).toAbsolutePath().normalize()
            val composePluginPath = Path.of(
                ComposePluginRegistrar::class.java.protectionDomain.codeSource.location.toURI()
            ).toAbsolutePath().normalize()
            val stdlibPath = Path.of(
                Unit::class.java.protectionDomain.codeSource.location.toURI()
            ).toAbsolutePath().normalize()
            val composeRuntimePath = Path.of(
                Composer::class.java.protectionDomain.codeSource.location.toURI()
            ).toAbsolutePath().normalize()

            System.setProperty("aegis.kotlin.semantic.output", output.toString())
            System.setProperty("aegis.kotlin.semantic.project", workspace.toString())
            System.setProperty("aegis.kotlin.semantic.test", "false")
            System.setProperty("aegis.kotlin.semantic.generated", "")

            val arguments = K2JVMCompilerArguments().apply {
                destination = classes.toString()
                classpath = listOf(stdlibPath, composeRuntimePath).joinToString(File.pathSeparator)
                noStdlib = true
                noReflect = true
                jvmTarget = "21"
                languageVersion = "2.4"
                apiVersion = "2.4"
                moduleName = "compose-semantic-fixture"
                pluginClasspaths = arrayOf(composePluginPath.toString(), workerPluginPath.toString())
                pluginOptions = arrayOf(
                    "plugin:androidx.compose.compiler.plugins.kotlin:sourceInformation=true"
                )
                freeArgs = listOf(source.toString())
            }
            val messages = PrintingMessageCollector(
                System.err,
                MessageRenderer.PLAIN_RELATIVE_PATHS,
                false
            )
            val exitCode = K2JVMCompiler().exec(messages, Services.EMPTY, arguments)
            assertEquals(ExitCode.OK, exitCode)

            val evidence = JsonParser.parseString(Files.readString(output)).asJsonObject
            val symbols = evidence.getAsJsonArray("symbols").map { it.asJsonObject }
            val composables = symbols.filter { symbol ->
                symbol.getAsJsonArray("modifiers")?.any {
                    it.asString.equals("Composable", ignoreCase = true)
                } == true
            }
            assertEquals(
                setOf("sample.Child", "sample.Screen"),
                composables.map { it.get("qualifiedName").asString }.toSet()
            )
            assertTrue(composables.all { it.get("origin").asInt == 1 })
            assertTrue(composables.all {
                Path.of(it.get("filePath").asString).toAbsolutePath().normalize()
                    == source.toAbsolutePath().normalize()
            })

            URLClassLoader(
                arrayOf(classes.toUri().toURL()),
                KotlinSemanticAnalysisEngine::class.java.classLoader
            ).use { classLoader ->
                val screenClass = classLoader.loadClass("sample.ScreenKt")
                val screenMethod = screenClass.declaredMethods.single { it.name == "Screen" }
                assertTrue(
                    screenMethod.parameterTypes.any { it.name == Composer::class.java.name },
                    "The Compose compiler plugin must lower composable functions with a Composer parameter."
                )
            }

            val child = composables.single { it.get("qualifiedName").asString == "sample.Child" }
            val screen = composables.single { it.get("qualifiedName").asString == "sample.Screen" }
            val relationships = evidence.getAsJsonArray("relationships").map { it.asJsonObject }
            assertTrue(relationships.any {
                it.get("kind").asInt == 4
                    && it.get("sourceSymbolId").asString == screen.get("id").asString
                    && it.get("targetSymbolId").asString == child.get("id").asString
            }, "A composable invocation must resolve to the source-level composable declaration.")
            assertFalse(
                symbols.any { it.get("name").asString == "\$composer" },
                "Compiler-injected Composer parameters must not become source declarations."
            )
            symbols.filter { it.get("origin").asInt == 2 }.forEach { generated ->
                assertTrue(generated.get("filePath").isJsonNull)
                assertTrue(generated.get("startLine").isJsonNull)
            }
        } finally {
            restoreProperty("aegis.kotlin.semantic.output", previousOutput)
            restoreProperty("aegis.kotlin.semantic.project", previousProject)
            restoreProperty("aegis.kotlin.semantic.test", previousTest)
            restoreProperty("aegis.kotlin.semantic.generated", previousGenerated)
            workspace.toFile().deleteRecursively()
        }
    }

    private fun restoreProperty(name: String, value: String?) {
        if (value == null) System.clearProperty(name) else System.setProperty(name, value)
    }
}
