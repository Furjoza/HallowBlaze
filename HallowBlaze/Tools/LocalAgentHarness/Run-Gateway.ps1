param(
    [switch] $Stop
)

$ErrorActionPreference = 'Stop'

$runtimeRoot = Join-Path $env:LOCALAPPDATA 'HallowBlaze\LocalAgentHarness'
$stateDirectory = Join-Path $runtimeRoot 'state'
$publishDirectory = Join-Path $runtimeRoot 'gateway'
$pidPath = Join-Path $stateDirectory 'gateway.pid'
$projectPath = Join-Path $PSScriptRoot 'Gateway\LocalAgentGateway.csproj'
$gatewayDll = Join-Path $publishDirectory 'LocalAgentGateway.dll'
$dotnet = (Get-Command dotnet -ErrorAction Stop).Source

function Get-GatewayProcess {
    if (-not (Test-Path -LiteralPath $pidPath -PathType Leaf)) { return $null }
    $savedPid = 0
    if (-not [int]::TryParse((Get-Content -LiteralPath $pidPath -Raw), [ref] $savedPid)) {
        return $null
    }
    return Get-Process -Id $savedPid -ErrorAction SilentlyContinue
}

if ($Stop) {
    $process = Get-GatewayProcess
    if ($null -ne $process) {
        Stop-Process -Id $process.Id -Force
    }
    Remove-Item -LiteralPath $pidPath -Force -ErrorAction SilentlyContinue
    Write-Output 'Local agent gateway stopped.'
    exit 0
}

$process = Get-GatewayProcess
if ($null -ne $process) {
    Write-Output "Local agent gateway is already running (PID $($process.Id))."
    exit 0
}

New-Item -ItemType Directory -Path $stateDirectory, $publishDirectory -Force | Out-Null
& $dotnet publish $projectPath --configuration Release --output $publishDirectory
if ($LASTEXITCODE -ne 0) {
    throw 'Local agent gateway publish failed.'
}

$process = Start-Process `
    -FilePath $dotnet `
    -ArgumentList @($gatewayDll) `
    -WorkingDirectory $publishDirectory `
    -WindowStyle Hidden `
    -PassThru
[IO.File]::WriteAllText($pidPath, [string] $process.Id, [Text.UTF8Encoding]::new($false))
Write-Output "Local agent gateway started on http://127.0.0.1:11435 (PID $($process.Id))."