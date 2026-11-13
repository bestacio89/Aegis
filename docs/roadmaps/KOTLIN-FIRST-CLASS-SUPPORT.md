# Aegis — Kotlin First-Class Language Support

## Execution contract

**Objective:** Deliver production-grade Kotlin semantic analysis in Aegis, using the same evidence discipline and phase governance established for Java.

**Execution sequence:** K0 analysis and engine proof → K1 build/source ownership → K2 semantic worker → K3 JVM interoperability and dependency projection → K4 framework intelligence → K5 test architecture → K6 representative validation and production readiness.

**Delivery model:** One umbrella pull request against develop, implemented in small responsibility-based commits. The PR remains open until mandatory acceptance gates pass. A phase is not complete because its code has merely been committed.

**Initial scope:** Kotlin/JVM projects using Maven and Gradle, including mixed Java/Kotlin modules. Kotlin Multiplatform, Kotlin/Native, Kotlin/JS and Kotlin/Wasm are outside the first production checkpoint. Android/JVM source analysis is a candidate extension; it must not be claimed complete until Android source-set and SDK dependency handling are validated.

---

## 1. Baseline analysis — 2026-11-13

### Existing capabilities

The merged develop baseline already contains:

- Kotlin source recognition for .kt and non-Gradle .kts files.
- Kotlin-aware JVM build-system detection for Maven and Gradle.
- Kotlin project classification, including mixed Java/Kotlin module classification.
- Static Gradle parsing that understands both Groovy and Kotlin DSL syntax.
- Shared Maven/Gradle project models, dependency evidence, project modules and semantic contracts.
- A language-neutral ISemanticAnalysisAdapter boundary, SemanticAnalysisCoordinator, and SemanticAnalysisPipeline.
- An out-of-process, supervised Java 21 worker pattern with a versioned JSON-line protocol, request limits, cancellation and private runtime packaging.

### Confirmed gaps

1. No Kotlin semantic adapter or project orchestrator exists. The registered semantic project analyzer is Java-specific.
2. The Java worker only analyzes .java files. It cannot parse Kotlin source or resolve Kotlin language constructs.
3. The shared source-root contract is Java-shaped. ProjectSourceRoot exposes ContainsJavaSources; it cannot accurately describe Kotlin-only or mixed-language roots.
4. Gradle source-set discovery is Java-shaped. The current Gradle model extracts JavaSourceDirectories; it does not model Kotlin source directories as a first-class source-root collection.
5. Maven Kotlin configuration is not represented as a Kotlin compiler model. Kotlin plugin version, source roots, language/API version, JVM target and Kotlin-specific build metadata need explicit discovery.
6. No Kotlin semantic fixtures or worker tests exist. The existing Kotlin Gradle fixture contains Java application source and validates Kotlin DSL parsing, not Kotlin language semantics.
7. No Kotlin-specific framework or test evidence exists. Existing Java evidence cannot be assumed to describe Kotlin module semantics.

### Existing architecture to preserve

- Shared semantic contracts remain language-neutral.
- Parser/compiler types never escape the Kotlin adapter boundary.
- Build declarations, resolved classpath entries and observed semantic relationships remain separate evidence.
- Production and test analysis remain independent.
- Unresolved and recovered compiler references are never reported as resolved.
- Build scripts and arbitrary project plugins are not executed during ordinary scans.
- Existing Java and non-JVM analysis must not regress.
- Semantic evidence retains stable identities, source locations and run-scoped provenance.

---

## 2. Kotlin language semantics to represent

Kotlin is not Java syntax with different keywords. The implementation must preserve language-level constructs instead of flattening them into misleading Java-shaped symbols.

### Declaration model

- Classes, interfaces, objects, companion objects, data classes, value classes, sealed classes/interfaces, enums and annotation classes.
- Primary and secondary constructors.
- Member and top-level functions.
- Extension functions and extension properties.
- Properties, accessors, delegated properties and backing fields.
- Type aliases and generic/type-parameter declarations where relevant.
- File-level declarations and JVM file-facade identity.
- Visibility distinctions: public, internal, protected and private.
- Generated declarations versus declarations explicitly present in source.

### Relationship model

- Inheritance and interface implementation.
- Resolved function and constructor calls.
- Property reads/writes and references.
- Annotation usage.
- Extension-function resolution, including the resolved receiver and callable.
- Delegated-property/operator relationships when compiler evidence exposes them.
- Java/Kotlin interoperability in both directions.
- Unresolved references, compiler errors and incomplete dependency evidence.

### Semantic correctness constraints

- A property access must not be duplicated as an unrelated Java-style method call merely because its JVM lowering uses accessors.
- Top-level functions must not be assigned fabricated enclosing classes; their package/file-facade identity must be explicit.
- Extension calls must target the resolved extension declaration, not only the receiver type.
- Companion objects and object declarations retain their declaration identity.
- A Kotlin internal declaration must not be silently treated as public or private.
- Compiler-generated bridges, accessors and synthetic declarations must not inflate source declaration counts.
- Stable IDs must not depend on source line numbers or traversal order.
- Error/recovered compiler symbols must not become resolved graph edges.

---

## 3. Semantic engine assessment and decision gate

The engine must provide compiler-backed declaration and call resolution, source locations, diagnostics, Java interop, and useful recovery for incomplete projects.

### Candidate assessment

| Candidate | Strength | Limitation | Decision |
|---|---|---|---|
| Kotlin Analysis API Standalone | High-level Kotlin semantic API for symbols, types, references and calls; Java declarations can be represented through Kotlin analysis | Standalone mode is explicitly in active development and subject to incompatible changes; Java/Kotlin bridging APIs are also marked experimental | Keep as a candidate; do not assume it is production-stable |
| Kotlin Symbol Processing (KSP) | Purpose-built symbol model for Kotlin declarations and types | Cannot inspect expressions or statements, so it cannot provide the required call/dependency graph | Reject as the primary semantic engine |
| K2 compiler with an isolated IR/FIR extraction extension | Uses the actual compiler frontend and resolved program representation; supports a version-pinned worker and can emit compiler diagnostics | Compiler plugin APIs are unstable and require deliberate version-specific maintenance | Preferred technical-spike candidate |
| Kotlin Language Server / IntelliJ platform | Rich semantic services and Java/Kotlin interoperability | Much larger IDE-oriented runtime and integration surface; LSP does not directly provide the complete normalized Aegis evidence model | Fallback only if the compiler spike cannot meet correctness requirements |

### Provisional engine direction

Prototype a dedicated Kotlin/JVM worker using the released Kotlin compiler **2.4.21** and K2. Prefer a pre-lowering IR extraction point if the spike demonstrates that declarations, resolved calls, property accesses, source offsets and diagnostics can be collected without executing project build logic.

The compiler plugin API is explicitly unstable. Therefore:

- Pin the compiler and worker dependency graph exactly.
- Keep compiler APIs inside the worker.
- Keep the worker protocol versioned and independent of compiler object models.
- Do not load arbitrary compiler plugins, KSP processors, annotation processors or Gradle plugins from the scanned repository.
- Test the supported Kotlin compiler/language-version matrix explicitly.
- Upgrade the compiler only through a dedicated compatibility change with fixture and representative-project evidence.

**K0 is not accepted until the prototype proves** source declaration extraction, overloaded call resolution, inheritance, property access, Java source interop, compiler diagnostics, deterministic IDs and safe process operation. If IR cannot preserve a required source-level fact, evaluate FIR/Analysis API before committing to the production extraction boundary.

### Why KSP is insufficient

KSP intentionally does not expose expressions or statements. It can support declaration-oriented framework discovery, but cannot be the source of truth for method/function invocation relationships. It may be considered later as optional generated-symbol evidence, never as a replacement for compiler semantics.

### Kotlin version policy

The current stable Kotlin release is 2.4.21. K2 is the default frontend from Kotlin 2.0.0. The compiler's language-version compatibility changes across release lines; Kotlin 2.4 no longer permits rollback to language version 1.9. The worker must identify the project's declared Kotlin compiler/language version and either select a validated compatible engine or emit an explicit unsupported/partial diagnostic. It must never silently claim equivalent semantics for an incompatible version.

---

## 4. Target architecture

### Worker boundary

Create a dedicated Kotlin worker rather than adding Kotlin compiler dependencies to the JDT worker.

- JVM 21 runtime, isolated worker classpath and private packaging.
- Versioned JSON-line protocol with request IDs and strict request/response size limits.
- One request at a time per worker process unless concurrency is explicitly designed and tested.
- Startup and request timeouts, cancellation, process-tree termination and bounded diagnostics.
- No execution of Maven/Gradle scripts or application code.
- No project-supplied compiler plugins or annotation processors.
- Temporary compiler outputs, if required for Java interop, must be created under a worker-owned temporary directory and cleaned up deterministically.
- Compiler output must not be written into the scanned repository.

### .NET integration

Add Kotlin-specific implementations behind the existing shared boundaries:

- IKotlinWorkerClient and Kotlin worker options.
- KotlinSemanticAnalysisAdapter : ISemanticAnalysisAdapter.
- KotlinSemanticAnalysisOrchestrator : ISemanticAnalysisProjectAnalyzer.
- A Kotlin classpath resolver using explicit module/build evidence and local artifact caches only.
- Dependency-injection registration integrated with the existing semantic pipeline.

Do not fork the shared coordinator, semantic run contract, relationship projection, architecture graph or ledger.

### Shared source-root model

Generalize source-root evidence so it records the languages actually present in a root, while retaining a compatibility path for existing Java consumers during migration.

The model must represent:

- Production, test, test-support and unknown ownership.
- Explicit, conventional and observed origin.
- Generated versus handwritten roots.
- One or more languages per root.
- The manifest/build-file evidence that established the root.

Refactor Java source-root selection to consume the generalized contract only where required for mixed JVM modules. Preserve existing Java behavior with regression tests.

### Module-scoped analysis

- Analyze each discovered Kotlin module independently.
- Respect nested module ownership; aggregators must not recursively absorb child module sources.
- Split production and test scopes when ownership is known.
- Preserve the established combined-analysis fallback when ownership is genuinely unknown, with an explicit diagnostic.
- Include only explicitly declared in-workspace project source roots as binding inputs.
- Reject source roots outside the workspace boundary.
- Keep generated sources scoped to their owning module/source set.

### Build metadata

Gradle:
- Recognize Kotlin JVM plugin IDs and plugin aliases from statically readable declarations/version catalogs.
- Preserve plugin version and report unresolved aliases or dynamic plugin declarations.
- Model Kotlin source directories for main, test, custom source sets and generated sources.
- Parse only a bounded literal subset of Kotlin/Groovy DSL; do not execute scripts.
- Preserve dependency configuration and production/test scope.
- Recognize Android and Multiplatform plugins as separate project kinds, not as proof of JVM-only support.

Maven:
- Parse Kotlin Maven plugin coordinates/version and bounded literal configuration.
- Model Kotlin main/test source roots and configured source directories.
- Preserve compiler language/API version, JVM target and plugin options when statically available.
- Preserve inherited properties and unresolved interpolation diagnostics.
- Keep dependency declarations separate from resolved classpath entries.

Classpath:
- Reuse the existing JVM dependency model where its semantics are valid.
- Do not treat declared coordinates as resolved artifacts.
- Local-cache lookup remains explicitly incomplete when transitive closure is not known.
- Kotlin standard library/compiler built-ins must be available to the worker without falsely claiming that the project's complete external classpath was resolved.

---

## 5. Java/Kotlin interoperability

Mixed JVM modules are a required design concern, not a later cosmetic enhancement.

### Kotlin references to Java

The Kotlin compiler must receive the module's owned Java source roots as Java source inputs for semantic resolution, plus only the explicitly resolved classpath entries. Java source files are binding inputs; they must not be counted as Kotlin analyzed files.

### Java references to Kotlin

The preferred path is to produce Kotlin JVM class outputs in a worker-owned temporary directory, then pass those outputs as explicit classpath inputs to JDT for the Java source analysis. This must be proven in K0/K3 with:

- Kotlin classes and interfaces.
- Top-level functions and file facades.
- Properties and JVM accessors.
- Companion objects and @JvmStatic.
- Overloads, generics and visibility.
- Java calls into Kotlin and Kotlin calls into Java.

This is compiler analysis/temporary compilation only. It must not execute application code or project build scripts. If compilation cannot produce usable outputs because of unresolved dependencies or missing generated declarations, preserve partial status and diagnostics instead of inventing resolved edges.

The two language runs must share a consistent module identity and provenance scheme. Cross-language edges must not be double-counted as separate relationships merely because both compilers observe the same call.

---

## 6. Framework and test intelligence

### Framework evidence

Framework evidence must be derived from module-owned build coordinates and compiler-confirmed source usage, not from package names or annotations alone.

Initial candidates for the Kotlin catalogue:
- Spring Framework / Spring Boot.
- Ktor.
- Micronaut and Quarkus Kotlin integrations.
- kotlinx.coroutines.
- kotlinx.serialization.
- Exposed and common persistence integrations.
- AndroidX / Jetpack Compose, gated by Android support.
- gRPC and Protocol Buffers Kotlin integrations.
- Messaging integrations such as Kafka and RabbitMQ.

Use the existing normalized FrameworkEvidence contracts and provenance. Keep Kotlin-specific framework rules out of shared evaluators.

### Test architecture

- JUnit 4/5 and JUnit Platform.
- Kotest and Kotlin test.
- MockK and Mockito.
- AssertJ.
- Testcontainers.
- Gradle test source sets and Maven Surefire/Failsafe.
- Test fixtures and test-support modules.

Production and test source roots, classpaths, symbols and relationships must remain separate. Test-to-production relationships are inspectable but excluded from production coupling and cycle metrics.

---

## 7. Phases and acceptance checkpoints

### K0 — Baseline and compiler-engine proof

- Complete repository audit and document confirmed current behavior.
- Build an isolated compiler prototype and validate the candidate extraction boundary.
- Prove deterministic symbol identity, source provenance, calls, properties, inheritance, diagnostics, Java interop and safe process lifecycle.
- Record compiler version, licensing/SBOM, memory and performance baseline.
- Decide the supported Kotlin compiler/language-version matrix.

**Acceptance:** The prototype emits correct normalized evidence for a focused semantic corpus; limitations are explicit; the production engine decision is documented. No production integration before this checkpoint.

### K1 — Build and source ownership

- Generalize source-root contracts without regressing Java.
- Add Kotlin Gradle source-set and Maven plugin/source-root discovery.
- Capture Kotlin compiler/plugin version, JVM target, language/API version and declared dependencies.
- Preserve module ownership, production/test separation, generated roots and dynamic-configuration diagnostics.
- Add Gradle Kotlin DSL and Groovy DSL, Maven, multi-module and mixed Java/Kotlin fixtures.

**Acceptance:** Kotlin modules and roots are correctly discovered; no build scripts execute; unresolved build metadata is visible; Java regression suite remains green.

### K2 — Kotlin semantic worker and adapter

- Implement the versioned worker and compiler extraction.
- Emit normalized symbols, relationships, diagnostics and provenance.
- Cover classes, interfaces, objects, companion objects, data/value/sealed classes, enums, annotations, constructors, member/top-level functions, properties, extension declarations and type aliases.
- Cover calls, constructor calls, inheritance, implementation, property access and annotation usage.
- Preserve generated/source distinctions and compiler recovery status.
- Integrate through the existing coordinator and semantic pipeline.

**Acceptance:** Semantic fixture corpus passes; recovered/unresolved references are never marked resolved; worker lifecycle and resource limits pass on Windows and Linux.

### K3 — JVM interoperability and graph projection

- Resolve Kotlin-to-Java source and binary references.
- Provide temporary Kotlin class outputs to JDT for Java-to-Kotlin resolution.
- Verify canonical symbol identities across both providers.
- Prevent duplicate cross-language edges and retain exact provenance.
- Preserve module, production/test and logical-architecture projection behavior.

**Acceptance:** Mixed-language fixtures demonstrate bidirectional type/call resolution and accurate dependency graphs. Incomplete classpaths or generated-code gaps remain explicitly partial.

### K4 — Kotlin framework intelligence

- Add module-owned dependency catalogue and role mapping.
- Separate declared dependency evidence from compiler-confirmed source usage.
- Preserve versions, source module, scope and provenance.
- Add focused regression tests for positive, negative and test-only cases.

**Acceptance:** Supported framework evidence is accurate and contextual; no annotation-only architecture inference is introduced.

### K5 — Kotlin test architecture

- Discover test projects, source sets, test-support modules and test frameworks.
- Analyze test sources with test-aware classpaths and production source inputs.
- Preserve test-to-production relationships as test evidence.
- Keep test evidence out of production metrics.

**Acceptance:** Kotlin production/test fixtures prove isolation and traceability.

### K6 — Representative validation and production readiness

Use representative repositories/corpora for:
1. Conventional Kotlin/JVM Gradle application.
2. Kotlin Maven application.
3. Multi-module Gradle Kotlin application.
4. Mixed Java/Kotlin module with calls in both directions.
5. Spring Boot Kotlin application.
6. Ktor application.
7. Coroutines and extension-heavy code.
8. Data/value/sealed classes, delegated properties and companion objects.
9. Production/test/test-support separation.
10. Incomplete dependency metadata and generated-code cases.

Validate discovery, source ownership, semantic resolution and diagnostics, Java interoperability, framework evidence, architecture graphs/ledger/reports, test separation, performance, memory, worker reliability and existing Java/non-JVM regression gates.

**Final acceptance:**
- All mandatory fixture and representative-project tests pass.
- All required CI workflows pass on the exact PR head.
- Supported compiler versions and known limitations are documented.
- No critical unresolved correctness defects remain.
- Kotlin is marked first-class only for the platforms/toolchains actually validated.

---

## 8. Checkpoint governance and commit discipline

Every phase must have written scope and implementation tasks, automated tests and explicit acceptance criteria, exact-head CI evidence, known limitations and outstanding risks, and a decision to proceed, remediate or revise scope.

Commit format:

[Action][Scope]: concise lowercase summary

Every commit body must explain context, implementation, architectural rationale, impact/compatibility implications, and validation/evidence.

Keep commits grouped by responsibility, normally 3–20 files, and avoid unrelated changes. Do not weaken existing gates, suppress failures, execute project build scripts, add unbounded dependencies, or claim support based on parser recognition alone.

**Final principle:** Aegis reports what the compiler proves, distinguishes inferred evidence, and exposes what it cannot determine.

## Cross-language identity constraint

The K2 worker's current symbol IDs are provider-local. For example, a Java type discovered as an external Kotlin IR stub currently receives a Kotlin-worker ID such as `kotlin:type:interop.JavaContract`. The Java worker uses JDT binding keys. Matching the displayed qualified name or the `language` field is not sufficient to prove that two provider records represent the same declaration.

K3 must introduce a normalized JVM interop identity and a collision-aware binder. The identity must account for module/artifact scope, owner binary name, JVM member name, erased JVM descriptor, constructor/static semantics and relevant Kotlin lowering such as extension receivers, property accessors, `@JvmName`, value-class mangling and companion/object bridges. Original provider IDs and run-scoped evidence references remain intact; interop identity is an additional join key, never a replacement for provenance.

Until that binder is implemented, K2's Java source binding proves that Kotlin analysis can resolve Java declarations, but it does not prove that Java and Kotlin worker records are already unified in the shared semantic graph.
