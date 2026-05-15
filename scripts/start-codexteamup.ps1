param(
    [string]$Workspace = "",
    [string]$ServiceUrl = "http://127.0.0.1:47319/",
    [switch]$ForceTurnsAscending,
    [switch]$NoForceTurnsAscending,
    [switch]$NoStampTurnStartedAt,
    [switch]$NoPublish,
    [switch]$NoLaunch,
    [switch]$AllowExistingDesktop,
    [switch]$RestartService,
    [switch]$NoService,
    [switch]$NoConfigureMcp
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$publish = Join-Path $repoRoot "scripts\publish-ctu.ps1"
$desktopStart = Join-Path $repoRoot "scripts\start-codex-desktop-with-cli-wrapper.ps1"
$wrapperExe = Join-Path $repoRoot ".ctu\tools\wrapper\CodexTeamUp.CodexWrapper.exe"
$serviceExe = Join-Path $repoRoot ".ctu\tools\service\CodexTeamUp.Service.exe"
$serviceLogRoot = Join-Path $repoRoot ".ctu\service"

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

function Start-CodexTeamUpService {
    param(
        [int]$ParentProcessId
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
        Write-Host "Starting CodexTeamUp service at $ServiceUrl (watching pid=$ParentProcessId)"
        $stamp = Get-Date -Format "yyyyMMdd-HHmmss"
        $stdoutLog = Join-Path $serviceLogRoot "service-$stamp.out.log"
        $stderrLog = Join-Path $serviceLogRoot "service-$stamp.err.log"
        $previousServiceUrl = [Environment]::GetEnvironmentVariable("CTU_SERVICE_URL", "Process")
        $previousPipeName = [Environment]::GetEnvironmentVariable("CODEX_WRAPPER_PIPE_NAME", "Process")
        try {
            Set-Item -LiteralPath "Env:CTU_SERVICE_URL" -Value $ServiceUrl
            Set-Item -LiteralPath "Env:CODEX_WRAPPER_PIPE_NAME" -Value "codexteamup-appserver"
            Start-Process `
                -FilePath $serviceExe `
                -ArgumentList @("--url", $ServiceUrl, "--parent-pid", "$ParentProcessId") `
                -WorkingDirectory $repoRoot `
                -WindowStyle Hidden `
                -RedirectStandardOutput $stdoutLog `
                -RedirectStandardError $stderrLog `
                -PassThru | Out-Null
        } finally {
            if ($null -eq $previousServiceUrl) { Remove-Item -LiteralPath "Env:CTU_SERVICE_URL" -ErrorAction SilentlyContinue } else { Set-Item -LiteralPath "Env:CTU_SERVICE_URL" -Value $previousServiceUrl }
            if ($null -eq $previousPipeName) { Remove-Item -LiteralPath "Env:CODEX_WRAPPER_PIPE_NAME" -ErrorAction SilentlyContinue } else { Set-Item -LiteralPath "Env:CODEX_WRAPPER_PIPE_NAME" -Value $previousPipeName }
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
    } else {
        Write-Host "CodexTeamUp service already running at $ServiceUrl"
    }
}

if ($RestartService -or ((-not $NoService) -and (-not $NoPublish))) {
    Stop-ServiceListener -Url $ServiceUrl
}

if (-not $NoPublish) {
    & $publish
}

if (-not $NoConfigureMcp) {
    Set-CodexTeamUpMcpRegistration -Url $ServiceUrl
}

if (-not (Test-Path -LiteralPath $wrapperExe -PathType Leaf)) {
    $wrapperExe = Join-Path $repoRoot "src\CodexTeamUp.CodexWrapper\bin\Release\net10.0\CodexTeamUp.CodexWrapper.exe"
}

if (-not (Test-Path -LiteralPath $serviceExe -PathType Leaf)) {
    $serviceExe = Join-Path $repoRoot "src\CodexTeamUp.Service\bin\Release\net10.0\CodexTeamUp.Service.exe"
}

if (-not (Test-Path -LiteralPath $wrapperExe -PathType Leaf)) {
    throw "Wrapper not found: $wrapperExe. Close Codex Desktop, then run scripts\publish-ctu.ps1 or dotnet build -c Release."
}

$startArgs = @{
    WrapperExe = $wrapperExe
}

if (-not [string]::IsNullOrWhiteSpace($Workspace)) {
    $startArgs.Workspace = $Workspace
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

$desktopProcess = & $desktopStart @startArgs

if (-not $NoService) {
    $serviceParentPid = $PID
    if ($desktopProcess -and $desktopProcess.Id) {
        $serviceParentPid = $desktopProcess.Id
    }
    Start-CodexTeamUpService -ParentProcessId $serviceParentPid
}

Write-Host ""
Write-Host "CodexTeamUp agent backend:"
Write-Host "  Service URL: $ServiceUrl"
Write-Host "  Dashboard:   $($ServiceUrl.TrimEnd('/'))/"
Write-Host "  MCP URL:     $($ServiceUrl.TrimEnd('/'))/mcp"
Write-Host "  Agent tools: Codex Desktop uses the registered HTTP MCP endpoint."
