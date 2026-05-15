param(
    [string]$Workspace = "",
    [string]$Configuration = "Release",
    [string]$RealCodexExe = "$env:LOCALAPPDATA\OpenAI\Codex\bin\codex.exe",
    [string]$DesktopExe = "C:\Program Files\WindowsApps\OpenAI.Codex_26.506.3741.0_x64__2p2nqsd0c76g0\app\Codex.exe",
    [string]$WrapperExe = "",
    [string]$PipeName = "codexteamup-appserver",
    [string]$CtuServiceUrl = "",
    [switch]$ForceTurnsAscending,
    [switch]$StampTurnStartedAt,
    [switch]$Build,
    [switch]$NoLaunch,
    [switch]$AllowExistingDesktop,
    [switch]$PassThru
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
$wrapperProject = Join-Path $repoRoot "src\CodexTeamUp.CodexWrapper\CodexTeamUp.CodexWrapper.csproj"
if ([string]::IsNullOrWhiteSpace($WrapperExe)) {
    $wrapperExe = Join-Path $repoRoot "src\CodexTeamUp.CodexWrapper\bin\$Configuration\net10.0\CodexTeamUp.CodexWrapper.exe"
} else {
    $wrapperExe = [System.IO.Path]::GetFullPath([Environment]::ExpandEnvironmentVariables($WrapperExe))
}
$logRoot = Join-Path $repoRoot ".ctu\cli-wrapper-probe"
$workspacePath = if ([string]::IsNullOrWhiteSpace($Workspace)) {
    ""
} else {
    [System.IO.Path]::GetFullPath([Environment]::ExpandEnvironmentVariables($Workspace))
}

New-Item -ItemType Directory -Force -Path $logRoot | Out-Null

Write-Section "Inputs"
$realCodexPath = Resolve-File -Path $RealCodexExe -Name "Real Codex CLI"
$desktopPath = Resolve-File -Path $DesktopExe -Name "Codex Desktop"
Write-Host "workspace=$(if ([string]::IsNullOrWhiteSpace($workspacePath)) { '<desktop-default>' } else { $workspacePath })"
Write-Host "wrapperProject=$wrapperProject"
Write-Host "wrapperExe=$wrapperExe"
Write-Host "realCodexExe=$realCodexPath"
Write-Host "desktopExe=$desktopPath"
Write-Host "logRoot=$logRoot"
Write-Host "pipeName=$PipeName"
if (-not [string]::IsNullOrWhiteSpace($CtuServiceUrl)) {
    Write-Host "ctuServiceUrl=$CtuServiceUrl"
}
Write-Host "forceTurnsAscending=$ForceTurnsAscending"
Write-Host "stampTurnStartedAt=$StampTurnStartedAt"

Write-Section "Preflight"
if (-not [string]::IsNullOrWhiteSpace($workspacePath) -and -not (Test-Path -LiteralPath $workspacePath -PathType Container)) {
    throw "Workspace directory not found: $workspacePath"
}

if ($Build -or -not (Test-Path -LiteralPath $wrapperExe -PathType Leaf)) {
    Write-Host "Building wrapper..."
    dotnet build $wrapperProject -c $Configuration
}

if (-not (Test-Path -LiteralPath $wrapperExe -PathType Leaf)) {
    throw "Wrapper exe not found after build: $wrapperExe"
}

$existingDesktop = $null
if (-not $NoLaunch) {
    $existingDesktop = Get-Process Codex -ErrorAction SilentlyContinue
}

if ($existingDesktop -and -not $AllowExistingDesktop) {
    Write-Host "Codex Desktop is already running:"
    $existingDesktop | Select-Object Id, ProcessName, Path, MainWindowTitle | Format-Table -AutoSize
    throw "Close Codex Desktop first, or rerun with -AllowExistingDesktop. Existing instances may ignore CODEX_CLI_PATH."
}

Write-Section "Wrapper Smoke"
$versionOutput = Set-TemporaryProcessEnvironment -Values @{
    CODEX_WRAPPER_REAL_CODEX = $realCodexPath
    CODEX_WRAPPER_LOG_DIR = $logRoot
} -Body {
    & $wrapperExe --version
}
Write-Host "wrapper --version output:"
$versionOutput | ForEach-Object { Write-Host $_ }

if ($NoLaunch) {
    Write-Section "NoLaunch"
    Write-Host "Wrapper is built and can delegate to the real Codex CLI."
    Write-Host "Close Codex Desktop, then run:"
    Write-Host "`$env:CODEX_CLI_PATH='$wrapperExe'"
    Write-Host "`$env:CODEX_WRAPPER_REAL_CODEX='$realCodexPath'"
    Write-Host "`$env:CODEX_WRAPPER_LOG_DIR='$logRoot'"
    Write-Host "`$env:CODEX_WRAPPER_PIPE_NAME='$PipeName'"
    if (-not [string]::IsNullOrWhiteSpace($CtuServiceUrl)) {
        Write-Host "`$env:CTU_SERVICE_URL='$CtuServiceUrl'"
    }
    if ($ForceTurnsAscending) {
        Write-Host "`$env:CODEX_WRAPPER_FORCE_TURNS_ASC='1'"
    }
    if ($StampTurnStartedAt) {
        Write-Host "`$env:CODEX_WRAPPER_STAMP_TURN_STARTED_AT='1'"
    }
    if ([string]::IsNullOrWhiteSpace($workspacePath)) {
        Write-Host "`& '$desktopPath'"
    } else {
        Write-Host "`& '$desktopPath' '$workspacePath'"
    }
    return
}

Write-Section "Launch Desktop"
$desktopEnv = @{
    CODEX_CLI_PATH = $wrapperExe
    CODEX_WRAPPER_REAL_CODEX = $realCodexPath
    CODEX_WRAPPER_LOG_DIR = $logRoot
    CODEX_WRAPPER_PIPE_NAME = $PipeName
}

if (-not [string]::IsNullOrWhiteSpace($CtuServiceUrl)) {
    $desktopEnv.CTU_SERVICE_URL = $CtuServiceUrl
}

if ($ForceTurnsAscending) {
    $desktopEnv.CODEX_WRAPPER_FORCE_TURNS_ASC = "1"
}

if ($StampTurnStartedAt) {
    $desktopEnv.CODEX_WRAPPER_STAMP_TURN_STARTED_AT = "1"
}

$desktop = Set-TemporaryProcessEnvironment -Values $desktopEnv -Body {
    $startInfo = @{
        FilePath = $desktopPath
        PassThru = $true
    }

    if (-not [string]::IsNullOrWhiteSpace($workspacePath)) {
        $startInfo.ArgumentList = @($workspacePath)
        $startInfo.WorkingDirectory = $workspacePath
    }

    Start-Process @startInfo
}

Write-Host "desktopPid=$($desktop.Id)"
Write-Host "CODEX_CLI_PATH=$wrapperExe"
Write-Host "CODEX_WRAPPER_PIPE_NAME=$PipeName"
if (-not [string]::IsNullOrWhiteSpace($CtuServiceUrl)) {
    Write-Host "CTU_SERVICE_URL=$CtuServiceUrl"
}

Write-Section "Next"
Write-Host "Use Codex Desktop normally for a minute, then inspect wrapper logs:"
Write-Host "Get-ChildItem '$logRoot' | Sort-Object LastWriteTime -Descending | Select-Object -First 20"
Write-Host ""
Write-Host "If logs appear with args containing app-server, Desktop is using CODEX_CLI_PATH."
if ($PassThru) {
    Write-Output $desktop
}
