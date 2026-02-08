using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Telemed.Migrations
{
    /// <inheritdoc />
    public partial class UpdateAppointmentStatusEnum : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "RefundAmount",
                table: "Payments",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RefundDate",
                table: "Payments",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RefundStatus",
                table: "Payments",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "StripeRefundId",
                table: "Payments",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RefundAmount",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "RefundDate",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "RefundStatus",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "StripeRefundId",
                table: "Payments");
        }
    }
}
