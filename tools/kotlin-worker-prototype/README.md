# Aegis Kotlin Semantic Worker

This is the isolated Kotlin/JVM semantic-analysis worker used by Aegis. It is intentionally version-pinned and does not execute Gradle, Maven, Kotlin build scripts, annotation processors, KAPT, or KSP.

## Build and verify

Requires JDK 21 and Maven.

```powershell
mvn --batch-mode --no-transfer-progress -f tools/kotlin-worker-prototype/pom.xml verify
```

The build runs the compiler integration and process-protocol tests, assembles a private runtime under `target/runtime`, and generates a CycloneDX SBOM at `target/bom.json`.

The packaged runtime contains:

- `runtime/kotlin-worker.jar` — worker entry point and compiler-plugin registrar.
- `runtime/lib/` — pinned Kotlin compiler, Kotlin standard library, Gson, and runtime dependencies.

The JAR manifest declares the relative runtime classpath. The packaged worker is launched with:

```powershell
java -jar target/runtime/kotlin-worker.jar
```

The worker uses JSON Lines over standard input/output. Standard output is reserved for protocol frames; compiler diagnostics are returned in the analysis response.

## Runtime contract

Every analysis request supplies `kotlinStandardLibraryPath` explicitly. The host resolves this to the standard-library JAR inside the packaged runtime and passes it separately from project dependency classpath entries. This prevents the worker from guessing its runtime layout and keeps compiler runtime dependencies distinct from the analyzed project's dependencies.

The compiler version is pinned in `pom.xml` and in the worker protocol. Project compiler/language compatibility is validated by the worker; unsupported versions must produce an explicit unsupported/partial result rather than a guessed analysis.

## Packaged process probe

```powershell
./tools/kotlin-worker-prototype/packaging/Probe-PackagedWorker.ps1
```

The probe launches the packaged JAR (not Maven's test classpath), performs a handshake, compiles a Kotlin fixture, verifies semantic output, and requests a clean shutdown. CI runs this probe on Linux and Windows.

## Design boundary

The worker extracts semantic evidence from the Kotlin compiler's K2 IR before backend lowering. The IR extension is isolated behind this worker because compiler-plugin APIs are version-sensitive. The host owns source discovery, build metadata, classpath resolution, process supervision, cancellation, timeouts, and mapping into Aegis's language-neutral semantic contracts.

The worker enforces a 16 MiB source-manifest limit, a 50,000-file limit, 1,024 source roots, 2,048 referenced source roots, 8,192 classpath entries, 16 MiB per source file, 512 MiB aggregate source bytes, and bounded JSON Lines request/response frames. The host applies the same admission limits before manifest staging. The supervised JVM uses fixed ceilings of 64 MiB initial heap, 1 GiB maximum heap, 384 MiB metaspace, 128 MiB direct memory, and two active processors, with exit on out-of-memory. These JVM flags are process-level constraints, not an OS-level sandbox or a guarantee of total process memory/CPU isolation. Compiler cancellation is performed by terminating the supervised worker process, not by interrupting compiler internals.

The worker does not infer architecture from package names. Logical architectural classification remains in Aegis's shared architecture pipeline.
