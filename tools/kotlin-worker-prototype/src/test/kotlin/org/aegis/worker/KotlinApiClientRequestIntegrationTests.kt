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

class KotlinApiClientRequestIntegrationTests {
    @Test
    fun `K2 discovers grounded Ktor and Spring client requests and preserves dynamic targets`() {
        val workspace = Files.createTempDirectory("aegis-kotlin-api-client-")
        val destination = workspace.resolve("classes")
        val evidence = workspace.resolve("semantic.json")
        val sources = listOf(
            workspace.resolve("io/ktor/client/HttpClient.kt"),
            workspace.resolve("io/ktor/client/request/Requests.kt"),
            workspace.resolve("org/springframework/http/HttpMethod.kt"),
            workspace.resolve("org/springframework/web/client/RestTemplate.kt"),
            workspace.resolve("org/springframework/web/reactive/function/client/WebClient.kt"),
            workspace.resolve("sample/OrderGateway.kt")
        )
        sources.forEach { Files.createDirectories(it.parent) }

        Files.writeString(
            sources[0],
            """
            package io.ktor.client

            class HttpClient
            """.trimIndent()
        )
        Files.writeString(
            sources[1],
            """
            package io.ktor.client.request

            import io.ktor.client.HttpClient

            suspend fun HttpClient.get(urlString: String): String = urlString
            suspend fun HttpClient.post(urlString: String): String = urlString
            """.trimIndent()
        )
        Files.writeString(
            sources[2],
            """
            package org.springframework.http

            enum class HttpMethod {
                GET, POST, PUT, PATCH, DELETE
            }
            """.trimIndent()
        )
        Files.writeString(
            sources[3],
            """
            package org.springframework.web.client

            import org.springframework.http.HttpMethod

            class RestTemplate {
                fun getForObject(url: String, responseType: Class<*>): Any? = null
                fun exchange(url: String, method: HttpMethod, request: Any?, responseType: Class<*>): Any? = null
            }
            """.trimIndent()
        )
        Files.writeString(
            sources[4],
            """
            package org.springframework.web.reactive.function.client

            class WebClient {
                fun get(): UriSpec = UriSpec()
                fun post(): UriSpec = UriSpec()

                class UriSpec {
                    fun uri(value: String): UriSpec = this
                }
            }
            """.trimIndent()
        )
        Files.writeString(
            sources[5],
            """
            package sample

            import io.ktor.client.HttpClient
            import io.ktor.client.request.get
            import io.ktor.client.request.post
            import org.springframework.http.HttpMethod
            import org.springframework.web.client.RestTemplate
            import org.springframework.web.reactive.function.client.WebClient

            class OrderGateway(
                private val restTemplate: RestTemplate,
                private val webClient: WebClient,
                private val ktorClient: HttpClient
            ) {
                suspend fun load(orderId: String) {
                    restTemplate.getForObject("/orders", Any::class.java)
                    restTemplate.exchange("/orders/${'$'}orderId", HttpMethod.GET, null, Any::class.java)
                    webClient.get().uri("/catalog")
                    ktorClient.get("/orders/${'$'}orderId")
                    ktorClient.post("/orders")
                    UnrelatedClient().get("/not-an-http-client")
                }
            }

            class UnrelatedClient {
                fun get(value: String): String = value
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
            this.moduleName = "api-client-fixture"
            this.pluginClasspaths = arrayOf(pluginPath)
            this.freeArgs = sources.map(Path::toString)
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
            val result = JsonParser.parseString(Files.readString(evidence)).asJsonObject
            val requests = result.getAsJsonArray("apiClientRequests").map { it.asJsonObject }
            assertEquals(5, requests.size)

            val restTemplateGet = requests.single {
                it.get("framework").asString == "Spring RestTemplate"
                    && it.get("declarationEvidence").asString.contains("getForObject")
            }
            assertEquals("\"/orders\"", restTemplateGet.get("requestUriExpression").asString)
            assertEquals(0, restTemplateGet.get("resolutionStatus").asInt)

            val restTemplateExchange = requests.single {
                it.get("framework").asString == "Spring RestTemplate"
                    && it.get("declarationEvidence").asString.contains("exchange")
            }
            assertEquals("GET", restTemplateExchange.get("httpMethod").asString)
            assertEquals(1, restTemplateExchange.get("resolutionStatus").asInt)
            assertTrue(restTemplateExchange.get("requestUriExpression").asString.contains("orderId"))

            val webClient = requests.single { it.get("framework").asString == "Spring WebClient" }
            assertEquals("GET", webClient.get("httpMethod").asString)
            assertEquals("\"/catalog\"", webClient.get("requestUriExpression").asString)
            assertEquals(0, webClient.get("resolutionStatus").asInt)

            val ktorGet = requests.single {
                it.get("framework").asString == "Ktor Client" && it.get("httpMethod").asString == "GET"
            }
            assertEquals(1, ktorGet.get("resolutionStatus").asInt)
            assertTrue(ktorGet.get("requestUriExpression").asString.contains("orderId"))

            val ktorPost = requests.single {
                it.get("framework").asString == "Ktor Client" && it.get("httpMethod").asString == "POST"
            }
            assertEquals("\"/orders\"", ktorPost.get("requestUriExpression").asString)
            assertEquals(0, ktorPost.get("resolutionStatus").asInt)

            assertTrue(requests.all { it.get("callerIdentity").asString.contains("OrderGateway.load") })
            assertTrue(requests.all { it.get("language").asString == "Kotlin" })
            assertTrue(requests.all { it.get("modulePath").asString == workspace.toString() })
            assertTrue(requests.all { it.get("providerId").asString.startsWith("kotlin-k2-") })
            assertFalse(requests.any { it.get("declarationEvidence").asString.contains("not-an-http-client") })
        } finally {
            if (previousOutput == null) System.clearProperty("aegis.kotlin.semantic.output")
            else System.setProperty("aegis.kotlin.semantic.output", previousOutput)
            if (previousProject == null) System.clearProperty("aegis.kotlin.semantic.project")
            else System.setProperty("aegis.kotlin.semantic.project", previousProject)
            workspace.toFile().deleteRecursively()
        }
    }
}
