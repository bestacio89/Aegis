# Polyglot server API endpoint discovery

## Phase 7 implementation boundary

The first provider slice introduces language-neutral endpoint evidence and a lossless registry, plus a Roslyn syntax provider for ASP.NET Core controller actions and literal Minimal API mappings. It remains separate from API policy scoring, client request discovery, cross-language contract matching, and graph projection.

## Evidence contract

Each observation retains declaration-derived identity, language, framework, provider, module, source file and position, handler identity, HTTP methods, route template, route-composition evidence, resolution state, and original declaration text. The registry preserves duplicate route observations; equal method/path pairs are not identity.

## C# provider coverage

Recognized constructs:
- controller-level Route prefixes;
- action-level HttpGet, HttpPost, HttpPut, HttpDelete, HttpPatch, HttpHead, and HttpOptions attributes;
- literal Minimal API MapGet, MapPost, MapPut, MapDelete, MapPatch, MapHead, and MapOptions declarations;
- one-level literal MapGroup variable prefixes.

Controller tokens such as [controller] remain partially resolved. Non-literal route expressions remain dynamic; the provider does not execute application code or infer configuration values.

## Known coverage limits

This is an initial implementation slice, not Phase 7 completion. Java Spring MVC/WebFlux, Kotlin Spring MVC/WebFlux/Ktor, TypeScript/JavaScript Express/NestJS providers, nested group composition, MapMethods, convention-based MVC routes, and semantic handler binding remain outstanding. The existing API consistency evaluator is unchanged. API client discovery and matching remain separate phases.
