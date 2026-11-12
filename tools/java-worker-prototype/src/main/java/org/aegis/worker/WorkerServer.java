package org.aegis.worker;

import com.google.gson.Gson;
import com.google.gson.JsonObject;
import com.google.gson.JsonParser;
import com.google.gson.Strictness;
import com.google.gson.stream.JsonReader;
import com.google.gson.stream.JsonToken;

import java.io.StringReader;

import java.io.ByteArrayOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.nio.ByteBuffer;
import java.nio.charset.CharacterCodingException;
import java.nio.charset.CodingErrorAction;
import java.nio.charset.StandardCharsets;
import java.util.Set;
import java.util.concurrent.ArrayBlockingQueue;
import java.util.concurrent.ConcurrentHashMap;
import java.util.concurrent.Future;
import java.util.concurrent.FutureTask;
import java.util.concurrent.RejectedExecutionException;
import java.util.concurrent.ThreadPoolExecutor;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.atomic.AtomicBoolean;

public final class WorkerServer {
    public static final int PROTOCOL_VERSION = 1;
    public static final int DEFAULT_MAX_FRAME_BYTES = 1_048_576;
    public static final int DEFAULT_MAX_CONCURRENT_REQUESTS = 2;
    public static final int MAX_REQUESTS_PER_SESSION = 10_000;

    private static final Gson GSON = new Gson();
    private static final Set<String> OPERATIONS = Set.of("handshake", "ping", "capabilities", "analyze", "cancel", "shutdown");

    private final WorkerOperationHandler operationHandler;
    private final int maxFrameBytes;
    private final int maxConcurrentRequests;

    public WorkerServer(WorkerOperationHandler operationHandler, int maxFrameBytes, int maxConcurrentRequests) {
        if (operationHandler == null) throw new IllegalArgumentException("An operation handler is required.");
        if (maxFrameBytes < 256) throw new IllegalArgumentException("The maximum frame size must be at least 256 bytes.");
        if (maxConcurrentRequests < 1) throw new IllegalArgumentException("At least one concurrent request slot is required.");
        this.operationHandler = operationHandler;
        this.maxFrameBytes = maxFrameBytes;
        this.maxConcurrentRequests = maxConcurrentRequests;
    }

    public void serve(InputStream input, OutputStream output) throws IOException {
        var writer = new ProtocolWriter(output);
        var executor = new ThreadPoolExecutor(
            maxConcurrentRequests,
            maxConcurrentRequests,
            0L,
            TimeUnit.MILLISECONDS,
            new ArrayBlockingQueue<>(maxConcurrentRequests));
        var activeRequests = new ConcurrentHashMap<String, Future<?>>();
        var seenRequestIds = ConcurrentHashMap.<String>newKeySet();
        boolean handshakeComplete = false;

        try {
            Frame frame;
            while ((frame = readFrame(input)) != null) {
                if (frame.tooLarge()) {
                    writer.write(WorkerResponse.failure(null, "FRAME_TOO_LARGE", "The JSON Lines frame exceeds the configured byte limit."));
                    continue;
                }

                String jsonText;
                try {
                    jsonText = decode(frame.bytes());
                } catch (CharacterCodingException exception) {
                    writer.write(WorkerResponse.failure(null, "INVALID_UTF8", "The frame is not valid UTF-8."));
                    continue;
                }

                WorkerRequest request;
                try {
                    try (var jsonReader = new JsonReader(new StringReader(jsonText))) {
                        jsonReader.setStrictness(Strictness.STRICT);
                        var element = JsonParser.parseReader(jsonReader);
                        if (!element.isJsonObject() || jsonReader.peek() != JsonToken.END_DOCUMENT) {
                            throw new IllegalArgumentException("Exactly one JSON object is required per frame.");
                        }
                        request = WorkerRequest.from(element.getAsJsonObject());
                    }
                } catch (RuntimeException exception) {
                    writer.write(WorkerResponse.failure(null, "INVALID_JSON", "The frame does not contain a valid worker request."));
                    continue;
                }

                if (!isValidRequestId(request.requestId())) {
                    writer.write(WorkerResponse.failure(null, "INVALID_REQUEST_ID", "A non-empty requestId of at most 128 characters is required."));
                    continue;
                }
                if (seenRequestIds.size() >= MAX_REQUESTS_PER_SESSION) {
                    writer.write(WorkerResponse.failure(request.requestId(), "SESSION_REQUEST_LIMIT_REACHED",
                        "The worker session reached its request limit and must be recycled."));
                    continue;
                }
                if (!seenRequestIds.add(request.requestId())) {
                    writer.write(WorkerResponse.failure(request.requestId(), "DUPLICATE_REQUEST_ID", "Request identifiers must be unique within a worker session."));
                    continue;
                }
                if (request.protocolVersion() != PROTOCOL_VERSION) {
                    writer.write(WorkerResponse.failure(request.requestId(), "UNSUPPORTED_PROTOCOL_VERSION", "The requested protocol version is not supported."));
                    continue;
                }
                if (request.operation() == null || request.operation().isBlank()) {
                    writer.write(WorkerResponse.failure(request.requestId(), "INVALID_OPERATION", "An operation is required."));
                    continue;
                }
                if (!handshakeComplete && !request.operation().equals("handshake")) {
                    writer.write(WorkerResponse.failure(request.requestId(), "HANDSHAKE_REQUIRED", "The protocol handshake must complete before other operations."));
                    continue;
                }

                switch (request.operation()) {
                    case "handshake" -> {
                        if (handshakeComplete) {
                            writer.write(WorkerResponse.failure(request.requestId(), "HANDSHAKE_ALREADY_COMPLETED", "Only one handshake is allowed per worker session."));
                            continue;
                        }
                        handshakeComplete = true;
                        var result = new JsonObject();
                        result.addProperty("protocolVersion", PROTOCOL_VERSION);
                        result.addProperty("maxFrameBytes", maxFrameBytes);
                        result.addProperty("maxConcurrentRequests", maxConcurrentRequests);
                        writer.write(WorkerResponse.success(request.requestId(), result));
                    }
                    case "ping" -> {
                        var result = new JsonObject();
                        result.addProperty("alive", true);
                        writer.write(WorkerResponse.success(request.requestId(), result));
                    }
                    case "capabilities" -> {
                        var result = new JsonObject();
                        result.addProperty("analysisAvailable", operationHandler.isAvailable());
                        result.add("operations", GSON.toJsonTree(OPERATIONS));
                        writer.write(WorkerResponse.success(request.requestId(), result));
                    }
                    case "analyze" -> {
                        if (!operationHandler.isAvailable()) {
                            writer.write(WorkerResponse.failure(request.requestId(), "ANALYSIS_ENGINE_NOT_CONFIGURED", "The protocol prototype has no semantic engine attached."));
                            continue;
                        }
                        var responseWritten = new AtomicBoolean();
                        FutureTask<Void> task = new FutureTask<>(() -> {
                            try {
                                JsonObject result = operationHandler.analyze(request.payload());
                                if (responseWritten.compareAndSet(false, true)) {
                                    writer.write(WorkerResponse.success(request.requestId(), result == null ? new JsonObject() : result));
                                }
                            } catch (InterruptedException exception) {
                                Thread.currentThread().interrupt();
                                if (responseWritten.compareAndSet(false, true)) {
                                    writer.write(WorkerResponse.failure(request.requestId(), "REQUEST_CANCELLED", "The request was cancelled."));
                                }
                            } catch (Exception exception) {
                                if (responseWritten.compareAndSet(false, true)) {
                                    writer.write(WorkerResponse.failure(request.requestId(), "ANALYSIS_FAILED", "The analysis request failed."));
                                }
                            } finally {
                                activeRequests.remove(request.requestId());
                            }
                            return null;
                        }) {
                            @Override
                            protected void done() {
                                activeRequests.remove(request.requestId());
                                if (isCancelled() && responseWritten.compareAndSet(false, true)) {
                                    try {
                                        writer.write(WorkerResponse.failure(request.requestId(), "REQUEST_CANCELLED", "The request was cancelled."));
                                    } catch (IOException ignored) {
                                        // The output stream is no longer available; the supervising process observes worker termination.
                                    }
                                }
                            }
                        };
                        activeRequests.put(request.requestId(), task);
                        try {
                            executor.execute(task);
                        } catch (RejectedExecutionException exception) {
                            activeRequests.remove(request.requestId());
                            writer.write(WorkerResponse.failure(request.requestId(), "WORKER_BUSY",
                                "The bounded worker queue is full; retry after capacity becomes available."));
                        }
                    }
                    case "cancel" -> {
                        if (!isValidRequestId(request.targetRequestId())) {
                            writer.write(WorkerResponse.failure(request.requestId(), "INVALID_TARGET_REQUEST_ID", "A valid targetRequestId is required."));
                            continue;
                        }
                        Future<?> target = activeRequests.get(request.targetRequestId());
                        boolean cancelled = target != null && target.cancel(true);
                        var result = new JsonObject();
                        result.addProperty("targetRequestId", request.targetRequestId());
                        result.addProperty("cancellationAccepted", cancelled);
                        writer.write(WorkerResponse.success(request.requestId(), result));
                    }
                    case "shutdown" -> {
                        writer.write(WorkerResponse.success(request.requestId(), new JsonObject()));
                        return;
                    }
                    default -> writer.write(WorkerResponse.failure(request.requestId(), "UNKNOWN_OPERATION", "The operation is not supported."));
                }
            }
        } finally {
            activeRequests.values().forEach(request -> request.cancel(true));
            executor.shutdownNow();
            try {
                executor.awaitTermination(2, TimeUnit.SECONDS);
            } catch (InterruptedException exception) {
                Thread.currentThread().interrupt();
            }
        }
    }

    private Frame readFrame(InputStream input) throws IOException {
        var buffer = new ByteArrayOutputStream(Math.min(maxFrameBytes, 8192));
        boolean tooLarge = false;
        boolean receivedAnyByte = false;
        while (true) {
            int value = input.read();
            if (value == -1) {
                if (!receivedAnyByte && !tooLarge) return null;
                break;
            }
            if (value == '\n') break;
            receivedAnyByte = true;
            if (!tooLarge) {
                if (buffer.size() == maxFrameBytes) tooLarge = true;
                else buffer.write(value);
            }
        }
        byte[] bytes = buffer.toByteArray();
        if (!tooLarge && bytes.length > 0 && bytes[bytes.length - 1] == '\r') {
            bytes = java.util.Arrays.copyOf(bytes, bytes.length - 1);
        }
        return new Frame(bytes, tooLarge);
    }

    private static String decode(byte[] bytes) throws CharacterCodingException {
        return StandardCharsets.UTF_8.newDecoder()
            .onMalformedInput(CodingErrorAction.REPORT)
            .onUnmappableCharacter(CodingErrorAction.REPORT)
            .decode(ByteBuffer.wrap(bytes)).toString();
    }

    private static boolean isValidRequestId(String requestId) {
        return requestId != null && !requestId.isBlank() && requestId.length() <= 128;
    }

    private record Frame(byte[] bytes, boolean tooLarge) { }

    private static final class ProtocolWriter {
        private final OutputStream output;
        private ProtocolWriter(OutputStream output) { this.output = output; }

        private synchronized void write(WorkerResponse response) throws IOException {
            output.write((GSON.toJson(response) + "\n").getBytes(StandardCharsets.UTF_8));
            output.flush();
        }
    }
}