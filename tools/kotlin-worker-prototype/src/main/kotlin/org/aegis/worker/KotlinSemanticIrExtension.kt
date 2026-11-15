package org.aegis.worker

import com.google.gson.Gson
import org.jetbrains.kotlin.backend.common.extensions.IrGenerationExtension
import org.jetbrains.kotlin.backend.common.extensions.IrPluginContext
import org.jetbrains.kotlin.ir.declarations.IrModuleFragment
import java.nio.file.Files
import java.nio.file.Path

class KotlinSemanticIrExtension : IrGenerationExtension {
    override fun generate(moduleFragment: IrModuleFragment, pluginContext: IrPluginContext) {
        val outputPath = System.getProperty(OUTPUT_PATH_PROPERTY)?.takeIf(String::isNotBlank) ?: return
        val projectPath = System.getProperty(PROJECT_PATH_PROPERTY).orEmpty()
        val isTestCode = System.getProperty(TEST_SCOPE_PROPERTY).toBoolean()
        val generatedRoots = System.getProperty(GENERATED_ROOTS_PROPERTY)
            .orEmpty()
            .split(java.io.File.pathSeparator)
            .filter(String::isNotBlank)
            .map { Path.of(it).toAbsolutePath().normalize() }
        val collector = KotlinSemanticCollector(projectPath, isTestCode, generatedRoots)
        collector.collect(moduleFragment)

        val output = Path.of(outputPath).toAbsolutePath().normalize()
        Files.createDirectories(output.parent)
        Files.writeString(output, Gson().toJson(collector.toJson(moduleFragment.files.size)))
    }

    private companion object {
        const val OUTPUT_PATH_PROPERTY = "aegis.kotlin.semantic.output"
        const val PROJECT_PATH_PROPERTY = "aegis.kotlin.semantic.project"
        const val TEST_SCOPE_PROPERTY = "aegis.kotlin.semantic.test"
        const val GENERATED_ROOTS_PROPERTY = "aegis.kotlin.semantic.generated"
    }
}
