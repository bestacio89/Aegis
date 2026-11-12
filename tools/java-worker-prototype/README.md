# Java semantic worker

The worker provides compiler-binding Java analysis through Eclipse JDT Core 3.47.0. It runs out of process using the versioned JSON Lines protocol and receives a module-scoped source/classpath snapshot from the .NET host.

## Analyze request payload

```json
{
  "projectPath": "/workspace/service",
  "sourceRoots": ["/workspace/service/src/main/java"],
  "sourceFiles": ["/workspace/service/src/main/java/example/Service.java"],
  "classpathEntries": ["/workspace/.m2/repository/example/api.jar"],
  "sourceLevel": "21",
  "includeTestCode": false,
  "includeGeneratedCode": false
}
```

All source files must belong to a declared source root, and source roots must remain inside the declared module path. Classpath entries are explicit inputs; the worker never invokes a build tool, plugin, annotation processor or project code.

## Result

The response uses the numeric values of the shared Aegis contracts for symbol kinds, relationship kinds, resolution status, analysis status, symbol origin and evidence source. It returns symbols, relationships and native JDT compiler diagnostics with file/line/column provenance. Unresolved references remain explicit relationships with no fabricated target identity.

## Build and test

    mvn --batch-mode --no-transfer-progress -f tools/java-worker-prototype/pom.xml verify

The worker is backed by Eclipse JDT Core 3.47.0. The protocol and process supervisor remain separate concerns; this module does not claim to provide an OS sandbox or enforce host-level CPU/memory limits.
