using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MDS.Studio.Infrastructure.Migrations;

[DbContext(typeof(StudioDbContext))]
[Migration("202610130012_AddFrontArtworkScale")]
public partial class AddFrontArtworkScale : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
        => migrationBuilder.AddColumn<string>(
            name: "FrontArtworkScale",
            table: "project_garment_briefs",
            type: "character varying(20)",
            maxLength: 20,
            nullable: true);

    protected override void Down(MigrationBuilder migrationBuilder)
        => migrationBuilder.DropColumn(name: "FrontArtworkScale", table: "project_garment_briefs");
}
