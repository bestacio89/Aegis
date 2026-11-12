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


## Cross-platform private-runtime process probe

The Java semantic workflow's existing Windows/Linux packaging matrix now compiles this worker, resolves its runtime-only dependency closure, includes its classes and Gson runtime dependency closure in the minimized Java 21 package, and invokes `packaging/Probe-WorkerProcess.ps1` using that private runtime.

The process-level probe verifies:

- Actual JVM process startup and protocol handshake.
- Capability reporting without claiming an attached semantic engine.
- Liveness response and request correlation.
- Malformed JSON rejection without terminating the session.
- Oversized-frame rejection and continued processing.
- Unsupported protocol-version rejection.
- Graceful shutdown with exit code zero.
- Forced process-tree termination followed by a successful replacement worker handshake and shutdown.

The probe records per-platform launch/handshake timings and the worker package size. It is operational feasibility evidence, not a .NET supervisor implementation or an OS sandbox. It does not prove analysis-request deadlines, native-code termination, CPU/memory enforcement, filesystem/network restrictions, or cross-workspace cache isolation.


## Recorded CI result (2026-11-12)

The seven protocol tests passed. The worker was also launched from the minimized private Java 21 runtime on Windows and Linux; both process probes passed handshake, capabilities, ping, malformed/oversized-frame handling, protocol-version rejection, graceful shutdown, forced termination and replacement-worker startup.

The worker remains protocol-only and reports no attached semantic engine. See the [Windows artifact](https://github.com/bestacio89/Aegis/actions/runs/43207732837/artifacts/13939951010), [Linux artifact](https://github.com/bestacio89/Aegis/actions/runs/43207732837/artifacts/13939681044), and [Java comparison/SBOM artifact](https://github.com/bestacio89/Aegis/actions/runs/43207732837/artifacts/13940141303).
