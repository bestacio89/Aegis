# Java worker protocol prototype

This is an isolated J2-A5.3 operational prototype. It is not a production worker and does not contain either semantic engine.

## Contract

- Protocol: versioned JSON Lines over stdin/stdout; no listening socket.
- Encoding: UTF-8, one JSON object per line.
- Frame limit: 1 MiB by default, enforced in bytes; oversized frames are drained before processing the next frame. UTF-8 and JSON syntax are validated strictly.
- Request IDs: required, at most 128 characters, unique for the lifetime of a worker process.
- Handshake: mandatory first valid request; protocol version must match exactly.
- Responses: carry protocol version, request ID, status, and either result or structured error.
- Concurrency: bounded executor and bounded pending queue; responses may complete out of order and are correlated by request ID. A session is capped at 10,000 request IDs and must then be recycled.
- Cancellation: a cancel request identifies an active analysis request by targetRequestId; cancellation interrupts the handler thread and guarantees a terminal cancellation response, including when a queued task has not started.
- Shutdown: explicit shutdown request; outstanding work is interrupted and the process exits.
- Diagnostics: stdout is reserved for protocol frames. Logs and stack traces must never be written to stdout.

## Operations

| Operation | Purpose |
|---|---|
| handshake | Negotiate protocol version and receive configured limits |
| ping | Liveness probe |
| capabilities | Report whether an analysis engine is attached |
| analyze | Dispatch a bounded analysis payload to the injected handler |
| cancel | Interrupt an active request |
| shutdown | Stop the worker and cancel outstanding requests |

The current executable deliberately reports analysisAvailable=false. No JDT or JavaParser implementation is attached. This prevents the protocol spike from being mistaken for compiler-grade semantic analysis.

## Build and test

    mvn --batch-mode --no-transfer-progress -f tools/java-worker-prototype/pom.xml verify

## Security boundary

The protocol prototype does not provide an OS sandbox. A supervising host must still enforce process-tree lifetime, wall-clock deadlines, memory/CPU limits, restricted identity, workspace and classpath allowlists, and filesystem/network policy. The worker must never execute project build scripts, Maven/Gradle plugins, or annotation processors. Any future cache must be scoped to a single workspace/session and must not reuse project-derived state across tenants.

## Deliberate non-goals

- No parser dependency or production Aegis integration.
- No claim that thread interruption can forcibly stop arbitrary native or non-cooperative code.
- No promise that the worker can enforce its own memory/CPU budget.
- No semantic analysis until a candidate engine is selected and connected behind the handler contract.
