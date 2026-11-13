package org.aegis.worker

import com.google.gson.JsonObject

data class KotlinWorkerRequest(
    val protocolVersion: Int,
    val requestId: String?,
    val operation: String?,
    val payload: JsonObject
) {
    companion object {
        fun from(json: JsonObject): KotlinWorkerRequest = KotlinWorkerRequest(
            protocolVersion = json.get("protocolVersion")?.takeUnless { it.isJsonNull }?.asInt ?: 0,
            requestId = json.get("requestId")?.takeUnless { it.isJsonNull }?.asString,
            operation = json.get("operation")?.takeUnless { it.isJsonNull }?.asString,
            payload = json.getAsJsonObject("payload") ?: JsonObject()
        )
    }
}
