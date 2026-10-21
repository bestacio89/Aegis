# Evaluator and Rule-Engine Alignment Roadmap

## Objective

Make Aegis evaluator evidence, metric contracts, rule evaluations, and reports consistent and auditable. A rule must only evaluate when its declared metric is produced with the expected meaning, unit, scope, and completeness. Missing evidence must never be converted to zero, a pass, or a fabricated violation.

## Non-negotiable contracts

- Evaluator execution statistics describe registration, applicability, execution status, duration, and diagnostics only.
- Metric production statistics describe emitted measurements, scope, provenance, completeness, and unmatched evidence.
- Rule statistics describe registered/enabled/applicable/evaluated/passed/violated/not-applicable/not-evaluated rules and individual checks.
- A finding is emitted only from a completed rule comparison or an evaluator-specific finding contract explicitly designed to produce findings.
- Missing, unsupported, unreadable, malformed, partial, non-finite, and disabled are distinct states.
- File/entity evidence is never evaluated as project evidence, and project aggregates are never evaluated as file evidence.
- Metric aliases are permitted only when a reviewed contract proves semantic equivalence, including units, polarity, aggregation, and evidence provenance.

## Phase 0 — Establish the contract inventory

1. Enumerate every enabled and disabled architecture rule from the authoritative registry.
2. Enumerate every evaluator metric emitted in production code, including file, entity, and project-summary outputs.
3. Record for each rule: rule ID, metric key, scope, operator, threshold, units, expected polarity, applicable technologies, evidence producer, aggregation method, and current support status.
4. Add tests that fail when a rule has no declared producer, a producer has ambiguous duplicate keys, or a metric is emitted at an incompatible scope.
5. Keep genuinely unsupported rules explicitly unsupported until their evidence producer exists; do not make the inventory appear complete by inventing aliases.

**Exit criteria:** all registered rules have a documented producer or a deliberate unsupported/not-applicable policy; no unexplained contract gaps remain.

## Phase 1 — Correct rule-engine diagnostics and reporting semantics

1. Separate “no metric key exists”, “metric exists only at wrong scope”, “invalid/non-finite metric”, “invalid threshold”, “no facts for required scope”, and “evidence incomplete”.
2. Count unmatched metrics and scope mismatches independently; current unmatched-fact statistics must not hide wrong-scope metrics.
3. Ensure rule registration, applicability, execution, checks, findings, and evaluator execution counts are separate report sections.
4. Reconcile totals from detailed rule evaluations and findings. A rule can have many checks but counts as one evaluated rule.
5. Preserve explicit reasons for disabled, unsupported, not applicable, failed, and not evaluated outcomes.

**Exit criteria:** report statistics reconcile with their underlying records and a missing metric never becomes a zero-valued check.

## Phase 2 — Repair metric producers by domain

### Architecture and API
- Emit declared project-level API consistency metrics from a defined denominator and aggregation formula.
- Produce layer isolation, forbidden reference, cross-domain leakage, infrastructure inversion, and controller leakage metrics only from the corresponding dependency, domain, or controller evidence.
- Do not treat one generic dependency count as a substitute for distinct architectural properties.

### Backend quality
- Emit measurements for all analyzable files/methods, including compliant ones; findings-only output is insufficient for aggregate metric rules.
- Define and document complexity and maintainability formulas, units, and scope. Label lexical estimates as heuristics; do not claim compiler-backed cyclomatic complexity or a standard maintainability index unless implemented.
- Derive cohesion, coupling, dependency graph density, error-handling coverage, and maintainability from their own evidence sources and explicit denominators.

### Design patterns
- For each pattern rule, emit its declared metrics from that pattern's actual candidates and structural evidence.
- Keep “pattern not detected” separate from “pattern detected and compliant” and “pattern could not be assessed”.
- Do not infer a violation merely because no candidate was found.

### Dependencies
- Distinguish declaration inventory from freshness, version drift, vulnerabilities, and source usage.
- Implement registry/version comparison and language-aware usage analysis only when reliable metadata and parsing are available.
- Until then, report those capabilities as unsupported/inconclusive rather than emitting misleading zero counts.

### Frontend
- Validate framework applicability and file extensions per Angular, React, Razor, and Blazor rules.
- Emit each declared metric only from a supported analyzer; unsupported frameworks or missing toolchains must not produce fabricated scores.
- Use compiler/parser-backed evidence where feasible; otherwise expose the heuristic and confidence level.

### Infrastructure, performance, transactions, security
- Audit every rule against an actual producer and required inputs.
- Emit project summaries only when the full required population has been assessed or expose explicit coverage/completeness fields.
- Never equate no detected evidence with proof of compliance where discovery was incomplete.

**Exit criteria:** every supported rule receives valid evidence in positive, negative, boundary, empty, malformed, and partial-evidence tests.

## Phase 3 — Shared evaluator output conventions

1. Standardize metadata keys for EvaluationScope, FilePath, AnalysisComplete, AnalyzedCount, SkippedCount, UnreadableCount, and unsupported reason where applicable.
2. Standardize numeric metric names, units, value ranges, and aggregation semantics.
3. Keep file/entity facts and project summaries distinct; avoid summaries averaging only finding records.
4. Surface cancellation and read/parse failures without converting them into successful empty results.
5. Keep evaluator responsibilities separated (e.g., configuration discovery does not subsume secret scanning or package intelligence).

## Phase 4 — Verification suite

- Metric-contract inventory tests across all registered rules.
- Evaluator producer tests: safe input, violating input, near miss, empty input, unsupported technology, malformed input, unreadable/partial input, and cancellation.
- Rule-engine tests for every status, scope mismatch, ambiguous keys, non-finite values, invalid thresholds, and repeated facts.
- End-to-end deterministic fixtures validating evaluator output → rule checks → findings → aggregates → report statistics.
- Regression fixture for the HeroService scan, comparing metric coverage and findings without assuming previous counts were correct.
- Keep storage testing out of this phase; persistence has already been exercised in the user's environment and is not the current blocker.

## Phase 5 — Operational hardening

- Run the non-WPF test and production-build quality gates on every PR.
- Track rule-evaluation coverage and unmatched/scope-mismatch rates as explicit CI artifacts.
- Add coverage thresholds only after a measured baseline.
- Establish performance baselines after correctness and metric completeness stabilize.

## Delivery sequence

Use small, reviewable PRs in dependency order:
1. Metric inventory + rule-engine diagnostic separation.
2. API and architecture metrics.
3. Backend quality metrics.
4. Design-pattern metrics.
5. Dependency metrics and capability states.
6. Frontend metrics and applicability.
7. Infrastructure/performance/transaction/security metric audit.
8. End-to-end regression, report reconciliation, and CI gates.

Every PR must include tests and update this roadmap with completed items and any deliberately unsupported capabilities. Do not merge until CI is green and the HeroService report's remaining unevaluated rules are explained by explicit, accurate reasons.