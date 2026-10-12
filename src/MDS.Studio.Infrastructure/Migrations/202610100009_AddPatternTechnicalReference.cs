using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MDS.Studio.Infrastructure.Migrations;

[DbContext(typeof(StudioDbContext))]
[Migration("202610100009_AddPatternTechnicalReference")]
public partial class AddPatternTechnicalReference : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "TechnicalFilePath", table: "studio_patterns", type: "character varying(1024)", maxLength: 1024, nullable: true);
        migrationBuilder.AddColumn<string>(name: "TechnicalVersion", table: "studio_patterns", type: "character varying(80)", maxLength: 80, nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "TechnicalFilePath", table: "studio_patterns");
        migrationBuilder.DropColumn(name: "TechnicalVersion", table: "studio_patterns");
    }
}
