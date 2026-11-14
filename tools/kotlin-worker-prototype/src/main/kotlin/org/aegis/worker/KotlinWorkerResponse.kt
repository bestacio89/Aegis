package org.aegis.worker

import com.google.gson.JsonObject

data class KotlinWorkerResponse(
    val protocolVersion: Int,
    val requestId: String?,
    val status: String,
    val result: JsonObject?,
    val error: KotlinWorkerError?
) {
    companion object {
        fun success(requestId: String, result: JsonObject) =
            KotlinWorkerResponse(KotlinWorkerServer.PROTOCOL_VERSION, requestId, "ok", result, null)

        fun failure(requestId: String?, code: String, message: String) =
            KotlinWorkerResponse(
                KotlinWorkerServer.PROTOCOL_VERSION,
                requestId,
                "error",
                null,
                KotlinWorkerError(code, message)
            )
    }
}
