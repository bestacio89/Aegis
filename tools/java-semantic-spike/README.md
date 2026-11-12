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

The authoritative per-trial source is the `performance.jsonl` file in [Java semantic engine workflow run 43199349380](https://github.com/bestacio89/Aegis/actions/runs/43199349380), on commit `67fc751b2929a3185f3f29d9ef4d3c8b34e95f39`.

**Environment:** Temurin JDK 21.0.13, Linux amd64, 4 reported processors, two warm-ups and seven measured trials per candidate/mode, `-Xms64m -Xmx1g -XX:+UseG1GC`. All 42 files were parsed in every measured trial; every child exited with code 0 and no intentionally unresolved type was resolved.

| Mode | Candidate | Analysis median / p95 | Process median / p95 | Peak RSS median / p95 |
|---|---|---:|---:|---:|
| Source-only | Eclipse JDT | 505 / 517 ms | 830 / 855 ms | 154.5 / 155.0 MiB |
| Source-only | JavaParser | 297 / 327 ms | 640 / 661 ms | 115.6 / 118.5 MiB |
| External classpath | Eclipse JDT | 558 / 566 ms | 932 / 948 ms | 148.7 / 154.8 MiB |
| External classpath | JavaParser | 337 / 347 ms | 747 / 752 ms | 120.3 / 121.4 MiB |

Analysis time excludes process startup; process time includes JVM startup and shutdown. Peak RSS is process-level Linux VmHWM, not Java heap occupancy. With seven samples, nearest-rank p95 is the maximum observed sample. The candidates' diagnostic counts and aggregate binding-attempt counts are not directly comparable because their diagnostic and AST node models differ.

**Interpretation:** On this bounded fixture, JavaParser is approximately 1.6–1.7x faster in analysis, with approximately 1.2–1.3x lower process time and 1.2–1.3x lower peak RSS. JDT batch parsing materially improves its previous per-file baseline, but a performance disadvantage remains. This is a material counterweight in the engine decision, not a reason to disregard JDT's stronger bounded semantic/diagnostic coverage (17/17 facts and 7/7 diagnostics versus JavaParser's 14/17 and 5/7). These measurements use the patched Guava 33.8.0-jre dependency graph. The 42-file corpus and one CI runner are insufficient to establish production-scale performance or a final engine winner.

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

The current spike manages Guava to 33.8.0-jre and CI asserts the resolved dependency-tree version. The latest OSV scan of the generated CycloneDX SBOM returned zero findings; the report is retained as a CI artifact. CI also emits a CycloneDX 1.6 JSON SBOM for the combined candidate dependency closure and verifies both parser roots plus the patched Guava component. The current combined SBOM has 29 components and no missing license metadata: 15 EPL-2.0, 10 Apache-2.0-only, two Apache-2.0/LGPL-2.1-or-later, one Apache-2.0/LGPL-2.1-only/MPL-1.1 and one MIT. This is inventory evidence, not legal approval; multi-license choices and notices still require review. The workflow runs OSV-Scanner 2.7.1 against the SBOM and uploads its JSON report. **OSV scan result (2026-11-12):** OSV-Scanner 2.7.1 scanned the generated CycloneDX SBOM and returned an empty vulnerability-results array (zero findings). The JSON report is retained in [the CI artifact](https://github.com/bestacio89/Aegis/actions/runs/43198643879/artifacts/13933598969). This is a point-in-time match against OSV's available database, not a guarantee of absence of vulnerabilities or a substitute for a complete security review. The advisory's attack path requires untrusted Java object deserialization; the parser workload does not intentionally deserialize project-controlled Java objects. That narrows the direct reachability assessment but is not a reason to ship a known-affected dependency. A full transitive vulnerability scan remains required for both candidate closures and every future version update.

### Runtime and packaging

- The evaluation is built and executed with Temurin JDK 21.0.13. Production must define and support an explicit Java runtime baseline; the host must not silently depend on whichever system Java happens to be installed.
- Preferred packaging direction for a desktop deployment is an application-managed, minimized Java runtime image (for example, a jlink-generated runtime) alongside a versioned worker distribution. This is a proposal, not an accepted packaging decision. It creates an explicit responsibility to patch and redistribute the private runtime.
- JDT's Eclipse platform dependencies create a larger runtime closure than JavaParser's core/symbol-solver closure. The combined evaluation package's runtime/module closure and approximate size have been measured on Windows and Linux; candidate-specific production packages, installer overhead, native/platform requirements and notices still require review.
- The packaging workflow now also launches the engine-neutral worker from the minimized private runtime on Windows and Linux. Runtime discovery, protocol startup, graceful shutdown and forced termination/restart are exercised; patch/upgrade/rollback, offline host operation and production WPF supervision remain open.

### Worker lifecycle and isolation proposal

Do not embed a JVM inside the .NET WPF process for the first production integration. Keep the Java engine behind the language-neutral adapter and use a supervised, out-of-process worker. The process boundary protects the .NET host from JVM crashes, but it is not by itself a security sandbox.

The proposed contract is versioned JSON Lines over standard input/output (no listening TCP port): protocol handshake, request IDs, deterministic structured responses, bounded payloads, structured diagnostics, cancellation and graceful shutdown. Reserve stdout for protocol frames and send logs to stderr. The .NET supervisor should enforce a bounded worker pool, per-request deadlines, memory/CPU limits, process-tree termination, crash restart and worker recycling. Apply workspace/classpath allowlists and an OS-restricted identity; do not execute Maven/Gradle scripts, plugins or annotation processors. Avoid cross-project or cross-tenant cache leakage.

The 42-file cold-process measurements show roughly 1.0 s median process time and 150 MiB median peak RSS for JDT, versus roughly 0.8 s and 120 MiB for JavaParser. These are not production capacity figures, but they argue against launching an unbounded JVM per source file or per request. A bounded long-lived worker pool is the leading operational hypothesis; its cache reset, memory growth and concurrency behaviour still need testing.

### J2-A5.3 acceptance gates

1. Review the generated CycloneDX SBOM; reconcile missing/ambiguous license metadata and produce a full transitive license inventory for both exact candidate closures; verify notices and selected license terms.
2. Keep the OSV SBOM scan current for direct and transitive dependencies; the current snapshot returned zero findings. Review future findings, maintain patch policy and document any reachability exceptions.
3. Cross-platform private-runtime feasibility and worker process smoke are implemented for Windows and Linux, including package size, startup, handshake, protocol errors, graceful shutdown and forced termination/restart. Complete candidate-specific packaging, runtime patch process, upgrade/rollback and production host supervision.
4. Exercise worker handshake, malformed requests, cancellation, timeouts, process crash/restart, memory exhaustion, bounded concurrency and cross-project isolation.
5. Confirm no build scripts, plugins or annotation processors are executed and that file/network access is restricted as designed.
6. Obtain legal review of redistribution obligations before any production binary ships with either engine.

**Status:** J2-A5.3 is in progress and not accepted. The Guava 33.8.0 override, combined CycloneDX inventory, zero-finding OSV snapshot and Windows jlink packaging feasibility probe are complete. Full license/notice review, ongoing vulnerability governance, WPF host integration, runtime patch/upgrade/rollback, worker lifecycle/isolation tests and legal approval remain open. JDT remains the provisional correctness-first candidate, with its measured performance/memory disadvantage recorded. J2-A remains open and J2-B remains blocked.



### CycloneDX inventory snapshot (2026-11-12)

The generated combined candidate SBOM contains 29 components and reports license metadata for all 29. The current declared license inventory is:

- 15 EPL-2.0 components, comprising JDT Core/ECJ and its Eclipse platform dependency closure.
- 10 Apache-2.0-only components.
- 2 components declaring Apache-2.0 and LGPL-2.1-or-later (JNA and JNA Platform).
- 1 component declaring Apache-2.0, LGPL-2.1-only and MPL-1.1 (Javassist).
- 1 MIT component (Checker Framework annotations).

This is a combined candidate dependency closure; the SBOM preserves dependency edges and package URLs. License metadata is not a legal determination, and multi-license expressions require explicit selection and notice review. The JDT EPL-2.0 distribution obligations and Aegis's proprietary distribution model still require legal approval. The SBOM and OSV report are uploaded as CI artifacts; a clean scan would not replace a complete license/security review.


## Windows private-runtime packaging probe

The Java semantic workflow includes a cross-platform J2-A5.3 packaging probe on Windows and Linux. It compiles the semantic spike and the isolated worker prototype, resolves both runtime dependency closures, derives their combined JDK module closure with `jdeps`, creates a minimized Java 21 runtime with `jlink`, and assembles separate candidate-evaluation and worker classpaths.

The private runtime executes the JDT and JavaParser semantic-fact and diagnostic benchmarks. It also launches the actual worker process from its isolated classpath and exercises handshake, capabilities, liveness, malformed/oversized frames, protocol-version rejection, graceful shutdown, forced process-tree termination and replacement-worker startup. The artifacts retain runtime modules, candidate probe output, worker lifecycle measurements and package-size summaries.

The candidate package contains both parser closures and remains an upper-bound evaluation bundle. The worker package contains only the worker classes and its runtime-only Gson dependency; it does not bundle either parser. Both estimates exclude the Aegis WPF host, launcher, notices and installer overhead. The worker protocol probe is operational feasibility evidence, not a .NET supervisor implementation, production semantic integration or an OS sandbox. Host-enforced deadlines, CPU/memory limits, restricted identity, filesystem/network policy, cache isolation, runtime patch/upgrade/rollback and legal approval remain open.


### Historical Windows candidate-only private-runtime baseline (2026-11-12)

The Windows packaging probe completed successfully using Temurin JDK 21.0.13. `jdeps` identified nine root modules; the resulting `jlink` runtime contains 14 modules after transitive closure:

`java.base, java.compiler, java.desktop, java.instrument, java.logging, java.management, jdk.attach, jdk.jdi, jdk.unsupported`.

| Package component | Size |
|---|---:|
| Minimized Java runtime image | 48.41 MiB |
| 29 candidate dependency JARs | 20.69 MiB |
| Compiled spike classes | 0.17 MiB |
| **Estimated combined evaluation package** | **69.27 MiB** |

The private runtime successfully executed the semantic-fact and normalized-diagnostic probes on Windows:

| Candidate | Semantic facts | Normalized diagnostics |
|---|---:|---:|
| Eclipse JDT | 17/17 | 7/7 |
| JavaParser + Symbol Solver | 14/17 (3 known gaps) | 5/7 (2 known capability gaps) |

The measurements above are the earlier candidate-only package baseline, before the worker protocol was added to the cross-platform packaging probe. That bundle contains both candidates and is therefore an upper-bound evaluation bundle. It excludes the Aegis WPF host, launcher, notices, installer overhead and worker package. It proves that a minimized private Java runtime can execute both candidate engines on the Windows target; it does not prove worker supervision, cancellation, crash recovery, sandboxing or final distribution readiness.

The complete runtime image, 29 JARs, module list, four smoke-test outputs and size summary are retained in [the Windows packaging artifact](https://github.com/bestacio89/Aegis/actions/runs/43201770009/artifacts/13935597122).


The packaging job runs the same private-runtime probe on Windows and Linux. Each OS artifact records the requested JDK module roots, actual modules present in the linked runtime, runtime launch time, per-candidate semantic/diagnostic process durations and the package-size breakdown. These remain descriptive measurements of the combined evaluation bundle; they are not production worker capacity or startup guarantees.


## Candidate-specific dependency SBOMs

The Java semantic workflow retains the combined CycloneDX 1.6 SBOM and generates isolated candidate inventories from:

- `candidate-sboms/jdt/pom.xml` — Eclipse JDT Core 3.47.0 and its transitive closure (19 components).
- `candidate-sboms/javaparser/pom.xml` — JavaParser Symbol Solver 3.28.2 and its transitive closure (10 components), with Guava managed to 33.8.0-jre.

Both child POMs inherit pinned versions from `candidate-sboms/pom.xml`; neither includes the other candidate. CI verifies the candidate root, expected component count and non-empty license metadata. The combined 29-component SBOM remains the union inventory and is the input to the OSV scan. Legal interpretation and redistribution approval remain separate review gates.


### Worker process probe result (2026-11-12)

The worker-specific protocol test suite passed 7/7 tests. The Windows and Linux private-runtime packaging jobs both launched the protocol-only worker from the minimized Java 21 runtime and passed handshake, capabilities, ping, malformed/oversized-frame, protocol-version, graceful-shutdown and forced-termination/restart checks. The worker runtime closure is kept separate from JDT and JavaParser.

- [Windows private-runtime artifact](https://github.com/bestacio89/Aegis/actions/runs/43207732837/artifacts/13939951010)
- [Linux private-runtime artifact](https://github.com/bestacio89/Aegis/actions/runs/43207732837/artifacts/13939681044)
- [Java comparison, worker SBOM and OSV artifact](https://github.com/bestacio89/Aegis/actions/runs/43207732837/artifacts/13940141303)

This is a protocol/process feasibility result. The worker still advertises no semantic engine and does not provide a .NET supervisor or OS sandbox.


## Representative project matrix

The CI matrix uses three pinned Apache-2.0 source snapshots, recorded in `representative-projects.tsv`:

| Project | Build model | Main Java files | Test Java files |
|---|---|---:|---:|
| Spring Petclinic | Maven and Gradle Groovy | 30 | 20 |
| Quarkus getting-started | Maven | 2 | 2 |
| Micronaut starter AWS CDK module | Gradle Kotlin DSL | 3 | 2 |

Each checkout is pinned to an immutable commit and its license file and source inventory are verified before analysis. The Micronaut module resolves local project types through the `starter-core` source root. Both engines analyze main and test source sets separately; test analysis can see main sources, while main analysis cannot see test sources.

The benchmark records parse completeness, syntax errors, declarations, method-call resolution and type-reference resolution. It runs in a deliberately bounded **source-roots plus running-JDK environment**: project build scripts are not executed and third-party dependency classpaths are not supplied. Unresolved external framework symbols are therefore environment evidence, not candidate correctness failures. The strict gate requires every pinned Java source file in the selected source sets to parse without syntax errors.

This is the first real-project robustness and local-source-resolution checkpoint, not a full compiler-equivalence oracle. The next evaluation step must add controlled external dependency visibility and selected expected semantic facts before using these measurements to make a final engine recommendation.


## Representative source-local semantic facts (J2-A5.1)

The representative-project matrix is supplemented by an engine-neutral semantic fact corpus for three pinned, Apache-2.0 projects:

- Spring Petclinic (Maven and Gradle Groovy): inheritance, overload-aware method invocation, and a negative fact proving that an unrelated method is not reported as the target invocation.
- Quarkus getting-started (Maven): an intra-project method invocation.
- Micronaut starter AWS CDK module (Gradle Kotlin DSL): inheritance and constructor invocation across local project source roots.

Each fact identifies the source declaration, expected target identity and expected resolution state. Both JDT and JavaParser are run independently against the same source files and source-root boundary. The negative fact is intentional: the benchmark must not accept a nearby declaration as evidence that the requested call exists.

The runner now uses strict benchmark mode. A candidate mismatch fails the workflow rather than being retained as report-only evidence. It validates every per-fact result, the final summary, the expected fact count, the pinned repository commit and the repository license evidence. Six JSONL reports are retained (three projects × two engines).

**Environment boundary:** these facts use checked-out project source roots plus the running JDK. Third-party framework dependencies are not supplied, and no Maven/Gradle build script, plugin or annotation processor is executed. The six facts therefore validate source-local semantic resolution, not framework binding coverage or complete project compilation. The separate parser-robustness matrix also remains source-roots-plus-JDK-only. Controlled external dependency classpaths for these representative repositories are a subsequent evaluation task.

Run from the repository root after the pinned repositories have been checked out:

```bash
bash tools/java-semantic-spike/run-representative-semantic-facts.sh
```


### Representative external API bindings

The representative semantic-fact runner resolves a separate curated compile classpath for each pinned project from isolated synthetic Maven POMs. It does **not** invoke the checked-out projects' Maven or Gradle builds, settings, plugins or annotation processors.

- Spring Petclinic: Spring Boot 4.1.0-managed JPA and validation starters, exercising `jakarta.persistence.Entity` and Spring's external `Assert.notNull` method.
- Quarkus getting-started: Quarkus platform BOM 3.40.1.2 plus the declared `quarkus-rest` API, exercising JAX-RS `Path` and `GET` annotations.
- Micronaut starter AWS CDK module: the pinned `aws-cdk-lib:2.243.0` API dependency, exercising `Function.Builder.create(Construct, String)` across the module-owned source roots.

The classpath file is validated for non-empty, unique JAR entries and required API artifacts. Candidate parser JARs are explicitly forbidden from these analysis classpaths. JDT receives the JAR list through `ASTParser.setEnvironment`; JavaParser receives the same list through `JarTypeSolver`.

These are curated external API classpaths selected to validate named semantic facts. They are not asserted to be complete effective classpaths for the checked-out projects, and they are not exported by the current J1 static model. The exact project build models remain authoritative for declarations; no project build is executed to obtain this evaluation input.


## Representative-project external API resolution matrix

The pinned real-project matrix preserves the original twelve source-roots-plus-JDK runs (main and test source sets, both candidates) and adds twelve curated external-classpath runs: main and test source sets for each candidate across all three representative projects.

The external classpaths are resolved from the isolated, project-specific synthetic Maven POMs under `representative-classpaths/`. The dependency plugin resolves only the declared curated API dependencies; no checked-out Maven/Gradle project build, wrapper, settings file, plugin, build script or annotation processor is executed. Both candidates receive the exact same validated JAR list for a given project. Candidate-engine JAR leakage is rejected by the benchmark itself and by the runner.

The added mode reports `source-roots+jdk+curated-external-classpath` and the exact JAR-entry count. Main classpaths use Maven `includeScope=compile`; test classpaths use `includeScope=test`, which includes the compile, runtime and test dependency scopes declared by each synthetic POM. Spring Petclinic and Quarkus include the test API dependencies declared by their pinned project revisions. The Micronaut test fixture uses the same curated project-source and AWS CDK APIs; its full Gradle test dependency graph is not claimed. These are controlled evaluation inputs, not complete effective project classpaths. The original source-only results remain available as a baseline; do not compare aggregate resolution percentages across different environments as if they represented the same analysis boundary.

Run the matrix from the repository root:

```bash
bash tools/java-semantic-spike/run-representative-project-matrix.sh
```

A report is accepted only when every discovered file parses without syntax errors, the output declares the expected environment and classpath-entry count, the unresolved-method sample count matches the bounded sample contract (up to twenty per report), and the shared type-reference metric contract remains `outermost-reference-type-nodes-v1`. Each unresolved-method sample retains source path, line, expression and native resolution detail where available. Resolution percentages are comparative observations, not semantic correctness gates; the separate source-scoped semantic fact oracle remains the correctness gate.


### Historical 18-report baseline (before test-classpath expansion, 2026-11-12)

The eighteen-report matrix passed on [workflow run 43237842802](https://github.com/bestacio89/Aegis/actions/runs/43237842802); the JSONL reports and classpath snapshots are retained in [artifact 13952309452](https://github.com/bestacio89/Aegis/actions/runs/43237842802/artifacts/13952309452).

| Project | Main / test files | Curated main JARs | JDT method calls | JavaParser method calls |
|---|---:|---:|---:|---:|
| Spring Petclinic | 30 / 20 | 50 | 206/230 | 206/230 |
| Quarkus getting-started | 2 / 2 | 109 | 1/1 | 1/1 |
| Micronaut starter AWS CDK | 3 / 2 | 12 | 16/16 | 9/16 |

Both candidates parsed all 35 main and 24 test source files without parse failures or syntax errors in this historical baseline. At that point, external classpaths were supplied only to main-source runs and test-source metrics were JDK-only. The current runner expands this to curated main and test classpaths and emits twenty-four reports; the results of that expanded matrix will supersede this historical baseline.

The representative semantic fact oracle contains eleven facts: JDT passed 11/11; JavaParser passed 10/11. The single JavaParser mismatch is RFACT-011, the AWS CDK `Function.Builder.create(Construct, String)` binding. The native diagnostic reports `UnsolvedSymbolException: Unsolved symbol : Named`; JDT resolves the expected method. CI asserts this exact known gap and retains the diagnostic instead of treating it as a pass.

The external classpath raised Spring Petclinic method-call resolution from 118/230 (JDT) and 115/230 (JavaParser) to 206/230 for both. Quarkus remained 1/1 for both. Micronaut reached 16/16 for JDT, while JavaParser remained at 9/16. Both candidates resolved all measured type references in the Micronaut curated-classpath run. Spring's type-reference denominators differ by one between AST models, so these aggregate values are descriptive, not a claim of identical AST inventories.

The curated classpaths are explicit evaluation inputs, not complete project dependency graphs. No representative project build scripts, wrappers, plugins or annotation processors are executed. The `elapsedMillis` values are single-run observations and are not performance results.
