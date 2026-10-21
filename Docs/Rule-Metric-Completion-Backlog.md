# Rule Metric Completion Backlog

## Purpose

This document turns the HeroService scan into an implementation backlog for closing the gap between Aegis's 73 registered rules and the evaluator evidence currently available to the rule engine. It is the delivery plan for metric contracts, producers, applicability, reporting, and regression tests—not a claim that the listed capabilities already exist.

**Correctness is the acceptance criterion.** A rule must be evaluated only from evidence that matches its declared meaning, scope, unit, denominator, applicability, and completeness. Missing evidence must not be converted into a zero, a pass, or a fabricated violation.

## Baseline: HeroService local scan

Captured from the user's local run on 2026-10-21:

| Signal | Observed value |
|---|---:|
| Applicable evaluators executed | 31 |
| Source files reported by NamingEvaluator | 7,176 |
| Files processed by PerformanceEvaluator | 6,909 |
| Facts generated | 8,675 |
| Registered rules | 73 |
| Rules evaluated | 11 (15.1%) |
| Rules not evaluated | 62 (84.9%) |
| Rule checks | 32,770 |
| Checks passed / violated | 27,976 / 4,794 |
| Facts with no declared rule metric | 1,146 |
| Facts with declared metrics only at the wrong scope | 13 |
| Invalid thresholds / non-finite metrics skipped | 0 / 0 |
| Project summary facts processed | 22 |
| Final report snapshot | ID 17; ~29.1 MB |

The 4,794 violations are not a success metric on their own. Coverage changes may legitimately alter them. The goal is correct and explainable decisions, not a target violation count.

### Known working change

The project summary from `ApiConsistencyEvaluator` now emits `ApiConsistencyIndex` at project scope, in addition to compatibility metrics `AverageApiConsistencyIndex` and `ApiMaturityIndex`. This addresses the declared metric for `AEG-ARCH-R7`. The run moved from 10 to 11 evaluated rules, consistent with one additional rule becoming evaluable.

### Additional observed issue: configuration parse visibility

`ConfigurationEvaluator` logged repeated `JsonReaderException` and `YamlDotNet.Core.SemanticErrorException` exceptions while ending with 295 files analyzed and zero diagnostics. This discrepancy needs investigation. Malformed inputs must be counted and explained in evaluator results; they must not disappear from diagnostics or be mistaken for successfully analyzed configuration. The scan should continue where safe.

### Contract corrections made during implementation

The baseline inventory records the metric keys declared at the time of the HeroService scan. The implementation must change a contract when its name or semantics do not match the evidence actually produced:

- `AEG-BACK-R2`: changed from `CyclomaticComplexityIndex` to `EstimatedCyclomaticComplexity`. The current producer uses source-pattern heuristics, not compiler-backed cyclomatic complexity; it now emits a measurement for compliant and violating methods.
- `AEG-BACK-R5`: changed from `MaintainabilityIndex` to `MaintainabilityScore`. The producer emits a documented Aegis heuristic based on lexical decision points, excess file length, and comment-density policy. It is explicitly not presented as a standard industry maintainability index.
- `AEG-FRONT-R11`: remains a Razor fallback only where a dedicated framework-specific complexity rule does not apply. Shared metric names cannot be reused across framework facts.

These corrections intentionally supersede the original metric names in the baseline tables. They do not mark the remaining inventory complete.

## Non-negotiable design contracts

1. **Separate statistics by responsibility.**
   - Evaluator execution: registered, enabled, applicable, started, completed, failed, cancelled, duration, diagnostics.
   - Metric production: emitted metrics, scope, producer, coverage, completeness, unsupported inputs, parse/read failures, unmatched metrics.
   - Rule evaluation: registered, enabled, applicable, not applicable, evaluated, passed, violated, inconclusive/not evaluated, checks, findings.
2. **Scope is a hard boundary.** File/entity facts cannot satisfy project rules, and project summaries cannot satisfy file rules. A project metric must not be copied to every file to inflate coverage.
3. **No evidence is not zero.** Empty, unsupported, malformed, unreadable, partially analyzed, and genuinely measured zero are distinct states.
4. **No findings-only aggregates.** A percentage or index must use its declared population, including compliant observations. A list of violations alone is not a valid denominator.
5. **Aliases require proof.** An alias is allowed only after verifying semantic meaning, units, polarity, aggregation, scope, and evidence provenance. Similar names are not proof of equivalence.
6. **Heuristics are labelled.** Lexical/source-pattern estimates must identify themselves as heuristics and expose limitations; they must not claim compiler-backed or standard industry measurements without implementing those semantics.
7. **Applicability is evidence-based.** A rule may be not applicable only when the required context positively establishes that. Unknown technology or failed discovery is not proof of inapplicability.
8. **Reports must reconcile.** Summary counts must derive from detailed rule/evaluator records and agree with them.

## Inventory of rules reported unevaluated by the baseline

The following 62 warnings were emitted by the scan. Their metric names and required scopes below are transcribed from the run; they are not assumptions about what a producer ought to emit. Every item must end in one of three explicit states: supported and evaluated; not applicable with evidence; or unsupported/inconclusive with a precise reason and owner/backlog item.

### Architecture

| Rule | Required metric | Scope | Work required |
|---|---|---|---|
| AEG-ARCH-R2 | `LayerIsolationCompliance` | Project | Build dependency/layer evidence and a documented denominator. |
| AEG-ARCH-R3 | `ForbiddenReferenceCount` | Project | Detect configured forbidden dependency directions and aggregate counts. |
| AEG-ARCH-R4 | `CrossDomainLeakageCount` | Project | Define domain boundaries and detect boundary leakage from reliable evidence. |
| AEG-ARCH-R5 | `InfrastructureInversionCompliance` | Project | Measure dependency direction/inversion separately from generic layer isolation. |
| AEG-ARCH-R6 | `ControllerLogicDensity` | File | Identify controller/API-boundary files and define logic-density numerator/denominator. |
| AEG-ARCH-R8 | `GodClassDensity` | Project | Define candidate criteria and project denominator; do not conflate a candidate count with density. |
| AEG-ARCH-R9 | `LayerCohesionIndex` | Project | Define layer-level cohesion evidence and aggregation. |
| AEG-ARCH-R10 | `SecurityComplianceIndex` | Project | Establish actual security evidence sources and coverage; do not infer compliance from no findings. |
| AEG-ARCH-R12 | `NodeCouplingIndex` | File | Emit file-level node coupling from dependency graph evidence or revise contract if this is intrinsically graph-node scoped. |

### Backend quality

| Rule | Required metric | Scope | Work required |
|---|---|---|---|
| AEG-BACK-R1 | `CohesionIndex` | File | Produce cohesion measurements for analyzable files/classes, including compliant cases. |
| AEG-BACK-R2 | `CyclomaticComplexityIndex` | File | Define complexity semantics and denominator; use syntax/semantic analysis where possible and label heuristics honestly. |
| AEG-BACK-R3 | `DependencyGraphDensity` | Project | Define graph vertices/edges and density formula, then aggregate the graph. |
| AEG-BACK-R4 | `ErrorHandlingCoverage` | Project | Define which operations require handling and how coverage is assessed. |
| AEG-BACK-R5 | `MaintainabilityIndex` | File | Implement a documented maintainability formula or rename/redefine the rule to match actual evidence. |

### Dependency intelligence

| Rule | Required metric | Scope | Work required |
|---|---|---|---|
| AEG-DEPS-R1 | `OutdatedPackageRatio` | Project | Compare declared package versions with a defined trusted source and denominator. |
| AEG-DEPS-R2 | `DependencyFreshness` | Project | Define freshness semantics independently of outdated-package ratio. |
| AEG-DEPS-R3 | `UnusedReferenceCount` | Project | Analyze project references/package usage with language-aware evidence; missing usage evidence is inconclusive. |
| AEG-DEPS-R4 | `MajorVersionDrift` | Project | Define drift across a dependency set and version comparison source. |

### Design patterns

| Rule | Required metric | Scope | Work required |
|---|---|---|---|
| AEG-DES-ADM001 | `AnemicDomainModelIndex` | File | Define domain-model applicability and the evidence needed to assess behavior vs data. |
| AEG-DES-BLD001 | `BuilderPatternComplianceIndex` | File | Emit compliance measurements for identified builder candidates. |
| AEG-DES-BLD002 | `BuilderMutabilityViolationCount` | File | Detect the declared mutability violations for builder candidates. |
| AEG-DES-CIRC001 | `PatternCircularReferenceCount` | File | Define the pattern relationships considered circular and emit per-file evidence. |
| AEG-DES-CMD001 | `CommandHandlerPairingIndex` | File | Match command/handler declarations and define partial/unmatched pairing semantics. |
| AEG-DES-CMD002 | `CommandHandlerOverloadCount` | File | Detect and count overloads under a documented matching rule. |
| AEG-DES-DEC001 | `DecoratorPatternComplianceIndex` | File | Convert structurally supported decorator candidates into explicit compliance measurements. |
| AEG-DES-DEC002 | `DecoratorSelfReferenceCount` | File | Detect self-reference separately from naming/candidate classification. |
| AEG-DES-FAC001 | `FacadePublicMethodCount` | File | Measure public surface only for confidently identified facades. |
| AEG-DES-FAC002 | `FacadePatternComplianceIndex` | File | Define encapsulation expectations and emit compliance for assessed candidates. |
| AEG-DES-FACR001 | `FactoryPatternComplianceIndex` | File | Assess factory structure and compliance, including compliant candidates. |
| AEG-DES-FACR002 | `FactoryDirectInstantiationCount` | File | Detect direct instantiation relevant to factory boundaries, not every `new` expression. |
| AEG-DES-GOD001 | `GodClassDensity` | File | Emit file-level density with explicit numerator/denominator; reconcile semantics with AEG-ARCH-R8. |
| AEG-DES-MEDI001 | `MediatorCouplingIndex` | File | Define coupling evidence for mediator usage and emit file-level measurements. |
| AEG-DES-MEDI002 | `MediatorDirectCallCount` | File | Detect direct handler calls in the intended mediator context. |
| AEG-DES-OBS001 | `ObserverUnsubscribeMissingCount` | File | Assess observable/subscriber lifetimes and supported unsubscribe patterns. |
| AEG-DES-OBS002 | `ObserverCouplingIndex` | File | Define observer coupling inputs and denominator. |
| AEG-DES-OVER001 | `PatternOveruseIndex` | File | Define overuse criteria by pattern and avoid equating repeated use with misuse. |
| AEG-DES-REPO001 | `RepositoryInterfaceComplianceIndex` | File | Define repository applicability and interface/abstraction expectations. |
| AEG-DES-REPO002 | `RepositoryIOOperationCount` | File | Detect relevant external I/O inside confidently identified repositories. |
| AEG-DES-SING001 | `SingletonThreadSafetyIndex` | File | Assess singleton state and concurrency protections using documented criteria. |
| AEG-DES-SING002 | `SingletonGlobalStateUsage` | File | Detect global/shared state in singleton candidates, not unrelated classes. |
| AEG-DES-STRAT001 | `StrategyConditionalCount` | File | Detect conditional strategy selection candidates with bounded false positives. |
| AEG-DES-STRAT002 | `StrategyPatternComplianceIndex` | File | Implement or explicitly mark unsupported; the evaluator was disabled by policy in this run. |

### Frontend

| Rule | Required metric | Scope | Work required |
|---|---|---|---|
| AEG-FRONT-R1 | `SelectorScore` | File | Analyze Angular selector conventions only when Angular evidence is present. |
| AEG-FRONT-R2 | `ComplexityScore` | File | Measure Angular component complexity with documented formula and supported extensions. |
| AEG-FRONT-R3 | `HookDisciplineScore` | File | Analyze React hook rules only for supported React source files. |
| AEG-FRONT-R4 | `TypingScore` | File | Define React component typing checks and scoring. |
| AEG-FRONT-R5 | `PresentationSeparationScore` | File | Analyze Razor view business-logic leakage with explicit coverage. |
| AEG-FRONT-R6 | `ViewModelScore` | File | Detect direct entity exposure only where view/model relationships can be established. |
| AEG-FRONT-R7 | `DataIsolationScore` | File | Detect data-access leakage in Razor views using supported syntax/evidence. |
| AEG-FRONT-R8 | `ComplexityScore` | File | Measure Blazor component complexity only for positively identified Blazor files. |
| AEG-FRONT-R9 | `CodeBehindScore` | File | Define inline-code ratio and denominator for Blazor components. |
| AEG-FRONT-R10 | `DependencyInjectionScore` | File | Detect direct service construction in Blazor components using supported syntax. |
| AEG-FRONT-R11 | `ComplexityScore` | File | Define cross-framework applicability and normalize the score meaning across supported frameworks, or split the contract. |

### Infrastructure, performance, and transactions

| Rule | Required metric | Scope | Work required |
|---|---|---|---|
| AEG-INF-R1 | `ConfigurationHealthIndex` | Project | Include malformed/unreadable configuration in coverage and produce a project score with explicit denominator. |
| AEG-INF-R2 | `LoggingHygieneIndex` | Project | Define structured logging and undesirable logging evidence; aggregate all assessed source files. |
| AEG-PERF-R2 | `AsyncUsageCompliance` | File | Detect blocking waits, async misuse, and applicable cancellation practices under a documented score. |
| AEG-PERF-R3 | `MemoryAllocationRate` | Project | Determine whether static source analysis can support this claim; otherwise require runtime/profile inputs or mark unsupported. |
| AEG-PERF-R4 | `BlockingIOCount` | File | Detect supported blocking I/O APIs and distinguish unknown/custom wrappers. |
| AEG-TXN-R1 | `TransactionIntegrityIndex` | Project | Define transaction boundaries, commit/rollback expectations, coverage, and aggregation. |
| AEG-TXN-R2 | `DataAccessHygieneIndex` | File | Detect supported data-access patterns and define a score with explicit evidence. |
| AEG-TXN-R3 | `ConnectionPoolAbuseCount` | File | Detect actual misuse patterns; avoid treating every explicit connection open as abuse. |
| AEG-TXN-R4 | `UncommittedTransactionCount` | File | Detect incomplete transaction lifecycles only when static evidence supports the conclusion. |

## Delivery plan and exit gates

### Stage 1 — Contract inventory and engine semantics

- [ ] Build an executable inventory joining every registry rule to its metric contract, producer, scope, applicability, units, polarity, aggregation, denominator, and support status.
- [ ] Enumerate emitted metrics by evaluator and output scope, including project summaries and finding-only facts.
- [ ] Identify collisions where one metric key is used by rules with different semantics, particularly shared keys such as `GodClassDensity` and `ComplexityScore`.
- [ ] Distinguish no producer, wrong scope, no applicable evidence, unsupported input, partial analysis, invalid value, and disabled evaluator in report diagnostics.
- [ ] Add tests that ensure all 73 rules have an explicit contract disposition and that the diagnostic counts reconcile.
- [ ] Confirm that a disabled evaluator cannot silently leave a rule looking like a generic missing metric.

**Exit gate:** every registered rule has an explicit producer or an explicit unsupported/not-applicable policy; every metric contract gap is accounted for by tests.

### Stage 2 — Correctness and coverage conventions

- [ ] Define shared result metadata: scope, stable file/entity identity, analysis-complete flag, analyzed/skipped/unreadable counts, supported language/framework, and reason for incomplete/unsupported evidence.
- [ ] Ensure evaluators emit measurements for compliant observations as well as findings when a rule requires a ratio/index.
- [ ] Ensure project aggregations use a documented population and do not average only violating facts.
- [ ] Treat malformed or unreadable files as incomplete evidence, not a clean pass.
- [ ] Make logs actionable and avoid swallowing parser exceptions while reporting zero diagnostics.
- [ ] Add positive, negative, boundary, empty, malformed, partial, unreadable, and unsupported-technology tests.

**Exit gate:** no unsupported or incomplete analysis is reported as full coverage.

### Stage 3 — Architecture and API

- [ ] Preserve and test the `ApiConsistencyIndex` project summary contract for AEG-ARCH-R7.
- [ ] Implement and test layer isolation, forbidden references, cross-domain leakage, and infrastructure inversion as separate metrics.
- [ ] Implement controller logic density with explicit controller discovery and numerator/denominator.
- [ ] Implement project/file God Class metrics with consistent documented semantics.
- [ ] Implement layer cohesion and node coupling from the dependency graph.
- [ ] Implement security compliance only from identified security evidence and coverage.

**Exit gate:** each supported architecture rule is evaluated from its own evidence; no generic metric is used as a semantic substitute.

### Stage 4 — Backend quality

- [ ] Implement file-level cohesion, complexity, and maintainability contracts.
- [ ] Decide and document whether complexity is Roslyn-backed cyclomatic complexity or a clearly named heuristic.
- [ ] Implement project dependency graph density.
- [ ] Define error-handling coverage against identifiable operations and handled failure paths.
- [ ] Add fixtures with known expected measurements and boundary values.

**Exit gate:** metric values and denominators are deterministic and independently testable.

### Stage 5 — Design-pattern analysis

- [ ] Create one explicit contract per pattern rule, with candidate detection separated from compliance assessment.
- [ ] Emit pattern-specific metrics only for relevant candidates/files, while representing unassessed files as such.
- [ ] Implement the missing builder, command, decorator, facade, factory, mediator, observer, repository, singleton, strategy, anemic-model, circular-reference, and pattern-overuse metrics.
- [ ] Investigate why `StrategyPatternEvaluator` is disabled by policy; reflect that state in rule applicability/reporting.
- [ ] Test positive candidate, compliant candidate, violating candidate, no candidate, false-positive near miss, and unsupported syntax.

**Exit gate:** absence of a pattern is never misreported as successful compliance or as a violation unless the rule explicitly requires that pattern.

### Stage 6 — Dependency intelligence

- [ ] Build an authoritative package/reference inventory from project files and supported manifests.
- [ ] Define trusted version sources, cache/failure behavior, and reproducibility for freshness and major-version drift.
- [ ] Analyze unused references only where language/project metadata and symbol usage are available.
- [ ] Distinguish unknown/offline version information from up-to-date dependencies.
- [ ] Test central package management, floating versions, prereleases, transitive dependencies, and missing feeds as applicable.

**Exit gate:** unavailable external metadata produces a transparent inconclusive result, never a fabricated clean score.

### Stage 7 — Frontend analysis and applicability

- [ ] Make framework detection evidence-based for Angular, React, Razor, and Blazor.
- [ ] Do not run framework-specific rules on unrelated source files; unknown framework remains unknown, not automatically not applicable.
- [ ] Implement the declared metrics with supported syntax parsing and documented heuristic confidence.
- [ ] Resolve shared `ComplexityScore` semantics so framework-specific rules do not accidentally compare incomparable scales.
- [ ] Test mixed repositories and repositories with no frontend.

**Exit gate:** each frontend rule is assessed only against supported, positively identified inputs, with complete coverage metadata.

### Stage 8 — Infrastructure, performance, transactions, and security

- [ ] Fix configuration parse diagnostics and produce `ConfigurationHealthIndex` from a defensible denominator.
- [ ] Produce project logging hygiene from structured/undesirable logging evidence.
- [ ] Implement async misuse and blocking-I/O detection with file-level metrics.
- [ ] Decide whether memory-allocation rate is statically measurable; if not, require profiling input or mark unsupported.
- [ ] Define transaction-integrity, data-access hygiene, connection-pool misuse, and incomplete-transaction contracts.
- [ ] Define security compliance inputs and partial-coverage behavior.

**Exit gate:** metrics reflect the evidence actually available; no static source scan claims runtime measurements without runtime data.

### Stage 9 — End-to-end validation and release gates

- [ ] Add deterministic evaluator-to-rule-to-report tests with known fixture expectations.
- [ ] Add a HeroService regression scenario; compare rule coverage, completeness, and diagnostic categories, not just violation totals.
- [ ] Verify evaluator stats, metric stats, rule stats, finding counts, and domain aggregates reconcile.
- [ ] Preserve report-storage testing as already exercised; do not divert this work into storage changes unless a regression is observed.
- [ ] Run all non-WPF tests and production builds in CI on every PR.
- [ ] Publish rule coverage and missing/wrong-scope/inconclusive counts as CI artifacts.
- [ ] Establish performance and coverage thresholds only after correctness baselines stabilize.

**Final exit gate:** all 73 rules have an explicit, tested disposition; all enabled/applicable rules with supported complete evidence are evaluated; remaining rules have precise reasons and are not silently omitted.

## Recommended PR sequence

1. **Contract inventory and diagnostics:** executable 73-rule/metric-producer inventory, metric semantics, reporting separation, contract tests.
2. **Architecture/API:** complete architecture metrics, preserving the API consistency fix.
3. **Backend:** cohesion, complexity, dependency graph density, error handling, maintainability.
4. **Design patterns:** pattern-specific metrics and candidate/compliance semantics.
5. **Dependencies:** package inventory, version intelligence, reference usage.
6. **Frontend:** framework applicability and framework-specific metrics.
7. **Infrastructure/performance/transactions/security:** evidence-backed metrics and explicit unsupported states.
8. **Regression and release gates:** deterministic end-to-end report verification and CI artifacts.

Keep each PR reviewable and based on `develop`. Do not merge a stage until tests pass and the HeroService baseline shows the expected coverage change with no unexplained metric or scope gaps. Update this backlog as stages complete; do not mark a rule complete merely because its warning disappeared.
