using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MDS.Studio.Infrastructure.Migrations;

[DbContext(typeof(StudioDbContext))]
[Migration("202610090007_LinkLibraryToProjects")]
public partial class LinkLibraryToProjects : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(name: "ProjectId", table: "file_assets", type: "uuid", nullable: true);
        migrationBuilder.CreateIndex(name: "IX_file_assets_ProjectId", table: "file_assets", column: "ProjectId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "IX_file_assets_ProjectId", table: "file_assets");
        migrationBuilder.DropColumn(name: "ProjectId", table: "file_assets");
    }
}
