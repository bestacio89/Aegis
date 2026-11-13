package org.aegis.worker

import com.google.gson.Gson
import com.google.gson.JsonObject
import com.google.gson.JsonParser
import com.google.gson.Strictness
import com.google.gson.stream.JsonReader
import com.google.gson.stream.JsonToken
import java.io.ByteArrayOutputStream
import java.io.InputStream
import java.io.OutputStream
import java.io.StringReader
import java.nio.ByteBuffer
import java.nio.charset.CodingErrorAction
import java.nio.charset.StandardCharsets

class KotlinWorkerServer(
    private val operationHandler: KotlinWorkerOperationHandler,
    private val maxFrameBytes: Int = DEFAULT_MAX_FRAME_BYTES,
    private val maxResponseBytes: Int = DEFAULT_MAX_RESPONSE_BYTES
) {
    init {
        require(maxFrameBytes >= 256) { "The maximum request frame must be at least 256 bytes." }
        require(maxResponseBytes >= 256) { "The maximum response frame must be at least 256 bytes." }
    }

    fun serve(input: InputStream, output: OutputStream) {
        val gson = Gson()
        val seenRequestIds = HashSet<String>()
        var handshakeComplete = false
        var requestCount = 0

        while (true) {
            val frame = readFrame(input) ?: return
            if (frame.tooLarge) {
                writeResponse(output, gson, KotlinWorkerResponse.failure(
                    null,
                    "FRAME_TOO_LARGE",
                    "The JSON Lines frame exceeds the configured byte limit."
                ))
                continue
            }

            val requestText = try {
                decode(frame.bytes)
            } catch (_: Exception) {
                writeResponse(output, gson, KotlinWorkerResponse.failure(
                    null,
                    "INVALID_UTF8",
                    "The frame is not valid UTF-8."
                ))
                continue
            }

            val request = try {
                parseRequest(requestText)
            } catch (_: Exception) {
                writeResponse(output, gson, KotlinWorkerResponse.failure(
                    null,
                    "INVALID_JSON",
                    "The frame does not contain exactly one valid worker request."
                ))
                continue
            }

            val requestId = request.requestId
            if (requestId.isNullOrBlank() || requestId.length > MAX_REQUEST_ID_LENGTH) {
                writeResponse(output, gson, KotlinWorkerResponse.failure(
                    null,
                    "INVALID_REQUEST_ID",
                    "A non-empty requestId of at most 128 characters is required."
                ))
                continue
            }

            if (requestCount >= MAX_REQUESTS_PER_SESSION) {
                writeResponse(output, gson, KotlinWorkerResponse.failure(
                    requestId,
                    "SESSION_REQUEST_LIMIT_REACHED",
                    "The worker session reached its request limit and must be recycled."
                ))
                continue
            }
            requestCount++

            if (!seenRequestIds.add(requestId)) {
                writeResponse(output, gson, KotlinWorkerResponse.failure(
                    requestId,
                    "DUPLICATE_REQUEST_ID",
                    "Request identifiers must be unique within a worker session."
                ))
                continue
            }

            if (request.protocolVersion != PROTOCOL_VERSION) {
                writeResponse(output, gson, KotlinWorkerResponse.failure(
                    requestId,
                    "UNSUPPORTED_PROTOCOL_VERSION",
                    "The requested protocol version is not supported."
                ))
                continue
            }

            val operation = request.operation
            if (operation.isNullOrBlank()) {
                writeResponse(output, gson, KotlinWorkerResponse.failure(
                    requestId,
                    "INVALID_OPERATION",
                    "An operation is required."
                ))
                continue
            }

            if (!handshakeComplete && operation != "handshake") {
                writeResponse(output, gson, KotlinWorkerResponse.failure(
                    requestId,
                    "HANDSHAKE_REQUIRED",
                    "The protocol handshake must complete before other operations."
                ))
                continue
            }

            when (operation) {
                "handshake" -> {
                    if (handshakeComplete) {
                        writeResponse(output, gson, KotlinWorkerResponse.failure(
                            requestId,
                            "HANDSHAKE_ALREADY_COMPLETED",
                            "Only one handshake is allowed per worker session."
                        ))
                        continue
                    }

                    handshakeComplete = true
                    val result = JsonObject().apply {
                        addProperty("protocolVersion", PROTOCOL_VERSION)
                        addProperty("maxFrameBytes", maxFrameBytes)
                        addProperty("maxResponseBytes", maxResponseBytes)
                        addProperty("maxConcurrentRequests", 1)
                    }
                    writeResponse(output, gson, KotlinWorkerResponse.success(requestId, result))
                }

                "ping" -> {
                    writeResponse(output, gson, KotlinWorkerResponse.success(
                        requestId,
                        JsonObject().apply { addProperty("alive", true) }
                    ))
                }

                "capabilities" -> {
                    val result = JsonObject().apply {
                        addProperty("analysisAvailable", operationHandler.isAvailable())
                        addProperty("engineId", "kotlin-k2-ir")
                        addProperty("engineVersion", KOTLIN_COMPILER_VERSION)
                        add("operations", gson.toJsonTree(OPERATIONS))
                    }
                    writeResponse(output, gson, KotlinWorkerResponse.success(requestId, result))
                }

                "analyze" -> {
                    if (!operationHandler.isAvailable()) {
                        writeResponse(output, gson, KotlinWorkerResponse.failure(
                            requestId,
                            "ANALYSIS_ENGINE_NOT_CONFIGURED",
                            "The Kotlin semantic analysis engine is not configured."
                        ))
                        continue
                    }

                    try {
                        writeResponse(output, gson, KotlinWorkerResponse.success(
                            requestId,
                            operationHandler.analyze(request.payload)
                        ))
                    } catch (exception: Exception) {
                        System.err.println(
                            "Kotlin semantic analysis failed: ${exception.javaClass.simpleName}"
                        )
                        writeResponse(output, gson, KotlinWorkerResponse.failure(
                            requestId,
                            "ANALYSIS_FAILED",
                            "The Kotlin semantic analysis request failed."
                        ))
                    }
                }

                "shutdown" -> {
                    writeResponse(output, gson, KotlinWorkerResponse.success(
                        requestId,
                        JsonObject().apply { addProperty("shutdown", true) }
                    ))
                    return
                }

                else -> writeResponse(output, gson, KotlinWorkerResponse.failure(
                    requestId,
                    "UNSUPPORTED_OPERATION",
                    "The requested worker operation is not supported."
                ))
            }
        }
    }

    private fun readFrame(input: InputStream): Frame? {
        val buffer = ByteArrayOutputStream()
        var tooLarge = false
        var sawData = false

        while (true) {
            val value = input.read()
            if (value < 0) {
                if (!sawData) return null
                return Frame(trimCarriageReturn(buffer.toByteArray()), tooLarge)
            }
            if (value == NEW_LINE) {
                return Frame(trimCarriageReturn(buffer.toByteArray()), tooLarge)
            }

            sawData = true
            if (buffer.size() < maxFrameBytes) {
                buffer.write(value)
            } else {
                tooLarge = true
            }
        }
    }

    private fun trimCarriageReturn(bytes: ByteArray): ByteArray =
        if (bytes.isNotEmpty() && bytes.last() == CARRIAGE_RETURN)
            bytes.copyOf(bytes.size - 1)
        else
            bytes

    private fun decode(bytes: ByteArray): String =
        StandardCharsets.UTF_8.newDecoder()
            .onMalformedInput(CodingErrorAction.REPORT)
            .onUnmappableCharacter(CodingErrorAction.REPORT)
            .decode(ByteBuffer.wrap(bytes))
            .toString()

    private fun parseRequest(text: String): KotlinWorkerRequest {
        JsonReader(StringReader(text)).use { reader ->
            reader.setStrictness(Strictness.STRICT)
            val element = JsonParser.parseReader(reader)
            require(element.isJsonObject && reader.peek() == JsonToken.END_DOCUMENT)
            return KotlinWorkerRequest.from(element.asJsonObject)
        }
    }

    private fun writeResponse(
        output: OutputStream,
        gson: Gson,
        response: KotlinWorkerResponse
    ) {
        val json = gson.toJson(response)
        val bytes = json.toByteArray(StandardCharsets.UTF_8)
        val boundedBytes = if (bytes.size <= maxResponseBytes) {
            bytes
        } else {
            gson.toJson(
                KotlinWorkerResponse.failure(
                    response.requestId,
                    "RESPONSE_TOO_LARGE",
                    "The analysis response exceeds the configured byte limit."
                )
            ).toByteArray(StandardCharsets.UTF_8)
        }

        output.write(boundedBytes)
        output.write(NEW_LINE)
        output.flush()
    }

    private data class Frame(
        val bytes: ByteArray,
        val tooLarge: Boolean
    )

    companion object {
        const val PROTOCOL_VERSION = 1
        const val DEFAULT_MAX_FRAME_BYTES = 1_048_576
        const val DEFAULT_MAX_RESPONSE_BYTES = 67_108_864
        const val MAX_REQUESTS_PER_SESSION = 10_000
        const val MAX_REQUEST_ID_LENGTH = 128
        const val KOTLIN_COMPILER_VERSION = "2.4.21"

        private const val NEW_LINE = 10
        private const val CARRIAGE_RETURN = 13
        private val OPERATIONS = listOf("handshake", "ping", "capabilities", "analyze", "shutdown")
    }
}
