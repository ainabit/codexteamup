param(
    [string]$Workspace = "",
    [int]$Port = 8765,
    [string]$CodexExe = "$env:LOCALAPPDATA\OpenAI\Codex\bin\codex.exe",
    [string]$DesktopExe = "",
    [string]$CodexHome = "$env:USERPROFILE\.codex",
    [switch]$Stop,
    [switch]$ProbeOnly,
    [switch]$NoLaunch,
    [switch]$AllowExistingDesktop
)

$ErrorActionPreference = "Stop"

function Write-Section {
    param([string]$Title)
    Write-Host ""
    Write-Host "== $Title =="
}

function Resolve-File {
    param(
        [string]$Path,
        [string]$Name
    )

    $expanded = [Environment]::ExpandEnvironmentVariables($Path)
    if (-not (Test-Path -LiteralPath $expanded -PathType Leaf)) {
        throw "$Name not found: $expanded"
    }

    return (Resolve-Path -LiteralPath $expanded).Path
}

function Wait-Readyz {
    param(
        [string]$Url,
        [int]$TimeoutSeconds = 15
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        try {
            $response = Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec 2
            if ($response.StatusCode -eq 200) {
                return $true
            }
        } catch {
            Start-Sleep -Milliseconds 250
        }
    } while ((Get-Date) -lt $deadline)

    return $false
}

function Set-TemporaryProcessEnvironment {
    param(
        [hashtable]$Values,
        [scriptblock]$Body
    )

    $previous = @{}
    foreach ($key in $Values.Keys) {
        $previous[$key] = [Environment]::GetEnvironmentVariable($key, "Process")
        Set-Item -LiteralPath "Env:$key" -Value $Values[$key]
    }

    try {
        & $Body
    } finally {
        foreach ($key in $Values.Keys) {
            if ($null -eq $previous[$key]) {
                Remove-Item -LiteralPath "Env:$key" -ErrorAction SilentlyContinue
            } else {
                Set-Item -LiteralPath "Env:$key" -Value $previous[$key]
            }
        }
    }
}

$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($Workspace)) {
    $Workspace = $repoRoot
}
$runRoot = Join-Path $repoRoot ".ctu\desktop-ws-probe"
$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$serverLog = Join-Path $runRoot "app-server-$timestamp.log"
$serverErr = Join-Path $runRoot "app-server-$timestamp.err.log"
$pidFile = Join-Path $runRoot "app-server.pid"
$wsUrl = "ws://127.0.0.1:$Port"
$readyzUrl = "http://127.0.0.1:$Port/readyz"

New-Item -ItemType Directory -Force -Path $runRoot | Out-Null

if ($Stop) {
    Write-Section "Stop App-Server"
    if (-not (Test-Path -LiteralPath $pidFile -PathType Leaf)) {
        Write-Host "pidFileMissing=$pidFile"
        return
    }

    $pidValue = (Get-Content -LiteralPath $pidFile | Select-Object -First 1).Trim()
    if ($pidValue -notmatch "^\d+$") {
        throw "Invalid PID file content: $pidValue"
    }

    $process = Get-Process -Id ([int]$pidValue) -ErrorAction SilentlyContinue
    if ($null -eq $process) {
        Write-Host "alreadyStoppedPid=$pidValue"
        return
    }

    Stop-Process -Id ([int]$pidValue) -Force
    Write-Host "stoppedPid=$pidValue"
    return
}

Write-Section "Inputs"
$codexPath = Resolve-File -Path $CodexExe -Name "Codex CLI"
$desktopPath = $null
if (-not $ProbeOnly) {
    if ([string]::IsNullOrWhiteSpace($DesktopExe)) {
        throw "Codex Desktop path is required for launch mode. Pass -DesktopExe or run with -ProbeOnly."
    }
    $desktopPath = Resolve-File -Path $DesktopExe -Name "Codex Desktop"
}
$workspacePath = [System.IO.Path]::GetFullPath([Environment]::ExpandEnvironmentVariables($Workspace))
$codexHomePath = [System.IO.Path]::GetFullPath([Environment]::ExpandEnvironmentVariables($CodexHome))

Write-Host "workspace=$workspacePath"
Write-Host "codexExe=$codexPath"
if ($desktopPath) {
    Write-Host "desktopExe=$desktopPath"
}
Write-Host "codexHome=$codexHomePath"
Write-Host "wsUrl=$wsUrl"
Write-Host "readyzUrl=$readyzUrl"
Write-Host "serverLog=$serverLog"
Write-Host "serverErr=$serverErr"

Write-Section "Preflight"
if (-not (Test-Path -LiteralPath $workspacePath -PathType Container)) {
    throw "Workspace directory not found: $workspacePath"
}

if (-not (Test-Path -LiteralPath $codexHomePath -PathType Container)) {
    throw "CODEX_HOME directory not found: $codexHomePath"
}

$existingDesktop = $null
if (-not $ProbeOnly -and -not $NoLaunch) {
    $existingDesktop = Get-Process Codex -ErrorAction SilentlyContinue
}

if ($existingDesktop -and -not $AllowExistingDesktop) {
    Write-Host "Codex Desktop is already running:"
    $existingDesktop | Select-Object Id, ProcessName, Path, MainWindowTitle | Format-Table -AutoSize
    throw "Close Codex Desktop first, or rerun with -AllowExistingDesktop. Existing instances may ignore the new CODEX_APP_SERVER_WS_URL environment."
}

try {
    $probe = Invoke-WebRequest -Uri $readyzUrl -UseBasicParsing -TimeoutSec 2
    if ($probe.StatusCode -eq 200) {
        throw "Port $Port already has a Codex app-server /readyz endpoint. Stop it or choose another -Port."
    }
} catch {
    if ($_.Exception.Message -like "Port $Port already*") {
        throw
    }
}

Write-Section "Start App-Server"
$serverEnv = @{
    CODEX_HOME = $codexHomePath
    RUST_LOG = "codex_app_server=info,codex_app_server_transport=info"
    LOG_FORMAT = "json"
}

$server = Set-TemporaryProcessEnvironment -Values $serverEnv -Body {
    Start-Process `
        -FilePath $codexPath `
        -ArgumentList @("app-server", "--listen", $wsUrl) `
        -WorkingDirectory $workspacePath `
        -RedirectStandardOutput $serverLog `
        -RedirectStandardError $serverErr `
        -WindowStyle Hidden `
        -PassThru
}

$server.Id | Set-Content -LiteralPath $pidFile

Write-Host "appServerPid=$($server.Id)"
Write-Host "pidFile=$pidFile"

if (-not (Wait-Readyz -Url $readyzUrl -TimeoutSeconds 15)) {
    if (-not $server.HasExited) {
        Stop-Process -Id $server.Id -Force
    }

    Write-Host "stderr tail:"
    if (Test-Path -LiteralPath $serverErr) {
        Get-Content -LiteralPath $serverErr -Tail 40
    }
    Write-Host "stdout tail:"
    if (Test-Path -LiteralPath $serverLog) {
        Get-Content -LiteralPath $serverLog -Tail 40
    }
    throw "App-server did not become ready on $wsUrl."
}

Write-Host "ready=True"

if ($ProbeOnly) {
    Write-Section "ProbeOnly"
    Write-Host "Standalone ws app-server became ready and will now be stopped."
    if (-not $server.HasExited) {
        Stop-Process -Id $server.Id -Force
    }
    Write-Host "stopped=True"
    return
}

if ($NoLaunch) {
    Write-Section "NoLaunch"
    Write-Host "App-server is running. Start Desktop manually with:"
    Write-Host "`$env:CODEX_APP_SERVER_WS_URL='$wsUrl'"
    Write-Host "`$env:CODEX_HOME='$codexHomePath'"
    Write-Host "`& '$desktopPath' '$workspacePath'"
    return
}

Write-Section "Launch Desktop"
$desktopEnv = @{
    CODEX_HOME = $codexHomePath
    CODEX_APP_SERVER_WS_URL = $wsUrl
    CODEX_ELECTRON_DISABLE_QUIT_CONFIRMATION = "1"
}

$desktop = Set-TemporaryProcessEnvironment -Values $desktopEnv -Body {
    Start-Process `
        -FilePath $desktopPath `
        -ArgumentList @($workspacePath) `
        -WorkingDirectory $workspacePath `
        -PassThru
}

Write-Host "desktopPid=$($desktop.Id)"
Write-Host "CODEX_APP_SERVER_WS_URL=$wsUrl"

Write-Section "Next"
Write-Host "Open Codex Desktop and check whether it loads the workspace via the external ws app-server."
Write-Host "If it does, the app-server log should show connection/activity:"
Write-Host $serverErr
Write-Host ""
Write-Host "To stop the test app-server later:"
Write-Host "Stop-Process -Id $($server.Id) -Force"
