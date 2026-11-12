[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
Set-Location $repositoryRoot

$artifactRoot = Join-Path $repositoryRoot 'artifacts/private-runtime-probe'
$runtimeDirectory = Join-Path $artifactRoot 'runtime'
$appDirectory = Join-Path $artifactRoot 'app'
$libraryDirectory = Join-Path $artifactRoot 'lib'
$classpathFile = Join-Path $artifactRoot 'runtime-classpath.txt'
$moduleFile = Join-Path $artifactRoot 'jdk-module-roots.txt'
$workerAppDirectory = Join-Path $artifactRoot 'worker-app'
$workerLibraryDirectory = Join-Path $artifactRoot 'worker-lib'
$workerArtifactDirectory = Join-Path $artifactRoot 'worker-process'

New-Item -ItemType Directory -Path $artifactRoot -Force | Out-Null
foreach ($directory in @($appDirectory, $libraryDirectory, $workerAppDirectory, $workerLibraryDirectory, $workerArtifactDirectory)) {
    if (Test-Path -LiteralPath $directory) {
        Remove-Item -LiteralPath $directory -Recurse -Force
    }
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
}

$mavenCompileArguments = @('--batch-mode', '--no-transfer-progress', '-f', 'tools/java-semantic-spike/pom.xml', 'compile')
& mvn @mavenCompileArguments
if ($LASTEXITCODE -ne 0) {
    throw "Maven compilation failed with exit code $LASTEXITCODE."
}

$workerMavenArguments = @(
    '--batch-mode',
    '--no-transfer-progress',
    '-f',
    'tools/java-worker-prototype/pom.xml',
    'package',
    '-DskipTests'
)
& mvn @workerMavenArguments
if ($LASTEXITCODE -ne 0) {
    throw "Java worker packaging failed with exit code $LASTEXITCODE."
}

$workerClasspathFile = Join-Path $artifactRoot 'worker-runtime-classpath.txt'
$workerClasspathArguments = @(
    '--batch-mode',
    '--no-transfer-progress',
    '-f',
    'tools/java-worker-prototype/pom.xml',
    'org.apache.maven.plugins:maven-dependency-plugin:3.8.1:build-classpath',
    '-DincludeScope=runtime',
    "-Dmdep.outputFile=$workerClasspathFile"
)
& mvn @workerClasspathArguments
if ($LASTEXITCODE -ne 0) {
    throw "Java worker runtime classpath resolution failed with exit code $LASTEXITCODE."
}

$workerClasspath = (Get-Content -LiteralPath $workerClasspathFile -Raw).Trim()
if ([string]::IsNullOrWhiteSpace($workerClasspath)) {
    throw 'Maven produced an empty Java worker runtime classpath.'
}

$workerClassesDirectory = Join-Path $repositoryRoot 'tools/java-worker-prototype/target/classes'
$workerDependencyJars = @(
    $workerClasspath.Split([IO.Path]::PathSeparator) |
        Where-Object { $_ -and (Test-Path -LiteralPath $_ -PathType Leaf) }
)
$workerDependencyJarNames = @($workerDependencyJars | ForEach-Object { Split-Path -Leaf $_ })
if ($workerDependencyJarNames -notcontains 'gson-2.13.2.jar') {
    throw "The isolated worker runtime closure does not contain Gson 2.13.2: $($workerDependencyJarNames -join ', ')."
}
if (-not ($workerDependencyJarNames | Where-Object { $_ -match 'org\.eclipse\.jdt\.core-3\.47\.0\.jar' })) {
    throw "The isolated worker runtime closure does not contain Eclipse JDT Core 3.47.0: $($workerDependencyJarNames -join ', ')."
}
$unexpectedParserJars = @($workerDependencyJarNames | Where-Object { $_ -match 'javaparser' })
if ($unexpectedParserJars.Count -gt 0) {
    throw "The production worker runtime must not contain the rejected JavaParser candidate: $($unexpectedParserJars -join ', ')."
}

$mavenClasspathArguments = @(
    '--batch-mode',
    '--no-transfer-progress',
    '-f',
    'tools/java-semantic-spike/pom.xml',
    'org.apache.maven.plugins:maven-dependency-plugin:3.8.1:build-classpath',
    '-DincludeScope=runtime',
    "-Dmdep.outputFile=$classpathFile"
)
& mvn @mavenClasspathArguments
if ($LASTEXITCODE -ne 0) {
    throw "Maven runtime classpath resolution failed with exit code $LASTEXITCODE."
}

$classpath = (Get-Content -LiteralPath $classpathFile -Raw).Trim()
if ([string]::IsNullOrWhiteSpace($classpath)) {
    throw 'Maven produced an empty runtime classpath.'
}

$classesDirectory = Join-Path $repositoryRoot 'tools/java-semantic-spike/target/classes'
$executableSuffix = if ($IsWindows) { '.exe' } else { '' }
$jdeps = Join-Path $env:JAVA_HOME "bin/jdeps$executableSuffix"
$jlink = Join-Path $env:JAVA_HOME "bin/jlink$executableSuffix"

$jdepsArguments = @(
    '--multi-release', '21',
    '--ignore-missing-deps',
    '--print-module-deps',
    '--recursive',
    '--class-path', $classpath,
    $classesDirectory
)
$moduleOutput = & $jdeps @jdepsArguments
if ($LASTEXITCODE -ne 0) {
    throw "jdeps failed with exit code $LASTEXITCODE."
}

$moduleList = ($moduleOutput -join '').Trim()
if ([string]::IsNullOrWhiteSpace($moduleList)) {
    throw 'jdeps returned an empty JDK module closure.'
}

$moduleNames = @($moduleList.Split(',') | ForEach-Object { $_.Trim() } | Where-Object { $_ })

$workerJdepsArguments = @(
    '--multi-release', '21',
    '--ignore-missing-deps',
    '--print-module-deps',
    '--recursive',
    '--class-path', $workerClasspath,
    $workerClassesDirectory
)
$workerModuleOutput = & $jdeps @workerJdepsArguments
if ($LASTEXITCODE -ne 0) {
    throw "Worker jdeps analysis failed with exit code $LASTEXITCODE."
}
$workerModuleList = ($workerModuleOutput -join '').Trim()
if ([string]::IsNullOrWhiteSpace($workerModuleList)) {
    throw 'jdeps returned an empty Java worker module closure.'
}
$workerModuleNames = @($workerModuleList.Split(',') | ForEach-Object { $_.Trim() } | Where-Object { $_ })
$moduleNames = @($moduleNames + $workerModuleNames | Select-Object -Unique)
$moduleList = [string]::Join(',', $moduleNames)

if ($moduleNames -notcontains 'java.base') {
    throw "The derived JDK module closure is invalid: $moduleList"
}
$moduleList | Set-Content -LiteralPath $moduleFile -Encoding utf8

if (Test-Path -LiteralPath $runtimeDirectory) {
    Remove-Item -LiteralPath $runtimeDirectory -Recurse -Force
}

$jlinkArguments = @(
    '--add-modules', $moduleList,
    '--strip-debug',
    '--no-header-files',
    '--no-man-pages',
    '--compress=2',
    '--output', $runtimeDirectory
)
& $jlink @jlinkArguments
if ($LASTEXITCODE -ne 0) {
    throw "jlink failed with exit code $LASTEXITCODE."
}

Copy-Item -Path (Join-Path $classesDirectory '*') -Destination $appDirectory -Recurse -Force

$dependencyJars = @(
    $classpath.Split([IO.Path]::PathSeparator) |
        Where-Object { $_ -and (Test-Path -LiteralPath $_ -PathType Leaf) }
)
if ($dependencyJars.Count -ne 29) {
    throw "Expected the pinned 29-JAR combined candidate closure, found $($dependencyJars.Count)."
}

foreach ($jar in $dependencyJars) {
    Copy-Item -LiteralPath $jar -Destination $libraryDirectory -Force
}

Copy-Item -Path (Join-Path $workerClassesDirectory '*') -Destination $workerAppDirectory -Recurse -Force
foreach ($jar in $workerDependencyJars) {
    Copy-Item -LiteralPath $jar -Destination $workerLibraryDirectory -Force
}

$runtimeJava = Join-Path $runtimeDirectory "bin/java$executableSuffix"
$libraryWildcard = Join-Path $libraryDirectory '*'
$packagedClasspath = [string]::Join([IO.Path]::PathSeparator, @($appDirectory, $libraryWildcard))
$workerLibraryWildcard = Join-Path $workerLibraryDirectory '*'
$workerPackagedClasspath = [string]::Join([IO.Path]::PathSeparator, @($workerAppDirectory, $workerLibraryWildcard))
$script:probeMeasurements = [System.Collections.Generic.List[object]]::new()

function Invoke-PackagedJavaProbe {
    param(
        [Parameter(Mandatory)]
        [string]$Name,

        [Parameter(Mandatory)]
        [string]$MainClass,

        [Parameter(Mandatory)]
        [string[]]$Arguments
    )

    $outputPath = Join-Path $artifactRoot "$Name.jsonl"
    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    $output = & $runtimeJava -cp $packagedClasspath $MainClass @Arguments 2>&1
    $exitCode = $LASTEXITCODE
    $stopwatch.Stop()
    $output | Set-Content -LiteralPath $outputPath -Encoding utf8

    $script:probeMeasurements.Add([ordered]@{
        name = $Name
        durationMillis = $stopwatch.ElapsedMilliseconds
        exitCode = $exitCode
    })

    if ($exitCode -ne 0) {
        throw "Packaged runtime probe '$Name' failed with exit code $exitCode."
    }
}

$fixtureRoot = 'tests/Aegis.Architecture.Tests/Fixtures/Java'
$factCorpus = 'tools/java-semantic-spike/semantic-facts.tsv'
$diagnosticCorpus = 'tools/java-semantic-spike/semantic-diagnostics.tsv'

Invoke-PackagedJavaProbe -Name 'jdt-semantic-facts' -MainClass 'org.aegis.spike.SemanticFactBenchmark' -Arguments @('jdt', $fixtureRoot, $factCorpus)
Invoke-PackagedJavaProbe -Name 'javaparser-semantic-facts' -MainClass 'org.aegis.spike.SemanticFactBenchmark' -Arguments @('javaparser', $fixtureRoot, $factCorpus, '--report-only')
Invoke-PackagedJavaProbe -Name 'jdt-semantic-diagnostics' -MainClass 'org.aegis.spike.SemanticDiagnosticBenchmark' -Arguments @('jdt', $fixtureRoot, $diagnosticCorpus)
Invoke-PackagedJavaProbe -Name 'javaparser-semantic-diagnostics' -MainClass 'org.aegis.spike.SemanticDiagnosticBenchmark' -Arguments @('javaparser', $fixtureRoot, $diagnosticCorpus, '--report-only')

$workerProbeArguments = @{
    JavaExecutable = $runtimeJava
    Classpath = $workerPackagedClasspath
    ArtifactDirectory = $workerArtifactDirectory
}
& (Join-Path $repositoryRoot 'tools/java-worker-prototype/packaging/Probe-WorkerProcess.ps1') @workerProbeArguments
$workerProcessSummary = Get-Content -LiteralPath (Join-Path $workerArtifactDirectory 'worker-process-probe.json') -Raw | ConvertFrom-Json

$runtimeBytes = [long]((Get-ChildItem -LiteralPath $runtimeDirectory -File -Recurse | Measure-Object -Property Length -Sum).Sum)
$dependencyBytes = [long](($dependencyJars | ForEach-Object { (Get-Item -LiteralPath $_).Length } | Measure-Object -Sum).Sum)
$applicationBytes = [long]((Get-ChildItem -LiteralPath $appDirectory -File -Recurse | Measure-Object -Property Length -Sum).Sum)
$workerDependencyBytes = [long](($workerDependencyJars | ForEach-Object { (Get-Item -LiteralPath $_).Length } | Measure-Object -Sum).Sum)
$workerApplicationBytes = [long]((Get-ChildItem -LiteralPath $workerAppDirectory -File -Recurse | Measure-Object -Property Length -Sum).Sum)
$workerPackageBytes = $workerDependencyBytes + $workerApplicationBytes
$combinedBytes = $runtimeBytes + $dependencyBytes + $applicationBytes + $workerPackageBytes

$startupStopwatch = [System.Diagnostics.Stopwatch]::StartNew()
$runtimeVersionOutput = & $runtimeJava -version 2>&1
$runtimeVersionExitCode = $LASTEXITCODE
$startupStopwatch.Stop()
if ($runtimeVersionExitCode -ne 0) {
    throw "The generated private runtime failed its startup probe with exit code $runtimeVersionExitCode."
}
$runtimeVersion = ($runtimeVersionOutput | Out-String).Trim()

$runtimeModules = @(& $runtimeJava --list-modules)
if ($LASTEXITCODE -ne 0) {
    throw 'The generated private runtime could not enumerate its modules.'
}
$runtimeModuleNames = @($runtimeModules | ForEach-Object { ($_ -split '@')[0] })
$runtimeModules | Set-Content -LiteralPath (Join-Path $artifactRoot 'runtime-modules.txt') -Encoding utf8

$summary = [ordered]@{
    operatingSystem = [System.Runtime.InteropServices.RuntimeInformation]::OSDescription
    architecture = [System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString()
    javaVersion = $runtimeVersion
    jdepsModuleRootCount = $moduleNames.Count
    jdepsModuleRoots = $moduleNames
    runtimeModuleCount = $runtimeModuleNames.Count
    runtimeModules = $runtimeModuleNames
    runtimeLaunchMillis = $startupStopwatch.ElapsedMilliseconds
    candidateProbeDurations = @($script:probeMeasurements)
    dependencyJarCount = $dependencyJars.Count
    workerDependencyJarCount = $workerDependencyJars.Count
    workerDependencyJarNames = $workerDependencyJarNames
    workerProcessProbe = $workerProcessSummary
    runtimeBytes = $runtimeBytes
    dependencyJarBytes = $dependencyBytes
    applicationClassBytes = $applicationBytes
    workerDependencyBytes = $workerDependencyBytes
    workerApplicationClassBytes = $workerApplicationBytes
    workerPackageBytes = $workerPackageBytes
    estimatedWorkerPackageBytes = $combinedBytes
    runtimeMiB = [Math]::Round($runtimeBytes / 1MB, 2)
    dependencyJarMiB = [Math]::Round($dependencyBytes / 1MB, 2)
    applicationClassMiB = [Math]::Round($applicationBytes / 1MB, 2)
    estimatedWorkerPackageMiB = [Math]::Round($combinedBytes / 1MB, 2)
}

$summary | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $artifactRoot 'runtime-package-summary.json') -Encoding utf8
