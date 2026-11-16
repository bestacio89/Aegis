# Polyglot API Client Request Discovery

## Purpose

Phase 8 records source-level outbound HTTP request declarations independently from server endpoint declarations. A discovered request is not proof that the call executes, is reachable at runtime, or reaches a particular server endpoint. Endpoint matching belongs to Phase 9; graph projection remains out of scope.

## Shared evidence

Every provider emits `ApiClientRequestEvidence` with:

- language, framework, provider and build-module ownership;
- source file, source position and caller identity;
- HTTP method when statically established;
- the original request URI expression and optional base-address expression;
- resolution status and the evidence supporting that status;
- the original declaration text.

Duplicate observations and dynamic expressions are retained. An unknown method or target is not replaced with a guessed value. Provider identity and provenance remain visible to downstream matching.

## Provider coverage

| Language | Provider | Current discovery scope |
|---|---|---|
| C# | Roslyn semantic symbols | `System.Net.Http.HttpClient` methods and `HttpClientJsonExtensions`, including statically correlated `HttpRequestMessage` use with `SendAsync` |
| Java | Eclipse JDT method bindings | Spring `RestTemplate` and Spring `WebClient` request declarations |
| Kotlin | Kotlin K2 IR | Ktor client request functions, Spring `RestTemplate`, and Spring `WebClient` URI builder calls |
| TypeScript / JavaScript | TypeScript compiler API AST | Global or explicitly imported Fetch API calls, Axios imports/instances/configuration, Angular `HttpClient`, and NestJS `HttpService` |

The Kotlin provider uses compiler-resolved IR function identities and retains source-level request expressions. The TypeScript/JavaScript provider uses grounded import, factory, or type-annotation evidence. An arbitrary object's `.get()` method is not treated as an HTTP request. JavaScript and TypeScript are parsed with their respective TypeScript compiler script kinds; this is syntax-level evidence, not a claim of full TypeScript type checking.

## Resolution rules

- String literals are `Resolved`.
- Template expressions with interpolation are `PartiallyResolved`; their source expression is preserved.
- Other computed URI expressions are `Dynamic`.
- A dynamic HTTP method remains unknown.
- A configured Axios `baseURL` is retained as a source expression; it is not evaluated or concatenated with the request URI.
- Calls with insufficient request URI evidence are not silently converted into concrete requests.

## Boundaries

This phase does not:

- match client requests to server routes;
- infer runtime execution, reachability, authentication, or successful delivery;
- create dependency graph edges;
- infer API equivalence from DTO shape, route text, or matching method names;
- interpret a build dependency as a runtime request.

Phase 9 will correlate client request evidence with server endpoint evidence while preserving unresolved and ambiguous matches.
