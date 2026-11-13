package org.aegis.worker

import com.google.gson.JsonObject

interface KotlinWorkerOperationHandler {
    fun isAvailable(): Boolean

    fun analyze(payload: JsonObject): JsonObject
}
