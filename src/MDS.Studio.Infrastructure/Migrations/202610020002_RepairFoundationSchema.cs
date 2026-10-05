using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace MDS.Studio.Infrastructure.Migrations;

// Safely repairs partially initialized M0 databases created before the first
// handwritten migration had EF migration metadata.
[DbContext(typeof(StudioDbContext))]
[Migration("202610020002_RepairFoundationSchema")]
public partial class RepairFoundationSchema : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE IF NOT EXISTS file_assets (
              "Id" uuid PRIMARY KEY, "OriginalName" text NOT NULL, "ContentType" text NOT NULL,
              "SizeBytes" bigint NOT NULL, "Sha256" text NULL, "Status" text NOT NULL, "CreatedAt" timestamp with time zone NOT NULL);
            CREATE TABLE IF NOT EXISTS local_agents (
              "Id" uuid PRIMARY KEY, "MachineName" text NOT NULL, "Version" text NOT NULL,
              "IsAuthorized" boolean NOT NULL, "LastHeartbeatAt" timestamp with time zone NULL);
            CREATE TABLE IF NOT EXISTS agent_capabilities (
              "Id" uuid PRIMARY KEY, "AgentId" uuid NOT NULL, "Name" text NOT NULL, "Version" text NOT NULL);
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_agent_capabilities_AgentId_Name" ON agent_capabilities ("AgentId", "Name");
            CREATE TABLE IF NOT EXISTS background_jobs (
              "Id" uuid PRIMARY KEY, "Type" text NOT NULL, "CorrelationId" text NOT NULL, "Status" text NOT NULL);
            CREATE TABLE IF NOT EXISTS outbox_events (
              "Id" uuid PRIMARY KEY, "Type" text NOT NULL, "Payload" text NOT NULL, "CorrelationId" text NOT NULL,
              "OccurredAt" timestamp with time zone NOT NULL, "ProcessedAt" timestamp with time zone NULL);
            CREATE TABLE IF NOT EXISTS inbox_events (
              "Id" uuid PRIMARY KEY, "ProcessedAt" timestamp with time zone NOT NULL);
            CREATE TABLE IF NOT EXISTS audit_events (
              "Id" uuid PRIMARY KEY, "Operation" text NOT NULL, "CorrelationId" text NOT NULL, "OccurredAt" timestamp with time zone NOT NULL);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // A repair migration must never remove a database that may contain M0 work.
    }
}
