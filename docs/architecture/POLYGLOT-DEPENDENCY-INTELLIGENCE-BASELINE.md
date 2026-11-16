# Polyglot Dependency Intelligence — Phase 0 Baseline

## Purpose

Establish observable, regression-tested boundaries for semantic relationship extraction, component projection, logical architecture projection, and graph presentation before changing relationship identity or resolution behavior.

This phase is diagnostic and protective. It must not alter graph semantics, infer new relationships, or change WPF interaction behavior.

## Pipeline boundaries

1. **Provider output** — each `SemanticAnalysisRun` owns its symbols, relationships, language, engine, and module path.
2. **Code-container projection** — `SemanticArchitectureComponentDetector` maps provider source symbols into language-native components and provider-resolved relationships into component edges.
3. **Logical architecture projection** — classification and dependency projection map code containers into logical architectural roles.
4. **Presentation** — `DependencyGraphViewModel` selects a graph projection and renders its nodes and edges. It does not resolve semantic relationships.

## Existing regression coverage

The baseline already includes dedicated Java and Kotlin end-to-end tests, Java/Kotlin interop pipeline tests, Java/Kotlin component projection tests, cross-module resolution tests, C# project-component projection tests, Python package projection tests, and WPF projection-selection tests.

Phase 0 extends this coverage rather than duplicating existing fixtures.

## New instrumentation

Each usable semantic run receives a non-coverage-affecting diagnostic with code `SemanticArchitectureComponentProjectionBaseline`. It records:

- relationship observations eligible for component projection;
- provider-resolved candidates with target identities;
- unresolved or targetless observations;
- missing source symbols and missing source components;
- targets that cannot be resolved to a project component;
- incompatible Android target variants;
- relationships that pass endpoint and variant checks and reach edge aggregation.

The aggregate architecture diagnostic reports the corresponding totals alongside component and edge counts. The instrumentation does not change which relationships are projected.

## Regression additions

- A semantic projection test verifies that a resolved relationship is projected while unresolved and missing-target evidence is counted rather than silently confused with successful projection.
- A WPF graph-view-model test verifies that C#, TypeScript, and Java code-container nodes remain present together, including an isolated node, when the code-container projection is selected.

The WPF test exercises view-model projection only; it is not a substitute for a manual application UI check when production WPF behavior changes.

## Known limitations exposed by the baseline

- The code-container graph currently projects only resolved or partially resolved relationships with a non-empty target symbol ID. Unresolved observations remain in the semantic run but do not become graph edges.
- A target that cannot be mapped to a unique component is currently represented by a single failed-resolution path at this projection boundary. The new counters make that loss measurable; they do not yet distinguish all underlying causes.
- Provider symbol IDs remain scoped by module and language in the projection index. Cross-language contract relationships are not inferred in this phase.
- A graph may contain isolated code-container nodes with no edges. Node count and edge count must therefore be assessed independently.

## Phase 0 acceptance

- Existing Java, Kotlin, Java/Kotlin interop, C# projection, and graph view-model regressions remain green.
- New diagnostics are deterministic for the same semantic run and do not affect coverage scoring.
- Polyglot graph presentation retains nodes from multiple languages without changing relationship semantics.
- No resolver, relationship taxonomy, ledger, or production WPF behavior changes are included.


## Phase 1 — Protect existing Java and C# behavior

### Existing Java protection

The existing regression suite covers the packaged Eclipse JDT worker through the production semantic pipeline, Java project discovery, Maven and Gradle project dependencies, module ownership, classpath resolution, Java component projection, and Java/Kotlin interoperability.

Key executable guards include:

- `JavaSemanticAnalysisEndToEndTests.Packaged_jdt_worker_produces_semantic_evidence_through_the_production_orchestrator`
- `JavaProjectDependencyGraphTests` for Maven/Gradle module edges and production/test scope
- `SemanticArchitectureComponentGraphTests` for Java package-level component projection

### Existing C# protection

C# build-module dependency behavior is guarded by the `ProjectArchitectureContextDetectorTests.Detect_maps_project_references_to_semantic_layer_edges` regression. C# semantic component projection is also covered by the existing project-level component graph regression in `SemanticArchitectureComponentGraphTests`.

### New polyglot identity regression

The Phase 1 addition runs Java and C# semantic evidence through the same component projection in one repository/module. Both providers deliberately emit overlapping opaque symbol IDs. The test asserts that:

- all Java and C# component nodes remain present;
- each language's relationship resolves only to its own language's target;
- no cross-language edge is fabricated from an ID collision;
- each provider's relationship evidence remains independently attributable.

This specifically protects the Java-only and C#-only behavior while establishing a regression boundary for mixed-language repositories.

An additional repository-level regression creates .NET projects connected by `ProjectReference` and Maven modules connected by project coordinates in the same repository. It asserts that both native build-graph edge families survive project discovery and dependency detection together.

### Audit observation

`JavaSemanticDependencyDetector` currently exists as an implementation but is not invoked by `DependencyDetector.Analyze`; the active Java build-module dependency path invokes `JavaProjectDependencyDetector`. Phase 1 does not wire the unused detector or change dependency semantics. Its intended contract and overlap with the semantic component graph must be resolved explicitly before any activation.

### Phase 1 acceptance

- Existing Java and C# regression suites remain green.
- The new mixed Java/C# identity regression passes.
- No existing project-reference, Maven, Gradle, or language-native component behavior is removed or reinterpreted.
- No shared relationship taxonomy or resolver behavior is changed in this phase.
