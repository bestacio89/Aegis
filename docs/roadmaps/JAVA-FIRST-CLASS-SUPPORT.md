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

---

# J1 implementation checkpoint — Maven, Gradle and framework hardening — 2026-11-11

**Status: validated implementation progress; J1 remains in progress and is not accepted.**

## Implemented since the previous J1 checkpoint

### Maven

- Resolve local imported BOMs against a repository-wide POM coordinate index. Imported dependency-management entries retain their source POM provenance; unresolved external BOMs remain explicit diagnostics.
- Index project coordinates once instead of repeatedly resolving every POM for each BOM import. Duplicate local coordinates produce an ambiguity diagnostic rather than arbitrary selection.
- Resolve declared reactor module paths to scanned child POM files. Preserve declared paths and separately expose resolved reactor module files; unresolved property paths, invalid paths and missing modules are diagnosed.
- Separate applied build plugins from pluginManagement declarations. Parent inheritance remains represented independently for both inventories.

### Gradle

- Exercise the Groovy DSL independently, including plugin declarations, direct/platform/project dependencies, test configurations, annotation processors, toolchains and conventional source sets.
- Harden static version-catalog parsing: diagnose unsupported multiline TOML, invalid entries, missing files/read failures and unresolved version references; support plugin version references and shorthand library coordinates with inline versions.
- Scope version catalogs to the nearest owning Gradle build root. Independent Gradle roots may reuse the same alias without cross-project coordinate or version leakage.
- Keep unsupported declarations explicitly incomplete; do not execute Gradle scripts or plugins.

### Framework evidence

- Move dependency discovery before framework classification so structured build evidence is available at the correct pipeline stage.
- Classify Spring Boot, Spring Framework, Quarkus, Micronaut, Jakarta EE and Hibernate from module-owned Maven/Gradle coordinates.
- Remove the Java framework path-name heuristic. Preserve module-level framework signals and select a deterministic primary framework identity.
- Java module classification is independent of the repository's primary language, so mixed-language repositories do not lose Java framework evidence.

## Validation evidence

All three workflows passed on head 2aea9e017332291f407651ade5102ed2c0879206:

| Workflow | Run | Result |
|---|---:|---|
| Non-WPF quality gates — all non-WPF tests and production builds | 43090498831 | Success |
| WPF build validation | 43090498963 | Success |
| Java semantic engine spike — JDT and JavaParser, source-only and controlled external classpath | 43090498908 | Success |

The test suite includes regression coverage for:
- Maven local BOM import, managed version and source provenance.
- Maven applied plugins versus pluginManagement.
- Maven reactor module resolution and missing module diagnostics.
- Framework classification from dependency coordinates rather than path names.
- Gradle Groovy DSL declarations.
- Unsupported multiline catalog syntax and plugin version references.
- Independent Gradle build roots with colliding catalog aliases.

This is validation of the current implementation, **not J1 acceptance**.

## Remaining J1 work

- Generated source roots and their production/test ownership for Maven and Gradle.
- Full included-build membership and ownership modelling, beyond root discovery and build-scoped catalog selection.
- External Maven parent/BOM resolution semantics and richer property inheritance. Remote repositories remain outside static inspection.
- Maven profile-aware effective models; inactive/conditional profile content must remain explicit rather than guessed.
- Gradle custom source sets, generated roots, advanced catalog constraints and additional DSL constructs.
- Final declared-versus-resolved dependency model review and broad representative fixture coverage.
- Re-run the full Maven/Gradle acceptance matrix and unchanged .NET regression suite after the remaining work.

**Checkpoint decision:** continue J1. J2 production semantic-engine integration remains blocked until the J1 acceptance criteria are demonstrated.


---

# J1-A implementation checkpoint — generated source roots — 2026-11-11

**Status: implementation committed; validation is pending. J1-A is not accepted.**

## Implemented

- Added a language-neutral ProjectSourceRoot evidence model with explicit Production, Test and Unknown ownership.
- Source-root origin is retained as Conventional, Explicit, GeneratedConvention or Observed.
- Source-root evidence records whether Java files were observed and the build/source manifest that supplied the evidence.
- Maven discovery now models conventional src/main/java and src/test/java, literal custom sourceDirectory and testSourceDirectory declarations, and observed target/generated-sources / target/generated-test-sources roots.
- Gradle discovery now models conventional Java source sets and observed build/generated/sources roots. A generated root is assigned to a source set only when the standard Gradle generator/language/source-set path positions provide evidence.
- Ambiguous Gradle generated roots remain visible with Unknown ownership and a coverage-affecting diagnostic; package names are not used to infer source-set ownership.
- Each module exposes the same source-root evidence as its Maven or Gradle build model. Build scripts and plugins remain unexecuted.

## Automated coverage added

- Maven conventional production/test roots.
- Maven generated production/test roots.
- Gradle conventional production roots.
- Gradle generated production/test roots using the standard generated-source layout.
- Gradle generated roots without source-set evidence remain Unknown and produce a diagnostic.
- Module-level and build-model source-root projections are asserted to remain consistent.

## Known limitations

- Maven custom roots are currently limited to literal sourceDirectory and testSourceDirectory values; dynamic property expressions remain unresolved.
- Maven generated-root discovery is based on observed conventional target/generated-sources and target/generated-test-sources paths. Build-helper plugin executions and arbitrary generator output directories are not interpreted yet.
- Gradle custom sourceSets DSL expressions are not evaluated. Unsupported custom source directories remain explicitly incomplete.
- Gradle generated-root ownership is only assigned for the supported conventional generated path layout. Other layouts remain Unknown.
- Generated roots are evidence inventory at this stage; this work does not yet change every downstream evaluator's source-file inclusion policy. J1-A must verify production/test consumers before acceptance.

## Validation

Implementation and focused tests have been pushed to the existing Java support branch. Final-head non-WPF, WPF and semantic-engine workflows must complete successfully before this checkpoint can be accepted.

**Checkpoint decision:** continue J1-A validation and hardening. J1-B and J2 remain blocked.


---

# J1-B — Gradle included-build membership and ownership — 2026-11-11

**Status: accepted.**

- Included builds are modelled as independent Gradle roots, not as ordinary subprojects of the including build.
- Literal includeBuild paths are resolved relative to the owning settings root and constrained to the analysis root.
- Membership is associated with independently discovered Gradle roots/modules; parent build project graphs do not absorb included-build descendants.
- Shared included builds retain distinct membership evidence when referenced by multiple parent builds.
- Missing, inaccessible, dynamic, external or undiscovered included-build targets remain diagnostic/incomplete evidence.
- Version-catalog and source-root ownership remain scoped to the correct Gradle root.

Automated coverage verifies independent roots, shared membership, missing targets, analysis-root boundaries and prevention of parent-project leakage.

All mandatory workflows passed on head `a602f0287aee97534565aba79b2f3af7b95b4b26`:
- Non-WPF quality gates — success.
- WPF build validation — success.
- Java semantic engine comparison — success.

**Decision:** J1-B accepted.

# J1-C — Maven parent, BOM and profile semantics — 2026-11-11

**Status: accepted.**

- Local parent POM inheritance validates declared parent coordinates against the candidate POM before inheriting.
- Parent resolution distinguishes not declared, external declared, locally resolved, locally incomplete and local-coordinate mismatch states.
- Local imported BOMs are resolved from the scanned POM coordinate index; external/unavailable BOM imports remain explicit evidence and diagnostics.
- Maven profile declarations retain conditional properties, dependencies, managed dependencies, plugins, managed plugins and activation metadata.
- Profiles are not implicitly activated or merged into the unconditional model.
- Incomplete inheritance remains explicit; no remote repository lookup, Maven invocation or plugin execution occurs.

Automated coverage verifies parent-coordinate mismatch and profile isolation in addition to the earlier Maven reactor, dependency-management and BOM fixtures.

All mandatory workflows passed on head `90f30fcfab6006c545723bbdfaf7fa3993d03a27`:
- Non-WPF quality gates — success.
- WPF build validation — success.
- Java semantic engine comparison — success.

**Decision:** J1-C accepted.

# J1-D1 — Gradle custom source-set ownership — 2026-11-11

**Status: accepted.**

- Supported literal custom source-set declarations are projected into module-owned Java source roots.
- Relative source directories are resolved against the owning Gradle project.
- Production, test, generated and unknown ownership remain distinct.
- Dynamic or ambiguous source-set declarations remain incomplete and diagnostic; ownership is not guessed.
- Included-build boundaries are preserved.

Automated coverage includes literal Kotlin/Groovy custom source sets, nested Java blocks, file notation and dynamic-directory negative cases.

All mandatory workflows passed on head `6001772697d9a06732b337ba3a7eedf50267017d`:
- Non-WPF quality gates — success.
- WPF build validation — success.
- Java semantic engine comparison — success.

**Decision:** J1-D1 accepted.

# J1-D2 — Advanced Gradle version catalogs — 2026-11-11

**Status: accepted.**

- Static `libs.bundles.*` declarations expand into their constituent library declarations.
- Literal `version.ref` references are resolved for libraries and plugins.
- Supported rich version declarations extract the strongest static version value in this order: `strictly`, `require`, then `prefer`.
- Missing bundle members, missing version references and unsupported declarations produce diagnostics and incomplete evidence.
- Catalog resolution remains isolated to the owning Gradle build root.

The current shared dependency contract retains the selected declared version string, not the complete Gradle rich-version constraint language. In particular, all reject/rejectAll and conflict-resolution semantics are not represented as a first-class resolved constraint model. No artifact resolution is claimed.

All mandatory workflows passed on head `d64108e348bba560bd336474715b90d6964a346e`:
- Non-WPF quality gates — run `43116603281`, success.
- WPF build validation — run `43116603294`, success.
- Java semantic engine comparison — run `43116603638`, success.

**Decision:** J1-D2 accepted within the documented static-catalog scope.

# J1-E — Dependency contract and fixture matrix — 2026-11-12

**Status: accepted.**

- Gradle project dependencies are emitted as project-kind dependency evidence, not dropped or represented as external packages.
- Enforced-platform declarations retain their enforcement flag through the additive `ArchitectureDependencyEvidence.IsEnforced` property.
- Maven annotation-processor paths are projected into normalized dependency evidence with declaring-POM provenance and inherited/direct ownership.
- Gradle test-only classification uses configuration-name segments, preventing false positives from arbitrary substrings.
- Maven fixtures cover compile, provided, system and test scopes plus optional/type/classifier metadata and inherited processor provenance.
- Gradle fixtures cover API, compile-only, runtime-only, test-runtime, project, enforced-platform and custom-configuration declarations.
- Declared versions remain separate from resolved artifact versions; the static inventory does not claim remote artifact resolution.

All mandatory workflows passed on final J1-E head `67813b74aa345fd62ccdd75776b09c189fbdff2b`:
- Non-WPF quality gates — run `43118586301`, success.
- WPF build validation — run `43118586278`, success.
- Java semantic engine comparison — run `43118586393`, success.

**Decision:** J1-E accepted.

# J1-F — Final regression and acceptance review — 2026-11-12

**Review result: accepted.**

## Acceptance matrix

| J1 acceptance criterion | Evidence and result |
|---|---|
| Single- and multi-module Maven projects | Maven reactor paths and module ownership are retained; multi-module fixture corpus and model tests pass. |
| Gradle multi-project and included-build boundaries | Settings/project discovery and independent included-build membership are tested; parent/child ownership leakage is guarded against. |
| Declared and resolved dependency distinction | Declared versions and resolution status are explicit. Static discovery does not invent resolved artifact versions; `ResolvedVersion` remains null without resolution evidence. |
| Dependency scopes/configurations | Maven scopes and Gradle configurations are retained; production/test ownership is separately classified. |
| Incomplete or unsupported model evidence | Maven parent/BOM/profile and Gradle dynamic/catalog/source-set limitations produce diagnostics and incomplete model state rather than fabricated effective values. |
| Plugins and annotation processors | Maven applied plugins and pluginManagement are distinct; Maven and Gradle annotation-processor declarations are retained in their build models, with Maven processor evidence projected into the shared inventory. |
| Source roots and generated-source ownership | Conventional, supported custom and generated roots are represented with production/test/unknown ownership and source provenance. Ambiguous ownership remains unknown. |
| Framework evidence | Framework classification consumes module-owned dependency coordinates; the Java path-name heuristic has been removed. |
| Regression compatibility | Mandatory non-WPF test/production build, WPF build and Java semantic-engine workflows passed together on the final J1-E implementation head. |

## Explicit boundaries and known limitations

J1 delivers bounded, static build intelligence. It does **not** claim to implement a complete Maven effective model or a complete Groovy/Kotlin Gradle interpreter.

- External Maven parents and BOMs are not fetched. Their declared-but-unavailable status remains explicit.
- Maven profiles are represented as conditional evidence; profile activation is not evaluated or implicitly merged.
- Maven custom/generated source roots are limited to supported literal and conventional evidence. Arbitrary build-helper executions and generator configuration are not executed.
- Gradle dynamic expressions and unsupported DSL/catalog constructs are not evaluated. They remain diagnostic/incomplete.
- Gradle custom source sets and generated-source ownership are supported only for the statically recognised forms and layouts.
- Gradle rich-version extraction retains the strongest supported literal version value, not the full reject/rejectAll and conflict-resolution model.
- No Maven/Gradle scripts, plugins, annotation processors or remote artifact resolution are executed during ordinary analysis.

These are documented scope boundaries, not claims of complete effective-build resolution. They must remain visible in diagnostics/documentation and must not be represented as successful artifact resolution.

## Final J1-F validation

The final regression review was performed against implementation head `67813b74aa345fd62ccdd75776b09c189fbdff2b`. All three mandatory workflows passed on that same head:

- Non-WPF quality gates — run `43118586301`, success.
- WPF build validation — run `43118586278`, success.
- Java semantic engine comparison — run `43118586393`, success.

The roadmap update and acceptance matrix passed all mandatory workflows on final documentation head `e20832ebed3a64a9caac55a23a78183cb522ae38`.

**Checkpoint decision:** J1 implementation and regression criteria are satisfied. J1-F is formally accepted. J1 is complete and J2 is unblocked.


# J2-A — Semantic-engine evaluation audit — 2026-11-12

**Status: evaluation design in progress; production engine selection is not accepted.**

The existing J0 spike is a useful smoke test, but it is not sufficient to select a production engine. The archived 15-file run demonstrates that both candidates can parse the fixture corpus and resolve selected source/external symbols. It does not establish comparative semantic accuracy or production suitability.

## Confirmed benchmark limitations

- The harness combines source roots from all fixture modules into one resolution environment. This can expose declarations to modules that do not have a declared project dependency.
- The controlled external classpath is global rather than module-owned; Maven/Gradle scope, test visibility, exclusions, dependency conflicts and module-path behaviour are not modelled per module.
- JavaParser visits `ClassOrInterfaceType` while JDT visits `SimpleType`; their aggregate binding attempts and ratios are not equivalent measures.
- JavaParser parse problems and JDT compiler `IProblem` errors are counted together as if they were the same diagnostic category.
- Five source-type and three external-type names are narrow spot checks, not a semantic fact matrix.
- One process run and a post-run heap snapshot do not establish repeatable performance or peak memory.

## Archived smoke-test observations

On the 15-file fixture corpus:
- JavaParser parsed 15/15 files and resolved 5/5 named source-type spot checks; with the controlled classpath it resolved 3/3 named external-type spot checks.
- JDT parsed 15/15 files and resolved 3/5 named source-type spot checks; with the controlled classpath it resolved 3/3 named external-type spot checks.
- JavaParser reported zero parse problems in this run; JDT reported 22 source-only and 9 classpath-mode compiler errors. These diagnostic categories are not directly comparable.
- JavaParser elapsed times were 295/304 ms (source-only/classpath); JDT elapsed times were 770/860 ms. These are single-run observations without controlled warm-up, repeated trials, GC controls or peak-memory measurement.
- Neither engine resolved the deliberately missing type names.

These results are not a production engine ranking. In particular, aggregate resolution ratios and elapsed times must not be used to choose a winner.

## Required J2-A work

1. **Module-scoped inputs:** derive a separate source/classpath/module-path view for every module from J1 module, source-root and dependency evidence. Do not expose all repository source roots or all external artifacts to every module.
2. **Visibility correctness:** add positive and negative cross-module tests. A module with a declared project dependency must resolve the target; a module without that dependency must not. Test-only dependencies and test source roots must not leak into production analysis.
3. **Semantic fact oracle:** define engine-neutral expected facts with stable fact IDs and expected source/target identities. Cover declarations, inheritance, implementation, generic substitutions, overload selection, calls, constructor references, annotations, visibility, module declarations, generated code and unresolved symbols.
4. **Equivalent extraction coverage:** make JDT and JavaParser inspect equivalent semantic constructs. Report per-fact outcomes and unsupported cases; do not compare raw node counts from different AST categories.
5. **Diagnostic taxonomy:** separate syntax/parse errors, compiler diagnostics, unresolved bindings, unsupported constructs, incomplete environment and infrastructure failures.
6. **Representative corpus:** extend beyond the current fixtures to realistic Maven/Gradle modules, named modules, dependency conflicts, generated sources, partial projects and larger repositories.
7. **Measurement protocol:** use fresh worker processes, controlled warm-up and repeated trials. Report distributions (median and dispersion) and define heap/RSS measurement semantics. Do not use one post-run heap snapshot as peak memory.
8. **Operational and legal review:** compare worker lifecycle, cancellation, timeout/crash isolation, concurrency, JVM/runtime prerequisites, packaging, dependency update burden and licensing/redistribution obligations for JDT, JavaParser and Spoon.

## Architecture constraints

- Keep parser/compiler types behind the language adapter boundary; no Java AST or binding type may enter `Aegis.Shared`.
- Use the J1 build model as the source of module ownership and declared dependency evidence. Do not independently guess classpaths in the semantic engine.
- Ordinary analysis remains static and must not execute Maven/Gradle scripts, plugins or annotation processors.
- Declared dependencies are not resolved artifacts. Any optional artifact-resolution phase must be separately authorized and report incomplete evidence.
- Evaluate an isolated JVM worker as a candidate, not as a foregone conclusion. The .NET orchestration contract must remain language-neutral and versioned.

## Acceptance gate

J2-A is accepted only when:
- the module-aware benchmark proves both allowed and forbidden cross-module visibility;
- the same semantic fact oracle is exercised by each candidate;
- diagnostic categories and incomplete environments are explicit;
- representative corpus and repeated measurement results are available;
- legal and operational trade-offs are documented;
- a production recommendation is supported by evidence and known limitations.

**Decision:** engine selection remains open. J2-B production integration is blocked until J2-A is accepted.


# J2-A1 — Module-scoped semantic visibility — 2026-11-12

**Status: accepted (bounded implementation checkpoint).** This is not full J2-A acceptance and does not authorize production engine selection or J2-B integration.

The isolated JDT and JavaParser benchmark now receives module-owned production/test source roots and explicit project dependency edges from a controlled J1 fixture projection. Both engines pass the same four visibility assertions:
1. Declared consumer-to-API dependency resolves.
2. Undeclared isolated-to-API reference remains unresolved.
3. Production code cannot resolve a test-only support dependency.
4. Test code can resolve its declared test-only support dependency.

Each engine passed 4/4 expected facts across 11 observations (7 resolved, 4 unresolved). This is fixture gate evidence, not a cross-engine accuracy ranking.

All three mandatory workflows passed on `021feeeb0998a4d3c65ef3e2236183d88ffc2553`:
- Java semantic engine spike — run `43174315755`, success.
- Non-WPF quality gates — run `43174315694`, success.
- WPF build validation — run `43174315692`, success.

**Decision:** J2-A1 accepted. J2-A continues with module-owned external artifact classpaths, dependency scopes/exclusions/conflicts, JPMS module-path behaviour, broader semantic fact coverage, representative corpora, repeatable performance measurement and operational/legal review. J2-B remains blocked.


# J2-A2 — Module-owned external artifact visibility — 2026-11-12

**Status: accepted (bounded implementation checkpoint).** This does not complete J2-A, select a production engine or authorize J2-B.

The isolated module-visibility harness now loads explicit module-owned compile, test and runtime classpath snapshots. The controlled snapshots are generated from pinned Maven fixture POMs; they are not claimed as J1 output or as production dependency resolution by Aegis.

Both Eclipse JDT and JavaParser passed all 12 expected visibility assertions across 21 observations per engine:

1. Consumer compile dependencies (Spring Context and MapStruct) resolve.
2. Test-only JUnit API resolves in the consumer test source set and remains unavailable to production sources.
3. Runtime-only Spring JCL is recorded in runtime evidence but is not exposed to compile-time analysis.
4. An excluded Spring Core artifact is absent; its Resource type remains unresolved.
5. The conflict fixture selects Spring Core 6.1.0 and rejects Spring Core 6.2.8.
6. Existing positive/negative project-module and production/test source-root visibility assertions remain enforced.

The JDT and JavaParser adapters receive the same module/source-set artifact entries. The gate fails on missing classpath snapshots, missing JARs, duplicate entries, missing required artifacts or forbidden artifacts.

### J2-A2 CI evidence

All three mandatory workflows passed on implementation head 3f1824e80d760b5f636466dab8cc8303f43161a9:
- Java semantic engine spike — run 43179049509, success.
- Non-WPF quality gates — run 43179049510, success.
- WPF build validation — run 43179049489, success.

### Boundaries retained

This is controlled Maven-fixture evidence only. It does not implement dependency resolution in Aegis, export resolved artifacts from J1, establish Gradle variant selection, model every Maven scope or Gradle configuration, prove arbitrary transitive/exclusion semantics, or cover JPMS module-path visibility. Conflict mediation is verified for one pinned fixture, not implemented as a general resolver.

**Decision:** J2-A2 accepted. Continue J2-A with an engine-neutral semantic fact oracle and equivalent extraction coverage. Full J2-A remains in progress; J2-B remains blocked.


# J2-A3 — Engine-neutral semantic fact oracle — in progress

The next bounded checkpoint defines stable semantic fact identities before either parser-specific extractor is implemented. The corpus is stored in tools/java-semantic-spike/semantic-facts.tsv and uses fixture sources under SemanticFacts/.

The initial oracle covers 11 fact IDs and 11 categories: type declaration, inheritance, generic supertype substitution, annotation, constructor invocation, method invocation, overload selection, method reference, module requires, module exports and an intentionally unresolved type.

Each row records a stable fact ID, category, module, source set, fixture path, source identity, expected target identity and expected resolution state. Type identities are fully qualified. Member identities use owner#member(parameter-types):return-type; constructors use <init>. Generic source-level identities retain their type arguments. An unresolved fact names the expected qualified symbol and explicitly expects no binding.

A standalone validator checks schema, stable-ID uniqueness, supported fact categories, required identities, valid source-set values, fixture containment/existence and required category coverage. It does not claim that either engine has extracted or resolved the facts.

**Acceptance boundary:** J2-A3 is accepted only when the corpus validator passes in CI and the oracle contract is reviewed. Equivalent JDT/JavaParser extraction and per-fact identity comparison are the separate J2-A4 checkpoint. Full J2-A and J2-B remain blocked.


# J2-A3 — Engine-neutral semantic fact oracle — 2026-11-12

**Status: accepted (bounded corpus/contract checkpoint).** This accepts the oracle definition and integrity gate only. It does not claim that either engine extracts the facts, and it does not complete J2-A or authorize J2-B.

The stable-ID corpus contains 11 facts across 11 required categories: type declaration, inheritance, generic supertype, annotation, constructor invocation, method invocation, overload selection, method reference, JPMS requires, JPMS exports and unresolved type. Each fact binds a source identity to an expected target identity and resolution state, with module/source-set and fixture provenance.

The corpus validator passed. It enforces the eight-column TSV contract, stable unique JFACT-NNN IDs, required category coverage, non-empty identities, supported source sets and fixture path containment/existence.

### J2-A3 CI evidence

All mandatory workflows passed on implementation head 3549fdc25161102d50a23d7542aff495e4ce5e33:
- Java semantic engine spike — run 43180514181, success; includes the corpus validator and both J2-A1/J2-A2 engine visibility gates.
- Non-WPF quality gates — run 43180514116, success.
- WPF build validation — run 43180514089, success.

**Decision:** J2-A3 accepted. J2-A4 now owns equivalent JDT/JavaParser extraction and per-fact target-identity comparison. Full J2-A remains in progress; J2-B remains blocked.


# J2-A4 — Equivalent semantic fact extraction — accepted comparative checkpoint (2026-11-12)

**Objective:** Execute the same J2-A3 semantic fact oracle independently through Eclipse JDT and JavaParser, compare expected target identities and resolution states per fact, and expose capability differences without weakening the oracle.

The isolated SemanticFactBenchmark materializes the complete bounded SemanticFacts fixture module, including supporting source files needed for cross-source binding. Both engines receive the same module-owned source boundary and the same 12 fact definitions. The harness emits per-fact JSON evidence. Strict local mode exits non-zero on mismatches; CI uses report-only mode so both candidates always produce evidence and a candidate mismatch does not suppress the second engine's result.

The corpus covers 12 facts across 11 categories, including record declarations and inheritance, generic supertypes, annotations, constructor and method invocations, overload selection, constructor method references, JPMS requires/exports, unresolved symbols and the implicit record-component accessor.

### Observed comparison

| Candidate | Expected facts resolved correctly | Mismatches |
|---|---:|---:|
| Eclipse JDT Core 3.47.0 | 12/12 | 0 |
| JavaParser 3.28.2 + Symbol Solver | 9/12 | 3 |

JavaParser's three observed gaps concern implicit Java record members:
1. Binding the implicit canonical Customer record constructor invocation.
2. Resolving the implicit record-component accessor Customer.id().
3. Resolving Customer::new as a constructor method reference to the implicit canonical record constructor.

JDT resolved all three, as well as the other nine facts. The overload-selection fact explicitly calls OverloadProbe.select(String) so it tests overload binding independently of the implicit record accessor; the accessor remains a separate oracle fact. The oracle was not weakened, and the implicit members were not made explicit in the fixture to conceal a candidate limitation.

### CI evidence

All mandatory workflows passed on implementation head `4575ec46bec25464dd83300ed5abb114438f9bcf`:
- Java semantic engine spike — run `43184106145`, success.
- Non-WPF quality gates — run `43184106079`, success; all non-WPF test projects and production builds completed successfully.
- WPF build validation — run `43184106137`, success.

**Decision:** J2-A4 is accepted as a comparative-evaluation checkpoint because both candidates were evaluated against the same oracle, all mismatches were retained and explained, and the mandatory workflows passed. Acceptance does not mean candidate parity. Eclipse JDT is the preferred candidate and the working selection for the next evaluation stage because it resolved 12/12 facts, while JavaParser left three implicit-record-member relationships unresolved. This is a provisional engineering decision, not yet final production-engine approval.

J2-A5 must broaden the corpus and assess generated/partial sources, representative repositories, diagnostic taxonomy, JPMS module-path behaviour, repeatable performance and memory, licensing, packaging, worker lifecycle, cancellation, timeouts, crash isolation and concurrency. JDT must remain the preferred candidate unless that broader evidence identifies a material correctness, performance, operational or legal disadvantage. No production parser dependency is added in J2-A4. Full J2-A remains in progress; J2-B remains blocked pending J2-A completion and final engine approval.


# J2-A5 — Broader semantic-engine evaluation — in progress (2026-11-12)

**Objective:** Determine whether Eclipse JDT remains the most correct, efficient and operationally viable semantic engine under representative Java project conditions. JDT is the provisional preferred candidate after J2-A4; this phase must test that preference rather than assume it.

### J2-A5.1 — Representative corpus and diagnostic taxonomy

- Expand the semantic corpus beyond the current single-module fixture.
- Cover checked-in generated-source output without executing annotation processors; incomplete and partially available source; malformed syntax; unresolved symbols; and incomplete dependency environments.
- Add semantic cases for sealed hierarchies, nested and generic types, wildcard bounds, lambdas and method references, default interface methods, and overloaded generic calls where those cases exercise distinct binding behaviour.
- Define normalized diagnostic categories: syntax error, compiler error, unresolved symbol, unsupported construct, incomplete analysis environment, and infrastructure failure. Preserve warnings separately from errors and retain engine-native code/message/location as provenance.
- Distinguish a missing binding caused by a genuinely absent symbol from one caused by an incomplete classpath, unsupported construct, parser recovery or engine failure.
- Keep stable fact IDs and expected target identities. Do not weaken the oracle to improve candidate scores.

**Acceptance:** Both candidates are exercised against the same expanded oracle where their capabilities permit; JDT's expected semantic facts and diagnostic classification are explicitly asserted. Candidate-specific limitations remain reported rather than suppressed. Fixture build scripts and annotation processors are never executed.

### J2-A5.2 — Repeatable performance and memory

- Execute each candidate in independent JVM processes.
- Separate process startup from analysis duration.
- Use explicit warm-up and repeated measured trials.
- Report sample count, median and p95, source/file/fact counts, runtime/JDK/environment, exit status and failures.
- Measure peak resident memory at the process boundary; post-run heap snapshots are not peak-memory measurements.
- Keep performance results descriptive and reproducible. Do not introduce unstable wall-clock CI thresholds.

**Acceptance:** The benchmark produces machine-readable per-trial data and a reproducible summary for both candidates under identical inputs and resource limits.

### J2-A5.3 — Operational, deployment and legal review

Assess JDT dependency closure and licensing, packaging, JVM worker lifecycle, protocol/versioning, cancellation, deadlines, crash isolation, concurrency, resource limits and deployment footprint. Compare in-process and worker-process boundaries using evidence. Do not assume that a JVM worker is mandatory or that embedding the engine is operationally safe.

Ordinary analysis must not execute arbitrary Maven/Gradle scripts, plugins or annotation processors. Any generated-source analysis must consume explicitly supplied or safely discovered generated files and retain provenance.

**Acceptance:** A documented operational decision identifies the chosen process boundary, supported runtime, dependency/licensing obligations, failure semantics and resource controls.

### J2-A5.4 — Candidate recommendation

Consolidate correctness, diagnostic quality, performance, memory, operational complexity and licensing evidence. JDT remains the preferred candidate unless the broader evidence demonstrates a material correctness, performance, operational or legal disadvantage.

**Acceptance:** An evidence-backed recommendation is reviewed and recorded. Final production-engine approval is separate from the provisional preference. No production parser dependency is introduced before this decision.

**Current decision:** J2-A4 accepted as a comparative checkpoint. J2-A5 in progress, starting with J2-A5.1. Full J2-A remains open; J2-B remains blocked.


## J2-A5.1 progress — semantic corpus and diagnostic contract (2026-11-12)

**Bounded result:** The semantic corpus expansion is validated. The oracle now contains 17 facts and includes a nested record, inherited default-interface method invocation, generic overload selection, and a checked-in generated-source class under a distinct `src/generated/java` root. Production source references the generated class; no annotation processor or project build script is executed.

The generated-source constructor is explicitly declared so the generated-source visibility fact is independent of JavaParser's known implicit record-constructor limitation.

| Candidate | Facts resolved correctly | Mismatches |
|---|---:|---:|
| Eclipse JDT Core 3.47.0 | 17/17 | 0 |
| JavaParser 3.28.2 + Symbol Solver | 14/17 | 3 |

The same three JavaParser mismatches remain: implicit canonical record constructor binding, implicit record-component accessor binding, and constructor method-reference binding to the implicit canonical record constructor. The five added facts did not create additional candidate gaps.

A separate diagnostic scenario corpus now defines seven categories: syntax error, compiler/type error, unresolved symbol, incomplete declared dependency environment, unsupported construct at a lower configured source level, warning, and infrastructure failure. Its validator checks stable IDs, category/severity consistency, source-level values, fixture containment/existence and complete category coverage. The semantic fact validator's empty-category JSON output was also corrected.

**Boundary:** The diagnostic corpus is currently a validated engine-neutral contract. Candidate-specific diagnostic extraction and normalization are not yet implemented; a green validator does not mean that either engine has passed diagnostic classification.

### CI evidence

All mandatory workflows passed on head `5c91f92f2d7f104e3b955944c5deebb4a0f10df7`:
- Java semantic engine spike — run `43188405804`, success; validates both corpora, runs both semantic-fact candidates, and runs source/classpath/module-visibility checks.
- Non-WPF quality gates — run `43188405806`, success.
- WPF build validation — run `43188405812`, success.

**Decision:** Accept the expanded semantic-fact corpus sub-checkpoint. Keep J2-A5.1 in progress until engine-native diagnostic extraction and normalized category mapping are implemented and asserted. JDT remains the preferred candidate. J2-B remains blocked.


### J2-A5.1 — Diagnostic evidence normalization hardening

The first diagnostic comparison implementation was reviewed and found to contain a classification tautology: expected categories were reused when constructing observed results. The correction now derives categories from native engine evidence and explicit scenario context, and validates category plus severity. The unresolved-symbol and incomplete-environment cases carry separate qualified-symbol and declared-artifact evidence; the latter is classified only when the unresolved diagnostic matches the imported symbol and the declared artifact is explicitly known to be omitted.

Acceptance remains pending strict JDT comparison, report-only JavaParser comparison, inspection of native diagnostic IDs/messages and all three mandatory CI workflows on the corrected head. A green corpus-schema validator alone is not diagnostic-normalization evidence.


### J2-A5.1 diagnostic normalization — measured result (2026-11-12)

The corrected benchmark derives observed categories from native engine diagnostics/resolution failures plus explicit project-model evidence. It compares normalized category and severity; expected categories are not copied into observations.

| Candidate | Diagnostic scenarios matched | Capability gaps |
|---|---:|---|
| Eclipse JDT Core 3.47.0 | 7/7 | None in this bounded corpus |
| JavaParser 3.28.2 + Symbol Solver | 5/7 | Compiler/type-check diagnostics and deprecation warnings are not provided by its parser/symbol-solver API |

Both candidates distinguish an ordinary unresolved symbol from a declared dependency omitted from the analysis classpath. JDT reports the omitted Spring dependency at the import declaration (ImportNotFound); normalization correlates that native evidence with the qualified imported symbol and declared-artifact metadata. Both candidates also report the Java 21 record-pattern construct as unsupported at configured source level 17. JavaParser's absent compiler-error and warning diagnostics are retained as explicit capability gaps in report-only mode, not counted as successful matches.

The infrastructure scenario is a controlled missing-input read failure. The corpus validator checks scenario integrity; the benchmark independently exercises engine observations. Native problem codes/messages and source-line evidence are retained in the per-case output.

**Sub-checkpoint decision:** Accept diagnostic normalization for the current bounded corpus. JDT has complete coverage (7/7); JavaParser has two known capability gaps (5/7). This adds evidence in favour of JDT, but does not by itself constitute production approval. J2-A5.1 remains open for broader diagnostic cases and representative project validation; J2-A5.2 performance/memory and J2-A5.3 operational/legal review remain outstanding. J2-B remains blocked.

**CI evidence on corrected implementation head** 139ec582689f90d8303ac35ebb754bd14ccff578:
- Java semantic engine spike — run 43191794838, success.
- Non-WPF quality gates — run 43191794907, success.
- WPF build validation — run 43191794884, success.




### J2-A5.2 — Repeatable performance and memory protocol implemented (2026-11-12)

The isolated performance harness launches each candidate in a fresh JVM with identical fixed heap/GC arguments, the same harness/runtime classpath and identical fixture inputs. It runs source-only and controlled-external-classpath modes separately and alternates candidate order between trials.

The CI protocol uses two warm-up processes and seven measured processes per engine/mode. Warm-ups are excluded from summary statistics. Per-trial JSONL captures analysis duration, process duration including JVM startup/shutdown, peak resident set size sampled from Linux /proc VmHWM, source/parsed file counts, diagnostics, resolution attempts, resolved bindings and the unresolved-symbol guard. Environment metadata records JDK, OS, architecture, processor count and JVM arguments. Summaries report sample count, median and nearest-rank p95 for analysis time, process time and peak RSS.

The harness rejects non-Linux execution rather than substituting heap occupancy for process RSS. CI timing remains observational; no flaky wall-clock threshold is introduced.

**Acceptance remains pending:** run the protocol on the PR head, inspect all candidate/mode trial records and confirm comparable successful workloads. Seven samples on the bounded fixture corpus are not a production-scale performance conclusion. Larger representative repositories and workload scaling remain necessary. J2-A5.2 is implementation-complete but not yet accepted; J2-A5.3 and final J2-A approval remain outstanding. J2-B remains blocked.
