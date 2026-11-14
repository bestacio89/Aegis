package org.aegis.worker

import com.google.gson.JsonArray
import com.google.gson.JsonObject
import org.jetbrains.kotlin.cli.common.messages.CompilerMessageSeverity
import org.jetbrains.kotlin.cli.common.messages.CompilerMessageSourceLocation
import org.jetbrains.kotlin.cli.common.messages.MessageCollector

class KotlinCompilerMessageCollector : MessageCollector {
    private var items = JsonArray()
    private var containsErrors = false

    override fun clear() {
        items = JsonArray()
        containsErrors = false
    }

    override fun hasErrors(): Boolean = containsErrors

    override fun report(
        severity: CompilerMessageSeverity,
        message: String,
        location: CompilerMessageSourceLocation?
    ) {
        val isError = severity == CompilerMessageSeverity.ERROR
            || severity == CompilerMessageSeverity.EXCEPTION
        containsErrors = containsErrors || isError

        val diagnostic = JsonObject()
        diagnostic.addProperty("code", "KOTLIN-${severity.name}")
        diagnostic.addProperty("component", "KotlinCompiler")
        diagnostic.addProperty("message", message.take(MAX_MESSAGE_LENGTH))
        diagnostic.addProperty("target", location?.path ?: "")
        diagnostic.addProperty("severity", if (isError) "Error" else if (
            severity == CompilerMessageSeverity.WARNING
                || severity == CompilerMessageSeverity.STRONG_WARNING
        ) "Warning" else "Info")
        diagnostic.addProperty("line", location?.line ?: -1)
        diagnostic.addProperty("column", location?.column ?: -1)
        diagnostic.addProperty("affectsCoverage", isError)
        items.add(diagnostic)
    }

    fun diagnostics(): JsonArray = items.deepCopy()

    companion object {
        private const val MAX_MESSAGE_LENGTH = 8_192
    }
}
