using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MDS.Studio.Infrastructure.Migrations;

[DbContext(typeof(StudioDbContext))]
[Migration("202610090005_AddProjectOrganization")]
public partial class AddProjectOrganization : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "Description",
            table: "studio_projects",
            type: "character varying(2000)",
            maxLength: 2000,
            nullable: true);
        migrationBuilder.AddColumn<string>(
            name: "Status",
            table: "studio_projects",
            type: "character varying(40)",
            maxLength: 40,
            nullable: false,
            defaultValue: "Ativo");
        migrationBuilder.CreateTable(
            name: "project_organization_items",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                Title = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_project_organization_items", x => x.Id));
        migrationBuilder.CreateIndex(name: "IX_project_organization_items_ProjectId", table: "project_organization_items", column: "ProjectId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "project_organization_items");
        migrationBuilder.DropColumn(name: "Description", table: "studio_projects");
        migrationBuilder.DropColumn(name: "Status", table: "studio_projects");
    }
}
