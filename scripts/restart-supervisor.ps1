param(
    [Parameter(Mandatory = $true)]
    [string]$OperationPath
)

$ErrorActionPreference = "Stop"

Set-StrictMode -Version Latest

$script:BootstrapLogPath = "$OperationPath.supervisor.log"

$terminalStatuses = @(
    "completed",
    "rolled_back",
    "failed"
)

$supervisorPid = $PID

$validTransitions = @{
    prepared = @("helper_started", "failed")
    helper_started = @("stopping_source", "failed")
    stopping_source = @("starting_target", "rollback_starting", "failed")
    starting_target = @("target_healthy", "rollback_starting", "failed")
    target_healthy = @("continuation_dispatched", "continuation_enqueued", "failed", "rollback_starting")
    continuation_enqueued = @("continuation_dispatched", "completed", "failed", "rollback_starting")
    continuation_dispatched = @("completed", "failed")
    rollback_starting = @("rolled_back", "failed")
}

function Write-Phase([string]$Message)
{
    Write-Host "restart:$Message"
    Write-BootstrapLog $Message
}

function Write-BootstrapLog([string]$Message)
{
    try
    {
        Add-Content -LiteralPath $script:BootstrapLogPath -Value ("{0:o} {1}" -f [DateTimeOffset]::Now, $Message) -Encoding UTF8
    }
    catch
    {
    }
}

function Normalize-BusRoot([string]$busRoot)
{
    if ([string]::IsNullOrWhiteSpace($busRoot))
    {
        return ""
    }

    return [System.IO.Path]::GetFullPath($busRoot).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar).ToLowerInvariant()
}

function Get-KnownGoodCheckpoint([string]$sourceCwd)
{
    if ([string]::IsNullOrWhiteSpace($sourceCwd))
    {
        return $null
    }

    $checkpointPath = Join-Path $sourceCwd ".codexteamup/runtime/checkpoints/known-good.json"
    if (-not (Test-Path -LiteralPath $checkpointPath -PathType Leaf))
    {
        return $null
    }

    try
    {
        $content = Get-Content -LiteralPath $checkpointPath -Raw
        return ConvertFrom-Json -InputObject $content
    }
    catch
    {
        Write-Phase "warning known-good checkpoint read failed: $($_.Exception.Message)"
        return $null
    }
}

function Resolve-RollbackTarget([object]$operation)
{
    $fromFallback = if ([string]::IsNullOrWhiteSpace($operation.FallbackCwd))
    {
        $null
    }
    else
    {
        @{
            Cwd = $operation.FallbackCwd
            BusRoot = $operation.FallbackBusRoot
            NoPublish = $true
        }
    }

    if ($null -ne $fromFallback -and (Test-Path -LiteralPath $fromFallback.Cwd -PathType Container))
    {
        return $fromFallback
    }

    $checkpoint = Get-KnownGoodCheckpoint -sourceCwd $operation.SourceCwd
    if ($null -eq $checkpoint -or [string]::IsNullOrWhiteSpace($checkpoint.CheckoutCwd))
    {
        return $null
    }

    if (-not (Test-Path -LiteralPath $checkpoint.CheckoutCwd -PathType Container))
    {
        return $null
    }

    return @{
        Cwd = $checkpoint.CheckoutCwd
        BusRoot = if ([string]::IsNullOrWhiteSpace($checkpoint.BusRoot)) { $null } else { $checkpoint.BusRoot }
        NoPublish = if ($checkpoint.PSObject.Properties.Match("UseNoPublishOnRecovery").Count -gt 0)
        {
            $checkpoint.UseNoPublishOnRecovery
        }
        else
        {
            $true
        }
    }
}

function Get-ExchangeRoot([string]$operationTargetBusRoot)
{
    if ([string]::IsNullOrWhiteSpace($operationTargetBusRoot))
    {
        return $null
    }

    $busRoot = Resolve-Path -LiteralPath $operationTargetBusRoot -ErrorAction SilentlyContinue
    if ($null -eq $busRoot)
    {
        return $null
    }

    $busParent = Split-Path -Parent $busRoot.Path
    if ((Split-Path -Leaf $busParent).ToLowerInvariant() -eq ".codexteamup")
    {
        return Join-Path $busParent "exchange"
    }

    return Join-Path (Split-Path -Parent $operationTargetBusRoot) ".codexteamup/exchange"
}

function Update-ExchangeCorrelation([string]$exchangeRoot, [string]$correlationId, [object]$envelope)
{
    if ([string]::IsNullOrWhiteSpace($exchangeRoot) -or [string]::IsNullOrWhiteSpace($correlationId))
    {
        return
    }

    $correlationDirectory = Join-Path $exchangeRoot "correlations"
    New-Item -ItemType Directory -Force -Path $correlationDirectory | Out-Null
    $correlationPath = Join-Path $correlationDirectory "$correlationId.json"

    $existing = $null
    if (Test-Path -LiteralPath $correlationPath -PathType Leaf)
    {
        $raw = Get-Content -LiteralPath $correlationPath -Raw
        $existing = if ([string]::IsNullOrWhiteSpace($raw)) { $null } else { ConvertFrom-Json -InputObject $raw }
    }

    if ($null -eq $existing)
    {
        $existing = [pscustomobject]@{
            CorrelationId = $correlationId
            CreatedAt = [DateTimeOffset]::Now
            UpdatedAt = [DateTimeOffset]::Now
            MessageIds = @($envelope.MessageId)
            LastMessageId = $envelope.MessageId
            LastStatus = $envelope.Status
        }
    }
    else
    {
        $messageIds = if ($existing.MessageIds) { @($existing.MessageIds) } else { @() }
        if ($messageIds -notcontains $envelope.MessageId)
        {
            $messageIds += $envelope.MessageId
        }

        $existing = [pscustomobject]@{
            CorrelationId = $correlationId
            CreatedAt = if ($existing.CreatedAt) { $existing.CreatedAt } else { [DateTimeOffset]::Now }
            UpdatedAt = [DateTimeOffset]::Now
            MessageIds = $messageIds
            LastMessageId = $envelope.MessageId
            LastStatus = $envelope.Status
        }
    }

    $tempPath = "${correlationPath}.tmp"
    $existing | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $tempPath -Encoding UTF8
    Move-Item -LiteralPath $tempPath -Destination $correlationPath -Force
}

function New-RestartHandoff([object]$operation)
{
    $messageId = "restart-handoff-$([guid]::NewGuid().ToString("N"))"
    $exchangeRoot = Get-ExchangeRoot $operation.TargetBusRoot
    if ([string]::IsNullOrWhiteSpace($exchangeRoot))
    {
        throw "Target exchange root not resolvable from bus root $($operation.TargetBusRoot)"
    }

    $startupDirectory = Join-Path $exchangeRoot "startup\system\restart"
    New-Item -ItemType Directory -Force -Path $startupDirectory | Out-Null

    $payload = @{
        operationId = $operation.Id
        operationPath = (Resolve-Path -LiteralPath $script:OperationPath).Path
        sourceCwd = $operation.SourceCwd
        sourceBusRoot = $operation.SourceBusRoot
        targetCwd = $operation.TargetCwd
        targetBusRoot = $operation.TargetBusRoot
        targetAgentId = $operation.TargetAgentId
    }

    $envelope = [pscustomobject]@{
        MessageId = $messageId
        Kind = "restart"
        TargetScope = "system"
        TargetProject = Split-Path -Leaf $operation.TargetCwd
        TargetAgentId = $operation.TargetAgentId
        TargetThreadName = $operation.TargetAgentId
        CorrelationId = $operation.Id
        CausationId = $operation.Id
        CreatedAt = [DateTimeOffset]::Now
        ExpiresAt = [DateTimeOffset]::Now.AddHours(4)
        PayloadType = "application/json"
        Payload = $payload
        AttemptCount = 0
        Status = "pending"
    }

    $messagePath = Join-Path $startupDirectory "$messageId.json"
    $tempPath = "$messagePath.tmp"
    $envelope | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $tempPath -Encoding UTF8
    Move-Item -LiteralPath $tempPath -Destination $messagePath -Force

    Update-ExchangeCorrelation -exchangeRoot $exchangeRoot -correlationId $operation.Id -envelope $envelope

    return @{
        MessageId = $messageId
        MessagePath = $messagePath
        Envelope = $envelope
    }
}

function Wait-ForRestartProgress([string]$operationPath, [int]$TimeoutSeconds = 120, [int]$IntervalSeconds = 2)
{
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline)
    {
        Start-Sleep -Seconds $IntervalSeconds
        if (-not (Test-Path -LiteralPath $operationPath -PathType Leaf))
        {
            continue
        }

        $current = Get-Operation $operationPath
        if ($null -eq $current)
        {
            continue
        }

        $status = Get-ObjectPropertyValue $current "Status"
        if ([string]::IsNullOrWhiteSpace($status))
        {
            continue
        }

        $normalized = $status.ToLowerInvariant()
        if ($normalized -in @("continuation_dispatched", "completed", "failed", "rolled_back"))
        {
            return $normalized
        }
    }

    return $null
}

function Get-Operation([string]$path)
{
    if (-not (Test-Path -LiteralPath $path -PathType Leaf))
    {
        throw "Operation path not found: $path"
    }

    $content = Get-Content -LiteralPath $path -Raw
    if ([string]::IsNullOrWhiteSpace($content))
    {
        throw "Operation file is empty: $path"
    }

    return $content | ConvertFrom-Json
}

function Get-ObjectPropertyValue([Parameter(Mandatory=$true)] $object, [Parameter(Mandatory=$true)][string]$Name)
{
    $property = $object.PSObject.Properties[$Name]
    if ($null -eq $property)
    {
        return $null
    }

    return $property.Value
}

function Set-ObjectPropertyValue([Parameter(Mandatory=$true)] $object, [Parameter(Mandatory=$true)][string]$Name, $Value)
{
    $property = $object.PSObject.Properties[$Name]
    if ($null -eq $property)
    {
        Add-Member -InputObject $object -NotePropertyName $Name -NotePropertyValue $Value -Force
        return
    }

    $property.Value = $Value
}

function Write-Operation(
    [Parameter(Mandatory=$true)] $operation,
    [string]$Status,
    [string]$HelperPid = $null,
    [string]$ContinuationTaskId = $null,
    [string]$StartupHandoffMessageId = $null,
    [string]$LastError = $null)
{
    $currentStatusValue = Get-ObjectPropertyValue $operation "Status"
    $currentStatus = if ($currentStatusValue) { $currentStatusValue.ToLowerInvariant() } else { "" }
    $nextStatus = if ($Status) { $Status.ToLowerInvariant() } else { $currentStatus }
    if (-not ($currentStatus -eq "" -or $currentStatus -eq $nextStatus))
    {
        if (-not $validTransitions.ContainsKey($currentStatus) -or -not ($validTransitions[$currentStatus] -contains $nextStatus))
        {
            throw "Invalid restart status transition '$currentStatus' -> '$nextStatus'."
        }
    }

    Set-ObjectPropertyValue $operation "Status" $nextStatus
    if (-not [string]::IsNullOrWhiteSpace($HelperPid))
    {
        Set-ObjectPropertyValue $operation "HelperPid" $HelperPid
    }

    if (-not [string]::IsNullOrWhiteSpace($ContinuationTaskId))
    {
        Set-ObjectPropertyValue $operation "ContinuationTaskId" $ContinuationTaskId
    }

    if (-not [string]::IsNullOrWhiteSpace($StartupHandoffMessageId))
    {
        Set-ObjectPropertyValue $operation "StartupHandoffMessageId" $StartupHandoffMessageId
    }

    if ($null -ne $LastError)
    {
        Set-ObjectPropertyValue $operation "LastError" $LastError
    }

    $completedAt = Get-ObjectPropertyValue $operation "CompletedAt"
    if ($terminalStatuses -contains $nextStatus -and -not $completedAt)
    {
        Set-ObjectPropertyValue $operation "CompletedAt" ([DateTimeOffset]::Now)
    }

    $tempPath = "${script:OperationPath}.tmp"
    $operation | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $tempPath -Encoding UTF8
    Move-Item -LiteralPath $tempPath -Destination $script:OperationPath -Force

    return $operation
}

function Invoke-WithRetries([scriptblock]$Action, [string]$Context)
{
    $maxRetries = 3
    for ($i = 0; $i -lt $maxRetries; $i++)
    {
        try
        {
            return & $Action
        }
        catch
        {
            if ($i -lt ($maxRetries - 1))
            {
                Start-Sleep -Seconds 1
                continue
            }

            throw "Failed ${Context}: $($_.Exception.Message)"
        }
    }
}

function Start-StartupScript([string]$cwd, [int]$preservePid, [switch]$NoPublish)
{
    $startupScript = Join-Path $cwd "scripts/start-codexteamup.ps1"
    if (-not (Test-Path -LiteralPath $startupScript -PathType Leaf))
    {
        throw "Startup script not found: $startupScript"
    }

    $argsList = @(
        "-ExecutionPolicy", "Bypass",
        "-File", $startupScript,
        "-RestartSupervisorMode",
        "-ForceStopExisting",
        "-PreservePid", $preservePid
    )
    if ($NoPublish)
    {
        $argsList += "-NoPublish"
    }
    Write-BootstrapLog "launching startup script detached for $cwd"
    $process = Start-Process "pwsh" -ArgumentList $argsList -WorkingDirectory $cwd -WindowStyle Hidden -PassThru
    if ($null -eq $process)
    {
        throw "Could not start startup script in $cwd."
    }

    return @{
        ExitCode = $null
        ProcessId = $process.Id
    }
}

function Test-TargetHealthy([string]$expectedBusRoot)
{
    try
    {
        $response = Invoke-RestMethod -Uri "http://127.0.0.1:47319/health" -TimeoutSec 2
        if (-not $response -or -not $response.status -or $response.status -ne "ok")
        {
            return $false
        }

        return (Normalize-BusRoot $response.defaultBusRoot) -eq (Normalize-BusRoot $expectedBusRoot)
    }
    catch
    {
        return $false
    }
}

function Get-TargetHealth([string]$expectedBusRoot, [int]$TimeoutSeconds = 120, [int]$IntervalSeconds = 2)
{
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline)
    {
        Start-Sleep -Seconds $IntervalSeconds
        if (Test-TargetHealthy -expectedBusRoot $expectedBusRoot)
        {
            return $true
        }
    }

    return $false
}

function Stop-SourceStartupConsoles([string]$sourceCwd, [int]$preservePid)
{
    if ([string]::IsNullOrWhiteSpace($sourceCwd))
    {
        return
    }

    $normalizedRoot = $sourceCwd.Replace('\', '/')
    $stale = @()
    foreach ($process in @(Get-CimInstance Win32_Process -Filter "Name='pwsh.exe' OR Name='powershell.exe'" -ErrorAction SilentlyContinue))
    {
        if (-not $process.ProcessId -or [int]$process.ProcessId -eq $PID -or ($preservePid -gt 0 -and [int]$process.ProcessId -eq $preservePid))
        {
            continue
        }

        $commandLine = if ($process.CommandLine) { $process.CommandLine.Replace('\', '/') } else { "" }
        if ([string]::IsNullOrWhiteSpace($commandLine))
        {
            continue
        }

        if ($commandLine.IndexOf($normalizedRoot, [StringComparison]::OrdinalIgnoreCase) -lt 0)
        {
            continue
        }

        if (($commandLine.IndexOf("scripts/start-codexteamup.ps1", [StringComparison]::OrdinalIgnoreCase) -ge 0) -or
            ($commandLine.IndexOf("scripts/restart-supervisor.ps1", [StringComparison]::OrdinalIgnoreCase) -ge 0))
        {
            $stale += $process
        }
    }

    foreach ($process in $stale)
    {
        try
        {
            Write-Phase "stopping stale console pid=$($process.ProcessId)"
            Stop-Process -Id ([int]$process.ProcessId) -Force -ErrorAction Stop
        }
        catch
        {
            Write-Phase "warning stale console pid=$($process.ProcessId) stop failed: $($_.Exception.Message)"
        }
    }
}

function Update-Phases(
    [string]$status,
    [string]$phase,
    [string]$helperPid = $null,
    [string]$continuationTaskId = $null,
    [string]$startupHandoffMessageId = $null,
    [string]$error = $null)
{
    script:Operation = Write-Operation -Operation $script:Operation -Status $status -HelperPid $helperPid -ContinuationTaskId $continuationTaskId -StartupHandoffMessageId $startupHandoffMessageId -LastError $phase
    if ($error -ne $null)
    {
        $script:Operation = Write-Operation -Operation $script:Operation -Status $status -HelperPid $helperPid -ContinuationTaskId $continuationTaskId -StartupHandoffMessageId $startupHandoffMessageId -LastError $error
    }
}

$script:OperationPath = (Resolve-Path -LiteralPath $OperationPath).Path
Write-BootstrapLog "resolved operation path $script:OperationPath"
$script:Operation = Get-Operation $script:OperationPath
Write-BootstrapLog "loaded operation status=$($script:Operation.Status)"

try
{
    Write-Phase "helper_started"
    $script:Operation = Write-Operation -Operation $script:Operation -Status "helper_started" -HelperPid $PID -LastError "phase=helper_started"

    Write-Phase "stopping_source"
    $script:Operation = Write-Operation -Operation $script:Operation -Status "stopping_source" -LastError "phase=stopping_source"
    Stop-SourceStartupConsoles -sourceCwd $script:Operation.SourceCwd -preservePid $supervisorPid
    $script:Operation = Write-Operation -Operation $script:Operation -Status "starting_target" -LastError "phase=stopping_source_done"

    Write-Phase "starting_target"
    $startupProcess = Start-StartupScript $script:Operation.TargetCwd $supervisorPid
    $script:Operation = Write-Operation -Operation $script:Operation -Status "starting_target" -LastError "phase=starting_target_launch pid=$($startupProcess.ProcessId)"
    $script:Operation = Write-Operation -Operation $script:Operation -Status "starting_target" -LastError "phase=starting_target_wait_health"

    $healthy = Get-TargetHealth -expectedBusRoot $script:Operation.TargetBusRoot
    if (-not $healthy)
    {
        throw "Target service did not become healthy within timeout for $($script:Operation.TargetCwd)."
    }

    Write-Phase "target_healthy"
    $script:Operation = Write-Operation -Operation $script:Operation -Status "target_healthy" -LastError "phase=target_healthy"
    $script:Operation = Write-Operation -Operation $script:Operation -Status "target_healthy" -LastError "phase=continuation_create"

    Write-Phase "continuation_create"
    $handoff = New-RestartHandoff -operation $script:Operation
    $handoffId = $handoff.MessageId
    Write-Phase "handoff_written id=$handoffId path=$($handoff.MessagePath)"
    $script:Operation = Write-Operation -Operation $script:Operation -Status "continuation_enqueued" -StartupHandoffMessageId $handoffId -LastError "phase=continuation_handoff_written id=$handoffId"

    $handoffStatus = Wait-ForRestartProgress -operationPath $script:OperationPath -TimeoutSeconds 60
    if ($handoffStatus -eq "continuation_dispatched")
    {
        Write-Phase "continuation_dispatched"
        $script:Operation = Write-Operation -Operation $script:Operation -Status "continuation_dispatched" -StartupHandoffMessageId $handoffId -LastError "phase=continuation_dispatched"
        $script:Operation = Write-Operation -Operation $script:Operation -Status "completed" -StartupHandoffMessageId $handoffId -LastError "phase=completed"
        Write-Phase "completed"
        exit 0
    }

    if ($handoffStatus -eq "completed")
    {
        Write-Phase "completed"
        $script:Operation = Write-Operation -Operation $script:Operation -Status "completed" -StartupHandoffMessageId $handoffId -LastError "phase=completed"
        exit 0
    }

    if (-not [string]::IsNullOrWhiteSpace($handoffStatus))
    {
        Write-Phase "operation_terminal_status=$handoffStatus"
    }

    Write-Phase "continuation_waited"
    Write-Host "continuation handoff is enqueued at $handoffId; waiting for target runtime import"
    exit 0
}
catch
{
    $errorMessage = $_.Exception.Message
    Write-Phase "error $errorMessage"
    $script:Operation = Write-Operation -Operation $script:Operation -Status $script:Operation.Status -LastError "phase=error:$errorMessage"

    if ($null -ne (Resolve-RollbackTarget -operation $script:Operation))
    {
        Write-Phase "rollback_starting"
        $script:Operation = Write-Operation -Operation $script:Operation -Status "rollback_starting" -LastError $errorMessage
        $fallbackTarget = Resolve-RollbackTarget -operation $script:Operation
        try
        {
            $fallbackProcess = Start-StartupScript -cwd $fallbackTarget.Cwd -preservePid $supervisorPid -NoPublish:$fallbackTarget.NoPublish
            $fallbackBusRoot = if ([string]::IsNullOrWhiteSpace($fallbackTarget.BusRoot))
            {
                Join-Path $fallbackTarget.Cwd ".codexteamup\agentbus"
            }
            else
            {
                $fallbackTarget.BusRoot
            }

            if (-not (Get-TargetHealth -expectedBusRoot $fallbackBusRoot -TimeoutSeconds 120))
            {
                throw "Rollback startup health check failed for $($fallbackTarget.Cwd)"
            }

            $script:Operation = Write-Operation -Operation $script:Operation -Status "rolled_back" -LastError "phase=rollback_complete pid=$($fallbackProcess.ProcessId)"
            Write-Phase "rolled_back"
            exit 1
        }
        catch
        {
            $script:Operation = Write-Operation -Operation $script:Operation -Status "failed" -LastError "rollback_failed:$($_.Exception.Message)"
            throw
        }
    }

    $script:Operation = Write-Operation -Operation $script:Operation -Status "failed" -LastError $errorMessage
    Write-Error $errorMessage
    exit 1
}
