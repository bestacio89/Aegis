package org.aegis.worker

import com.google.gson.JsonArray
import com.google.gson.JsonObject
import org.jetbrains.kotlin.ir.IrElement
import org.jetbrains.kotlin.ir.declarations.IrClass
import org.jetbrains.kotlin.ir.declarations.IrFile
import org.jetbrains.kotlin.ir.declarations.IrModuleFragment
import org.jetbrains.kotlin.ir.declarations.IrSimpleFunction
import org.jetbrains.kotlin.ir.expressions.IrCall
import org.jetbrains.kotlin.ir.expressions.IrConst
import org.jetbrains.kotlin.ir.expressions.IrExpression
import org.jetbrains.kotlin.ir.expressions.IrGetEnumValue
import org.jetbrains.kotlin.ir.expressions.IrGetField
import org.jetbrains.kotlin.ir.expressions.IrStringConcatenation
import org.jetbrains.kotlin.ir.util.fqNameWhenAvailable
import org.jetbrains.kotlin.ir.visitors.IrVisitorVoid
import org.jetbrains.kotlin.ir.visitors.acceptChildrenVoid
import java.nio.file.Files
import java.nio.file.Path
import java.security.MessageDigest

internal class KotlinApiClientRequestCollector(
    private val modulePath: String
) {
    private val requests = JsonArray()

    fun collect(module: IrModuleFragment): JsonArray {
        module.acceptChildrenVoid(RequestVisitor())
        return requests
    }

    private inner class RequestVisitor : IrVisitorVoid() {
        private var currentFile: IrFile? = null
        private var currentCaller: String? = null

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
            val previous = currentCaller
            currentCaller = declaration.fqNameWhenAvailable?.asString()
                ?: declaration.name.asString()
            declaration.acceptChildrenVoid(this)
            currentCaller = previous
        }

        override fun visitCall(expression: IrCall) {
            val file = currentFile
            val function = expression.symbol.owner
            val qualifiedName = function.fqNameWhenAvailable?.asString()
            if (file != null && qualifiedName != null) {
                discoverKtorRequest(expression, qualifiedName, file, currentCaller)
                discoverRestTemplateRequest(expression, qualifiedName, file, currentCaller)
                discoverWebClientRequest(expression, qualifiedName, file, currentCaller)
            }
            expression.acceptChildrenVoid(this)
        }
    }

    private fun discoverKtorRequest(call: IrCall, qualifiedName: String, file: IrFile, callerIdentity: String?) {
        if (!qualifiedName.startsWith(KTOR_CLIENT_REQUEST_PREFIX)) return
        val functionName = qualifiedName.substringAfterLast('.')
        if (functionName !in KTOR_METHODS) return

        val uri = firstValueArgument(call, hasExtensionReceiver = true)
        val method = KTOR_METHODS[functionName]
        addEvidence(
            call = call,
            file = file,
            qualifiedName = qualifiedName,
            framework = "Ktor Client",
            providerId = "kotlin-k2-ktor-client",
            httpMethod = method,
            uriExpression = uri,
            evidence = listOf(
                "Kotlin IR resolved the call to the Ktor client request API '$qualifiedName'."
            ),
            callerIdentity = callerIdentity
        )
    }

    private fun discoverRestTemplateRequest(call: IrCall, qualifiedName: String, file: IrFile, callerIdentity: String?) {
        if (!qualifiedName.startsWith(SPRING_REST_TEMPLATE_PREFIX)) return
        val functionName = qualifiedName.substringAfterLast('.')
        val method = when (functionName) {
            "getForObject", "getForEntity" -> "GET"
            "postForObject", "postForEntity", "postForLocation" -> "POST"
            "put" -> "PUT"
            "delete" -> "DELETE"
            "patchForObject" -> "PATCH"
            "exchange", "execute" -> enumArgument(call, 1)
            else -> return
        }
        addEvidence(
            call = call,
            file = file,
            qualifiedName = qualifiedName,
            framework = "Spring RestTemplate",
            providerId = "kotlin-k2-spring-resttemplate",
            httpMethod = method,
            uriExpression = firstValueArgument(call),
            evidence = listOf(
                "Kotlin IR resolved the call to the Spring RestTemplate API '$qualifiedName'."
            ) + if (functionName in setOf("exchange", "execute") && method == null) {
                listOf("The HTTP method argument is not a statically resolved enum constant.")
            } else {
                emptyList()
            },
            callerIdentity = callerIdentity
        )
    }

    private fun discoverWebClientRequest(call: IrCall, qualifiedName: String, file: IrFile, callerIdentity: String?) {
        if (!qualifiedName.startsWith(SPRING_WEB_CLIENT_PREFIX)
            || !qualifiedName.endsWith(".uri")) return

        val receiver = call.dispatchReceiver
        val builderCall = receiver as? IrCall
        val builderFunction = builderCall?.symbol?.owner
        val builderName = builderFunction?.name?.asString()
        val builderQualifiedName = builderFunction?.fqNameWhenAvailable?.asString()
        val method = when (builderName) {
            "get" -> "GET"
            "post" -> "POST"
            "put" -> "PUT"
            "patch" -> "PATCH"
            "delete" -> "DELETE"
            "head" -> "HEAD"
            "method" -> builderCall?.let { enumArgument(it, 0) }
            else -> null
        }
        val evidence = mutableListOf(
            "Kotlin IR resolved the URI declaration to the Spring WebClient API '$qualifiedName'."
        )
        if (builderQualifiedName == null || !builderQualifiedName.startsWith(SPRING_WEB_CLIENT_PREFIX)) {
            evidence.add("The WebClient request method could not be recovered from the URI builder receiver.")
        } else {
            evidence.add("URI builder receiver: $builderQualifiedName.")
        }
        if (method == null) {
            evidence.add("The HTTP method is not statically resolved from the WebClient builder.")
        }

        addEvidence(
            call = call,
            file = file,
            qualifiedName = qualifiedName,
            framework = "Spring WebClient",
            providerId = "kotlin-k2-spring-webclient",
            httpMethod = method,
            uriExpression = firstValueArgument(call),
            evidence = evidence,
            callerIdentity = callerIdentity
        )
    }

    private fun addEvidence(
        call: IrCall,
        file: IrFile,
        qualifiedName: String,
        framework: String,
        providerId: String,
        httpMethod: String?,
        uriExpression: IrExpression?,
        evidence: List<String>,
        callerIdentity: String?
    ) {
        val sourcePath = file.fileEntry.name
        val uriText = uriExpression?.let { sourceSnippet(file, it.startOffset, it.endOffset) ?: it.toString() }
        val uriStatus = when {
            uriExpression == null -> RESOLUTION_DYNAMIC
            (uriExpression as? IrConst)?.value is String -> RESOLUTION_RESOLVED
            uriExpression is IrStringConcatenation || hasKotlinTemplateInterpolation(uriText) ->
                RESOLUTION_PARTIALLY_RESOLVED
            else -> RESOLUTION_DYNAMIC
        }
        val resolutionEvidence = JsonArray().also { values ->
            evidence.forEach(values::add)
            when (uriStatus) {
                RESOLUTION_RESOLVED -> values.add("Request URI is a string literal.")
                RESOLUTION_PARTIALLY_RESOLVED -> values.add("Request URI contains a statically visible string template with dynamic interpolation.")
                else -> values.add("Request URI is not a statically resolved string literal.")
            }
        }
        val declarationEvidence = sourceSnippet(file, call.startOffset, call.endOffset) ?: call.toString()
        val identity = sourcePath + "|" + call.startOffset + "|" + qualifiedName + "|" + providerId

        requests.add(JsonObject().apply {
            addProperty("evidenceId", "kotlin-client:" + stableHash(identity))
            addProperty("language", "Kotlin")
            addProperty("framework", framework)
            addProperty("providerId", providerId)
            addProperty("modulePath", modulePath)
            addProperty("sourceFile", sourcePath)
            addProperty("startLine", file.fileEntry.getLineNumber(call.startOffset) + 1)
            addProperty("startColumn", file.fileEntry.getColumnNumber(call.startOffset) + 1)
            addProperty("callerIdentity", callerIdentity ?: "<top-level>")
            addProperty("httpMethod", httpMethod)
            addProperty("requestUriExpression", uriText)
            add("baseAddressExpression", com.google.gson.JsonNull.INSTANCE)
            addProperty("resolutionStatus", uriStatus)
            add("resolutionEvidence", resolutionEvidence)
            addProperty("declarationEvidence", declarationEvidence)
        })
    }

    private fun firstValueArgument(
        call: IrCall,
        hasExtensionReceiver: Boolean = false
    ): IrExpression? = valueArgument(call, 0, hasExtensionReceiver)

    private fun valueArgument(
        call: IrCall,
        valueArgumentIndex: Int,
        hasExtensionReceiver: Boolean = false
    ): IrExpression? {
        val receiverCount = (if (call.dispatchReceiver != null) 1 else 0)
            + (if (hasExtensionReceiver) 1 else 0)
        return call.arguments
            .filterNotNull()
            .drop(receiverCount)
            .getOrNull(valueArgumentIndex)
    }

    private fun enumArgument(call: IrCall, valueArgumentIndex: Int): String? =
        valueArgument(call, valueArgumentIndex)?.let(::springHttpMethodConstant)

    private fun springHttpMethodConstant(expression: IrExpression): String? =
        when (expression) {
            is IrGetEnumValue -> {
                val enumClass = expression.symbol.owner.parent as? IrClass
                expression.symbol.owner.name.asString().takeIf {
                    enumClass?.fqNameWhenAvailable?.asString() == SPRING_HTTP_METHOD
                }
            }
            is IrGetField -> {
                val enumClass = expression.symbol.owner.parent as? IrClass
                expression.symbol.owner.name.asString().takeIf {
                    enumClass?.fqNameWhenAvailable?.asString() == SPRING_HTTP_METHOD
                }
            }
            else -> null
        }

    private fun hasKotlinTemplateInterpolation(source: String?): Boolean {
        if (source.isNullOrEmpty()) return false
        for (index in source.indices) {
            if (source[index] != '$') continue
            if (index > 0 && source[index - 1] == '\\') continue
            val next = source.getOrNull(index + 1) ?: continue
            if (next == '{' || Character.isJavaIdentifierStart(next)) return true
        }
        return false
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

    private companion object {
        const val KTOR_CLIENT_REQUEST_PREFIX = "io.ktor.client.request."
        const val SPRING_REST_TEMPLATE_PREFIX = "org.springframework.web.client.RestTemplate."
        const val SPRING_HTTP_METHOD = "org.springframework.http.HttpMethod"
        const val SPRING_WEB_CLIENT_PREFIX = "org.springframework.web.reactive.function.client.WebClient."
        const val RESOLUTION_RESOLVED = 0
        const val RESOLUTION_PARTIALLY_RESOLVED = 1
        const val RESOLUTION_DYNAMIC = 2

        val KTOR_METHODS = mapOf(
            "get" to "GET",
            "post" to "POST",
            "put" to "PUT",
            "delete" to "DELETE",
            "patch" to "PATCH",
            "head" to "HEAD",
            "options" to "OPTIONS",
            "request" to null
        )
    }
}
