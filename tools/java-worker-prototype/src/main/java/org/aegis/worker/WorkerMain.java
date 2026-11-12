package org.aegis.worker;

import com.google.gson.JsonObject;

public final class WorkerMain {
    private WorkerMain() { }

    public static void main(String[] args) throws Exception {
        WorkerOperationHandler unavailableHandler = new WorkerOperationHandler() {
            @Override
            public boolean isAvailable() { return false; }

            @Override
            public JsonObject analyze(JsonObject payload) {
                throw new UnsupportedOperationException("No semantic engine is attached to the protocol prototype.");
            }
        };

        var server = new WorkerServer(
            unavailableHandler,
            WorkerServer.DEFAULT_MAX_FRAME_BYTES,
            WorkerServer.DEFAULT_MAX_CONCURRENT_REQUESTS);
        server.serve(System.in, System.out);
    }
}