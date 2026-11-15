[CmdletBinding()]
param(
    [string]$JavaExecutable = 'java',
    [string]$WorkerJar = ([System.IO.Path]::Combine($PSScriptRoot, '..', 'target', 'runtime', 'kotlin-worker.jar')),
    [string]$LibrariesDirectory = ([System.IO.Path]::Combine($PSScriptRoot, '..', 'target', 'runtime', 'lib'))
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$workerJarPath = [System.IO.Path]::GetFullPath($WorkerJar)
$librariesPath = [System.IO.Path]::GetFullPath($LibrariesDirectory)
$standardLibraryPath = Join-Path $librariesPath 'kotlin-stdlib-2.4.21.jar'

if (-not (Test-Path -LiteralPath $workerJarPath -PathType Leaf)) {
    throw "The packaged Kotlin worker JAR does not exist: $workerJarPath"
}
if (-not (Test-Path -LiteralPath $standardLibraryPath -PathType Leaf)) {
    throw "The packaged Kotlin standard library does not exist: $standardLibraryPath"
}

$workspace = Join-Path ([System.IO.Path]::GetTempPath()) "aegis-kotlin-packaged-probe-$([guid]::NewGuid().ToString('N'))"
$projectPath = Join-Path $workspace 'project'
$sourceRoot = Join-Path $projectPath 'src/main/kotlin'
$sourceFile = Join-Path $sourceRoot 'sample/PackagedProbe.kt'
$artifactRoot = Join-Path ([System.IO.Path]::GetTempPath()) "aegis-kotlin-artifacts-$([guid]::NewGuid().ToString('N'))"
$manifestPath = Join-Path $artifactRoot 'source-manifest.json'

$process = $null
$stderrTask = $null

function Send-WorkerRequest {
    param(
        [Parameter(Mandatory)][System.Diagnostics.Process]$Worker,
        [Parameter(Mandatory)][System.Collections.IDictionary]$Request
    )

    $frame = ConvertTo-Json -InputObject $Request -Compress -Depth 16
    $Worker.StandardInput.WriteLine($frame)
    $Worker.StandardInput.Flush()
}

function Read-WorkerResponse {
    param(
        [Parameter(Mandatory)][System.Diagnostics.Process]$Worker,
        [int]$TimeoutMilliseconds = 30000
    )

    $responseTask = $Worker.StandardOutput.ReadLineAsync()
    if (-not $responseTask.Wait($TimeoutMilliseconds)) {
        throw "The packaged Kotlin worker did not respond within $TimeoutMilliseconds ms."
    }

    $line = $responseTask.GetAwaiter().GetResult()
    if ([string]::IsNullOrWhiteSpace($line)) {
        throw 'The packaged Kotlin worker closed stdout without a protocol response.'
    }

    return ConvertFrom-Json -InputObject $line -AsHashtable
}

function Assert-WorkerSuccess {
    param(
        [Parameter(Mandatory)][System.Collections.IDictionary]$Response,
        [Parameter(Mandatory)][string]$RequestId
    )

    if ($Response.status -ne 'ok' -or $Response.requestId -ne $RequestId) {
        throw "Unexpected packaged-worker response: $(ConvertTo-Json -InputObject $Response -Compress -Depth 12)"
    }
}

try {
    New-Item -ItemType Directory -Path (Split-Path -Parent $sourceFile) -Force | Out-Null
    @'
package sample

class PackagedProbe {
    fun answer(): String = "packaged-runtime-ready"
}
'@ | Set-Content -LiteralPath $sourceFile -Encoding utf8

    New-Item -ItemType Directory -Path $artifactRoot -Force | Out-Null
    ConvertTo-Json -InputObject @($sourceFile) -Compress |
        Set-Content -LiteralPath $manifestPath -Encoding utf8 -NoNewline

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $JavaExecutable
    $startInfo.ArgumentList.Add('-Xms64m')
    $startInfo.ArgumentList.Add('-Xmx1024m')
    $startInfo.ArgumentList.Add('-jar')
    $startInfo.ArgumentList.Add($workerJarPath)
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardInput = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    if (-not $process.Start()) {
        throw 'The packaged Kotlin worker process could not be started.'
    }
    $stderrTask = $process.StandardError.ReadToEndAsync()

    Send-WorkerRequest $process @{
        protocolVersion = 1
        requestId = 'packaged-handshake'
        operation = 'handshake'
    }
    $handshake = Read-WorkerResponse $process
    Assert-WorkerSuccess $handshake 'packaged-handshake'
    if ($handshake.result.protocolVersion -ne 1) {
        throw 'The packaged Kotlin worker returned an unsupported protocol version.'
    }

    Send-WorkerRequest $process @{
        protocolVersion = 1
        requestId = 'packaged-analysis'
        operation = 'analyze'
        payload = @{
            projectPath = $projectPath
            workspaceRoot = $workspace
            sourceManifestPath = $manifestPath
            artifactRoot = $artifactRoot
            sourceRoots = @($sourceRoot)
            referencedSourceRoots = @()
            testSourceRoots = @()
            generatedSourceRoots = @()
            classpathEntries = @()
            kotlinStandardLibraryPath = $standardLibraryPath
            compilerVersion = '2.4.21'
            languageVersion = '2.4'
            apiVersion = '2.4'
            jvmTarget = '21'
            moduleName = 'packaged-worker-probe'
            includeTestCode = $false
        }
    }

    $analysis = Read-WorkerResponse $process 60000
    Assert-WorkerSuccess $analysis 'packaged-analysis'
    if ($analysis.result.engineId -ne 'kotlin-k2-ir' -or $analysis.result.status -ne 2) {
        throw "The packaged worker did not complete Kotlin semantic analysis: $(ConvertTo-Json -InputObject $analysis.result -Compress -Depth 12)"
    }

    $probeSymbols = @($analysis.result.symbols | Where-Object { $_.qualifiedName -eq 'sample.PackagedProbe' })
    if ($probeSymbols.Count -ne 1) {
        throw 'The packaged worker did not return the expected Kotlin source symbol.'
    }


    Send-WorkerRequest $process @{
        protocolVersion = 1
        requestId = 'packaged-shutdown'
        operation = 'shutdown'
    }
    $shutdown = Read-WorkerResponse $process
    Assert-WorkerSuccess $shutdown 'packaged-shutdown'
    if (-not $process.WaitForExit(10000) -or $process.ExitCode -ne 0) {
        throw 'The packaged Kotlin worker did not shut down cleanly.'
    }

    Write-Output 'Packaged Kotlin worker handshake, semantic analysis, and graceful shutdown passed.'
}
catch {
    if ($null -ne $process -and -not $process.HasExited) {
        $process.Kill($true)
        $process.WaitForExit(5000) | Out-Null
    }
    if ($null -ne $stderrTask -and $stderrTask.IsCompleted) {
        $stderr = $stderrTask.GetAwaiter().GetResult()
        if (-not [string]::IsNullOrWhiteSpace($stderr)) {
            Write-Error "Packaged Kotlin worker stderr: $stderr" -ErrorAction Continue
        }
    }
    throw
}
finally {
    if ($null -ne $process) {
        $process.Dispose()
    }
    if (Test-Path -LiteralPath $artifactRoot) {
        Remove-Item -LiteralPath $artifactRoot -Recurse -Force
    }
    if (Test-Path -LiteralPath $workspace) {
        Remove-Item -LiteralPath $workspace -Recurse -Force
    }
}
