param(
    [string]$Python = "",
    [switch]$Require
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($Python)) {
    $command = Get-Command python -ErrorAction SilentlyContinue
    if ($null -eq $command) {
        if ($Require) {
            throw "Python was not found."
        }

        Write-Host "SKIP Codex Python SDK probe: python was not found."
        exit 0
    }

    $Python = $command.Source
}

$probe = @'
import importlib.util
import json
import sys

if importlib.util.find_spec("openai_codex") is None:
    print(json.dumps({"status": "skipped", "reason": "openai_codex is not installed"}))
    sys.exit(2)

try:
    from openai_codex import Codex

    with Codex() as codex:
        metadata = codex.metadata
        print(json.dumps({"status": "ok", "metadataType": type(metadata).__name__}))
except Exception as exc:
    print(json.dumps({"status": "failed", "error": str(exc)}))
    sys.exit(1)
'@

$probePath = [System.IO.Path]::ChangeExtension([System.IO.Path]::GetTempFileName(), ".py")
try {
    Set-Content -LiteralPath $probePath -Value $probe -Encoding UTF8
    & $Python $probePath
    $exitCode = $LASTEXITCODE
    if ($exitCode -eq 2 -and -not $Require) {
        exit 0
    }

    exit $exitCode
} finally {
    Remove-Item -LiteralPath $probePath -ErrorAction SilentlyContinue
}
