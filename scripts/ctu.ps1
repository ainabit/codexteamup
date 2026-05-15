param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$CtuArgs
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$publishedExe = Join-Path $repoRoot ".ctu\tools\ctu\CodexTeamUp.Cli.exe"
$project = Join-Path $repoRoot "src\CodexTeamUp.Cli\CodexTeamUp.Cli.csproj"

if (Test-Path -LiteralPath $publishedExe -PathType Leaf) {
    & $publishedExe @CtuArgs
    exit $LASTEXITCODE
}

& dotnet run --project $project -- @CtuArgs
exit $LASTEXITCODE
