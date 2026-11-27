# Polyglot semantic symbol index

## Purpose

The semantic symbol index is a derived, immutable lookup structure over the complete semantic-run snapshot. It centralizes declaration occurrence identity, language-qualified lookup, provider-ID lookup, and defensible project ownership without applying resolution policy.

## Occurrence model

Every symbol occurrence is retained, including repeated declarations across runs, duplicate provider IDs, empty IDs, missing or invalid ownership, external declarations, and symbols from failed or partial runs. Each occurrence retains the original run and symbol references plus zero-based run and symbol indexes.

A canonical `SemanticSymbolIdentity` is attached only when the existing identity contract can establish language, owner, and provider ID. Source/generated declarations inherit their run project as owner. External declarations require an explicit, fully qualified `OwningProjectPath`; their analysis-context project path is never treated as declaration ownership.

## Lookups

The index supports:
- run-scoped occurrence enumeration;
- canonical identity lookup;
- opaque provider symbol ID lookup;
- language-qualified name lookup (language is case-insensitive; qualified name is ordinal);
- normalized owner project path lookup (host filesystem path semantics).

All lookup results preserve occurrence multiplicity and deterministic source order. The index does not collapse duplicates, choose among ambiguous candidates, infer missing owners, or filter by run status/origin.

## Resolution policy

Consumers must explicitly select eligible run statuses and declaration origins, require unique candidates where needed, and apply build reachability or variant compatibility separately. The component graph projector uses the index for its existing source lookup, external symbol discovery, exact-owner matching, qualified-name fallback, and unique provider-ID fallback. Existing fail-closed ambiguity and Android variant behavior remain projection policy.

## Lifecycle

The index is rebuilt from `ProjectArchitectureContext.SemanticAnalysisRuns` and exposed as a non-serialized derived property. The semantic runs remain the persisted source of truth. The index is snapshot-scoped and is not a persistent cross-scan identity store.

## Non-goals

This index does not discover relationships, equate structurally similar types, infer HTTP or messaging integrations, or create graph edges. It does not replace build dependency data or provider-native semantic evidence.
