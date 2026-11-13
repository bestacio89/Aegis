package org.aegis.worker

import com.google.gson.JsonArray
import com.google.gson.JsonObject
import org.jetbrains.kotlin.ir.IrElement
import org.jetbrains.kotlin.ir.declarations.IrClass
import org.jetbrains.kotlin.ir.declarations.IrConstructor
import org.jetbrains.kotlin.ir.declarations.IrFile
import org.jetbrains.kotlin.ir.declarations.IrDeclaration
import org.jetbrains.kotlin.ir.declarations.IrDeclarationWithName
import org.jetbrains.kotlin.ir.declarations.IrDeclarationWithVisibility
import org.jetbrains.kotlin.ir.declarations.IrFunction
import org.jetbrains.kotlin.ir.declarations.IrModuleFragment
import org.jetbrains.kotlin.ir.declarations.IrProperty
import org.jetbrains.kotlin.ir.declarations.IrSimpleFunction
import org.jetbrains.kotlin.ir.declarations.IrTypeAlias
import org.jetbrains.kotlin.ir.expressions.IrCall
import org.jetbrains.kotlin.ir.expressions.IrConstructorCall
import org.jetbrains.kotlin.ir.declarations.IrParameterKind
import org.jetbrains.kotlin.ir.declarations.IrDeclarationOrigin
import org.jetbrains.kotlin.ir.types.IrSimpleType
import org.jetbrains.kotlin.ir.types.IrStarProjection
import org.jetbrains.kotlin.ir.types.IrType
import org.jetbrains.kotlin.ir.types.IrTypeProjection
import org.jetbrains.kotlin.ir.types.classOrNull
import org.jetbrains.kotlin.ir.types.isMarkedNullable
import org.jetbrains.kotlin.ir.types.classFqName
import org.jetbrains.kotlin.types.Variance
import org.jetbrains.kotlin.ir.symbols.UnsafeDuringIrConstructionAPI
import org.jetbrains.kotlin.ir.util.fqNameWhenAvailable
import org.jetbrains.kotlin.ir.visitors.IrVisitorVoid
import org.jetbrains.kotlin.ir.visitors.acceptChildrenVoid
import java.nio.file.Files
import java.nio.file.Path
import java.util.ArrayDeque
import java.util.LinkedHashMap

internal @OptIn(UnsafeDuringIrConstructionAPI::class)
class KotlinSemanticCollector(
    private val projectPath: String,
    private val isTestCode: Boolean,
    private val generatedRoots: List<Path>
) {
    private val symbols = LinkedHashMap<String, JsonObject>()
    private val relationships = LinkedHashMap<String, JsonObject>()
    private val propertyAccessors = HashMap<Any, String>()

    fun collect(module: IrModuleFragment) {
        module.acceptChildrenVoid(DeclarationCollector())
        module.acceptChildrenVoid(RelationshipCollector())
    }

    fun toJson(analyzedFileCount: Int): JsonObject {
        val result = JsonObject()
        result.addProperty("engineId", "kotlin-k2-ir")
        result.addProperty("engineVersion", "2.4.21")
        result.addProperty("projectPath", projectPath)
        result.addProperty("status", 2)
        result.addProperty("analyzedFileCount", analyzedFileCount)
        result.addProperty("skippedFileCount", 0)
        result.add("symbols", JsonArray().also { array -> symbols.values.forEach(array::add) })
        result.add("relationships", JsonArray().also { array -> relationships.values.forEach(array::add) })
        result.add("diagnostics", JsonArray())
        return result
    }

    private inner class DeclarationCollector : IrVisitorVoid() {
        private var currentFile: IrFile? = null
        private val owners = ArrayDeque<String>()

        override fun visitElement(element: IrElement) {
            element.acceptChildrenVoid(this)
        }

        override fun visitFile(declaration: IrFile) {
            val previous = currentFile
            currentFile = declaration
            declaration.acceptChildrenVoid(this)
            currentFile = previous
        }

        override fun visitClass(declaration: IrClass) {
            if (declaration.origin != IrDeclarationOrigin.DEFINED) return
            val id = classId(declaration)
            val symbolKind = when (declaration.kind.name) {
                "INTERFACE" -> 5
                "ENUM_CLASS" -> 7
                "ANNOTATION_CLASS" -> 8
                else -> 4
            }
            addSymbol(
                id = id,
                name = declaration.name.asString(),
                qualifiedName = declaration.fqNameWhenAvailable?.asString() ?: declaration.name.asString(),
                kind = symbolKind,
                declaration = declaration,
                containingSymbolId = owners.lastOrNull()
            )
            owners.addLast(id)
            declaration.acceptChildrenVoid(this)
            owners.removeLast()
        }

        override fun visitTypeAlias(declaration: IrTypeAlias) {
            if (declaration.origin != IrDeclarationOrigin.DEFINED) return
            val qualifiedName = declaration.fqNameWhenAvailable?.asString() ?: declaration.name.asString()
            addSymbol(
                id = "kotlin:typealias:$qualifiedName",
                name = declaration.name.asString(),
                qualifiedName = qualifiedName,
                kind = 14,
                declaration = declaration,
                containingSymbolId = owners.lastOrNull()
            )
            declaration.acceptChildrenVoid(this)
        }

        override fun visitProperty(declaration: IrProperty) {
            if (declaration.origin != IrDeclarationOrigin.DEFINED) return
            val id = propertyId(declaration)
            addSymbol(
                id = id,
                name = declaration.name.asString(),
                qualifiedName = declaration.fqNameWhenAvailable?.asString() ?: declaration.name.asString(),
                kind = 12,
                declaration = declaration,
                containingSymbolId = owners.lastOrNull()
            )
            declaration.getter?.let { propertyAccessors[it.symbol] = id }
            declaration.setter?.let { propertyAccessors[it.symbol] = id }
            owners.addLast(id)
            declaration.acceptChildrenVoid(this)
            owners.removeLast()
        }

        override fun visitSimpleFunction(declaration: IrSimpleFunction) {
            if (declaration.origin != IrDeclarationOrigin.DEFINED) return
            if (propertyAccessors.containsKey(declaration.symbol)) {
                declaration.acceptChildrenVoid(this)
                return
            }
            val id = functionId(declaration)
            addSymbol(
                id = id,
                name = declaration.name.asString(),
                qualifiedName = declaration.fqNameWhenAvailable?.asString() ?: id,
                kind = 9,
                declaration = declaration,
                containingSymbolId = owners.lastOrNull()
            )
            owners.addLast(id)
            declaration.acceptChildrenVoid(this)
            owners.removeLast()
        }

        override fun visitConstructor(declaration: IrConstructor) {
            if (declaration.origin != IrDeclarationOrigin.DEFINED) return
            val id = functionId(declaration)
            addSymbol(
                id = id,
                name = "<init>",
                qualifiedName = declaration.fqNameWhenAvailable?.asString() ?: id,
                kind = 10,
                declaration = declaration,
                containingSymbolId = owners.lastOrNull()
            )
            owners.addLast(id)
            declaration.acceptChildrenVoid(this)
            owners.removeLast()
        }

        private fun addSymbol(
            id: String,
            name: String,
            qualifiedName: String,
            kind: Int,
            declaration: org.jetbrains.kotlin.ir.declarations.IrDeclaration,
            containingSymbolId: String?
        ) {
            if (symbols.containsKey(id)) return
            val file = currentFile
            val offset = declaration.startOffset
            val isSource = file != null
                && offset >= 0
                && declaration.origin == IrDeclarationOrigin.DEFINED
            val symbol = JsonObject()
            symbol.addProperty("id", id)
            symbol.addProperty("kind", kind)
            symbol.addProperty("name", name)
            symbol.addProperty("qualifiedName", qualifiedName)
            symbol.addProperty("containerName", file?.packageFqName?.asString().orEmpty())
            symbol.addProperty("containerKind", "Package")
            symbol.addProperty("language", "Kotlin")
            symbol.addProperty("projectPath", projectPath)
            symbol.addProperty("filePath", if (isSource) file.fileEntry.name else null)
            symbol.addProperty("startLine", if (isSource) file.fileEntry.getLineNumber(offset) + 1 else null)
            symbol.addProperty("startColumn", if (isSource) file.fileEntry.getColumnNumber(offset) + 1 else null)
            symbol.addProperty("containingSymbolId", containingSymbolId)
            symbol.addProperty("origin", when {
                !isSource -> 3
                isGenerated(file) -> 2
                else -> 1
            })
            symbol.addProperty("isTestCode", isTestCode)
            symbol.addProperty(
                "isPublic",
                isPublic(declaration)
            )
            symbol.addProperty(
                "visibility",
                (declaration as? IrDeclarationWithVisibility)?.visibility?.name
                    ?.replaceFirstChar(Char::uppercase)
                    ?: "Unknown"
            )
            symbol.addProperty("declarationKind", declarationKind(declaration))
            symbol.addProperty("receiverType", receiverType(declaration))
            symbol.addProperty(
                "expandedType",
                (declaration as? IrTypeAlias)?.expandedType?.let(::typeIdentity)
            )
            symbol.add("modifiers", JsonArray().apply {
                declarationModifiers(declaration).forEach(::add)
            })
            symbol.add("provenance", provenance(file, offset))
            symbols[id] = symbol
        }
    }

    private inner class RelationshipCollector : IrVisitorVoid() {
        private var currentFile: IrFile? = null
        private val owners = ArrayDeque<String>()

        override fun visitElement(element: IrElement) {
            element.acceptChildrenVoid(this)
        }

        override fun visitFile(declaration: IrFile) {
            val previous = currentFile
            currentFile = declaration
            declaration.acceptChildrenVoid(this)
            currentFile = previous
        }

        override fun visitClass(declaration: IrClass) {
            if (declaration.origin != IrDeclarationOrigin.DEFINED) return
            val sourceId = classId(declaration)
            owners.addLast(sourceId)
            addAnnotationRelationships(sourceId, declaration, currentFile)
            declaration.superTypes.forEach { superType ->
                val targetClass = superType.classOrNull?.owner
                val targetName = targetClass?.fqNameWhenAvailable?.asString()
                    ?: superType.classFqName?.asString()
                    ?: return@forEach
                if (targetName == "kotlin.Any") return@forEach

                val targetId = targetClass?.let(::classId) ?: "kotlin:type:$targetName"
                val targetIsInterface = targetClass?.kind?.name in setOf("INTERFACE", "ANNOTATION_CLASS")
                val relationshipKind = when {
                    declaration.kind.name == "INTERFACE" -> 2
                    targetIsInterface -> 3
                    else -> 2
                }
                addExternalTypeIfNeeded(
                    targetId,
                    targetName,
                    targetIsInterface,
                    targetClass?.origin == IrDeclarationOrigin.IR_EXTERNAL_JAVA_DECLARATION_STUB
                )
                addRelationship(sourceId, targetId, relationshipKind, declaration.startOffset, currentFile)
            }
            declaration.acceptChildrenVoid(this)
            owners.removeLast()
        }

        override fun visitProperty(declaration: IrProperty) {
            if (declaration.origin != IrDeclarationOrigin.DEFINED) return
            val sourceId = propertyId(declaration)
            owners.addLast(sourceId)
            addAnnotationRelationships(sourceId, declaration, currentFile)
            declaration.acceptChildrenVoid(this)
            owners.removeLast()
        }

        override fun visitSimpleFunction(declaration: IrSimpleFunction) {
            if (declaration.origin != IrDeclarationOrigin.DEFINED) return
            if (propertyAccessors.containsKey(declaration.symbol)) {
                declaration.acceptChildrenVoid(this)
                return
            }
            val sourceId = functionId(declaration)
            owners.addLast(sourceId)
            addAnnotationRelationships(sourceId, declaration, currentFile)
            declaration.acceptChildrenVoid(this)
            owners.removeLast()
        }

        override fun visitConstructor(declaration: IrConstructor) {
            if (declaration.origin != IrDeclarationOrigin.DEFINED) return
            val sourceId = functionId(declaration)
            owners.addLast(sourceId)
            addAnnotationRelationships(sourceId, declaration, currentFile)
            declaration.acceptChildrenVoid(this)
            owners.removeLast()
        }

        override fun visitCall(expression: IrCall) {
            val sourceId = owners.lastOrNull()
            val target = expression.symbol.owner
            val targetId = functionId(target)
            val propertyId = propertyAccessors[expression.symbol]
            addExternalFunctionIfNeeded(targetId, target)
            if (sourceId != null) {
                addRelationship(
                    sourceId,
                    propertyId ?: targetId,
                    if (propertyId == null) 4 else 5,
                    expression.startOffset,
                    currentFile
                )
            }
            expression.acceptChildrenVoid(this)
        }

        override fun visitConstructorCall(expression: IrConstructorCall) {
            val sourceId = owners.lastOrNull()
            val target = expression.symbol.owner
            val targetClass = target.parent as? IrClass
            val annotationClass = targetClass?.takeIf { it.kind.name == "ANNOTATION_CLASS" }
            val isAnnotation = annotationClass != null
            val targetId = annotationClass?.let(::classId) ?: functionId(target)
            if (annotationClass != null) {
                addExternalTypeIfNeeded(
                    targetId,
                    annotationClass.fqNameWhenAvailable?.asString() ?: annotationClass.name.asString(),
                    true,
                    annotationClass.origin == IrDeclarationOrigin.IR_EXTERNAL_JAVA_DECLARATION_STUB
                )
            } else {
                addExternalFunctionIfNeeded(targetId, target)
            }
            if (sourceId != null)
                addRelationship(sourceId, targetId, if (isAnnotation) 7 else 4, expression.startOffset, currentFile)
            expression.acceptChildrenVoid(this)
        }
    }

    private fun addAnnotationRelationships(
        sourceId: String,
        declaration: IrDeclaration,
        file: IrFile?
    ) {
        declaration.annotations.forEach { annotation ->
            val annotationClass = annotation.symbol.owner.parent as? IrClass ?: return@forEach
            val qualifiedName = annotationClass.fqNameWhenAvailable?.asString()
                ?: annotationClass.name.asString()
            val targetId = classId(annotationClass)
            addExternalTypeIfNeeded(
                targetId,
                qualifiedName,
                true,
                annotationClass.origin == IrDeclarationOrigin.IR_EXTERNAL_JAVA_DECLARATION_STUB
            )
            addRelationship(sourceId, targetId, 7, annotation.startOffset, file)
        }
    }

    private fun addExternalFunctionIfNeeded(id: String, declaration: IrFunction) {
        if (symbols.containsKey(id)) return
        val name = if (declaration is IrConstructor) "<init>" else declaration.name.asString()
        val symbol = JsonObject()
        symbol.addProperty("id", id)
        symbol.addProperty("kind", if (declaration is IrConstructor) 10 else 9)
        symbol.addProperty("name", name)
        symbol.addProperty("qualifiedName", declaration.fqNameWhenAvailable?.asString() ?: id)
        symbol.addProperty("containerName", declaration.parent.toString())
        symbol.addProperty("containerKind", "Package")
        symbol.addProperty(
            "language",
            if (declaration.origin == IrDeclarationOrigin.IR_EXTERNAL_JAVA_DECLARATION_STUB) "Java" else "Kotlin"
        )
        symbol.addProperty("projectPath", projectPath)
        symbol.add("filePath", com.google.gson.JsonNull.INSTANCE)
        symbol.add("startLine", com.google.gson.JsonNull.INSTANCE)
        symbol.add("startColumn", com.google.gson.JsonNull.INSTANCE)
        symbol.add("containingSymbolId", com.google.gson.JsonNull.INSTANCE)
        symbol.addProperty("origin", 3)
        symbol.addProperty("isTestCode", isTestCode)
        symbol.addProperty("isPublic", declaration.visibility.name == "public")
        symbol.add("provenance", JsonArray())
        symbols[id] = symbol
    }

    private fun addExternalTypeIfNeeded(
        id: String,
        qualifiedName: String,
        isInterface: Boolean,
        isJava: Boolean
    ) {
        if (symbols.containsKey(id)) return
        val symbol = JsonObject()
        symbol.addProperty("id", id)
        symbol.addProperty("kind", if (isInterface) 5 else 4)
        symbol.addProperty("name", qualifiedName.substringAfterLast('.'))
        symbol.addProperty("qualifiedName", qualifiedName)
        symbol.addProperty("containerName", qualifiedName.substringBeforeLast('.', ""))
        symbol.addProperty("containerKind", "Package")
        symbol.addProperty("language", if (isJava) "Java" else "Kotlin")
        symbol.addProperty("projectPath", projectPath)
        symbol.add("filePath", com.google.gson.JsonNull.INSTANCE)
        symbol.add("startLine", com.google.gson.JsonNull.INSTANCE)
        symbol.add("startColumn", com.google.gson.JsonNull.INSTANCE)
        symbol.add("containingSymbolId", com.google.gson.JsonNull.INSTANCE)
        symbol.addProperty("origin", 3)
        symbol.addProperty("isTestCode", isTestCode)
        symbol.addProperty("isPublic", true)
        symbol.add("provenance", JsonArray())
        symbols[id] = symbol
    }

    private fun addRelationship(sourceId: String, targetId: String, kind: Int, offset: Int, file: IrFile?) {
        val key = "$sourceId|$kind|$targetId"
        val relationship = relationships.getOrPut(key) {
            JsonObject().apply {
                addProperty("id", key)
                addProperty("sourceSymbolId", sourceId)
                addProperty("targetSymbolId", targetId)
                add("unresolvedTarget", com.google.gson.JsonNull.INSTANCE)
                addProperty("kind", kind)
                addProperty("resolutionStatus", 1)
                addProperty("isTestCode", isTestCode)
                add("provenance", JsonArray())
            }
        }
        val evidence = provenance(file, offset).firstOrNull()?.asJsonObject ?: return
        val evidenceArray = relationship.getAsJsonArray("provenance")
        val evidenceKey = "${evidence.get("filePath").asString}|${evidence.get("startLine").asInt}|${evidence.get("startColumn").asInt}"
        val exists = evidenceArray.any { item ->
            val existing = item.asJsonObject
            "${existing.get("filePath").asString}|${existing.get("startLine").asInt}|${existing.get("startColumn").asInt}" == evidenceKey
        }
        if (!exists) evidenceArray.add(evidence)
    }

    private fun isGenerated(file: IrFile?): Boolean {
        val filePath = file?.fileEntry?.name?.let { Path.of(it).toAbsolutePath().normalize() } ?: return false
        return generatedRoots.any { root -> filePath.startsWith(root) }
    }

    private fun provenance(file: IrFile?, offset: Int): JsonArray {
        val result = JsonArray()
        if (file == null || offset < 0) return result
        val item = JsonObject()
        item.addProperty("analyzerId", "kotlin-k2-ir")
        item.addProperty("source", 2)
        item.addProperty("filePath", file.fileEntry.name)
        item.addProperty("startLine", file.fileEntry.getLineNumber(offset) + 1)
        item.addProperty("startColumn", file.fileEntry.getColumnNumber(offset) + 1)
        item.addProperty("confidence", 1.0)
        result.add(item)
        return result
    }

    private fun classId(declaration: IrClass): String =
        "kotlin:type:${declaration.fqNameWhenAvailable?.asString() ?: declaration.name.asString()}"

    private fun receiverType(declaration: IrDeclaration): String? = when (declaration) {
        is IrFunction -> declaration.parameters
            .firstOrNull { it.kind == IrParameterKind.ExtensionReceiver }
            ?.type
            ?.let(::typeIdentity)
        is IrProperty -> (declaration.getter ?: declaration.setter)?.parameters
            ?.firstOrNull { it.kind == IrParameterKind.ExtensionReceiver }
            ?.type
            ?.let(::typeIdentity)
        else -> null
    }

    private fun declarationModifiers(declaration: IrDeclaration): List<String> = buildList {
        when (declaration) {
            is IrClass -> {
                if (declaration.isData) add("data")
                if (declaration.isValue) add("value")
                if (declaration.modality.name == "SEALED") add("sealed")
                if (declaration.kind.name == "OBJECT") add("object")
                if (declaration.isCompanion) add("companion")
            }
            is IrSimpleFunction -> {
                if (declaration.isSuspend) add("suspend")
                if (declaration.isInline) add("inline")
                if (declaration.isOperator) add("operator")
                if (declaration.isInfix) add("infix")
                if (declaration.isTailrec) add("tailrec")
                if (declaration.isExternal) add("external")
            }
            is IrFunction -> {
                if (declaration.isInline) add("inline")
                if (declaration.isExternal) add("external")
            }
            is IrProperty -> {
                if (declaration.isConst) add("const")
                if (declaration.isLateinit) add("lateinit")
                if (declaration.isDelegated) add("delegated")
            }
        }
    }

    private fun declarationKind(declaration: IrDeclaration): String = when (declaration) {
        is IrClass -> when {
            declaration.kind.name == "INTERFACE" && declaration.modality.name == "SEALED" -> "SealedInterface"
            declaration.kind.name == "INTERFACE" -> "Interface"
            declaration.kind.name == "ENUM_CLASS" -> "EnumClass"
            declaration.kind.name == "ANNOTATION_CLASS" -> "AnnotationClass"
            declaration.kind.name == "OBJECT" && declaration.isCompanion -> "CompanionObject"
            declaration.kind.name == "OBJECT" -> "Object"
            declaration.isData -> "DataClass"
            declaration.isValue -> "ValueClass"
            declaration.modality.name == "SEALED" -> "SealedClass"
            else -> "Class"
        }
        is IrProperty -> if (receiverType(declaration) != null) "ExtensionProperty" else "Property"
        is IrTypeAlias -> "TypeAlias"
        is IrConstructor -> "Constructor"
        is IrSimpleFunction -> if (receiverType(declaration) != null) "ExtensionFunction" else "Function"
        else -> declaration.javaClass.simpleName
    }

    private fun propertyId(declaration: IrProperty): String {
        val accessor = declaration.getter ?: declaration.setter
        val receiver = accessor?.parameters
            ?.firstOrNull { it.kind == IrParameterKind.ExtensionReceiver }
            ?.type
            ?.let(::typeIdentity)
        val receiverIdentity = receiver?.let { "($it)" }.orEmpty()
        return "kotlin:property:${declaration.fqNameWhenAvailable?.asString() ?: declaration.name.asString()}$receiverIdentity"
    }

    private fun functionId(declaration: IrFunction): String {
        val name = declaration.fqNameWhenAvailable?.asString() ?: declaration.name.asString()
        val parameters = declaration.parameters
        val receiver = parameters.firstOrNull { it.kind == IrParameterKind.ExtensionReceiver }
            ?.type
            ?.let(::typeIdentity)
        val parameterTypes = parameters
            .filter { it.kind == IrParameterKind.Regular || it.kind == IrParameterKind.Context }
            .joinToString(",") { typeIdentity(it.type) }
        val suspendMarker = if ((declaration as? IrSimpleFunction)?.isSuspend == true) "suspend" else null
        val signature = listOfNotNull(receiver, parameterTypes.takeIf(String::isNotEmpty), suspendMarker)
            .joinToString("|")
        return "kotlin:function:$name($signature)"
    }

    private fun typeIdentity(type: IrType): String {
        val simpleType = type as? IrSimpleType ?: return type.toString()
        val classifierOwner = simpleType.classifier.owner as? IrDeclarationWithName
        val classifierName = classifierOwner?.fqNameWhenAvailable?.asString()
            ?: classifierOwner?.name?.asString()
            ?: simpleType.classifier.toString()
        val arguments = simpleType.arguments.joinToString(",") { argument ->
            when (argument) {
                is IrStarProjection -> "*"
                is IrTypeProjection -> {
                    val variance = when (argument.variance) {
                        Variance.IN_VARIANCE -> "in "
                        Variance.OUT_VARIANCE -> "out "
                        Variance.INVARIANT -> ""
                    }
                    variance + typeIdentity(argument.type)
                }
                else -> argument.toString()
            }
        }
        val nullability = if (simpleType.isMarkedNullable()) "?" else ""
        return if (arguments.isEmpty()) {
            classifierName + nullability
        } else {
            "$classifierName<$arguments>$nullability"
        }
    }

    private fun isPublic(declaration: IrDeclaration): Boolean =
        (declaration as? IrDeclarationWithVisibility)?.visibility?.name == "public"

}

