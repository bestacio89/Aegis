package org.aegis.worker;

import com.google.gson.JsonObject;

public record WorkerRequest(
    int protocolVersion,
    String requestId,
    String operation,
    String targetRequestId,
    JsonObject payload) {

    public static WorkerRequest from(JsonObject json) {
        return new WorkerRequest(
            json.has("protocolVersion") ? json.get("protocolVersion").getAsInt() : 0,
            json.has("requestId") && !json.get("requestId").isJsonNull() ? json.get("requestId").getAsString() : null,
            json.has("operation") && !json.get("operation").isJsonNull() ? json.get("operation").getAsString() : null,
            json.has("targetRequestId") && !json.get("targetRequestId").isJsonNull() ? json.get("targetRequestId").getAsString() : null,
            json.has("payload") && json.get("payload").isJsonObject() ? json.getAsJsonObject("payload") : new JsonObject());
    }
}