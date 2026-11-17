package org.aegis.worker

import com.google.gson.JsonArray
import com.google.gson.JsonObject
import org.jetbrains.kotlin.ir.IrElement
import org.jetbrains.kotlin.ir.declarations.IrClass
import org.jetbrains.kotlin.ir.declarations.IrFile
import org.jetbrains.kotlin.ir.declarations.IrModuleFragment
import org.jetbrains.kotlin.ir.declarations.IrParameterKind
import org.jetbrains.kotlin.ir.declarations.IrSimpleFunction
import org.jetbrains.kotlin.ir.expressions.IrCall
import org.jetbrains.kotlin.ir.expressions.IrConst
import org.jetbrains.kotlin.ir.expressions.IrConstructorCall
import org.jetbrains.kotlin.ir.expressions.IrExpression
import org.jetbrains.kotlin.ir.expressions.IrSpreadElement
import org.jetbrains.kotlin.ir.expressions.IrVararg
import org.jetbrains.kotlin.ir.types.classFqName
import org.jetbrains.kotlin.ir.util.fqNameWhenAvailable
import org.jetbrains.kotlin.ir.visitors.IrVisitorVoid
import org.jetbrains.kotlin.ir.visitors.acceptChildrenVoid
import java.nio.file.Files
import java.nio.file.Path
import java.security.MessageDigest

internal class KotlinMessageIntegrationCollector(
    private val modulePath: String
) {
    private val evidence = JsonArray()

    fun collect(module: IrModuleFragment): JsonArray {
        module.acceptChildrenVoid(MessageVisitor())
        return evidence
    }

    private inner class MessageVisitor : IrVisitorVoid() {
        private var currentFile: IrFile? = null
        private var currentHandler: String? = null

        override fun visitElement(element: IrElement) {
            element.acceptChildrenVoid(this)
        }

        override fun visitFile(declaration: IrFile) {
            val previous = currentFile
            currentFile = declaration
            declaration.acceptChildrenVoid(this)
            currentFile = previous
        }

        override fun visitSimpleFunction(declaration: IrSimpleFunction) {
            val previous = currentHandler
            currentHandler = handlerIdentity(declaration)
            if (declaration.origin.name == "DEFINED") {
                currentFile?.let { file ->
                    discoverKafkaListener(declaration, file)
                    discoverJmsListener(declaration, file)
                }
            }
            declaration.acceptChildrenVoid(this)
            currentHandler = previous
        }

        override fun visitCall(expression: IrCall) {
            val file = currentFile
            val qualifiedName = expression.symbol.owner.fqNameWhenAvailable?.asString()
            if (file != null && qualifiedName != null) {
                discoverKafkaSend(expression, qualifiedName, file, currentHandler)
                discoverJmsSend(expression, qualifiedName, file, currentHandler)
            }
            expression.acceptChildrenVoid(this)
        }
    }

    private fun discoverKafkaListener(function: IrSimpleFunction, file: IrFile) {
        val annotation = function.annotations.firstOrNull {
            annotationClassName(it) == KAFKA_LISTENER
        } ?: return
        val topics = annotationStrings(annotation, "topics", file)
            .filter { it.literal?.isNotEmpty() != false }
        val patterns = annotationStrings(annotation, "topicPattern", file)
            .filter { it.literal?.isNotEmpty() != false }
            .map { it.copy(literal = null, isDynamic = true) }
        val destinations = topics.ifEmpty { patterns }
        val values = destinations.ifEmpty { listOf(SourceValue(null, null, false)) }
        values.forEachIndexed { index, destination ->
            addListener(
                function, file, annotation, destination,
                "Spring Kafka", "kotlin-k2-spring-kafka", index
            )
        }
    }

    private fun discoverJmsListener(function: IrSimpleFunction, file: IrFile) {
        val annotation = function.annotations.firstOrNull {
            annotationClassName(it) == JMS_LISTENER
        } ?: return
        val destination = annotationStrings(annotation, "destination", file)
            .firstOrNull() ?: SourceValue(null, null, false)
        addListener(
            function, file, annotation, destination,
            "Spring JMS", "kotlin-k2-spring-jms", 0
        )
    }

    private fun addListener(
        function: IrSimpleFunction,
        file: IrFile,
        annotation: IrConstructorCall,
        destination: SourceValue,
        framework: String,
        providerId: String,
        destinationIndex: Int
    ) {
        val payload = function.parameters.firstOrNull { it.kind == IrParameterKind.Regular }
        val messageType = payload?.type?.classFqName?.asString()
        val handler = handlerIdentity(function)
        val status = when {
            destination.literal == null && destination.expression == null -> STATUS_UNRESOLVED
            destination.isDynamic -> STATUS_DYNAMIC
            messageType == null -> STATUS_PARTIALLY_RESOLVED
            else -> STATUS_RESOLVED
        }
        addEvidence(
            file = file,
            offset = annotation.startOffset,
            operation = OPERATION_CONSUME,
            framework = framework,
            providerId = providerId,
            messageType = messageType,
            messageExpression = payload?.name?.asString(),
            destination = destination,
            handler = handler,
            status = status,
            declarationEvidence = sourceSnippet(file, function.startOffset, function.endOffset)
                ?: function.toString(),
            identitySuffix = destinationIndex.toString()
        )
    }

    private fun discoverKafkaSend(
        call: IrCall,
        qualifiedName: String,
        file: IrFile,
        handler: String?
    ) {
        if (!qualifiedName.startsWith(KAFKA_TEMPLATE_PREFIX)) return
        val name = qualifiedName.substringAfterLast('.')
        if (name != "send" && name != "sendDefault") return
        val destination = stringArgument(call, setOf("topic", "topicName"))
            ?.let { sourceValue(it, file) } ?: SourceValue(null, null, false)
        addCallEvidence(
            call, file, "Spring Kafka", "kotlin-k2-spring-kafka",
            destination, messageArgument(call), handler
        )
    }

    private fun discoverJmsSend(
        call: IrCall,
        qualifiedName: String,
        file: IrFile,
        handler: String?
    ) {
        if (!qualifiedName.startsWith(JMS_TEMPLATE_PREFIX)
            || !qualifiedName.substringAfterLast('.').startsWith("convertAndSend")) return
        val destination = argument(call, setOf("destinationName", "destination"))
            ?.let { sourceValue(it, file) } ?: SourceValue(null, null, false)
        addCallEvidence(
            call, file, "Spring JMS", "kotlin-k2-spring-jms",
            destination, messageArgument(call), handler
        )
    }

    private fun addCallEvidence(
        call: IrCall,
        file: IrFile,
        framework: String,
        providerId: String,
        destination: SourceValue,
        payload: IrExpression?,
        handler: String?
    ) {
        val payloadType = payload?.type?.classFqName?.asString()
        val status = when {
            destination.literal == null && destination.expression == null -> STATUS_DYNAMIC
            destination.isDynamic -> STATUS_DYNAMIC
            payloadType == null -> STATUS_PARTIALLY_RESOLVED
            else -> STATUS_RESOLVED
        }
        addEvidence(
            file = file,
            offset = call.startOffset,
            operation = OPERATION_PUBLISH,
            framework = framework,
            providerId = providerId,
            messageType = payloadType,
            messageExpression = payload?.let {
                sourceSnippet(file, it.startOffset, it.endOffset) ?: it.toString()
            },
            destination = destination,
            handler = handler,
            status = status,
            declarationEvidence = sourceSnippet(file, call.startOffset, call.endOffset)
                ?: call.toString(),
            identitySuffix = call.symbol.owner.name.asString()
        )
    }

    private fun addEvidence(
        file: IrFile,
        offset: Int,
        operation: Int,
        framework: String,
        providerId: String,
        messageType: String?,
        messageExpression: String?,
        destination: SourceValue,
        handler: String?,
        status: Int,
        declarationEvidence: String,
        identitySuffix: String
    ) {
        val sourcePath = file.fileEntry.name
        val identity = "$sourcePath|$offset|$providerId|$operation|$identitySuffix"
        evidence.add(JsonObject().apply {
            addProperty("evidenceId", "kotlin-message:" + stableHash(identity))
            addProperty("language", "Kotlin")
            addProperty("framework", framework)
            addProperty("providerId", providerId)
            addProperty("modulePath", modulePath)
            addProperty("sourceFile", sourcePath)
            addProperty("startLine", file.fileEntry.getLineNumber(offset) + 1)
            addProperty("startColumn", file.fileEntry.getColumnNumber(offset) + 1)
            addProperty("operation", operation)
            addProperty("messageTypeIdentity", messageType)
            addProperty("messageTypeExpression", messageExpression)
            addProperty("destinationKind", 0)
            addProperty("destinationIdentity", destination.literal)
            addProperty("destinationExpression", destination.expression)
            addProperty("handlerIdentity", handler)
            addProperty("resolutionStatus", status)
            add("resolutionEvidence", JsonArray().apply {
                add("Kotlin K2 IR resolved the source operation to the framework declaration.")
                when (status) {
                    STATUS_RESOLVED -> add("Message type and destination are statically resolved.")
                    STATUS_DYNAMIC -> add("The destination is configured or computed dynamically; no destination identity was inferred.")
                    STATUS_PARTIALLY_RESOLVED -> add("The operation and destination are identified, but the message type is not statically resolved.")
                    else -> add("The operation is recognized, but required destination or handler evidence is missing.")
                }
            })
            addProperty("declarationEvidence", declarationEvidence)
        })
    }

    private fun messageArgument(call: IrCall): IrExpression? {
        val named = argument(call, setOf("data", "message", "payload", "record", "body"))
        if (named != null) return named
        return call.arguments.zip(call.symbol.owner.parameters)
            .filter { (value, parameter) ->
                value != null && parameter.kind == IrParameterKind.Regular
                    && parameter.name.asString() !in setOf(
                        "topic", "topicName", "key", "partition", "timestamp",
                        "headers", "postProcessor", "destinationName", "destination"
                    )
                    && parameter.type.classFqName?.asString() != "kotlin.String"
            }
            .lastOrNull()?.first
    }

    private fun stringArgument(call: IrCall, names: Set<String>): IrExpression? =
        argument(call, names)?.takeIf {
            it.type.classFqName?.asString() == "kotlin.String"
        }

    private fun argument(call: IrCall, names: Set<String>): IrExpression? =
        call.arguments.zip(call.symbol.owner.parameters)
            .firstOrNull { (value, parameter) ->
                value != null && parameter.kind == IrParameterKind.Regular
                    && parameter.name.asString() in names
            }?.first

    private fun annotationStrings(
        annotation: IrConstructorCall,
        name: String,
        file: IrFile
    ): List<SourceValue> {
        val expression = annotation.arguments.zip(annotation.symbol.owner.parameters)
            .firstOrNull { (_, parameter) ->
                parameter.kind == IrParameterKind.Regular && parameter.name.asString() == name
            }?.first ?: return emptyList()
        val values = when (expression) {
            is IrVararg -> expression.elements.mapNotNull { element ->
                when (element) {
                    is IrExpression -> element
                    is IrSpreadElement -> element.expression
                    else -> null
                }
            }
            else -> listOf(expression)
        }
        return values.map { sourceValue(it, file) }
    }

    private fun sourceValue(expression: IrExpression, file: IrFile): SourceValue {
        val text = sourceSnippet(file, expression.startOffset, expression.endOffset)
            ?: expression.toString()
        val literal = (expression as? IrConst)?.value as? String
        val resolved = literal != null
            && !literal.contains("${'$'}{")
            && !literal.contains("#{")
        return SourceValue(
            literal = literal.takeIf { resolved },
            expression = text,
            isDynamic = !resolved
        )
    }

    private fun annotationClassName(annotation: IrConstructorCall): String? =
        (annotation.symbol.owner.parent as? IrClass)?.fqNameWhenAvailable?.asString()

    private fun handlerIdentity(function: IrSimpleFunction): String {
        val name = function.fqNameWhenAvailable?.asString() ?: function.name.asString()
        val parameters = function.parameters
            .filter { it.kind == IrParameterKind.Regular }
            .joinToString(",") { it.type.classFqName?.asString() ?: it.type.toString() }
        return "$name($parameters)"
    }

    private fun sourceSnippet(file: IrFile, startOffset: Int, endOffset: Int): String? {
        if (startOffset < 0 || endOffset < startOffset) return null
        return try {
            val source = Files.readString(Path.of(file.fileEntry.name))
            if (endOffset > source.length) null else source.substring(startOffset, endOffset).trim()
        } catch (_: Exception) {
            null
        }
    }

    private fun stableHash(value: String): String =
        MessageDigest.getInstance("SHA-256")
            .digest(value.toByteArray(Charsets.UTF_8))
            .joinToString("") { "%02x".format(it) }

    private data class SourceValue(
        val literal: String?,
        val expression: String?,
        val isDynamic: Boolean
    )

    private companion object {
        const val KAFKA_LISTENER = "org.springframework.kafka.annotation.KafkaListener"
        const val JMS_LISTENER = "org.springframework.jms.annotation.JmsListener"
        const val KAFKA_TEMPLATE_PREFIX = "org.springframework.kafka.core.KafkaTemplate."
        const val JMS_TEMPLATE_PREFIX = "org.springframework.jms.core.JmsTemplate."
        const val OPERATION_PUBLISH = 0
        const val OPERATION_CONSUME = 1
        const val STATUS_RESOLVED = 0
        const val STATUS_PARTIALLY_RESOLVED = 1
        const val STATUS_DYNAMIC = 2
        const val STATUS_UNRESOLVED = 3
    }
}
