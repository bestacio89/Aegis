# Polyglot semantic relationship evidence registry

## Purpose

The evidence registry is the lossless indexing boundary between provider output and downstream relationship consumers. It retains every relationship occurrence from every semantic run, including unresolved, partially resolved, targetless, unsupported-kind, duplicate-ID, and ID-less observations.

## Identity and traceability

Provider relationship IDs are optional and are only meaningful within their originating run. They are not assumed unique: providers can emit duplicate IDs, and separate runs can reuse the same run or relationship IDs.

The registry therefore assigns each occurrence a snapshot-scoped identity composed of:
- the zero-based run index in the source `ProjectArchitectureContext.SemanticAnalysisRuns` collection;
- the zero-based relationship index in that run's `Relationships` collection.

The evidence entry also retains the original run metadata, the provider relationship ID, and the original `SemanticRelationship` instance. No provider relationship is rewritten, inferred, deduplicated, or discarded.

Snapshot indexes are exact references within the current analysis context. They are not cross-scan persistent identifiers: reordering runs or relationships changes the indexes. Stable cross-scan identity remains the responsibility of providers and future canonical identity contracts.

## Registry invariants

- Registry construction is independent of run status and relationship resolution status.
- Registry construction does not filter by relationship kind, source/target completeness, provider ID, or confidence.
- Every list occurrence receives one unique evidence identity, even when the same relationship instance or provider ID appears more than once.
- The registry is read-only after construction and preserves source ordering.
- Relationship classification, provenance, resolution status, and confidence remain separate fields.
- Registry membership does not make an observation eligible for a graph edge.

## Projection boundary

The existing semantic component projector now reads its candidate observations from the registry. Its graph eligibility rules remain unchanged: only supported native relationship kinds with a resolved or partially resolved target ID proceed to target resolution. Unresolved and targetless observations remain in the registry but are not promoted to graph edges.

Projected dependency references now carry both the provider identifiers and snapshot indexes. This closes the previous traceability gap for missing or duplicate provider relationship IDs without changing the graph's semantic edge eligibility.

## Scope and non-goals

This checkpoint does not add cross-language resolution, HTTP/API discovery, a new resolver, worker protocol changes, or WPF presentation behavior. It establishes lossless evidence retention and exact in-snapshot traceability for later symbol-index and relationship-integration phases.
