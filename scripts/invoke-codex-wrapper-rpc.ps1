param(
    [Parameter(Mandatory = $true)]
    [string]$Method,

    [string]$ParamsJson = "{}",
    [string]$PipeName = "codexteamup-appserver",
    [int]$TimeoutSeconds = 120
)

$ErrorActionPreference = "Stop"

$params = $ParamsJson | ConvertFrom-Json
$request = @{
    method = $Method
    params = $params
} | ConvertTo-Json -Depth 50 -Compress

$pipe = [System.IO.Pipes.NamedPipeClientStream]::new(".", $PipeName, [System.IO.Pipes.PipeDirection]::InOut, [System.IO.Pipes.PipeOptions]::None)
try {
    $pipe.Connect($TimeoutSeconds * 1000)

    $writer = [System.IO.StreamWriter]::new($pipe, [System.Text.UTF8Encoding]::new($false), 4096, $true)
    $writer.NewLine = "`n"
    $writer.AutoFlush = $true

    $reader = [System.IO.StreamReader]::new($pipe, [System.Text.Encoding]::UTF8, $false, 4096, $true)

    $writer.WriteLine($request)
    $response = $reader.ReadLine()
    if ($null -eq $response) {
        throw "Wrapper pipe closed without a response."
    }

    $response
} finally {
    $pipe.Dispose()
}
