# Architectural Dependency Intelligence

## Status

- Baseline: `develop` at `26d97c3b3c95e4558cd1ea072fbe1a3d8e2c8104`
- Working branch: `feat/architectural-dependency-intelligence`
- Status: ADI-1 evidence traceability validated; ADI-2 declarative mapping and conservative classification implemented, pending exact-head CI
- Scope: introduce a logical architectural-component projection while preserving the existing build-module and language-container dependency views

## Executive assessment

Aegis already has most of the raw evidence and two structural projections required for this capability. The implementation must extend those contracts rather than create a parallel graph engine.

The current model distinguishes:

1. **Build modules** — independently discovered build-system units such as .NET projects, Maven modules, Gradle projects, npm workspaces and Python distributions.
2. **Language-level code components** — Java/Kotlin/Python packages, TypeScript/JavaScript modules, and C# projects. The exact boundary semantics are provider/language-specific.
3. **Semantic declarations and relationships** — language-neutral symbols and resolved/partially resolved relationships with provenance.
4. **Runtime integrations and dependency inventory** — deliberately separate from internal source-level dependency edges.

The missing layer is a stable, explainable mapping from language-level code components into logical architectural components such as Domain, Application, Infrastructure, Presentation, Shared Kernel, or user-defined boundaries.

## Existing implementation findings

### Shared evidence and context

- `SemanticSymbol` represents a language-neutral declaration. It carries an opaque provider-generated ID, qualified name, container identity, language, project path, file path, source origin, test ownership and provenance.
- `SemanticRelationship` retains source and target symbol IDs, relationship kind, resolution status, test scope and provenance. Unresolved targets are represented without fabricating target symbol IDs.
- `SemanticAnalysisRun` retains the symbols, relationships, diagnostics, capabilities and completion state for each module/language analysis.
- `ProjectArchitectureContext` already stores semantic runs, build modules, build dependencies, language-level components, semantic component dependencies, integrations and dependency evidence.
- `ArchitectureComponentContext` is the current language-level code-container projection. It is not a logical architectural component.
- `ArchitectureComponentDependencyContext` is an aggregate of semantic relationships between language-level code components. It retains relationship counts, resolution counts, relationship kinds and evidence files, but currently does not expose the contributing semantic relationship IDs.
- `ArchitectureDependencyContext` represents build/project dependency edges. It must not be conflated with semantic code dependencies.
- `ArchitectureBoundaryContext` currently represents allowed/forbidden dependency policy by source layer. It is not a logical component identity or membership model.
- `ArchitectureInferenceTrace` is a generic key/value inference record, not a sufficiently typed component-classification contract.

### Semantic component projection

`SemanticArchitectureComponentDetector` builds language-level components from semantic runs and projects resolved/partially resolved symbol relationships into component-to-component edges.

Current language semantics are intentionally different:
- C# components are build projects, not namespaces.
- Java, Kotlin and Python generally use packages.
- TypeScript/JavaScript generally use modules.
- Other providers can supply their own container kind.

The detector preserves production and test scopes separately and excludes relationships internal to the same language-level component from the inter-component edge list. Raw semantic relationships remain available in `SemanticAnalysisRun`, so internal relationships can be recovered for drill-down. The current aggregate edge model should be strengthened with explicit contributing relationship identities before it becomes the source for higher-level architectural aggregation.

### WPF graph and ledger

- `DependencyGraphViewModel` currently offers two projections: **Architectural components** and **Build modules**. The first label is misleading: it actually displays `ArchitectureComponentContext` nodes, which are language-level packages/modules/projects, not logical architecture components.
- The current graph renderer is a reusable, custom WPF graph surface with filtering, node selection, counts and relationship evidence display.
- `ArchitectureWorkspaceViewModel` exposes a Dependency Graph tab and a Relationship Ledger tab.
- `RelationshipLedgerViewModel` already displays build-module and language-level component relationships as distinct relationship levels. It aggregates semantic relationships and currently shows relationship kinds and evidence files, not the exact contributing semantic relationship IDs.
- Existing tests cover Java package projection, Python package projection, C# project-level projection and partial semantic runs. The graph projection and WPF view-model behaviour need dedicated tests.

## Target capability and terminology

The UI and contracts must use precise names:

| Projection | Node identity | Evidence source | Purpose |
|---|---|---|---|
| **Architectural components** | Logical, architect-defined or evidence-inferred component | Aggregated semantic edges, with build edges retained as a separate evidence category | Understand and assess the intended/observed logical architecture |
| **Packages / code containers** | Existing language-level `ArchitectureComponentContext` | Semantic symbol relationships | Inspect concrete code organisation and coupling |
| **Build modules** | Existing `ProjectModuleContext` | Build-system project/module references | Inspect build and project boundaries |

The second projection should display its actual boundary kind. It must not call C# project nodes namespaces or imply that all languages have Java-style packages. When the language-native container is a package, the UI may call it the package graph; otherwise it should use the neutral **Code containers** label.

The existing graph renderer should be reused. The new capability is a new projection and navigation layer, not a second dependency extraction engine.

## Proposed domain contracts

Add strongly typed, single-purpose models (one top-level type per file):

1. **ArchitecturalComponentContext**
   - Stable ID and display name.
   - Optional parent ID for future nested architecture boundaries.
   - Component role/category, language scope where relevant, and description.
   - Classification source, confidence, and provenance/matched-rule information.
   - Counts of assigned code containers, production symbols and test symbols.
2. **ArchitecturalComponentMembership**
   - Links one existing language-level code component to one logical architectural component.
   - Retains classification source, confidence, explanation and matched mapping identity.
   - Must not duplicate or rewrite the underlying semantic symbols.
3. **ArchitecturalComponentDependencyContext**
   - Aggregates observed dependencies between logical components.
   - Keeps semantic relationship evidence distinct from build/project-reference evidence.
   - Retains contributing source relationship IDs, evidence files, relationship kinds, resolution counts and production/test scope.
   - Tracks internal dependencies that collapse within one logical component separately from inter-component edges.
4. **ArchitectureClassificationDiagnostic** (or a shared analysis diagnostic with equivalent structured fields)
   - Reports unmapped components, ambiguous mappings, low-confidence inference and conflicting explicit mappings.
   - Classification uncertainty must be visible; it must not silently become an architectural fact.

Names should be reconciled with the existing `ArchitectureComponentContext` and `ArchitectureComponentDependencyContext` before implementation to avoid ambiguous near-duplicate APIs. The logical model should use a clearly distinct name such as `ArchitecturalComponentContext`, while the existing code-container model should be renamed only if a compatibility-safe migration is available. Do not break serialized reports without an explicit schema/version strategy.

## Classification strategy

Classification must be explainable and deterministic.

### Sources

1. **Explicit project configuration** — highest precedence. A versioned, non-executable configuration file (proposed: `.aegis/architecture-map.json`) maps module/package/namespace prefixes to logical architectural components.
2. **Convention-based inference** — recognises strong structural and naming signals such as `domain`, `application`, `infrastructure`, `presentation`, bounded-context names and framework conventions.
3. **Evidence-based heuristics** — use dependencies, framework evidence, project roles and existing architecture metadata as supporting signals, never as proof on their own.
4. **Unclassified** — retained as explicit classification coverage data. Do not invent a logical component for an unmapped code container.

### Mapping rules

- Match package/namespace prefixes by segments, not raw string substring. For example, `com.acme.domain` may match `com.acme.domain.entity`, but not `com.acme.domainish`.
- Scope mappings by language and build module when necessary.
- Use deterministic precedence: exact mapping, then most-specific valid prefix, then module-scoped mapping, then inference.
- Equal-precedence conflicting mappings are ambiguous and must produce diagnostics rather than arbitrary selection.
- Record the selected rule, source, confidence and explanation for every inferred or configured membership.
- Explicit configuration overrides inference. Invalid or stale mappings are diagnostics, not silently ignored.
- Classification should be recomputable from existing evidence and configuration without rerunning compiler analysis.

### Intended versus observed architecture

Keep two concepts separate:
- **Observed architecture:** what semantic and build evidence demonstrates.
- **Intended architecture:** the configured component boundaries and allowed/forbidden dependency rules.

The first release can introduce explicit mappings and observed graph aggregation. A later checkpoint can compare observed edges with intended rules and report architectural erosion, forbidden dependencies and drift. Aegis must not claim a violation merely because an inferred classification has low confidence.

## Projection and evidence rules

- Keep semantic extraction and the raw `SemanticAnalysisRun` records as the canonical evidence source.
- Build the code-container graph from symbol-to-symbol relationships, as today.
- Build the logical architectural graph by projecting code-container memberships and aggregating the existing semantic edges.
- Keep build-module edges as a separate evidence kind/filter. Do not silently merge build declarations, compiler-resolved relationships and runtime integrations into one undifferentiated edge count.
- Preserve source relationship IDs so every aggregate architectural edge can be traced back to exact semantic evidence.
- Preserve production and test scope separately.
- Inter-component edges appear in the top-level architecture graph. Relationships that collapse inside one logical component remain available as internal-coupling metrics and drill-down evidence; they are not deleted.
- Cycles must be evaluated independently at code-container and logical architecture levels.
- Unmapped code containers and classification coverage must be visible beside the graph.
- No semantic relationships, unresolved targets or partial-resolution diagnostics may be fabricated to improve graph completeness.

## WPF experience

Extend the existing Dependency Graph workspace rather than introducing a competing renderer.

Projection selector:
1. **Architectural components** — logical component graph; default when reliable logical classifications exist.
2. **Packages / code containers** — current semantic component graph, relabelled accurately.
3. **Build modules** — current project/module dependency graph.

Interaction:
- Selecting a logical component filters to its incoming/outgoing edges and offers drill-down into member code containers.
- Selecting a code container exposes its symbols and exact relationship evidence through the ledger.
- Selecting an edge exposes evidence category, counts, relationship kinds, resolution status, source files and contributing relationship IDs.
- Show classification source/confidence and unmapped coverage; inferred classifications must be visually distinguishable from explicit mappings.
- Keep test-scope relationships available in the ledger but excluded from the production graph by default.
- Empty, partial and unsupported semantic-analysis states must be explicit.

The Relationship Ledger should add a relationship-level filter and expose the underlying evidence IDs. Existing integration editing remains a separate concern and should not be coupled to graph projection logic.

## Implementation roadmap and acceptance gates

### ADI-0 — Baseline and contracts
- Complete the read-only assessment on merged `develop`.
- Add this design document and establish the dedicated feature branch.
- Define serialized model compatibility and configuration schema before adding the logical architecture contracts.
- Acceptance: assessment and implementation roadmap committed; baseline branch is based directly on merged `develop`.
- Status: completed.

### ADI-1 — Evidence traceability
- Extend semantic component aggregates to retain contributing relationship IDs and any required evidence category metadata.
- Preserve existing counts, relationship kinds, evidence files and production/test separation.
- Add regression tests proving that every aggregate count can be traced to unique source relationships and duplicate IDs are not double-counted.
- Acceptance: existing Java, Python and C# component graph tests remain green.
- Status: implementation committed; all required workflows passed on exact PR head `1af4fc62330dfe6bf5a63aa7f2c18372cd1000c9`.

### ADI-2 — Architectural classification
- Add typed logical component and membership contracts.
- Added shared contracts and additive project-context collections.
- Added a versioned JSON loader for `.aegis/architecture-map.json`, including schema, mapping and metadata validation.
- Added deterministic exact/prefix/module matching, language/module scoping, ambiguity diagnostics, conservative role-segment inference and explicit unclassified memberships. Convention-derived components are scoped by build-module identity to avoid collapsing unrelated modules into one role bucket.
- Integrated classification into both the full semantic pipeline and the Java compatibility path.
- Projected classification diagnostics and coverage counts into the architecture report so configuration and inference uncertainty is visible to report consumers.
- Added tests for serialization compatibility, exact override, longest-prefix selection, segment boundaries, module mapping, ambiguity, conservative inference and configuration loading.
- Status: implementation committed; exact-head CI validation pending.
- Implement configuration parsing/validation for explicit mappings.
- Implement deterministic segment-aware matching, ambiguity diagnostics, provenance and confidence.
- Add conservative convention inference; unclassified is a valid outcome.
- Acceptance: explicit mappings override inference; ambiguous mappings are diagnosed; unknown packages are not silently assigned; classification is deterministic and does not invoke project build scripts.

### ADI-3 — Architectural graph projection
- Aggregate code-container edges into logical component edges.
- Preserve internal collapsed relationships separately.
- Keep semantic and build evidence categories distinguishable.
- Preserve production/test separation and exact source relationship IDs.
- Acceptance: projection is lossless with respect to source evidence; package/module projections remain unchanged; cycles can be calculated at both levels.

### ADI-4 — WPF projections and drill-down
- Add the Architectural components projection to the existing graph surface.
- Correct the current misleading projection label.
- Add component membership drill-down, edge evidence inspection and classification coverage.
- Extend the relationship ledger with relationship IDs and evidence category.
- Acceptance: view-model tests cover all projections, filtering, selection, empty/partial data and drill-down; WPF build succeeds.

### ADI-5 — Architectural governance
- Compare observed dependencies with intended allowed/forbidden component boundaries.
- Add evaluators for dependency direction, forbidden edges, component cycles and architectural drift.
- Distinguish confirmed violations from low-confidence or incomplete evidence.
- Acceptance: evaluator tests include positive, negative, ambiguous and incomplete-evidence cases; no unsupported inference is reported as a confirmed violation.

### ADI-6 — Validation corpus and release
- Validate against representative Java (Maven/Gradle), C#/.NET, Python and TypeScript/JavaScript repositories.
- Include single-module and multi-module repositories, mixed-language workspaces, tests, generated code, unresolved relationships and deliberately planted architectural violations.
- Compare graph edge evidence with source declarations and relationship ledger records.
- Acceptance: all mandatory CI workflows pass on exact head; no regressions in existing .NET or Java analysis; documentation and configuration examples are complete.

## Test matrix

| Concern | Required cases |
|---|---|
| Mapping | Exact, longest-prefix, segment boundary, module scope, language scope, explicit override |
| Ambiguity | Conflicting equal-precedence mappings, stale module/package path, duplicate identities |
| Inference | Strong convention, weak convention, conflicting signals, no classification |
| Aggregation | Multiple package edges collapse into one architecture edge; counts and IDs remain exact |
| Internal coupling | Edges within one logical component are retained for drill-down and metrics |
| Scope | Production and test edges remain separate, including test-support modules |
| Evidence | Resolved and partially resolved relationships, missing target, multiple provenance files |
| Cycles | Package-level cycle and architecture-level cycle, including cycles created only by aggregation |
| Languages | Java, Kotlin, Python packages; TypeScript/JavaScript modules; C# projects |
| Modules | Single-module, multi-module, duplicate/incomplete module inventory |
| UI | Three projections, empty state, partial analysis, filtering, node/edge selection, drill-down |
| Compatibility | Existing report serialization, .NET graph, Java graph, relationship ledger and module graph |

## Non-goals for the first checkpoint

- Replacing semantic providers or re-running extraction for each projection.
- Treating package names or namespaces as definitive architectural roles.
- Collapsing runtime integrations into compile-time dependency edges.
- Hiding unresolved/partial relationships to make the graph appear complete.
- Implementing a generic graph-layout framework before the projection contracts are stable.
- Automatically rewriting project code or build configuration.

## Engineering constraints

- One top-level type per source file; filename matches the type.
- Keep domain/projection contracts out of WPF.
- Keep graph rendering out of semantic analysis.
- Use deterministic IDs and ordering.
- Preserve report compatibility and explicitly version serialized changes.
- Add focused tests with every meaningful implementation commit.
- Work on this feature branch; do not merge without explicit approval.
