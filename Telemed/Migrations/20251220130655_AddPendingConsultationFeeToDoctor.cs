using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Telemed.Migrations
{
    /// <inheritdoc />
    public partial class AddPendingConsultationFeeToDoctor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "PendingConsultationFee",
                table: "Doctors",
                type: "decimal(18,2)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PendingConsultationFee",
                table: "Doctors");
        }
    }
}
