package org.aegis.worker;

import com.google.gson.JsonObject;

public interface WorkerOperationHandler {
    boolean isAvailable();

    JsonObject analyze(JsonObject payload) throws Exception;
}