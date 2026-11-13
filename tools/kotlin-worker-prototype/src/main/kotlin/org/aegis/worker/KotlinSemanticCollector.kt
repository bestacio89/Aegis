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
import org.jetbrains.kotlin.ir.declarations.IrEnumEntry
import org.jetbrains.kotlin.ir.declarations.IrField
import org.jetbrains.kotlin.ir.declarations.IrModuleFragment
import org.jetbrains.kotlin.ir.declarations.IrProperty
import org.jetbrains.kotlin.ir.declarations.IrSimpleFunction
import org.jetbrains.kotlin.ir.declarations.IrTypeAlias
import org.jetbrains.kotlin.ir.expressions.IrBlock
import org.jetbrains.kotlin.ir.expressions.IrCall
import org.jetbrains.kotlin.ir.expressions.IrConstructorCall
import org.jetbrains.kotlin.ir.expressions.IrDelegatingConstructorCall
import org.jetbrains.kotlin.ir.expressions.IrFunctionReference
import org.jetbrains.kotlin.ir.expressions.IrPropertyReference
import org.jetbrains.kotlin.ir.expressions.IrClassReference
import org.jetbrains.kotlin.ir.expressions.IrGetField
import org.jetbrains.kotlin.ir.expressions.IrSetField
import org.jetbrains.kotlin.ir.expressions.IrGetEnumValue
import org.jetbrains.kotlin.ir.expressions.IrGetObjectValue
import org.jetbrains.kotlin.ir.declarations.IrParameterKind
import org.jetbrains.kotlin.ir.declarations.IrPackageFragment
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
import java.security.MessageDigest
import java.util.ArrayDeque
import java.util.IdentityHashMap
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
    private val backingFieldProperties = HashMap<Any, String>()
    private val localDeclarationIds = IdentityHashMap<IrDeclaration, String>()
    private val lexicalScopes = ArrayDeque<LexicalScope>()
    private val sourceTextCache = HashMap<String, String>()

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

        override fun visitBlock(expression: IrBlock) {
            lexicalScopes.addLast(
                createLexicalScope(currentFile, expression.startOffset, expression.endOffset, "block")
            )
            expression.acceptChildrenVoid(this)
            lexicalScopes.removeLast()
        }

        override fun visitClass(declaration: IrClass) {
            if (declaration.origin != IrDeclarationOrigin.DEFINED) return
            val id = classId(declaration, owners.lastOrNull())
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
            val id = propertyId(declaration, owners.lastOrNull())
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
            declaration.backingField?.let { backingFieldProperties[it.symbol] = id }
            owners.addLast(id)
            declaration.acceptChildrenVoid(this)
            owners.removeLast()
        }

        override fun visitEnumEntry(declaration: IrEnumEntry) {
            if (declaration.origin != IrDeclarationOrigin.DEFINED) return
            val containingClass = declaration.parent as? IrClass
            val qualifiedName = containingClass?.fqNameWhenAvailable?.asString()
                ?.let { it + "." + declaration.name.asString() }
                ?: declaration.name.asString()
            val id = enumEntryId(declaration)
            addSymbol(
                id = id,
                name = declaration.name.asString(),
                qualifiedName = qualifiedName,
                kind = 11,
                declaration = declaration,
                containingSymbolId = owners.lastOrNull()
            )
            declaration.acceptChildrenVoid(this)
        }

        override fun visitSimpleFunction(declaration: IrSimpleFunction) {
            if (propertyAccessors.containsKey(declaration.symbol)) {
                declaration.acceptChildrenVoid(this)
                return
            }

            if (declaration.origin != IrDeclarationOrigin.DEFINED) {
                if (declaration.origin.name == "LOCAL_FUNCTION_FOR_LAMBDA") {
                    lexicalScopes.addLast(
                        createLexicalScope(currentFile, declaration.startOffset, declaration.endOffset, "lambda")
                    )
                    declaration.acceptChildrenVoid(this)
                    lexicalScopes.removeLast()
                }
                return
            }

            val id = functionId(declaration, owners.lastOrNull())
            addSymbol(
                id = id,
                name = declaration.name.asString(),
                qualifiedName = declaration.fqNameWhenAvailable?.asString() ?: id,
                kind = 9,
                declaration = declaration,
                containingSymbolId = owners.lastOrNull()
            )
            owners.addLast(id)
            lexicalScopes.addLast(
                createLexicalScope(currentFile, declaration.startOffset, declaration.endOffset, "function")
            )
            declaration.acceptChildrenVoid(this)
            lexicalScopes.removeLast()
            owners.removeLast()
        }

        override fun visitConstructor(declaration: IrConstructor) {
            if (declaration.origin != IrDeclarationOrigin.DEFINED) return
            val id = functionId(declaration, owners.lastOrNull())
            addSymbol(
                id = id,
                name = "<init>",
                qualifiedName = declaration.fqNameWhenAvailable?.asString() ?: id,
                kind = 10,
                declaration = declaration,
                containingSymbolId = owners.lastOrNull()
            )
            owners.addLast(id)
            lexicalScopes.addLast(
                createLexicalScope(currentFile, declaration.startOffset, declaration.endOffset, "constructor")
            )
            declaration.acceptChildrenVoid(this)
            lexicalScopes.removeLast()
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
            val sourceId = classId(declaration, owners.lastOrNull())
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
                    targetClass?.origin == IrDeclarationOrigin.IR_EXTERNAL_JAVA_DECLARATION_STUB,
                    targetClass
                )
                addRelationship(sourceId, targetId, relationshipKind, declaration.startOffset, currentFile)
            }
            declaration.acceptChildrenVoid(this)
            owners.removeLast()
        }

        override fun visitProperty(declaration: IrProperty) {
            if (declaration.origin != IrDeclarationOrigin.DEFINED) return
            val sourceId = propertyId(declaration, owners.lastOrNull())
            owners.addLast(sourceId)
            addAnnotationRelationships(sourceId, declaration, currentFile)
            declaration.acceptChildrenVoid(this)
            owners.removeLast()
        }

        override fun visitSimpleFunction(declaration: IrSimpleFunction) {
            if (propertyAccessors.containsKey(declaration.symbol)) {
                declaration.acceptChildrenVoid(this)
                return
            }

            if (declaration.origin != IrDeclarationOrigin.DEFINED) {
                if (declaration.origin.name == "LOCAL_FUNCTION_FOR_LAMBDA") {
                    declaration.acceptChildrenVoid(this)
                }
                return
            }

            val sourceId = functionId(declaration, owners.lastOrNull())
            owners.addLast(sourceId)
            addAnnotationRelationships(sourceId, declaration, currentFile)
            declaration.acceptChildrenVoid(this)
            owners.removeLast()
        }

        override fun visitConstructor(declaration: IrConstructor) {
            if (declaration.origin != IrDeclarationOrigin.DEFINED) return
            val sourceId = functionId(declaration, owners.lastOrNull())
            owners.addLast(sourceId)
            addAnnotationRelationships(sourceId, declaration, currentFile)
            declaration.acceptChildrenVoid(this)
            owners.removeLast()
        }

        override fun visitCall(expression: IrCall) {
            val sourceId = owners.lastOrNull()
            val target = expression.symbol.owner
            val targetProperty = (target as? IrSimpleFunction)?.correspondingPropertySymbol?.owner
            val propertyId = propertyAccessors[expression.symbol] ?: targetProperty?.let(::propertyId)
            val targetId = propertyId ?: functionId(target)
            if (propertyId != null) {
                targetProperty?.let { addExternalPropertyIfNeeded(propertyId, it) }
            } else {
                addExternalFunctionIfNeeded(targetId, target)
            }
            if (sourceId != null) {
                addRelationship(
                    sourceId,
                    targetId,
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

        override fun visitDelegatingConstructorCall(expression: IrDelegatingConstructorCall) {
            val sourceId = owners.lastOrNull()
            val target = expression.symbol.owner
            val targetId = functionId(target)
            addExternalFunctionIfNeeded(targetId, target)
            if (sourceId != null) {
                addRelationship(sourceId, targetId, 4, expression.startOffset, currentFile)
            }
            expression.acceptChildrenVoid(this)
        }

        override fun visitFunctionReference(expression: IrFunctionReference) {
            val sourceId = owners.lastOrNull()
            val target = expression.symbol.owner
            val targetProperty = (target as? IrSimpleFunction)?.correspondingPropertySymbol?.owner
            val propertyId = targetProperty?.let(::propertyId)
            val targetId = propertyId ?: functionId(target)
            if (propertyId != null) {
                addExternalPropertyIfNeeded(propertyId, targetProperty!!)
            } else {
                addExternalFunctionIfNeeded(targetId, target)
            }
            if (sourceId != null && sourceId != targetId) {
                addRelationship(sourceId, targetId, 5, expression.startOffset, currentFile)
            }
            expression.acceptChildrenVoid(this)
        }

        override fun visitPropertyReference(expression: IrPropertyReference) {
            val sourceId = owners.lastOrNull()
            val target = expression.symbol.owner
            val targetId = propertyId(target)
            addExternalPropertyIfNeeded(targetId, target)
            if (sourceId != null && sourceId != targetId) {
                addRelationship(sourceId, targetId, 5, expression.startOffset, currentFile)
            }
            expression.acceptChildrenVoid(this)
        }

        override fun visitClassReference(expression: IrClassReference) {
            val sourceId = owners.lastOrNull()
            val target = expression.symbol.owner as? IrClass
            if (target == null) {
                expression.acceptChildrenVoid(this)
                return
            }
            val targetId = classId(target)
            val qualifiedName = target.fqNameWhenAvailable?.asString() ?: targetId
            addExternalTypeIfNeeded(
                targetId,
                qualifiedName,
                target.kind.name in setOf("INTERFACE", "ANNOTATION_CLASS"),
                target.origin == IrDeclarationOrigin.IR_EXTERNAL_JAVA_DECLARATION_STUB,
                target
            )
            if (sourceId != null && sourceId != targetId) {
                addRelationship(sourceId, targetId, 5, expression.startOffset, currentFile)
            }
            expression.acceptChildrenVoid(this)
        }

        override fun visitGetField(expression: IrGetField) {
            val sourceId = owners.lastOrNull()
            val targetId = fieldTargetId(expression.symbol.owner)
            if (sourceId != null && sourceId != targetId) {
                addRelationship(sourceId, targetId, 5, expression.startOffset, currentFile)
            }
            expression.acceptChildrenVoid(this)
        }

        override fun visitSetField(expression: IrSetField) {
            val sourceId = owners.lastOrNull()
            val targetId = fieldTargetId(expression.symbol.owner)
            if (sourceId != null && sourceId != targetId) {
                addRelationship(sourceId, targetId, 5, expression.startOffset, currentFile)
            }
            expression.acceptChildrenVoid(this)
        }

        override fun visitGetEnumValue(expression: IrGetEnumValue) {
            val sourceId = owners.lastOrNull()
            val target = expression.symbol.owner
            val targetId = enumEntryId(target)
            addExternalEnumEntryIfNeeded(targetId, target)
            if (sourceId != null && sourceId != targetId) {
                addRelationship(sourceId, targetId, 5, expression.startOffset, currentFile)
            }
            expression.acceptChildrenVoid(this)
        }

        override fun visitGetObjectValue(expression: IrGetObjectValue) {
            val sourceId = owners.lastOrNull()
            val target = expression.symbol.owner
            val targetId = classId(target)
            val qualifiedName = target.fqNameWhenAvailable?.asString() ?: targetId
            addExternalTypeIfNeeded(
                targetId,
                qualifiedName,
                false,
                target.origin == IrDeclarationOrigin.IR_EXTERNAL_JAVA_DECLARATION_STUB,
                target
            )
            if (sourceId != null && sourceId != targetId) {
                addRelationship(sourceId, targetId, 5, expression.startOffset, currentFile)
            }
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
                annotationClass.origin == IrDeclarationOrigin.IR_EXTERNAL_JAVA_DECLARATION_STUB,
                annotationClass
            )
            addRelationship(sourceId, targetId, 7, annotation.startOffset, file)
        }
    }

    private fun addExternalFunctionIfNeeded(id: String, declaration: IrFunction) {
        if (symbols.containsKey(id)) return

        val containingClass = declaration.parent as? IrClass
        val containingSymbolId = containingClass?.let(::classId)
        if (containingClass != null && containingSymbolId != null) {
            val qualifiedName = containingClass.fqNameWhenAvailable?.asString() ?: containingSymbolId
            addExternalTypeIfNeeded(
                containingSymbolId,
                qualifiedName,
                containingClass.kind.name in setOf("INTERFACE", "ANNOTATION_CLASS"),
                containingClass.origin == IrDeclarationOrigin.IR_EXTERNAL_JAVA_DECLARATION_STUB,
                containingClass
            )
        }

        val name = if (declaration is IrConstructor) "<init>" else declaration.name.asString()
        val visibility = (declaration as? IrDeclarationWithVisibility)?.visibility?.name
        val symbol = JsonObject()
        symbol.addProperty("id", id)
        symbol.addProperty("kind", if (declaration is IrConstructor) 10 else 9)
        symbol.addProperty("name", name)
        symbol.addProperty("qualifiedName", declaration.fqNameWhenAvailable?.asString() ?: id)
        symbol.addProperty("containerName", packageName(declaration))
        symbol.addProperty("containerKind", "Package")
        symbol.addProperty(
            "language",
            if (declaration.origin == IrDeclarationOrigin.IR_EXTERNAL_JAVA_DECLARATION_STUB) "Java" else "Kotlin"
        )
        symbol.addProperty("projectPath", projectPath)
        symbol.add("filePath", com.google.gson.JsonNull.INSTANCE)
        symbol.add("startLine", com.google.gson.JsonNull.INSTANCE)
        symbol.add("startColumn", com.google.gson.JsonNull.INSTANCE)
        symbol.addProperty("containingSymbolId", containingSymbolId)
        symbol.addProperty("origin", 3)
        symbol.addProperty("isTestCode", isTestCode)
        symbol.addProperty("isPublic", visibility == "public")
        symbol.addProperty("visibility", visibility?.replaceFirstChar(Char::uppercase) ?: "Unknown")
        symbol.addProperty(
            "declarationKind",
            when {
                declaration is IrConstructor -> "Constructor"
                receiverType(declaration) != null -> "ExtensionFunction"
                else -> "Function"
            }
        )
        symbol.addProperty("receiverType", receiverType(declaration))
        symbol.add("provenance", JsonArray())
        symbols[id] = symbol
    }

    private fun addExternalPropertyIfNeeded(id: String, declaration: IrProperty) {
        if (symbols.containsKey(id)) return

        if (declaration.origin == IrDeclarationOrigin.IR_EXTERNAL_JAVA_DECLARATION_STUB) {
            declaration.backingField?.let { field ->
                addExternalFieldIfNeeded(id, field)
                return
            }
        }

        val containingClass = declaration.parent as? IrClass
        val containingSymbolId = containingClass?.let(::classId)
        if (containingClass != null && containingSymbolId != null) {
            val qualifiedName = containingClass.fqNameWhenAvailable?.asString() ?: containingSymbolId
            addExternalTypeIfNeeded(
                containingSymbolId,
                qualifiedName,
                containingClass.kind.name in setOf("INTERFACE", "ANNOTATION_CLASS"),
                containingClass.origin == IrDeclarationOrigin.IR_EXTERNAL_JAVA_DECLARATION_STUB,
                containingClass
            )
        }

        val visibility = (declaration as? IrDeclarationWithVisibility)?.visibility?.name
        val symbol = JsonObject()
        symbol.addProperty("id", id)
        symbol.addProperty("kind", 12)
        symbol.addProperty("name", declaration.name.asString())
        symbol.addProperty("qualifiedName", declaration.fqNameWhenAvailable?.asString() ?: id)
        symbol.addProperty("containerName", packageName(declaration))
        symbol.addProperty("containerKind", "Package")
        symbol.addProperty(
            "language",
            if (declaration.origin == IrDeclarationOrigin.IR_EXTERNAL_JAVA_DECLARATION_STUB) "Java" else "Kotlin"
        )
        symbol.addProperty("projectPath", projectPath)
        symbol.add("filePath", com.google.gson.JsonNull.INSTANCE)
        symbol.add("startLine", com.google.gson.JsonNull.INSTANCE)
        symbol.add("startColumn", com.google.gson.JsonNull.INSTANCE)
        symbol.addProperty("containingSymbolId", containingSymbolId)
        symbol.addProperty("origin", 3)
        symbol.addProperty("isTestCode", isTestCode)
        symbol.addProperty("isPublic", visibility == "public")
        symbol.addProperty("visibility", visibility?.replaceFirstChar(Char::uppercase) ?: "Unknown")
        symbol.addProperty("declarationKind", "Property")
        symbol.addProperty("receiverType", receiverType(declaration))
        symbol.add("provenance", JsonArray())
        symbols[id] = symbol
    }

    private fun addExternalFieldIfNeeded(id: String, declaration: IrField) {
        if (symbols.containsKey(id)) return

        val containingClass = declaration.parent as? IrClass
        val containingSymbolId = containingClass?.let(::classId)
        if (containingClass != null && containingSymbolId != null) {
            val qualifiedName = containingClass.fqNameWhenAvailable?.asString() ?: containingSymbolId
            addExternalTypeIfNeeded(
                containingSymbolId,
                qualifiedName,
                containingClass.kind.name in setOf("INTERFACE", "ANNOTATION_CLASS"),
                containingClass.origin == IrDeclarationOrigin.IR_EXTERNAL_JAVA_DECLARATION_STUB,
                containingClass
            )
        }

        val visibility = (declaration as? IrDeclarationWithVisibility)?.visibility?.name
        val qualifiedName = declaration.fqNameWhenAvailable?.asString()
            ?: containingClass?.fqNameWhenAvailable?.asString()
                ?.let { it + "." + declaration.name.asString() }
            ?: declaration.name.asString()
        val symbol = JsonObject()
        symbol.addProperty("id", id)
        symbol.addProperty("kind", 11)
        symbol.addProperty("name", declaration.name.asString())
        symbol.addProperty("qualifiedName", qualifiedName)
        symbol.addProperty("containerName", packageName(declaration))
        symbol.addProperty("containerKind", "Package")
        symbol.addProperty(
            "language",
            if (declaration.origin == IrDeclarationOrigin.IR_EXTERNAL_JAVA_DECLARATION_STUB
                || containingClass?.origin == IrDeclarationOrigin.IR_EXTERNAL_JAVA_DECLARATION_STUB) "Java" else "Kotlin"
        )
        symbol.addProperty("projectPath", projectPath)
        symbol.add("filePath", com.google.gson.JsonNull.INSTANCE)
        symbol.add("startLine", com.google.gson.JsonNull.INSTANCE)
        symbol.add("startColumn", com.google.gson.JsonNull.INSTANCE)
        symbol.addProperty("containingSymbolId", containingSymbolId)
        symbol.addProperty("origin", 3)
        symbol.addProperty("isTestCode", isTestCode)
        symbol.addProperty("isPublic", visibility == "public")
        symbol.addProperty("visibility", visibility?.replaceFirstChar(Char::uppercase) ?: "Unknown")
        symbol.addProperty("declarationKind", "Field")
        symbol.add("provenance", JsonArray())
        symbols[id] = symbol
    }

    private fun addExternalEnumEntryIfNeeded(id: String, declaration: IrEnumEntry) {
        if (symbols.containsKey(id)) return

        val containingClass = declaration.parent as? IrClass
        val containingSymbolId = containingClass?.let(::classId)
        if (containingClass != null && containingSymbolId != null) {
            val qualifiedName = containingClass.fqNameWhenAvailable?.asString() ?: containingSymbolId
            addExternalTypeIfNeeded(
                containingSymbolId,
                qualifiedName,
                false,
                containingClass.origin == IrDeclarationOrigin.IR_EXTERNAL_JAVA_DECLARATION_STUB,
                containingClass
            )
        }

        val qualifiedName = containingClass?.fqNameWhenAvailable?.asString()
            ?.let { it + "." + declaration.name.asString() }
            ?: declaration.name.asString()
        val visibility = (declaration as? IrDeclarationWithVisibility)?.visibility?.name
        val symbol = JsonObject()
        symbol.addProperty("id", id)
        symbol.addProperty("kind", 11)
        symbol.addProperty("name", declaration.name.asString())
        symbol.addProperty("qualifiedName", qualifiedName)
        symbol.addProperty("containerName", packageName(declaration))
        symbol.addProperty("containerKind", "Package")
        symbol.addProperty(
            "language",
            if (declaration.origin == IrDeclarationOrigin.IR_EXTERNAL_JAVA_DECLARATION_STUB) "Java" else "Kotlin"
        )
        symbol.addProperty("projectPath", projectPath)
        symbol.add("filePath", com.google.gson.JsonNull.INSTANCE)
        symbol.add("startLine", com.google.gson.JsonNull.INSTANCE)
        symbol.add("startColumn", com.google.gson.JsonNull.INSTANCE)
        symbol.addProperty("containingSymbolId", containingSymbolId)
        symbol.addProperty("origin", 3)
        symbol.addProperty("isTestCode", isTestCode)
        symbol.addProperty("isPublic", visibility == "public")
        symbol.addProperty("visibility", visibility?.replaceFirstChar(Char::uppercase) ?: "Unknown")
        symbol.addProperty("declarationKind", "EnumEntry")
        symbol.add("provenance", JsonArray())
        symbols[id] = symbol
    }

    private fun fieldTargetId(declaration: IrField): String {
        backingFieldProperties[declaration.symbol]?.let { return it }

        if (declaration.origin == IrDeclarationOrigin.IR_EXTERNAL_JAVA_DECLARATION_STUB) {
            val id = fieldId(declaration)
            addExternalFieldIfNeeded(id, declaration)
            return id
        }

        val externalProperty = declaration.correspondingPropertySymbol?.owner
        if (externalProperty != null) {
            val id = propertyId(externalProperty)
            addExternalPropertyIfNeeded(id, externalProperty)
            return id
        }

        val id = fieldId(declaration)
        addExternalFieldIfNeeded(id, declaration)
        return id
    }

    private fun addExternalTypeIfNeeded(
        id: String,
        qualifiedName: String,
        isInterface: Boolean,
        isJava: Boolean,
        declaration: IrClass? = null
    ) {
        if (symbols.containsKey(id)) return

        val containingClass = declaration?.parent as? IrClass
        val visibility = declaration?.visibility?.name
        val symbol = JsonObject()
        symbol.addProperty("id", id)
        symbol.addProperty("kind", if (isInterface) 5 else 4)
        symbol.addProperty("name", qualifiedName.substringAfterLast('.'))
        symbol.addProperty("qualifiedName", qualifiedName)
        symbol.addProperty(
            "containerName",
            declaration?.let(::packageName) ?: qualifiedName.substringBeforeLast('.', "")
        )
        symbol.addProperty("containerKind", "Package")
        symbol.addProperty("language", if (isJava) "Java" else "Kotlin")
        symbol.addProperty("projectPath", projectPath)
        symbol.add("filePath", com.google.gson.JsonNull.INSTANCE)
        symbol.add("startLine", com.google.gson.JsonNull.INSTANCE)
        symbol.add("startColumn", com.google.gson.JsonNull.INSTANCE)
        symbol.addProperty("containingSymbolId", containingClass?.let(::classId))
        symbol.addProperty("origin", 3)
        symbol.addProperty("isTestCode", isTestCode)
        symbol.addProperty("isPublic", visibility == "public")
        symbol.addProperty("visibility", visibility?.replaceFirstChar(Char::uppercase) ?: "Unknown")
        symbol.addProperty(
            "declarationKind",
            declaration?.let(::declarationKind) ?: if (isInterface) "Interface" else "Class"
        )
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

    private fun enumEntryId(declaration: IrEnumEntry): String {
        val containingClass = declaration.parent as? IrClass
        val qualifiedName = containingClass?.fqNameWhenAvailable?.asString()
            ?.let { it + "." + declaration.name.asString() }
            ?: declaration.name.asString()
        return "kotlin:field:$qualifiedName"
    }

    private fun fieldId(declaration: IrField): String {
        val containingClass = declaration.parent as? IrClass
        val qualifiedName = declaration.fqNameWhenAvailable?.asString()
            ?: containingClass?.fqNameWhenAvailable?.asString()
                ?.let { it + "." + declaration.name.asString() }
            ?: declaration.name.asString()
        return "kotlin:field:$qualifiedName"
    }

    private fun classId(
        declaration: IrClass,
        containingSymbolId: String? = null
    ): String {
        val qualifiedName = declaration.fqNameWhenAvailable?.asString()
            ?: localDeclarationName(declaration, containingSymbolId, "type")
        return "kotlin:type:$qualifiedName"
    }

    private fun localDeclarationName(
        declaration: IrDeclaration,
        containingSymbolId: String?,
        declarationKind: String
    ): String {
        localDeclarationIds[declaration]?.let { return it }

        val owner = containingSymbolId ?: parentSymbolId(declaration) ?: projectPath
        val name = (declaration as? IrDeclarationWithName)?.name?.asString()
            ?: declaration.javaClass.simpleName
        val scopeIdentity = lexicalScopes.lastOrNull()?.identity ?: "root"
        val identity = "$owner::$declarationKind:$name@$scopeIdentity"
        localDeclarationIds[declaration] = identity
        return identity
    }

    private fun createLexicalScope(
        file: IrFile?,
        startOffset: Int,
        endOffset: Int,
        kind: String
    ): LexicalScope {
        val parent = lexicalScopes.lastOrNull()
        val source = sourceSnippet(file, startOffset, endOffset)
        val parentPrefix = parent?.let { sourceSnippet(file, it.startOffset, startOffset) }
        val identitySource = if (source == null) {
            listOf(kind, parent?.identity.orEmpty(), file?.fileEntry?.name.orEmpty(), "source-unavailable")
                .joinToString("|")
        } else {
            listOf(
                kind,
                parent?.identity.orEmpty(),
                normalizeSourceSnippet(parentPrefix),
                normalizeSourceSnippet(source)
            ).joinToString("|")
        }

        return LexicalScope(startOffset, stableHash(identitySource))
    }

    private fun sourceSnippet(file: IrFile?, startOffset: Int, endOffset: Int): String? {
        if (file == null || startOffset < 0 || endOffset < startOffset) return null

        val path = file.fileEntry.name
        val source = sourceTextCache.getOrPut(path) {
            try {
                Files.readString(Path.of(path))
            } catch (_: Exception) {
                ""
            }
        }

        if (source.isEmpty() || endOffset > source.length) return null
        return source.substring(startOffset, endOffset)
    }

    private fun normalizeSourceSnippet(source: String?): String =
        source.orEmpty().lineSequence()
            .filter(String::isNotBlank)
            .joinToString("\n") { it.trim() }

    private fun stableHash(value: String): String =
        MessageDigest.getInstance("SHA-256")
            .digest(value.toByteArray(Charsets.UTF_8))
            .joinToString("") { "%02x".format(it) }

    private data class LexicalScope(
        val startOffset: Int,
        val identity: String
    )

    private fun packageName(declaration: IrDeclaration): String {
        var parent = declaration.parent
        while (parent is IrDeclaration) {
            parent = parent.parent
        }

        return (parent as? IrPackageFragment)?.packageFqName?.asString().orEmpty()
    }

    private fun parentSymbolId(declaration: IrDeclaration): String? = when (val parent = declaration.parent) {
        is IrClass -> classId(parent)
        is IrFunction -> functionId(parent)
        else -> null
    }

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
        is IrEnumEntry -> "EnumEntry"
        is IrField -> "Field"
        is IrSimpleFunction -> if (receiverType(declaration) != null) "ExtensionFunction" else "Function"
        else -> declaration.javaClass.simpleName
    }

    private fun propertyId(
        declaration: IrProperty,
        containingSymbolId: String? = null
    ): String {
        if (declaration.origin == IrDeclarationOrigin.IR_EXTERNAL_JAVA_DECLARATION_STUB) {
            declaration.backingField?.let { return fieldId(it) }
        }

        val accessor = declaration.getter ?: declaration.setter
        val receiver = accessor?.parameters
            ?.firstOrNull { it.kind == IrParameterKind.ExtensionReceiver }
            ?.type
            ?.let(::typeIdentity)
        val receiverIdentity = receiver?.let { "($it)" }.orEmpty()
        val name = declaration.fqNameWhenAvailable?.asString()
            ?: localDeclarationName(declaration, containingSymbolId, "property")
        return "kotlin:property:$name$receiverIdentity"
    }

    private fun functionId(
        declaration: IrFunction,
        containingSymbolId: String? = null
    ): String {
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
        val name = declaration.fqNameWhenAvailable?.asString()
            ?: localDeclarationName(
                declaration,
                containingSymbolId,
                if (declaration is IrConstructor) "constructor" else "function"
            )
        val symbolPrefix = if (declaration is IrConstructor) "constructor" else "function"
        return "kotlin:$symbolPrefix:$name($signature)"
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

