# Aegis automated testing strategy

## Scope

The automated test suite covers all production behavior outside the WPF presentation layer. WPF-specific view and interaction tests are intentionally separate; they must not become a prerequisite for testing the headless analysis pipeline.

The test suite is organized by architectural responsibility, not by a requirement to create one test project for every production assembly.

## Test layers

| Layer | Primary responsibility | Required evidence |
| --- | --- | --- |
| Shared contracts | Models, DTOs, enum semantics, serialization compatibility | Defaults, valid/invalid states, round-trip tests |
| Architecture | Source analysis, evaluators, evidence production | Positive, negative, boundary, and regression cases |
| Rule engine | Scope, metric contracts, thresholds, aggregation | Correct evaluation state and traceable evidence |
| Security | Probes, signals, evaluators, security rules | Safe/vulnerable fixtures, severity, false-positive controls |
| Infrastructure | Persistence, report generation, integrations | Round trips, schema compatibility, failure handling |
| SDK and CLI | Public extension contracts and command behavior | Plugin contracts, argument validation, exit codes |
| Quality architecture tests | Dependency boundaries and structural constraints | Acyclic project graph, no dependency on WPF from non-UI source projects |
| Integration | Connected analysis pipeline | Fixture repository to findings, rules, and serialized report |
| Performance | Large repositories and expensive evaluators | Reproducible duration, allocation, and memory baselines |

## Required test matrix for evaluators

Each evaluator should have, where applicable:

1. A positive fixture that produces the intended evidence.
2. A negative fixture that must not produce a violation.
3. A near-miss fixture to constrain heuristic false positives.
4. Empty-input and unsupported-language behavior.
5. Malformed-input behavior and diagnostic quality.
6. Assertions over evidence scope, metric names, units, provenance, and aggregation.
7. A regression test for every confirmed defect.

Tests should assert semantic outcomes, not implementation details or incidental collection ordering.

## Rule evaluation semantics

Tests must preserve the distinction between:

- Pass: the rule was evaluated and the evidence satisfied it.
- Violation: the rule was evaluated and evidence breached it.
- NotApplicable: the rule does not apply to the scanned subject.
- Unsupported or equivalent: the engine cannot evaluate the rule with available capabilities.
- Missing or invalid evidence: evaluation is incomplete and must not silently become a pass.

Project-level metrics must be evaluated against project-level evidence, and file-level metrics against file-level evidence. Aggregates must have explicit denominators and must not double-count facts.

## CI policy

Pull-request CI must:

- Discover and run every tests/**/*.csproj test project.
- Build every non-WPF C# production project in Release configuration.
- Fail immediately when any test or build fails.
- Run on the repository's supported .NET SDK baseline.
- Keep test output visible in logs and avoid silently swallowing failures.

Coverage thresholds should be introduced after a measured baseline exists. A threshold should not be selected arbitrarily or used as a proxy for test quality. Critical rule-engine and contract paths should receive stronger branch and mutation testing than incidental getters or generated code.

## Growth plan

The initial quality suite establishes enforceable dependency rules and shared security-rule model contract tests. Subsequent work should expand in this order:

1. Rule engine scope, metric compatibility, aggregation, and unsupported-evidence semantics.
2. Evaluator behavior matrices and language-specific fixtures.
3. Security probe and evaluator tests.
4. Infrastructure report persistence and serialization integration tests.
5. SDK plugin contracts and CLI behavior.
6. End-to-end fixture repositories, performance baselines, and selective mutation testing.

A test is not considered verified merely because it has been authored. CI results must be green, and tests must assert meaningful behavior.
