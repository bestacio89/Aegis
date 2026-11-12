# Java semantic-engine evaluation

**Status:** J2-A in progress. Eclipse JDT is the preferred candidate following J2-A4; final production-engine approval remains pending J2-A5.

## Purpose

Select a Java semantic-analysis engine for Aegis based on correctness, module-aware resolution, operational fit, performance and legal/maintenance cost. Candidate engines are Eclipse JDT, JavaParser with Symbol Solver and Spoon.

The J0 spike is a smoke test only. Its output is not a production benchmark or a defensible ranking.

## J0 smoke-test results

The archived run analysed 15 Java source files across the checked-in Maven and Gradle fixture corpora.

| Observation | JavaParser 3.28.2 | Eclipse JDT Core 3.47.0 |
|---|---:|---:|
| Files parsed | 15/15 | 15/15 |
| Named source-type spot checks, source-only | 5/5 | 3/5 |
| Named external-type spot checks, controlled classpath | 3/3 | 3/3 |
| Parse/compiler diagnostic count, source-only | 0 parse problems | 22 compiler errors |
| Parse/compiler diagnostic count, controlled classpath | 0 parse problems | 9 compiler errors |
| Elapsed time, source-only | 295 ms | 770 ms |
| Elapsed time, controlled classpath | 304 ms | 860 ms |

Both engines left intentionally missing types unresolved. The diagnostic counts describe different categories and must not be compared as equivalent. The timing data comes from one uncontrolled process run and is descriptive only.

## Benchmark defects that invalidate engine ranking

### Repository-wide source-root leakage

The current harness discovers all fixture source roots and supplies them to a single JavaParser `CombinedTypeSolver` and to every JDT parser environment. This ignores module dependency edges. A service module can see an API module even if the build model does not declare the dependency; the reverse direction can also be accidentally exposed.

### Global external classpath

The same controlled external JAR list is supplied to every source file. The harness does not model module-owned compile/runtime/test scopes, exclusions, dependency conflicts or named-module visibility.

### Non-equivalent AST coverage

JavaParser resolves `ClassOrInterfaceType`; JDT resolves `SimpleType`. These are not identical AST node sets. Aggregate attempts, resolved counts and ratios therefore have no valid comparative interpretation.

### Incompatible diagnostic counts

JavaParser parse problems and JDT compiler errors are counted under one metric. They need separate categories and an engine-neutral expected outcome.

### Insufficient correctness oracle

A handful of expected type names does not verify method overloads, generic substitutions, constructors, inheritance, annotations, module access, visibility, or deterministic identity. The next benchmark must compare stable semantic fact IDs and resolved target identities.

### Inadequate performance methodology

A single run with an approximate post-run heap snapshot is not a reliable benchmark. The comparison needs isolated processes, warm-up, repeated trials, distributions and an explicitly defined peak-memory measurement.

## J2-A work plan

1. Generate module-scoped source roots and dependency visibility from J1 module/build evidence.
2. Add positive and negative project-reference visibility tests, plus production/test separation.
3. Define a common semantic fact corpus with stable IDs and expected identities.
4. Implement equivalent extraction categories for JDT and JavaParser; report per-fact resolution outcomes rather than aggregate AST ratios.
5. Split parse, compiler, unresolved-binding, unsupported, incomplete-environment and infrastructure diagnostics.
6. Expand to named modules, generated code, dependency conflicts, partial projects and larger representative repositories.
7. Establish reproducible worker-process performance and memory measurements.
8. Review licensing, packaging, worker lifecycle, cancellation, timeouts, crash isolation, concurrency and JVM deployment requirements.

## Architectural constraints

- No parser-specific types in `Aegis.Shared`.
- Use J1 module ownership and dependency evidence; do not guess a module's classpath.
- Do not execute Maven/Gradle scripts, plugins or annotation processors during ordinary analysis.
- Keep declared dependencies distinct from resolved artifacts.
- Treat a JVM worker as an option, not a predetermined architecture.
- Keep the .NET-facing adapter contract language-neutral and versioned.

## Acceptance criteria

J2-A requires module-aware visibility correctness, the same semantic fact oracle across candidates, explicit diagnostic taxonomy, representative corpus coverage, repeatable performance measurements, legal/operational review, and an evidence-backed production recommendation.

**Current decision:** Eclipse JDT is the provisional preferred candidate after J2-A4. Final production-engine approval is pending J2-A5; J2-B remains blocked.


## J2-A1 — Module-scoped project and source-set visibility — accepted

**Checkpoint result:** accepted on 2026-11-12. This accepts the first bounded J2-A implementation slice only; it does not accept the full J2-A evaluation or select a production engine.

The isolated visibility harness now consumes an explicit, reviewable projection of J1 fixture evidence:
- module-owned production source roots;
- module-owned test source roots;
- declared production project-dependency edges;
- declared test-only project-dependency edges.

The same fixture and four expected visibility outcomes are evaluated independently by Eclipse JDT and JavaParser.

| Expected fact | JDT | JavaParser |
|---|---|---|
| Declared consumer resolves API module type | Pass | Pass |
| Undeclared isolated module cannot resolve API module type | Pass | Pass |
| Production source cannot resolve test-only support dependency | Pass | Pass |
| Test source can resolve its declared test-only support dependency | Pass | Pass |

Each engine reported 4/4 expected facts satisfied across 11 observations (7 resolved, 4 unresolved). The observations are gate evidence for this fixture, not comparative accuracy metrics.

### J2-A1 CI evidence

All mandatory workflows passed on implementation head `021feeeb0998a4d3c65ef3e2236183d88ffc2553`:
- Java semantic engine spike — run `43174315755`, success; includes both module-visibility engine runs.
- Non-WPF quality gates — run `43174315694`, success.
- WPF build validation — run `43174315692`, success.

### Boundaries retained

The manifest is a controlled fixture projection, not yet a general exporter from every Maven/Gradle model. It proves source-root and project-dependency isolation for the declared cases. It does not yet prove external artifact classpath ownership, Maven/Gradle scope completeness, exclusions, dependency conflict selection, JPMS module-path visibility, generated-source completeness or arbitrary transitive build-model behaviour.

**Decision:** J2-A1 accepted. Continue J2-A with module-owned external artifact classpaths and broader visibility matrices. Full J2-A remains in progress; J2-B remains blocked.


## J2-A2 — Module-owned external artifact visibility — accepted

**Checkpoint result:** accepted on 2026-11-12. This accepts the second bounded J2-A implementation slice only; it does not accept the full evaluation, select a production engine or authorize J2-B.

The module-aware harness now loads separate compile, test and runtime classpath snapshots per module. Pinned Maven fixture POMs produce these snapshots independently of the J1 declaration/source-root projection. This distinction is intentional: J1 declared dependencies are not equivalent to resolved artifact paths, and the fixture is not presented as Aegis's production resolver.

Both candidate engines passed the same 12 expected visibility assertions across 21 observations per engine. The matrix covers declared compile artifacts, test-only visibility, runtime-only exclusion from compile analysis, a transitive exclusion, a controlled selected-version conflict, and the existing project/source-set visibility boundaries.

| Expected boundary | Eclipse JDT | JavaParser |
|---|---|---|
| Module-owned compile artifacts resolve | Pass | Pass |
| Test-only artifact resolves in test source set | Pass | Pass |
| Test-only artifact remains unavailable to production | Pass | Pass |
| Runtime-only artifact remains unavailable to compile analysis | Pass | Pass |
| Excluded Spring Core artifact remains unavailable | Pass | Pass |
| Selected Spring Core 6.1.0 resolves; 6.2.8 is rejected | Pass | Pass |
| Positive/negative project and source-set visibility | Pass | Pass |

The same module/source-set artifact entries are passed to JDT's parser environment and JavaParser's JAR type solvers. Runtime artifacts are retained as evidence but deliberately excluded from compile-time semantic resolution.

### J2-A2 CI evidence

All mandatory workflows passed on implementation head 3f1824e80d760b5f636466dab8cc8303f43161a9:
- Java semantic engine spike — run 43179049509, success.
- Non-WPF quality gates — run 43179049510, success.
- WPF build validation — run 43179049489, success.

### Boundaries retained

The classpath snapshots are controlled Maven fixture inputs, not J1-generated resolved artifacts and not evidence of a general Aegis dependency resolver. Gradle variant selection, complete Maven/Gradle scope semantics, arbitrary transitive/exclusion behaviour, JPMS module paths and production artifact resolution remain open. The one conflict fixture verifies the resolved output of Maven for a pinned case; it does not implement general conflict mediation.

**Decision:** J2-A2 accepted. Continue J2-A with an engine-neutral semantic fact oracle and equivalent extraction coverage. Full J2-A remains in progress; J2-B remains blocked.


## J2-A3 — Engine-neutral semantic fact oracle — in progress

The next evaluation input is a stable fact corpus, independent of either candidate engine's AST model. It is checked in at tools/java-semantic-spike/semantic-facts.tsv, with source fixtures under tests/Aegis.Architecture.Tests/Fixtures/Java/SemanticFacts/.

The initial corpus contains 11 stable IDs across 11 categories:
- type declaration;
- inheritance;
- generic supertype substitution;
- annotation;
- constructor invocation;
- method invocation;
- overload selection;
- method reference;
- JPMS requires;
- JPMS exports;
- intentionally unresolved type.

Every row identifies the owning module and source set, the fixture source, a source identity, the expected target identity and whether the target must resolve. Type identities use fully qualified names. Member identities use owner#member(parameter-types):return-type; constructors use <init>. Generic source-level identities preserve their type arguments. For unresolved facts, the expected qualified symbol remains explicit while the expected binding state is unresolved.

SemanticFactCorpusValidator validates the tab-separated contract, stable and unique fact IDs, required categories, identity fields, source-set values, fixture path containment and fixture existence. This validates the oracle itself; it does not run semantic extraction and must not be interpreted as a JDT/JavaParser result.

**Acceptance boundary:** J2-A3 requires the corpus validator to pass in CI and the contract to be reviewed. J2-A4 will implement equivalent extraction of these same fact IDs and compare target identities and resolution outcomes per fact. Full J2-A remains in progress; J2-B remains blocked.


## J2-A3 — Engine-neutral semantic fact oracle — accepted

**Checkpoint result:** accepted on 2026-11-12. This accepts the stable corpus and its integrity gate only. It does not claim that either candidate extracts the facts or that the engines are semantically equivalent.

The oracle contains 11 stable fact IDs across 11 categories: type declarations, inheritance, generic supertype substitution, annotations, constructor invocations, method invocations, overload selection, method references, JPMS requires/exports and an intentionally unresolved type. Every row includes module/source-set ownership, fixture provenance, source identity, expected target identity and expected resolution state.

The standalone validator passed all schema and fixture-integrity checks: eight TSV columns, unique JFACT-NNN IDs, complete required category coverage, non-empty identities, valid source sets, and contained existing fixture paths.

### J2-A3 CI evidence

All mandatory workflows passed on implementation head 3549fdc25161102d50a23d7542aff495e4ce5e33:
- Java semantic engine spike — run 43180514181, success.
- Non-WPF quality gates — run 43180514116, success.
- WPF build validation — run 43180514089, success.

**Decision:** J2-A3 accepted. J2-A4 must implement equivalent JDT and JavaParser fact extraction and compare each stable fact ID's target identity and resolution state. Full J2-A remains open; J2-B remains blocked.


## J2-A4 — Equivalent semantic fact extraction — accepted comparative checkpoint (2026-11-12)

A dedicated semantic-fact benchmark extracts the J2-A3 oracle independently through Eclipse JDT and JavaParser. Both candidates receive the same bounded module/source fixture and the same 12 stable fact definitions. The harness emits per-fact JSON evidence and compares canonical target identities and expected resolution states. It does not compare parser-specific AST counts or treat diagnostic totals as equivalent.

The corpus contains 12 facts across 11 categories: record declaration, record inheritance, generic interface supertype, annotation, constructor invocation, generic method invocation, overload selection, constructor method reference, JPMS requires, JPMS exports, unresolved type and implicit record-component accessor. Canonical method/constructor identities include fully qualified declaring type, parameter types and return type; generic type arguments are retained.

CI runs both candidates in report-only mode. A mismatch is preserved in the output and does not suppress the second candidate's run. Strict local mode remains non-zero on mismatches. This is an evaluation policy, not a pass-through correctness gate: the oracle remains authoritative and a green comparison workflow means that both evaluations completed, not that both candidates passed every fact.

### Results

| Candidate | Facts resolved correctly | Mismatches |
|---|---:|---:|
| Eclipse JDT Core 3.47.0 | 12/12 | 0 |
| JavaParser 3.28.2 + Symbol Solver | 9/12 | 3 |

The three JavaParser mismatches are:
- Implicit canonical constructor binding for the Customer record.
- Implicit record-component accessor binding for Customer.id().
- Constructor method-reference binding for Customer::new when it targets the implicit canonical record constructor.

JDT resolved all 12 expected facts. The overload-resolution probe calls OverloadProbe.select(String) directly so overload selection is independently tested rather than being conflated with the implicit record accessor. The accessor remains a separate fact. No expected identity was relaxed and no implicit record member was added explicitly to the fixture to mask a candidate limitation.

### CI evidence

All mandatory workflows passed on implementation head `4575ec46bec25464dd83300ed5abb114438f9bcf`:
- Java semantic engine spike — run `43184106145`, success.
- Non-WPF quality gates — run `43184106079`, success.
- WPF build validation — run `43184106137`, success.

**Decision:** J2-A4 is accepted as a comparative-evaluation checkpoint. Both candidates were exercised against the same oracle, mismatches were retained and reviewed, and all mandatory CI workflows passed. This checkpoint does not require artificial candidate parity: it establishes the measured capability difference. JDT is the preferred candidate and working selection for J2-A5 because it resolved 12/12 facts; JavaParser's three missing implicit-record-member bindings are material correctness gaps for the required Java semantic model. This is a provisional engineering selection, not final production approval.

J2-A5 remains necessary before adding a production dependency. It must broaden the corpus to representative repositories and generated/partial-source scenarios; formalize diagnostic categories; evaluate JPMS module-path behaviour; measure performance and memory through repeated, isolated worker runs; and assess licensing, packaging, worker lifecycle, cancellation, timeouts, crash isolation and concurrency. JDT remains the preferred candidate unless that evidence demonstrates a material correctness, performance, operational or legal disadvantage. J2-B remains blocked until J2-A is complete and the production-engine decision is approved.


## J2-A5 — Broader evaluation plan — in progress (2026-11-12)

J2-A4 established a bounded correctness advantage for JDT (12/12) over JavaParser (9/12), including three material implicit-record-member binding gaps in JavaParser. JDT is therefore the preferred candidate entering J2-A5, but the result is not sufficient for production approval.

### J2-A5.1 — Representative corpus and diagnostic taxonomy

The next corpus expansion must include generated-source output, incomplete/partial source, malformed syntax, genuinely unresolved symbols, and incomplete dependency environments. It must also exercise additional semantic binding cases: sealed hierarchies, nested and generic types, wildcard bounds, lambdas/method references, default interface methods and overloaded generic calls.

The normalized diagnostic model must distinguish:
- syntax errors;
- compiler errors;
- unresolved symbols;
- unsupported constructs;
- incomplete analysis environments;
- infrastructure failures;
- warnings, retained separately from errors.

Every normalized diagnostic must retain engine-native category/code, message and source location where available. A failed binding must not automatically be classified as an unresolved symbol: incomplete classpaths, unsupported syntax, recovery behaviour and infrastructure failures are distinct causes. Ordinary analysis must not execute build scripts or annotation processors; generated source is supplied as fixture input with explicit provenance.

### J2-A5.2 — Repeatable performance and memory

Use independent JVM processes, explicit warm-up, repeated measured trials, startup/analysis separation, median and p95 reporting, and process-boundary peak resident memory. Store machine-readable per-trial results and environment metadata. Avoid unstable timing thresholds in mandatory CI.

### J2-A5.3 — Operational, deployment and legal review

Assess dependency closure, licensing/redistribution obligations, packaging, supported JDK, in-process versus worker-process execution, protocol versioning, lifecycle, cancellation, deadlines, crash isolation, concurrency and resource limits. The worker model remains an architectural option until evidence supports it.

### J2-A5.4 — Candidate recommendation

Consolidate semantic correctness, diagnostic quality, repeatable performance, peak memory, deployment/operational complexity and legal obligations. JDT remains the preferred candidate unless evidence demonstrates a material disadvantage. Final production approval requires this review; J2-B remains blocked.

**Current checkpoint:** J2-A5.1 is the immediate implementation target. No production parser dependency is to be added during J2-A5.


### J2-A5.1 progress — corpus expansion and diagnostic contract (2026-11-12)

The expanded semantic oracle contains 17 facts. It now covers a nested record declaration, an inherited default interface method invocation, a generic overload selection, a generated-source ordinary class declaration, and a production-source constructor reference into the generated source root. The generated class has an explicit constructor so this fact isolates generated-source visibility from implicit record-member synthesis.

| Candidate | Correct facts | Mismatches |
|---|---:|---:|
| Eclipse JDT Core 3.47.0 | 17/17 | 0 |
| JavaParser 3.28.2 + Symbol Solver | 14/17 | 3 |

The three JavaParser gaps remain exactly the J2-A4 findings: implicit canonical record constructor, implicit record-component accessor and constructor method reference to the implicit canonical record constructor. The expanded corpus did not add another gap.

The new `semantic-diagnostics.tsv` defines seven engine-neutral diagnostic scenarios: syntax error, compiler/type error, unresolved symbol, incomplete declared dependency environment, unsupported construct at a lower configured source level, warning, and infrastructure failure. The corpus validator passes all seven categories and enforces stable IDs, severity/category consistency, fixture containment and source-level constraints. It validates scenario integrity only; neither candidate has yet been credited with normalized diagnostic coverage.

All mandatory workflows passed on head `5c91f92f2d7f104e3b955944c5deebb4a0f10df7`:
- Java semantic engine spike — run `43188405804`, success.
- Non-WPF quality gates — run `43188405806`, success.
- WPF build validation — run `43188405812`, success.

**Decision:** The semantic-corpus expansion sub-checkpoint is accepted. J2-A5.1 remains in progress until JDT and JavaParser diagnostic extraction is normalized and asserted against the scenario contract. JDT remains the preferred candidate; final production approval and J2-B remain blocked.


## J2-A5.1 diagnostic-normalization correction

Review of the first diagnostic benchmark identified a false-positive risk: the initial implementation stamped the expected category onto any selected JDT problem and used the scenario's expected category to label JavaParser resolution exceptions. It therefore did not independently establish all seven categories.

The corrected scenario contract includes explicit symbol identity and declared-artifact coordinates for the two unresolved-symbol cases. A result is classified as incomplete-environment only when the engine emits native unresolved-symbol evidence matching the fixture symbol and the fixture imports the symbol whose declared artifact is explicitly recorded as omitted. The ordinary unresolved-symbol case has no declared-artifact evidence. The benchmark compares observed category **and severity** with the expected contract; it does not stamp expected values into observations.

The unsupported-construct case also requires native parser/compiler rejection at source level 17 and evidence tied to the Java 21 record-pattern construct. JavaParser compiler-error and warning absence remains a capability gap, not a pass. The infrastructure case is an actual controlled missing-input read failure.

This correction is a prerequisite for accepting diagnostic taxonomy coverage. Until the strict JDT result and report-only JavaParser evidence have been inspected on the corrected benchmark, the diagnostic sub-checkpoint remains in progress.


### J2-A5.1 diagnostic normalization — measured result (2026-11-12)

The corrected benchmark derives observed categories from native engine diagnostics/resolution failures plus explicit project-model evidence. It compares normalized category and severity; expected categories are not copied into observations.

| Candidate | Diagnostic scenarios matched | Capability gaps |
|---|---:|---|
| Eclipse JDT Core 3.47.0 | 7/7 | None in this bounded corpus |
| JavaParser 3.28.2 + Symbol Solver | 5/7 | Compiler/type-check diagnostics and deprecation warnings are not provided by its parser/symbol-solver API |

Both candidates distinguish an ordinary unresolved symbol from a declared dependency omitted from the analysis classpath. JDT reports the omitted Spring dependency at the import declaration (ImportNotFound); normalization correlates that native evidence with the qualified imported symbol and declared-artifact metadata. Both candidates also report the Java 21 record-pattern construct as unsupported at configured source level 17. JavaParser's absent compiler-error and warning diagnostics are retained as explicit capability gaps in report-only mode, not counted as successful matches.

The infrastructure scenario is a controlled missing-input read failure. The corpus validator checks scenario integrity; the benchmark independently exercises engine observations. Native problem codes/messages and source-line evidence are retained in the per-case output.

**Sub-checkpoint decision:** Accept diagnostic normalization for the current bounded corpus. JDT has complete coverage (7/7); JavaParser has two known capability gaps (5/7). This adds evidence in favour of JDT, but does not by itself constitute production approval. J2-A5.1 remains open for broader diagnostic cases and representative project validation; J2-A5.2 performance/memory and J2-A5.3 operational/legal review remain outstanding. J2-B remains blocked.

**CI evidence on corrected implementation head** 139ec582689f90d8303ac35ebb754bd14ccff578:
- Java semantic engine spike — run 43191794838, success.
- Non-WPF quality gates — run 43191794907, success.
- WPF build validation — run 43191794884, success.


## J2-A5.2 — Repeatable performance and memory protocol

The next performance comparison uses isolated child JVMs rather than the Maven plugin's in-process classloader. Each candidate receives identical fixture roots, identical optional external JARs, the same harness/runtime classpath and fixed JVM settings. Source-only and controlled-external-classpath modes are measured independently. Candidate order alternates across trials. The JDT side uses ASTParser.createASTs for the complete source-file set so binding work can be shared across compilation units, as supported by the JDT API. JavaParser uses one configured parser and a shared JavaSymbolSolver/CombinedTypeSolver for the same source roots. Both candidates receive the same source files, external JAR set and JVM resource settings; their native processing strategies are measured as intended usage paths.

The CI protocol performs two warm-ups and seven measured processes per candidate and mode. It records analysis duration separately from end-to-end process duration (which includes JVM startup/shutdown), and samples process peak RSS from Linux /proc VmHWM while each child is alive. Reports include per-trial JSON Lines and per-engine/mode sample count, median and nearest-rank p95 for both duration measures and peak RSS. Environment metadata and file/binding counts accompany the measurements.

This is deliberately not a hard timing gate. The bounded fixture corpus, current runner environment and seven samples are useful comparative evidence, not a production-scale performance claim. The prior post-run heap snapshot remains descriptive only and is not treated as peak memory. The harness fails explicitly outside Linux rather than substituting a non-equivalent memory metric.

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

