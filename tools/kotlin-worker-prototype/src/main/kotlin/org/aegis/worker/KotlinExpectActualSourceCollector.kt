package org.aegis.worker

import com.google.gson.JsonArray
import com.google.gson.JsonNull
import com.google.gson.JsonObject
import com.intellij.openapi.util.Disposer
import org.jetbrains.kotlin.cli.jvm.compiler.EnvironmentConfigFiles
import org.jetbrains.kotlin.cli.jvm.compiler.KotlinCoreEnvironment
import org.jetbrains.kotlin.config.CompilerConfiguration
import org.jetbrains.kotlin.lexer.KtTokens
import org.jetbrains.kotlin.psi.KtClass
import org.jetbrains.kotlin.psi.KtClassOrObject
import org.jetbrains.kotlin.psi.KtDeclaration
import org.jetbrains.kotlin.psi.KtFile
import org.jetbrains.kotlin.psi.KtModifierListOwner
import org.jetbrains.kotlin.psi.KtNamedDeclaration
import org.jetbrains.kotlin.psi.KtNamedFunction
import org.jetbrains.kotlin.psi.KtProperty
import org.jetbrains.kotlin.psi.KtPsiFactory
import org.jetbrains.kotlin.psi.KtTypeAlias
import java.nio.file.Files
import java.nio.file.Path

internal class KotlinExpectActualSourceCollector {
    fun reconcile(
        evidence: JsonObject,
        sourceFiles: List<Path>,
        sharedSourceRoots: List<Path>,
        targetName: String?,
        includeTestCode: Boolean
    ): JsonObject {
        if (sharedSourceRoots.isEmpty() || sourceFiles.isEmpty())
            return evidence

        val declarations = parseDeclarations(sourceFiles, sharedSourceRoots)
        val expectedDeclarations = declarations.filter { it.isExpected }
        val actualDeclarations = declarations.filter { it.isActual }
        if (expectedDeclarations.isEmpty() || actualDeclarations.isEmpty())
            return evidence

        val symbols = evidence.getAsJsonArray("symbols")
            ?.map { it.asJsonObject }
            ?.toMutableList()
            ?: return evidence
        val diagnostics = mutableListOf<JsonObject>()
        val pairs = mutableListOf<ActualizationPair>()
        val actualizedIds = LinkedHashMap<String, String>()

        for (actual in actualDeclarations.sortedBy { it.qualifiedName.count { character -> character == '.' } }) {
            val expectedCandidates = expectedDeclarations.filter { expected ->
                expected.qualifiedName == actual.qualifiedName
                    && expected.kind == actual.kind
            }
            val exactMatches = expectedCandidates.filter { it.signature == actual.signature }
            val expected = when {
                exactMatches.size == 1 -> exactMatches.single()
                exactMatches.isEmpty() && expectedCandidates.size == 1 -> expectedCandidates.single()
                else -> {
                    diagnostics.add(createDiagnostic(
                        "KotlinExpectActualMatchAmbiguous",
                        "Could not uniquely match actual declaration '${actual.qualifiedName}' to a common expect declaration.",
                        actual.filePath,
                        actual.startLine,
                        targetName
                    ))
                    continue
                }
            }

            val symbolCandidates = symbols.filter { symbol ->
                symbol.stringValue("qualifiedName") == actual.qualifiedName
                    && symbol.intValue("kind") in actual.compilerKinds
                    && symbol.stringValue("filePath") == actual.filePath
            }
            val locatedCandidates = symbolCandidates.filter { symbol ->
                symbol.intValue("startLine") == actual.startLine
                    && symbol.intValue("startColumn") == actual.startColumn
            }
            val actualSymbol = when {
                locatedCandidates.size == 1 -> locatedCandidates.single()
                locatedCandidates.isEmpty() && symbolCandidates.size == 1 -> symbolCandidates.single()
                else -> {
                    diagnostics.add(createDiagnostic(
                        "KotlinActualSemanticSymbolNotFound",
                        "The compiler did not emit a unique semantic symbol for actual declaration '${actual.qualifiedName}'.",
                        actual.filePath,
                        actual.startLine,
                        targetName
                    ))
                    continue
                }
            }

            val expectedId = actualSymbol.stringValue("id")
            val actualId = "$expectedId#actual:${actual.sourceSetName ?: targetName ?: "unknown"}"
            if (actualizedIds.containsKey(expectedId))
                continue

            val expectedSymbol = actualSymbol.deepCopy()
            expectedSymbol.addProperty("id", expectedId)
            expectedSymbol.addProperty("filePath", expected.filePath)
            expectedSymbol.addProperty("startLine", expected.startLine)
            expectedSymbol.addProperty("startColumn", expected.startColumn)
            expectedSymbol.addProperty("origin", 1)
            expectedSymbol.addProperty("isTestCode", includeTestCode)
            expectedSymbol.add("provenance", provenance(expected))
            setModifier(expectedSymbol, "expect")
            setModifier(actualSymbol, "actual")
            actualizedIds[expectedId] = actualId
            pairs.add(ActualizationPair(expectedId, actualId, actual, expected, actualSymbol, expectedSymbol))
        }

        if (pairs.isEmpty()) {
            diagnostics.forEach { evidence.getAsJsonArray("diagnostics")?.add(it) }
            if (diagnostics.isNotEmpty())
                evidence.addProperty("status", KotlinWorkerStatus.PARTIALLY_COMPLETED)
            return evidence
        }

        for (pair in pairs) {
            val parentId = pair.actualSymbol.stringValueOrNull("containingSymbolId")
            if (parentId != null) {
                val actualParentId = actualizedIds[parentId]
                if (actualParentId != null)
                    pair.actualSymbol.addProperty("containingSymbolId", actualParentId)
            }
        }

        val updatedSymbols = JsonArray()
        for (symbol in symbols) {
            val originalId = symbol.stringValue("id")
            val actualId = actualizedIds[originalId]
            if (actualId == null) {
                updatedSymbols.add(symbol)
                continue
            }

            symbol.addProperty("id", actualId)
            val parentId = symbol.stringValueOrNull("containingSymbolId")
            if (parentId != null)
                actualizedIds[parentId]?.let { symbol.addProperty("containingSymbolId", it) }
            updatedSymbols.add(symbol)
            pairs.firstOrNull { it.expectedId == originalId }?.let { updatedSymbols.add(it.expectedSymbol) }
        }
        evidence.add("symbols", updatedSymbols)

        val relationships = evidence.getAsJsonArray("relationships") ?: JsonArray()
        for (relationshipElement in relationships) {
            val relationship = relationshipElement.asJsonObject
            val sourceId = relationship.stringValue("sourceSymbolId")
            val targetId = relationship.stringValue("targetSymbolId")
            val updatedSourceId = actualizedIds[sourceId] ?: sourceId
            val updatedTargetId = actualizedIds[targetId] ?: targetId
            relationship.addProperty("sourceSymbolId", updatedSourceId)
            relationship.addProperty("targetSymbolId", updatedTargetId)
            relationship.addProperty(
                "id",
                "$updatedSourceId|${relationship.intValue("kind")}|$updatedTargetId"
            )
        }

        for (pair in pairs) {
            relationships.add(actualizesRelationship(
                pair.actualId,
                pair.expectedId,
                pair.actual,
                includeTestCode
            ))
        }
        evidence.add("relationships", relationships)

        if (diagnostics.isNotEmpty()) {
            val outputDiagnostics = evidence.getAsJsonArray("diagnostics") ?: JsonArray()
            diagnostics.forEach(outputDiagnostics::add)
            evidence.add("diagnostics", outputDiagnostics)
            evidence.addProperty("status", KotlinWorkerStatus.PARTIALLY_COMPLETED)
        }
        evidence.addProperty("expectActualRelationshipCount", pairs.size)
        return evidence
    }

    private fun parseDeclarations(
        sourceFiles: List<Path>,
        sharedSourceRoots: List<Path>
    ): List<SourceDeclaration> {
        val disposable = Disposer.newDisposable("aegis-kotlin-expect-actual")
        try {
            val environment = KotlinCoreEnvironment.createForProduction(
                disposable,
                CompilerConfiguration(),
                EnvironmentConfigFiles.JVM_CONFIG_FILES
            )
            val factory = KtPsiFactory(environment.project, false)
            val declarations = mutableListOf<SourceDeclaration>()

            for (sourceFile in sourceFiles) {
                val isShared = sharedSourceRoots.any { root -> sourceFile.startsWith(root) }
                val source = Files.readString(sourceFile)
                val file = factory.createFile(sourceFile.fileName.toString(), source)
                collectDeclarations(
                    file.declarations,
                    file.packageFqName.asString(),
                    sourceFile.toAbsolutePath().normalize().toString(),
                    source,
                    sourceSetName(sourceFile),
                    isShared,
                    declarations
                )
            }

            return declarations
        } finally {
            Disposer.dispose(disposable)
        }
    }

    private fun collectDeclarations(
        sourceDeclarations: List<KtDeclaration>,
        parentName: String,
        filePath: String,
        source: String,
        sourceSetName: String?,
        isShared: Boolean,
        output: MutableList<SourceDeclaration>
    ) {
        for (declaration in sourceDeclarations) {
            val name = (declaration as? KtNamedDeclaration)?.name ?: continue
            val qualifiedName = listOf(parentName, name)
                .filter(String::isNotBlank)
                .joinToString(".")
            val isExpected = isShared && declaration.hasModifier(KtTokens.EXPECT_KEYWORD)
            val isActual = !isShared && declaration.hasModifier(KtTokens.ACTUAL_KEYWORD)

            if (isExpected || isActual) {
                val (line, column) = lineAndColumn(source, declaration.textOffset)
                val kind = compilerKind(declaration)
                if (kind.isNotEmpty()) {
                    output.add(SourceDeclaration(
                        qualifiedName,
                        name,
                        kind,
                        signature(declaration),
                        isExpected,
                        isActual,
                        filePath,
                        line,
                        column,
                        sourceSetName
                    ))
                }
            }

            if (declaration is KtClassOrObject) {
                collectDeclarations(
                    declaration.declarations,
                    qualifiedName,
                    filePath,
                    source,
                    sourceSetName,
                    isShared,
                    output
                )
            }
        }
    }

    private fun compilerKind(declaration: KtDeclaration): Set<Int> = when (declaration) {
        is KtNamedFunction -> setOf(9)
        is KtProperty -> setOf(12)
        is KtTypeAlias -> setOf(14)
        is KtClassOrObject -> setOf(4, 5, 7, 8)
        else -> emptySet()
    }

    private fun signature(declaration: KtDeclaration): String = when (declaration) {
        is KtNamedFunction -> listOf(
            declaration.receiverTypeReference?.text.orEmpty(),
            declaration.typeParameters.size.toString(),
            declaration.valueParameters.joinToString(",") {
                normalizeType(it.typeReference?.text.orEmpty())
            }
        ).joinToString("|")
        is KtProperty -> listOf(
            declaration.isVar.toString(),
            normalizeType(declaration.typeReference?.text.orEmpty())
        ).joinToString("|")
        is KtClassOrObject -> listOf(
            declaration.typeParameters.size.toString(),
            ((declaration as? KtClass)?.primaryConstructorParameters.orEmpty())
                .joinToString(",") { normalizeType(it.typeReference?.text.orEmpty()) }
        ).joinToString("|")
        is KtTypeAlias -> listOf(
            declaration.typeParameters.size.toString(),
            normalizeType(declaration.typeReference?.text.orEmpty())
        ).joinToString("|")
        else -> ""
    }

    private fun normalizeType(value: String): String =
        value.filterNot(Char::isWhitespace)

    private fun sourceSetName(path: Path): String? {
        val segments = path.toString().replace('\\', '/').split('/')
        val sourceIndex = segments.indexOfLast { it == "src" }
        if (sourceIndex < 0 || sourceIndex + 1 >= segments.size)
            return null
        return segments[sourceIndex + 1].takeIf {
            it.endsWith("Main") || it.endsWith("Test")
        }
    }

    private fun lineAndColumn(source: String, offset: Int): Pair<Int, Int> {
        val safeOffset = offset.coerceIn(0, source.length)
        val prefix = source.substring(0, safeOffset)
        val line = prefix.count { it == '\n' } + 1
        val column = safeOffset - (prefix.lastIndexOf('\n') + 1) + 1
        return line to column
    }

    private fun provenance(declaration: SourceDeclaration): JsonArray =
        JsonArray().apply {
            add(JsonObject().apply {
                addProperty("analyzerId", "kotlin-k2-psi")
                addProperty("source", 2)
                addProperty("filePath", declaration.filePath)
                addProperty("startLine", declaration.startLine)
                addProperty("startColumn", declaration.startColumn)
                addProperty("confidence", 1.0)
            })
        }

    private fun actualizesRelationship(
        actualId: String,
        expectedId: String,
        declaration: SourceDeclaration,
        isTestCode: Boolean
    ): JsonObject = JsonObject().apply {
        addProperty("id", "$actualId|11|$expectedId")
        addProperty("sourceSymbolId", actualId)
        addProperty("targetSymbolId", expectedId)
        add("unresolvedTarget", JsonNull.INSTANCE)
        addProperty("kind", 11)
        addProperty("resolutionStatus", 1)
        addProperty("isTestCode", isTestCode)
        add("provenance", provenance(declaration))
    }

    private fun setModifier(symbol: JsonObject, modifier: String) {
        val modifiers = symbol.getAsJsonArray("modifiers") ?: JsonArray()
        if (modifiers.none { it.asString == modifier })
            modifiers.add(modifier)
        symbol.add("modifiers", modifiers)
    }

    private fun createDiagnostic(
        code: String,
        message: String,
        filePath: String,
        line: Int,
        targetName: String?
    ): JsonObject = JsonObject().apply {
        addProperty("code", code)
        addProperty("component", javaClass.simpleName)
        addProperty("message", message)
        addProperty("severity", "Warning")
        addProperty("target", targetName ?: filePath)
        addProperty("line", line)
        addProperty("affectsCoverage", true)
    }

    private data class SourceDeclaration(
        val qualifiedName: String,
        val name: String,
        val compilerKinds: Set<Int>,
        val signature: String,
        val isExpected: Boolean,
        val isActual: Boolean,
        val filePath: String,
        val startLine: Int,
        val startColumn: Int,
        val sourceSetName: String?
    )

    private data class ActualizationPair(
        val expectedId: String,
        val actualId: String,
        val actual: SourceDeclaration,
        val expected: SourceDeclaration,
        val actualSymbol: JsonObject,
        val expectedSymbol: JsonObject
    )

    private fun JsonObject.stringValue(name: String): String =
        get(name)?.takeUnless { it.isJsonNull }?.asString.orEmpty()

    private fun JsonObject.stringValueOrNull(name: String): String? =
        get(name)?.takeUnless { it.isJsonNull }?.asString

    private fun JsonObject.intValue(name: String): Int =
        get(name)?.takeUnless { it.isJsonNull }?.asInt ?: -1

    private object KotlinWorkerStatus {
        const val PARTIALLY_COMPLETED = 3
    }
}
