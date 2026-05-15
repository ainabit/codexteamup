param(
    [string]$LogRoot = "",
    [int]$Limit = 30
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($LogRoot)) {
    $repoRoot = Split-Path -Parent $PSScriptRoot
    $LogRoot = Join-Path $repoRoot ".ctu\cli-wrapper-probe"
}

if (-not (Test-Path -LiteralPath $LogRoot -PathType Container)) {
    throw "Wrapper log directory not found: $LogRoot"
}

$entries = foreach ($file in Get-ChildItem -LiteralPath $LogRoot -File -Filter "codex-wrapper-*.jsonl" | Sort-Object LastWriteTime -Descending | Select-Object -First $Limit) {
    $start = $null
    $exit = $null
    foreach ($line in Get-Content -LiteralPath $file.FullName) {
        if ([string]::IsNullOrWhiteSpace($line)) {
            continue
        }

        $event = $line | ConvertFrom-Json
        if ($event.type -eq "start") {
            $start = $event
        } elseif ($event.type -eq "exit") {
            $exit = $event
        }
    }

    if ($start) {
        $args = @($start.payload.args) -join " "
        [pscustomobject]@{
            Time = $start.timestamp
            Pid = $start.payload.pid
            Args = $args
            AppServer = $args -match "(^| )app-server($| )"
            ExitCode = if ($exit) { $exit.payload.exitCode } else { $null }
            Log = $file.FullName
        }
    }
}

$entries | Sort-Object Time -Descending | Format-Table -AutoSize

if (-not ($entries | Where-Object { $_.AppServer })) {
    Write-Host ""
    Write-Host "No wrapper invocation with args containing 'app-server' was found."
    Write-Host "If Codex Desktop was started through CODEX_CLI_PATH, this means it did not start its app-server through this wrapper during the observed window."
}
