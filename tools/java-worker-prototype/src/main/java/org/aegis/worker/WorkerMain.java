package org.aegis.worker;

public final class WorkerMain {
    private WorkerMain() { }

    public static void main(String[] args) throws Exception {
        var server = new WorkerServer(
            new JdtSemanticAnalysisEngine(),
            WorkerServer.DEFAULT_MAX_FRAME_BYTES,
            WorkerServer.DEFAULT_MAX_CONCURRENT_REQUESTS,
            WorkerServer.DEFAULT_MAX_RESPONSE_BYTES);
        server.serve(System.in, System.out);
    }
}
