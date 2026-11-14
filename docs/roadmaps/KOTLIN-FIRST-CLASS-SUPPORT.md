# Aegis — Kotlin First-Class Language Support

## Execution contract

**Objective:** Deliver production-grade Kotlin semantic analysis in Aegis, using the same evidence discipline and phase governance established for Java.

**Execution sequence:** K0 analysis and engine proof → K1 build/source ownership → K2 semantic worker integration → K3 JVM interoperability and dependency projection → K4 framework intelligence → K5 test architecture → K6 JVM production validation → K7 Android support → K8 Kotlin Multiplatform support and target-specific validation.

**Delivery model:** One umbrella pull request against develop, implemented in small responsibility-based commits. The PR remains open until mandatory acceptance gates pass. A phase is not complete because its code has merely been committed.

**Initial implementation baseline:** Kotlin/JVM projects using Maven and Gradle, including mixed Java/Kotlin modules. Android and Kotlin Multiplatform are required deliverables in this workstream, not optional extensions; they have dedicated K7/K8 acceptance gates. The JVM baseline must not be described as complete Android or Multiplatform support.

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
- Temporary compiler outputs must be created in a host-selected, worker-validated temporary directory. The host retains usable class outputs only for downstream Kotlin-test/JDT binding and removes the entire temporary directory on completion, cancellation or failure.
- Compiler output must not be written into the scanned repository.
- The Kotlin worker reuses the packaged Java 21 runtime while keeping its compiler and library dependency set isolated and pinned.

### .NET integration

Add Kotlin-specific implementations behind the existing shared boundaries:

- IKotlinWorkerClient and Kotlin worker options.
- KotlinSemanticAnalysisAdapter : ISemanticAnalysisAdapter.
- KotlinSemanticAnalysisOrchestrator : ISemanticAnalysisProjectAnalyzer.
- A Kotlin classpath resolver using explicit module/build evidence and local artifact caches only.
- A shared JVM source-root resolver so Java and Kotlin use the same ownership and project-dependency rules.
- Dependency-injection registration integrated with the existing semantic pipeline.
- Kotlin compilation outputs represented as transient semantic artifacts, excluded from report serialization and cleaned by the pipeline after downstream Java analysis.

Do not fork the shared coordinator, semantic run contract, relationship projection, architecture graph or ledger.

### JVM host execution and artifact lifecycle

The K2 host must treat compiler outputs as transient execution artifacts, not report data:

- A host-created temporary root is passed to the worker and validated before the compiler writes anything. The worker never writes into the scanned repository.
- Each worker result may contain a successful JVM classes directory and the owning temporary root. Both paths are validated as descendants of the host-created root before registration.
- A per-invocation `SemanticAnalysisExecutionContext` owns artifacts and cleanup roots. It is created by the pipeline, passed to project analyzers, and disposed after all analyzers finish, including exception and cancellation paths.
- The Kotlin adapter returns semantic evidence plus optional language-neutral artifact metadata. It does not attach filesystem paths to `SemanticAnalysisRun` or serialize them into reports.
- The Kotlin orchestrator uses production output for its test compilation. The Java orchestrator may consume the same-module production output after Kotlin has run. K3 extends this to dependent modules and test-support outputs.
- Only outputs from a completed compiler run with complete required inputs are eligible as classpath artifacts. Partial outputs remain available for cleanup but are not used to claim resolved edges.

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

Kotlin production output is compiled before its test scope; usable production output is included in the test classpath. Kotlin module analysis follows project dependency order so dependent modules can consume already-produced outputs.

### Java references to Kotlin

The preferred path is to produce Kotlin JVM class outputs in a host-owned temporary directory, retain them as transient compilation artifacts, then pass those outputs as explicit classpath inputs to JDT for Java source analysis. The semantic pipeline orders Kotlin before Java and performs deterministic artifact cleanup after all analyzers complete. This must be proven in K2/K3 with:

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

## K0 status — accepted

K0 is accepted at exact head `c9ada0289a230347f898902a2a88c2964655b22f`. All four required workflows passed on that exact commit: Kotlin semantic worker (Linux and Windows), Java semantic worker (Linux and Windows), WPF build validation, and non-WPF quality gates including all test projects and production builds.

Validated in K0:

- Pinned Kotlin 2.4.21 K2 compiler and pre-lowering IR extraction for classes, interfaces, enums, annotation classes, type aliases, data classes, functions, constructors, properties, extension functions/properties, inheritance, implementation, calls, property reads/writes and annotation use.
- External Java source roots are available to Kotlin compilation; compiler output is materialized in a temporary classes directory and can be consumed by a Java compilation fixture.
- Stable relationship IDs aggregate repeated call-site provenance without embedding source offsets in identity; local declaration identity uses normalized lexical source context rather than line offsets or traversal ordinals.
- Lambda bodies are traversed without emitting synthetic lambda declarations, so calls inside callbacks are attributed to their enclosing source declaration.
- Kotlin property reads/writes resolve to their source property without duplicate synthetic accessor symbols.
- Java interop preserves declaration semantics: Java public fields accessed through Kotlin property syntax remain Field symbols; JavaBean synthetic property syntax resolves to the underlying getter method.
- Constructors have a distinct semantic identity namespace from ordinary functions. Constructor references and secondary-constructor delegation retain constructor identity.
- Callable references (`::function`), property references (`::property`), class references (`::class`), direct field access, enum-entry references, object references, constructor references and constructor delegation are covered by compiler integration assertions.
- Compiler errors remain explicit diagnostics and never produce a completed analysis.
- Bounded source manifests (16 MiB / 50,000 files), source-root collections, JSONL frames and response size.
- Process handshake, analysis, bounded-heap execution and graceful shutdown.
- Private runtime package with pinned dependencies, CycloneDX SBOM, and a packaged-JAR smoke test on Linux and Windows.
- Automated one-top-level-type-per-Kotlin-file and filename-matching enforcement.

K0 validates the compiler boundary and private worker packaging. It does **not** claim that the .NET host adapter, production/test orchestration, host-owned artifact lifecycle or cross-language graph identity are integrated; those remain K2–K3 work.

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
- Model inherited Kotlin Maven plugin source directories and compiler options from parent POMs without resolving inherited relative paths against the wrong module.
- Add Gradle Kotlin DSL and Groovy DSL, Maven, multi-module and mixed Java/Kotlin fixtures.

**Acceptance:** Kotlin modules and roots are correctly discovered; no build scripts execute; unresolved build metadata is visible; Java regression suite remains green.

### K2 — Kotlin semantic host integration

- Refactor process supervision into a shared `JvmWorkerProcess` used by both the JDT and Kotlin clients. Keep worker launch arguments and payload mapping provider-specific; do not duplicate timeout, cancellation, framing, handshake, process-tree termination or shutdown logic.
- Generalize `IJavaClasspathResolver` / `LocalJavaClasspathResolver` into JVM-scoped classpath resolution and reuse the same explicit Maven/Gradle dependency evidence and local-cache policy for Kotlin.
- Extract a shared `JvmSourceRootResolver` for language-specific source roots, production/test ownership, module boundaries, referenced Java roots, safe file enumeration and discovery diagnostics. Refactor Java to consume it with regression coverage.
- Add `KotlinWorkerOptions`, `IKotlinWorkerClient`, `KotlinSemanticAnalysisAdapter` and `KotlinSemanticAnalysisOrchestrator` behind the existing coordinator and project-analyzer contracts.
- Keep Kotlin compiler request/response DTOs private to the Kotlin adapter. Map worker status, diagnostics, source provenance and capabilities into the shared semantic contracts; compiler and IR types never cross the worker boundary.
- Introduce a per-pipeline-invocation semantic execution context for transient compilation artifacts. It must be concurrency-safe, excluded from report serialization, and responsible for cleanup in a `finally`/async-disposal path.
- The Kotlin orchestrator analyses production before test. A successful production output is an explicit classpath input to the test pass. Failed or partial compiler output is never treated as a resolved dependency.
- Feed module-owned Java source roots to Kotlin as binding-only inputs. Do not count Java files as Kotlin analyzed files.
- Order Kotlin before Java in the shared JVM pipeline so the Java analyzer can consume successful Kotlin production outputs. Cross-module dependency ordering and canonical cross-language graph identity remain K3.
- Package the Kotlin worker and pinned libraries alongside the existing private Java 21 runtime. Include its SBOM/notices in the deployment package and copy the worker into WPF/CLI output without adding Kotlin compiler dependencies to the .NET application.
- Recognize Android, Multiplatform, JS, Wasm and Native targets but do not route them through the JVM worker; emit explicit unsupported-target diagnostics until K7/K8.

**Acceptance:** A packaged Kotlin/JVM worker is invoked through the production .NET semantic pipeline on Windows and Linux. Production/test scopes remain distinct; test analysis binds to successful production output; Java source binding works; temporary artifacts are cleaned on success, failure and cancellation; Java regression tests remain green; unsupported platforms are not misreported as JVM.

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
- All mandatory Kotlin/JVM, Android and Multiplatform fixture and representative-project tests pass for the target families claimed as supported.
- All required CI workflows pass on the exact PR head.
- Supported compiler versions and known limitations are documented.
- No critical unresolved correctness defects remain.
- Kotlin is marked first-class only for the platforms/toolchains actually validated.

---

### K7 — Android Kotlin support

#### K7.1 — Android project and build-model discovery — accepted

K7.1 is accepted at exact code head \`813885b0c65b20fd088e1ba91e7a6297768f4768\`. All four required workflows passed on that head: Kotlin semantic worker, Java semantic worker, WPF build validation and non-WPF quality gates.

The accepted static model covers:

- Android Gradle Plugin application/library/test/dynamic-feature/asset-pack module kinds.
- Modern Groovy and Kotlin plugin declarations, version-catalog aliases, root \`apply false\` version declarations, legacy \`apply plugin\` declarations and root/module \`buildscript\` classpath versions.
- Plugin application state and static AGP version evidence. Dynamic/unresolved versions remain incomplete diagnostics.
- Namespace, compile SDK, build-tools version, default configuration, Java compile options, flavor dimensions, product flavors and build types.
- AGP 9 built-in Kotlin and new-DSL defaults, explicit module-level Kotlin disablement, and the legacy Kotlin Android plugin migration rule. The legacy plugin is accepted with AGP 9 only when both \`android.builtInKotlin=false\` and \`android.newDsl=false\` are declared.
- Android module identity and framework classification in the shared project model.
- Conservative Android DSL fallback when plugin identity is unresolved. Both Java and Kotlin semantic workers refuse to treat such modules as ordinary JVM projects.

K7.1 deliberately does not resolve active variants, Android manifests/resources, SDK installations, \`android.jar\`, variant classpaths, Compose semantics or Android architecture projections. Those belong to later checkpoints.

#### K7.2 — Variant-aware source and test ownership

- Model the variant matrix from build types and product flavors without executing Gradle.
- Discover variant-specific and conventional Java/Kotlin roots, generated roots and mixed-language ownership.
- Model \`AndroidManifest.xml\`, resources and assets by source set and variant.
- Distinguish local unit tests, instrumentation tests (\`androidTest\`), test fixtures and test-support ownership.
- Preserve production/test separation and variant-specific source/dependency evidence.

**Acceptance:** Application and library fixtures prove debug/release and flavor source ownership, manifest/resource ownership, Java/Kotlin mixed roots, unit-test versus instrumentation-test separation and test-to-production traceability.

#### K7.3 — Android framework and Jetpack Compose intelligence

- Add module-owned dependency and compiler-confirmed usage evidence for AndroidX, lifecycle, navigation, Room, Hilt/Dagger/Koin, coroutines and related Android integrations.
- Recognize Jetpack Compose declarations and usage without confusing Compose compiler-generated declarations with source declarations.
- Model Compose compiler/plugin compatibility and generated-code limitations as explicit evidence.
- Keep declared dependency evidence distinct from observed semantic usage.

**Acceptance:** Positive, negative and test-only fixtures validate framework classification, Compose usage and generated-symbol handling without annotation-only inference.

#### K7.4 — Android architecture projection

- Project Android modules, variants and source-set ownership into the existing component and dependency graph contracts.
- Preserve module, variant, production/test and source provenance in graph edges and the relationship ledger.
- Separate package-level dependencies from logical architectural components.
- Keep instrumentation-test relationships out of production coupling, cycle and component metrics.

**Acceptance:** Representative multi-module Android fixtures produce consistent module, package and architectural-component graphs with traceable variant-aware relationships and isolated test metrics.

#### K7.5 — Android SDK/classpath resolution and production validation

- Resolve SDK roots from \`local.properties\`, configured SDK environment variables and explicit build metadata without executing project scripts.
- Resolve the platform \`android.jar\` matching the selected compile SDK and the variant-specific compile classpath where local evidence is available.
- Preserve AAR/JAR and generated-source evidence with explicit resolution provenance.
- Report missing SDKs, ambiguous SDK versions, unresolved artifacts and unavailable generated code as partial coverage.
- Validate representative application/library projects, mixed Java/Kotlin compilation, worker safety, resource use and existing Java/Kotlin regression gates.

**Acceptance:** Android SDK and variant classpaths are proven against representative fixtures and a real project corpus. No Android source is reported as semantically complete when required SDK, dependency or generated-code inputs are missing.

**K7 acceptance:** All five checkpoints are accepted independently. Android support is first-class only for the SDK, variants, framework integrations and architecture projections actually validated.

### K8 — Kotlin Multiplatform support

- Model Kotlin Multiplatform plugin versions, target declarations and the complete source-set `dependsOn` graph.
- Distinguish `commonMain/commonTest`, intermediate shared sets and target-specific source sets; preserve source-set ownership and avoid counting shared source declarations once per target.
- Resolve `expect`/`actual` declarations and their relationships with exact source/run provenance.
- Introduce target-specific semantic compilation paths for JVM/Android, Kotlin/Native, Kotlin/JS and Kotlin/Wasm. A JVM worker must never claim semantic coverage for a non-JVM target.
- Model target-specific dependencies, platform SDK/toolchain requirements and generated source evidence. Unsupported targets remain explicit partial/unsupported results rather than silently disappearing.

**Acceptance:** Multiplatform fixtures cover common, intermediate and platform source sets; source-set dependency edges; expect/actual pairs; and each target family claimed as supported. Every configured target is either analyzed by a validated backend or reported as unsupported with an explicit coverage gap.

## 9. Checkpoint governance and commit discipline

Every phase must have written scope and implementation tasks, automated tests and explicit acceptance criteria, exact-head CI evidence, known limitations and outstanding risks, and a decision to proceed, remediate or revise scope.

Commit format:

[Action][Scope]: concise lowercase summary

Every commit body must explain context, implementation, architectural rationale, impact/compatibility implications, and validation/evidence.

Keep commits grouped by responsibility, normally 3–20 files, and avoid unrelated changes. Do not weaken existing gates, suppress failures, execute project build scripts, add unbounded dependencies, or claim support based on parser recognition alone.

**Final principle:** Aegis reports what the compiler proves, distinguishes inferred evidence, and exposes what it cannot determine. Android and Multiplatform support are not inferred from Kotlin/JVM recognition; each target family must pass its own acceptance gate.

## Cross-language identity constraint

The K2 worker's current symbol IDs are provider-local. For example, a Java type discovered as an external Kotlin IR stub currently receives a Kotlin-worker ID such as `kotlin:type:interop.JavaContract`. The Java worker uses JDT binding keys. Matching the displayed qualified name or the `language` field is not sufficient to prove that two provider records represent the same declaration.

K3 must introduce a normalized JVM interop identity and a collision-aware binder. The identity must account for module/artifact scope, owner binary name, JVM member name, erased JVM descriptor, constructor/static semantics and relevant Kotlin lowering such as extension receivers, property accessors, `@JvmName`, value-class mangling and companion/object bridges. Original provider IDs and run-scoped evidence references remain intact; interop identity is an additional join key, never a replacement for provenance.

Until that binder is implemented, K2's Java source binding proves that Kotlin analysis can resolve Java declarations, but it does not prove that Java and Kotlin worker records are already unified in the shared semantic graph.


## K2 status — accepted

K2 is accepted at exact head `6cda4b1a11ee33811d5cbfc2d2d380d72d6d600a`. All four required workflows passed on that exact commit: Kotlin semantic worker, Java semantic worker, WPF build validation and non-WPF quality gates.

The accepted host integration includes:

- Shared `JvmWorkerProcess` supervision for JDT and Kotlin, with bounded request/response protocol handling, timeouts, cancellation, process-tree termination and graceful shutdown.
- A Kotlin adapter and module-scoped orchestrator registered through the existing semantic coordinator/pipeline.
- Shared JVM source-root and local classpath resolution, with Java regression coverage.
- Host-owned per-invocation compiler artifact roots, output-path validation and deterministic cleanup.
- Production-before-test Kotlin analysis; successful production output is supplied to same-module test analysis.
- Kotlin-before-Java execution, with successful same-module Kotlin output supplied to JDT.
- Explicit partial/unsupported results for inferred compiler configuration, incomplete dependency metadata, unexecuted compiler plugins and unsupported non-JVM targets.
- Packaged worker validation on Windows and Linux without adding Kotlin compiler dependencies to the .NET application.

K2 acceptance proves same-module Kotlin/Java interoperability and the host lifecycle. It does **not** prove cross-module output ordering or canonical cross-provider symbol identity; those remain K3.

## K3 implementation decisions — 2026-11-14

### Module dependency execution

- Use only explicit Maven/Gradle project-dependency edges already represented in `ProjectArchitectureContext.Dependencies`. Do not infer module dependencies from directory names, package names or source imports.
- Order Kotlin production modules dependency-first using production edges.
- Complete production analysis for all Kotlin modules before starting test analysis.
- Order test analysis dependency-first using both production and test edges. A test-only dependency must not affect production compilation order or production classpaths.
- Resolve transitive project outputs by scope. Production consumers receive production outputs only. Test consumers receive production outputs plus test outputs reachable through explicit test-dependency edges.
- A project-dependency cycle or ambiguous module target is a coverage diagnostic, not a reason to fall back to alphabetical ordering and claim complete resolution.

### Compiler output eligibility

A successful compiler output is a **binding artifact**, not proof that the complete dependency graph was resolved.

- The Kotlin compiler must report successful compilation with no compiler errors.
- Compiler configuration must be explicit and compatible with the pinned worker.
- Declared compiler plugins/code generators must not have been skipped when they can affect the module ABI.
- The host must validate that the output remains inside its invocation-owned artifact root.
- A run may remain `PartiallyCompleted` because the local classpath resolver cannot prove transitive closure. A successfully compiled output can still be supplied as a binding artifact; downstream compilers must independently resolve each use and retain their own unresolved diagnostics.
- Failed compilation, inferred compiler settings, unsupported targets, or skipped ABI-affecting plugins never publish binding artifacts.

This distinction permits useful cross-module binding without relabelling a partial semantic run as complete. It does not upgrade missing external dependencies to resolved evidence.

### Canonical JVM identity

Provider IDs remain opaque and run-scoped. Add an additional, optional JVM ABI identity for cross-provider joins.

The identity must include:

- Owning module or resolved artifact scope.
- JVM binary owner name.
- JVM member kind and exact member name.
- Erased JVM field or method descriptor.
- Static/instance semantics where applicable.

A source symbol may expose multiple JVM identities when Kotlin lowering creates multiple callable ABI entries. This is required for properties/accessors, file facades, `@JvmName`, overload bridges and companion/object bridges.

Implementation rules:

- Java identities come from resolved JDT bindings and erased JVM descriptors.
- Kotlin identities must reflect the compiler's actual JVM ABI. Do not approximate value-class mangling, suspend lowering, extension receivers, property accessors or file-facade names from qualified names alone.
- In-workspace source symbols are scoped to their owning build module. External library symbols are scoped to a resolved artifact. Unknown ownership remains unbound.
- Preserve the original provider symbol IDs, relationship IDs and run-scoped provenance. JVM identity is an additional join key only.
- Bind an external relationship endpoint to an in-workspace source symbol only when the complete identity resolves to exactly one source declaration. Ambiguous or missing matches remain external and produce no internal architecture edge.
- The semantic architecture projection must use this binder for cross-language edges; it must not discard a compiler-resolved relationship merely because the target was represented as an external symbol by the consuming compiler.

### K3 acceptance corpus

- Kotlin module depending on another Kotlin module, including transitive project dependencies.
- Java module depending on a Kotlin module and Kotlin module depending on Java source modules.
- Production and test-only project dependencies, including test-support modules.
- Duplicate qualified names in separate modules to prove that scope prevents false joins.
- Overloaded Java/Kotlin methods, constructors, properties/accessors, top-level functions/file facades, extension functions, suspend functions, `@JvmName`, value classes and companion/object bridges.
- Ambiguous, unresolved and incomplete classpath cases; each must remain partial/unbound rather than being joined by display name.
- Architecture component graph and relationship-ledger assertions proving that cross-language edges retain exact source provenance and are not double-counted.


## K3 status — accepted

K3 is accepted at exact head `c0efa12ed0441aa2edc9b5dfd302dc56ebef7783`. The Kotlin and Java semantic workers, WPF build validation and non-WPF quality gates passed on that exact head.

The accepted implementation establishes dependency-first execution across explicit mixed Java/Kotlin project edges, production-before-test scheduling, scope-aware transitive project outputs, preservation of classpath precedence, and bidirectional mixed-language binding tests. The semantic graph and relationship-ledger projection retain the accepted cross-provider provenance and collision-aware binding behavior.

## K4 status — accepted

K4 is accepted at exact head `a75aeac463e27583783916231bb59ee05d2b53bc`. All four required workflows passed on that exact head.

Kotlin framework intelligence reuses the Java semantic framework evidence pipeline rather than introducing a parallel detector. It keeps module-owned declared dependency evidence distinct from compiler-confirmed source usage, classifies Ktor, kotlinx.coroutines, kotlinx.serialization and Exposed, and recognizes Kotlin test libraries including Kotest, MockK, Kotlin Test and kotlinx-coroutines-test. Declared versions and build-model provenance remain separate from compiler provenance; test-scoped dependencies are represented as test-only evidence.

## K5 implementation — in progress

K5 builds on the shared JVM production/test orchestration and dependency resolver. The existing implementation already discovers conventional and explicit Gradle source sets, Maven Kotlin main/test roots, test projects and test-support modules; it runs production before test, supplies successful production outputs to test compilation, resolves test-scoped project outputs, and projects test relationships separately from production dependencies.

A K5 correctness gap was identified in semantic component metrics: the projection trusted only the provider's per-symbol `IsTestCode` flag when counting production and test symbols. A module already classified as a test project or test-support module must remain test-scoped even if a provider omits that flag. The projection now derives test ownership from both symbol evidence and authoritative module ownership. Focused tests cover test projects and test-support modules, production/test component counts, and traceable test-to-production relationships.

K5 remains unaccepted until focused tests and all four required workflows pass on the exact PR head. Remaining K5 validation must cover source-set and module discovery, test-aware classpaths, framework attribution, test-to-production graph evidence, and exclusion of test evidence from production metrics.


## K5 status — accepted

K5 is accepted at exact head `7fc7324c104c74f53c58cc304c591eb3b597d125`. The four required workflows passed on that exact head.

The accepted test-architecture work validates conventional and explicit Gradle Kotlin source sets, Maven Kotlin main/test roots, test-project and test-support ownership, production-before-test scheduling, scope-aware transitive dependency traversal, test-only framework evidence, test-to-production relationship traceability, and production/test component metric isolation. The module-ownership projection regression is covered for both test projects and test-support modules.

## K6 status — accepted

K6 is accepted at exact head `3a050e83bf0c4a6950ca54fbdab356cdf1f6dd5a`. The Kotlin semantic worker, Java semantic worker, WPF build validation and non-WPF quality gates all passed on that exact head.

The production-readiness pass enforces positive, syntactically valid JVM heap settings and positive request timeouts before process launch. Regression tests cover malformed heap values and non-positive timeouts. Packaged worker execution, process supervision, diagnostics and partial-result behavior remain governed by the existing K0–K5 contracts.

## K7.1 — Android project and build-model discovery — in progress

**Scope:** Recognize Android Gradle modules from statically declared Android Gradle Plugin IDs; distinguish application, library, test, dynamic-feature and asset-pack module kinds; preserve AGP plugin version evidence from module declarations, version catalogs and root-level `apply false` declarations; and model literal namespace, compile SDK, build-tools version, default configuration, compile options, flavor dimensions, product flavors and build types.

Kotlin integration must distinguish the legacy `org.jetbrains.kotlin.android` plugin from AGP 9+ built-in Kotlin support, including explicit opt-out and incompatible legacy-plugin combinations. Android configuration remains declaration evidence: this checkpoint does not resolve installed SDKs, `android.jar`, active variants, variant-specific classpaths or source ownership. No Gradle scripts or plugins are executed.

**Acceptance:** Groovy and Kotlin DSL fixtures cover application and library modules, inherited AGP versions, AGP 9 built-in Kotlin, legacy Kotlin Android integration, literal SDK/default configuration, product flavors, build types, and dynamic or conflicting declarations. Unsupported or ambiguous configuration is surfaced as coverage diagnostics. Existing Kotlin/JVM behavior and the four required CI workflows remain green.

