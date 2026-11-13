package org.aegis.worker;

import com.google.gson.Gson;
import com.google.gson.JsonObject;
import org.junit.jupiter.api.Test;

import java.io.BufferedReader;
import java.io.BufferedWriter;
import java.io.IOException;
import java.io.ByteArrayInputStream;
import java.io.ByteArrayOutputStream;
import java.io.InputStreamReader;
import java.io.OutputStreamWriter;
import java.io.PipedInputStream;
import java.io.PipedOutputStream;
import java.nio.charset.StandardCharsets;
import java.util.List;
import java.util.concurrent.CompletableFuture;
import java.util.concurrent.TimeUnit;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertTrue;

final class WorkerServerTests {
    private static final Gson GSON = new Gson();

    @Test
    void handshake_and_ping_return_correlated_protocol_responses() throws Exception {
        String input = """
            {"protocolVersion":1,"requestId":"handshake-1","operation":"handshake"}
            {"protocolVersion":1,"requestId":"ping-1","operation":"ping"}
            """;
        List<JsonObject> responses = execute(input, 4096);
        assertEquals(2, responses.size());
        assertEquals("handshake-1", responses.get(0).get("requestId").getAsString());
        assertEquals("ok", responses.get(0).get("status").getAsString());
        assertEquals(1, responses.get(0).getAsJsonObject("result").get("protocolVersion").getAsInt());
        assertEquals("ping-1", responses.get(1).get("requestId").getAsString());
        assertTrue(responses.get(1).getAsJsonObject("result").get("alive").getAsBoolean());
    }

    @Test
    void operations_are_rejected_before_handshake() throws Exception {
        List<JsonObject> responses = execute("""
            {"protocolVersion":1,"requestId":"ping-1","operation":"ping"}
            """, 4096);
        assertEquals("HANDSHAKE_REQUIRED", responses.get(0).getAsJsonObject("error").get("code").getAsString());
    }

    @Test
    void malformed_json_does_not_terminate_the_worker_session() throws Exception {
        List<JsonObject> responses = execute("""
            {not-json}
            {"protocolVersion":1,"requestId":"handshake-1","operation":"handshake"}
            """, 4096);
        assertEquals("INVALID_JSON", responses.get(0).getAsJsonObject("error").get("code").getAsString());
        assertEquals("ok", responses.get(1).get("status").getAsString());
    }

    @Test
    void oversized_frame_is_drained_and_following_frames_are_processed() throws Exception {
        String input = "x".repeat(300) + "\n"
            + "{\"protocolVersion\":1,\"requestId\":\"handshake-1\",\"operation\":\"handshake\"}\n";
        List<JsonObject> responses = execute(input, 256);
        assertEquals("FRAME_TOO_LARGE", responses.get(0).getAsJsonObject("error").get("code").getAsString());
        assertEquals("handshake-1", responses.get(1).get("requestId").getAsString());
    }

    @Test
    void duplicate_request_ids_are_rejected_within_a_session() throws Exception {
        List<JsonObject> responses = execute("""
            {"protocolVersion":1,"requestId":"handshake-1","operation":"handshake"}
            {"protocolVersion":1,"requestId":"ping-1","operation":"ping"}
            {"protocolVersion":1,"requestId":"ping-1","operation":"ping"}
            """, 4096);
        assertEquals("DUPLICATE_REQUEST_ID", responses.get(2).getAsJsonObject("error").get("code").getAsString());
    }

    @Test
    void unavailable_analysis_engine_is_reported_without_claiming_analysis() throws Exception {
        List<JsonObject> responses = execute("""
            {"protocolVersion":1,"requestId":"handshake-1","operation":"handshake"}
            {"protocolVersion":1,"requestId":"capabilities-1","operation":"capabilities"}
            {"protocolVersion":1,"requestId":"analysis-1","operation":"analyze"}
            """, 4096);
        assertFalse(responses.get(1).getAsJsonObject("result").get("analysisAvailable").getAsBoolean());
        assertEquals("ANALYSIS_ENGINE_NOT_CONFIGURED", responses.get(2).getAsJsonObject("error").get("code").getAsString());
    }

    @Test
    void cancellation_returns_a_terminal_response_for_the_target_request() throws Exception {
        String input = """
            {"protocolVersion":1,"requestId":"handshake-1","operation":"handshake"}
            {"protocolVersion":1,"requestId":"analysis-1","operation":"analyze"}
            {"protocolVersion":1,"requestId":"cancel-1","operation":"cancel","targetRequestId":"analysis-1"}
            """;
        WorkerOperationHandler blockingHandler = new WorkerOperationHandler() {
            @Override
            public boolean isAvailable() { return true; }

            @Override
            public JsonObject analyze(JsonObject payload) throws InterruptedException {
                Thread.sleep(30_000);
                return new JsonObject();
            }
        };

        List<JsonObject> responses = execute(input, 4096, blockingHandler);
        JsonObject cancellation = responses.stream()
            .filter(response -> response.has("requestId") && response.get("requestId").getAsString().equals("cancel-1"))
            .findFirst().orElseThrow();
        JsonObject analysis = responses.stream()
            .filter(response -> response.has("requestId") && response.get("requestId").getAsString().equals("analysis-1"))
            .findFirst().orElseThrow();

        assertTrue(cancellation.getAsJsonObject("result").get("cancellationAccepted").getAsBoolean());
        assertEquals("REQUEST_CANCELLED", analysis.getAsJsonObject("error").get("code").getAsString());
    }

    @Test
    void oversized_analysis_response_is_replaced_with_a_bounded_protocol_error() throws Exception {
        WorkerOperationHandler largeResultHandler = new WorkerOperationHandler() {
            @Override
            public boolean isAvailable() { return true; }

            @Override
            public JsonObject analyze(JsonObject payload) {
                var result = new JsonObject();
                result.addProperty("large", "x".repeat(2048));
                return result;
            }
        };

        var workerInput = new PipedInputStream();
        var requestWriter = new PipedOutputStream(workerInput);
        var responseInput = new PipedInputStream();
        var workerOutput = new PipedOutputStream(responseInput);
        var serverTask = CompletableFuture.runAsync(() -> {
            try {
                new WorkerServer(largeResultHandler, 4096, 2, 512).serve(workerInput, workerOutput);
            } catch (IOException exception) {
                throw new IllegalStateException("The worker protocol server failed.", exception);
            }
        });

        JsonObject analysisResponse;
        try (var writer = new BufferedWriter(new OutputStreamWriter(requestWriter, StandardCharsets.UTF_8));
             var reader = new BufferedReader(new InputStreamReader(responseInput, StandardCharsets.UTF_8))) {
            var handshakeRequest = new JsonObject();
            handshakeRequest.addProperty("protocolVersion", 1);
            handshakeRequest.addProperty("requestId", "handshake-1");
            handshakeRequest.addProperty("operation", "handshake");
            writer.write(GSON.toJson(handshakeRequest));
            writer.newLine();
            writer.flush();

            JsonObject handshake = GSON.fromJson(reader.readLine(), JsonObject.class);
            assertEquals("ok", handshake.get("status").getAsString());

            var analysisRequest = new JsonObject();
            analysisRequest.addProperty("protocolVersion", 1);
            analysisRequest.addProperty("requestId", "analysis-1");
            analysisRequest.addProperty("operation", "analyze");
            writer.write(GSON.toJson(analysisRequest));
            writer.newLine();
            writer.flush();
            analysisResponse = GSON.fromJson(reader.readLine(), JsonObject.class);
        }

        serverTask.get(5, TimeUnit.SECONDS);
        workerOutput.close();

        assertEquals("analysis-1", analysisResponse.get("requestId").getAsString());
        assertEquals("RESPONSE_TOO_LARGE",
            analysisResponse.getAsJsonObject("error").get("code").getAsString());
    }

    private static List<JsonObject> execute(String input, int maxFrameBytes) throws Exception {
        WorkerOperationHandler unavailableHandler = new WorkerOperationHandler() {
            @Override
            public boolean isAvailable() { return false; }

            @Override
            public JsonObject analyze(JsonObject payload) { throw new UnsupportedOperationException(); }
        };
        return execute(input, maxFrameBytes, unavailableHandler);
    }

    private static List<JsonObject> execute(
        String input,
        int maxFrameBytes,
        WorkerOperationHandler handler) throws Exception {
        var output = new ByteArrayOutputStream();
        new WorkerServer(handler, maxFrameBytes, 2).serve(
            new ByteArrayInputStream(input.getBytes(StandardCharsets.UTF_8)), output);
        return output.toString(StandardCharsets.UTF_8).lines()
            .map(line -> GSON.fromJson(line, JsonObject.class))
            .toList();
    }
}
