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

The Ktor routing DSL is now covered for the `routing` container, nested literal `route` groups, and common HTTP route functions (`get`, `post`, `put`, `delete`, `patch`, `head`, `options`). Dynamic string expressions remain dynamic evidence; non-string route selectors remain partially resolved and are not converted into invented path templates. Composed/custom Spring annotations, inherited mapping semantics, and Kotlin convention-based routes remain outside this slice. Client discovery and contract matching remain Phases 8–9.


Ktor discovery is performed from K2 IR calls in the exact `io.ktor.server.routing` namespace. Nested `route` calls contribute route-prefix evidence while the visitor traverses their builder lambdas; endpoint calls retain their enclosing source handler, call-site position, module ownership, and declaration snippet. Non-literal String expressions are retained as dynamic evidence, while selector overloads that are not statically interpreted retain only the known prefix and a partially-resolved status.


### TypeScript and JavaScript provider slice

The TypeScript compiler API parser now discovers server-side endpoints in TypeScript, TSX, JavaScript, JSX, MJS, and CJS files owned by TypeScript/JavaScript modules:

- Express: statically grounded app/router instances, direct HTTP verb methods, and chained `.route(path).get/post/...()` declarations.
- NestJS: `@Controller` plus imported `@nestjs/common` HTTP method decorators.
- Literal routes are resolved; template expressions are partially resolved; non-literal route expressions remain dynamic with their source expression preserved.
- Evidence retains source file, declaration location, handler identity, methods, module ownership, provider identity, and declaration text.
- Unrelated `.get()` calls are not treated as Express endpoints unless the receiver is grounded as an Express application/router.
- TypeScript parser diagnostics are retained as coverage-affecting discovery diagnostics.

This is syntax-backed discovery, not TypeScript type checking or runtime route evaluation. Namespace-import Nest decorators, custom decorator metadata, Express router mount-prefix composition, aliases/data-flow, and framework-specific runtime conventions remain outside this slice. Client API calls and contract matching remain later phases.
