# Java semantic-engine spike

This is an isolated J0 benchmark harness, not a production dependency of Aegis.

## Compared engines

- Eclipse JDT Core `3.47.0` (EPL-2.0)
- JavaParser Symbol Solver `3.28.2` (Apache-2.0 or LGPL-3.0, at adopter's choice)

Both engines run against the same checked-in Maven and Gradle Kotlin fixture corpus. The harness materializes only `.java.fixture` source files into a temporary workspace; it does not execute Maven, Gradle, plugins, annotation processors, or project build scripts.

## Metrics

The harness reports per corpus:
- discovered and successfully parsed source files;
- parser/compiler diagnostics (the engines expose different diagnostic models, so these counts are not directly interchangeable);
- semantic resolution attempts and successful bindings for type references, method calls and constructor calls;
- elapsed analysis time;
- an approximate post-run heap snapshot, not peak memory.

The initial spike configures the running JDK and checked-in source roots only. It deliberately does not resolve third-party Maven/Gradle dependencies or execute project builds. This isolates cross-source source resolution but is not a full build-classpath benchmark. The next comparison must add a separately controlled, pre-resolved dependency classpath before choosing a production engine.

These are comparative diagnostics, not a complete correctness score. Binding counts must be interpreted alongside the fixture's known resolved/unresolved cases and manual spot checks. A higher count is not automatically better if the engine incorrectly resolves intentionally missing symbols.

## Run

From the repository root with JDK 21+ and Maven installed:

```bash
mvn -B -f tools/java-semantic-spike/pom.xml compile exec:java \
  -Dexec.args="jdt tests/Aegis.Architecture.Tests/Fixtures/Java"

mvn -B -f tools/java-semantic-spike/pom.xml compile exec:java \
  -Dexec.args="javaparser tests/Aegis.Architecture.Tests/Fixtures/Java"
```

Run each engine in a separate process for comparable heap observations. CI records both outputs as artifacts. Do not select a production engine from this small corpus alone; add larger real-world projects and classpath-resolution cases before the final J0 decision.
