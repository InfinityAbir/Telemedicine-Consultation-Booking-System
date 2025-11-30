using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Telemed.Migrations
{
    /// <inheritdoc />
    public partial class InitialPrescriptionWithFilePath : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FilePath",
                table: "Prescriptions",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FilePath",
                table: "Prescriptions");
        }
    }
}
