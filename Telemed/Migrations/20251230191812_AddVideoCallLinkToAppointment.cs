using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Telemed.Migrations
{
    /// <inheritdoc />
    public partial class AddVideoCallLinkToAppointment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "VideoCallLink",
                table: "Appointments",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "VideoCallLink",
                table: "Appointments");
        }
    }
}
