package org.aegis.worker

import org.jetbrains.kotlin.compiler.plugin.CompilerPluginRegistrar
import org.jetbrains.kotlin.compiler.plugin.ExperimentalCompilerApi
import org.jetbrains.kotlin.config.CompilerConfiguration
import org.jetbrains.kotlin.backend.common.extensions.IrGenerationExtension

@OptIn(ExperimentalCompilerApi::class)
class KotlinWorkerPluginRegistrar : CompilerPluginRegistrar() {
    override val pluginId: String = "org.aegis.kotlin.semantic"

    override val supportsK2: Boolean = true

    override fun ExtensionStorage.registerExtensions(configuration: CompilerConfiguration) {
        IrGenerationExtension.registerExtension(KotlinSemanticIrExtension())
    }
}
