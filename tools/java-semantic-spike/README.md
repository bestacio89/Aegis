# Java semantic-engine spike

This is an isolated J0 benchmark harness, not a production dependency of Aegis.

## Compared engines

- Eclipse JDT Core `3.47.0` (EPL-2.0)
- JavaParser Symbol Solver `3.28.2` (Apache-2.0 or LGPL-3.0, at adopter's choice)

Both engines run against the same checked-in Maven and Gradle Kotlin fixture corpus. The harness materializes only `.java.fixture` source files into a temporary workspace; it does not execute Maven/Gradle fixture builds, plugins, annotation processors, or project build scripts.

## Benchmark passes

The workflow runs each engine in two independent modes:

1. **Source-only:** checked-in source roots and the running JDK only.
2. **Controlled external classpath:** the same source roots plus a separately resolved, pinned dependency manifest in `classpath-pom.xml`.

The classpath manifest is deliberately separate from the fixture Maven and Gradle build files. CI resolves it with Maven's dependency plugin and passes the resulting JAR list to both engines. This avoids executing fixture build scripts and avoids accidentally placing either semantic engine's own implementation dependencies on the analysed project's classpath. The manifest currently covers Spring Context, JUnit Jupiter API and MapStruct API, including their transitives. It is a controlled representative classpath, not a claim that the fixture build models have been fully resolved.

The harness validates every classpath entry before analysis. JDT receives the entries through `ASTParser.setEnvironment`; JavaParser receives the same JARs through `JarTypeSolver`.

## Metrics and interpretation

The JSON output includes:
- discovered and successfully parsed source files;
- parser/compiler diagnostics (the engines expose different diagnostic models, so counts are not directly interchangeable);
- semantic resolution attempts and successful bindings for type references, method calls and constructor calls;
- independently tracked known cross-source type targets;
- independently tracked known third-party type targets;
- number of external JAR entries;
- elapsed analysis time;
- an approximate post-run heap snapshot, not peak memory.

The expected source-type and external-type targets are correctness spot checks, not exhaustive semantic validation. A higher aggregate binding count is not automatically better if an engine incorrectly resolves intentionally missing symbols. The corpus also includes intentionally unresolved names; those must remain unresolved.

Heap snapshots are affected by JVM allocation and GC timing. They are indicative only. Run each engine in a separate process; do not treat one timing sample as a stable performance ranking.

## Run

From the repository root with JDK 21+ and Maven installed:

```bash
mvn -B -f tools/java-semantic-spike/pom.xml compile exec:java \
  -Dexec.args="jdt tests/Aegis.Architecture.Tests/Fixtures/Java"

mvn -B -f tools/java-semantic-spike/pom.xml compile exec:java \
  -Dexec.args="javaparser tests/Aegis.Architecture.Tests/Fixtures/Java"

mvn -B -f tools/java-semantic-spike/classpath-pom.xml \
  org.apache.maven.plugins:maven-dependency-plugin:3.8.1:build-classpath \
  -DincludeScope=test -Dmdep.outputFile=/tmp/aegis-java-classpath.txt

mvn -B -f tools/java-semantic-spike/pom.xml compile exec:java \
  -Dexec.args="jdt tests/Aegis.Architecture.Tests/Fixtures/Java /tmp/aegis-java-classpath.txt"

mvn -B -f tools/java-semantic-spike/pom.xml compile exec:java \
  -Dexec.args="javaparser tests/Aegis.Architecture.Tests/Fixtures/Java /tmp/aegis-java-classpath.txt"
```

Do not select a production engine from this small corpus alone. Add larger real-world repositories, effective Maven/Gradle model resolution, module-path cases and dependency-version conflict cases before final production integration.


## Module visibility gate (J2-A)

The workflow also runs a separate module-aware visibility gate for both engines. Its
`module-visibility.properties` file is an explicit, reviewable projection of the J1
fixture model: module-owned production/test source roots, production project dependencies,
and test-only project dependencies. It does not merge source roots repository-wide.

The gate asserts four boundaries:

- A consumer with a declared project dependency resolves the API module type.
- A module without that dependency does not resolve the API module type.
- A production source set does not resolve a test-only support dependency.
- The consumer test source set does resolve that test-only support dependency.

The manifest is intentionally limited to this controlled fixture. It is not a replacement
for exporting the complete J1 model. External artifact visibility is covered separately by
the bounded J2-A2 fixture below; JPMS module paths and complete Maven/Gradle model semantics
remain open J2-A work.


## Module-owned external artifact visibility (J2-A2)

The module gate now accepts a separate resolved-classpath file for each module and source set. The J1 source-root/project-edge projection remains separate from artifact resolution: the controlled Maven fixture POMs under `classpaths/` produce the artifact snapshots used by this harness. These snapshots are test inputs, **not** a claim that J1 currently exports resolved artifacts or that Aegis resolves production dependencies.

The fixture matrix verifies:

- **Compile scope:** the consumer receives Spring Context and MapStruct on its production compile classpath.
- **Test scope:** JUnit Jupiter is available to the consumer test source set, not its production source set.
- **Runtime-only scope:** Spring JCL is present in the runtime evidence file but is not passed to either engine's compile-time semantic environment; its `Log` type must remain unresolved in production source analysis.
- **Exclusion:** the consumer fixture excludes Spring Core. The generated consumer classpath must not contain a Spring Core JAR, and its `Resource` type must remain unresolved.
- **Conflict mediation:** the conflict fixture requests Spring Context 6.2.8 and directly pins Spring Core 6.1.0. Maven's controlled fixture resolution must select exactly Spring Core 6.1.0; the harness rejects a classpath containing Spring Core 6.2.8.

JDT receives the module's compile/test JAR entries through `ASTParser.setEnvironment`; JavaParser receives the exact same entries through `JarTypeSolver`. Runtime-only entries are checked as evidence but deliberately excluded from compile-time analysis. The manifest rejects missing classpath files, missing JARs, duplicate entries, missing required artifacts and forbidden artifacts.

### Limits

This checkpoint validates module-owned, resolved Maven fixture classpaths and the visibility behaviour of the two candidate engines. It does not implement dependency resolution in Aegis, export resolved artifacts from J1, establish Gradle variant selection, model every Maven scope or Gradle configuration, or prove full transitive/exclusion semantics for arbitrary projects. The conflict fixture tests Maven's selected-version output for one controlled case; it is not a general conflict-resolution engine. Dynamic declarations, unresolved versions, unsupported constraints and incomplete build models must remain explicitly incomplete rather than being assigned guessed JARs.


## Engine-neutral semantic fact oracle (J2-A3)

The initial expected-fact corpus is maintained independently of JDT and JavaParser in semantic-facts.tsv. Its stable IDs refer to fixture sources under the SemanticFacts fixture tree and cover declarations, inheritance, generic substitution, annotations, constructor/method invocations, overload selection, method references, JPMS directives and an intentionally unresolved type.

Member identities use a language-neutral comparison convention: fully qualified owner, member name, fully qualified parameter types and return type; constructors use <init>. Generic source-level identities preserve type arguments. The corpus validator checks contract integrity and fixture references only. It does not claim that either candidate has extracted these facts.

The next checkpoint implements equivalent extraction from both ASTs against this exact oracle. A fact passes only when the expected target identity and resolution state match; aggregate AST-node counts are not used as a substitute.


## J2-A4 — Per-fact semantic extraction

The semantic fact benchmark runs JDT and JavaParser independently against the same semantic-facts.tsv oracle. Each engine is checked against the expected target identity in the fixture that owns the fact; unresolved facts must remain unresolved. Output is one JSON record per fact plus a process-level summary. CI uses --report-only so a candidate's semantic gaps remain visible in the artifact without preventing the other engine from running. Strict local mode (omit --report-only) exits non-zero on any mismatch.

Run from the repository root:

```bash
mvn -B -f tools/java-semantic-spike/pom.xml compile exec:java@semantic-fact-benchmark \
  -Dexec.args="jdt tests/Aegis.Architecture.Tests/Fixtures/Java tools/java-semantic-spike/semantic-facts.tsv"

mvn -B -f tools/java-semantic-spike/pom.xml compile exec:java@semantic-fact-benchmark \
  -Dexec.args="javaparser tests/Aegis.Architecture.Tests/Fixtures/Java tools/java-semantic-spike/semantic-facts.tsv"
```

The expanded J2-A5.1 corpus covers 17 facts across 11 categories. It includes a nested record, inherited default-interface invocation, generic overload selection, and a checked-in generated-source class consumed from production source. The generated fixture is supplied as source; annotation processors are never executed. It is intentionally an evaluation harness, not a production adapter. Passing this corpus is necessary but not sufficient for full J2-A: larger repositories, diagnostic taxonomy, generated code, JPMS module-path cases, repeatable performance measurements, licensing and worker-operational review remain required.


## J2-A5.1 — Expanded semantic corpus and diagnostic taxonomy

The semantic fact corpus now includes 17 stable facts. Generated output is represented by a checked-in source fixture under a distinct `src/generated/java` root; the benchmark consumes that output directly and does not execute annotation processors. The fixture verifies generated type discovery and constructor binding from production source. Additional facts cover nested record declarations, inherited default interface methods and generic overload selection.

The normalized diagnostic taxonomy is defined independently of either engine in `semantic-diagnostics.tsv`:

- `syntax-error`: malformed source syntax;
- `compiler-error`: syntactically valid source with a language/type/visibility error;
- `unresolved-symbol`: a symbol absent from source and declared dependency evidence;
- `incomplete-environment`: a symbol expected from a declared dependency that was deliberately omitted from the analysis classpath;
- `unsupported-construct`: a language feature newer than the configured source level;
- `warning`: non-fatal compiler information, kept separate from errors;
- `infrastructure-failure`: analysis could not be performed because an input or engine operation failed.

The diagnostic corpus validator checks stable IDs, category/severity consistency, source-level values, safe fixture paths, fixture existence and complete category coverage. It validates the diagnostic test contract; it does not yet claim that either engine has normalized its native diagnostics. Engine-specific extraction and mapping are the next J2-A5.1 implementation slice.


### Diagnostic extraction

The `SemanticDiagnosticBenchmark` runs each diagnostic scenario through the selected engine and emits JSON Lines with the expected and observed normalized category, candidate status, native diagnostic code, native message, source line where available and severity.

JDT is evaluated in strict mode against the seven expected scenarios. JavaParser runs in report-only mode: absence of compiler type-check and deprecation-warning diagnostics is recorded as a capability gap, not misrepresented as a successful compiler diagnostic. Other mismatches remain visible in the artifact. The incomplete-environment versus unresolved-symbol distinction is grounded in the explicit fixture/dependency trigger, while the engine must still produce evidence of an unresolved binding or native diagnostic.


### Diagnostic evidence normalization

The diagnostic benchmark does not copy an expected category into an observation. It classifies native JDT problem IDs/severity and JavaParser parse/resolution failures, then compares the independently observed category and severity with the scenario contract.

The corpus carries a qualified symbol and declared artifact only for symbol-resolution scenarios. The incomplete-environment classification requires all of the following: a native unresolved-symbol diagnostic, a matching symbol in the fixture's import/source, and explicit declared-artifact evidence whose JAR was deliberately omitted from the analysis environment. An ordinary unresolved symbol has no declared-artifact evidence. This distinction depends on the project model as well as compiler output; neither engine can infer a missing declared dependency from an isolated unresolved name alone.

JavaParser's lack of compiler type-check and deprecation-warning diagnostics remains a reported capability gap. Report-only mode preserves those results without treating the absence of diagnostics as a pass. Strict JDT mode compares both normalized category and severity.


## J2-A5.2 — Repeatable performance and memory evaluation

The performance harness launches each candidate in a fresh JVM with the same fixed heap settings and the same compiled harness/runtime classpath. It evaluates both source-only and controlled-external-classpath modes. The candidate order alternates per trial to reduce systematic order bias. The JDT side uses ASTParser.createASTs for the complete source-file set so binding work can be shared across compilation units, as supported by the JDT API. JavaParser uses one configured parser and a shared JavaSymbolSolver/CombinedTypeSolver for the same source roots. Both candidates receive the same source files, external JAR set and JVM resource settings; their native processing strategies are measured as intended usage paths.

The default CI protocol uses two warm-up processes per candidate/mode followed by seven measured processes. Warm-ups are excluded from the summary. Each measured process records:

- engine, analysis mode and trial number;
- analysis duration reported by the child benchmark;
- end-to-end child-process duration, including JVM startup and shutdown;
- peak resident set size sampled from Linux /proc VmHWM while the child is alive;
- source/parsed file counts, diagnostics, resolution attempts and successful bindings;
- exit status and intentionally-unresolved-symbol guard outcome (a failed child aborts the benchmark).

The JSON Lines report contains environment metadata (JDK, OS, architecture, processor count, JVM heap/GC arguments), per-trial observations and per-engine/mode summaries with sample count, median and nearest-rank p95 for analysis time, process time and peak RSS. The process-time metric must not be confused with analysis-only duration. RSS is process-level memory, not Java heap occupancy; the older post-run heap snapshot remains descriptive and is not used as a peak-memory measure.

Run from the repository root on Linux with JDK 21+ and Maven:

```bash
mvn --batch-mode --no-transfer-progress -f tools/java-semantic-spike/pom.xml \
  org.apache.maven.plugins:maven-dependency-plugin:3.8.1:build-classpath \
  -DincludeScope=runtime -Dmdep.outputFile=/tmp/aegis-spike-runtime-classpath.txt

mvn --batch-mode --no-transfer-progress -f tools/java-semantic-spike/classpath-pom.xml \
  org.apache.maven.plugins:maven-dependency-plugin:3.8.1:build-classpath \
  -DincludeScope=test -Dmdep.outputFile=/tmp/aegis-external-classpath.txt

mvn --batch-mode --no-transfer-progress -f tools/java-semantic-spike/pom.xml compile \
  exec:java@semantic-performance-benchmark \
  -Dexec.args="tests/Aegis.Architecture.Tests/Fixtures/Java /tmp/aegis-external-classpath.txt /tmp/aegis-spike-runtime-classpath.txt 2 7 /tmp/aegis-performance.jsonl"
```

Peak RSS currently uses Linux /proc VmHWM, so the measurement harness explicitly rejects non-Linux environments rather than silently substituting an incomparable heap estimate. CI results are evidence, not timing thresholds: no unstable wall-clock gate is introduced. The compared workload remains the existing bounded fixture corpus; representative real repositories and module-aware semantic fact workloads are still required before a production performance conclusion.


## J2-A5.2 — Measured result

### Measured bounded-corpus result — JDT batch path (2026-11-12)

The first per-file JDT timing baseline is superseded. The accepted comparison uses JDT's batch ASTParser API and the same 42 source files, source roots, optional external JARs, JDK and fixed JVM settings for both candidates.

**Environment:** Temurin JDK 21.0.13, Linux amd64, 4 reported processors, two warm-ups and seven measured trials per candidate/mode, `-Xms64m -Xmx1g -XX:+UseG1GC`. All 42 files were parsed in every measured trial; every child exited with code 0 and no intentionally unresolved type was resolved.

| Mode | Candidate | Analysis median / p95 | Process median / p95 | Peak RSS median / p95 |
|---|---|---:|---:|---:|
| Source-only | Eclipse JDT | 593 / 624 ms | 1,005 / 1,022 ms | 149.7 / 153.4 MiB |
| Source-only | JavaParser | 369 / 380 ms | 770 / 799 ms | 117.2 / 119.0 MiB |
| External classpath | Eclipse JDT | 643 / 679 ms | 1,047 / 1,090 ms | 143.8 / 154.0 MiB |
| External classpath | JavaParser | 386 / 407 ms | 815 / 833 ms | 120.5 / 121.1 MiB |

Analysis time excludes process startup; process time includes JVM startup and shutdown. Peak RSS is process-level Linux VmHWM, not Java heap occupancy. With seven samples, nearest-rank p95 is the maximum observed sample. The candidates' diagnostic counts and aggregate binding-attempt counts are not directly comparable because their diagnostic and AST node models differ.

**Interpretation:** On this bounded fixture, JavaParser is approximately 1.6–1.7x faster in analysis, with approximately 1.2–1.3x lower process time and 1.2–1.3x lower peak RSS. JDT batch parsing materially improves its previous per-file baseline, but a performance disadvantage remains. This is a material counterweight in the engine decision, not a reason to disregard JDT's stronger bounded semantic/diagnostic coverage (17/17 facts and 7/7 diagnostics versus JavaParser's 14/17 and 5/7). The 42-file corpus and one CI runner are insufficient to establish production-scale performance or a final engine winner.

**Decision:** Accept J2-A5.2 as a bounded, repeatable measurement checkpoint. Retain JDT as the provisional correctness-first candidate, with the measured performance and memory gap explicitly recorded. Representative repositories, workload scaling and the J2-A5.3 operational/legal review remain necessary before final engine approval. No production parser dependency is introduced.


## J2-A5.3 — Preliminary operational, security and licensing review (not accepted)

This is an evidence register and set of acceptance gates, not legal advice or a production deployment decision.

### Dependency and license evidence

| Candidate | Declared license | Relevant dependency footprint | Review position |
|---|---|---|---|
| Eclipse JDT Core 3.47.0 | EPL-2.0 | Required Eclipse core resources/runtime/filesystem/text plus ECJ; optional Eclipse team core. The complete transitive closure is larger and must be inventoried. | Technically viable, but distribution notices/source-availability obligations and the full transitive license set require legal review. |
| JavaParser 3.28.2 + Symbol Solver | Dual Apache-2.0 or LGPL-3.0 | JavaParser core, Guava, Checker Framework annotations and Javassist, plus their transitive dependencies. | The Apache-2.0 option is the preferred licensing route for evaluation, subject to confirming all included components and notices. |

Aegis is distributed under its own proprietary license. That fact does not by itself establish incompatibility with either library, nor does placing a library in a separate process eliminate license obligations. Before distribution, generate and review a complete SBOM/license inventory, preserve required notices, identify the selected JavaParser license option, and obtain legal approval for the JDT EPL-2.0 distribution model. The EPL requires, among other things, preserving its license and providing a reasonable source-code availability statement when the program is distributed. No legal conclusion is made by this engineering review.

### Security finding and mitigation

JavaParser 3.28.2's published dependency metadata selects Guava 33.6.0-jre. Google advisory GHSA-xxph-c9ww-hj94 / CVE-2026-102554 affects Guava through 33.7.1; the issue is uncontrolled allocation during native Java deserialization of selected Guava collections. The fix is present in 33.7.2 and later.

The current spike now manages Guava to 33.8.0-jre and CI asserts the resolved dependency-tree version. The advisory's attack path requires untrusted Java object deserialization; the parser workload does not intentionally deserialize project-controlled Java objects. That narrows the direct reachability assessment but is not a reason to ship a known-affected dependency. A full transitive vulnerability scan remains required for both candidate closures and every future version update.

### Runtime and packaging

- The evaluation is built and executed with Temurin JDK 21.0.13. Production must define and support an explicit Java runtime baseline; the host must not silently depend on whichever system Java happens to be installed.
- Preferred packaging direction for a desktop deployment is an application-managed, minimized Java runtime image (for example, a jlink-generated runtime) alongside a versioned worker distribution. This is a proposal, not an accepted packaging decision. It creates an explicit responsibility to patch and redistribute the private runtime.
- JDT's Eclipse platform dependencies create a larger runtime closure than JavaParser's core/symbol-solver closure. Exact archive size, module closure, native/platform requirements and license notices have not yet been measured.
- Validate packaging on the actual Windows WPF target as well as Linux CI. Verify path handling, runtime discovery, upgrade/rollback and offline operation.

### Worker lifecycle and isolation proposal

Do not embed a JVM inside the .NET WPF process for the first production integration. Keep the Java engine behind the language-neutral adapter and use a supervised, out-of-process worker. The process boundary protects the .NET host from JVM crashes, but it is not by itself a security sandbox.

The proposed contract is versioned JSON Lines over standard input/output (no listening TCP port): protocol handshake, request IDs, deterministic structured responses, bounded payloads, structured diagnostics, cancellation and graceful shutdown. Reserve stdout for protocol frames and send logs to stderr. The .NET supervisor should enforce a bounded worker pool, per-request deadlines, memory/CPU limits, process-tree termination, crash restart and worker recycling. Apply workspace/classpath allowlists and an OS-restricted identity; do not execute Maven/Gradle scripts, plugins or annotation processors. Avoid cross-project or cross-tenant cache leakage.

The 42-file cold-process measurements show roughly 1.0 s median process time and 150 MiB median peak RSS for JDT, versus roughly 0.8 s and 120 MiB for JavaParser. These are not production capacity figures, but they argue against launching an unbounded JVM per source file or per request. A bounded long-lived worker pool is the leading operational hypothesis; its cache reset, memory growth and concurrency behaviour still need testing.

### J2-A5.3 acceptance gates

1. Produce an SBOM and full transitive license inventory for both exact candidate closures; verify notices and selected license terms.
2. Run a current vulnerability scan against direct and transitive dependencies; fail CI on unapproved high/critical findings and document reachability exceptions.
3. Prototype a private Java runtime/worker package on Windows and Linux, recording artifact size, startup, runtime patch process and upgrade/rollback behaviour.
4. Exercise worker handshake, malformed requests, cancellation, timeouts, process crash/restart, memory exhaustion, bounded concurrency and cross-project isolation.
5. Confirm no build scripts, plugins or annotation processors are executed and that file/network access is restricted as designed.
6. Obtain legal review of redistribution obligations before any production binary ships with either engine.

**Status:** J2-A5.3 is in progress and not accepted. The Guava version override is implemented; the complete SBOM/license audit, security scan, Windows packaging prototype, worker lifecycle tests and legal approval remain open. JDT remains the provisional correctness-first candidate, with its measured performance/memory disadvantage recorded. J2-A remains open and J2-B remains blocked.

