param(
    [string]$Workspace = "",
    [string]$Configuration = "Release",
    [string]$RealCodexExe = "",
    [string]$DesktopExe = "",
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

function Add-CandidatePath {
    param(
        [System.Collections.Generic.List[string]]$Candidates,
        [string]$Path
    )

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return
    }

    $expanded = [Environment]::ExpandEnvironmentVariables($Path)
    if (-not [string]::IsNullOrWhiteSpace($expanded) -and -not $Candidates.Contains($expanded)) {
        $Candidates.Add($expanded)
    }
}

function Test-CodexCliCandidate {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return $false
    }

    try {
        $output = & $Path --version 2>$null
        return $LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace(($output | Select-Object -First 1))
    } catch {
        return $false
    }
}

function Resolve-CodexDesktopExe {
    param([string]$Path)

    if (-not [string]::IsNullOrWhiteSpace($Path)) {
        return Resolve-File -Path $Path -Name "Codex Desktop"
    }

    $candidates = [System.Collections.Generic.List[string]]::new()
    Add-CandidatePath -Candidates $candidates -Path ([Environment]::GetEnvironmentVariable("CODEX_DESKTOP_EXE", "Process"))
    Add-CandidatePath -Candidates $candidates -Path ([Environment]::GetEnvironmentVariable("CTU_CODEX_DESKTOP_EXE", "Process"))

    if (Get-Command Get-AppxPackage -ErrorAction SilentlyContinue) {
        $packages = @(Get-AppxPackage -Name "OpenAI.Codex" -ErrorAction SilentlyContinue |
            Sort-Object -Property @{ Expression = { [version]$_.Version }; Descending = $true })
        foreach ($package in $packages) {
            Add-CandidatePath -Candidates $candidates -Path (Join-Path $package.InstallLocation "app\Codex.exe")
        }
    }

    $windowsApps = Join-Path $env:ProgramFiles "WindowsApps"
    if (Test-Path -LiteralPath $windowsApps -PathType Container) {
        $packages = @(Get-ChildItem -LiteralPath $windowsApps -Directory -Filter "OpenAI.Codex_*__2p2nqsd0c76g0" -ErrorAction SilentlyContinue |
            Sort-Object -Property LastWriteTime, Name -Descending)
        foreach ($package in $packages) {
            Add-CandidatePath -Candidates $candidates -Path (Join-Path $package.FullName "app\Codex.exe")
        }
    }

    Add-CandidatePath -Candidates $candidates -Path (Join-Path $env:LOCALAPPDATA "Microsoft\WindowsApps\Codex.exe")

    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }

    $searched = if ($candidates.Count -gt 0) {
        "`nSearched:`n  " + (($candidates | ForEach-Object { $_ }) -join "`n  ")
    } else {
        ""
    }
    throw "Codex Desktop not found. Pass -DesktopExe or set CODEX_DESKTOP_EXE/CTU_CODEX_DESKTOP_EXE to the current Codex.exe path.$searched"
}

function Resolve-RealCodexExe {
    param(
        [string]$Path,
        [string]$DesktopPath
    )

    if (-not [string]::IsNullOrWhiteSpace($Path)) {
        $resolved = Resolve-File -Path $Path -Name "Real Codex CLI"
        if (Test-CodexCliCandidate -Path $resolved) {
            return $resolved
        }

        throw "Real Codex CLI is not runnable: $resolved. Pass the per-user CLI path, usually `"$env:LOCALAPPDATA\OpenAI\Codex\bin\codex.exe`", not the protected WindowsApps app resource."
    }

    $candidates = [System.Collections.Generic.List[string]]::new()

    Add-CandidatePath -Candidates $candidates -Path ([Environment]::GetEnvironmentVariable("CODEX_REAL_CLI_EXE", "Process"))
    Add-CandidatePath -Candidates $candidates -Path ([Environment]::GetEnvironmentVariable("CTU_REAL_CODEX_EXE", "Process"))
    Add-CandidatePath -Candidates $candidates -Path (Join-Path $env:LOCALAPPDATA "OpenAI\Codex\bin\codex.exe")
    Add-CandidatePath -Candidates $candidates -Path (Join-Path $env:LOCALAPPDATA "Microsoft\WindowsApps\codex.exe")

    if (-not [string]::IsNullOrWhiteSpace($DesktopPath)) {
        $desktopDirectory = Split-Path -Parent $DesktopPath
        Add-CandidatePath -Candidates $candidates -Path (Join-Path $desktopDirectory "resources\codex.exe")
    }

    $skipped = [System.Collections.Generic.List[string]]::new()
    foreach ($candidate in $candidates) {
        if (Test-CodexCliCandidate -Path $candidate) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }

        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            $skipped.Add($candidate)
        }
    }

    $searched = if ($candidates.Count -gt 0) {
        "`nSearched:`n  " + (($candidates | ForEach-Object { $_ }) -join "`n  ")
    } else {
        ""
    }
    $skippedText = if ($skipped.Count -gt 0) {
        "`nFound but not executable as a CLI:`n  " + (($skipped | ForEach-Object { $_ }) -join "`n  ")
    } else {
        ""
    }
    throw "Real Codex CLI not found. Pass -RealCodexExe or set CODEX_REAL_CLI_EXE/CTU_REAL_CODEX_EXE.$searched$skippedText"
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
$logRoot = Join-Path $repoRoot ".codexteamup\logs"
$workspacePath = if ([string]::IsNullOrWhiteSpace($Workspace)) {
    ""
} else {
    [System.IO.Path]::GetFullPath([Environment]::ExpandEnvironmentVariables($Workspace))
}

New-Item -ItemType Directory -Force -Path $logRoot | Out-Null

Write-Section "Inputs"
$desktopPath = Resolve-CodexDesktopExe -Path $DesktopExe
$realCodexPath = Resolve-RealCodexExe -Path $RealCodexExe -DesktopPath $desktopPath
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

$wrapperRequestTimeout = [Environment]::GetEnvironmentVariable("CODEX_WRAPPER_REQUEST_TIMEOUT_MS", "Process")
if (-not [string]::IsNullOrWhiteSpace($wrapperRequestTimeout)) {
    $desktopEnv.CODEX_WRAPPER_REQUEST_TIMEOUT_MS = $wrapperRequestTimeout
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
