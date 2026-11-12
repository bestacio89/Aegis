[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$JavaExecutable,

    [Parameter(Mandatory)]
    [string]$Classpath,

    [Parameter(Mandatory)]
    [string]$ArtifactDirectory
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

New-Item -ItemType Directory -Path $ArtifactDirectory -Force | Out-Null
$script:measurements = [System.Collections.Generic.List[object]]::new()

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
    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    if (-not $process.Start()) {
        throw 'The private-runtime worker process could not be started.'
    }
    $stopwatch.Stop()
    $stderrTask = $process.StandardError.ReadToEndAsync()

    return [pscustomobject]@{
        Process = $process
        StandardErrorTask = $stderrTask
        LaunchMillis = $stopwatch.ElapsedMilliseconds
    }
}

function Stop-WorkerProcess {
    param([Parameter(Mandatory)][System.Diagnostics.Process]$Process)

    if (-not $Process.HasExited) {
        $Process.Kill($true)
        if (-not $Process.WaitForExit(5000)) {
            throw 'The worker process tree did not terminate after forced termination.'
        }
    }
}

function Send-WorkerFrame {
    param(
        [Parameter(Mandatory)][System.Diagnostics.Process]$Process,
        [Parameter(Mandatory)][string]$Frame
    )

    $Process.StandardInput.WriteLine($Frame)
    $Process.StandardInput.Flush()
}

function Send-WorkerRequest {
    param(
        [Parameter(Mandatory)][System.Diagnostics.Process]$Process,
        [Parameter(Mandatory)][hashtable]$Request
    )

    Send-WorkerFrame -Process $Process -Frame ($Request | ConvertTo-Json -Compress -Depth 16)
}

function Read-WorkerResponse {
    param(
        [Parameter(Mandatory)][System.Diagnostics.Process]$Process,
        [int]$TimeoutMilliseconds = 5000
    )

    $readTask = $Process.StandardOutput.ReadLineAsync()
    if (-not $readTask.Wait($TimeoutMilliseconds)) {
        Stop-WorkerProcess -Process $Process
        throw "The worker did not produce a protocol response within $TimeoutMilliseconds ms."
    }

    $line = $readTask.GetAwaiter().GetResult()
    if ([string]::IsNullOrWhiteSpace($line)) {
        throw 'The worker closed stdout without producing a protocol frame.'
    }

    return ConvertFrom-Json -InputObject $line -AsHashtable
}

function Assert-WorkerSuccess {
    param(
        [Parameter(Mandatory)][System.Collections.IDictionary]$Response,
        [Parameter(Mandatory)][string]$RequestId
    )

    if ($Response.status -ne 'ok' -or $Response.requestId -ne $RequestId) {
        throw "Unexpected worker response for request '$RequestId': $($Response | ConvertTo-Json -Compress -Depth 8)"
    }
}

function Assert-WorkerError {
    param(
        [Parameter(Mandatory)][System.Collections.IDictionary]$Response,
        [Parameter(Mandatory)][string]$Code
    )

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
    Send-WorkerRequest -Process $worker -Request @{
        protocolVersion = 1
        requestId = 'runtime-handshake'
        operation = 'handshake'
    }
    $handshake = Read-WorkerResponse -Process $worker
    $handshakeTimer.Stop()
    Assert-WorkerSuccess -Response $handshake -RequestId 'runtime-handshake'
    if ($handshake.result.protocolVersion -ne 1 -or $handshake.result.maxFrameBytes -ne 1048576) {
        throw 'The worker handshake returned an unexpected protocol contract.'
    }

    Send-WorkerRequest -Process $worker -Request @{
        protocolVersion = 1
        requestId = 'runtime-capabilities'
        operation = 'capabilities'
    }
    $capabilities = Read-WorkerResponse -Process $worker
    Assert-WorkerSuccess -Response $capabilities -RequestId 'runtime-capabilities'
    if ($capabilities.result.analysisAvailable -ne $false) {
        throw 'The protocol-only worker must not claim that a semantic engine is attached.'
    }

    Send-WorkerRequest -Process $worker -Request @{
        protocolVersion = 1
        requestId = 'runtime-ping'
        operation = 'ping'
    }
    $ping = Read-WorkerResponse -Process $worker
    Assert-WorkerSuccess -Response $ping -RequestId 'runtime-ping'
    if ($ping.result.alive -ne $true) {
        throw 'The worker liveness probe did not return alive=true.'
    }

    Send-WorkerFrame -Process $worker -Frame '{malformed-json'
    $malformed = Read-WorkerResponse -Process $worker
    Assert-WorkerError -Response $malformed -Code 'INVALID_JSON'

    Send-WorkerFrame -Process $worker -Frame ('x' * 1048577)
    $oversized = Read-WorkerResponse -Process $worker
    Assert-WorkerError -Response $oversized -Code 'FRAME_TOO_LARGE'

    Send-WorkerRequest -Process $worker -Request @{
        protocolVersion = 99
        requestId = 'runtime-version-mismatch'
        operation = 'ping'
    }
    $versionMismatch = Read-WorkerResponse -Process $worker
    Assert-WorkerError -Response $versionMismatch -Code 'UNSUPPORTED_PROTOCOL_VERSION'

    Send-WorkerRequest -Process $worker -Request @{
        protocolVersion = 1
        requestId = 'runtime-shutdown'
        operation = 'shutdown'
    }
    $shutdown = Read-WorkerResponse -Process $worker
    Assert-WorkerSuccess -Response $shutdown -RequestId 'runtime-shutdown'
    if (-not $worker.WaitForExit(5000)) {
        Stop-WorkerProcess -Process $worker
        throw 'The worker did not exit after the graceful shutdown request.'
    }
    if ($worker.ExitCode -ne 0) {
        throw "The worker exited with code $($worker.ExitCode) after graceful shutdown."
    }
    $cleanStderr = $cleanWorker.StandardErrorTask.GetAwaiter().GetResult()
    if (-not [string]::IsNullOrWhiteSpace($cleanStderr)) {
        throw "The worker wrote unexpected stderr output during protocol smoke tests: $cleanStderr"
    }

    $script:measurements.Add([ordered]@{
        scenario = 'protocol-and-graceful-shutdown'
        launchMillis = $cleanWorker.LaunchMillis
        handshakeMillis = $handshakeTimer.ElapsedMilliseconds
        exitCode = $worker.ExitCode
        malformedJsonRejected = $true
        oversizedFrameRejected = $true
        unsupportedProtocolRejected = $true
        analysisEngineAdvertised = $capabilities.result.analysisAvailable
    })

    $crashWorker = Start-WorkerProcess
    Send-WorkerRequest -Process $crashWorker.Process -Request @{
        protocolVersion = 1
        requestId = 'crash-handshake'
        operation = 'handshake'
    }
    $crashHandshake = Read-WorkerResponse -Process $crashWorker.Process
    Assert-WorkerSuccess -Response $crashHandshake -RequestId 'crash-handshake'
    Stop-WorkerProcess -Process $crashWorker.Process
    if (-not $crashWorker.Process.HasExited) {
        throw 'The forced worker termination did not complete.'
    }
    $null = $crashWorker.StandardErrorTask.GetAwaiter().GetResult()

    $restartWorker = Start-WorkerProcess
    Send-WorkerRequest -Process $restartWorker.Process -Request @{
        protocolVersion = 1
        requestId = 'restart-handshake'
        operation = 'handshake'
    }
    $restartHandshake = Read-WorkerResponse -Process $restartWorker.Process
    Assert-WorkerSuccess -Response $restartHandshake -RequestId 'restart-handshake'
    Send-WorkerRequest -Process $restartWorker.Process -Request @{
        protocolVersion = 1
        requestId = 'restart-shutdown'
        operation = 'shutdown'
    }
    $restartShutdown = Read-WorkerResponse -Process $restartWorker.Process
    Assert-WorkerSuccess -Response $restartShutdown -RequestId 'restart-shutdown'
    if (-not $restartWorker.Process.WaitForExit(5000) -or $restartWorker.Process.ExitCode -ne 0) {
        throw 'The replacement worker did not shut down cleanly after restart.'
    }
    $restartStderr = $restartWorker.StandardErrorTask.GetAwaiter().GetResult()
    if (-not [string]::IsNullOrWhiteSpace($restartStderr)) {
        throw "The replacement worker wrote unexpected stderr output: $restartStderr"
    }

    $script:measurements.Add([ordered]@{
        scenario = 'forced-termination-and-restart'
        firstLaunchMillis = $crashWorker.LaunchMillis
        replacementLaunchMillis = $restartWorker.LaunchMillis
        replacementExitCode = $restartWorker.Process.ExitCode
        restartSucceeded = $true
    })
}
finally {
    foreach ($workerState in @($cleanWorker, $crashWorker, $restartWorker)) {
        if ($null -ne $workerState -and $null -ne $workerState.Process) {
            Stop-WorkerProcess -Process $workerState.Process
            $workerState.Process.Dispose()
        }
    }
}

$summary = [ordered]@{
    operatingSystem = [System.Runtime.InteropServices.RuntimeInformation]::OSDescription
    architecture = [System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString()
    protocolVersion = 1
    maxFrameBytes = 1048576
    scenarios = @($script:measurements)
}

$summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $ArtifactDirectory 'worker-process-probe.json') -Encoding utf8
