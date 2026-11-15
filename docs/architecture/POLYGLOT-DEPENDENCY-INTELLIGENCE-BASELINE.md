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
