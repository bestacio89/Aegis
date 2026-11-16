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
