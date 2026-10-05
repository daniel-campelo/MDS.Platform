using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace MDS.Studio.Infrastructure.Migrations;

[DbContext(typeof(StudioDbContext))]
[Migration("202610030003_AddAgentJobTarget")]
public partial class AddAgentJobTarget : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
        => migrationBuilder.Sql("ALTER TABLE background_jobs ADD COLUMN IF NOT EXISTS \"TargetAgentId\" uuid NULL;");

    protected override void Down(MigrationBuilder migrationBuilder) { }
}
