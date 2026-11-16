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
- nested MapGroup prefixes, including chained group expressions.

Controller tokens such as [controller] remain partially resolved. Non-literal route expressions and non-literal MapMethods collections remain explicitly unresolved/partially resolved; the provider does not execute application code or infer configuration values.

## Known coverage limits

This remains an incremental Phase 7 implementation, not Phase 7 completion. Java Spring MVC/WebFlux, Kotlin Spring MVC/WebFlux/Ktor, TypeScript/JavaScript Express/NestJS providers, convention-based MVC routes, semantic handler binding, and framework-specific route conventions remain outstanding. The existing API consistency evaluator is unchanged. API client discovery and matching remain separate phases.
