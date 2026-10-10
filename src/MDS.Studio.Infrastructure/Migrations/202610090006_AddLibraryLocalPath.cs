using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MDS.Studio.Infrastructure.Migrations;

[DbContext(typeof(StudioDbContext))]
[Migration("202610090006_AddLibraryLocalPath")]
public partial class AddLibraryLocalPath : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
        => migrationBuilder.AddColumn<string>(
            name: "LocalPath",
            table: "file_assets",
            type: "character varying(1024)",
            maxLength: 1024,
            nullable: true);

    protected override void Down(MigrationBuilder migrationBuilder)
        => migrationBuilder.DropColumn(name: "LocalPath", table: "file_assets");
}
