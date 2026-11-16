# Polyglot server API endpoint discovery

## Phase 7 implementation boundary

The first provider slice introduces language-neutral endpoint evidence and a lossless registry, plus a Roslyn syntax provider for ASP.NET Core controller actions and Minimal API mappings. It remains separate from API policy scoring, client request discovery, cross-language contract matching, and graph projection.

## Evidence contract

Each observation retains declaration-derived identity, language, framework, provider, module, source file and position, handler identity, HTTP methods, route template, route-composition evidence, resolution state, and original declaration text. The registry preserves duplicate route observations; equal method/path pairs are not identity.

## C# provider coverage

Recognized constructs:
- controller-level Route prefixes;
- action-level HttpGet, HttpPost, HttpPut, HttpDelete, HttpPatch, HttpHead, and HttpOptions attributes;
- literal Minimal API MapGet, MapPost, MapPut, MapDelete, MapPatch, MapHead, and MapOptions declarations;
- MapMethods when its HTTP method collection is a literal collection expression or array initializer;
- nested MapGroup prefixes, including chained group expressions;
- MVC MapControllerRoute and MapAreaRoute declarations with positional or named literal patterns;
- MapDefaultControllerRoute with its framework-defined default pattern.

Controller tokens such as [controller] remain partially resolved. Non-literal route expressions and non-literal MapMethods collections remain explicitly unresolved/partially resolved; the provider does not execute application code or infer configuration values.

## Java Spring MVC/WebFlux provider

The Eclipse JDT worker discovers `@RequestMapping`, `@GetMapping`, `@PostMapping`, `@PutMapping`, `@DeleteMapping`, and `@PatchMapping` declarations through resolved annotation bindings. Class-level and method-level literal route values are composed, HTTP methods are retained, and unresolved route expressions and Spring property/SpEL placeholders remain dynamic observations; Java compile-time string constants are resolved through JDT constant-expression bindings. Evidence is returned through the semantic run and merged into the shared server endpoint registry. The provider does not execute Spring configuration, resolve external property placeholders, infer composed custom annotations, or prove handler invocation semantics.

## Known coverage limits

This remains an incremental Phase 7 implementation, not Phase 7 completion. Kotlin Spring MVC/WebFlux/Ktor, TypeScript/JavaScript Express/NestJS providers, semantic handler binding, and additional framework-specific route conventions remain outstanding. The existing API consistency evaluator is unchanged. API client discovery and matching remain separate phases.


### Kotlin provider slice

The Kotlin K2 IR worker now emits Spring MVC/WebFlux server endpoint observations from compiler-resolved mapping annotations. Class-level and method-level route values are composed; HTTP methods from method mappings are retained; compile-time string constants are resolved by the compiler; Spring property/SpEL placeholders remain dynamic. Each observation retains module ownership, source location, handler identity, route-composition evidence, and declaration text. The Kotlin host client maps the worker output into the shared `SemanticAnalysisRun.ServerApiEndpoints` contract, which the existing pipeline merges into the shared registry.

Ktor routing DSL discovery, composed/custom Spring annotations, inherited mapping semantics, and Kotlin convention-based routes remain outside this slice and must not be treated as covered. Client discovery and contract matching remain Phases 8–9.
