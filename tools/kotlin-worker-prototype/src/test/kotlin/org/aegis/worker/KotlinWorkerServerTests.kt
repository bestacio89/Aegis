package org.aegis.worker

import com.google.gson.Gson
import com.google.gson.JsonObject
import com.google.gson.JsonParser
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Test
import java.io.ByteArrayInputStream
import java.io.ByteArrayOutputStream
import java.nio.charset.StandardCharsets

class KotlinWorkerServerTests {
    @Test
    fun `worker enforces handshake request identity and operation lifecycle`() {
        var analyzeCount = 0
        val handler = object : KotlinWorkerOperationHandler {
            override fun isAvailable(): Boolean = true

            override fun analyze(payload: JsonObject): JsonObject {
                analyzeCount++
                return JsonObject().apply { addProperty("analyzed", true) }
            }
        }

        val input = frames(
            request("before-handshake", "ping"),
            request("handshake", "handshake"),
            request("analysis", "analyze", JsonObject()),
            request("analysis", "ping"),
            request("shutdown", "shutdown")
        )
        val output = ByteArrayOutputStream()
        KotlinWorkerServer(handler).serve(ByteArrayInputStream(input), output)

        val responses = parseResponses(output)
        assertEquals(5, responses.size)
        assertEquals("HANDSHAKE_REQUIRED", responses[0].getAsJsonObject("error").get("code").asString)
        assertEquals("ok", responses[1].get("status").asString)
        assertEquals("ok", responses[2].get("status").asString)
        assertTrue(responses[2].getAsJsonObject("result").get("analyzed").asBoolean)
        assertEquals("DUPLICATE_REQUEST_ID", responses[3].getAsJsonObject("error").get("code").asString)
        assertEquals("ok", responses[4].get("status").asString)
        assertEquals(1, analyzeCount)
    }

    @Test
    fun `worker rejects oversized frames and oversized responses`() {
        val handler = object : KotlinWorkerOperationHandler {
            override fun isAvailable(): Boolean = true

            override fun analyze(payload: JsonObject): JsonObject =
                JsonObject().apply { addProperty("value", "x".repeat(1_024)) }
        }

        val input = frames(
            "x".repeat(300),
            request("handshake", "handshake"),
            request("analysis", "analyze", JsonObject()),
            request("shutdown", "shutdown")
        )
        val output = ByteArrayOutputStream()
        KotlinWorkerServer(handler, maxFrameBytes = 256, maxResponseBytes = 256)
            .serve(ByteArrayInputStream(input), output)

        val responses = parseResponses(output)
        assertEquals("FRAME_TOO_LARGE", responses[0].getAsJsonObject("error").get("code").asString)
        assertEquals("ok", responses[1].get("status").asString)
        assertEquals("RESPONSE_TOO_LARGE", responses[2].getAsJsonObject("error").get("code").asString)
        assertEquals("ok", responses[3].get("status").asString)
    }

    private fun request(
        requestId: String,
        operation: String,
        payload: JsonObject? = null
    ): String {
        val request = JsonObject().apply {
            addProperty("protocolVersion", KotlinWorkerServer.PROTOCOL_VERSION)
            addProperty("requestId", requestId)
            addProperty("operation", operation)
            if (payload == null) add("payload", com.google.gson.JsonNull.INSTANCE)
            else add("payload", payload)
        }
        return Gson().toJson(request)
    }

    private fun frames(vararg values: String): ByteArray =
        (values.joinToString("\n") + "\n").toByteArray(StandardCharsets.UTF_8)

    private fun parseResponses(output: ByteArrayOutputStream) =
        output.toString(StandardCharsets.UTF_8)
            .lineSequence()
            .filter(String::isNotBlank)
            .map { JsonParser.parseString(it).asJsonObject }
            .toList()
}
