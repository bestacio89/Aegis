package org.aegis.worker

import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Test
import java.nio.file.Files
import java.nio.file.Path

class KotlinSourceLayoutTests {
    @Test
    fun `each Kotlin source file contains one top-level type with a matching filename`() {
        val projectRoot = resolveProjectRoot()
        val sourceRoots = listOf(
            projectRoot.resolve("src/main/kotlin"),
            projectRoot.resolve("src/test/kotlin")
        )
        val declarationPattern = Regex(
            """(?m)^(?:(?:public|private|internal|protected|expect|actual|open|abstract|final|inline|value|data|sealed|enum|annotation|fun|external)\\s+)*(?:class|interface|object|typealias)\\s+(`?[A-Za-z_][A-Za-z0-9_]*`?)"""
        )

        sourceRoots.forEach { sourceRoot ->
            assertTrue(Files.isDirectory(sourceRoot), "Kotlin source root is missing: $sourceRoot")
            Files.walk(sourceRoot).use { paths ->
                paths.filter { Files.isRegularFile(it) && it.fileName.toString().endsWith(".kt") }
                    .forEach { sourceFile ->
                        val declarations = declarationPattern.findAll(Files.readString(sourceFile)).toList()
                        assertEquals(
                            1,
                            declarations.size,
                            "Each Kotlin source file must declare exactly one top-level type: $sourceFile"
                        )
                        val declaredName = declarations.single().groupValues[1].removeSurrounding("`")
                        val fileName = sourceFile.fileName.toString().removeSuffix(".kt")
                        assertEquals(
                            declaredName,
                            fileName,
                            "The Kotlin source filename must match its top-level type: $sourceFile"
                        )
                    }
            }
        }
    }

    private fun resolveProjectRoot(): Path {
        val currentDirectory = Path.of(System.getProperty("user.dir")).toAbsolutePath().normalize()
        return if (Files.isDirectory(currentDirectory.resolve("src/main/kotlin"))) {
            currentDirectory
        } else {
            currentDirectory.resolve("tools/kotlin-worker-prototype")
        }
    }
}
