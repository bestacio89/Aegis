# Polyglot native semantic relationship normalization

## Purpose

Normalize provider-native Java, Kotlin, and other semantic relationship observations into the shared architecture relationship taxonomy without replacing the original provider evidence or changing graph-edge eligibility.

## Evidence flow

1. Semantic workers emit provider-native symbols and relationships.
2. `SemanticRelationshipEvidenceRegistry` retains every relationship occurrence with run and relationship indexes.
3. `SemanticSymbolIndex` retains every symbol occurrence and applies the canonical ownership contract.
4. `SemanticRelationshipNormalizationRegistry` derives a language-neutral classification and associates source/target symbol occurrences only when the provider IDs identify exactly one symbol within that same run.
5. Later resolvers and graph projections decide whether normalized observations are eligible for correlation or graph edges.

The normalization registry is a derived runtime view. It does not replace `SemanticAnalysisRuns`, rewrite provider records, or serialize duplicate derived state.

## Classification

Provider-native kinds map to equivalent architecture kinds:

| Provider-native kind | Architecture kind |
|---|---|
| Declares | Declares |
| Inherits | Inherits |
| Implements | Implements |
| Calls | Calls |
| References | References |
| UsesAnnotation | UsesAnnotation |
| Injects | DependencyInjection |
| Registers | Registration |
| LoadsDynamically | DynamicLoading |
| Actualizes | Actualizes |
| DependsOn | Unknown |
| Unknown or unrecognized | Unknown |

Generic `DependsOn` deliberately remains `Unknown`: it does not establish whether the relationship is a call, reference, registration, or another specific semantic fact. The original provider kind remains available on every normalized item.

## Endpoint and ownership rules

- Endpoint provider IDs are resolved only within the relationship's originating semantic run. Provider IDs are opaque and must not be treated as workspace-global identifiers.
- An endpoint is associated only when exactly one symbol occurrence in that run has the requested ID.
- Duplicate matches remain explicitly ambiguous; missing IDs or missing declarations remain unresolved.
- An external symbol occurrence may be retained even when it has no canonical identity. Its analysis-context `ProjectPath` is not declaration ownership.
- A canonical external endpoint identity exists only when the provider supplies explicit, fully qualified `OwningProjectPath`.
- Repeated observations across runs remain separate evidence occurrences, even when provider relationship IDs match.

## Provider behavior retained

Java JDT emits compiler-backed inheritance, implementation, call, reference, and annotation relationships, including unresolved observations and provenance. Kotlin K2 IR emits inheritance/implementation, call, reference, and annotation relationships. Kotlin currently aggregates repeated same-source/kind/target observations into one relationship with multiple call-site provenance entries; normalization retains that provider evidence granularity.

The active component projector remains the existing graph consumer. The unused Java-only project dependency detector is not activated; its workspace-wide provider-ID grouping is not compatible with the canonical identity contract.

## Non-goals

- No new graph edges are created by normalization.
- No relationship is inferred from build reachability, matching names, DTO shape, or taxonomy membership.
- No external ownership is inferred from the consumer run path.
- No Java/Kotlin worker protocol changes or WPF behavior changes are required by this normalization boundary.
