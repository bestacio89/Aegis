# Polyglot integration evidence

## Purpose

Phase 10 introduces source-level evidence for messaging, RPC, and data boundaries. These observations are distinct from build dependencies, native symbol relationships, and HTTP/API evidence.

The shared taxonomy already defines the normalized relationship families:
- `MessagePublish` and `MessageConsume`
- `RpcCall` and `RpcEndpoint`
- `DataRead`, `DataWrite`, and `DataMapping`

A taxonomy value classifies an observation. It does not establish a relationship between two modules or create a graph edge.

## Shared evidence contract

Each domain evidence type implements `IIntegrationEvidence`, preserving:
- provider-supplied evidence ID without assuming global uniqueness;
- language, framework, and provider identity;
- owning module path and source file/position;
- normalized relationship kind;
- resolution status and resolution evidence;
- original declaration evidence.

`IntegrationEvidenceRegistry<T>` is an immutable, occurrence-preserving snapshot. It indexes by resolution status, module path, and evidence ID while retaining every occurrence in input order. Duplicate IDs are expected to remain distinct observations; callers must use snapshot occurrence identity when a stable reference is needed.

## Domain contracts

### Messaging

`MessageIntegrationEvidence` distinguishes `Publish` from `Consume` and retains message type identity/expression separately from destination kind, destination identity/expression, and handler identity. Topic, queue, exchange, and channel are distinct destination categories. Unknown destination semantics remain explicit.

A matching message type or broker package is not proof that a publisher reaches a consumer. Publish/consume correlation is a later derived operation and must retain fan-out and ambiguity.

### RPC

`RpcIntegrationEvidence` distinguishes a client `Call` from an `Endpoint` declaration. Protocol, service identity, method identity, target identity/expression, and handler identity remain separate. A service/method name without defensible protocol and ownership evidence is not a confirmed call-to-endpoint relationship.

### Data access

`DataAccessEvidence` distinguishes `Read`, `Write`, and `Mapping`. Technology, store identity, data-object identity/expression, source/target mapping identities, and query expression are retained independently. A database package or shared database name does not prove that two modules read or write the same data.

## Resolution semantics

- `Resolved`: the operation and the relevant identity fields are statically established.
- `PartiallyResolved`: some identity is established, but one or more destination/contract/object dimensions remain incomplete.
- `Dynamic`: the source operation is recognized, but a relevant target or identity is computed dynamically.
- `Unresolved`: evidence is insufficient to establish the operation's identity.
- `Unsupported`: the provider recognizes the construct but does not support its semantics.

These statuses describe source-analysis evidence only. They do not represent runtime success, message delivery, RPC reachability, transaction outcome, or database execution.

## Boundaries

- Build/package dependency presence remains technology metadata, not a message/RPC/data relationship.
- Discovery does not execute application code or evaluate runtime configuration.
- Provider-native declarations and source positions remain available; normalization does not discard evidence.
- Messaging, RPC, and data discovery are separate from cross-module matching and graph projection.
- No graph edges are created by these contracts or registries.


## Phase 10B — C# MassTransit discovery

The C# provider uses Roslyn syntax and semantic information already available in Aegis. It recognizes `IPublishEndpoint.Publish` and `ConsumeContext.Publish` calls, `ISendEndpoint.Send` calls, and `IConsumer<T>` declarations only when their symbols are grounded in the MassTransit namespace/type identity.

Message contract identity is resolved from the generic argument or statically typed message expression when the source compilation can establish it. Otherwise, the original expression is retained with partial/unresolved status. Publish remains type-routed and Send remains endpoint-oriented; neither operation is treated as proof of successful delivery or a runtime route.

The provider inspects MassTransit `ReceiveEndpoint` registrations and associates a statically grounded `ConfigureConsumer<T>` registration with the matching `IConsumer<T>` declaration. A literal endpoint name is retained as `DestinationIdentity`; a non-literal endpoint expression is retained as `DestinationExpression` without evaluating it. The destination kind remains `Unknown` because MassTransit receive endpoints are transport-neutral at this boundary; the provider does not guess whether the underlying broker entity is a queue, topic, or exchange. Multiple endpoint registrations remain separate evidence occurrences.

The provider runs only against C# source files owned by discovered C# modules. It does not infer message relationships from package references or arbitrary methods named `Publish` or `Send`. Broker topology, runtime registration beyond the statically grounded consumer association, and delivery remain outside this provider slice.

## Java — Spring Kafka and Spring JMS

The Java semantic worker uses Eclipse JDT bindings to recognize exact framework declarations rather than method names alone:

- Spring Kafka consumers through `org.springframework.kafka.annotation.KafkaListener`.
- Spring Kafka producers through `org.springframework.kafka.core.KafkaTemplate.send`.
- Spring JMS consumers through `org.springframework.jms.annotation.JmsListener`.
- Spring JMS producers through `org.springframework.jms.core.JmsTemplate.convertAndSend`.

Listener destinations, producer destinations, message expressions/types, handler or caller identity, source positions, and declaration evidence are retained. Literal destinations can be resolved; placeholders and dynamic expressions remain unresolved/dynamic evidence. Overload argument mapping is based on resolved method bindings and parameter identities. Unrelated annotations with the same simple name and unrelated methods with the same method name are not accepted as framework evidence.

The Java provider does not infer queue/topic semantics from Spring JMS configuration or Kafka API usage, evaluate runtime placeholders, inspect broker topology, correlate publishers with consumers, or claim message delivery.

## Kotlin — Spring Kafka and Spring JMS

The Kotlin semantic worker uses the existing K2 IR pipeline and a dedicated `KotlinMessageIntegrationCollector`. It recognizes the same exact Spring Kafka and Spring JMS annotation and API declarations as the Java provider:

- `@KafkaListener` and `KafkaTemplate.send` / `sendDefault`.
- `@JmsListener` and `JmsTemplate.convertAndSend`.

The collector preserves source location, handler/caller identity, message type and expression, destination identity and expression, provider/framework provenance, resolution status, and declaration evidence. It handles listener topic arrays and topic patterns, producer overloads using IR parameter identities, and JMS message post-processor overloads. Literal destinations are retained; placeholders, patterns, computed expressions, and APIs without an explicit destination (such as `sendDefault`) do not receive fabricated destination identities.

Kotlin observations are adapted into the existing `MessageIntegrationEvidence` contract and appended to the existing occurrence-preserving `IntegrationEvidenceRegistry<MessageIntegrationEvidence>` alongside C# and Java evidence. No parallel Kotlin-specific registry or aggregation path is introduced.

The Java and Kotlin providers retain destination kind as `Unknown` where the source declaration does not establish queue/topic semantics. Neither provider evaluates runtime configuration, inspects broker topology, correlates publishers and consumers, creates graph edges, or claims successful delivery.


## Phase 10D — C# Entity Framework Core data access

The C# data-access provider is technology-specific by design. It uses Roslyn semantic bindings to discover EF Core query execution, tracked and bulk mutations, relational entity-to-table/view mappings, schema-qualified store identities, and strongly typed property-to-column mappings. Dynamic schema and column expressions retain partial identity and are not promoted to resolved mappings.

This implementation does not imply EF Core support in Java, Kotlin, TypeScript, or JavaScript. Those ecosystems require independent providers for their own persistence technologies. The shared `DataAccessEvidence` contract remains technology-neutral; `Technology`, store/object identities, mapping endpoints, and source expressions preserve provider-specific facts without importing EF Core types into shared contracts.

## Phase 10E — Cross-domain integration normalization and regression corpus

`IntegrationEvidenceCatalog` provides one immutable cross-domain index over the existing typed messaging, RPC, and data-access registries. Each catalog entry carries a domain discriminator and a snapshot-local occurrence index while referencing the original `IIntegrationEvidence` instance. It does not copy domain evidence into a parallel contract, collapse provider IDs, match endpoints, or infer relationships.

The catalog supports indexes by language, owning module, provider, normalized relationship kind, and resolution status. Original typed registries remain authoritative, and the catalog is a derived, non-serialized property on `ProjectArchitectureContext`.

The shared regression corpus exercises heterogeneous provider metadata across the existing contract families (Java/Kotlin messaging and RPC observations alongside C# EF Core data observations), duplicate IDs across and within domains, repeated occurrences, dynamic and partially resolved evidence, case-insensitive language/module lookups, provider-specific lookup, relationship classification, empty domain snapshots, and preservation of original evidence object identity. This validates the common evidence boundary; it does not claim that persistence providers exist for languages where none have been implemented.

**Validation boundary:** language-specific discovery remains the responsibility of each native provider and its own compiler/semantic worker. Cross-domain catalog construction is aggregation only; it performs no cross-language symbol resolution, message/RPC/data correlation, runtime reachability analysis, or graph-edge creation.
