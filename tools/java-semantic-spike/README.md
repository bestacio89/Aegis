# Java semantic-engine spike

This is an isolated J0 benchmark harness, not a production dependency of Aegis.

## Compared engines

- Eclipse JDT Core `3.47.0` (EPL-2.0)
- JavaParser Symbol Solver `3.28.2` (Apache-2.0 or LGPL-3.0, at adopter's choice)

Both engines run against the same checked-in Maven and Gradle Kotlin fixture corpus. The harness materializes only `.java.fixture` source files into a temporary workspace; it does not execute Maven/Gradle fixture builds, plugins, annotation processors, or project build scripts.

## Benchmark passes

The workflow runs each engine in two independent modes:

1. **Source-only:** checked-in source roots and the running JDK only.
2. **Controlled external classpath:** the same source roots plus a separately resolved, pinned dependency manifest in `classpath-pom.xml`.

The classpath manifest is deliberately separate from the fixture Maven and Gradle build files. CI resolves it with Maven's dependency plugin and passes the resulting JAR list to both engines. This avoids executing fixture build scripts and avoids accidentally placing either semantic engine's own implementation dependencies on the analysed project's classpath. The manifest currently covers Spring Context, JUnit Jupiter API and MapStruct API, including their transitives. It is a controlled representative classpath, not a claim that the fixture build models have been fully resolved.

The harness validates every classpath entry before analysis. JDT receives the entries through `ASTParser.setEnvironment`; JavaParser receives the same JARs through `JarTypeSolver`.

## Metrics and interpretation

The JSON output includes:
- discovered and successfully parsed source files;
- parser/compiler diagnostics (the engines expose different diagnostic models, so counts are not directly interchangeable);
- semantic resolution attempts and successful bindings for type references, method calls and constructor calls;
- independently tracked known cross-source type targets;
- independently tracked known third-party type targets;
- number of external JAR entries;
- elapsed analysis time;
- an approximate post-run heap snapshot, not peak memory.

The expected source-type and external-type targets are correctness spot checks, not exhaustive semantic validation. A higher aggregate binding count is not automatically better if an engine incorrectly resolves intentionally missing symbols. The corpus also includes intentionally unresolved names; those must remain unresolved.

Heap snapshots are affected by JVM allocation and GC timing. They are indicative only. Run each engine in a separate process; do not treat one timing sample as a stable performance ranking.

## Run

From the repository root with JDK 21+ and Maven installed:

```bash
mvn -B -f tools/java-semantic-spike/pom.xml compile exec:java \
  -Dexec.args="jdt tests/Aegis.Architecture.Tests/Fixtures/Java"

mvn -B -f tools/java-semantic-spike/pom.xml compile exec:java \
  -Dexec.args="javaparser tests/Aegis.Architecture.Tests/Fixtures/Java"

mvn -B -f tools/java-semantic-spike/classpath-pom.xml \
  org.apache.maven.plugins:maven-dependency-plugin:3.8.1:build-classpath \
  -DincludeScope=test -Dmdep.outputFile=/tmp/aegis-java-classpath.txt

mvn -B -f tools/java-semantic-spike/pom.xml compile exec:java \
  -Dexec.args="jdt tests/Aegis.Architecture.Tests/Fixtures/Java /tmp/aegis-java-classpath.txt"

mvn -B -f tools/java-semantic-spike/pom.xml compile exec:java \
  -Dexec.args="javaparser tests/Aegis.Architecture.Tests/Fixtures/Java /tmp/aegis-java-classpath.txt"
```

Do not select a production engine from this small corpus alone. Add larger real-world repositories, effective Maven/Gradle model resolution, module-path cases and dependency-version conflict cases before final production integration.


## Module visibility gate (J2-A)

The workflow also runs a separate module-aware visibility gate for both engines. Its
`module-visibility.properties` file is an explicit, reviewable projection of the J1
fixture model: module-owned production/test source roots, production project dependencies,
and test-only project dependencies. It does not merge source roots repository-wide.

The gate asserts four boundaries:

- A consumer with a declared project dependency resolves the API module type.
- A module without that dependency does not resolve the API module type.
- A production source set does not resolve a test-only support dependency.
- The consumer test source set does resolve that test-only support dependency.

The manifest is intentionally limited to this controlled fixture. It is not a replacement
for exporting the complete J1 model. External artifact visibility is covered separately by
the bounded J2-A2 fixture below; JPMS module paths and complete Maven/Gradle model semantics
remain open J2-A work.


## Module-owned external artifact visibility (J2-A2)

The module gate now accepts a separate resolved-classpath file for each module and source set. The J1 source-root/project-edge projection remains separate from artifact resolution: the controlled Maven fixture POMs under `classpaths/` produce the artifact snapshots used by this harness. These snapshots are test inputs, **not** a claim that J1 currently exports resolved artifacts or that Aegis resolves production dependencies.

The fixture matrix verifies:

- **Compile scope:** the consumer receives Spring Context and MapStruct on its production compile classpath.
- **Test scope:** JUnit Jupiter is available to the consumer test source set, not its production source set.
- **Runtime-only scope:** Spring JCL is present in the runtime evidence file but is not passed to either engine's compile-time semantic environment; its `Log` type must remain unresolved in production source analysis.
- **Exclusion:** the consumer fixture excludes Spring Core. The generated consumer classpath must not contain a Spring Core JAR, and its `Resource` type must remain unresolved.
- **Conflict mediation:** the conflict fixture requests Spring Context 6.2.8 and directly pins Spring Core 6.1.0. Maven's controlled fixture resolution must select exactly Spring Core 6.1.0; the harness rejects a classpath containing Spring Core 6.2.8.

JDT receives the module's compile/test JAR entries through `ASTParser.setEnvironment`; JavaParser receives the exact same entries through `JarTypeSolver`. Runtime-only entries are checked as evidence but deliberately excluded from compile-time analysis. The manifest rejects missing classpath files, missing JARs, duplicate entries, missing required artifacts and forbidden artifacts.

### Limits

This checkpoint validates module-owned, resolved Maven fixture classpaths and the visibility behaviour of the two candidate engines. It does not implement dependency resolution in Aegis, export resolved artifacts from J1, establish Gradle variant selection, model every Maven scope or Gradle configuration, or prove full transitive/exclusion semantics for arbitrary projects. The conflict fixture tests Maven's selected-version output for one controlled case; it is not a general conflict-resolution engine. Dynamic declarations, unresolved versions, unsupported constraints and incomplete build models must remain explicitly incomplete rather than being assigned guessed JARs.


## Engine-neutral semantic fact oracle (J2-A3)

The initial expected-fact corpus is maintained independently of JDT and JavaParser in semantic-facts.tsv. Its stable IDs refer to fixture sources under the SemanticFacts fixture tree and cover declarations, inheritance, generic substitution, annotations, constructor/method invocations, overload selection, method references, JPMS directives and an intentionally unresolved type.

Member identities use a language-neutral comparison convention: fully qualified owner, member name, fully qualified parameter types and return type; constructors use <init>. Generic source-level identities preserve type arguments. The corpus validator checks contract integrity and fixture references only. It does not claim that either candidate has extracted these facts.

The next checkpoint implements equivalent extraction from both ASTs against this exact oracle. A fact passes only when the expected target identity and resolution state match; aggregate AST-node counts are not used as a substitute.


## J2-A4 — Per-fact semantic extraction

The semantic fact benchmark runs JDT and JavaParser independently against the same semantic-facts.tsv oracle. Each engine is checked against the expected target identity in the fixture that owns the fact; unresolved facts must remain unresolved. Output is one JSON record per fact plus a process-level summary. CI uses --report-only so a candidate's semantic gaps remain visible in the artifact without preventing the other engine from running. Strict local mode (omit --report-only) exits non-zero on any mismatch.

Run from the repository root:

```bash
mvn -B -f tools/java-semantic-spike/pom.xml compile exec:java@semantic-fact-benchmark \
  -Dexec.args="jdt tests/Aegis.Architecture.Tests/Fixtures/Java tools/java-semantic-spike/semantic-facts.tsv"

mvn -B -f tools/java-semantic-spike/pom.xml compile exec:java@semantic-fact-benchmark \
  -Dexec.args="javaparser tests/Aegis.Architecture.Tests/Fixtures/Java tools/java-semantic-spike/semantic-facts.tsv"
```

The expanded J2-A5.1 corpus covers 17 facts across 11 categories. It includes a nested record, inherited default-interface invocation, generic overload selection, and a checked-in generated-source class consumed from production source. The generated fixture is supplied as source; annotation processors are never executed. It is intentionally an evaluation harness, not a production adapter. Passing this corpus is necessary but not sufficient for full J2-A: larger repositories, diagnostic taxonomy, generated code, JPMS module-path cases, repeatable performance measurements, licensing and worker-operational review remain required.


## J2-A5.1 — Expanded semantic corpus and diagnostic taxonomy

The semantic fact corpus now includes 17 stable facts. Generated output is represented by a checked-in source fixture under a distinct `src/generated/java` root; the benchmark consumes that output directly and does not execute annotation processors. The fixture verifies generated type discovery and constructor binding from production source. Additional facts cover nested record declarations, inherited default interface methods and generic overload selection.

The normalized diagnostic taxonomy is defined independently of either engine in `semantic-diagnostics.tsv`:

- `syntax-error`: malformed source syntax;
- `compiler-error`: syntactically valid source with a language/type/visibility error;
- `unresolved-symbol`: a symbol absent from source and declared dependency evidence;
- `incomplete-environment`: a symbol expected from a declared dependency that was deliberately omitted from the analysis classpath;
- `unsupported-construct`: a language feature newer than the configured source level;
- `warning`: non-fatal compiler information, kept separate from errors;
- `infrastructure-failure`: analysis could not be performed because an input or engine operation failed.

The diagnostic corpus validator checks stable IDs, category/severity consistency, source-level values, safe fixture paths, fixture existence and complete category coverage. It validates the diagnostic test contract; it does not yet claim that either engine has normalized its native diagnostics. Engine-specific extraction and mapping are the next J2-A5.1 implementation slice.
