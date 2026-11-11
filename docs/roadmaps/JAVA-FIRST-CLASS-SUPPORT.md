# Aegis — Java First-Class Language Support

## PR manifesto and engineering execution contract

**Objective:** Deliver production-grade Java support in Aegis, extending beyond existing language recognition, static naming heuristics and basic manifest inspection.

**Strategic sequence:** Java first, Kotlin second, Python third.

**Delivery model:** One umbrella pull request, implemented through J0–J7 phases with explicit acceptance checkpoints. The PR remains open until all mandatory acceptance criteria are met. A checkpoint is not complete merely because its code has been committed.

---

## 1. Engineering principles

1. **Shared architecture model:** Java implementation details must not leak into shared architecture contracts or evaluators.
2. **Evidence before inference:** Every relationship identifies its origin, evidence type, source location where available, and confidence.
3. **No false certainty:** Unresolved symbols, incomplete dependencies and unsupported constructs are explicitly reported.
4. **Production/test separation:** Test projects and test-support libraries remain distinct from production architecture.
5. **Safe analysis by default:** Maven and Gradle builds are inspected statically. Arbitrary build scripts and plugins are not executed during ordinary scans.
6. **No heuristic masquerading as semantic analysis:** Names and annotations alone do not prove relationships that require semantic resolution.
7. **Backward compatibility:** Existing C# analysis and shared evaluator behaviour must not regress.
8. **Language-neutral presentation:** Graphs, ledgers, findings and reports must not depend on language-specific UI assumptions.
9. **Maintainable implementation:** No temporary workarounds, duplicated evaluators or weakened quality gates.
10. **Measured completeness:** Every capability has automated tests and explicit acceptance criteria.

## 2. Phases and acceptance checkpoints

### J0 — Java capability and semantic contracts

**Objective:** Establish the language adapter boundary and normalized evidence contracts.

Scope:
- Audit existing Java detection, build inspection, source analysis and evaluator assumptions.
- Define normalized project, symbol, dependency, framework and test evidence.
- Define evidence provenance, confidence and stable identity.
- Define analysis completeness and failure statuses.
- Define language capability reporting.
- Evaluate candidate semantic engines against explicit criteria.

**Checkpoint J0:**
- Java can populate shared architecture contracts without Java-specific dependencies leaking into shared evaluators.
- Complete, partial, unsupported and failed analysis are distinguishable.
- Existing C# regression tests remain green.
- Semantic-engine selection is supported by a documented technical spike, not preference alone.

### J1 — Maven and Gradle intelligence

**Objective:** Understand Java project structures and declared dependencies.

Maven:
- POM parsing and parent inheritance.
- Multi-module projects.
- BOMs and dependency management.
- Compile, runtime, provided and test scopes.
- Profiles, properties and exclusions.
- Plugins and annotation processors.
- Source roots and generated sources.

Gradle:
- Groovy and Kotlin DSL build files.
- Multi-project builds and included builds.
- Version catalogs.
- Dependency configurations and platforms.
- Plugins and toolchains.
- Source sets and generated sources.
- Annotation processors.

Security and correctness:
- Static inspection by default.
- No arbitrary build-script execution.
- Declared, resolved and observed dependencies are distinct concepts.
- Optional resolution must be isolated, explicitly authorized and report its limitations.

**Checkpoint J1:**
- Single- and multi-module repositories are correctly represented.
- Declared and resolved dependencies are distinguishable.
- Dependency scopes are preserved.
- Incomplete dependency resolution is reported.
- Maven and Gradle fixture suites pass.

### J2 — Compiler-grade Java semantic analysis

**Objective:** Replace heuristic source inspection with semantic source analysis.

Technology candidates:
- Eclipse JDT.
- JavaParser with Symbol Solver.
- Spoon.

Evaluation criteria:
- Modern Java syntax coverage.
- Binding and symbol resolution accuracy.
- Cross-project and classpath resolution.
- Performance and memory consumption.
- Error recovery and partial-project behaviour.
- Licensing and redistribution implications.
- Runtime/deployment complexity and maintenance burden.

Required capabilities:
- Packages and imports.
- Classes, interfaces, records, enums and annotations.
- Inheritance and interface implementation.
- Generic type relationships.
- Method calls and constructor references.
- Visibility and encapsulation.
- Dependency-injection evidence.
- Reflection and service loading indicators.
- Java module declarations.
- Generated versus handwritten code.
- Source locations and stable symbol identity.

The engine must be isolated behind a versioned language-adapter boundary. A JVM worker is a candidate deployment model, not a foregone conclusion; the J0 spike must compare operational alternatives.

**Checkpoint J2:**
- Source symbols can be resolved reliably.
- Cross-module references have identifiable targets.
- Relationships carry evidence and provenance.
- Unsupported and unresolved constructs are reported.
- Accuracy, memory and performance have representative tests.

### J3 — Dependency graph and architectural relationships

**Objective:** Integrate resolved Java relationships into the Aegis investigation workspace.

Relationship types:
- Project and module dependencies.
- Package and type relationships.
- Inheritance and implementation.
- Method invocation.
- Interface abstraction and implementation.
- Framework-managed dependencies.
- External library dependencies.
- Compile-time, runtime and test-only relationships.

Graph requirements:
- Preserve isolated project nodes; do not derive the complete node inventory exclusively from dependency edges.
- Separate production and test relationships.
- Preserve relationship identity and provenance.
- Calculate coupling, cycles and architectural metrics from production evidence.

**Checkpoint J3:**
- Graph and relationship ledger accurately represent Java projects.
- Test relationships remain inspectable but excluded from production metrics.
- Cycles and coupling use correctly scoped evidence.
- Existing C# graph behaviour remains correct.

### J4 — Java framework intelligence

**Objective:** Understand architectural responsibilities implemented through Java frameworks.

Priority:
1. Spring Framework and Spring Boot.
2. Jakarta EE.
3. Quarkus.
4. Micronaut.

Additional integrations:
- Hibernate and JPA.
- Kafka and RabbitMQ.
- gRPC and Protocol Buffers.
- MapStruct.
- Lombok.
- Jackson.
- OpenAPI.
- Flyway and Liquibase.

Framework evidence must be contextual. An annotation alone must not establish an architectural layer or responsibility.

**Checkpoint J4:**
- Frameworks and versions are identified when evidence exists.
- Framework-managed dependencies are represented.
- Architectural roles are resolved contextually.
- Framework fixtures demonstrate correct classification.
- Framework-specific rules do not contaminate shared evaluators.

### J5 — Test architecture and quality analysis

**Objective:** Integrate Java test architecture without contaminating production analysis.

Support:
- JUnit 4 and 5.
- TestNG.
- Mockito.
- AssertJ.
- Testcontainers.
- Maven Surefire and Failsafe.
- Gradle test tasks and source sets.
- Integration and component testing.

Analysis:
- Test-project discovery.
- Test-support library identification.
- Test-to-production dependencies.
- Unit versus integration test classification.
- Test fixtures and shared testing infrastructure.
- Coverage ingestion where available.
- Architectural boundary checks.

**Checkpoint J5:**
- Test projects and support modules are correctly identified.
- Production and test relationships remain separate.
- Test findings retain source attribution.
- Coverage is never inferred merely from test-file existence.

### J6 — Runtime and dynamic architecture evidence

**Objective:** Introduce optional runtime evidence to complement static analysis.

Candidate integrations:
- Java Flight Recorder.
- OpenTelemetry.
- Distributed tracing.
- Runtime dependency observations.
- Spring application-context inspection.
- Runtime messaging topology.
- Service discovery and dynamic component relationships.

Analysis:
- Declared versus observed dependencies.
- Static versus executed method relationships.
- Runtime-resolved dependency injection.
- Observed messaging and service interactions.
- Configuration-dependent architecture.
- Architectural drift.

Static and runtime evidence must never silently overwrite one another. Runtime collection is opt-in, with explicit collection boundaries and privacy controls.

**Checkpoint J6:**
- Static and runtime evidence remain distinguishable.
- Runtime observations have provenance and timestamps.
- Correlation between evidence sources is tested.
- Missing runtime evidence is not treated as proof of missing architecture.

### J7 — Production readiness and Java parity

**Objective:** Demonstrate that Java is a first-class Aegis language.

Validation repository matrix:
1. Conventional layered Spring Boot application.
2. Multi-module Maven enterprise application.
3. Gradle multi-project application.
4. DDD and hexagonal modular monolith.
5. Kafka and gRPC microservice ecosystem.
6. Legacy Java repository with incomplete build metadata.
7. Repository with substantial generated code.
8. Repository containing production and test-support modules.

Acceptance dimensions:
- Discovery and build metadata.
- Semantic parsing and symbol resolution.
- Dependency resolution.
- Framework recognition.
- Architecture evaluation.
- Graph and ledger.
- Test separation.
- Reporting.
- Performance and reliability.
- Regression compatibility.

**Checkpoint J7 — Final acceptance:**
- All required fixtures pass.
- Mandatory quality gates pass.
- Performance is measured and documented.
- Known limitations are documented.
- No critical unresolved analysis-correctness defects remain.
- Java support is marked production-ready only when evidence supports that claim.

---

## 3. Checkpoint governance

Every phase must have:
- Written scope and implementation tasks.
- Automated tests and explicit acceptance criteria.
- A documented checkpoint result.
- Known limitations and outstanding risks.
- A decision to proceed, remediate or revise scope.

Every checkpoint PR comment must identify:
1. Phase and checkpoint identifier.
2. Implemented capabilities.
3. Changed architectural contracts.
4. Tests executed and results.
5. CI evidence.
6. Outstanding issues.
7. Acceptance decision.

A failed checkpoint blocks dependent work until the underlying issue is resolved or an explicit architectural decision changes the scope. CI that is still running is not evidence of success.

## 4. Commit documentation policy

Commit format:

`[Action][Scope]: concise lowercase summary`

Every commit body must explain:
- **Context:** The problem or requirement.
- **Implementation:** What changed and how.
- **Architectural rationale:** Why this approach was selected.
- **Impact:** Affected components and compatibility implications.
- **Validation:** Tests, checks and evidence obtained.

Commits must be logically grouped by responsibility. Avoid unrelated changes in one commit and vague messages such as "fix Java support" or "update analysis".

Examples:
- `[Add][Java]: introduce normalized semantic evidence contracts`
- `[Add][Java.Build]: resolve Maven module inheritance and dependency scopes`
- `[Add][Java.Analysis]: resolve cross-module type references through compiler bindings`
- `[Fix][Architecture]: exclude test-only relationships from production coupling`
- `[Test][Java]: add multi-module Gradle resolution fixtures`

## 5. PR progress commentary policy

The PR conversation is the engineering audit trail. Add substantive comments for:
- Initial implementation plan.
- Start of every phase.
- Completion of every checkpoint.
- Significant architectural decisions.
- Technology evaluation results.
- CI failures and root-cause corrections.
- Scope changes.
- Final acceptance.

Comments must contain technical information, not progress-only announcements. A checkpoint comment does not replace automated test or CI evidence.

## 6. Quality and regression policy

Throughout all phases:
- Do not disable tests to achieve green CI.
- Do not weaken existing architectural rules to accommodate Java.
- Do not introduce language-specific shortcuts into shared analysis.
- Do not suppress unresolved evidence without recording it.
- Do not claim semantic resolution when only heuristics were used.
- Do not claim runtime validation without runtime evidence.
- Preserve existing .NET analysis capabilities.
- Maintain automated architecture and regression tests.
- Keep documentation synchronized with implementation.

Fix defects in shared contracts at their source rather than hiding them in the Java adapter.

## 7. Out of scope

This PR does not implement Kotlin, Python, Android-specific analysis, native compilation analysis or other new language integrations. The JVM foundation must nevertheless leave a clean path for Kotlin to reuse appropriate build-system and runtime infrastructure.

## 8. Definition of done

Java support is complete only when:
- J0–J7 acceptance gates are satisfied.
- Java projects are semantically analysed rather than merely recognised.
- Maven and Gradle structures are correctly understood.
- Source relationships are resolved with traceable evidence.
- Framework architecture is understood contextually.
- Production and test architecture remain separated.
- Shared evaluators work without language-specific duplication.
- Static and optional runtime evidence remain distinguishable.
- Graphs, findings, ledgers and reports provide accurate results.
- Mandatory CI and regression gates pass.
- Documentation reflects actual capabilities and limitations.

**Final principle:** Aegis must report what it can prove, distinguish what it infers, and explicitly expose what it cannot yet determine.

---

# J0 baseline audit — 2026-11-11

This audit began from the `main` branch baseline. During J0 implementation, the affected paths were re-inspected on `develop` (the PR base) on 2026-11-11. The corrected `develop` observations below govern implementation where they differ from the initial baseline.

## Confirmed baseline findings

1. **Language detection is heuristic.** `LanguageAndBuildDetector` identifies Java when it sees a `.java` file or a path containing `pom.xml`, then always selects Maven. A Gradle-only repository can therefore be misidentified.
2. **Build-system support is incomplete.** `ArchitectureBuildSystem` already contains Maven and Gradle values, but current primary language detection does not select Gradle from Gradle manifests.
3. **Module discovery omits Gradle.** On `develop`, `ModuleDetector.ProjectManifestNames` recognises `package.json` and `pom.xml`; `.csproj` files are handled separately. It does not recognise `build.gradle`, `build.gradle.kts`, `settings.gradle` or `settings.gradle.kts`.
4. **Maven dependency extraction is structurally unsafe.** `DependencyDetector.AnalyzeJava` uses a regular expression over every `<artifactId>` element. This can mix project coordinates, dependency coordinates, plugin coordinates and other artifact identifiers; it does not preserve group IDs, versions, scopes, exclusions, inheritance or dependency-management semantics.
5. **Gradle dependency extraction is absent.** The Gradle switch case calls the same Maven-only parser.
6. **Framework detection is shallow and order-sensitive.** The Java detector checks whether any file path contains the word `spring`; its dependency set is not populated from Maven metadata within that detector. It does not resolve framework versions or annotation semantics.
7. **No compiler-grade Java semantic engine is wired into the architecture analysis project.** The inspected project references Roslyn for C# but no Java parser/binding engine.
8. **The shared project context has no explicit semantic-analysis completeness contract.** It currently exposes broad confidence and inference metadata, but no structured distinction between unresolved symbols, unsupported constructs and dependency-resolution status.
9. **Java evaluator support is largely lexical/heuristic.** Several design-pattern evaluators list Java in `SupportedLanguages`, but their result metadata explicitly records `SemanticAnalysisPerformed=false` and/or `StructuralAnalysisHeuristic=true`. For example, `RepositoryPatternEvaluator` uses regular expressions and explicitly does not verify interface implementation. These evaluators must not be described as semantic Java analysis.
10. **Naming analysis is asymmetric.** `CSharpNamingDeclarationAnalyzer` is backed by Roslyn and is explicitly C#-only. `NamingEvaluator` also contains generic C-style regex extraction paths that can produce Java-looking names, but those are lexical heuristics, not a Java declaration analyzer or semantic evidence. The resolver, policy dispatch and evaluator integration still require a complete audit.
11. **The shared evidence model is currently thin.** `ArchitectureDependencyContext` carries source/target names, layers, dependency type and file only; `ArchitectureInferenceTrace` has a broad source enum and confidence; `ArchitectureEvaluatorResult` has generic evidence and line fields. These are useful existing primitives, but they do not yet form a stable semantic evidence contract with typed relationship kinds, provenance identity, resolution status or completeness semantics.
12. **The repository already has a test suite and solution.** The solution includes architecture, shared, quality, security, infrastructure and SDK test projects. Java fixtures should be added to the existing architecture test strategy rather than creating an isolated test universe.

13. **The discovery pipeline is ordered and currently loses Java build evidence.** `ProjectArchitectureContextDetector` runs language/build detection, module discovery, framework detection, dependency inventory, then layer/domain/architecture/pattern analysis and dependency graph mapping. Because Java framework detection runs before dependency extraction, its local dependency set is empty. The Java framework detector therefore falls back to a path substring check for `spring`, not declared framework coordinates or resolved annotations.
14. **Gradle is not a viable detected Java project path yet.** `LanguageAndBuildDetector` selects Maven for any Java source/POM and does not inspect Gradle settings/build manifests. `ModuleDetector` discovers `pom.xml` and `package.json`, not Gradle manifests. A Gradle-only project can have Java source classified as Maven, and its project/module boundaries are absent.
15. **The Maven module projection is shallow.** `PopulateMavenMetadata` reads the module's local artifact ID and packaging. It does not calculate inherited coordinates, parent properties, reactor relationships, dependency management, or effective scopes. This is project naming/type metadata, not Maven model resolution.
16. **Java dependency inventory is not coordinate-safe.** The current Java parser collects every `artifactId` from POM XML. It cannot distinguish project coordinates, dependency coordinates, BOM imports, plugins, extensions or annotation processors, and it drops group/version/scope/exclusion/provenance.
17. **Java entry-point detection is lexical.** `EntryPointDetector` searches source text for the classic `public static void main(` spelling. It does not bind the declaration to a type, account for module/project ownership, or support newer simplified/instance main-method forms. It should remain an indicator until semantic analysis exists.
18. **Naming declaration analysis is C#-specific at the semantic boundary.** Dependency injection registers `CSharpNamingDeclarationAnalyzer` as a concrete singleton and `NamingEvaluator` injects that concrete type. The `INamingDeclarationAnalyzer` interface is not used as a language-adapter collection. Java takes the generic C-style regex path in `EvaluateFile`; this is lexical naming evidence, not a Java AST analyzer.
19. **Evaluator applicability can suppress plain Java.** `RuleEngine` selects evaluators by both `SupportedLanguages` and `SupportedFrameworks`. `ArchitecturalEvaluator` lists Java but only lists Spring Boot among Java framework identities; the detector's default Java identity is Java SE. Consequently a plain Java SE module may not execute that evaluator. This needs an explicit language-agnostic/framework-applicability decision, not an accidental framework-name workaround.
20. **Existing Java design-pattern evaluators are heuristic by contract.** Their regex/source-mask analysis can identify naming and lexical patterns but cannot prove symbol identity, inheritance, interface implementation, injection wiring or invocation relationships. The existing `SemanticAnalysisPerformed=false` and heuristic metadata must remain truthful until normalized semantic evidence is consumed.

## Semantic engine evaluation — initial evidence

| Candidate | Confirmed strengths | Risks / questions |
|---|---|---|
| Eclipse JDT Core | AST parser and optional binding resolution; batch AST APIs; Java compiler infrastructure; EPL-2.0 | Binding resolution has material time and memory cost; Java model/environment and classpath setup complexity; worker packaging and lifecycle need a spike |
| JavaParser + Symbol Solver | AST plus symbol-resolution layer; project exposes Java 1–25 support; Apache 2.0 or LGPL option | Symbol-solver behaviour and edge-case coverage must be benchmarked against representative Maven/Gradle projects; avoid assuming compiler-equivalent resolution |
| Spoon | Analysis-oriented Java AST and transformation model; project documentation states support through Java 25 | Binding/classpath fidelity, memory, incremental behaviour, licensing and operational integration need validation |

**Initial recommendation:** Keep Eclipse JDT and JavaParser as the two primary candidates for a controlled technical spike. Spoon remains a comparator for analysis ergonomics. Do not commit to a parser or deployment model until representative fixture results are available.

References:
- Eclipse JDT ASTParser: https://help.eclipse.org/latest/topic/org.eclipse.jdt.doc.isv/reference/api/org/eclipse/jdt/core/dom/ASTParser.html
- Eclipse JDT Core repository and EPL-2.0 license: https://eclipse.googlesource.com/jdt/eclipse.jdt.core
- JavaParser repository and Java 1–25 / dual-license statement: https://github.com/javaparser/javaparser
- JavaParser parsing and symbol resolution: https://github-wiki-see.page/m/javaparser/javaparser/wiki/Parsing-and-Symbol-Resolution-APIs
- Spoon project and supported Java versions: https://spoon.gforge.inria.fr/

## J0 implementation tasks

- [x] Inspect the shared project context, dependency, inference, diagnostics and naming-analysis contracts.
- [x] Re-inspect the affected detector and evaluator paths on the actual PR base (`develop`) and correct stale baseline references.
- [x] Complete the end-to-end trace of Java source files through discovery, project/module context, evaluator applicability and naming-analysis registration; record remaining C#-specific assumptions.
- [x] Define normalized project, dependency, framework, symbol and relationship evidence with source provenance, confidence, canonical identity, and explicit resolution state.
- [x] Define semantic run outcomes: not requested, unsupported, completed, partially completed, and failed.
- [x] Define the shared semantic-analysis request and language-adapter boundary without parser-specific dependencies.
- [x] Build a representative Java fixture corpus covering Maven, Gradle, modules, generics, annotations, unresolved symbols, generated code and test sources.
- [x] Benchmark Eclipse JDT and JavaParser against the same fixtures in source-only and controlled external-classpath modes; Spoon was not added because the two primary candidates already exposed the relevant J0 trade-offs.
- [x] Record the evidence-based J2 prototype candidate, trade-offs, licensing and measured results in the PR checkpoint discussion; production engine and deployment selection remain subject to J2 validation.
- [x] Run the full existing non-WPF regression suite and production build, WPF build, fixture tests and benchmark workflow on the final J0 head.

### J0 contract implementation — 2026-11-11

The first additive contract slice is committed on this branch:

- `SemanticAnalysisStatus` distinguishes unrequested, unsupported, completed, partially completed and failed runs.
- `SemanticSymbolKind`, `SemanticSymbolOrigin`, `SemanticRelationshipKind` and `SemanticResolutionStatus` provide language-neutral declaration, origin, relationship and resolution vocabularies.
- `SemanticEvidenceProvenance` binds each fact to an analyzer, evidence source, optional source location and confidence.
- `SemanticSymbol` uses an opaque adapter-generated canonical identity. The contract requires deterministic identity that does not depend on source line numbers.
- `SemanticRelationship` retains unresolved target descriptions without fabricating target symbol IDs.
- `SemanticAnalysisRun` records engine identity, project scope, status, coverage counts, exercised capabilities, symbols, relationships and diagnostics.
- `ArchitectureDependencyEvidence` separates package/build/runtime dependency inventory from internal project graph edges and retains ecosystem, declared/resolved versions, scope, directness, test-only status and provenance.
- `FrameworkEvidence` records framework identity/version, normalized role, owning project, test-only status and provenance. `ProjectModuleContext` and `ProjectArchitectureContext` retain module- and repository-level framework evidence.
- `SemanticAnalysisRequest` defines the bounded input scope and requested capability identifiers. It contains discovered paths and policy flags, not executable build instructions.
- `ISemanticAnalysisAdapter` establishes the language boundary and declares supported capability identifiers. The run records the capabilities actually exercised; requested, supported and exercised capabilities remain distinct. No parser or compiler types cross the boundary.
- `ProjectArchitectureContext.SemanticAnalysisRuns` retains analysis attempts. An empty collection means no run has been recorded, not that analysis succeeded.
- `SemanticAnalysisContractTests` covers outcome distinctions, Completed-only completeness, unresolved target preservation, initial context state, and dependency/framework evidence retention.

This slice defines contracts only. No Java adapter is registered, no semantic engine has been selected, and no Java semantic-analysis capability is being claimed as operational.

### J0 semantic-engine spike harness — 2026-11-11

An isolated Maven tool under `tools/java-semantic-spike` compares:
- Eclipse JDT Core `3.47.0` (EPL-2.0).
- JavaParser Symbol Solver `3.28.2` (Apache-2.0 or LGPL-3.0, selected by the adopter).

The workflow `.github/workflows/java-semantic-engine-spike.yml` runs both engines in two independent modes against the same checked-in corpus:
1. Source roots plus the running JDK.
2. The same source roots plus a separately resolved, pinned external classpath (Spring Context, JUnit Jupiter API, MapStruct API and transitives).

The classpath manifest is independent of the fixture Maven and Gradle builds. CI does not execute fixture build scripts, plugins or annotation processors. Both engines receive the same external JAR list: JDT through `ASTParser.setEnvironment`, JavaParser through `JarTypeSolver`.

The corpus contains 15 Java source fixtures and six source roots. The benchmark independently tracks five expected cross-source types, three expected external types and two intentionally unresolved type identities. The run fails if either engine resolves an intentionally missing target.

#### Final J0 benchmark evidence

Final benchmark workflow run: `43077082530`, commit `14bcb141a69733c54a886f51d5d0c0f53ce21ed7`, artifact `13883240203`.

| Metric | JDT source-only | JDT external classpath | JavaParser source-only | JavaParser external classpath |
|---|---:|---:|---:|---:|
| Parsed files | 15/15 | 15/15 | 15/15 | 15/15 |
| Expected cross-source types | 3/5 | 3/5 | 5/5 | 5/5 |
| Expected external types | 0/3 | 3/3 | 0/3 | 3/3 |
| Unexpectedly resolved missing types | 0 | 0 | 0 | 0 |
| Diagnostics | 22 | 9 | 0 | 0 |
| Elapsed milliseconds | 709 | 819 | 275 | 261 |
| Approximate post-run heap | 61.7 MB | 44.0 MB | 19.1 MB | 25.7 MB |

The controlled classpath contained 13 JAR entries. Both engines resolved all three expected external types. Adding the external classpath reduced JDT diagnostics from 22 to 9 and increased its aggregate successful bindings from 65/72 to 66/72. JavaParser aggregate successful bindings increased from 50/82 to 54/82. Aggregate binding counts and ratios are not directly comparable because the engines expose different AST visitor and diagnostic models. Heap values are approximate post-run snapshots, not peak memory; timings are single-run observations.

#### J0 engine decision

**JavaParser Symbol Solver is the preferred candidate for the J2 integration prototype.** It resolved all five expected cross-source types, all three expected external types and none of the intentionally unresolved targets. It also completed both benchmark passes faster and with lower observed post-run heap than JDT on this corpus.

This is a candidate decision, not a production-engine or deployment-model commitment. The 15-file corpus is too small to establish production reliability. J2 must validate JavaParser against larger real-world Maven and Gradle repositories, module-path and dependency-version conflict cases, partial/error recovery, and memory/performance envelopes. JDT remains a comparator until that validation is complete. The two engines' aggregate binding ratios are not used as the selection criterion.

No production parser dependency has been added to Aegis, no Java adapter is registered, and no Java semantic capability is claimed operational.

#### Final J0 CI evidence

All mandatory workflows completed successfully on final J0 head `14bcb141a69733c54a886f51d5d0c0f53ce21ed7`:
- Non-WPF quality gates: run `43077082507` — success. All non-WPF test projects and production builds passed.
- WPF build validation: run `43077082484` — success.
- Java semantic engine spike: run `43077082530` — success. Both source-only and controlled-classpath passes completed, including the negative-resolution oracle.

**J0 checkpoint outcome: accepted.** Shared contracts, adapter boundary, discovery/evaluator audit, representative fixture corpus, controlled semantic-engine comparison, evidence-based J2 candidate decision and final-head CI validation are complete. J1 may proceed.

---

# J1 implementation checkpoint — Gradle model foundation — 2026-11-11

**Status: implementation slice committed; J1 overall remains in progress and is not accepted.**

## Implemented in this slice

- Added a module-owned `GradleBuildModel` and static resolver.
- Discovered common literal plugin and dependency declarations in Groovy and Kotlin DSL.
- Distinguished direct external dependencies, platform/enforced-platform dependencies, and project dependencies.
- Resolved version-catalog library and plugin aliases for supported literal forms.
- Captured Java toolchain version and conventional Java `main`/`test` source roots.
- Projected Gradle declarations into the shared dependency inventory with configuration, module ownership, build-file provenance, test-only classification, and declared-version evidence.
- Preserved unresolved artifact versions as unresolved; no dependency version is fabricated.
- Added Gradle model tests, including dynamic-coordinate diagnostics.
- Static inspection does not execute Gradle scripts or plugins.

## Validation evidence

All three workflows completed successfully on head `86f0c86313da8b5afd18015641b4121e64dc2d8a`:

| Workflow | Run | Result |
|---|---:|---|
| Non-WPF quality gates (all non-WPF test projects and production builds) | 43086474332 | Success |
| WPF build validation | 43086473905 | Success |
| Java semantic engine spike (JDT and JavaParser, source-only and controlled external classpath) | 43086474035 | Success |

This is CI evidence for the current J1 implementation slice, not acceptance of J1 as a whole.

## Known limitations and remaining J1 work

- The Gradle resolver is a bounded static parser for common literal forms, not a complete implementation of either DSL.
- Arbitrary expressions, custom source sets, generated source roots, advanced catalog syntax, and full Gradle resolution semantics are not established by this slice. Unsupported or dynamic declarations must remain explicitly incomplete rather than being inferred.
- Expand fixture coverage for Groovy and Kotlin syntax, malformed/unreadable catalogs, multiline declarations, included-build ownership, and unsupported constructs.
- Complete Maven reactor and BOM model resolution, external parent/BOM evidence, pluginManagement separation, and profile-aware completeness.
- Complete Gradle included-build/project ownership, source-set and generated-root modelling, annotation processors, and declared-versus-resolved evidence.
- Trace and correct framework/dependency pipeline ordering so framework analysis consumes the intended structured dependency evidence without relying on path-name heuristics.

**Checkpoint decision:** proceed with J1 hardening and completion. Do not begin J2 production integration until J1 acceptance criteria are demonstrated across Maven and Gradle fixtures.
