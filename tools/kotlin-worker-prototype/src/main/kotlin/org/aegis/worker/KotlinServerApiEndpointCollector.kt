package org.aegis.worker

import com.google.gson.JsonArray
import com.google.gson.JsonObject
import org.jetbrains.kotlin.ir.IrElement
import org.jetbrains.kotlin.ir.declarations.IrClass
import org.jetbrains.kotlin.ir.declarations.IrDeclarationOrigin
import org.jetbrains.kotlin.ir.declarations.IrFile
import org.jetbrains.kotlin.ir.declarations.IrModuleFragment
import org.jetbrains.kotlin.ir.declarations.IrParameterKind
import org.jetbrains.kotlin.ir.declarations.IrSimpleFunction
import org.jetbrains.kotlin.ir.expressions.IrCall
import org.jetbrains.kotlin.ir.expressions.IrConst
import org.jetbrains.kotlin.ir.expressions.IrConstructorCall
import org.jetbrains.kotlin.ir.expressions.IrExpression
import org.jetbrains.kotlin.ir.expressions.IrGetEnumValue
import org.jetbrains.kotlin.ir.expressions.IrSpreadElement
import org.jetbrains.kotlin.ir.expressions.IrVararg
import org.jetbrains.kotlin.ir.util.fqNameWhenAvailable
import org.jetbrains.kotlin.ir.symbols.UnsafeDuringIrConstructionAPI
import org.jetbrains.kotlin.ir.visitors.IrVisitorVoid
import org.jetbrains.kotlin.ir.visitors.acceptChildrenVoid
import java.nio.file.Files
import java.nio.file.Path
import java.security.MessageDigest

@OptIn(UnsafeDuringIrConstructionAPI::class)
internal class KotlinServerApiEndpointCollector(
    private val modulePath: String
) {
    private val endpoints = JsonArray()

    fun collect(module: IrModuleFragment): JsonArray {
        module.acceptChildrenVoid(EndpointVisitor())
        module.acceptChildrenVoid(KtorEndpointVisitor())
        return endpoints
    }

    private inner class EndpointVisitor : IrVisitorVoid() {
        private var currentFile: IrFile? = null

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
            if (declaration.origin == IrDeclarationOrigin.DEFINED) {
                discoverSpringEndpoint(declaration, declaration.parent as? IrClass, currentFile)
            }
            declaration.acceptChildrenVoid(this)
        }
    }

    private inner class KtorEndpointVisitor : IrVisitorVoid() {
        private var currentFile: IrFile? = null
        private var currentHandler: String? = null
        private val routeStack = mutableListOf<RouteValue>()
        private val routeEvidenceStack = mutableListOf<String>()

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
            if (declaration.origin == IrDeclarationOrigin.DEFINED) {
                currentHandler = handlerIdentity(declaration)
            }
            declaration.acceptChildrenVoid(this)
            currentHandler = previous
        }

        override fun visitCall(expression: IrCall) {
            val functionName = ktorFunctionName(expression)
            when {
                functionName == "routing" -> expression.acceptChildrenVoid(this)
                functionName == "route" -> {
                    val route = ktorPath(expression)
                    routeStack.add(route)
                    routeEvidenceStack.add("route path=[" + route.value + "]")
                    try {
                        expression.acceptChildrenVoid(this)
                    } finally {
                        routeEvidenceStack.removeAt(routeEvidenceStack.lastIndex)
                        routeStack.removeAt(routeStack.lastIndex)
                    }
                }
                functionName != null && KTOR_HTTP_METHODS.containsKey(functionName) -> {
                    val file = currentFile
                    if (file != null) {
                        discoverKtorEndpoint(expression, functionName, file, currentHandler, routeStack, routeEvidenceStack)
                    }
                    expression.acceptChildrenVoid(this)
                }
                else -> expression.acceptChildrenVoid(this)
            }
        }
    }

    private fun discoverSpringEndpoint(
        function: IrSimpleFunction,
        controller: IrClass?,
        file: IrFile?
    ) {
        val methodMappings = mappingAnnotations(function.annotations)
        if (methodMappings.isEmpty() || file == null) return

        val classMappings = controller?.let { mappingAnnotations(it.annotations) }.orEmpty()
        val classRoutes = if (classMappings.isEmpty()) {
            listOf(RouteValue("", false))
        } else {
            classMappings.flatMap(::routeValues)
        }
        val methodRoutes = methodMappings.flatMap(::routeValues)
        val httpMethods = methodMappings.flatMap(::httpMethods).distinct().sorted()
        val handler = handlerIdentity(function)
        val declarationEvidence = sourceSnippet(file, function.startOffset, function.endOffset)
            ?: function.toString()
        val sourcePath = file.fileEntry.name
        val startLine = file.fileEntry.getLineNumber(function.startOffset) + 1
        val startColumn = file.fileEntry.getColumnNumber(function.startOffset) + 1

        var routeIndex = 0
        classRoutes.forEach { classRoute ->
            methodRoutes.forEach { methodRoute ->
                val route = combineRoutes(classRoute.value, methodRoute.value)
                val status = when {
                    classRoute.dynamic || methodRoute.dynamic -> 3
                    classRoute.partiallyResolved || methodRoute.partiallyResolved -> 2
                    else -> 1
                }
                val evidenceIdentity = sourcePath + "|" + function.startOffset + "|" + handler + "|" + routeIndex
                val endpoint = JsonObject().apply {
                    addProperty("evidenceId", "kotlin-spring:" + stableHash(evidenceIdentity))
                    addProperty("language", "Kotlin")
                    addProperty("framework", "Spring MVC/WebFlux")
                    addProperty("providerId", "kotlin-k2-spring")
                    addProperty("modulePath", modulePath)
                    addProperty("sourceFile", sourcePath)
                    addProperty("startLine", startLine)
                    addProperty("startColumn", startColumn)
                    addProperty("handlerIdentity", handler)
                    add("httpMethods", JsonArray().also { values -> httpMethods.forEach(values::add) })
                    addProperty("routeTemplate", route)
                    addProperty("resolutionStatus", status)
                    add("routeCompositionEvidence", JsonArray().also { values ->
                        classMappings.forEach { values.add(annotationEvidence(it)) }
                        methodMappings.forEach { values.add(annotationEvidence(it)) }
                    })
                    addProperty("declarationEvidence", declarationEvidence)
                }
                endpoints.add(endpoint)
                routeIndex++
            }
        }
    }

    private fun discoverKtorEndpoint(
        call: IrCall,
        functionName: String,
        file: IrFile,
        enclosingHandler: String?,
        routeStack: List<RouteValue>,
        routeEvidenceStack: List<String>
    ) {
        val method = KTOR_HTTP_METHODS.getValue(functionName)
        val path = ktorPath(call)
        val prefix = composeRoutePrefix(routeStack)
        val route = if (path.partiallyResolved) prefix.value else combineRoutes(prefix.value, path.value)
        val status = when {
            prefix.dynamic || path.dynamic -> 3
            prefix.partiallyResolved || path.partiallyResolved -> 2
            else -> 1
        }
        val sourcePath = file.fileEntry.name
        val handler = (enclosingHandler ?: sourcePath) + "#ktor-" + functionName + "@" + call.startOffset
        val evidenceIdentity = sourcePath + "|" + call.startOffset + "|" + handler
        val declarationEvidence = sourceSnippet(file, call.startOffset, call.endOffset) ?: call.toString()
        val endpointEvidence = functionName + " path=[" + path.value + "]"
        val endpoint = JsonObject().apply {
            addProperty("evidenceId", "kotlin-ktor:" + stableHash(evidenceIdentity))
            addProperty("language", "Kotlin")
            addProperty("framework", "Ktor")
            addProperty("providerId", "kotlin-k2-ktor")
            addProperty("modulePath", modulePath)
            addProperty("sourceFile", sourcePath)
            addProperty("startLine", file.fileEntry.getLineNumber(call.startOffset) + 1)
            addProperty("startColumn", file.fileEntry.getColumnNumber(call.startOffset) + 1)
            addProperty("handlerIdentity", handler)
            add("httpMethods", JsonArray().also { values -> values.add(method) })
            addProperty("routeTemplate", route)
            addProperty("resolutionStatus", status)
            add("routeCompositionEvidence", JsonArray().also { values ->
                routeEvidenceStack.forEach(values::add)
                values.add(endpointEvidence)
            })
            addProperty("declarationEvidence", declarationEvidence)
        }
        endpoints.add(endpoint)
    }

    private fun ktorFunctionName(call: IrCall): String? {
        val name = call.symbol.owner.fqNameWhenAvailable?.asString() ?: return null
        if (!name.startsWith(KTOR_ROUTING_PREFIX)) return null
        return name.substringAfterLast('.')
    }

    private fun ktorPath(call: IrCall): RouteValue {
        val parameters = call.symbol.owner.parameters
        val namedIndex = parameters.indexOfFirst { it.name.asString() == "path" }
        val argumentIndex = if (namedIndex >= 0) {
            namedIndex
        } else {
            parameters.indexOfFirst { it.kind == IrParameterKind.Regular }
        }
        val expression = (if (argumentIndex < 0) null else call.arguments.getOrNull(argumentIndex))
            ?: return RouteValue("", false, true)
        val literal = (expression as? IrConst)?.value as? String
        val typeName = expression.type.toString()
        return when {
            literal != null -> RouteValue(literal, false)
            typeName.startsWith("kotlin.String") -> RouteValue(expression.toString(), true)
            else -> RouteValue(expression.toString() + " [type=" + typeName + "]", false, true)
        }
    }

    private fun composeRoutePrefix(routeStack: List<RouteValue>): RouteValue {
        var prefix = RouteValue("", false)
        routeStack.forEach { route ->
            val value = if (route.partiallyResolved) prefix.value else combineRoutes(prefix.value, route.value)
            prefix = RouteValue(
                value,
                prefix.dynamic || route.dynamic,
                prefix.partiallyResolved || route.partiallyResolved
            )
        }
        return prefix
    }

    private fun mappingAnnotations(annotations: List<IrConstructorCall>): List<IrConstructorCall> =
        annotations.filter { annotation ->
            annotationClassName(annotation) in SPRING_MAPPING_ANNOTATIONS
        }

    private fun annotationClassName(annotation: IrConstructorCall): String? =
        (annotation.symbol.owner.parent as? IrClass)?.fqNameWhenAvailable?.asString()

    private fun routeValues(annotation: IrConstructorCall): List<RouteValue> {
        val pathValues = stringValues(annotation, "path")
        val valueValues = stringValues(annotation, "value")
        val selected = pathValues.filter { it.value.isNotEmpty() }
            .ifEmpty { valueValues.filter { it.value.isNotEmpty() } }
            .ifEmpty { pathValues.ifEmpty { valueValues } }
        return selected.ifEmpty { listOf(RouteValue("", false)) }
    }

    private fun stringValues(annotation: IrConstructorCall, parameterName: String): List<RouteValue> {
        val expression = annotationArgument(annotation, parameterName) ?: return emptyList()
        val expressions = when (expression) {
            is IrVararg -> expression.elements.mapNotNull { element ->
                when (element) {
                    is IrExpression -> element
                    is IrSpreadElement -> element.expression
                    else -> null
                }
            }
            else -> listOf(expression)
        }

        return expressions.map { item ->
            val value = (item as? IrConst)?.value as? String
            if (value == null) {
                RouteValue(item.toString(), true)
            } else {
                RouteValue(value, value.contains("$" + "{") || value.contains("#" + "{"))
            }
        }
    }

    private fun httpMethods(annotation: IrConstructorCall): List<String> {
        val annotationName = annotationClassName(annotation)
        val fixed = when (annotationName) {
            "org.springframework.web.bind.annotation.GetMapping" -> "GET"
            "org.springframework.web.bind.annotation.PostMapping" -> "POST"
            "org.springframework.web.bind.annotation.PutMapping" -> "PUT"
            "org.springframework.web.bind.annotation.DeleteMapping" -> "DELETE"
            "org.springframework.web.bind.annotation.PatchMapping" -> "PATCH"
            else -> null
        }
        if (fixed != null) return listOf(fixed)
        if (annotationName != "org.springframework.web.bind.annotation.RequestMapping") return emptyList()

        val expression = annotationArgument(annotation, "method") ?: return emptyList()
        val expressions = when (expression) {
            is IrVararg -> expression.elements.mapNotNull { element ->
                when (element) {
                    is IrExpression -> element
                    is IrSpreadElement -> element.expression
                    else -> null
                }
            }
            else -> listOf(expression)
        }
        return expressions.mapNotNull { item ->
            val method = (item as? IrGetEnumValue)?.symbol?.owner?.name?.asString()
                ?: return@mapNotNull null
            method.takeIf { it in HTTP_METHODS }
        }
    }

    private fun annotationArgument(annotation: IrConstructorCall, name: String): IrExpression? {
        val parameters = annotation.symbol.owner.parameters
        val index = parameters.indexOfFirst {
            it.kind == IrParameterKind.Regular && it.name.asString() == name
        }
        return if (index < 0) null else annotation.arguments.getOrNull(index)
    }

    private fun annotationEvidence(annotation: IrConstructorCall): String {
        val name = annotationClassName(annotation) ?: "unresolved-annotation"
        val routes = routeValues(annotation).joinToString(",") { it.value }
        val methods = httpMethods(annotation).joinToString(",")
        return name + " route=[" + routes + "] methods=[" + methods + "]"
    }

    private fun handlerIdentity(function: IrSimpleFunction): String {
        val name = function.fqNameWhenAvailable?.asString() ?: function.name.asString()
        val receiver = function.parameters.firstOrNull { it.kind.name == "ExtensionReceiver" }
            ?.type?.toString()
        val parameters = function.parameters
            .filter { it.kind == IrParameterKind.Regular }
            .joinToString(",") { it.type.toString() }
        return name + "(" + (receiver?.let { it + ";" } ?: "") + parameters + ")"
    }

    private fun combineRoutes(prefix: String, route: String): String {
        val left = prefix.trim().trim('/')
        val right = route.trim().trim('/')
        return when {
            left.isEmpty() -> route
            right.isEmpty() -> prefix
            else -> "/" + left + "/" + right
        }
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

    private data class RouteValue(
        val value: String,
        val dynamic: Boolean,
        val partiallyResolved: Boolean = false
    )

    private companion object {
        const val KTOR_ROUTING_PREFIX = "io.ktor.server.routing."
        val KTOR_HTTP_METHODS = mapOf(
            "get" to "GET",
            "post" to "POST",
            "put" to "PUT",
            "delete" to "DELETE",
            "patch" to "PATCH",
            "head" to "HEAD",
            "options" to "OPTIONS"
        )
        val SPRING_MAPPING_ANNOTATIONS = setOf(
            "org.springframework.web.bind.annotation.RequestMapping",
            "org.springframework.web.bind.annotation.GetMapping",
            "org.springframework.web.bind.annotation.PostMapping",
            "org.springframework.web.bind.annotation.PutMapping",
            "org.springframework.web.bind.annotation.DeleteMapping",
            "org.springframework.web.bind.annotation.PatchMapping"
        )

        val HTTP_METHODS = setOf("GET", "POST", "PUT", "DELETE", "PATCH", "HEAD", "OPTIONS", "TRACE")
    }
}
