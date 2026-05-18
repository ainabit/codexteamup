param(
    [switch]$Live,
    [switch]$LiveAll,
    [ValidateSet("surface", "basic", "peer", "replacement", "controller", "controller-suite", "continuation", "error-paths", "queue-first", "stale-claimed", "all")]
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
    [switch]$CleanupAllTestAgents,
    [switch]$Coverage,
    [int]$CoverageThreshold = 80,
    [string]$CoverageOutput = "",
    [string]$ReportPath = ""
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
        if ($CleanupAllTestAgents) { $forward += "-CleanupAllTestAgents" }
        if ($Coverage) { $forward += "-Coverage" }
        $forward += @("-CoverageThreshold", $CoverageThreshold)
        if (-not [string]::IsNullOrWhiteSpace($CoverageOutput)) { $forward += @("-CoverageOutput", $CoverageOutput) }
        if (-not [string]::IsNullOrWhiteSpace($ReportPath)) { $forward += @("-ReportPath", $ReportPath) }
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
if ([string]::IsNullOrWhiteSpace($ReportPath)) {
    $reportName = "codexteamup-safety-" + (Get-Date -Format "yyyyMMdd-HHmmss") + ".md"
    $ReportPath = Join-Path $repoRoot ".codexteamup\reports\$reportName"
}

$ReportPath = [System.IO.Path]::GetFullPath($ReportPath)
$ProgressPath = Join-Path (Split-Path -Parent $ReportPath) (([System.IO.Path]::GetFileNameWithoutExtension($ReportPath)) + ".progress.md")
$ProgressJsonPath = Join-Path (Split-Path -Parent $ReportPath) (([System.IO.Path]::GetFileNameWithoutExtension($ReportPath)) + ".progress.json")
$reportRows = [System.Collections.Generic.List[object]]::new()
$liveProgressRows = [System.Collections.Generic.List[object]]::new()

function Write-TestProgress {
    param(
        [string]$Phase = "",
        [string]$CurrentScenario = "",
        [string]$Status = "running",
        [string]$LastLine = "",
        [string]$Error = ""
    )

    $progressDirectory = Split-Path -Parent $ProgressPath
    if (-not [string]::IsNullOrWhiteSpace($progressDirectory)) {
        New-Item -ItemType Directory -Force -Path $progressDirectory | Out-Null
    }

    $total = $script:liveProgressRows.Count
    $done = @($script:liveProgressRows | Where-Object { $_.Status -eq "passed" -or $_.Status -eq "failed" }).Count
    $current = @($script:liveProgressRows | Where-Object { $_.Status -eq "running" } | Select-Object -First 1)
    $currentNumber = if ($null -ne $current) { [int]$current.Number } else { [Math]::Min($done + 1, [Math]::Max($total, 1)) }

    $snapshot = [pscustomobject]@{
        generated = (Get-Date).ToString("yyyy-MM-dd HH:mm:ss zzz")
        workspace = $Workspace
        report = $ReportPath
        phase = $Phase
        currentScenario = $CurrentScenario
        status = $Status
        liveCurrent = $currentNumber
        liveTotal = $total
        completed = $done
        lastLine = $LastLine
        error = $Error
        scenarios = @($script:liveProgressRows)
    }

    $snapshot | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $ProgressJsonPath -Encoding UTF8

    $lines = [System.Collections.Generic.List[string]]::new()
    $lines.Add("# CodexTeamUp Safety Progress")
    $lines.Add("")
    $lines.Add("- updated: $($snapshot.generated)")
    $lines.Add("- workspace: $Workspace")
    $lines.Add("- report: $ReportPath")
    $lines.Add("- phase: $Phase")
    $lines.Add("- status: $Status")
    if ($total -gt 0) {
        $lines.Add("- live progress: $currentNumber/$total")
        $lines.Add("- current scenario: $CurrentScenario")
    }
    if (-not [string]::IsNullOrWhiteSpace($LastLine)) {
        $lines.Add("- last line: $LastLine")
    }
    if (-not [string]::IsNullOrWhiteSpace($Error)) {
        $lines.Add("- error: $Error")
    }
    if ($total -gt 0) {
        $lines.Add("")
        $lines.Add("| # | Scenario | Status | Started | Finished |")
        $lines.Add("| --- | --- | --- | --- | --- |")
        foreach ($row in $script:liveProgressRows) {
            $lines.Add("| $($row.Number) | $(Convert-ReportCell $row.Scenario) | $(Convert-ReportCell $row.Status) | $(Convert-ReportCell $row.StartedAt) | $(Convert-ReportCell $row.FinishedAt) |")
        }
    }

    Set-Content -LiteralPath $ProgressPath -Value $lines -Encoding UTF8
}

function Add-ReportRow {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Category,

        [Parameter(Mandatory = $true)]
        [string]$TestCase,

        [Parameter(Mandatory = $true)]
        [ValidateSet("passed", "failed", "skipped")]
        [string]$Status,

        [string]$Reason = "",

        [string]$Details = ""
    )

    $script:reportRows.Add([pscustomobject]@{
        Category = $Category
        TestCase = $TestCase
        Status = $Status
        Reason = $Reason
        Details = $Details
    })
}

function Get-TestCategory {
    param([string]$Name)

    if ($Name -like "AgentBus*" -or $Name -like "*AgentBus*") { return "AgentBus protocol" }
    if ($Name -like "Controller*" -or $Name -like "*continuation*" -or $Name -like "*guardian*" -or $Name -like "*delivery loop*") { return "Controller continuity" }
    if ($Name -like "MCP*" -or $Name -like "*MCP*") { return "MCP surface" }
    if ($Name -like "Wrapper*" -or $Name -like "*Wrapper*" -or $Name -like "*app-server*" -or $Name -like "*app server*") { return "Desktop adapter" }
    if ($Name -like "Restart*" -or $Name -like "*Restart*" -or $Name -like "*startup*" -or $Name -like "*Exchange*") { return "Restart and exchange" }
    if ($Name -like "*dashboard*" -or $Name -like "*Dashboard*") { return "Dashboard" }
    if ($Name -like "*runtime*" -or $Name -like "*Runtime*") { return "Runtime settings" }
    return "Core"
}

function Convert-ReportCell {
    param([string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return ""
    }

    ($Value -replace "\r?\n", " " -replace "\|", "\|").Trim()
}

function Write-SafetyReport {
    $reportDirectory = Split-Path -Parent $ReportPath
    if (-not [string]::IsNullOrWhiteSpace($reportDirectory)) {
        New-Item -ItemType Directory -Force -Path $reportDirectory | Out-Null
    }

    $total = $reportRows.Count
    $passed = @($reportRows | Where-Object { $_.Status -eq "passed" }).Count
    $failed = @($reportRows | Where-Object { $_.Status -eq "failed" }).Count
    $skipped = @($reportRows | Where-Object { $_.Status -eq "skipped" }).Count

    $lines = [System.Collections.Generic.List[string]]::new()
    $lines.Add("# CodexTeamUp Safety Report")
    $lines.Add("")
    $lines.Add("- generated: $(Get-Date -Format "yyyy-MM-dd HH:mm:ss zzz")")
    $lines.Add("- workspace: $Workspace")
    $lines.Add("- artifacts: $ArtifactsPath")
    $lines.Add("- totals: $total total, $passed passed, $failed failed, $skipped skipped")
    $lines.Add("")
    $lines.Add("| Category | Test case | Status | Reason | Details |")
    $lines.Add("| --- | --- | --- | --- | --- |")

    foreach ($row in $reportRows) {
        $lines.Add("| $(Convert-ReportCell $row.Category) | $(Convert-ReportCell $row.TestCase) | $(Convert-ReportCell $row.Status) | $(Convert-ReportCell $row.Reason) | $(Convert-ReportCell $row.Details) |")
    }

    Set-Content -LiteralPath $ReportPath -Value $lines -Encoding UTF8
    Write-Host ""
    Write-Host "Safety report: $ReportPath"
}

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
Write-Host "  report:    $ReportPath"
Write-Host "  progress:  $ProgressPath"
Write-Host ""

Write-TestProgress -Phase "restore" -Status "running" -LastLine "Running dotnet restore"
Write-Host "Running restore..."
Invoke-Checked -Name "dotnet restore" -Command { dotnet restore --disable-parallel --artifacts-path $ArtifactsPath }
Add-ReportRow -Category "Build" -TestCase "Restore solution packages" -Status "passed" -Details "dotnet restore --artifacts-path"
Write-Host ""

Write-TestProgress -Phase "build" -Status "running" -LastLine "Running dotnet build"
Write-Host "Running build..."
Invoke-CheckedWithBuildRetry -Name "dotnet build" -Command { dotnet build --no-restore --disable-build-servers --artifacts-path $ArtifactsPath /p:UseSharedCompilation=false }
Add-ReportRow -Category "Build" -TestCase "Build solution into isolated artifacts" -Status "passed" -Details "No src/bin or src/obj runtime dependency"

Write-Host ""
Write-TestProgress -Phase "deterministic" -Status "running" -LastLine "Running deterministic test suite"
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

    $deterministicOutput = & dotnet $testDll 2>&1
    $deterministicExitCode = $LASTEXITCODE
    foreach ($lineObject in $deterministicOutput) {
        $line = [string]$lineObject
        Write-Host $line
        if ($line -match "^PASS\s+(.+)$") {
            $testName = $Matches[1].Trim()
            Add-ReportRow -Category (Get-TestCategory $testName) -TestCase $testName -Status "passed"
        } elseif ($line -match "^FAIL\s+([^:]+):\s*(.*)$") {
            $testName = $Matches[1].Trim()
            $reason = $Matches[2].Trim()
            Add-ReportRow -Category (Get-TestCategory $testName) -TestCase $testName -Status "failed" -Reason $reason
        }
    }

    if ($deterministicExitCode -ne 0) {
        if (-not ($reportRows | Where-Object { $_.Category -ne "Build" })) {
            Add-ReportRow -Category "Deterministic suite" -TestCase "Run deterministic test binary" -Status "failed" -Reason "dotnet $testDll exited with code $deterministicExitCode"
        }

        Write-SafetyReport
        Write-TestProgress -Phase "deterministic" -Status "failed" -Error "deterministic test suite failed with exit code $deterministicExitCode"
        throw "deterministic test suite failed with exit code $deterministicExitCode."
    }

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
        Add-ReportRow -Category "Coverage" -TestCase "Line coverage gate" -Status "passed" -Details "Threshold $CoverageThreshold percent"
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
    Write-TestProgress -Phase "complete" -Status "passed" -LastLine "PASS deterministic safety net"
    Write-SafetyReport
    exit 0
}

$scenarios = if ($LiveAll) {
    @("controller-suite", "basic", "peer", "replacement", "surface", "queue-first", "continuation", "error-paths", "stale-claimed")
} elseif ($LiveScenario -eq "all") {
    @("controller-suite", "basic", "peer", "replacement", "surface", "queue-first", "continuation", "error-paths", "stale-claimed")
} else {
    @($LiveScenario)
}
$scenarios = @($scenarios)
for ($scenarioIndex = 0; $scenarioIndex -lt $scenarios.Count; $scenarioIndex++) {
    $script:liveProgressRows.Add([pscustomobject]@{
        Number = $scenarioIndex + 1
        Scenario = $scenarios[$scenarioIndex]
        Status = "pending"
        StartedAt = ""
        FinishedAt = ""
    })
}
Write-TestProgress -Phase "live" -Status "running" -CurrentScenario $scenarios[0] -LastLine "Preparing live smoke scenarios"

$baseRunId = $RunId
if ([string]::IsNullOrWhiteSpace($baseRunId)) {
    $baseRunId = Get-Date -Format "yyyyMMdd-HHmmss"
}

for ($scenarioIndex = 0; $scenarioIndex -lt $scenarios.Count; $scenarioIndex++) {
    $scenario = $scenarios[$scenarioIndex]
    $scenarioNumber = $scenarioIndex + 1
    $progressRow = $script:liveProgressRows[$scenarioIndex]
    $progressRow.Status = "running"
    $progressRow.StartedAt = Get-Date -Format "HH:mm:ss"
    Write-TestProgress -Phase "live" -Status "running" -CurrentScenario $scenario -LastLine "Running live smoke ${scenarioNumber}/$($scenarios.Count): $scenario"
    $scenarioRunId = if ($scenarios.Count -eq 1) {
        $baseRunId
    } else {
        "$baseRunId-$scenario"
    }

    Write-Host ""
    Write-Host "Running live smoke ${scenarioNumber}/$($scenarios.Count): $scenario"
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

    $liveOutputBuffer = [System.Collections.Generic.List[string]]::new()
    & $powerShellRunner @liveArgs 2>&1 | ForEach-Object {
        $line = [string]$_
        $liveOutputBuffer.Add($line)
        Write-Host $line
        Write-TestProgress -Phase "live" -Status "running" -CurrentScenario $scenario -LastLine $line
    }
    $liveExitCode = $LASTEXITCODE
    $liveOutput = @($liveOutputBuffer)

    $liveText = ($liveOutput | ForEach-Object { [string]$_ }) -join "`n"
    if ($liveExitCode -eq 0) {
        $details = if ($liveText -match "PASS live CTU .+ smoke run ([^\r\n]+)") { "run $($Matches[1].Trim())" } else { "scenario completed" }
        Add-ReportRow -Category "Live smoke" -TestCase "$scenario scenario" -Status "passed" -Details $details
        $progressRow.Status = "passed"
        $progressRow.FinishedAt = Get-Date -Format "HH:mm:ss"
        Write-TestProgress -Phase "live" -Status "running" -CurrentScenario $scenario -LastLine "PASS live smoke ${scenarioNumber}/$($scenarios.Count): $scenario"
    } else {
        $reasonLines = @($liveOutput | Select-Object -Last 8 | ForEach-Object { [string]$_ })
        Add-ReportRow -Category "Live smoke" -TestCase "$scenario scenario" -Status "failed" -Reason ($reasonLines -join " ")
        $progressRow.Status = "failed"
        $progressRow.FinishedAt = Get-Date -Format "HH:mm:ss"
        Write-TestProgress -Phase "live" -Status "failed" -CurrentScenario $scenario -Error ($reasonLines -join " ")
        Write-SafetyReport
        throw "live smoke $scenario failed with exit code $liveExitCode."
    }
}

if (($LiveAll -or $CleanupAllTestAgents) -and -not $KeepLiveAgents) {
    Write-Host ""
    Write-Host "Running live cleanup: ctu-test/* except ctu-test/architect"
    Write-TestProgress -Phase "cleanup" -Status "running" -LastLine "Running live cleanup"
    $cleanupArgs = @(
        "-ExecutionPolicy", "Bypass",
        "-File", (Join-Path $repoRoot "scripts/test-live-multi-agent-orchestration.ps1"),
        "-ServiceUrl", $ServiceUrl,
        "-Workspace", $Workspace,
        "-RunId", $baseRunId,
        "-TimeoutSeconds", $TimeoutSeconds,
        "-ToolTimeoutSeconds", $ToolTimeoutSeconds,
        "-CleanupOnly",
        "-CleanupAllTestAgents"
    )

    $cleanupOutput = & $powerShellRunner @cleanupArgs 2>&1
    $cleanupExitCode = $LASTEXITCODE
    foreach ($lineObject in $cleanupOutput) {
        Write-Host ([string]$lineObject)
    }

    if ($cleanupExitCode -eq 0) {
        Add-ReportRow -Category "Live cleanup" -TestCase "Archive/retire ctu-test/* except ctu-test/architect" -Status "passed"
    } else {
        $reasonLines = @($cleanupOutput | Select-Object -Last 8 | ForEach-Object { [string]$_ })
        Add-ReportRow -Category "Live cleanup" -TestCase "Archive/retire ctu-test/* except ctu-test/architect" -Status "failed" -Reason ($reasonLines -join " ")
        Write-TestProgress -Phase "cleanup" -Status "failed" -Error ($reasonLines -join " ")
        Write-SafetyReport
        throw "live cleanup failed with exit code $cleanupExitCode."
    }
}

Write-Host ""
Write-Host "PASS CodexTeamUp safety net"
Write-TestProgress -Phase "complete" -Status "passed" -LastLine "PASS CodexTeamUp safety net"
Write-SafetyReport
