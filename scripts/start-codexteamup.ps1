param(
    [string]$Workspace = "",
    [string]$ServiceUrl = "http://127.0.0.1:47319/",
    [string]$DesktopExe = "",
    [string]$RealCodexExe = "",
    [string]$PipeName = "",
    [int]$PreservePid = 0,
    [switch]$ForceTurnsAscending,
    [switch]$NoForceTurnsAscending,
    [switch]$NoStampTurnStartedAt,
    [switch]$NoPublish,
    [switch]$NoLaunch,
    [switch]$AllowExistingDesktop,
    [switch]$ForceStopExisting,
    [switch]$KillExistingCodex,
    [switch]$RestartService,
    [switch]$NoService,
    [switch]$NoConfigureMcp,
    [switch]$RestartSupervisorMode
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$publish = Join-Path $repoRoot "scripts\publish-ctu.ps1"
$desktopStart = Join-Path $repoRoot "scripts\start-codex-desktop-with-cli-wrapper.ps1"
$wrapperExe = Join-Path $repoRoot ".ctu\tools\wrapper\CodexTeamUp.CodexWrapper.exe"
$serviceExe = Join-Path $repoRoot ".ctu\tools\service\CodexTeamUp.Service.exe"
$serviceLogRoot = Join-Path $repoRoot ".ctu\service"
$controllerRuntimeRoot = Join-Path $repoRoot ".ctu\runtime\controllers\default"
$controllerPluginPath = Join-Path $controllerRuntimeRoot "CodexTeamUp.Controller.Default.dll"
$controllerCurrentPluginPath = Join-Path $controllerRuntimeRoot "current-plugin.txt"
$sessionRoot = Join-Path $repoRoot ".ctu\sessions"
$currentSessionPath = Join-Path $sessionRoot "current.json"
$sessionId = [Guid]::NewGuid().ToString("N")

function Get-ProcessCommandLines {
    $map = @{}
    try {
        Get-CimInstance Win32_Process -ErrorAction Stop | ForEach-Object {
            if ($_.ProcessId) {
                $map[[int]$_.ProcessId] = $_.CommandLine
            }
        }
    } catch {
        Write-Warning "Could not inspect process command lines: $($_.Exception.Message)"
    }

    return $map
}

function Get-ProcessMetadata {
    $map = @{}
    try {
        $processes = @(Get-CimInstance Win32_Process -ErrorAction Stop)
        foreach ($process in $processes) {
            if ($process.ProcessId) {
                $map[[int]$process.ProcessId] = [pscustomobject]@{
                    CommandLine = $process.CommandLine
                    ParentProcessId = if ($process.ParentProcessId) { [int]$process.ParentProcessId } else { $null }
                    ParentName = $null
                }
            }
        }

        foreach ($process in $processes) {
            if (-not $process.ProcessId -or -not $process.ParentProcessId) {
                continue
            }

            $metadata = $map[[int]$process.ProcessId]
            $parentProcess = $processes | Where-Object { $_.ProcessId -eq $process.ParentProcessId } | Select-Object -First 1
            if ($metadata -and $parentProcess) {
                $metadata.ParentName = $parentProcess.Name
            }
        }
    } catch {
        Write-Warning "Could not inspect process metadata: $($_.Exception.Message)"
    }

    return $map
}

function Write-SupervisedPhase {
    param([string]$Phase)

    if ($RestartSupervisorMode) {
        Write-Host "startup:$Phase"
    }
}

function Get-ProcessPathSafe {
    param($Process)

    try {
        return $Process.Path
    } catch {
        return $null
    }
}

function Get-ControllerPluginPath {
    if (Test-Path -LiteralPath $controllerCurrentPluginPath -PathType Leaf) {
        $candidate = (Get-Content -LiteralPath $controllerCurrentPluginPath -Raw).Trim()
        if (-not [string]::IsNullOrWhiteSpace($candidate) -and (Test-Path -LiteralPath $candidate -PathType Leaf)) {
            return $candidate
        }
    }

    return $controllerPluginPath
}

function Test-ContainsIgnoreCase {
    param(
        [string]$Text,
        [string]$Value
    )

    if ([string]::IsNullOrWhiteSpace($Text) -or [string]::IsNullOrWhiteSpace($Value)) {
        return $false
    }

    return $Text.IndexOf($Value, [StringComparison]::OrdinalIgnoreCase) -ge 0
}

function Add-ProcessCandidate {
    param(
        [System.Collections.Generic.List[object]]$Candidates,
        $Process,
        [string]$Reason,
        [hashtable]$ProcessMetadata,
        [int]$PreservePid = 0
    )

    if ($null -eq $Process -or $Process.Id -eq $PID -or ($PreservePid -gt 0 -and $Process.Id -eq $PreservePid)) {
        return
    }

    if ($Candidates | Where-Object { $_.Id -eq $Process.Id } | Select-Object -First 1) {
        return
    }

    $path = Get-ProcessPathSafe -Process $Process
    $metadata = $ProcessMetadata[$Process.Id]
    $commandLine = if ($metadata) { $metadata.CommandLine } else { $null }
    $parentProcessId = if ($metadata) { $metadata.ParentProcessId } else { $null }
    $parentName = if ($metadata) { $metadata.ParentName } else { $null }
    $Candidates.Add([pscustomobject]@{
        Id = $Process.Id
        Name = $Process.ProcessName
        ParentProcessId = $parentProcessId
        ParentName = $parentName
        Reason = $Reason
        Path = $path
        CommandLine = $commandLine
    }) | Out-Null
}

function Get-SessionPropertyValue {
    param(
        $Session,
        [string]$Name
    )

    if ($null -eq $Session) {
        return $null
    }

    $property = $Session.PSObject.Properties[$Name]
    if ($null -ne $property) {
        return $property.Value
    }

    $pascalName = if ($Name.Length -gt 1) { $Name.Substring(0, 1).ToUpperInvariant() + $Name.Substring(1) } else { $Name.ToUpperInvariant() }
    $property = $Session.PSObject.Properties[$pascalName]
    if ($null -ne $property) {
        return $property.Value
    }

    return $null
}

function Read-CtuSessionManifest {
    if (-not (Test-Path -LiteralPath $currentSessionPath -PathType Leaf)) {
        return $null
    }

    try {
        $raw = Get-Content -LiteralPath $currentSessionPath -Raw
        if ([string]::IsNullOrWhiteSpace($raw)) {
            return $null
        }

        return ConvertFrom-Json -InputObject $raw
    } catch {
        Write-Warning "Could not read CTU session manifest: $($_.Exception.Message)"
        return $null
    }
}

function Add-SessionPidCandidate {
    param(
        [System.Collections.Generic.List[object]]$Candidates,
        [int]$ProcessId,
        [string[]]$ExpectedProcessNames,
        [string]$Reason,
        [hashtable]$ProcessMetadata,
        [int]$PreservePid = 0
    )

    if ($ProcessId -le 0 -or $ProcessId -eq $PID -or ($PreservePid -gt 0 -and $ProcessId -eq $PreservePid)) {
        return
    }

    $process = Get-Process -Id $ProcessId -ErrorAction SilentlyContinue
    if ($null -eq $process) {
        return
    }

    if ($ExpectedProcessNames.Count -gt 0 -and -not ($ExpectedProcessNames -contains $process.ProcessName)) {
        return
    }

    Add-ProcessCandidate -Candidates $Candidates -Process $process -Reason $Reason -ProcessMetadata $ProcessMetadata -PreservePid $PreservePid
}

function Add-CtuSessionProcessCandidates {
    param(
        [System.Collections.Generic.List[object]]$Candidates,
        [hashtable]$ProcessMetadata,
        [int]$PreservePid = 0
    )

    $session = Read-CtuSessionManifest
    if ($null -eq $session) {
        return
    }

    $checkout = Get-SessionPropertyValue -Session $session -Name "checkoutCwd"
    if ([string]::IsNullOrWhiteSpace($checkout)) {
        return
    }

    $normalizedCheckout = ([System.IO.Path]::GetFullPath($checkout)).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    $normalizedRepo = ([System.IO.Path]::GetFullPath($repoRoot)).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    if (-not $normalizedCheckout.Equals($normalizedRepo, [StringComparison]::OrdinalIgnoreCase)) {
        return
    }

    foreach ($entry in @(
        @{ Name = "servicePid"; Expected = @("CodexTeamUp.Service"); Reason = "Previous CTU session service must be replaced." },
        @{ Name = "desktopPid"; Expected = @("Codex", "codex"); Reason = "Previous CTU session desktop must be replaced." },
        @{ Name = "launcherPid"; Expected = @("pwsh", "powershell"); Reason = "Previous CTU startup console must be closed." }
    )) {
        $value = Get-SessionPropertyValue -Session $session -Name $entry.Name
        if ($null -ne $value) {
            Add-SessionPidCandidate -Candidates $Candidates -ProcessId ([int]$value) -ExpectedProcessNames $entry.Expected -Reason $entry.Reason -ProcessMetadata $ProcessMetadata -PreservePid $PreservePid
        }
    }

    $wrapperPids = Get-SessionPropertyValue -Session $session -Name "wrapperPids"
    if ($null -ne $wrapperPids) {
        foreach ($wrapperPid in @($wrapperPids)) {
            Add-SessionPidCandidate -Candidates $Candidates -ProcessId ([int]$wrapperPid) -ExpectedProcessNames @("CodexTeamUp.CodexWrapper") -Reason "Previous CTU session wrapper must be replaced." -ProcessMetadata $ProcessMetadata -PreservePid $PreservePid
        }
    }
}

function Get-CodexTeamUpProcessCandidates {
    $processMetadata = Get-ProcessMetadata
    $candidates = [System.Collections.Generic.List[object]]::new()

    Add-CtuSessionProcessCandidates -Candidates $candidates -ProcessMetadata $processMetadata -PreservePid $PreservePid

    if (-not $NoLaunch -and -not $AllowExistingDesktop) {
        foreach ($process in @(Get-Process -Name "Codex", "codex" -ErrorAction SilentlyContinue)) {
            Add-ProcessCandidate -Candidates $candidates -Process $process -Reason "Codex Desktop must be relaunched with the current wrapper environment." -ProcessMetadata $processMetadata -PreservePid $PreservePid
        }
    }

    if (-not $NoService -or $RestartService) {
        foreach ($process in @(Get-Process -Name "CodexTeamUp.Service" -ErrorAction SilentlyContinue)) {
            Add-ProcessCandidate -Candidates $candidates -Process $process -Reason "Existing CodexTeamUp service must be replaced." -ProcessMetadata $processMetadata -PreservePid $PreservePid
        }
    }

    if (-not $NoLaunch) {
        foreach ($process in @(Get-Process -Name "CodexTeamUp.CodexWrapper" -ErrorAction SilentlyContinue)) {
            Add-ProcessCandidate -Candidates $candidates -Process $process -Reason "Existing CodexTeamUp wrapper must be replaced." -ProcessMetadata $processMetadata -PreservePid $PreservePid
        }
    }

    foreach ($process in @(Get-Process -Name "pwsh", "powershell", "dotnet" -ErrorAction SilentlyContinue)) {
        if ($process.Id -eq $PID) {
            continue
        }

        $metadata = $processMetadata[$process.Id]
        $commandLine = if ($metadata) { $metadata.CommandLine } else { $null }
        if ([string]::IsNullOrWhiteSpace($commandLine)) {
            continue
        }

        $normalizedCommand = $commandLine.Replace('\', '/')
        $normalizedRoot = $repoRoot.Replace('\', '/')
        if (-not (Test-ContainsIgnoreCase -Text $normalizedCommand -Value $normalizedRoot)) {
            continue
        }

        if ((Test-ContainsIgnoreCase -Text $normalizedCommand -Value "scripts/start-codexteamup.ps1") -or
            (Test-ContainsIgnoreCase -Text $normalizedCommand -Value "scripts/restart-supervisor.ps1") -or
            (Test-ContainsIgnoreCase -Text $normalizedCommand -Value "scripts/test-codexteamup.ps1") -or
            (Test-ContainsIgnoreCase -Text $normalizedCommand -Value "scripts/test-live-multi-agent-orchestration.ps1") -or
            ((-not $NoService -or $RestartService) -and (Test-ContainsIgnoreCase -Text $normalizedCommand -Value "CodexTeamUp.Service")) -or
            ((-not $NoLaunch) -and (Test-ContainsIgnoreCase -Text $normalizedCommand -Value "CodexTeamUp.CodexWrapper"))) {
            Add-ProcessCandidate -Candidates $candidates -Process $process -Reason "CTU-related helper/test process from this repository is still running." -ProcessMetadata $processMetadata -PreservePid $PreservePid
        }
    }

    return @($candidates)
}

function Stop-CodexTeamUpProcessesForFreshStart {
    $candidates = @(Get-CodexTeamUpProcessCandidates)
    if ($candidates.Count -eq 0) {
        Write-Host "No existing CTU/Desktop processes require cleanup."
        return
    }

    Write-Host "Existing processes must be stopped for a fresh CodexTeamUp start:"
    $candidates |
        Select-Object Id, Name, ParentProcessId, ParentName, Reason, Path |
        Format-Table -AutoSize

    if (-not ($ForceStopExisting -or $KillExistingCodex)) {
        $answer = Read-Host "Stop these processes now? Type YES to continue"
        if ($answer -ne "YES") {
            throw "Aborted. Existing CTU/Desktop processes are still running; fresh start was not guaranteed."
        }
    }

    foreach ($candidate in $candidates) {
        try {
            Write-Host "Stopping $($candidate.Name) pid=$($candidate.Id)"
            Stop-Process -Id $candidate.Id -Force -ErrorAction Stop
        } catch {
            Write-Warning "Could not stop pid=$($candidate.Id): $($_.Exception.Message)"
        }
    }

    Start-Sleep -Seconds 1

    $remaining = @()
    foreach ($candidate in $candidates) {
        $process = Get-Process -Id $candidate.Id -ErrorAction SilentlyContinue
        if ($process) {
            $remaining += $candidate
        }
    }

    if ($remaining.Count -gt 0) {
        $remaining | Select-Object Id, Name, ParentProcessId, ParentName, Reason, Path | Format-Table -AutoSize
        throw "Could not stop all existing CTU/Desktop processes. Fresh start was not guaranteed."
    }
}

if ($RestartSupervisorMode -and -not $ForceStopExisting) {
    $ForceStopExisting = $true
}

if ([string]::IsNullOrWhiteSpace($PipeName)) {
    $PipeName = "codexteamup-appserver-$([Guid]::NewGuid().ToString("N").Substring(0, 12))"
} else {
    $PipeName = $PipeName.Trim()
}

function Stop-ServiceListener {
    param([string]$Url)

    $servicePort = ([Uri]$Url).Port
    $listeners = @(Get-NetTCPConnection -LocalPort $servicePort -State Listen -ErrorAction SilentlyContinue |
        Select-Object -ExpandProperty OwningProcess -Unique)
    foreach ($listenerPid in $listeners) {
        try {
            $process = Get-Process -Id $listenerPid -ErrorAction Stop
            Write-Host "Stopping existing listener on port $servicePort (pid=$listenerPid, process=$($process.ProcessName))"
            Stop-Process -Id $listenerPid -Force -ErrorAction Stop
        } catch {
            Write-Warning "Could not stop listener pid=$listenerPid on port ${servicePort}: $($_.Exception.Message)"
        }
    }
    if ($listeners.Count -gt 0) {
        Start-Sleep -Milliseconds 500
    }

    $serviceProcesses = @(Get-Process -Name "CodexTeamUp.Service" -ErrorAction SilentlyContinue)
    foreach ($serviceProcess in $serviceProcesses) {
        try {
            if ($serviceProcess.Path -and $serviceProcess.Path.StartsWith($repoRoot, [StringComparison]::OrdinalIgnoreCase)) {
                Write-Host "Stopping existing CodexTeamUp service process (pid=$($serviceProcess.Id))"
                Stop-Process -Id $serviceProcess.Id -Force -ErrorAction Stop
            }
        } catch {
            Write-Warning "Could not stop service process pid=$($serviceProcess.Id): $($_.Exception.Message)"
        }
    }
    if ($serviceProcesses.Count -gt 0) {
        Start-Sleep -Milliseconds 500
    }
}

function Set-CodexTeamUpMcpRegistration {
    param(
        [string]$Url
    )

    $codexHome = Join-Path $env:USERPROFILE ".codex"
    $configPath = Join-Path $codexHome "config.toml"
    New-Item -ItemType Directory -Force -Path $codexHome | Out-Null

    $text = ""
    if (Test-Path -LiteralPath $configPath -PathType Leaf) {
        $text = Get-Content -LiteralPath $configPath -Raw
        $backupPath = "$configPath.ctu-backup-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
        Copy-Item -LiteralPath $configPath -Destination $backupPath
    }

    $mcpUrl = $Url.TrimEnd("/") + "/mcp"
    $newBlock = @"
[mcp_servers.ctu]
url = "$mcpUrl"

[mcp_servers.ctu.tools.agentbus_init]
approval_mode = "approve"

[mcp_servers.ctu.tools.agentbus_list_agents]
approval_mode = "approve"

[mcp_servers.ctu.tools.agentbus_register_agent]
approval_mode = "approve"

[mcp_servers.ctu.tools.agentbus_create_task]
approval_mode = "approve"

[mcp_servers.ctu.tools.agentbus_list_tasks]
approval_mode = "approve"

[mcp_servers.ctu.tools.agentbus_claim_task]
approval_mode = "approve"

[mcp_servers.ctu.tools.agentbus_write_result]
approval_mode = "approve"

[mcp_servers.ctu.tools.agentbus_wait_result]
approval_mode = "approve"

[mcp_servers.ctu.tools.bridge_dispatch_task]
approval_mode = "approve"

[mcp_servers.ctu.tools.bridge_notify_result]
approval_mode = "approve"

[mcp_servers.ctu.tools.team_create_agent]
approval_mode = "approve"

[mcp_servers.ctu.tools.team_ensure_agents]
approval_mode = "approve"

[mcp_servers.ctu.tools.team_discover_agents]
approval_mode = "approve"

[mcp_servers.ctu.tools.team_send_message]
approval_mode = "approve"

[mcp_servers.ctu.tools.team_dashboard_export]
approval_mode = "approve"

[mcp_servers.ctu.tools.codex_thread_list]
approval_mode = "approve"

[mcp_servers.ctu.tools.codex_thread_read]
approval_mode = "approve"

[mcp_servers.ctu.tools.codex_thread_archive]
approval_mode = "approve"

[mcp_servers.ctu.tools.codex_turn_start]
approval_mode = "approve"

[mcp_servers.ctu.tools.codex_appserver_adapter_status]
approval_mode = "approve"

[mcp_servers.ctu.tools.codex_appserver_adapter_reload]
approval_mode = "approve"

[mcp_servers.ctu.tools.codex_controller_status]
approval_mode = "approve"

[mcp_servers.ctu.tools.codex_controller_reload]
approval_mode = "approve"

[mcp_servers.ctu.tools.codex_controller_policy_status]
approval_mode = "approve"

[mcp_servers.ctu.tools.codex_controller_policy_reload]
approval_mode = "approve"
"@

    $keptLines = [System.Collections.Generic.List[string]]::new()
    $skipMcpBlock = $false
    foreach ($line in ($text -split "\r?\n")) {
        if ($line -match '^\[mcp_servers\.ctu(\]|\.)') {
            $skipMcpBlock = $true
            continue
        }

        if ($skipMcpBlock -and $line -match '^\[') {
            $skipMcpBlock = $false
        }

        if (-not $skipMcpBlock) {
            $keptLines.Add($line)
        }
    }

    $text = $keptLines -join "`r`n"

    $baseLines = [System.Collections.Generic.List[string]]::new()
    foreach ($line in ($text -split "\r?\n")) {
        $baseLines.Add($line)
    }

    $insertAt = -1
    for ($i = 0; $i -lt $baseLines.Count; $i++) {
        if ($baseLines[$i] -match '^\[(marketplaces\.|plugins\.)') {
            $insertAt = $i
            break
        }
    }

    $newLines = [System.Collections.Generic.List[string]]::new()
    foreach ($line in ($newBlock.TrimEnd() -split "\r?\n")) {
        $newLines.Add($line)
    }
    $newLines.Add("")

    if ($insertAt -ge 0) {
        $baseLines.InsertRange($insertAt, $newLines)
        $text = $baseLines -join "`r`n"
    } else {
        $text = $text.TrimEnd() + "`r`n`r`n" + ($newLines -join "`r`n")
    }

    Set-Content -LiteralPath $configPath -Value $text -Encoding UTF8
    Write-Host "CodexTeamUp MCP registered in $configPath"
}

function Get-CtuWrapperProcessIds {
    $ids = [System.Collections.Generic.List[int]]::new()
    foreach ($process in @(Get-Process -Name "CodexTeamUp.CodexWrapper" -ErrorAction SilentlyContinue)) {
        try {
            if ($process.Path -and $process.Path.StartsWith($repoRoot, [StringComparison]::OrdinalIgnoreCase)) {
                $ids.Add([int]$process.Id) | Out-Null
            }
        } catch {
        }
    }

    return @($ids)
}

function Write-CtuSessionManifest {
    param(
        $DesktopProcess,
        $ServiceInfo
    )

    try {
        New-Item -ItemType Directory -Force -Path $sessionRoot | Out-Null
        $servicePid = if ($null -ne $ServiceInfo -and $null -ne $ServiceInfo.ServiceProcessId) { [int]$ServiceInfo.ServiceProcessId } else { $null }
        $desktopPid = if ($null -ne $DesktopProcess -and $null -ne $DesktopProcess.Id) { [int]$DesktopProcess.Id } else { $null }
        $manifest = [ordered]@{
            schemaVersion = 1
            sessionId = $sessionId
            checkoutCwd = [System.IO.Path]::GetFullPath($repoRoot)
            launcherPid = $PID
            launcherProcessName = (Get-Process -Id $PID).ProcessName
            startedAt = [DateTimeOffset]::Now
            restartSupervisorMode = [bool]$RestartSupervisorMode
            serviceUrl = $ServiceUrl
            pipeName = $PipeName
            workspace = $Workspace
            desktopPid = $desktopPid
            servicePid = $servicePid
            wrapperPids = @(Get-CtuWrapperProcessIds)
            runtimeRoot = $controllerRuntimeRoot
            controllerPluginPath = (Get-ControllerPluginPath)
        }

        $tempPath = "$currentSessionPath.$sessionId.tmp"
        $manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $tempPath -Encoding UTF8
        Move-Item -LiteralPath $tempPath -Destination $currentSessionPath -Force
    } catch {
        Write-Warning "Could not write CTU session manifest: $($_.Exception.Message)"
    }
}

function Start-CodexTeamUpService {
    param(
        [int]$ParentProcessId = 0,
        [string]$PipeName = "codexteamup-appserver"
    )

    New-Item -ItemType Directory -Force -Path $serviceLogRoot | Out-Null
    if (-not (Test-Path -LiteralPath $serviceExe -PathType Leaf)) {
        throw "Service not found: $serviceExe. Run scripts\publish-ctu.ps1 first."
    }

    $healthUrl = ($ServiceUrl.TrimEnd('/') + "/health")
    if ($RestartService) {
        Stop-ServiceListener -Url $ServiceUrl
    }

    $serviceRunning = $false
    try {
        Invoke-RestMethod -Uri $healthUrl -Method Get -TimeoutSec 2 | Out-Null
        $serviceRunning = $true
    } catch {
        $serviceRunning = $false
    }

    if (-not $serviceRunning) {
        Write-Host "Starting CodexTeamUp service at $ServiceUrl (watching pid=$ParentProcessId, pipe=$PipeName)"
        $stamp = Get-Date -Format "yyyyMMdd-HHmmss"
        $stdoutLog = Join-Path $serviceLogRoot "service-$stamp.out.log"
        $stderrLog = Join-Path $serviceLogRoot "service-$stamp.err.log"
        $previousServiceUrl = [Environment]::GetEnvironmentVariable("CTU_SERVICE_URL", "Process")
        $previousPipeName = [Environment]::GetEnvironmentVariable("CODEX_WRAPPER_PIPE_NAME", "Process")
        $previousResponseTimeout = [Environment]::GetEnvironmentVariable("CTU_APP_SERVER_RESPONSE_TIMEOUT_MS", "Process")
        $previousLogRoot = [Environment]::GetEnvironmentVariable("CTU_LOG_ROOT", "Process")
        $previousControllerPolicy = [Environment]::GetEnvironmentVariable("CTU_CONTROLLER_POLICY_PATH", "Process")
        $previousControllerPlugin = [Environment]::GetEnvironmentVariable("CTU_CONTROLLER_PLUGIN_PATH", "Process")
        $serviceArguments = @("--url", $ServiceUrl)
        if ($ParentProcessId -gt 0) {
            $serviceArguments += @("--parent-pid", "$ParentProcessId")
        }
        try {
            Set-Item -LiteralPath "Env:CTU_SERVICE_URL" -Value $ServiceUrl
            Set-Item -LiteralPath "Env:CODEX_WRAPPER_PIPE_NAME" -Value $PipeName
            Set-Item -LiteralPath "Env:CTU_APP_SERVER_RESPONSE_TIMEOUT_MS" -Value "30000"
            Set-Item -LiteralPath "Env:CTU_LOG_ROOT" -Value (Join-Path $repoRoot ".codexteamup\logs")
            Set-Item -LiteralPath "Env:CTU_CONTROLLER_POLICY_PATH" -Value (Join-Path $repoRoot "config\ctu-controller-policy.json")
            Set-Item -LiteralPath "Env:CTU_CONTROLLER_PLUGIN_PATH" -Value (Get-ControllerPluginPath)
            $serviceProcess = Start-Process `
                -FilePath $serviceExe `
                -ArgumentList $serviceArguments `
                -WorkingDirectory $repoRoot `
                -WindowStyle Hidden `
                -RedirectStandardOutput $stdoutLog `
                -RedirectStandardError $stderrLog `
                -PassThru
        } finally {
            if ($null -eq $previousServiceUrl) { Remove-Item -LiteralPath "Env:CTU_SERVICE_URL" -ErrorAction SilentlyContinue } else { Set-Item -LiteralPath "Env:CTU_SERVICE_URL" -Value $previousServiceUrl }
            if ($null -eq $previousPipeName) { Remove-Item -LiteralPath "Env:CODEX_WRAPPER_PIPE_NAME" -ErrorAction SilentlyContinue } else { Set-Item -LiteralPath "Env:CODEX_WRAPPER_PIPE_NAME" -Value $previousPipeName }
            if ($null -eq $previousResponseTimeout) { Remove-Item -LiteralPath "Env:CTU_APP_SERVER_RESPONSE_TIMEOUT_MS" -ErrorAction SilentlyContinue } else { Set-Item -LiteralPath "Env:CTU_APP_SERVER_RESPONSE_TIMEOUT_MS" -Value $previousResponseTimeout }
            if ($null -eq $previousLogRoot) { Remove-Item -LiteralPath "Env:CTU_LOG_ROOT" -ErrorAction SilentlyContinue } else { Set-Item -LiteralPath "Env:CTU_LOG_ROOT" -Value $previousLogRoot }
            if ($null -eq $previousControllerPolicy) { Remove-Item -LiteralPath "Env:CTU_CONTROLLER_POLICY_PATH" -ErrorAction SilentlyContinue } else { Set-Item -LiteralPath "Env:CTU_CONTROLLER_POLICY_PATH" -Value $previousControllerPolicy }
            if ($null -eq $previousControllerPlugin) { Remove-Item -LiteralPath "Env:CTU_CONTROLLER_PLUGIN_PATH" -ErrorAction SilentlyContinue } else { Set-Item -LiteralPath "Env:CTU_CONTROLLER_PLUGIN_PATH" -Value $previousControllerPlugin }
        }

        $started = $false
        for ($i = 0; $i -lt 20; $i++) {
            try {
                Invoke-RestMethod -Uri $healthUrl -Method Get -TimeoutSec 1 | Out-Null
                $started = $true
                break
            } catch {
                Start-Sleep -Milliseconds 250
            }
        }

        if (-not $started) {
            Write-Host "Service stdout log: $stdoutLog"
            Write-Host "Service stderr log: $stderrLog"
            throw "CodexTeamUp service did not become healthy at $healthUrl."
        }

        return [pscustomobject]@{
            ServiceProcessId = if ($serviceProcess) { [int]$serviceProcess.Id } else { $null }
            AlreadyRunning = $false
        }
    } else {
        Write-Host "CodexTeamUp service already running at $ServiceUrl"
        $servicePort = ([Uri]$ServiceUrl).Port
        $listenerPid = @(Get-NetTCPConnection -LocalPort $servicePort -State Listen -ErrorAction SilentlyContinue |
            Select-Object -ExpandProperty OwningProcess -Unique |
            Select-Object -First 1)
        return [pscustomobject]@{
            ServiceProcessId = if ($listenerPid.Count -gt 0) { [int]$listenerPid[0] } else { $null }
            AlreadyRunning = $true
        }
    }
}

function Ensure-ControllerRuntime {
    if (Test-Path -LiteralPath $controllerPluginPath -PathType Leaf) {
        return
    }

    New-Item -ItemType Directory -Force -Path $controllerRuntimeRoot | Out-Null
    $servicePlugin = Join-Path (Split-Path -Parent $serviceExe) "CodexTeamUp.Controller.Default.dll"
    if (-not (Test-Path -LiteralPath $servicePlugin -PathType Leaf)) {
        throw "Controller runtime not found: $controllerPluginPath. Run scripts\publish-ctu.ps1 or scripts\publish-controller-runtime.ps1 first."
    }

    foreach ($file in Get-ChildItem -LiteralPath (Split-Path -Parent $servicePlugin) -File) {
        $extension = [System.IO.Path]::GetExtension($file.Name)
        if ($extension -in @(".dll", ".pdb", ".json")) {
            Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $controllerRuntimeRoot $file.Name) -Force
        }
    }

    Set-Content -LiteralPath $controllerCurrentPluginPath -Value $controllerPluginPath -Encoding UTF8
}

if (-not $NoLaunch -or -not $NoService -or $RestartService) {
    Write-SupervisedPhase "cleanup.begin"
    Stop-CodexTeamUpProcessesForFreshStart
    Write-SupervisedPhase "cleanup.done"
}

if ($RestartService -or ((-not $NoService) -and (-not $NoPublish))) {
    Write-SupervisedPhase "service-listener.stop"
    Stop-ServiceListener -Url $ServiceUrl
}

if (-not $NoPublish) {
    Write-SupervisedPhase "publish.begin"
    & $publish
    Write-SupervisedPhase "publish.done"
}

if (-not $NoConfigureMcp) {
    Write-SupervisedPhase "mcp-registration.begin"
    Set-CodexTeamUpMcpRegistration -Url $ServiceUrl
    Write-SupervisedPhase "mcp-registration.done"
}

if (-not (Test-Path -LiteralPath $wrapperExe -PathType Leaf)) {
    $wrapperExe = Join-Path $repoRoot "src\CodexTeamUp.CodexWrapper\bin\Release\net10.0\CodexTeamUp.CodexWrapper.exe"
}

if (-not (Test-Path -LiteralPath $serviceExe -PathType Leaf)) {
    $serviceExe = Join-Path $repoRoot "src\CodexTeamUp.Service\bin\Release\net10.0\CodexTeamUp.Service.exe"
}

Write-SupervisedPhase "controller-runtime.ensure"
Ensure-ControllerRuntime

if (-not (Test-Path -LiteralPath $wrapperExe -PathType Leaf)) {
    throw "Wrapper not found: $wrapperExe. Close Codex Desktop, then run scripts\publish-ctu.ps1 or dotnet build -c Release."
}

$startArgs = @{
    WrapperExe = $wrapperExe
    PipeName = $PipeName
}

if (-not [string]::IsNullOrWhiteSpace($Workspace)) {
    $startArgs.Workspace = $Workspace
}

if (-not [string]::IsNullOrWhiteSpace($DesktopExe)) {
    $startArgs.DesktopExe = $DesktopExe
}

if (-not [string]::IsNullOrWhiteSpace($RealCodexExe)) {
    $startArgs.RealCodexExe = $RealCodexExe
}

if ($ForceTurnsAscending) {
    Write-Warning "-ForceTurnsAscending is now the default. Use -NoForceTurnsAscending to opt out."
}

if (-not $NoForceTurnsAscending) {
    $startArgs.ForceTurnsAscending = $true
}

if (-not $NoStampTurnStartedAt) {
    $startArgs.StampTurnStartedAt = $true
}

if ($NoLaunch) {
    $startArgs.NoLaunch = $true
}

if ($AllowExistingDesktop) {
    $startArgs.AllowExistingDesktop = $true
}

$startArgs.PassThru = $true
$startArgs.CtuServiceUrl = $ServiceUrl
$previousWrapperTimeout = [Environment]::GetEnvironmentVariable("CODEX_WRAPPER_REQUEST_TIMEOUT_MS", "Process")
try {
    Set-Item -LiteralPath "Env:CODEX_WRAPPER_REQUEST_TIMEOUT_MS" -Value "30000"

    $desktopProcess = & $desktopStart @startArgs
} finally {
    if ($null -eq $previousWrapperTimeout) { Remove-Item -LiteralPath "Env:CODEX_WRAPPER_REQUEST_TIMEOUT_MS" -ErrorAction SilentlyContinue } else { Set-Item -LiteralPath "Env:CODEX_WRAPPER_REQUEST_TIMEOUT_MS" -Value $previousWrapperTimeout }
}

if (-not $NoService) {
    $serviceParentPid = if ($NoLaunch) { 0 } else { $PID }
    if ($desktopProcess -and $desktopProcess.Id) {
        $serviceParentPid = $desktopProcess.Id
    }
    Write-SupervisedPhase "service.start.begin"
    $serviceInfo = Start-CodexTeamUpService -ParentProcessId $serviceParentPid -PipeName $PipeName
    Write-SupervisedPhase "service.start.done"
}

Write-CtuSessionManifest -DesktopProcess $desktopProcess -ServiceInfo $serviceInfo

Write-Host ""
if ($RestartSupervisorMode) {
    Write-Host "startup:ready"
    return
}
Write-Host "CodexTeamUp agent backend:"
Write-Host "  Service URL: $ServiceUrl"
Write-Host "  Dashboard:   $($ServiceUrl.TrimEnd('/'))/"
Write-Host "  MCP URL:     $($ServiceUrl.TrimEnd('/'))/mcp"
Write-Host "  Pipe name:   $PipeName"
Write-Host "  Agent tools: Codex Desktop uses the registered HTTP MCP endpoint."
