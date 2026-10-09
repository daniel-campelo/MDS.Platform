[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$composeFile = Join-Path $repositoryRoot 'deployment\docker-compose.staging.yml'
$processFile = Join-Path $repositoryRoot 'work\staging-processes.json'

Push-Location $repositoryRoot
try {
    docker compose -f $composeFile ps
    if ($LASTEXITCODE -ne 0) { throw 'Unable to read the Docker Compose status.' }

    $health = Invoke-RestMethod -Uri 'http://localhost:5000/health' -TimeoutSec 10
    $version = Invoke-RestMethod -Uri 'http://localhost:5000/version' -TimeoutSec 10
    $agents = Invoke-RestMethod -Uri 'http://localhost:5000/agents' -TimeoutSec 10

    Write-Host "API health: $health" -ForegroundColor Green
    Write-Host "API version: $($version.version) ($($version.environment))"
    if (@($agents).Count -eq 0) {
        Write-Warning 'No Local Agent has enrolled yet. Wait up to 30 seconds and run this script again.'
    }
    else {
        $agents | Select-Object agentId, machineName, version, isAuthorized, lastHeartbeatAt | Format-Table -AutoSize
    }

    if (Test-Path -LiteralPath $processFile) {
        $processes = Get-Content -LiteralPath $processFile -Raw | ConvertFrom-Json
        foreach ($processId in @($processes.ApiProcessId, $processes.AgentProcessId)) {
            $process = Get-Process -Id $processId -ErrorAction SilentlyContinue
            if ($null -eq $process) { Write-Warning "Managed process $processId is not running." }
        }
    }
}
finally {
    Pop-Location
}
