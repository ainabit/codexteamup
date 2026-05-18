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

function Write-Operation([Parameter(Mandatory=$true)] $operation, [string]$Status, [string]$HelperPid = $null, [string]$ContinuationTaskId = $null, [string]$LastError = $null)
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

function Start-StartupScript([string]$cwd, [int]$preservePid)
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

function Resolve-CliRunner([string]$rootCwd)
{
    $publishedCli = Join-Path $rootCwd ".ctu/tools/ctu/CodexTeamUp.Cli.exe"
    if (Test-Path -LiteralPath $publishedCli -PathType Leaf)
    {
        return @{
            FileName = (Resolve-Path -LiteralPath $publishedCli).Path
            ArgumentPrefix = @()
        }
    }

    $cliProject = Join-Path $rootCwd "src/CodexTeamUp.Cli/CodexTeamUp.Cli.csproj"
    if (Test-Path -LiteralPath $cliProject -PathType Leaf)
    {
        return @{
            FileName = "dotnet"
            ArgumentPrefix = @("run", "--project", $cliProject, "--")
        }
    }

    throw "No CLI executable found in $rootCwd."
}

function Invoke-Cli([string]$rootCwd, [string[]]$Arguments)
{
    $runner = Resolve-CliRunner $rootCwd
    $processArguments = @($runner.ArgumentPrefix) + $Arguments
    $outputLines = @()

    $previous = Get-Location
    try
    {
        Set-Location $rootCwd
        if ($runner.FileName -ieq "dotnet")
        {
            $outFile = [IO.Path]::GetTempFileName()
            $errFile = [IO.Path]::GetTempFileName()
            $process = Start-Process "dotnet" -ArgumentList $processArguments -NoNewWindow -Wait -PassThru -RedirectStandardOutput $outFile -RedirectStandardError $errFile
            if ($process.ExitCode -ne 0)
            {
                $errorOutput = if (Test-Path $errFile) { Get-Content -LiteralPath $errFile -Raw } else { "" }
                throw "CLI invocation failed with exit code $($process.ExitCode): $errorOutput"
            }
            $outputLines = if (Test-Path $outFile) { Get-Content -LiteralPath $outFile } else { @() }
            Remove-Item -LiteralPath $outFile, $errFile -ErrorAction SilentlyContinue
        }
        else
        {
            $outputLines = & $runner.FileName @processArguments
            if ($LASTEXITCODE -ne 0)
            {
                throw "CLI invocation failed with exit code $LASTEXITCODE."
            }
        }
    }
    finally
    {
        Set-Location $previous
    }

    return $outputLines
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

function New-ContinuationTask([object]$operation)
{
    $from = if ([string]::IsNullOrWhiteSpace($operation.RequestedByAgentId)) { $operation.TargetAgentId } else { $operation.RequestedByAgentId }
    $title = if ([string]::IsNullOrWhiteSpace($operation.ContinueTitle)) { "Continue after restart" } else { $operation.ContinueTitle }
    $prompt = if ([string]::IsNullOrWhiteSpace($operation.ContinuePrompt))
    {
        "Please continue the source task after restart and inspect the state in the operation record."
    }
    else
    {
        $operation.ContinuePrompt
    }

    $promptPath = Join-Path $env:TEMP ("ctu-restart-continue-prompt-$($operation.Id).txt")
    Set-Content -LiteralPath $promptPath -Value $prompt -Encoding UTF8

    $args = @(
        "--bus-root", $operation.TargetBusRoot,
        "bus", "task", "create",
        "--from", $from,
        "--to", $operation.TargetAgentId,
        "--title", $title,
        "--prompt-file", $promptPath,
        "--project", (Split-Path -Leaf $operation.TargetCwd),
        "--cwd", $operation.TargetCwd
    )
    if (-not [string]::IsNullOrWhiteSpace($operation.RequestedByAgentId))
    {
        $args += "--return-to", $operation.RequestedByAgentId
    }

    $output = Invoke-Cli $operation.TargetCwd $args
    Remove-Item -LiteralPath $promptPath -ErrorAction SilentlyContinue

    $taskId = ($output | ForEach-Object {
        if ($_ -match '^created:\s*(\S+)')
        {
            return $Matches[1]
        }
    } | Select-Object -First 1)

    if ([string]::IsNullOrWhiteSpace($taskId))
    {
        throw "Unable to parse continuation task id from CLI output."
    }

    return $taskId
}

function Send-Continuation([object]$operation, [string]$taskId)
{
    $args = @(
        "--bus-root", $operation.TargetBusRoot,
        "dispatch",
        "--task-id", $taskId,
        "--to-agent", $operation.TargetAgentId,
        "--yes"
    )

    $dispatchOutput = Invoke-Cli $operation.TargetCwd $args
    return $dispatchOutput
}

function Update-Phases([string]$status, [string]$phase, [string]$helperPid = $null, [string]$continuationTaskId = $null, [string]$error = $null)
{
    script:Operation = Write-Operation -Operation $script:Operation -Status $status -HelperPid $helperPid -ContinuationTaskId $continuationTaskId -LastError $phase
    if ($error -ne $null)
    {
        $script:Operation = Write-Operation -Operation $script:Operation -Status $status -HelperPid $helperPid -ContinuationTaskId $continuationTaskId -LastError $error
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
    $createdTaskId = New-ContinuationTask -operation $script:Operation
    $dispatchedOutput = Send-Continuation -operation $script:Operation -taskId $createdTaskId

    if ($null -eq $dispatchedOutput)
    {
        throw "Continuation task was enqueued but dispatch failed."
    }

    Write-Phase "continuation_dispatched"
    $script:Operation = Write-Operation -Operation $script:Operation -Status "continuation_dispatched" -ContinuationTaskId $createdTaskId -LastError "phase=continuation_dispatched"
    $script:Operation = Write-Operation -Operation $script:Operation -Status "completed" -ContinuationTaskId $createdTaskId -LastError "phase=completed"
    Write-Phase "completed"
    exit 0
}
catch
{
    $errorMessage = $_.Exception.Message
    Write-Phase "error $errorMessage"
    $script:Operation = Write-Operation -Operation $script:Operation -Status $script:Operation.Status -LastError "phase=error:$errorMessage"

    if (-not [string]::IsNullOrWhiteSpace($script:Operation.FallbackCwd))
    {
        Write-Phase "rollback_starting"
        $script:Operation = Write-Operation -Operation $script:Operation -Status "rollback_starting" -LastError $errorMessage
        $fallbackProcess = Start-StartupScript $script:Operation.FallbackCwd $supervisorPid
        $script:Operation = Write-Operation -Operation $script:Operation -Status "rolled_back" -LastError "phase=rollback_complete pid=$($fallbackProcess.ProcessId)"
        Write-Phase "rolled_back"
        exit 1
    }

    $script:Operation = Write-Operation -Operation $script:Operation -Status "failed" -LastError $errorMessage
    Write-Error $errorMessage
    exit 1
}
