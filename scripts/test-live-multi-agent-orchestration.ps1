param(
    [string]$ServiceUrl = "http://127.0.0.1:47319/",
    [string]$Workspace = "",
    [string]$RunId = "",
    [string]$Prefix = "ctu-test",
    [ValidateSet("basic", "peer", "replacement", "controller", "controller-suite", "all")]
    [string]$Scenario = "all",
    [string]$ControllerAgent = "ctu-test/architect",
    [int]$TimeoutSeconds = 900,
    [int]$ToolTimeoutSeconds = 10,
    [switch]$Cleanup,
    [switch]$CleanupOnly,
    [switch]$CleanupAllTestAgents,
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
$TimeoutSeconds = [Math]::Max(1, $TimeoutSeconds)
$ToolTimeoutSeconds = [Math]::Max(1, $ToolTimeoutSeconds)
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

    if ($Arguments.ContainsKey("timeoutSeconds")) {
        $Arguments["timeoutSeconds"] = [Math]::Max(1, [int]$Arguments["timeoutSeconds"])
    }

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
    [Math]::Max(1, [Math]::Min(3, $ToolTimeoutSeconds - 3))
}

function Get-RemainingSeconds {
    param([datetime]$Deadline)

    [Math]::Max(1, [int][Math]::Ceiling(($Deadline - (Get-Date)).TotalSeconds))
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

function Wait-Agent {
    param([string]$Id)

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        $agent = Get-Agent -Id $Id
        if ($null -ne $agent -and -not [string]::IsNullOrWhiteSpace($agent.threadId) -and $agent.status -eq "active") {
            return $agent
        }

        Start-Sleep -Seconds 1
    }

    throw "Timed out waiting for agent $Id to be active with a threadId."
}

function Ensure-CtuAgents {
    param(
        [Parameter(Mandatory = $true)]
        [string]$AgentsJson,

        [string[]]$Ids,

        [string]$CreateMissing = "true",

        [string]$Prime = "true",

        [string]$SetName = "false"
    )

    $response = Invoke-CtuTool -Name "team_ensure_agents" -Arguments @{
        cwd = $Workspace
        agentsJson = $AgentsJson
        createMissing = $CreateMissing
        prime = $Prime
        setName = $SetName
        defer = $true
    }

    Assert-True ($response.accepted -eq $true) "team_ensure_agents did not ACK the deferred ensure request."
    Assert-True (-not [string]::IsNullOrWhiteSpace($response.operationId)) "team_ensure_agents deferred ACK did not include an operation id."
    Wait-DeferredOperation -OperationId $response.operationId -Label "team_ensure_agents" | Out-Null
    foreach ($id in $Ids) {
        Wait-Agent -Id $id | Out-Null
    }

    $response
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
        $remainingSeconds = Get-RemainingSeconds -Deadline $deadline
        $wait = Invoke-CtuTool -Name "agentbus_wait_result" -Arguments @{
            cwd = $Workspace
            taskId = $taskId
            timeoutSeconds = [Math]::Min((Get-PollTimeoutSeconds), $remainingSeconds)
        }

        if ($wait.completed -eq $true) {
            return $wait.result
        }

        Start-Sleep -Seconds 1
    }

    throw "Timed out waiting for $Label result."
}

function Assert-ResultContains {
    param(
        [Parameter(Mandatory = $true)]
        $Result,

        [Parameter(Mandatory = $true)]
        [string[]]$Markers,

        [Parameter(Mandatory = $true)]
        [string]$Label
    )

    $haystack = @(
        $Result.summary
        $Result.outcome
        $Result.tests
        $Result.checks
        $Result.nextSuggestedAction
    ) -join "`n"

    foreach ($marker in $Markers) {
        Assert-True ($haystack -like "*$marker*") "Expected $Label result to contain evidence marker '$marker'."
    }
}

function Wait-DeferredOperation {
    param(
        [Parameter(Mandatory = $true)]
        [string]$OperationId,

        [Parameter(Mandatory = $true)]
        [string]$Label
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        $events = Invoke-CtuTool -Name "agentbus_list_events" -Arguments @{
            cwd = $Workspace
            limit = 500
        }

        $match = @($events.events) | Where-Object {
            $_.payload.operationId -eq $OperationId
        } | Select-Object -Last 1

        if ($null -ne $match) {
            if ($match.type -like "*failed*") {
                throw "Deferred $Label failed: $($match.message)"
            }

            if ($match.type -like "*_deferred") {
                return $match
            }
        }

        Start-Sleep -Seconds 1
    }

    throw "Timed out waiting for deferred $Label operation $OperationId."
}

function Wait-TaskDispatch {
    param(
        [Parameter(Mandatory = $true)]
        [string]$TaskId
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        $events = Invoke-CtuTool -Name "agentbus_list_events" -Arguments @{
            cwd = $Workspace
            limit = 500
        }

        $match = @($events.events) | Where-Object {
            $_.taskId -eq $TaskId -and $_.type -like "task.dispatch*"
        } | Select-Object -Last 1

        if ($null -ne $match -and $match.type -ne "task.dispatch_accepted") {
            if ($match.type -eq "task.dispatch_failed") {
                Write-Warning "Dispatch failed or is uncertain for ${TaskId}: $($match.message). Continuing because AgentBus task/result state is authoritative."
            }

            return $match
        }

        Start-Sleep -Seconds 1
    }

    throw "Timed out waiting for dispatch event for task $TaskId."
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
            defer = $true
        }
        $response | Add-Member -NotePropertyName dispatchResult -NotePropertyValue $dispatchResult -Force
        $dispatchEvent = Wait-TaskDispatch -TaskId $taskId
        $response | Add-Member -NotePropertyName dispatchEvent -NotePropertyValue $dispatchEvent -Force
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
    if ($CleanupAllTestAgents) {
        return @($agents.agents) | Where-Object {
            (
                $_.id -like "$Prefix/*" -or
                $_.displayName -like "$Prefix/*"
            ) -and
            $_.id -ne "$Prefix/architect" -and
            $_.displayName -ne "$Prefix/architect"
        }
    }

    @($agents.agents) | Where-Object {
        $_.id -like "$agentPrefix/*" -or $_.displayName -like "$agentPrefix/*"
    }
}

function Invoke-TestCleanup {
    Assert-SafeCleanupPrefix
    $agents = @(Get-TestAgents)
    if ($agents.Count -eq 0) {
        $cleanupLabel = if ($CleanupAllTestAgents) { "$Prefix/* except $Prefix/architect" } else { $agentPrefix }
        Write-Host "Cleanup: no test agents found for $cleanupLabel."
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

    $cleanupLabel = if ($CleanupAllTestAgents) { "$Prefix/* except $Prefix/architect" } else { $agentPrefix }
    Write-Host "Cleanup: archived $archived thread(s), retired $retired agent binding(s) for $cleanupLabel."
}

if ($CleanupOnly) {
    Write-Host "CodexTeamUp live multi-agent smoke cleanup"
    Write-Host "  service:   $ServiceUrl"
    Write-Host "  workspace: $Workspace"
    Write-Host "  run id:    $RunId"
    Write-Host "  scenario:  $Scenario"
    if ($CleanupAllTestAgents) {
        Write-Host "  prefix:    $Prefix/* except $Prefix/architect"
    } else {
        Write-Host "  prefix:    $agentPrefix"
    }
    Invoke-TestCleanup
    exit 0
}

Write-Host "CodexTeamUp live multi-agent smoke"
Write-Host "  service:   $ServiceUrl"
Write-Host "  workspace: $Workspace"
Write-Host "  run id:    $RunId"
Write-Host "  scenario:  $Scenario"
Write-Host "  agents:    $agentA, $agentB, $agentC"
if ($Scenario -eq "controller" -or $Scenario -eq "controller-suite") {
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

    if ($Scenario -eq "controller" -or $Scenario -eq "controller-suite") {
        Write-Host "Checking manually provided controller agent..."
        $controller = Get-Agent -Id $ControllerAgent
        if ($null -eq $controller -or [string]::IsNullOrWhiteSpace($controller.threadId)) {
            Write-Host "Controller is not bound yet; binding an existing visible Desktop thread named $ControllerAgent."
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
                setName = "false"
            } | Out-Null
        }

        $controller = Require-Agent -Id $ControllerAgent

        if ($Scenario -eq "controller-suite") {
            $suiteAgentAName = "$agentPrefix/implementation-role"
            $suiteAgentBName = "$agentPrefix/review-role"
            $suiteAgentCName = "$agentPrefix/deep-role"
            $controllerPrompt = @"
Live CTU controller-suite run $RunId.

You are the manually provided test controller $ControllerAgent in workspace $Workspace.

This is a repeatable safety-net package. Use CodexTeamUp MCP only. Keep direct calls short:
- enqueue work quickly;
- wake queued tasks with bridge_dispatch_task;
- poll AgentBus in short chunks;
- do not turn this into one long blocking RPC.

Create or bind these run-scoped worker agents with team_ensure_agents defer=true prime=true setName=false:
- id=$agentA, displayName=$suiteAgentAName, role="Implementation role marker for controller-suite; this role is intentionally not the id.", speed=fast, model=gpt-5.4-mini, reasoningEffort=low.
- id=$agentB, displayName=$suiteAgentBName, role="Review role marker for controller-suite; this role is intentionally not the id.", speed=fast, model=gpt-5.4-mini, reasoningEffort=low.
- id=$agentC, displayName=$suiteAgentCName, role="Deep reasoning role marker for controller-suite; this role is intentionally not the id.", speed=standard, model=gpt-5.4, reasoningEffort=medium.

Verify with agentbus_list_agents that all three agents have active thread ids, persisted displayName values, persisted roles, and the requested model/reasoning/speed. Only then include evidence marker runtime-ok and role-ok in your final result.

Then exercise these result/stop outcomes through normal AgentBus tasks:
1. Send a task to $agentA asking for exactly one result with outcome done. Wait for it. Include outcome-done-ok only if observed.
2. Send a task to $agentB asking for exactly one result with outcome human and a short open question. Wait for it. Include outcome-human-ok only if observed.
3. Send a task to $agentC asking for exactly one result with outcome failed and a short failure reason. Wait for it. Include outcome-failed-ok only if observed.
4. Send a task to $agentA asking it to create one child task for $agentB, wake it, and then write outcome handed_off back to you with the child task id. Wait for $agentA result. Include outcome-handed-off-ok only if observed.
5. Send a task to $agentC asking it to write outcome self_continue with a deduplicated continuation wakeup after 5 seconds, then handle the continuation by writing outcome done. Poll continuations/results in short chunks. Include self-continue-ok only if both the self_continue result and follow-up done result are observed.

Finally write exactly one AgentBus result for your controller-suite task back to ctu-test/runner.
Your final result summary/checks must include these exact evidence markers only when verified:
- runtime-ok
- role-ok
- outcome-done-ok
- outcome-human-ok
- outcome-failed-ok
- outcome-handed-off-ok
- self-continue-ok

Also include worker agent ids, worker task ids, worker result ids, continuation id if available, and any Desktop wakeup uncertainty.
Use forward-slash cwd values in any Git app directives.
"@
            $controllerTitle = "Live smoke: controller suite"
            $resultLabel = "$ControllerAgent controller suite"
            $requiredMarkers = @(
                "runtime-ok",
                "role-ok",
                "outcome-done-ok",
                "outcome-human-ok",
                "outcome-failed-ok",
                "outcome-handed-off-ok",
                "self-continue-ok"
            )
        } else {
            $controllerPrompt = @"
Live CTU in-app smoke test run $RunId.

You are the manually provided test controller $ControllerAgent in workspace $Workspace.

Use CodexTeamUp MCP only. Keep every direct call short and use ACK/NACK semantics:
- enqueue work quickly;
- do not block one tool call for long work;
- poll AgentBus in short chunks when waiting.

Create or bind these run-scoped worker agents with team_ensure_agents defer=true prime=true setName=false, then poll agentbus_list_agents in short chunks until they have active thread ids:
- $agentA, speed standard, reasoningEffort medium.
- $agentB, speed fast, model gpt-5.4-mini, reasoningEffort low.

Then use team_send_message to enqueue one short task to $agentA and one short task to $agentB. Use bridge_dispatch_task for each queued worker task. Ask each worker to claim its task and write one AgentBus result back to $ControllerAgent.

Finally write exactly one AgentBus result for your controller task back to ctu-test/runner. Include:
- worker agent ids;
- worker task ids if available;
- worker result ids or timeout/warning notes;
- whether any Desktop app-server wakeup returned an uncertain error.

Use forward-slash cwd values in any Git app directives.
"@
            $controllerTitle = "Live smoke: controller orchestration"
            $resultLabel = "$ControllerAgent controller orchestration"
            $requiredMarkers = @()
        }

        Write-Host "Sending controller-orchestrated smoke task..."
        $phaseController = Send-TeamMessageQueued -Dispatch -Arguments @{
            cwd = $Workspace
            from = "ctu-test/runner"
            to = $ControllerAgent
            title = $controllerTitle
            message = $controllerPrompt
            returnTo = "ctu-test/runner"
            waitResult = $false
        }

        if ($phaseController.dispatchEvent.type -eq "task.dispatch_failed") {
            throw "Controller thread $ControllerAgent is bound but not wakeable through Desktop turn/start: $($phaseController.dispatchEvent.message)"
        }

        $controllerResult = Wait-TeamMessageResult -Response $phaseController -Label $resultLabel
        if ($requiredMarkers.Count -gt 0) {
            Assert-ResultContains -Result $controllerResult -Markers $requiredMarkers -Label $resultLabel
        }

        Write-Host ""
        Write-Host "PASS live CTU $Scenario smoke run $RunId"
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
        returnTo = $ControllerAgent
        speed = "fast"
        model = "gpt-5.4-mini"
        reasoningEffort = "low"
        initialPrompt = "You are $agentA. This is a live Codex Desktop CTU smoke test. Work only on tasks addressed to you. When asked, use team_ensure_agents with defer=true prime=true setName=false to create $agentB and $agentC, then use team_send_message to enqueue peer tasks and bridge_dispatch_task to wake them. Keep messages short and write exactly one AgentBus result. Use forward-slash cwd paths in Git app directives, for example X:/repo/codexteamup."
    }
) -Depth 40 -Compress

    Write-Host "Ensuring coordinator agent..."

    if ($Scenario -eq "basic") {
        Write-Host "Queueing coordinator task before prime..."
        $phaseBasic = Send-TeamMessageQueued -Arguments @{
            cwd = $Workspace
            from = $ControllerAgent
            to = $agentA
            title = "Live smoke: basic wakeup"
            message = "Live CTU smoke test run $RunId. Confirm you are $agentA and write one short AgentBus result."
            returnTo = $ControllerAgent
            waitResult = $true
            timeoutSeconds = $TimeoutSeconds
        }

        Ensure-CtuAgents -AgentsJson $agentAJson -Ids @($agentA) | Out-Null
        $basicResult = Wait-TeamMessageResult -Response $phaseBasic -Label "$agentA basic wakeup"

        Write-Host ""
        Write-Host "PASS live CTU basic smoke run $RunId"
        Write-Host "  $agentA result: $($basicResult.id)"
        return
    }

    $peerAgentsJson = ConvertTo-Json -InputObject @(
        @{
            id = $agentB
            displayName = $agentB
            role = "Live smoke worker B. Claim assigned task and write one AgentBus result back to $agentA."
            allowedPaths = @("docs/", "samples/")
            instructionFiles = @("AGENTS.md", "docs/mcp-tools.md")
            returnTo = $agentA
            speed = "fast"
            model = "gpt-5.4-mini"
            reasoningEffort = "low"
            initialPrompt = "You are $agentB. Claim assigned CTU smoke tasks addressed to $agentB and write exactly one AgentBus result."
        },
        @{
            id = $agentC
            displayName = $agentC
            role = "Live smoke worker C. Claim assigned task and write one AgentBus result back to $agentA."
            allowedPaths = @("docs/", "samples/")
            instructionFiles = @("AGENTS.md", "docs/mcp-tools.md")
            returnTo = $agentA
            speed = "fast"
            model = "gpt-5.4-mini"
            reasoningEffort = "low"
            initialPrompt = "You are $agentC. Claim assigned CTU smoke tasks addressed to $agentC and write exactly one AgentBus result."
        }
    ) -Depth 40 -Compress

    $createPeersPrompt = @"
Live CTU smoke test run $RunId.

Use MCP tools, not shell commands.

1. Use team_send_message with dispatchMode=enqueue to create one short task for $agentB and one short task for $agentC before ensuring those agents. Set returnTo=$agentA and ask each worker to claim its exact task and write one AgentBus result.
2. Call team_ensure_agents once with cwd=$Workspace, defer=true, prime=true, setName=false and this exact agentsJson:
$peerAgentsJson
Do not use team_create_agent for $agentB or $agentC in this smoke.
3. Poll agentbus_list_agents in short chunks until $agentB and $agentC have active thread ids.
4. Poll agentbus_wait_result in short chunks for the two worker tasks when practical.
5. Do not rely on bridge_dispatch_task for this smoke; treat Desktop wakeups as best-effort only.
6. Write one AgentBus result to $ControllerAgent summarizing created agents, worker task ids, worker result ids or timeouts, and any blockers.
"@

    Write-Host "Queueing coordinator peer-creation task before prime..."
    $phaseA = Send-TeamMessageQueued -Arguments @{
        cwd = $Workspace
        from = $ControllerAgent
        to = $agentA
        title = "Live smoke: create peer agents"
        message = $createPeersPrompt
        returnTo = $ControllerAgent
        waitResult = $true
        timeoutSeconds = $TimeoutSeconds
    }

    Ensure-CtuAgents -AgentsJson $agentAJson -Ids @($agentA) | Out-Null
    $agentAResult = Wait-TeamMessageResult -Response $phaseA -Label "$agentA peer creation"

    $b = Require-Agent -Id $agentB
    $c = Require-Agent -Id $agentC
    Assert-True ($b.speed -eq "fast") "Expected $agentB speed=fast, got '$($b.speed)'."
    Assert-True ($b.model -eq "gpt-5.4-mini") "Expected $agentB model=gpt-5.4-mini, got '$($b.model)'."
    Assert-True ($b.reasoningEffort -eq "low") "Expected $agentB reasoningEffort=low, got '$($b.reasoningEffort)'."
    Assert-True ($c.speed -eq "fast") "Expected $agentC speed=fast, got '$($c.speed)'."
    Assert-True ($c.model -eq "gpt-5.4-mini") "Expected $agentC model=gpt-5.4-mini, got '$($c.model)'."
    Assert-True ($c.reasoningEffort -eq "low") "Expected $agentC reasoningEffort=low, got '$($c.reasoningEffort)'."

    if ($Scenario -eq "peer") {
        Write-Host ""
        Write-Host "PASS live CTU peer smoke run $RunId"
        Write-Host "  $agentA result: $($agentAResult.id)"
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
        returnTo = $ControllerAgent
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
        returnTo = $ControllerAgent
        speed = "fast"
        model = "gpt-5.4-mini"
        reasoningEffort = "low"
        initialPrompt = "You are replacement $agentB for live CTU smoke test $RunId. Work only on tasks addressed to $agentB and write exactly one AgentBus result when asked."
    }
) -Depth 40 -Compress

    Write-Host "Queueing replacement check before replacement prime..."
    $phaseReplacement = Send-TeamMessageQueued -Arguments @{
        cwd = $Workspace
        from = $ControllerAgent
        to = $agentB
        title = "Live smoke: replacement check"
        message = "Confirm you are replacement $agentBReplacementName for run $RunId. Write one short AgentBus result."
        returnTo = $ControllerAgent
        waitResult = $true
        timeoutSeconds = $TimeoutSeconds
    }

    Ensure-CtuAgents -AgentsJson $replacementJson -Ids @($agentB) | Out-Null

    $newB = Require-Agent -Id $agentB
    Assert-True ($newB.threadId -ne $staleThreadId) "Replacement did not move $agentB off the stale thread id."
    Assert-True ($newB.threadId -ne $oldThreadId) "Replacement reused old $agentB thread id instead of creating/binding replacement."
    Assert-True ($newB.displayName -eq $agentBReplacementName) "Expected replacement displayName $agentBReplacementName, got '$($newB.displayName)'."
    Assert-True ($newB.model -eq "gpt-5.4-mini") "Expected replacement model=gpt-5.4-mini, got '$($newB.model)'."
    Assert-True ($newB.reasoningEffort -eq "low") "Expected replacement reasoningEffort=low, got '$($newB.reasoningEffort)'."

    $replacementResult = Wait-TeamMessageResult -Response $phaseReplacement -Label "replacement $agentB"

    Write-Host ""
    Write-Host "PASS live CTU multi-agent smoke run $RunId"
    Write-Host "  $agentA result: $($agentAResult.id)"
    Write-Host "  replacement $agentB result: $($replacementResult.id)"
    Write-Host "  old b thread: $oldThreadId"
    Write-Host "  new b thread: $($newB.threadId)"
} finally {
    if ($Cleanup) {
        Invoke-TestCleanup
    }
}
