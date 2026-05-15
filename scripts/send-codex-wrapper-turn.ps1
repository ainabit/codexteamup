param(
    [Parameter(Mandatory = $true)]
    [string]$ThreadId,

    [Parameter(Mandatory = $true)]
    [string]$Message,

    [string]$Cwd,
    [string]$PipeName = "codexteamup-appserver",
    [switch]$SkipResume,
    [switch]$Yes
)

$ErrorActionPreference = "Stop"

if (-not $Yes) {
    Write-Host "About to send a live turn/start into Codex Desktop thread:"
    Write-Host "threadId=$ThreadId"
    Write-Host "message=$Message"
    $answer = Read-Host "Type YES to continue"
    if ($answer -ne "YES") {
        throw "Aborted by user."
    }
}

if (-not $SkipResume) {
    $resumeParams = @{
        threadId = $ThreadId
        approvalPolicy = "on-request"
    }

    if (-not [string]::IsNullOrWhiteSpace($Cwd)) {
        $resumeParams.cwd = $Cwd
    }

    $resumeJson = $resumeParams | ConvertTo-Json -Depth 50 -Compress
    Write-Host "Resuming thread through wrapper pipe..."
    & "$PSScriptRoot\invoke-codex-wrapper-rpc.ps1" -PipeName $PipeName -Method "thread/resume" -ParamsJson $resumeJson | Out-Null
}

$params = @{
    threadId = $ThreadId
    input = @(
        @{
            type = "text"
            text = $Message
        }
    )
    approvalPolicy = "on-request"
} | ConvertTo-Json -Depth 50 -Compress

& "$PSScriptRoot\invoke-codex-wrapper-rpc.ps1" -PipeName $PipeName -Method "turn/start" -ParamsJson $params
