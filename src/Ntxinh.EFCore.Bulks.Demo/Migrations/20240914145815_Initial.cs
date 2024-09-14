using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ntxinh.EFCore.Bulks.Demo.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tblDemoEntity",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Process_Id = table.Column<int>(type: "INTEGER", nullable: false),
                    Category_Id = table.Column<int>(type: "INTEGER", nullable: false),
                    Formula = table.Column<string>(type: "TEXT", nullable: false),
                    DefaultValue = table.Column<decimal>(type: "TEXT", nullable: false),
                    Active = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tblDemoEntity", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tblDemoEntity");
        }
    }
}
