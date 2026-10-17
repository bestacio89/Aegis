# Rule evaluation scope

Aegis rules declare the scope of evidence they are allowed to evaluate.

- **File scope** rules consume facts for an individual file or code entity.
- **Project scope** rules consume facts explicitly marked with `EvaluationScope=Project` or categorized as `ArchitectureSummary`.

The rule engine matches facts and rules only when their scopes agree. Project summaries are no longer discarded before metric matching, and project-level findings do not masquerade as file paths. Missing-metric diagnostics are scoped, so a metric emitted at the wrong scope does not count as evidence for a rule.

The scope assignments follow the current metric contract:
- Project scope: architecture, backend, dependency, infrastructure, performance, and transaction rules.
- File scope: design-pattern, frontend, and naming rules.

When adding a rule, choose its scope based on the evaluator that produces the metric and the metric's semantics—not merely the rule's category. A scope change should include a producer/consumer review and regression coverage.
