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
import org.jetbrains.kotlin.ir.expressions.IrConst
import org.jetbrains.kotlin.ir.expressions.IrConstructorCall
import org.jetbrains.kotlin.ir.expressions.IrExpression
import org.jetbrains.kotlin.ir.expressions.IrGetEnumValue
import org.jetbrains.kotlin.ir.expressions.IrSpreadElement
import org.jetbrains.kotlin.ir.expressions.IrVararg
import org.jetbrains.kotlin.ir.util.fqNameWhenAvailable
import org.jetbrains.kotlin.ir.visitors.IrVisitorVoid
import org.jetbrains.kotlin.ir.visitors.acceptChildrenVoid
import java.nio.file.Files
import java.nio.file.Path
import java.security.MessageDigest

internal class KotlinServerApiEndpointCollector(
    private val modulePath: String
) {
    private val endpoints = JsonArray()

    fun collect(module: IrModuleFragment): JsonArray {
        module.acceptChildrenVoid(EndpointVisitor())
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
                val status = if (classRoute.dynamic || methodRoute.dynamic) 3 else 1
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

    private data class RouteValue(val value: String, val dynamic: Boolean)

    private companion object {
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
