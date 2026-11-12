# Java semantic-engine evaluation

**Status:** J2-A in progress. No production engine has been selected.

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

**Current decision:** no production engine selected. J2-B remains blocked.


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
