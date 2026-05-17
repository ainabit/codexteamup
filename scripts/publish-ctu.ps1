param(
    [string]$Configuration = "Release",
    [string]$ArtifactsPath = ""
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$toolsRoot = Join-Path $repoRoot ".ctu\tools"
$runtimeRoot = Join-Path $repoRoot ".ctu\runtime"
$controllerRuntimeRoot = Join-Path $runtimeRoot "controllers\default"
if ([string]::IsNullOrWhiteSpace($ArtifactsPath)) {
    $ArtifactsPath = Join-Path ([System.IO.Path]::GetTempPath()) ("ctu-publish-artifacts-" + [guid]::NewGuid().ToString("N"))
}

$ArtifactsPath = [System.IO.Path]::GetFullPath($ArtifactsPath)

New-Item -ItemType Directory -Force -Path $toolsRoot | Out-Null
New-Item -ItemType Directory -Force -Path $controllerRuntimeRoot | Out-Null
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
    dotnet publish $projectPath -c $Configuration -o $outputPath --artifacts-path $ArtifactsPath --nologo
    if ($LASTEXITCODE -ne 0) {
        if ($Optional) {
            Write-Warning "Could not republish optional $Name. If Codex Desktop is using the wrapper, close Desktop before republishing it. CLI/MCP publishing can still be used."
            return
        }

        throw "Publishing $Name failed with exit code $LASTEXITCODE."
    }
}

function Publish-ControllerRuntime {
    $projectPath = Join-Path $repoRoot "src\CodexTeamUp.Controller.Default\CodexTeamUp.Controller.Default.csproj"
    $stagingPath = Join-Path $ArtifactsPath "controller-runtime-default"
    Write-Host "Publishing default controller runtime -> $controllerRuntimeRoot"
    dotnet publish $projectPath -c $Configuration -o $stagingPath --artifacts-path $ArtifactsPath --nologo
    if ($LASTEXITCODE -ne 0) {
        throw "Publishing default controller runtime failed with exit code $LASTEXITCODE."
    }

    foreach ($file in Get-ChildItem -LiteralPath $stagingPath -File) {
        $extension = [System.IO.Path]::GetExtension($file.Name)
        if ($extension -in @(".dll", ".pdb", ".json")) {
            Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $controllerRuntimeRoot $file.Name) -Force
        }
    }

    Set-Content -LiteralPath (Join-Path $controllerRuntimeRoot "current-plugin.txt") -Value (Join-Path $controllerRuntimeRoot "CodexTeamUp.Controller.Default.dll") -Encoding UTF8
}

Publish-Project -Name "ctu" -Project "src\CodexTeamUp.Cli\CodexTeamUp.Cli.csproj"
Publish-Project -Name "service" -Project "src\CodexTeamUp.Service\CodexTeamUp.Service.csproj"
Publish-ControllerRuntime
Publish-Project -Name "wrapper" -Project "src\CodexTeamUp.CodexWrapper\CodexTeamUp.CodexWrapper.csproj" -Optional

Write-Host ""
Write-Host "Published tools:"
Write-Host "  CLI:     $(Join-Path $toolsRoot 'ctu\CodexTeamUp.Cli.exe')"
Write-Host "  Wrapper: $(Join-Path $toolsRoot 'wrapper\CodexTeamUp.CodexWrapper.exe')"
Write-Host "  Service: $(Join-Path $toolsRoot 'service\CodexTeamUp.Service.exe')"
Write-Host "  Controller runtime: $(Join-Path $controllerRuntimeRoot 'CodexTeamUp.Controller.Default.dll')"
Write-Host ""
Write-Host "Agent command shim:"
Write-Host "  powershell -ExecutionPolicy Bypass -File `"$repoRoot\scripts\ctu.ps1`" wrapper status"
