[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$projectFile = Join-Path $repositoryRoot 'src\MDS.Studio.Desktop\MDS.Studio.Desktop.csproj'
$studioExecutable = Join-Path $repositoryRoot 'src\MDS.Studio.Desktop\bin\Release\net8.0-windows\MDS.Studio.Desktop.exe'
$dotnetPath = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'

if (-not (Test-Path -LiteralPath $dotnetPath)) {
    $dotnetPath = (Get-Command dotnet -ErrorAction Stop).Source
}

if (Get-Process -Name 'MDS.Studio.Desktop' -ErrorAction SilentlyContinue) {
    throw 'Feche o MDS Studio antes de atualizar. Depois execute este comando novamente.'
}

& $dotnetPath build $projectFile --configuration Release
if ($LASTEXITCODE -ne 0) {
    throw 'Não foi possível atualizar o MDS Studio. A compilação apresentou erros.'
}

Start-Process -FilePath $studioExecutable -WorkingDirectory (Split-Path -Parent $studioExecutable)
Write-Host 'MDS Studio atualizado e aberto.' -ForegroundColor Green
