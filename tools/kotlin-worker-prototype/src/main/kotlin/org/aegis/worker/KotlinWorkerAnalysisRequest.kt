package org.aegis.worker

import com.google.gson.JsonObject

data class KotlinWorkerAnalysisRequest(
    val projectPath: String,
    val workspaceRoot: String,
    val sourceManifestPath: String,
    val artifactRoot: String,
    val sourceRoots: List<String>,
    val referencedSourceRoots: List<String>,
    val testSourceRoots: List<String>,
    val generatedSourceRoots: List<String>,
    val classpathEntries: List<String>,
    val kotlinStandardLibraryPath: String,
    val compilerVersion: String,
    val languageVersion: String,
    val apiVersion: String,
    val jvmTarget: String,
    val moduleName: String,
    val includeTestCode: Boolean
) {
    companion object {
        fun from(payload: JsonObject): KotlinWorkerAnalysisRequest = KotlinWorkerAnalysisRequest(
            projectPath = payload.requiredString("projectPath"),
            workspaceRoot = payload.requiredString("workspaceRoot"),
            sourceManifestPath = payload.requiredString("sourceManifestPath"),
            artifactRoot = payload.requiredString("artifactRoot"),
            sourceRoots = payload.stringList("sourceRoots"),
            referencedSourceRoots = payload.stringList("referencedSourceRoots"),
            testSourceRoots = payload.stringList("testSourceRoots"),
            generatedSourceRoots = payload.stringList("generatedSourceRoots"),
            classpathEntries = payload.stringList("classpathEntries"),
            kotlinStandardLibraryPath = payload.requiredString("kotlinStandardLibraryPath"),
            compilerVersion = payload.requiredString("compilerVersion"),
            languageVersion = payload.requiredString("languageVersion"),
            apiVersion = payload.requiredString("apiVersion"),
            jvmTarget = payload.requiredString("jvmTarget"),
            moduleName = payload.optionalString("moduleName") ?: "aegis-module",
            includeTestCode = payload.get("includeTestCode")?.takeUnless { it.isJsonNull }?.asBoolean ?: false
        )

        private fun JsonObject.requiredString(name: String): String =
            optionalString(name)?.takeIf(String::isNotBlank)
                ?: throw IllegalArgumentException("The worker request is missing '$name'.")

        private fun JsonObject.optionalString(name: String): String? =
            get(name)?.takeUnless { it.isJsonNull }?.asString

        private fun JsonObject.stringList(name: String): List<String> {
            val value = get(name) ?: return emptyList()
            require(value.isJsonArray) { "The worker request field '$name' must be an array." }
            return value.asJsonArray.map { element ->
                require(element.isJsonPrimitive && element.asJsonPrimitive.isString) {
                    "The worker request field '$name' must contain only strings."
                }
                element.asString
            }
        }
    }
}
