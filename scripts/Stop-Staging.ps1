[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$composeFile = Join-Path $repositoryRoot 'deployment\docker-compose.staging.yml'
$processFile = Join-Path $repositoryRoot 'work\staging-processes.json'

if (Test-Path -LiteralPath $processFile) {
    $processes = Get-Content -LiteralPath $processFile -Raw | ConvertFrom-Json
    foreach ($processId in @($processes.ApiProcessId, $processes.AgentProcessId)) {
        $process = Get-Process -Id $processId -ErrorAction SilentlyContinue
        if ($null -ne $process) {
            Stop-Process -Id $processId
        }
    }
    Remove-Item -LiteralPath $processFile -Force
}

Push-Location $repositoryRoot
try {
    docker compose -f $composeFile stop
    if ($LASTEXITCODE -ne 0) { throw 'Unable to stop the PostgreSQL container.' }
    Write-Host 'MDS Studio Staging was stopped. PostgreSQL data volume was preserved.' -ForegroundColor Green
}
finally {
    Pop-Location
}
