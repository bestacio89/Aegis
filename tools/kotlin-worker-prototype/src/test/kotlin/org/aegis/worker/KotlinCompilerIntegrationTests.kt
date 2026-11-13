package org.aegis.worker

import com.google.gson.JsonParser
import org.jetbrains.kotlin.cli.common.ExitCode
import org.jetbrains.kotlin.cli.common.messages.MessageRenderer
import org.jetbrains.kotlin.cli.common.messages.PrintingMessageCollector
import org.jetbrains.kotlin.cli.jvm.K2JVMCompiler
import org.jetbrains.kotlin.cli.common.arguments.K2JVMCompilerArguments
import org.jetbrains.kotlin.config.Services
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertFalse
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Test
import java.nio.file.Files
import java.nio.file.Path
import javax.tools.ToolProvider

class KotlinCompilerIntegrationTests {
    @Test
    fun `K2 extraction preserves declarations calls properties extensions overloads and Java interop`() {
        val workspace = Files.createTempDirectory("aegis-kotlin-k2-")
        val source = workspace.resolve("Application.kt")
        val javaSourceRoot = workspace.resolve("java")
        val javaSource = javaSourceRoot.resolve("interop/JavaRepository.java")
        val destination = workspace.resolve("classes")
        val evidence = workspace.resolve("semantic.json")
        Files.createDirectories(destination)
        Files.createDirectories(javaSource.parent)
        Files.writeString(
            javaSource,
            """
            package interop;

            public final class JavaRepository {
                public String load() {
                    return "loaded";
                }
            }
            """.trimIndent()
        )
        Files.writeString(
            javaSourceRoot.resolve("interop/JavaContract.java"),
            """
            package interop;

            public interface JavaContract {
                void verify();
            }
            """.trimIndent()
        )
        Files.writeString(
            source,
            """
            package sample

            import interop.JavaContract
            import interop.JavaRepository

            interface Executable {
                fun execute()
            }

            interface DerivedExecutable : Executable

            annotation class Audited

            typealias UserIdentifier = String

            data class User(val id: UserIdentifier)

            class Service(private val repository: JavaRepository) : Executable, JavaContract {
                override fun execute() {
                    repository.load()
                }

                override fun verify() {
                    repository.load()
                }
            }

            @Audited
            class AuditedService

            fun User.label(prefix: String): String = prefix + id

            val User.label: String
                get() = id

            val String.label: String
                get() = this

            @get:JvmName("getUserLabels")
            val List<User>.labels: String
                get() = joinToString()

            @get:JvmName("getStringLabels")
            val List<String>.labels: String
                get() = joinToString()

            @JvmName("selectUsers")
            fun List<User>.select(): User = first()

            @JvmName("selectStrings")
            fun List<String>.select(): String = first()

            fun readId(user: User): String = user.id

            fun overload(value: String): String = value

            fun overload(value: Int): String = value.toString()

            fun exercise(user: User, service: Service) {
                service.execute()
                service.execute()
                user.label("user")
                overload("text")
                overload(42)
                readId(user)
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
            this.javaSourceRoots = arrayOf(javaSourceRoot.toString())
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
            val symbols = result.getAsJsonArray("symbols").map { it.asJsonObject }
            val relationships = result.getAsJsonArray("relationships").map { it.asJsonObject }

            assertTrue(symbols.any { it.get("qualifiedName").asString == "sample.Service" })
            assertTrue(symbols.any { it.get("qualifiedName").asString == "sample.User" })
            val userSymbol = symbols.single { it.get("qualifiedName").asString == "sample.User" }
            assertEquals("DataClass", userSymbol.get("declarationKind").asString)
            assertTrue(userSymbol.getAsJsonArray("modifiers").any { it.asString == "data" })
            assertTrue(symbols.any {
                it.get("kind").asInt == 8 &&
                    it.get("qualifiedName").asString == "sample.Audited"
            }, "Kotlin annotation classes must retain the normalized annotation symbol kind.")
            assertTrue(symbols.any {
                it.get("kind").asInt == 14 &&
                    it.get("qualifiedName").asString == "sample.UserIdentifier" &&
                    it.get("declarationKind").asString == "TypeAlias" &&
                    it.get("expandedType").asString == "kotlin.String"
            }, "Kotlin type aliases must remain explicit source declarations after IR generation.")
            assertTrue(symbols.any {
                it.get("kind").asInt == 12 &&
                    it.get("qualifiedName").asString == "sample.User.id"
            })
            val extensionPropertyIds = symbols
                .filter { it.get("kind").asInt == 12 && it.get("qualifiedName").asString == "sample.label" }
                .map { it.get("id").asString }
                .distinct()
            assertEquals(2, extensionPropertyIds.size, "Extension property identity must include the receiver type.")
            val extensionPropertyReceivers = symbols
                .filter { it.get("kind").asInt == 12 && it.get("qualifiedName").asString == "sample.label" }
                .map { it.get("receiverType").asString }
                .toSet()
            assertEquals(setOf("sample.User", "kotlin.String"), extensionPropertyReceivers)

            val genericExtensionPropertyIds = symbols
                .filter { it.get("kind").asInt == 12 && it.get("qualifiedName").asString == "sample.labels" }
                .map { it.get("id").asString }
                .distinct()
            assertEquals(
                2,
                genericExtensionPropertyIds.size,
                "Generic receiver arguments must participate in extension-property identity."
            )

            val genericExtensionFunctionIds = symbols
                .filter { it.get("kind").asInt == 9 && it.get("qualifiedName").asString == "sample.select" }
                .map { it.get("id").asString }
                .distinct()
            assertEquals(
                2,
                genericExtensionFunctionIds.size,
                "Generic receiver arguments must participate in extension-function identity."
            )

            assertTrue(symbols.any {
                it.get("kind").asInt == 9 &&
                    it.get("qualifiedName").asString == "sample.label" &&
                    it.get("declarationKind").asString == "ExtensionFunction" &&
                    it.get("receiverType").asString == "sample.User"
            })
            assertFalse(
                symbols.any {
                    it.get("qualifiedName").asString.startsWith("sample.User.")
                        && it.get("name").asString in setOf("copy", "component1", "equals", "hashCode", "toString")
                },
                "Compiler-generated data-class members must not be reported as source declarations."
            )
            assertFalse(
                symbols.any { it.get("name").asString == "getId" },
                "Compiler-generated property accessors must not become duplicate source symbols."
            )

            assertTrue(relationships.any {
                it.get("kind").asInt == 3 &&
                    it.get("sourceSymbolId").asString == "kotlin:type:sample.Service" &&
                    it.get("targetSymbolId").asString == "kotlin:type:interop.JavaContract"
            }, "A class implementing an external Java interface must be classified as Implements.")
            assertTrue(relationships.any {
                it.get("kind").asInt == 2 &&
                    it.get("sourceSymbolId").asString == "kotlin:type:sample.DerivedExecutable" &&
                    it.get("targetSymbolId").asString == "kotlin:type:sample.Executable"
            }, "An interface extending another interface must be classified as Inherits.")
            assertTrue(relationships.any {
                it.get("kind").asInt == 7 &&
                    it.get("sourceSymbolId").asString == "kotlin:type:sample.AuditedService" &&
                    it.get("targetSymbolId").asString == "kotlin:type:sample.Audited"
            }, "Annotation use must remain distinct from ordinary constructor calls.")
            val repeatedCall = relationships.single {
                it.get("kind").asInt == 4
                    && it.get("sourceSymbolId").asString == "kotlin:function:sample.exercise(sample.User,sample.Service)"
                    && it.get("targetSymbolId").asString == "kotlin:function:sample.Service.execute()"
            }
            assertEquals(
                2,
                repeatedCall.getAsJsonArray("provenance").size(),
                "Repeated calls should aggregate into one stable relationship with both source locations."
            )
            assertEquals(
                "${repeatedCall.get("sourceSymbolId").asString}|4|${repeatedCall.get("targetSymbolId").asString}",
                repeatedCall.get("id").asString
            )
            assertTrue(relationships.any {
                it.get("kind").asInt == 5 &&
                    it.get("targetSymbolId").asString == "kotlin:property:sample.User.id"
            }, "A Kotlin property read must resolve to the property declaration.")
            assertTrue(relationships.any {
                it.get("kind").asInt == 4 &&
                    it.get("targetSymbolId").asString.contains("sample.label")
            }, "An extension call must resolve to its extension declaration.")

            val overloadIds = symbols
                .filter { it.get("qualifiedName").asString.contains("sample.overload") }
                .map { it.get("id").asString }
                .distinct()
            assertEquals(2, overloadIds.size, "Overloaded functions need distinct deterministic identities.")

            assertTrue(symbols.any {
                it.get("language").asString == "Java" &&
                    it.get("qualifiedName").asString.contains("JavaRepository")
            }, "Kotlin analysis must preserve the language identity of Java source targets.")

            verifyJavaCanConsumeKotlinOutput(workspace, destination, stdlibPath)
        } finally {
            if (previousOutput == null) System.clearProperty("aegis.kotlin.semantic.output")
            else System.setProperty("aegis.kotlin.semantic.output", previousOutput)
            if (previousProject == null) System.clearProperty("aegis.kotlin.semantic.project")
            else System.setProperty("aegis.kotlin.semantic.project", previousProject)
            workspace.toFile().deleteRecursively()
        }
    }

    private fun verifyJavaCanConsumeKotlinOutput(
        workspace: Path,
        kotlinOutput: Path,
        stdlibPath: String
    ) {
        val javaCompiler = ToolProvider.getSystemJavaCompiler()
            ?: error("A JDK compiler is required for Java/Kotlin interoperability validation.")
        val javaSource = workspace.resolve("java-caller/interop/JavaCaller.java")
        val javaOutput = workspace.resolve("java-caller-classes")
        Files.createDirectories(javaSource.parent)
        Files.createDirectories(javaOutput)
        Files.writeString(
            javaSource,
            """
            package interop;

            public final class JavaCaller {
                public String call(sample.Service service, sample.User user) {
                    service.execute();
                    service.verify();
                    return sample.ApplicationKt.label(user, "id") + user.getId();
                }
            }
            """.trimIndent()
        )

        val fileManager = javaCompiler.getStandardFileManager(null, null, null)
        try {
            val compilationUnits = fileManager.getJavaFileObjectsFromPaths(
                listOf(
                    javaSource,
                    workspace.resolve("java/interop/JavaRepository.java"),
                    workspace.resolve("java/interop/JavaContract.java")
                )
            )
            val options = listOf(
                "-classpath",
                listOf(kotlinOutput.toString(), stdlibPath).joinToString(System.getProperty("path.separator")),
                "-d",
                javaOutput.toString()
            )
            val task = javaCompiler.getTask(null, fileManager, null, options, null, compilationUnits)
            assertTrue(task.call(), "Java source must resolve Kotlin classes, top-level functions and properties.")
        } finally {
            fileManager.close()
        }
    }
}
