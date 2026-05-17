param(
    [string]$ServiceUrl = "http://127.0.0.1:47319/",
    [string]$Workspace = "",
    [string]$RunId = "",
    [int]$TimeoutSeconds = 900,
    [int]$ToolTimeoutSeconds = 10,
    [switch]$KeepLiveAgents
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($Workspace)) {
    $Workspace = $repoRoot
}

$Workspace = [System.IO.Path]::GetFullPath($Workspace).Replace('\', '/')
$ServiceUrl = $ServiceUrl.TrimEnd('/')
$BusRoot = "$Workspace/.codexteamup/agentbus"

function Assert-AcceptanceCheckout {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $nativePath = $Path.Replace('/', [System.IO.Path]::DirectorySeparatorChar)
    if (-not (Test-Path -LiteralPath $nativePath)) {
        throw "Acceptance workspace does not exist: $Path"
    }

    $gitPath = Join-Path $nativePath ".git"
    if (-not (Test-Path -LiteralPath $gitPath)) {
        throw "Acceptance must run inside a real cloned checkout or worktree. Missing .git at $Path"
    }

    $startupScript = Join-Path $nativePath "scripts\start-codexteamup.ps1"
    $runnerScript = Join-Path $nativePath "scripts\test-codexteamup.ps1"
    if (-not (Test-Path -LiteralPath $startupScript) -or -not (Test-Path -LiteralPath $runnerScript)) {
        throw "Acceptance workspace does not look like a codexteamup checkout: $Path"
    }
}

function Assert-HealthyService {
    try {
        $health = Invoke-RestMethod -Uri "$ServiceUrl/health" -Method Get -TimeoutSec 5
        if ($null -eq $health) {
            throw "Empty health response."
        }
    } catch {
        throw "CTU service is not reachable at $ServiceUrl. Start Codex Desktop through scripts/start-codexteamup.ps1 first. $($_.Exception.Message)"
    }
}

function Invoke-ToolCheck {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ToolName,

        [hashtable]$Arguments = @{}
    )

    $body = $Arguments | ConvertTo-Json -Depth 20 -Compress
    Invoke-RestMethod `
        -Uri "$ServiceUrl/mcp/tools/$([Uri]::EscapeDataString($ToolName))" `
        -Method Post `
        -ContentType "application/json; charset=utf-8" `
        -TimeoutSec 10 `
        -Body $body
}

Assert-AcceptanceCheckout -Path $Workspace
Assert-HealthyService

Invoke-ToolCheck -ToolName "agentbus_init" -Arguments @{ cwd = $Workspace } | Out-Null
$snapshotUrl = "$ServiceUrl/api/snapshot?busRoot=$([Uri]::EscapeDataString($BusRoot))"
Invoke-RestMethod -Uri $snapshotUrl -Method Get -TimeoutSec 5 | Out-Null
Invoke-ToolCheck -ToolName "codex_appserver_adapter_status" | Out-Null
Invoke-ToolCheck -ToolName "codex_controller_status" | Out-Null
Invoke-ToolCheck -ToolName "agentbus_list_agents" -Arguments @{ cwd = $Workspace } | Out-Null

$args = @(
    "-ExecutionPolicy", "Bypass",
    "-File", (Join-Path $Workspace.Replace('/', [System.IO.Path]::DirectorySeparatorChar) "scripts/test-codexteamup.ps1"),
    "-Workspace", $Workspace,
    "-Live",
    "-LiveScenario", "basic",
    "-ServiceUrl", $ServiceUrl,
    "-TimeoutSeconds", $TimeoutSeconds,
    "-ToolTimeoutSeconds", $ToolTimeoutSeconds
)

if (-not [string]::IsNullOrWhiteSpace($RunId)) {
    $args += @("-RunId", $RunId)
}

if ($KeepLiveAgents) {
    $args += "-KeepLiveAgents"
}

$runner = (Get-Command pwsh -ErrorAction SilentlyContinue).Source
if ([string]::IsNullOrWhiteSpace($runner)) {
    $runner = (Get-Command powershell -ErrorAction Stop).Source
}

& $runner @args
exit $LASTEXITCODE
