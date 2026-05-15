param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$toolsRoot = Join-Path $repoRoot ".ctu\tools"

New-Item -ItemType Directory -Force -Path $toolsRoot | Out-Null
$legacyMcpToolRoot = Join-Path $toolsRoot "mcp"
if (Test-Path -LiteralPath $legacyMcpToolRoot) {
    Remove-Item -LiteralPath $legacyMcpToolRoot -Recurse -Force
}

function Publish-Project {
    param(
        [string]$Name,
        [string]$Project,
        [switch]$Optional
    )

    $item = @{ Name = $Name; Project = $Project }
    $projectPath = Join-Path $repoRoot $item.Project
    $outputPath = Join-Path $toolsRoot $item.Name
    Write-Host "Publishing $($item.Name) -> $outputPath"
    dotnet publish $projectPath -c $Configuration -o $outputPath --nologo
    if ($LASTEXITCODE -ne 0) {
        if ($Optional) {
            Write-Warning "Could not republish optional $Name. If Codex Desktop is using the wrapper, close Desktop before republishing it. CLI/MCP publishing can still be used."
            return
        }

        throw "Publishing $Name failed with exit code $LASTEXITCODE."
    }
}

Publish-Project -Name "ctu" -Project "src\CodexTeamUp.Cli\CodexTeamUp.Cli.csproj"
Publish-Project -Name "service" -Project "src\CodexTeamUp.Service\CodexTeamUp.Service.csproj"
Publish-Project -Name "wrapper" -Project "src\CodexTeamUp.CodexWrapper\CodexTeamUp.CodexWrapper.csproj" -Optional

Write-Host ""
Write-Host "Published tools:"
Write-Host "  CLI:     $(Join-Path $toolsRoot 'ctu\CodexTeamUp.Cli.exe')"
Write-Host "  Wrapper: $(Join-Path $toolsRoot 'wrapper\CodexTeamUp.CodexWrapper.exe')"
Write-Host "  Service: $(Join-Path $toolsRoot 'service\CodexTeamUp.Service.exe')"
Write-Host ""
Write-Host "Agent command shim:"
Write-Host "  powershell -ExecutionPolicy Bypass -File `"$repoRoot\scripts\ctu.ps1`" wrapper status"
