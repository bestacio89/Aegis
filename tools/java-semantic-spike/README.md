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
for exporting the complete J1 model, and it does not yet model external artifact classpaths,
version conflicts, JPMS module paths or all Maven/Gradle scopes. Those remain J2-A work.
