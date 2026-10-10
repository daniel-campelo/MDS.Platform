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

# dotnet can hand off the process started by Start-Process to the application host.
# Always stop the API that owns the explicit staging port as well.
Get-NetTCPConnection -LocalPort 5000 -State Listen -ErrorAction SilentlyContinue |
    Select-Object -ExpandProperty OwningProcess -Unique |
    ForEach-Object {
        $process = Get-Process -Id $_ -ErrorAction SilentlyContinue
        if ($null -ne $process -and $process.ProcessName -eq 'MDS.Studio.Api') {
            Stop-Process -Id $_
        }
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
