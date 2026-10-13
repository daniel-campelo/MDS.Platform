using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MDS.Studio.Infrastructure.Migrations;

[DbContext(typeof(StudioDbContext))]
[Migration("202610130013_AddFrontArtworkPosition")]
public partial class AddFrontArtworkPosition : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
        => migrationBuilder.AddColumn<string>(
            name: "FrontArtworkPosition",
            table: "project_garment_briefs",
            type: "character varying(30)",
            maxLength: 30,
            nullable: true);

    protected override void Down(MigrationBuilder migrationBuilder)
        => migrationBuilder.DropColumn(name: "FrontArtworkPosition", table: "project_garment_briefs");
}
