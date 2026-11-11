# Java semantic-analysis fixture corpus

This corpus is intentionally stored with a `.fixture` suffix so Maven and Gradle do not mistake the test fixtures for executable projects when scanning the Aegis repository itself. The corpus manifest maps each fixture to its real build-file role.

## Corpora

- **MavenMultiModule**: Maven reactor with inherited parent metadata, BOM-managed versions, multiple modules, production/test scopes, exclusions, compiler annotation processors, generated sources, service loading, module metadata, and unresolved external references.
- **GradleKotlinMultiProject**: Kotlin DSL multi-project build with plugin management, an included build, version catalog, Java toolchain, custom configurations, source sets, generated sources, test-only dependencies, and unresolved external references.

Both corpora include Java constructs needed to compare parser and symbol-resolution engines: records, generic types, interfaces, inheritance, annotations, constructor/method calls, dependency injection, reflection, service loading, and deliberately unresolved references.

The corpus is an input and regression baseline for J0 engine evaluation. It is not a claim that current Aegis detectors can yet resolve these constructs. Build execution is not required to inspect these fixtures.
