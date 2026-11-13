package org.aegis.worker;

import com.google.gson.JsonObject;

public record WorkerResponse(
    int protocolVersion,
    String requestId,
    String status,
    JsonObject result,
    WorkerError error) {

    public static WorkerResponse success(String requestId, JsonObject result) {
        return new WorkerResponse(WorkerServer.PROTOCOL_VERSION, requestId, "ok", result, null);
    }

    public static WorkerResponse failure(String requestId, String code, String message) {
        return new WorkerResponse(WorkerServer.PROTOCOL_VERSION, requestId, "error", null, new WorkerError(code, message));
    }
}