param(
    [switch]$Live,
    [switch]$LiveAll,
    [ValidateSet("basic", "peer", "replacement", "controller", "all")]
    [string]$LiveScenario = "basic",
    [string]$ServiceUrl = "http://127.0.0.1:47319/",
    [string]$Workspace = "",
    [string]$RunId = "",
    [string]$ArtifactsPath = "",
    [switch]$UseTestWorkspace,
    [string]$TestWorkspace = "",
    [switch]$UseAcceptanceWorkspace,
    [string]$AcceptanceWorkspace = "",
    [int]$TimeoutSeconds = 900,
    [int]$ToolTimeoutSeconds = 10,
    [switch]$Restore,
    [switch]$KeepLiveAgents,
    [switch]$Coverage,
    [int]$CoverageThreshold = 80,
    [string]$CoverageOutput = ""
)

$ErrorActionPreference = "Stop"

if ($UseTestWorkspace -and $UseAcceptanceWorkspace) {
    throw "Use either -UseTestWorkspace or -UseAcceptanceWorkspace, not both."
}

if ($PSVersionTable.PSEdition -ne "Core") {
    $pwsh = (Get-Command pwsh -ErrorAction SilentlyContinue).Source
    if (-not [string]::IsNullOrWhiteSpace($pwsh)) {
        $forward = @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $PSCommandPath)
        if ($Live) { $forward += "-Live" }
        if ($LiveAll) { $forward += "-LiveAll" }
        $forward += @("-LiveScenario", $LiveScenario)
        if (-not [string]::IsNullOrWhiteSpace($ServiceUrl)) { $forward += @("-ServiceUrl", $ServiceUrl) }
        if (-not [string]::IsNullOrWhiteSpace($Workspace)) { $forward += @("-Workspace", $Workspace) }
        if (-not [string]::IsNullOrWhiteSpace($RunId)) { $forward += @("-RunId", $RunId) }
        if (-not [string]::IsNullOrWhiteSpace($ArtifactsPath)) { $forward += @("-ArtifactsPath", $ArtifactsPath) }
        if ($UseTestWorkspace) { $forward += "-UseTestWorkspace" }
        if (-not [string]::IsNullOrWhiteSpace($TestWorkspace)) { $forward += @("-TestWorkspace", $TestWorkspace) }
        if ($UseAcceptanceWorkspace) { $forward += "-UseAcceptanceWorkspace" }
        if (-not [string]::IsNullOrWhiteSpace($AcceptanceWorkspace)) { $forward += @("-AcceptanceWorkspace", $AcceptanceWorkspace) }
        $forward += @("-TimeoutSeconds", $TimeoutSeconds)
        $forward += @("-ToolTimeoutSeconds", $ToolTimeoutSeconds)
        if ($Restore) { $forward += "-Restore" }
        if ($KeepLiveAgents) { $forward += "-KeepLiveAgents" }
        if ($Coverage) { $forward += "-Coverage" }
        $forward += @("-CoverageThreshold", $CoverageThreshold)
        if (-not [string]::IsNullOrWhiteSpace($CoverageOutput)) { $forward += @("-CoverageOutput", $CoverageOutput) }
        & $pwsh @forward
        exit $LASTEXITCODE
    }
}

$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($TestWorkspace)) {
    $TestWorkspace = Join-Path (Split-Path -Parent $repoRoot) ((Split-Path -Leaf $repoRoot) + ".test")
}

if ([string]::IsNullOrWhiteSpace($AcceptanceWorkspace)) {
    $AcceptanceWorkspace = Join-Path (Split-Path -Parent $repoRoot) ((Split-Path -Leaf $repoRoot) + ".acceptance")
}

if ($UseTestWorkspace -and [string]::IsNullOrWhiteSpace($Workspace)) {
    $Workspace = $TestWorkspace
}

if ($UseAcceptanceWorkspace -and [string]::IsNullOrWhiteSpace($Workspace)) {
    $Workspace = $AcceptanceWorkspace
}

if ([string]::IsNullOrWhiteSpace($Workspace)) {
    $Workspace = $repoRoot
}

$Workspace = [System.IO.Path]::GetFullPath($Workspace).Replace('\', '/')
$TestWorkspace = [System.IO.Path]::GetFullPath($TestWorkspace).Replace('\', '/')
$AcceptanceWorkspace = [System.IO.Path]::GetFullPath($AcceptanceWorkspace).Replace('\', '/')
if ([string]::IsNullOrWhiteSpace($ArtifactsPath)) {
    $ArtifactsPath = Join-Path ([System.IO.Path]::GetTempPath()) ("ctu-safety-artifacts-" + [guid]::NewGuid().ToString("N"))
}

$ArtifactsPath = [System.IO.Path]::GetFullPath($ArtifactsPath)

function Invoke-Checked {
    param(
        [Parameter(Mandatory = $true)]
        [scriptblock]$Command,

        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    & $Command
    if ($LASTEXITCODE -ne 0) {
        throw "$Name failed with exit code $LASTEXITCODE."
    }
}

function Invoke-CheckedWithBuildRetry {
    param(
        [Parameter(Mandatory = $true)]
        [scriptblock]$Command,

        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    & $Command
    if ($LASTEXITCODE -eq 0) {
        return
    }

    $firstExitCode = $LASTEXITCODE
    Write-Warning "$Name failed with exit code $firstExitCode. Shutting down dotnet build servers and retrying once."
    dotnet build-server shutdown | Out-Null
    Start-Sleep -Seconds 2

    & $Command
    if ($LASTEXITCODE -ne 0) {
        throw "$Name failed with exit code $LASTEXITCODE after retry. First exit code was $firstExitCode."
    }
}

function Initialize-TestWorkspace {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $nativePath = $Path.Replace('/', [System.IO.Path]::DirectorySeparatorChar)
    New-Item -ItemType Directory -Force -Path $nativePath | Out-Null
    New-Item -ItemType Directory -Force -Path (Join-Path $nativePath ".codexteamup") | Out-Null
    New-Item -ItemType Directory -Force -Path (Join-Path $nativePath ".codexteamup\agentbus") | Out-Null
    New-Item -ItemType Directory -Force -Path (Join-Path $nativePath "docs") | Out-Null

    $agentsPath = Join-Path $nativePath "AGENTS.md"
    if (-not (Test-Path -LiteralPath $agentsPath)) {
        @'
# CodexTeamUp Test Workspace

This workspace is for live CodexTeamUp smoke tests only.

Use run-scoped agent ids such as `ctu-test/<run-id>/agent-a`, `ctu-test/<run-id>/agent-b`, and `ctu-test/<run-id>/agent-c`.

Keep Git app directive `cwd` values in forward-slash form, for example `S:/_work/_development/codexteamup.test`.

Do not use this workspace for product changes. Write smoke-test evidence through AgentBus results.
'@ | Set-Content -LiteralPath $agentsPath -Encoding UTF8
    }

    $readmePath = Join-Path $nativePath "README.md"
    if (-not (Test-Path -LiteralPath $readmePath)) {
        @'
# codexteamup.test

Generated workspace for live CodexTeamUp smoke tests.

The source scripts stay in the main `codexteamup` repository. This workspace keeps live test AgentBus state separate from day-to-day project coordination.
'@ | Set-Content -LiteralPath $readmePath -Encoding UTF8
    }
}

function Initialize-AcceptanceWorkspace {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $nativePath = $Path.Replace('/', [System.IO.Path]::DirectorySeparatorChar)
    New-Item -ItemType Directory -Force -Path $nativePath | Out-Null
    New-Item -ItemType Directory -Force -Path (Join-Path $nativePath ".codexteamup") | Out-Null
    New-Item -ItemType Directory -Force -Path (Join-Path $nativePath ".codexteamup\agentbus") | Out-Null
    New-Item -ItemType Directory -Force -Path (Join-Path $nativePath "docs") | Out-Null

    $agentsPath = Join-Path $nativePath "AGENTS.md"
    if (-not (Test-Path -LiteralPath $agentsPath)) {
        @'
# CodexTeamUp Acceptance Workspace

This workspace is for fresh-clone and clean-checkout acceptance runs.

Use it to verify that CTU startup, MCP registration, dashboard loading, and the minimal live smoke path work without relying on the main development workspace state.

Keep Git app directive `cwd` values in forward-slash form, for example `S:/_work/_development/codexteamup.acceptance`.
'@ | Set-Content -LiteralPath $agentsPath -Encoding UTF8
    }

    $readmePath = Join-Path $nativePath "README.md"
    if (-not (Test-Path -LiteralPath $readmePath)) {
        @'
# codexteamup.acceptance

Generated workspace for fresh-clone and clean-checkout acceptance runs.

The source scripts stay in the main `codexteamup` repository. This workspace keeps acceptance AgentBus state isolated from day-to-day coordination and from `codexteamup.test`.
'@ | Set-Content -LiteralPath $readmePath -Encoding UTF8
    }
}

if (($Live -or $LiveAll) -and ($UseTestWorkspace -or $Workspace.EndsWith(".test", [StringComparison]::OrdinalIgnoreCase))) {
    Initialize-TestWorkspace -Path $Workspace
}

if ($UseAcceptanceWorkspace -or $Workspace.EndsWith(".acceptance", [StringComparison]::OrdinalIgnoreCase)) {
    Initialize-AcceptanceWorkspace -Path $Workspace
}

Write-Host "CodexTeamUp safety net"
Write-Host "  workspace: $Workspace"
Write-Host "  artifacts: $ArtifactsPath"
Write-Host ""

Write-Host "Running restore..."
Invoke-Checked -Name "dotnet restore" -Command { dotnet restore --disable-parallel --artifacts-path $ArtifactsPath }
Write-Host ""

Write-Host "Running build..."
Invoke-CheckedWithBuildRetry -Name "dotnet build" -Command { dotnet build --no-restore --disable-build-servers --artifacts-path $ArtifactsPath /p:UseSharedCompilation=false }

Write-Host ""
Write-Host "Running deterministic test suite..."
$testDll = Join-Path $ArtifactsPath "bin/CodexTeamUp.Tests/debug/CodexTeamUp.Tests.dll"
$testControllerPlugin = Join-Path (Split-Path -Parent $testDll) "CodexTeamUp.Controller.Default.dll"
$previousControllerPluginPath = [Environment]::GetEnvironmentVariable("CTU_CONTROLLER_PLUGIN_PATH", "Process")
$previousControllerPluginType = [Environment]::GetEnvironmentVariable("CTU_CONTROLLER_PLUGIN_TYPE", "Process")
$previousTestRepoRoot = [Environment]::GetEnvironmentVariable("CTU_TEST_REPO_ROOT", "Process")
try {
    Set-Item -LiteralPath "Env:CTU_CONTROLLER_PLUGIN_PATH" -Value $testControllerPlugin
    Set-Item -LiteralPath "Env:CTU_TEST_REPO_ROOT" -Value $repoRoot
    Remove-Item -LiteralPath "Env:CTU_CONTROLLER_PLUGIN_TYPE" -ErrorAction SilentlyContinue

    Invoke-Checked -Name "deterministic test suite" -Command { dotnet $testDll }

    if ($Coverage) {
        Write-Host ""
        Write-Host "Running coverage gate..."
        if ([string]::IsNullOrWhiteSpace($CoverageOutput)) {
            $CoverageOutput = Join-Path $ArtifactsPath "coverage"
        }

        New-Item -ItemType Directory -Force -Path $CoverageOutput | Out-Null
        Invoke-Checked -Name "dotnet tool restore" -Command { dotnet tool restore }
        Invoke-Checked -Name "coverage gate" -Command {
            dotnet tool run coverlet $testDll `
                --target "dotnet" `
                --targetargs "`"$testDll`"" `
                --format cobertura `
                --output "$CoverageOutput/" `
                --include "[CodexTeamUp.*]*" `
                --exclude "[CodexTeamUp.Tests]*" `
                --exclude-by-file "**/CodexTeamUp.CodexWrapper/Program.cs" `
                --exclude-by-file "**/System.Text.RegularExpressions.Generator/**/*.cs" `
                --threshold $CoverageThreshold `
                --threshold-type line `
                --threshold-stat total
        }
    }
} finally {
    if ($null -eq $previousControllerPluginPath) {
        Remove-Item -LiteralPath "Env:CTU_CONTROLLER_PLUGIN_PATH" -ErrorAction SilentlyContinue
    } else {
        Set-Item -LiteralPath "Env:CTU_CONTROLLER_PLUGIN_PATH" -Value $previousControllerPluginPath
    }

    if ($null -eq $previousControllerPluginType) {
        Remove-Item -LiteralPath "Env:CTU_CONTROLLER_PLUGIN_TYPE" -ErrorAction SilentlyContinue
    } else {
        Set-Item -LiteralPath "Env:CTU_CONTROLLER_PLUGIN_TYPE" -Value $previousControllerPluginType
    }

    if ($null -eq $previousTestRepoRoot) {
        Remove-Item -LiteralPath "Env:CTU_TEST_REPO_ROOT" -ErrorAction SilentlyContinue
    } else {
        Set-Item -LiteralPath "Env:CTU_TEST_REPO_ROOT" -Value $previousTestRepoRoot
    }
}

if (-not $Live -and -not $LiveAll) {
    Write-Host ""
    Write-Host "PASS CodexTeamUp deterministic safety net"
    exit 0
}

$scenarios = if ($LiveAll) {
    @("basic", "peer", "replacement")
} elseif ($LiveScenario -eq "all") {
    @("replacement")
} else {
    @($LiveScenario)
}

$baseRunId = $RunId
if ([string]::IsNullOrWhiteSpace($baseRunId)) {
    $baseRunId = Get-Date -Format "yyyyMMdd-HHmmss"
}

foreach ($scenario in $scenarios) {
    $scenarioRunId = if ($scenarios.Count -eq 1) {
        $baseRunId
    } else {
        "$baseRunId-$scenario"
    }

    Write-Host ""
    Write-Host "Running live smoke: $scenario"
    $liveArgs = @(
        "-ExecutionPolicy", "Bypass",
        "-File", (Join-Path $repoRoot "scripts/test-live-multi-agent-orchestration.ps1"),
        "-ServiceUrl", $ServiceUrl,
        "-Workspace", $Workspace,
        "-RunId", $scenarioRunId,
        "-Scenario", $scenario,
        "-TimeoutSeconds", $TimeoutSeconds,
        "-ToolTimeoutSeconds", $ToolTimeoutSeconds
    )

    if (-not $KeepLiveAgents) {
        $liveArgs += "-Cleanup"
    }

    $powerShellRunner = (Get-Command pwsh -ErrorAction SilentlyContinue).Source
    if ([string]::IsNullOrWhiteSpace($powerShellRunner)) {
        $powerShellRunner = (Get-Command powershell -ErrorAction Stop).Source
    }

    Invoke-Checked -Name "live smoke $scenario" -Command { & $powerShellRunner @liveArgs }
}

Write-Host ""
Write-Host "PASS CodexTeamUp safety net"
