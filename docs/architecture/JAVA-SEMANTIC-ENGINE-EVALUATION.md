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
