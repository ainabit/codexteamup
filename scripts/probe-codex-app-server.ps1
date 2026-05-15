param(
    [string]$CodexHome = "$env:USERPROFILE\.codex",
    [string]$CodexExe = "",
    [int]$ThreadLimit = 3,
    [switch]$GenerateSchemas
)

$ErrorActionPreference = "Continue"

function Redact-Text {
    param([AllowNull()][string]$Text)
    if ([string]::IsNullOrEmpty($Text)) { return "" }

    $value = $Text
    $value = [regex]::Replace($value, '(?i)(token|api[_-]?key|secret|password)(\s*[:=]\s*)\S+', '$1$2[redacted]')
    $value = [regex]::Replace($value, '(?i)Bearer\s+[A-Za-z0-9._~+/-]+=*', 'Bearer [redacted]')
    $value = [regex]::Replace($value, 'sk-[A-Za-z0-9]{20,}', '[redacted-api-key]')
    return $value
}

function Write-Section {
    param([string]$Title)
    Write-Host ""
    Write-Host "== $Title =="
}

function Resolve-CodexExe {
    param([AllowNull()][string]$RequestedPath)

    $candidates = New-Object System.Collections.Generic.List[string]
    if (-not [string]::IsNullOrWhiteSpace($RequestedPath)) {
        $expanded = [Environment]::ExpandEnvironmentVariables($RequestedPath)
        $candidates.Add($expanded)
    }

    $cmd = Get-Command codex -ErrorAction SilentlyContinue
    if ($null -ne $cmd -and -not [string]::IsNullOrWhiteSpace($cmd.Source)) {
        $candidates.Add($cmd.Source)
    }

    if (-not [string]::IsNullOrWhiteSpace($env:LOCALAPPDATA)) {
        $candidates.Add((Join-Path $env:LOCALAPPDATA "OpenAI\Codex\bin\codex.exe"))
        $candidates.Add((Join-Path $env:LOCALAPPDATA "Microsoft\WindowsApps\codex.exe"))
    }

    foreach ($candidate in ($candidates | Select-Object -Unique)) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }

    return $null
}

function Invoke-CodexProxyProbe {
    param(
        [string]$CodexExe,
        [int]$Limit
    )

    $requestLines = @(
        '{"id":1,"method":"initialize","params":{"clientInfo":{"name":"CodexTeamUpManualProbe","version":"0.1.0"},"capabilities":{"experimentalApi":true}}}'
        '{"method":"initialized"}'
        ('{"id":2,"method":"thread/list","params":{"limit":' + $Limit + ',"useStateDbOnly":true}}')
    )

    Write-Host "request:"
    $requestLines | ForEach-Object { Write-Host "  $_" }

    Write-Host ""
    Write-Host "response:"
    $raw = $requestLines | & $CodexExe app-server proxy 2>&1
    $exit = $LASTEXITCODE
    $text = Redact-Text (($raw | ForEach-Object { $_.ToString() }) -join [Environment]::NewLine)
    if ($text.Trim().Length -gt 0) {
        $text
    } else {
        Write-Host "(no output)"
    }

    Write-Host ""
    Write-Host "proxyExitCode=$exit"

    $hasInitialize = $text -match '"id"\s*:\s*1' -and $text -match '"result"'
    $hasThreadList = $text -match '"id"\s*:\s*2' -and $text -match '"result"'
    Write-Host "initializeResult=$hasInitialize"
    Write-Host "threadListResult=$hasThreadList"
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$outDir = Join-Path $repoRoot ".ctu\manual-probe"
$outPath = Join-Path $outDir "codex-app-server-probe-$timestamp.txt"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

Start-Transcript -Path $outPath -Force | Out-Null

try {
    Write-Section "CodexTeamUp Manual Probe"
    Write-Host "timestamp=$(Get-Date -Format o)"
    Write-Host "user=$env:USERNAME"
    Write-Host "userProfile=$env:USERPROFILE"
    Write-Host "repoRoot=$repoRoot"
    Write-Host "logPath=$outPath"

    Write-Section "Codex CLI"
    $resolvedCodexExe = Resolve-CodexExe -RequestedPath $CodexExe
    if ([string]::IsNullOrWhiteSpace($resolvedCodexExe)) {
        Write-Host "codexFound=False"
        Write-Host "codex command was not found on PATH or in known install locations."
        Write-Host "Try rerunning with: -CodexExe `"$env:LOCALAPPDATA\OpenAI\Codex\bin\codex.exe`""
        return
    }

    Write-Host "codexFound=True"
    Write-Host "codexSource=$resolvedCodexExe"
    $codexItem = Get-Item -LiteralPath $resolvedCodexExe -ErrorAction SilentlyContinue
    if ($null -ne $codexItem) {
        Write-Host "codexVersion=$($codexItem.VersionInfo.FileVersion)"
    }
    $onPath = Get-Command codex -ErrorAction SilentlyContinue
    Write-Host "codexOnPath=$($null -ne $onPath)"

    Write-Section "Help Checks"
    $helpChecks = @(
        @("--help"),
        @("app-server", "--help"),
        @("app-server", "proxy", "--help"),
        @("remote-control", "--help"),
        @("app-server", "generate-json-schema", "--help")
    )

    foreach ($check in $helpChecks) {
        $label = "codex " + ($check -join " ")
        $null = & $resolvedCodexExe @check 2>&1
        Write-Host "$label exit=$LASTEXITCODE"
    }

    Write-Section "Codex Home"
    $env:CODEX_HOME = $CodexHome
    Write-Host "CODEX_HOME=$env:CODEX_HOME"
    Write-Host "codexHomeExists=$(Test-Path -LiteralPath $env:CODEX_HOME)"
    Write-Host "sessionIndexExists=$(Test-Path -LiteralPath (Join-Path $env:CODEX_HOME 'session_index.jsonl'))"
    Write-Host "stateDbExists=$(Test-Path -LiteralPath (Join-Path $env:CODEX_HOME 'state_5.sqlite'))"
    Write-Host "logsDbExists=$(Test-Path -LiteralPath (Join-Path $env:CODEX_HOME 'logs_2.sqlite'))"

    $controlDir = Join-Path $env:CODEX_HOME "app-server-control"
    $socketPath = Join-Path $controlDir "app-server-control.sock"
    Write-Host "controlDir=$controlDir"
    Write-Host "controlDirExists=$(Test-Path -LiteralPath $controlDir)"
    Write-Host "socketPath=$socketPath"
    Write-Host "socketExists=$(Test-Path -LiteralPath $socketPath)"

    Write-Section "Codex Processes"
    Get-Process Codex,codex -ErrorAction SilentlyContinue |
        Select-Object Id, ProcessName, Path, MainWindowTitle |
        Format-Table -AutoSize

    Write-Section "Desktop Proxy Probe"
    Invoke-CodexProxyProbe -CodexExe $resolvedCodexExe -Limit $ThreadLimit

    Write-Section "Standalone stdio App-Server Probe"
    $stdioLines = @(
        '{"id":1,"method":"initialize","params":{"clientInfo":{"name":"CodexTeamUpManualProbe","version":"0.1.0"},"capabilities":{"experimentalApi":true}}}'
        '{"method":"initialized"}'
        '{"id":2,"method":"thread/list","params":{"limit":1,"useStateDbOnly":true}}'
    )
    $stdioRaw = $stdioLines | & $resolvedCodexExe app-server --listen stdio:// 2>&1
    $stdioExit = $LASTEXITCODE
    Redact-Text (($stdioRaw | ForEach-Object { $_.ToString() }) -join [Environment]::NewLine)
    Write-Host "stdioExitCode=$stdioExit"

    if ($GenerateSchemas) {
        Write-Section "Schema Generation"
        $schemaDir = Join-Path $outDir "schemas-$timestamp"
        New-Item -ItemType Directory -Force -Path $schemaDir | Out-Null
        $schemaRaw = & $resolvedCodexExe app-server generate-json-schema --experimental --out $schemaDir 2>&1
        $schemaExit = $LASTEXITCODE
        Redact-Text (($schemaRaw | ForEach-Object { $_.ToString() }) -join [Environment]::NewLine)
        Write-Host "schemaDir=$schemaDir"
        Write-Host "schemaExitCode=$schemaExit"
        if (Test-Path -LiteralPath (Join-Path $schemaDir "ClientRequest.json")) {
            Select-String -Path (Join-Path $schemaDir "ClientRequest.json") -Pattern "thread/list","thread/read","thread/start","turn/start","turn/steer" |
                ForEach-Object { $_.Line.Trim() } |
                Select-Object -Unique
        }
    }

    Write-Section "Summary Hints"
    Write-Host "If socketExists=True and threadListResult=True, CodexTeamUp should be able to talk to the Desktop app-server with your user rights."
    Write-Host "If socketExists=False or proxy fails with a socket error, Desktop is not publishing the expected control socket right now."
    Write-Host "Please share this log only after checking it for anything you do not want to share:"
    Write-Host $outPath
}
finally {
    Stop-Transcript | Out-Null
}
