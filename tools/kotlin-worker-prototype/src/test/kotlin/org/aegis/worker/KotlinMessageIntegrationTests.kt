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

class KotlinMessageIntegrationTests {
    @Test
    fun `K2 discovers grounded Spring Kafka and JMS operations and preserves dynamic destinations`() {
        val workspace = Files.createTempDirectory("aegis-kotlin-message-")
        val destination = workspace.resolve("classes")
        val evidence = workspace.resolve("semantic.json")
        val sources = listOf(
            workspace.resolve("org/springframework/kafka/annotation/KafkaListener.kt"),
            workspace.resolve("org/springframework/kafka/core/KafkaTemplate.kt"),
            workspace.resolve("org/springframework/jms/annotation/JmsListener.kt"),
            workspace.resolve("org/springframework/jms/core/JmsTemplate.kt"),
            workspace.resolve("sample/OrderMessaging.kt")
        )
        sources.forEach { Files.createDirectories(it.parent) }
        Files.writeString(sources[0], """
            package org.springframework.kafka.annotation
            annotation class KafkaListener(
                val topics: Array<String> = [],
                val topicPattern: String = ""
            )
        """.trimIndent())
        Files.writeString(sources[1], """
            package org.springframework.kafka.core
            class KafkaTemplate<K, V> {
                fun send(topic: String, data: V) {}
                fun send(topic: String, key: K, data: V) {}
                fun sendDefault(data: V) {}
            }
        """.trimIndent())
        Files.writeString(sources[2], """
            package org.springframework.jms.annotation
            annotation class JmsListener(val destination: String)
        """.trimIndent())
        Files.writeString(sources[3], """
            package org.springframework.jms.core
            class JmsTemplate {
                fun convertAndSend(destinationName: String, message: Any) {}
                fun convertAndSend(destinationName: String, message: Any, postProcessor: Any) {}
            }
        """.trimIndent())
        Files.writeString(sources[4], """
            package sample

            import org.springframework.kafka.annotation.KafkaListener
            import org.springframework.kafka.core.KafkaTemplate
            import org.springframework.jms.annotation.JmsListener
            import org.springframework.jms.core.JmsTemplate

            data class OrderSubmitted(val id: String)

            class OrderMessaging(
                private val kafka: KafkaTemplate<String, OrderSubmitted>,
                private val jms: JmsTemplate
            ) {
                fun publish(order: OrderSubmitted) {
                    kafka.send("orders", order)
                    kafka.send("orders.keyed", "key", order)
                    kafka.sendDefault(order)
                    jms.convertAndSend("orders.queue", order)
                    jms.convertAndSend("orders.post", order, Any())
                    OtherTemplate().send("not-a-topic", order)
                }

                @KafkaListener(topics = ["orders"])
                fun consumeKafka(order: OrderSubmitted) {}

                @KafkaListener(topics = ["\${'
                fun consumeDynamicKafka(order: OrderSubmitted) {}

                @KafkaListener(topicPattern = "orders.*")
                fun consumePatternKafka(order: OrderSubmitted) {}

                @JmsListener(destination = "orders.queue")
                fun consumeJms(order: OrderSubmitted) {}
            }

            class OtherTemplate {
                fun send(topic: String, message: Any) {}
            }
        """.trimIndent())

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
            this.moduleName = "message-integration-fixture"
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
            val messagesFound = result.getAsJsonArray("messageIntegrations").map { it.asJsonObject }
            assertEquals(9, messagesFound.size)

            val kafkaConsumers = messagesFound.filter {
                it.get("framework").asString == "Spring Kafka"
                    && it.get("operation").asInt == 1
            }
            assertEquals(3, kafkaConsumers.size)
            val literalKafka = kafkaConsumers.single {
                it.get("destinationIdentity").asString == "orders"
            }
            assertEquals(0, literalKafka.get("resolutionStatus").asInt)
            val dynamicKafka = kafkaConsumers.single {
                it.get("destinationIdentity").isJsonNull
            }
            assertEquals(2, dynamicKafka.get("resolutionStatus").asInt)
            assertTrue(dynamicKafka.get("destinationExpression").asString.contains("messaging.orders.topic"))
            val patternKafka = kafkaConsumers.single {
                it.get("destinationExpression").asString.contains("orders.*")
            }
            assertTrue(patternKafka.get("destinationIdentity").isJsonNull)
            assertEquals(2, patternKafka.get("resolutionStatus").asInt)

            val jmsConsumer = messagesFound.single {
                it.get("framework").asString == "Spring JMS"
                    && it.get("operation").asInt == 1
            }
            assertEquals("orders.queue", jmsConsumer.get("destinationIdentity").asString)
            assertEquals("sample.OrderSubmitted", jmsConsumer.get("messageTypeIdentity").asString)

            val kafkaPublishers = messagesFound.filter {
                it.get("framework").asString == "Spring Kafka"
                    && it.get("operation").asInt == 0
            }
            assertEquals(3, kafkaPublishers.size)
            assertTrue(kafkaPublishers.any { it.get("destinationIdentity").asString == "orders.keyed" })
            val defaultPublisher = kafkaPublishers.single { it.get("destinationIdentity").isJsonNull }
            assertEquals(2, defaultPublisher.get("resolutionStatus").asInt)

            val jmsPublishers = messagesFound.filter {
                it.get("framework").asString == "Spring JMS"
                    && it.get("operation").asInt == 0
            }
            assertEquals(2, jmsPublishers.size)
            assertTrue(jmsPublishers.any { it.get("destinationIdentity").asString == "orders.post" })
            assertTrue(messagesFound.all { it.get("language").asString == "Kotlin" })
            assertTrue(messagesFound.all { it.get("modulePath").asString == workspace.toString() })
            assertTrue(messagesFound.all { it.get("destinationKind").asInt == 0 })
            assertFalse(messagesFound.any { it.get("declarationEvidence").asString.contains("not-a-topic") })
        } finally {
            if (previousOutput == null) System.clearProperty("aegis.kotlin.semantic.output")
            else System.setProperty("aegis.kotlin.semantic.output", previousOutput)
            if (previousProject == null) System.clearProperty("aegis.kotlin.semantic.project")
            else System.setProperty("aegis.kotlin.semantic.project", previousProject)
            workspace.toFile().deleteRecursively()
        }
    }
}
}{messaging.orders.topic}"])
                fun consumeDynamicKafka(order: OrderSubmitted) {}

                @KafkaListener(topicPattern = "orders.*")
                fun consumePatternKafka(order: OrderSubmitted) {}

                @JmsListener(destination = "orders.queue")
                fun consumeJms(order: OrderSubmitted) {}
            }

            class OtherTemplate {
                fun send(topic: String, message: Any) {}
            }
        """.trimIndent())

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
            this.moduleName = "message-integration-fixture"
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
            val messagesFound = result.getAsJsonArray("messageIntegrations").map { it.asJsonObject }
            assertEquals(9, messagesFound.size)

            val kafkaConsumers = messagesFound.filter {
                it.get("framework").asString == "Spring Kafka"
                    && it.get("operation").asInt == 1
            }
            assertEquals(3, kafkaConsumers.size)
            val literalKafka = kafkaConsumers.single {
                it.get("destinationIdentity").asString == "orders"
            }
            assertEquals(0, literalKafka.get("resolutionStatus").asInt)
            val dynamicKafka = kafkaConsumers.single {
                it.get("destinationIdentity").isJsonNull
            }
            assertEquals(2, dynamicKafka.get("resolutionStatus").asInt)
            assertTrue(dynamicKafka.get("destinationExpression").asString.contains("messaging.orders.topic"))
            val patternKafka = kafkaConsumers.single {
                it.get("destinationExpression").asString.contains("orders.*")
            }
            assertTrue(patternKafka.get("destinationIdentity").isJsonNull)
            assertEquals(2, patternKafka.get("resolutionStatus").asInt)

            val jmsConsumer = messagesFound.single {
                it.get("framework").asString == "Spring JMS"
                    && it.get("operation").asInt == 1
            }
            assertEquals("orders.queue", jmsConsumer.get("destinationIdentity").asString)
            assertEquals("sample.OrderSubmitted", jmsConsumer.get("messageTypeIdentity").asString)

            val kafkaPublishers = messagesFound.filter {
                it.get("framework").asString == "Spring Kafka"
                    && it.get("operation").asInt == 0
            }
            assertEquals(3, kafkaPublishers.size)
            assertTrue(kafkaPublishers.any { it.get("destinationIdentity").asString == "orders.keyed" })
            val defaultPublisher = kafkaPublishers.single { it.get("destinationIdentity").isJsonNull }
            assertEquals(2, defaultPublisher.get("resolutionStatus").asInt)

            val jmsPublishers = messagesFound.filter {
                it.get("framework").asString == "Spring JMS"
                    && it.get("operation").asInt == 0
            }
            assertEquals(2, jmsPublishers.size)
            assertTrue(jmsPublishers.any { it.get("destinationIdentity").asString == "orders.post" })
            assertTrue(messagesFound.all { it.get("language").asString == "Kotlin" })
            assertTrue(messagesFound.all { it.get("modulePath").asString == workspace.toString() })
            assertTrue(messagesFound.all { it.get("destinationKind").asInt == 0 })
            assertFalse(messagesFound.any { it.get("declarationEvidence").asString.contains("not-a-topic") })
        } finally {
            if (previousOutput == null) System.clearProperty("aegis.kotlin.semantic.output")
            else System.setProperty("aegis.kotlin.semantic.output", previousOutput)
            if (previousProject == null) System.clearProperty("aegis.kotlin.semantic.project")
            else System.setProperty("aegis.kotlin.semantic.project", previousProject)
            workspace.toFile().deleteRecursively()
        }
    }
}
