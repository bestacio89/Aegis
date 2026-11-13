# Java semantic worker

The worker provides compiler-binding Java analysis through Eclipse JDT Core 3.47.0. It runs out of process using the versioned JSON Lines protocol and receives a module-scoped source/classpath snapshot from the .NET host.

## Analyze request payload

```json
{
  "projectPath": "/workspace/service",
  "workspaceRoot": "/workspace",
  "sourceRoots": ["/workspace/service/src/main/java"],
  "referencedSourceRoots": ["/workspace/api/src/main/java"],
  "sourceFiles": ["/workspace/service/src/main/java/example/Service.java"],
  "classpathEntries": ["/workspace/.m2/repository/example/api.jar"],
  "sourceLevel": "21",
  "includeTestCode": false,
  "includeGeneratedCode": false
}
```

All analyzed source files must belong to a module-owned source root, and those roots must remain inside the declared module path. Referenced source roots are binding-only inputs from the transitive closure of explicitly declared project dependencies; they must remain inside the workspace boundary and are never added to the analyzed-file inventory. The .NET host excludes test-scoped project dependencies from the referenced-source-root set for production modules; test source-set ownership remains explicit in the analysis request. Classpath entries are explicit inputs; the worker never invokes a build tool, plugin, annotation processor or project code.

## Result

The response uses the numeric values of the shared Aegis contracts for symbol kinds, relationship kinds, resolution status, analysis status, symbol origin and evidence source. It returns symbols, relationships and native JDT compiler diagnostics with file/line/column provenance. Unresolved references remain explicit relationships with no fabricated target identity.

## Build and test

    mvn --batch-mode --no-transfer-progress -f tools/java-worker-prototype/pom.xml verify

The worker is backed by Eclipse JDT Core 3.47.0. The protocol and process supervisor remain separate concerns; this module does not claim to provide an OS sandbox or enforce host-level CPU/memory limits.

## WPF and CLI deployment package

Build the self-contained worker/runtime package before building or publishing either host:

    pwsh tools/java-worker-prototype/packaging/Package-Worker.ps1

The script verifies the worker, copies runtime dependencies, generates a CycloneDX SBOM and license index, derives required Java modules with jdeps, and creates a private Java 21 runtime with jlink. The WPF and CLI projects copy the resulting artifacts/java-worker-package/Java tree into their output and publish directories. The runtime is selected automatically when present; otherwise the host falls back to a system java executable.

The generated third-party notice file is an inventory of declared license metadata. Upstream license-text/notice reconciliation and legal approval remain separate release requirements.
