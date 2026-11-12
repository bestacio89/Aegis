[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$JavaExecutable,
    [Parameter(Mandatory)][string]$Classpath,
    [Parameter(Mandatory)][string]$ArtifactDirectory
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
New-Item -ItemType Directory -Path $ArtifactDirectory -Force | Out-Null
$script:measurements = [System.Collections.Generic.List[object]]::new()

$projectDirectory = Join-Path $ArtifactDirectory 'analysis-workspace/project'
$sourceRoot = Join-Path $projectDirectory 'src/main/java'
$sourceDirectory = Join-Path $sourceRoot 'example'
New-Item -ItemType Directory -Path $sourceDirectory -Force | Out-Null
$sourceFile = Join-Path $sourceDirectory 'WorkerProbe.java'
@'
package example;
public final class WorkerProbe {
    public String execute() { return "jdt-ready"; }
}
'@ | Set-Content -LiteralPath $sourceFile -Encoding utf8

function Start-WorkerProcess {
    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $JavaExecutable
    $startInfo.ArgumentList.Add('-cp')
    $startInfo.ArgumentList.Add($Classpath)
    $startInfo.ArgumentList.Add('org.aegis.worker.WorkerMain')
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardInput = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    $timer = [System.Diagnostics.Stopwatch]::StartNew()
    if (-not $process.Start()) { throw 'The private-runtime worker process could not be started.' }
    $timer.Stop()
    return [pscustomobject]@{
        Process = $process
        StandardErrorTask = $process.StandardError.ReadToEndAsync()
        LaunchMillis = $timer.ElapsedMilliseconds
    }
}

function Stop-WorkerProcess {
    param([Parameter(Mandatory)][System.Diagnostics.Process]$Process)
    if (-not $Process.HasExited) {
        $Process.Kill($true)
        if (-not $Process.WaitForExit(5000)) { throw 'The worker process tree did not terminate.' }
    }
}

function Send-WorkerRequest {
    param([Parameter(Mandatory)][System.Diagnostics.Process]$Process,[Parameter(Mandatory)][hashtable]$Request)
    $Process.StandardInput.WriteLine(($Request | ConvertTo-Json -Compress -Depth 16))
    $Process.StandardInput.Flush()
}

function Read-WorkerResponse {
    param([Parameter(Mandatory)][System.Diagnostics.Process]$Process,[int]$TimeoutMilliseconds = 5000)
    $task = $Process.StandardOutput.ReadLineAsync()
    if (-not $task.Wait($TimeoutMilliseconds)) {
        Stop-WorkerProcess -Process $Process
        throw "The worker did not respond within $TimeoutMilliseconds ms."
    }
    $line = $task.GetAwaiter().GetResult()
    if ([string]::IsNullOrWhiteSpace($line)) { throw 'The worker closed stdout without a protocol frame.' }
    return ConvertFrom-Json -InputObject $line -AsHashtable
}

function Assert-WorkerSuccess {
    param([Parameter(Mandatory)][System.Collections.IDictionary]$Response,[Parameter(Mandatory)][string]$RequestId)
    if ($Response.status -ne 'ok' -or $Response.requestId -ne $RequestId) {
        throw "Unexpected response for '$RequestId': $($Response | ConvertTo-Json -Compress -Depth 8)"
    }
}

function Assert-WorkerError {
    param([Parameter(Mandatory)][System.Collections.IDictionary]$Response,[Parameter(Mandatory)][string]$Code)
    if ($Response.status -ne 'error' -or $Response.error.code -ne $Code) {
        throw "Expected worker error '$Code': $($Response | ConvertTo-Json -Compress -Depth 8)"
    }
}

$cleanWorker = $null
$crashWorker = $null
$restartWorker = $null
try {
    $cleanWorker = Start-WorkerProcess
    $worker = $cleanWorker.Process
    $handshakeTimer = [System.Diagnostics.Stopwatch]::StartNew()
    Send-WorkerRequest $worker @{ protocolVersion = 1; requestId = 'runtime-handshake'; operation = 'handshake' }
    $handshake = Read-WorkerResponse $worker
    $handshakeTimer.Stop()
    Assert-WorkerSuccess $handshake 'runtime-handshake'
    if ($handshake.result.protocolVersion -ne 1 -or $handshake.result.maxFrameBytes -ne 1048576 -or $handshake.result.maxResponseBytes -ne 67108864) {
        throw 'Unexpected worker handshake contract.'
    }

    Send-WorkerRequest $worker @{ protocolVersion = 1; requestId = 'runtime-capabilities'; operation = 'capabilities' }
    $capabilities = Read-WorkerResponse $worker
    Assert-WorkerSuccess $capabilities 'runtime-capabilities'
    if ($capabilities.result.analysisAvailable -ne $true) {
        throw 'The worker must advertise the attached Eclipse JDT semantic engine.'
    }

    $analysisTimer = [System.Diagnostics.Stopwatch]::StartNew()
    Send-WorkerRequest $worker @{
        protocolVersion = 1
        requestId = 'runtime-analysis'
        operation = 'analyze'
        payload = @{
            projectPath = $projectDirectory
            sourceRoots = @($sourceRoot)
            sourceFiles = @($sourceFile)
            classpathEntries = @()
            sourceLevel = '21'
            includeTestCode = $false
            includeGeneratedCode = $false
        }
    }
    $analysis = Read-WorkerResponse $worker 20000
    $analysisTimer.Stop()
    Assert-WorkerSuccess $analysis 'runtime-analysis'
    if ($analysis.result.engineId -ne 'eclipse-jdt' -or $analysis.result.status -ne 2) {
        throw "The worker did not return completed JDT analysis: $($analysis.result | ConvertTo-Json -Compress -Depth 8)"
    }
    $probeSymbol = @($analysis.result.symbols | Where-Object { $_.qualifiedName -eq 'example.WorkerProbe' })
    if ($probeSymbol.Count -ne 1) { throw 'The JDT worker did not return the expected type symbol.' }

    Send-WorkerRequest $worker @{ protocolVersion = 1; requestId = 'runtime-ping'; operation = 'ping' }
    $ping = Read-WorkerResponse $worker
    Assert-WorkerSuccess $ping 'runtime-ping'
    if ($ping.result.alive -ne $true) { throw 'Worker liveness probe failed.' }

    $worker.StandardInput.WriteLine('{malformed-json')
    $malformed = Read-WorkerResponse $worker
    Assert-WorkerError $malformed 'INVALID_JSON'

    $worker.StandardInput.WriteLine(('x' * 1048577))
    $oversized = Read-WorkerResponse $worker
    Assert-WorkerError $oversized 'FRAME_TOO_LARGE'

    Send-WorkerRequest $worker @{ protocolVersion = 99; requestId = 'runtime-version-mismatch'; operation = 'ping' }
    $versionMismatch = Read-WorkerResponse $worker
    Assert-WorkerError $versionMismatch 'UNSUPPORTED_PROTOCOL_VERSION'

    Send-WorkerRequest $worker @{ protocolVersion = 1; requestId = 'runtime-shutdown'; operation = 'shutdown' }
    $shutdown = Read-WorkerResponse $worker
    Assert-WorkerSuccess $shutdown 'runtime-shutdown'
    if (-not $worker.WaitForExit(5000) -or $worker.ExitCode -ne 0) {
        Stop-WorkerProcess $worker
        throw 'Worker did not exit cleanly after shutdown.'
    }
    $cleanStderr = $cleanWorker.StandardErrorTask.GetAwaiter().GetResult()
    if (-not [string]::IsNullOrWhiteSpace($cleanStderr)) { throw "Unexpected worker stderr: $cleanStderr" }

    $script:measurements.Add([ordered]@{
        scenario = 'jdt-analysis-protocol-and-graceful-shutdown'
        launchMillis = $cleanWorker.LaunchMillis
        handshakeMillis = $handshakeTimer.ElapsedMilliseconds
        analysisMillis = $analysisTimer.ElapsedMilliseconds
        analyzedFiles = $analysis.result.analyzedFileCount
        symbolCount = @($analysis.result.symbols).Count
        relationshipCount = @($analysis.result.relationships).Count
        exitCode = $worker.ExitCode
        malformedJsonRejected = $true
        oversizedFrameRejected = $true
        unsupportedProtocolRejected = $true
        analysisEngineAdvertised = $capabilities.result.analysisAvailable
    })

    $crashWorker = Start-WorkerProcess
    Send-WorkerRequest $crashWorker.Process @{ protocolVersion = 1; requestId = 'crash-handshake'; operation = 'handshake' }
    $crashHandshake = Read-WorkerResponse $crashWorker.Process
    Assert-WorkerSuccess $crashHandshake 'crash-handshake'
    Stop-WorkerProcess $crashWorker.Process
    $null = $crashWorker.StandardErrorTask.GetAwaiter().GetResult()

    $restartWorker = Start-WorkerProcess
    Send-WorkerRequest $restartWorker.Process @{ protocolVersion = 1; requestId = 'restart-handshake'; operation = 'handshake' }
    $restartHandshake = Read-WorkerResponse $restartWorker.Process
    Assert-WorkerSuccess $restartHandshake 'restart-handshake'
    Send-WorkerRequest $restartWorker.Process @{ protocolVersion = 1; requestId = 'restart-shutdown'; operation = 'shutdown' }
    $restartShutdown = Read-WorkerResponse $restartWorker.Process
    Assert-WorkerSuccess $restartShutdown 'restart-shutdown'
    if (-not $restartWorker.Process.WaitForExit(5000) -or $restartWorker.Process.ExitCode -ne 0) {
        throw 'Replacement worker did not shut down cleanly.'
    }
    $restartStderr = $restartWorker.StandardErrorTask.GetAwaiter().GetResult()
    if (-not [string]::IsNullOrWhiteSpace($restartStderr)) { throw "Unexpected replacement worker stderr: $restartStderr" }

    $script:measurements.Add([ordered]@{
        scenario = 'forced-termination-and-restart'
        firstLaunchMillis = $crashWorker.LaunchMillis
        replacementLaunchMillis = $restartWorker.LaunchMillis
        replacementExitCode = $restartWorker.Process.ExitCode
        restartSucceeded = $true
    })
}
finally {
    foreach ($state in @($cleanWorker, $crashWorker, $restartWorker)) {
        if ($null -ne $state -and $null -ne $state.Process) {
            Stop-WorkerProcess $state.Process
            $state.Process.Dispose()
        }
    }
}

$summary = [ordered]@{
    operatingSystem = [System.Runtime.InteropServices.RuntimeInformation]::OSDescription
    architecture = [System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString()
    protocolVersion = 1
    maxFrameBytes = 1048576
    maxResponseBytes = 67108864
    engine = 'eclipse-jdt'
    engineVersion = '3.47.0'
    scenarios = @($script:measurements)
}
$summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $ArtifactDirectory 'worker-process-probe.json') -Encoding utf8
