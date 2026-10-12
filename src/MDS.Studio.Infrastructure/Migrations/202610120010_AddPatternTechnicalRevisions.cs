using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MDS.Studio.Infrastructure.Migrations;

[DbContext(typeof(StudioDbContext))]
[Migration("202610120010_AddPatternTechnicalRevisions")]
public partial class AddPatternTechnicalRevisions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "pattern_technical_revisions",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                PatternId = table.Column<Guid>(type: "uuid", nullable: false),
                FilePath = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                Version = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                RecordedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_pattern_technical_revisions", x => x.Id));
        migrationBuilder.CreateIndex(name: "IX_pattern_technical_revisions_PatternId_Version", table: "pattern_technical_revisions", columns: new[] { "PatternId", "Version" }, unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable(name: "pattern_technical_revisions");
}
