# Decorator evidence and naming compliance

The decorator evaluator uses lexical and structural heuristics to identify possible decorator candidates. A candidate is not, by itself, proof that a class intends to implement the Decorator pattern.

## Separate evidence dimensions

- **Candidate detection** records a class whose name or structure suggests a decorator.
- **Structural support** records evidence such as an abstraction reference, wrapped component, constructor injection, and delegation calls.
- **Naming compliance** applies only when the class name explicitly signals decorator intent with the `Decorator` suffix.

A structurally inferred wrapper without that explicit naming signal is not counted as a naming violation. Its `DecoratorNamingCompliance` metric is omitted and `NamingConventionCompliant` is marked `NotApplicable`. The structural compliance score is not reduced by naming convention status.

At project scope, `DecoratorNamingApplicableCount` is the denominator for naming compliance. The aggregate `DecoratorNamingCompliance` metric is omitted when no applicable naming evidence exists; it is not fabricated as 100% or 0%.

The evaluator still marks its structural analysis as heuristic and does not claim semantic compilation. Findings should retain that provenance so consumers can distinguish static evidence from verified semantic analysis.
