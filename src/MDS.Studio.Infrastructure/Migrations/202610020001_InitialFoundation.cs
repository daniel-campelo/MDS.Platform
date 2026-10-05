using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;

#nullable disable
namespace MDS.Studio.Infrastructure.Migrations;
[DbContext(typeof(StudioDbContext))]
[Migration("202610020001_InitialFoundation")]
public partial class InitialFoundation : Migration
{
 protected override void Up(MigrationBuilder m)
 {
  m.CreateTable(name: "file_assets", columns: t => new { Id=t.Column<Guid>(nullable:false), OriginalName=t.Column<string>(nullable:false), ContentType=t.Column<string>(nullable:false), SizeBytes=t.Column<long>(nullable:false), Sha256=t.Column<string>(nullable:true), Status=t.Column<string>(nullable:false), CreatedAt=t.Column<DateTimeOffset>(nullable:false) }, constraints: c => { c.PrimaryKey("PK_file_assets",x=>x.Id); });
  m.CreateTable(name: "local_agents", columns: t => new { Id=t.Column<Guid>(nullable:false), MachineName=t.Column<string>(nullable:false), Version=t.Column<string>(nullable:false), IsAuthorized=t.Column<bool>(nullable:false), LastHeartbeatAt=t.Column<DateTimeOffset>(nullable:true) }, constraints: c => { c.PrimaryKey("PK_local_agents",x=>x.Id); });
  m.CreateTable(name: "agent_capabilities", columns: t => new { Id=t.Column<Guid>(nullable:false), AgentId=t.Column<Guid>(nullable:false), Name=t.Column<string>(nullable:false), Version=t.Column<string>(nullable:false) }, constraints: c => { c.PrimaryKey("PK_agent_capabilities",x=>x.Id); });
  m.CreateIndex("IX_agent_capabilities_AgentId_Name", "agent_capabilities", new[]{"AgentId","Name"}, unique:true);
  m.CreateTable(name: "background_jobs", columns: t => new { Id=t.Column<Guid>(nullable:false), Type=t.Column<string>(nullable:false), CorrelationId=t.Column<string>(nullable:false), Status=t.Column<string>(nullable:false) }, constraints: c => { c.PrimaryKey("PK_background_jobs",x=>x.Id); });
  m.CreateTable(name: "outbox_events", columns: t => new { Id=t.Column<Guid>(nullable:false), Type=t.Column<string>(nullable:false), Payload=t.Column<string>(nullable:false), CorrelationId=t.Column<string>(nullable:false), OccurredAt=t.Column<DateTimeOffset>(nullable:false), ProcessedAt=t.Column<DateTimeOffset>(nullable:true) }, constraints: c => { c.PrimaryKey("PK_outbox_events",x=>x.Id); });
  m.CreateTable(name: "inbox_events", columns: t => new { Id=t.Column<Guid>(nullable:false), ProcessedAt=t.Column<DateTimeOffset>(nullable:false) }, constraints: c => { c.PrimaryKey("PK_inbox_events",x=>x.Id); });
  m.CreateTable(name: "audit_events", columns: t => new { Id=t.Column<Guid>(nullable:false), Operation=t.Column<string>(nullable:false), CorrelationId=t.Column<string>(nullable:false), OccurredAt=t.Column<DateTimeOffset>(nullable:false) }, constraints: c => { c.PrimaryKey("PK_audit_events",x=>x.Id); });
 }
 protected override void Down(MigrationBuilder m) { foreach (var t in new[]{"audit_events","inbox_events","outbox_events","background_jobs","agent_capabilities","local_agents","file_assets"}) m.DropTable(t); }
}
