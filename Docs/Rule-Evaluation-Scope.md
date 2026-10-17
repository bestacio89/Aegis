# Rule evaluation scope

Aegis rules declare the scope of evidence they are allowed to evaluate.

- **File scope** rules consume facts for an individual file or code entity.
- **Project scope** rules consume facts explicitly marked with `EvaluationScope=Project` or categorized as `ArchitectureSummary`.

The rule engine matches facts and rules only when their scopes agree. Project summaries are no longer discarded before metric matching, and project-level findings do not masquerade as source-file paths. Missing-metric diagnostics are scoped, so a metric emitted at the wrong scope does not count as evidence for a rule.

The current assignments are based on each metric's intended unit of analysis, not only on its rule category.

- **Project scope:** `AEG-ARCH-R1`–`R5`, `AEG-ARCH-R7`–`R12`; `AEG-BACK-R3`–`R4`; all `AEG-DEPS` and `AEG-INF` rules; `AEG-PERF-R1` and `R3`; `AEG-TXN-R1`.
- **File/entity scope:** `AEG-ARCH-R6`; `AEG-BACK-R1`, `R2`, and `R5`; all design-pattern and frontend rules; naming rules `AEG-NAM-R1`–`R5`; `AEG-PERF-R2` and `R4`; `AEG-TXN-R2`–`R4`; `AEG-NAM-R6`.

When adding a rule, choose its scope based on the evaluator that produces the metric and the metric's semantics—not merely on the rule's category. Scope is mandatory: an unspecified scope causes registry validation to fail. A scope change should include a producer/consumer review and regression coverage. Scope matching does not substitute for implementing a missing metric producer or validating its unit, aggregation, and threshold.
