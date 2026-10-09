# MDS Studio 0.0.1 — Inicialização da estação de homologação

Este roteiro inicia somente a fundação M0: PostgreSQL, API e Local Agent. Não há tela de produto nesta etapa.

## Inicialização rápida

Na raiz do projeto, abra um PowerShell e execute:

```powershell
.\scripts\Start-Staging.ps1
```

O roteiro solicita a senha atual do banco sem exibi-la, inicia PostgreSQL na porta `5433`, compila a solução e inicia API e Local Agent em segundo plano. Para uma execução posterior sem recompilar, use `./scripts/Start-Staging.ps1 -SkipBuild`.

Verifique o ambiente com:

```powershell
.\scripts\Test-Staging.ps1
```

Os registros ficam em `work\logs`. Para encerrar API, Agent e o contêiner, sem apagar os dados do banco:

```powershell
.\scripts\Stop-Staging.ps1
```

As etapas manuais abaixo continuam disponíveis para diagnóstico.

## 1. Banco de dados

Abra o Docker Desktop. Em um PowerShell na raiz do projeto, execute:

```powershell
docker compose -f deployment/docker-compose.staging.yml up -d
docker compose -f deployment/docker-compose.staging.yml ps
```

O serviço `postgres` deve aparecer como `Up` na porta local `5433`.

## 2. API

Em um PowerShell na raiz do projeto, informe a senha já configurada para o banco. A senha não deve ser adicionada a arquivos ou ao Git.

```powershell
$dbPassword = Read-Host "Digite a senha atual do banco"
$env:ASPNETCORE_ENVIRONMENT = "Staging"
$env:ConnectionStrings__Studio = "Host=localhost;Port=5433;Database=mds_studio;Username=mds_studio;Password="
$env:ConnectionStrings__Studio += $dbPassword
& "C:\Program Files\dotnet\dotnet.exe" run --project src\MDS.Studio.Api --configuration Release --no-build
```

Deixe essa janela aberta. O processo não retorna ao prompt enquanto a API está ativa. Verifique em `http://localhost:5000/health`.

## 3. Local Agent

Em outro PowerShell na raiz do projeto, execute:

```powershell
& "C:\Program Files\dotnet\dotnet.exe" run --project src\MDS.LocalAgent --configuration Release --no-build
```

Também é normal essa janela permanecer aberta. Em até 30 segundos, `http://localhost:5000/agents` deve mostrar um heartbeat recente.

## 4. Verificação M0

O Agent precisa aparecer com `isAuthorized: true`. Para consultar um job, use `GET /jobs/{jobId}`. Um `AgentPingJob` concluído deve aparecer como `Completed`.

Para interromper a API ou o Agent, use `Ctrl + C` somente na janela correspondente.
