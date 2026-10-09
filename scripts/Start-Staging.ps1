[CmdletBinding()]
param(
    [switch]$SkipBuild,
    [switch]$UseCoreSession,
    [string]$CoreApiUrl = 'http://192.168.7.14:8081/api'
)

$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$composeFile = Join-Path $repositoryRoot 'deployment\docker-compose.staging.yml'
$workDirectory = Join-Path $repositoryRoot 'work'
$logDirectory = Join-Path $workDirectory 'logs'
$processFile = Join-Path $workDirectory 'staging-processes.json'
$dotnetPath = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
$environmentNames = @(
    'MDS_STUDIO_POSTGRES_PASSWORD',
    'ASPNETCORE_ENVIRONMENT',
    'ASPNETCORE_URLS',
    'ConnectionStrings__Studio',
    'Authentication__Mode',
    'Authentication__CoreBaseUrl',
    'Authentication__RequireHttps'
)
$previousEnvironment = @{}
foreach ($environmentName in $environmentNames) {
    $previousEnvironment[$environmentName] = [Environment]::GetEnvironmentVariable($environmentName, 'Process')
}

if (-not (Test-Path -LiteralPath $dotnetPath)) {
    $dotnetPath = (Get-Command dotnet -ErrorAction Stop).Source
}

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    throw 'Docker Desktop is required. Start Docker Desktop and run this script again.'
}

New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null

$securePassword = Read-Host 'Digite a senha atual do banco' -AsSecureString
$passwordPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($securePassword)
try {
    $databasePassword = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($passwordPointer)

    # The value is kept only in this PowerShell session and inherited by its child processes.
    $env:MDS_STUDIO_POSTGRES_PASSWORD = $databasePassword
    $env:ASPNETCORE_ENVIRONMENT = 'Staging'
    $env:ASPNETCORE_URLS = 'http://localhost:5000'
    $env:ConnectionStrings__Studio = "Host=localhost;Port=5433;Database=mds_studio;Username=mds_studio;Password=$databasePassword"
    if ($UseCoreSession) {
        $coreUri = $null
        if (-not [Uri]::TryCreate($CoreApiUrl, [UriKind]::Absolute, [ref]$coreUri)) {
            throw 'CoreApiUrl must be a valid absolute URL, for example http://192.168.7.14:8081/api.'
        }
        $env:Authentication__Mode = 'CoreSession'
        $env:Authentication__CoreBaseUrl = $coreUri.AbsoluteUri.TrimEnd('/')
        $env:Authentication__RequireHttps = ($coreUri.Scheme -eq 'https').ToString().ToLowerInvariant()
    }

    Push-Location $repositoryRoot
    try {
        docker compose -f $composeFile up -d

        $databaseReady = $false
        for ($attempt = 1; $attempt -le 30; $attempt++) {
            docker compose -f $composeFile exec -T postgres pg_isready -U mds_studio -d mds_studio *> $null
            if ($LASTEXITCODE -eq 0) {
                $databaseReady = $true
                break
            }
            Start-Sleep -Seconds 2
        }
        if (-not $databaseReady) {
            throw 'PostgreSQL did not become ready within 60 seconds. Check Docker Desktop and try again.'
        }

        if (-not $SkipBuild) {
            & $dotnetPath build MDS.Platform.sln --configuration Release
            if ($LASTEXITCODE -ne 0) { throw 'The Release build failed; the services were not started.' }
        }

        $api = Start-Process -FilePath $dotnetPath -WorkingDirectory $repositoryRoot -WindowStyle Hidden -PassThru `
            -ArgumentList @('run', '--project', 'src\MDS.Studio.Api', '--configuration', 'Release', '--no-build') `
            -RedirectStandardOutput (Join-Path $logDirectory 'api.log') `
            -RedirectStandardError (Join-Path $logDirectory 'api.error.log')

        $apiReady = $false
        for ($attempt = 1; $attempt -le 20; $attempt++) {
            try {
                $response = Invoke-WebRequest -Uri 'http://localhost:5000/health' -UseBasicParsing -TimeoutSec 2
                if ($response.StatusCode -eq 200) {
                    $apiReady = $true
                    break
                }
            }
            catch {
                if ($api.HasExited) { break }
                Start-Sleep -Seconds 1
            }
        }
        if (-not $apiReady) {
            if (-not $api.HasExited) { Stop-Process -Id $api.Id -Force }
            throw "The API did not become ready. Verify the current database password and review $(Join-Path $logDirectory 'api.error.log')."
        }

        $agent = Start-Process -FilePath $dotnetPath -WorkingDirectory $repositoryRoot -WindowStyle Hidden -PassThru `
            -ArgumentList @('run', '--project', 'src\MDS.LocalAgent', '--configuration', 'Release', '--no-build') `
            -RedirectStandardOutput (Join-Path $logDirectory 'agent.log') `
            -RedirectStandardError (Join-Path $logDirectory 'agent.error.log')

        [pscustomobject]@{
            ApiProcessId = $api.Id
            AgentProcessId = $agent.Id
            StartedAtUtc = [DateTimeOffset]::UtcNow
        } | ConvertTo-Json | Set-Content -LiteralPath $processFile -Encoding utf8

        Write-Host 'MDS Studio Staging was started.' -ForegroundColor Green
        Write-Host 'Run .\scripts\Test-Staging.ps1 to verify API and Local Agent status.'
        Write-Host "Logs: $logDirectory"
    }
    finally {
        Pop-Location
    }
}
finally {
    if ($passwordPointer -ne [IntPtr]::Zero) {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($passwordPointer)
    }
    foreach ($environmentName in $environmentNames) {
        $previousValue = $previousEnvironment[$environmentName]
        if ($null -eq $previousValue) {
            Remove-Item "Env:$environmentName" -ErrorAction SilentlyContinue
        }
        else {
            Set-Item "Env:$environmentName" -Value $previousValue
        }
    }
}
