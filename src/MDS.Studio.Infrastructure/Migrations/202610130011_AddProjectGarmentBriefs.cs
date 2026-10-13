using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MDS.Studio.Infrastructure.Migrations;

[DbContext(typeof(StudioDbContext))]
[Migration("202610130011_AddProjectGarmentBriefs")]
public partial class AddProjectGarmentBriefs : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "project_garment_briefs",
            columns: table => new
            {
                ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                GarmentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                Model = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                Fabric = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                Colors = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                SizeRange = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                Quantity = table.Column<int>(type: "integer", nullable: true),
                Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_project_garment_briefs", x => x.ProjectId));
    }

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable(name: "project_garment_briefs");
}
