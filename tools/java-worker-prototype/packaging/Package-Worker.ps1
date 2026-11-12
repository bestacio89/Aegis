[CmdletBinding()]
param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot "..\..\..\artifacts\java-worker-package")
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..\..")).Path
$pomPath = Join-Path $repositoryRoot "tools/java-worker-prototype/pom.xml"
$outputRoot = [System.IO.Path]::GetFullPath($OutputDirectory)
$javaRoot = Join-Path $outputRoot "Java"
$classesDirectory = Join-Path $javaRoot "worker/classes"
$librariesDirectory = Join-Path $javaRoot "worker/lib"
$runtimeDirectory = Join-Path $javaRoot "runtime"
$targetDirectory = Join-Path $repositoryRoot "tools/java-worker-prototype/target"
$classpathFile = Join-Path $outputRoot "worker-classpath.txt"

if (-not $env:JAVA_HOME -or -not (Test-Path $env:JAVA_HOME)) {
    throw "JAVA_HOME must point to a JDK 21 installation."
}
$javaExecutable = Join-Path $env:JAVA_HOME "bin/java"
$jdepsExecutable = Join-Path $env:JAVA_HOME "bin/jdeps"
$jlinkExecutable = Join-Path $env:JAVA_HOME "bin/jlink"
if ($IsWindows) {
    $javaExecutable += ".exe"
    $jdepsExecutable += ".exe"
    $jlinkExecutable += ".exe"
}
if (-not (Test-Path $javaExecutable) -or -not (Test-Path $jdepsExecutable) -or -not (Test-Path $jlinkExecutable)) {
    throw "The configured JAVA_HOME does not contain java, jdeps and jlink."
}

if (Test-Path $outputRoot) {
    Remove-Item -LiteralPath $outputRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $classesDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $librariesDirectory -Force | Out-Null

Push-Location $repositoryRoot
try {
    & mvn --batch-mode --no-transfer-progress -f $pomPath package -DskipTests
    if ($LASTEXITCODE -ne 0) { throw "The Java worker Maven verification failed with exit code $LASTEXITCODE." }

    & mvn --batch-mode --no-transfer-progress -f $pomPath org.apache.maven.plugins:maven-dependency-plugin:3.8.1:copy-dependencies "-DincludeScope=runtime" "-DoutputDirectory=$librariesDirectory"
    if ($LASTEXITCODE -ne 0) { throw "Copying Java worker runtime dependencies failed with exit code $LASTEXITCODE." }

    & mvn --batch-mode --no-transfer-progress -f $pomPath org.apache.maven.plugins:maven-dependency-plugin:3.8.1:build-classpath "-Dmdep.includeScope=runtime" "-Dmdep.outputFile=$classpathFile"
    if ($LASTEXITCODE -ne 0) { throw "Building the Java worker dependency classpath failed with exit code $LASTEXITCODE." }

    & mvn --batch-mode --no-transfer-progress -f $pomPath org.cyclonedx:cyclonedx-maven-plugin:2.9.3:makeBom -DoutputFormat=json -Dcyclonedx.skipAttach=true
    if ($LASTEXITCODE -ne 0) { throw "Generating the Java worker SBOM failed with exit code $LASTEXITCODE." }

    Copy-Item -Path (Join-Path $targetDirectory "classes/*") -Destination $classesDirectory -Recurse -Force
    Copy-Item -LiteralPath (Join-Path $targetDirectory "bom.json") -Destination (Join-Path $javaRoot "worker/sbom.cdx.json") -Force

    $requiredJdt = Join-Path $librariesDirectory "org.eclipse.jdt.core-3.47.0.jar"
    $requiredGson = Join-Path $librariesDirectory "gson-2.13.2.jar"
    if (-not (Test-Path $requiredJdt) -or -not (Test-Path $requiredGson)) {
        throw "The packaged runtime libraries do not contain the pinned JDT 3.47.0 and Gson 2.13.2 dependencies."
    }

    $sbom = Get-Content -LiteralPath (Join-Path $javaRoot "worker/sbom.cdx.json") -Raw | ConvertFrom-Json
    if (-not $sbom.components -or @($sbom.components).Count -lt 20) {
        throw "The worker SBOM does not contain the expected dependency closure."
    }
    $componentsWithoutLicenses = @($sbom.components | Where-Object { -not $_.licenses -or @($_.licenses).Count -eq 0 })
    if ($componentsWithoutLicenses.Count -gt 0) {
        throw "The worker SBOM contains components without license metadata: $($componentsWithoutLicenses.name -join ', ')."
    }

    $classpath = (Get-Content -LiteralPath $classpathFile -Raw).Trim()
    $moduleOutput = & $jdepsExecutable --multi-release 21 --ignore-missing-deps --print-module-deps --recursive --class-path $classpath $classesDirectory
    if ($LASTEXITCODE -ne 0) { throw "jdeps failed with exit code $LASTEXITCODE." }
    $modules = ($moduleOutput | Select-Object -Last 1).Trim()
    if ([string]::IsNullOrWhiteSpace($modules) -or ($modules -split ',') -notcontains "java.base") {
        throw "jdeps returned an invalid Java runtime module set: $modules"
    }

    & $jlinkExecutable --add-modules $modules --bind-services --strip-debug --no-header-files --no-man-pages --compress=2 --output $runtimeDirectory
    if ($LASTEXITCODE -ne 0) { throw "jlink failed with exit code $LASTEXITCODE." }

    $noticeLines = [System.Collections.Generic.List[string]]::new()
    $noticeLines.Add("# Third-party component and license index")
    $noticeLines.Add("")
    $noticeLines.Add("Generated from the packaged CycloneDX SBOM. License identifiers are inventory metadata, not legal approval or a substitute for the applicable license texts.")
    $noticeLines.Add("")
    foreach ($component in ($sbom.components | Sort-Object name, version)) {
        $licenses = @($component.licenses | ForEach-Object {
            if ($_.license.id) { $_.license.id }
            elseif ($_.license.name) { $_.license.name }
            elseif ($_.expression) { $_.expression }
        } | Where-Object { $_ } | Sort-Object -Unique)
        $noticeLines.Add("- $($component.name)@$($component.version) — $($licenses -join ', ')")
    }
    $noticeLines | Set-Content -LiteralPath (Join-Path $javaRoot "worker/THIRD-PARTY-NOTICES.md") -Encoding utf8

    $manifest = [ordered]@{
        engine = "eclipse-jdt"
        engineVersion = "3.47.0"
        protocolVersion = 1
        javaRuntimeFeature = 21
        runtimeModules = $modules.Split(',')
        dependencyCount = @($sbom.components).Count
        sbom = "worker/sbom.cdx.json"
        notices = "worker/THIRD-PARTY-NOTICES.md"
    }
    $manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $javaRoot "worker/worker-manifest.json") -Encoding utf8

    Write-Host "Packaged Eclipse JDT 3.47.0 worker and private Java 21 runtime at $javaRoot"
}
finally {
    Pop-Location
}
