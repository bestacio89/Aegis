# Polyglot Relationship Taxonomy

## Purpose

Define a stable, language-neutral vocabulary before changing symbol identity, evidence retention, resolution, or graph projection. The taxonomy separates *what kind of relationship was observed* from *how it was discovered*, *whether its endpoints resolve*, and *how confident the evidence is*.

## Canonical relationship kinds

| Range | Family | Kinds | Meaning |
|---|---|---|---|
| 100–199 | Build topology | BuildProjectReference, BuildModuleDependency, BuildPackageDependency | Declared build/package reachability. Not proof of a source-level call or runtime interaction. |
| 200–299 | Native semantic | Declares, Inherits, Implements, Calls, References, DependencyInjection, Registration, DynamicLoading, Actualizes, UsesAnnotation | Language/compiler/parser facts about declarations and source-level semantics. |
| 300–399 | HTTP/API | HttpRequest, HttpRoute | Client-to-route or route-to-handler integration evidence. |
| 400–499 | Messaging | MessagePublish, MessageConsume | Producer and consumer observations. Matching a topic/channel is correlation, not proof of delivery. |
| 500–599 | RPC | RpcCall, RpcEndpoint | Remote invocation and exposed endpoint evidence. |
| 600–699 | Data integration | DataRead, DataWrite, DataMapping | Reads, writes, and explicit mapping across data boundaries. |

The numeric ranges are reserved by family to keep additions non-overlapping. Existing provider-native `SemanticRelationshipKind` values remain intact; they are not renumbered or silently reinterpreted.

## Orthogonal dimensions

These are deliberately not encoded into the relationship kind:

- **Evidence source/provenance:** compiler, parser, build model, framework model, runtime, or inference.
- **Resolution status:** resolved, partially resolved, unresolved, or not applicable.
- **Confidence:** evidence strength, independent of resolution status.
- **Direction:** source-to-target direction as reported by the evidence; never inferred from display ordering.
- **Scope:** production, test, generated, or other explicitly identified scope.
- **Identity/ownership:** canonical endpoint identity and owning project/module, to be addressed in later phases.

An unresolved HTTP client call remains an HTTP request observation with unresolved endpoint identity. It must not be downgraded to a generic dependency or discarded. A message producer and consumer remain separate observations until a later correlation step establishes a defensible link.

## Normalization boundaries

1. Language providers continue emitting their native semantic facts.
2. Build analyzers continue emitting build topology.
3. API, messaging, RPC, and data analyzers emit integration-specific observations.
4. A shared normalization boundary maps those facts to `ArchitectureRelationshipKind` without erasing native kind, analyzer identity, or provenance.
5. Resolution and confidence are evaluated independently from classification.
6. Graph projections decide which normalized observations can become edges; taxonomy membership alone never creates an edge.

## Explicit non-equivalences

- Build dependency is not a method call.
- A type reference is not an HTTP request.
- Similar DTO/property shapes are not proof of shared contract identity.
- A route declaration is not proof that a particular client calls it.
- A shared topic name is not proof of producer-consumer delivery.
- A database client dependency is not proof of a specific table read/write.
- An inferred relationship is not equivalent to compiler- or runtime-confirmed evidence.

## Phase 2 implementation boundary

This phase introduces the shared enum and executable contract tests for stable family ranges and distinct relationship kinds. It does not yet add an evidence registry, resolver, API discovery, integration analyzers, or graph edges. Those belong to subsequent phases and must consume this taxonomy without bypassing the evidence/provenance dimensions.

## Compatibility

The existing `SemanticRelationshipKind` and build dependency models remain source-compatible. The new taxonomy is additive and is not yet populated by existing analyzers. No current graph output changes as a result of this phase.
