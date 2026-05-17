param(
    [string]$Configuration = "Release",
    [string]$ArtifactsPath = "",
    [string]$RuntimePath = "",
    [switch]$RefreshSharedDependencies
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($ArtifactsPath)) {
    $ArtifactsPath = Join-Path ([System.IO.Path]::GetTempPath()) ("ctu-controller-runtime-artifacts-" + [guid]::NewGuid().ToString("N"))
}

if ([string]::IsNullOrWhiteSpace($RuntimePath)) {
    $RuntimePath = Join-Path $repoRoot ".ctu\runtime\controllers\default"
}

$ArtifactsPath = [System.IO.Path]::GetFullPath($ArtifactsPath)
$RuntimePath = [System.IO.Path]::GetFullPath($RuntimePath)
$projectPath = Join-Path $repoRoot "src\CodexTeamUp.Controller.Default\CodexTeamUp.Controller.Default.csproj"
$stagingPath = Join-Path $ArtifactsPath "controller-runtime-default"

New-Item -ItemType Directory -Force -Path $RuntimePath | Out-Null

Write-Host "Publishing CodexTeamUp controller runtime"
Write-Host "  project:  $projectPath"
Write-Host "  staging:  $stagingPath"
Write-Host "  runtime:  $RuntimePath"

dotnet publish $projectPath -c $Configuration -o $stagingPath --artifacts-path $ArtifactsPath --nologo
if ($LASTEXITCODE -ne 0) {
    throw "Publishing controller runtime failed with exit code $LASTEXITCODE."
}

$publishVersioned = $false
foreach ($file in Get-ChildItem -LiteralPath $stagingPath -File) {
    $extension = [System.IO.Path]::GetExtension($file.Name)
    if ($extension -notin @(".dll", ".pdb", ".json")) {
        continue
    }

    $isDefaultControllerFile = $file.Name.StartsWith("CodexTeamUp.Controller.Default.", [StringComparison]::OrdinalIgnoreCase)
    if (-not $isDefaultControllerFile -and -not $RefreshSharedDependencies) {
        continue
    }

    $destination = Join-Path $RuntimePath $file.Name
    try {
        Copy-Item -LiteralPath $file.FullName -Destination $destination -Force
    }
    catch {
        if ($file.Name.Equals("CodexTeamUp.Controller.Default.dll", [StringComparison]::OrdinalIgnoreCase)) {
            Write-Warning "Active controller DLL is locked; publishing a versioned hot-reload runtime instead."
            $publishVersioned = $true
            break
        }

        Write-Warning "Skipped locked runtime sidecar: $destination"
    }
}

if ($publishVersioned) {
    $versionName = (Get-Date).ToString("yyyyMMdd-HHmmss") + "-" + [guid]::NewGuid().ToString("N").Substring(0, 8)
    $versionedRuntimePath = Join-Path (Join-Path $RuntimePath "releases") $versionName
    New-Item -ItemType Directory -Force -Path $versionedRuntimePath | Out-Null

    foreach ($file in Get-ChildItem -LiteralPath $stagingPath -File) {
        $extension = [System.IO.Path]::GetExtension($file.Name)
        if ($extension -in @(".dll", ".pdb", ".json")) {
            Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $versionedRuntimePath $file.Name) -Force
        }
    }

    $RuntimePath = $versionedRuntimePath
}

$pluginPath = Join-Path $RuntimePath "CodexTeamUp.Controller.Default.dll"
if (-not (Test-Path -LiteralPath $pluginPath -PathType Leaf)) {
    throw "Controller runtime plugin was not published: $pluginPath"
}

$pointerPath = Join-Path (Join-Path $repoRoot ".ctu\runtime\controllers\default") "current-plugin.txt"
Set-Content -LiteralPath $pointerPath -Value $pluginPath -Encoding UTF8

Write-Host "Controller runtime ready: $pluginPath"
