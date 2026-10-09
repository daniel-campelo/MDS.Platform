# MDS Studio 0.0.1 — M0 Foundation

## Boundary

This implementation establishes the Studio technical foundation only. MDS Core remains the owner of commercial data and the future App remains the customer channel. No Pattern, Graphics, Smart Size, product, customer, production, RIP, or real-device functionality is included.

## What is present

- .NET 8 solution with SharedKernel, Domain, Application, Infrastructure, API, Windows Desktop shell, and Local Agent.
- Version `0.0.1` embedded centrally for Desktop, API and Agent.
- Development, Staging (homologação) and Production configuration overlays; Desktop visibly identifies homologação.
- PostgreSQL EF Core foundation with versioned migrations for file assets, agents, jobs, outbox, inbox and audit events. The schema repair migrations are safe for the initial homologação database.
- `GET /health`, `GET /version`, authenticated `GET /me`, and authorized diagnostics endpoint.
- Development-only authentication foundation with the three M0 permission names. The M1 [Shared Identity contract](SHARED-IDENTITY-CONTRACT.md) defines the production JWT boundary; no production credential is present here.
- Local filesystem implementation of `IFileStorage`, SHA-256 metadata, path containment and archive support.
- Correlation-ID middleware and structured logging scope.
- Local Agent worker foundation with SQLite entities and sandbox-only test-file writer.
- Enrollment-request, administrative authorization, heartbeat and authorized `AgentPingJob` queue/claim/complete endpoints. The Agent persists a processed event before accepting the same job again; completion is registered through the inbox and emits an outbox event.
- Unit/integration test coverage for units, hashing, Result, permissions, job transitions, storage, agent sandbox behavior, and the API `AgentPingJob` contract.

## Configuration and safe startup

Copy connection strings and secrets to `appsettings.Local.json` or environment variables. Do not commit them. Staging PostgreSQL can be started from `deployment/docker-compose.staging.yml` after supplying `MDS_STUDIO_POSTGRES_PASSWORD`.

On a workstation with .NET SDK 8 installed:

```powershell
dotnet restore MDS.Platform.sln
dotnet build MDS.Platform.sln --configuration Release
dotnet test MDS.Platform.sln --configuration Release
```

Start the API using `ASPNETCORE_ENVIRONMENT=Staging` with the configured staging connection. The Staging profile applies the Foundation migration automatically; Production deliberately does not. The migration is intentionally restricted to Foundation tables.

## Acceptance status

The workstation now has .NET SDK 8.0.425 installed. The complete solution builds in Release with zero warnings and zero errors. All ten automated tests pass: SharedKernel/domain fundamentals, permissions, storage behavior, Agent idempotency/sandbox behavior, and the API job contract. The initial build found and corrected an invalid `sealed` declaration on the generic `Result<T>` base type; test projects now explicitly target .NET 8, and package references are aligned to the available .NET 8.0.11 Foundation stack.

`NuGet.Config` deliberately uses only the official NuGet v3 feed. PostgreSQL and identity deployment are intentionally not provisioned automatically: they require environment-specific credentials and operational approval.

Docker Desktop is available on the workstation. The MDS Staging database is exposed locally on port `5433` to avoid interfering with any existing PostgreSQL instance on `5432`. Start the API in Staging to apply the Foundation schema.

## Workstation proof completed

On 2026-10-03, the station completed the M0 operational proof:

1. PostgreSQL Staging started on port `5433`.
2. API `0.0.1` started in `Staging`; `/health` returned `Healthy` and `/version` returned the expected version.
3. Local Agent `MDSCOMP01` enrolled, was explicitly authorized, and sent a successful heartbeat.
4. An `AgentPingJob` was queued for the authorized Agent, executed from its SQLite-backed worker, and observed as `Completed` through the API.

This proves the bounded M0 route: API → PostgreSQL/outbox → Local Agent/SQLite → API inbox/completion.

## Operational handoff

The repeatable station startup procedure is documented in [WORKSTATION-STARTUP.md](WORKSTATION-STARTUP.md). The workstation also includes `scripts/Start-Staging.ps1`, `scripts/Test-Staging.ps1`, and `scripts/Stop-Staging.ps1` to start, verify and stop the bounded Staging stack without exposing the database password or deleting the database volume.

The M0 technical foundation and its bounded job contract are complete. Next, review the M0 acceptance checklist and version the initial baseline; do not begin Pattern, Graphics, Smart Size, product, customer, or production work during M0.
