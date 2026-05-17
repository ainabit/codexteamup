param(
    [string]$ServiceUrl = "http://127.0.0.1:47319/",
    [string]$Workspace = "",
    [string]$RunId = "",
    [string]$Prefix = "ctu-test",
    [ValidateSet("basic", "peer", "replacement", "controller", "all")]
    [string]$Scenario = "all",
    [string]$ControllerAgent = "ctu-test/architect",
    [int]$TimeoutSeconds = 900,
    [int]$ToolTimeoutSeconds = 10,
    [switch]$Cleanup,
    [switch]$CleanupOnly,
    [switch]$NoArchiveThreads,
    [switch]$ForceCleanup
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($Workspace)) {
    $Workspace = $repoRoot
}

$Workspace = [System.IO.Path]::GetFullPath($Workspace).Replace('\', '/')
$ServiceUrl = $ServiceUrl.TrimEnd("/")
if ([string]::IsNullOrWhiteSpace($RunId)) {
    $RunId = Get-Date -Format "yyyyMMdd-HHmmss"
}

$agentPrefix = "$Prefix/$RunId"
$agentA = "$agentPrefix/agent-a"
$agentB = "$agentPrefix/agent-b"
$agentC = "$agentPrefix/agent-c"
$agentBReplacementName = "$agentPrefix/agent-b-replacement"

function Assert-True {
    param(
        [bool]$Condition,
        [string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function Invoke-CtuTool {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [hashtable]$Arguments = @{}
    )

    $json = $Arguments | ConvertTo-Json -Depth 80 -Compress
    Write-Host "  tool: $Name"
    Invoke-RestMethod `
        -Uri "$ServiceUrl/mcp/tools/$([Uri]::EscapeDataString($Name))" `
        -Method Post `
        -ContentType "application/json; charset=utf-8" `
        -TimeoutSec $ToolTimeoutSeconds `
        -Body $json
}

function Get-WakeupTimeoutSeconds {
    [Math]::Max(1, [Math]::Min(8, $ToolTimeoutSeconds - 2))
}

function Get-PollTimeoutSeconds {
    [Math]::Max(1, [Math]::Min(8, $ToolTimeoutSeconds - 2))
}

function Get-Agent {
    param([string]$Id)

    $agents = Invoke-CtuTool -Name "agentbus_list_agents" -Arguments @{ cwd = $Workspace }
    @($agents.agents) | Where-Object { $_.id -eq $Id } | Select-Object -First 1
}

function Require-Agent {
    param([string]$Id)

    $agent = Get-Agent -Id $Id
    Assert-True ($null -ne $agent) "Expected agent $Id to be registered."
    Assert-True (-not [string]::IsNullOrWhiteSpace($agent.threadId)) "Expected agent $Id to have a threadId."
    $agent
}

function Wait-TeamMessageResult {
    param(
        [Parameter(Mandatory = $true)]
        $Response,

        [Parameter(Mandatory = $true)]
        [string]$Label
    )

    if ($Response.wait.completed -eq $true) {
        return $Response.wait.result
    }

    $taskId = $Response.task.id
    Assert-True (-not [string]::IsNullOrWhiteSpace($taskId)) "Could not wait for $Label because team_send_message did not return a task id."

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        $wait = Invoke-CtuTool -Name "agentbus_wait_result" -Arguments @{
            cwd = $Workspace
            taskId = $taskId
            timeoutSeconds = [Math]::Min((Get-PollTimeoutSeconds), [Math]::Max(1, [int]($deadline - (Get-Date)).TotalSeconds))
        }

        if ($wait.completed -eq $true) {
            return $wait.result
        }

        Start-Sleep -Seconds 1
    }

    throw "Timed out waiting for $Label result."
}

function Send-TeamMessageQueued {
    param(
        [hashtable]$Arguments,
        [switch]$Dispatch
    )

    $Arguments["dispatchMode"] = "enqueue"
    $response = Invoke-CtuTool -Name "team_send_message" -Arguments $Arguments

    if ($Dispatch) {
        $taskId = $response.task.id
        Assert-True (-not [string]::IsNullOrWhiteSpace($taskId)) "Could not dispatch queued message because team_send_message did not return a task id."
        $dispatchResult = Invoke-CtuTool -Name "bridge_dispatch_task" -Arguments @{
            cwd = $Workspace
            taskId = $taskId
        }
        $response | Add-Member -NotePropertyName dispatchResult -NotePropertyValue $dispatchResult -Force
    }

    $response
}

function Assert-SafeCleanupPrefix {
    if ($Prefix -match "^(ctu-test|ctu-smoke)$") {
        return
    }

    if ($ForceCleanup) {
        return
    }

    throw "Cleanup only supports test prefixes such as ctu-test or ctu-smoke. Prefix '$Prefix' would be unsafe without -ForceCleanup."
}

function Get-TestAgents {
    $agents = Invoke-CtuTool -Name "agentbus_list_agents" -Arguments @{ cwd = $Workspace }
    @($agents.agents) | Where-Object {
        $_.id -like "$agentPrefix/*" -or $_.displayName -like "$agentPrefix/*"
    }
}

function Invoke-TestCleanup {
    Assert-SafeCleanupPrefix
    $agents = @(Get-TestAgents)
    if ($agents.Count -eq 0) {
        Write-Host "Cleanup: no test agents found for $agentPrefix."
        return
    }

    $archived = 0
    $retired = 0
    foreach ($agent in $agents) {
        if (-not $NoArchiveThreads -and -not [string]::IsNullOrWhiteSpace($agent.threadId)) {
            try {
                Invoke-CtuTool -Name "codex_thread_archive" -Arguments @{ threadId = $agent.threadId } | Out-Null
                $archived += 1
            } catch {
                Write-Warning "Could not archive thread '$($agent.threadId)' for '$($agent.id)': $($_.Exception.Message)"
            }
        }

        Invoke-CtuTool -Name "agentbus_register_agent" -Arguments @{
            cwd = $Workspace
            id = $agent.id
            displayName = if ([string]::IsNullOrWhiteSpace($agent.displayName)) { $agent.id } else { $agent.displayName }
            role = if ([string]::IsNullOrWhiteSpace($agent.role)) { $agent.id } else { $agent.role }
            threadId = $agent.threadId
            returnTo = $agent.returnTo
            model = $agent.model
            reasoningEffort = $agent.reasoningEffort
            speed = $agent.speed
            status = "retired"
        } | Out-Null
        $retired += 1
    }

    Write-Host "Cleanup: archived $archived thread(s), retired $retired agent binding(s) for $agentPrefix."
}

if ($CleanupOnly) {
    Write-Host "CodexTeamUp live multi-agent smoke cleanup"
    Write-Host "  service:   $ServiceUrl"
    Write-Host "  workspace: $Workspace"
    Write-Host "  run id:    $RunId"
    Write-Host "  scenario:  $Scenario"
    Write-Host "  prefix:    $agentPrefix"
    Invoke-TestCleanup
    exit 0
}

Write-Host "CodexTeamUp live multi-agent smoke"
Write-Host "  service:   $ServiceUrl"
Write-Host "  workspace: $Workspace"
Write-Host "  run id:    $RunId"
Write-Host "  scenario:  $Scenario"
Write-Host "  agents:    $agentA, $agentB, $agentC"
if ($Scenario -eq "controller") {
    Write-Host "  controller: $ControllerAgent"
}
Write-Host "  tool wait: ${ToolTimeoutSeconds}s"
Write-Host ""

try {
    $health = Invoke-RestMethod -Uri "$ServiceUrl/health" -Method Get -TimeoutSec 5
    Assert-True ($health.status -eq "ok") "CTU service is not healthy."

    try {
        Invoke-CtuTool -Name "codex_thread_list" -Arguments @{ cwd = $Workspace; limit = 5 } | Out-Null
    } catch {
        throw "Could not reach live Codex Desktop wrapper through CTU. Start Desktop with scripts/start-codexteamup.ps1 first. $($_.Exception.Message)"
    }

    if ($Scenario -eq "controller") {
        Write-Host "Checking manually provided controller agent..."
        $controller = Get-Agent -Id $ControllerAgent
        if ($null -eq $controller -or [string]::IsNullOrWhiteSpace($controller.threadId)) {
            Write-Host "Controller is not bound yet; trying to bind a visible Desktop thread named $ControllerAgent."
            Invoke-CtuTool -Name "team_ensure_agents" -Arguments @{
                cwd = $Workspace
                agentsJson = (ConvertTo-Json -InputObject @(
                    @{
                        id = $ControllerAgent
                        displayName = $ControllerAgent
                        role = "Live smoke test controller. Orchestrates run-scoped ctu-test agents from inside Codex Desktop."
                        allowedPaths = @("AGENTS.md", "docs/")
                        instructionFiles = @("AGENTS.md", "docs/mcp-tools.md", "docs/agent-thread-usage.md")
                        returnTo = $ControllerAgent
                        speed = "standard"
                        reasoningEffort = "medium"
                    }
                ) -Depth 40 -Compress)
                createMissing = "false"
                prime = "false"
            } | Out-Null
        }

        Require-Agent -Id $ControllerAgent | Out-Null

        $controllerPrompt = @"
Live CTU in-app smoke test run $RunId.

You are the manually provided test controller $ControllerAgent in workspace $Workspace.

Use CodexTeamUp MCP only. Keep every direct call short and use ACK/NACK semantics:
- enqueue work quickly;
- do not block one tool call for long work;
- poll AgentBus in short chunks when waiting.

Create or bind these run-scoped worker agents:
- $agentA, speed standard, reasoningEffort medium.
- $agentB, speed fast, model gpt-5.4-mini, reasoningEffort low.

Then use team_send_message to enqueue one short task to $agentA and one short task to $agentB. Use bridge_dispatch_task for each queued worker task. Ask each worker to claim its task and write one AgentBus result back to $ControllerAgent.

Finally write exactly one AgentBus result for your controller task back to ctu/architect. Include:
- worker agent ids;
- worker task ids if available;
- worker result ids or timeout/warning notes;
- whether any Desktop app-server wakeup returned an uncertain error.

Use forward-slash cwd values in any Git app directives.
"@

        Write-Host "Sending controller-orchestrated smoke task..."
        $phaseController = Send-TeamMessageQueued -Dispatch -Arguments @{
            cwd = $Workspace
            from = "ctu/architect"
            to = $ControllerAgent
            title = "Live smoke: controller orchestration"
            message = $controllerPrompt
            returnTo = "ctu/architect"
            waitResult = $false
        }

        $controllerResult = Wait-TeamMessageResult -Response $phaseController -Label "$ControllerAgent controller orchestration"

        Write-Host ""
        Write-Host "PASS live CTU controller smoke run $RunId"
        Write-Host "  controller: $ControllerAgent"
        Write-Host "  controller result: $($controllerResult.id)"
        return
    }

    $agentAJson = ConvertTo-Json -InputObject @(
        @{
        id = $agentA
        displayName = $agentA
        role = "Live smoke coordinator. Creates peer smoke agents and reports protocol evidence."
        allowedPaths = @("docs/", "samples/")
        instructionFiles = @("AGENTS.md", "docs/mcp-tools.md", "docs/agent-thread-usage.md")
        returnTo = "ctu/architect"
        speed = "deep"
        model = "gpt-5.5"
        reasoningEffort = "high"
        initialPrompt = "You are $agentA. This is a live Codex Desktop CTU smoke test. Work only on tasks addressed to you. When asked, use team_ensure_agents to create $agentB and $agentC, then use team_send_message to enqueue peer tasks and bridge_dispatch_task to wake them. Keep messages short and write exactly one AgentBus result. Use forward-slash cwd paths in Git app directives, for example X:/repo/codexteamup."
    }
) -Depth 40 -Compress

    Write-Host "Ensuring coordinator agent..."
    Invoke-CtuTool -Name "team_ensure_agents" -Arguments @{
        cwd = $Workspace
        agentsJson = $agentAJson
        createMissing = "true"
        prime = "true"
    } | Out-Null
    Require-Agent -Id $agentA | Out-Null

    if ($Scenario -eq "basic") {
        Write-Host "Waking coordinator agent..."
        $phaseBasic = Send-TeamMessageQueued -Dispatch -Arguments @{
            cwd = $Workspace
            from = "ctu/architect"
            to = $agentA
            title = "Live smoke: basic wakeup"
            message = "Live CTU smoke test run $RunId. Confirm you are $agentA and write one short AgentBus result."
            returnTo = "ctu/architect"
            waitResult = $true
            timeoutSeconds = $TimeoutSeconds
        }

        $basicResult = Wait-TeamMessageResult -Response $phaseBasic -Label "$agentA basic wakeup"

        Write-Host ""
        Write-Host "PASS live CTU basic smoke run $RunId"
        Write-Host "  $agentA result: $($basicResult.id)"
        return
    }

    $createPeersPrompt = @"
Live CTU smoke test run $RunId.

Use MCP tools, not shell commands.

1. Call team_ensure_agents for these exact agents:
   - $agentB with displayName $agentB, speed fast, model gpt-5.4-mini, reasoningEffort low.
   - $agentC with displayName $agentC, speed deep, model gpt-5.5, reasoningEffort high.
2. Use team_send_message to enqueue one short task to $agentB and one short task to $agentC with returnTo=$agentA.
3. Use bridge_dispatch_task for each queued worker task. Treat Desktop wakeup as best-effort and continue with short AgentBus polls.
4. Wait only if practical; otherwise report task ids and what you dispatched.
5. Write one AgentBus result to ctu/architect summarizing created agents, task ids, dispatch status, and any blockers.
"@

    Write-Host "Asking coordinator to create and wake peer agents..."
    $phaseA = Send-TeamMessageQueued -Dispatch -Arguments @{
        cwd = $Workspace
        from = "ctu/architect"
        to = $agentA
        title = "Live smoke: create peer agents"
        message = $createPeersPrompt
        returnTo = "ctu/architect"
        waitResult = $true
        timeoutSeconds = $TimeoutSeconds
    }

    $agentAResult = Wait-TeamMessageResult -Response $phaseA -Label "$agentA peer creation"

    $b = Require-Agent -Id $agentB
    $c = Require-Agent -Id $agentC
    Assert-True ($b.speed -eq "fast") "Expected $agentB speed=fast, got '$($b.speed)'."
    Assert-True ($b.model -eq "gpt-5.4-mini") "Expected $agentB model=gpt-5.4-mini, got '$($b.model)'."
    Assert-True ($b.reasoningEffort -eq "low") "Expected $agentB reasoningEffort=low, got '$($b.reasoningEffort)'."
    Assert-True ($c.speed -eq "deep") "Expected $agentC speed=deep, got '$($c.speed)'."
    Assert-True ($c.model -eq "gpt-5.5") "Expected $agentC model=gpt-5.5, got '$($c.model)'."
    Assert-True ($c.reasoningEffort -eq "high") "Expected $agentC reasoningEffort=high, got '$($c.reasoningEffort)'."

    $peerPrompt = @"
Live CTU smoke test run $RunId.

Use team_send_message to enqueue a short ping task from $agentB to $agentC with returnTo=$agentB, then use bridge_dispatch_task for that task.
Then write one AgentBus result to ctu/architect that includes the result id or timeout/error from $agentC.
"@

    Write-Host "Asking agent-b to communicate with agent-c..."
    $phaseB = Send-TeamMessageQueued -Dispatch -Arguments @{
        cwd = $Workspace
        from = "ctu/architect"
        to = $agentB
        title = "Live smoke: peer communication"
        message = $peerPrompt
        returnTo = "ctu/architect"
        waitResult = $true
        timeoutSeconds = $TimeoutSeconds
    }

    $agentBResult = Wait-TeamMessageResult -Response $phaseB -Label "$agentB peer communication"

    if ($Scenario -eq "peer") {
        Write-Host ""
        Write-Host "PASS live CTU peer smoke run $RunId"
        Write-Host "  $agentA result: $($agentAResult.id)"
        Write-Host "  $agentB peer result: $($agentBResult.id)"
        return
    }

    $oldThreadId = $b.threadId
    $staleThreadId = "stale-$RunId-agent-b"
    Write-Host "Forcing a stale agent-b binding and creating a replacement..."
    Invoke-CtuTool -Name "agentbus_register_agent" -Arguments @{
        cwd = $Workspace
        id = $agentB
        role = "Live smoke agent-b stale binding marker"
        displayName = $agentB
        threadId = $staleThreadId
        returnTo = "ctu/architect"
        speed = "fast"
        model = "gpt-5.4-mini"
        reasoningEffort = "low"
        status = "active"
    } | Out-Null

    $replacementJson = ConvertTo-Json -InputObject @(
        @{
        id = $agentB
        displayName = $agentBReplacementName
        role = "Live smoke replacement for agent-b."
        allowedPaths = @("docs/", "samples/")
        instructionFiles = @("AGENTS.md", "docs/mcp-tools.md")
        returnTo = "ctu/architect"
        speed = "standard"
        model = "gpt-5.5"
        reasoningEffort = "medium"
        initialPrompt = "You are replacement $agentB for live CTU smoke test $RunId. Work only on tasks addressed to $agentB and write exactly one AgentBus result when asked."
    }
) -Depth 40 -Compress

    Invoke-CtuTool -Name "team_ensure_agents" -Arguments @{
        cwd = $Workspace
        agentsJson = $replacementJson
        createMissing = "true"
        prime = "true"
    } | Out-Null

    $newB = Require-Agent -Id $agentB
    Assert-True ($newB.threadId -ne $staleThreadId) "Replacement did not move $agentB off the stale thread id."
    Assert-True ($newB.threadId -ne $oldThreadId) "Replacement reused old $agentB thread id instead of creating/binding replacement."
    Assert-True ($newB.displayName -eq $agentBReplacementName) "Expected replacement displayName $agentBReplacementName, got '$($newB.displayName)'."
    Assert-True ($newB.model -eq "gpt-5.5") "Expected replacement model=gpt-5.5, got '$($newB.model)'."
    Assert-True ($newB.reasoningEffort -eq "medium") "Expected replacement reasoningEffort=medium, got '$($newB.reasoningEffort)'."

    Write-Host "Waking replacement agent-b..."
    $phaseReplacement = Send-TeamMessageQueued -Dispatch -Arguments @{
        cwd = $Workspace
        from = "ctu/architect"
        to = $agentB
        title = "Live smoke: replacement check"
        message = "Confirm you are replacement $agentBReplacementName for run $RunId. Write one short AgentBus result."
        returnTo = "ctu/architect"
        waitResult = $true
        timeoutSeconds = $TimeoutSeconds
    }

    $replacementResult = Wait-TeamMessageResult -Response $phaseReplacement -Label "replacement $agentB"

    Write-Host ""
    Write-Host "PASS live CTU multi-agent smoke run $RunId"
    Write-Host "  $agentA result: $($agentAResult.id)"
    Write-Host "  $agentB peer result: $($agentBResult.id)"
    Write-Host "  replacement $agentB result: $($replacementResult.id)"
    Write-Host "  old b thread: $oldThreadId"
    Write-Host "  new b thread: $($newB.threadId)"
} finally {
    if ($Cleanup) {
        Invoke-TestCleanup
    }
}
