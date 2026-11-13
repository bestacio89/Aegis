package org.aegis.worker

import com.google.gson.JsonParser
import org.jetbrains.kotlin.cli.common.ExitCode
import org.jetbrains.kotlin.cli.common.messages.MessageRenderer
import org.jetbrains.kotlin.cli.common.messages.PrintingMessageCollector
import org.jetbrains.kotlin.cli.jvm.K2JVMCompiler
import org.jetbrains.kotlin.cli.common.arguments.K2JVMCompilerArguments
import org.jetbrains.kotlin.config.Services
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Test
import java.nio.file.Files
import java.nio.file.Path

class KotlinCompilerIntegrationTests {
    @Test
    fun `K2 IR extraction preserves declarations inheritance and resolved calls`() {
        val workspace = Files.createTempDirectory("aegis-kotlin-k2-")
        val source = workspace.resolve("Application.kt")
        val destination = workspace.resolve("classes")
        val evidence = workspace.resolve("semantic.json")
        Files.createDirectories(destination)
        Files.writeString(
            source,
            """
            package sample

            interface Executable {
                fun execute()
            }

            data class User(val id: String)

            class Repository {
                fun find(): User = User("user-1")
            }

            class Service(private val repository: Repository) : Executable {
                override fun execute() {
                    repository.find()
                }
            }

            fun run(service: Service) {
                service.execute()
            }
            """.trimIndent()
        )

        val pluginPath = Path.of(
            KotlinWorkerPluginRegistrar::class.java.protectionDomain.codeSource.location.toURI()
        ).toString()
        val stdlibPath = Path.of(
            Unit::class.java.protectionDomain.codeSource.location.toURI()
        ).toString()
        val arguments = K2JVMCompilerArguments().apply {
            this.destination = destination.toString()
            this.classpath = stdlibPath
            this.noStdlib = true
            this.noReflect = true
            this.jvmTarget = "21"
            this.languageVersion = "2.4"
            this.apiVersion = "2.4"
            this.pluginClasspaths = arrayOf(pluginPath)
            this.freeArgs = listOf(source.toString())
        }

        val previousOutput = System.getProperty("aegis.kotlin.semantic.output")
        val previousProject = System.getProperty("aegis.kotlin.semantic.project")
        try {
            System.setProperty("aegis.kotlin.semantic.output", evidence.toString())
            System.setProperty("aegis.kotlin.semantic.project", workspace.toString())

            val messages = PrintingMessageCollector(
                System.err,
                MessageRenderer.PLAIN_RELATIVE_PATHS,
                false
            )
            val exitCode = K2JVMCompiler().exec(messages, Services.EMPTY, arguments)

            assertEquals(ExitCode.OK, exitCode)
            assertTrue(Files.exists(evidence), "The IR extension must emit semantic evidence.")

            val result = JsonParser.parseString(Files.readString(evidence)).asJsonObject
            val symbols = result.getAsJsonArray("symbols")
            val relationships = result.getAsJsonArray("relationships")

            assertTrue(symbols.any { it.asJsonObject.get("qualifiedName").asString == "sample.Service" })
            assertTrue(symbols.any { it.asJsonObject.get("qualifiedName").asString == "sample.User" })
            assertTrue(symbols.any { it.asJsonObject.get("kind").asInt == 12 })
            assertTrue(relationships.any { it.asJsonObject.get("kind").asInt == 3 })
            assertTrue(relationships.any { it.asJsonObject.get("kind").asInt == 4 })
        } finally {
            if (previousOutput == null) System.clearProperty("aegis.kotlin.semantic.output")
            else System.setProperty("aegis.kotlin.semantic.output", previousOutput)
            if (previousProject == null) System.clearProperty("aegis.kotlin.semantic.project")
            else System.setProperty("aegis.kotlin.semantic.project", previousProject)
            workspace.toFile().deleteRecursively()
        }
    }
}
