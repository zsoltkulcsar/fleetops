using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FleetOps.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class EpicTwelveNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "critical_email",
                schema: "fleetops",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "critical_push",
                schema: "fleetops",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "digest_email",
                schema: "fleetops",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "digest_date",
                schema: "fleetops",
                table: "notifications",
                type: "date",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "alert_covers",
                schema: "fleetops",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_id = table.Column<Guid>(type: "uuid", nullable: false),
                    to_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_date = table.Column<DateOnly>(type: "date", nullable: false),
                    to_date = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_alert_covers", x => x.id);
                    table.CheckConstraint("ck_alert_cover_dates", "to_date >= from_date and from_id <> to_id");
                    table.ForeignKey(
                        name: "FK_alert_covers_employees_from_id",
                        column: x => x.from_id,
                        principalSchema: "fleetops",
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_alert_covers_employees_to_id",
                        column: x => x.to_id,
                        principalSchema: "fleetops",
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "push_subscriptions",
                schema: "fleetops",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    endpoint = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    p256dh = table.Column<string>(type: "text", nullable: false),
                    auth = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_push_subscriptions", x => x.id);
                    table.ForeignKey(
                        name: "FK_push_subscriptions_employees_employee_id",
                        column: x => x.employee_id,
                        principalSchema: "fleetops",
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sms_daily_usage",
                schema: "fleetops",
                columns: table => new
                {
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sms_daily_usage", x => x.date);
                });

            migrationBuilder.CreateIndex(
                name: "IX_notifications_employee_id_alert_id_template_code_channel",
                schema: "fleetops",
                table: "notifications",
                columns: new[] { "employee_id", "alert_id", "template_code", "channel" },
                unique: true,
                filter: "alert_id is not null");

            migrationBuilder.CreateIndex(
                name: "IX_notifications_employee_id_template_code_channel_digest_date",
                schema: "fleetops",
                table: "notifications",
                columns: new[] { "employee_id", "template_code", "channel", "digest_date" },
                unique: true,
                filter: "digest_date is not null");

            migrationBuilder.CreateIndex(
                name: "IX_alert_covers_from_id_from_date_to_date",
                schema: "fleetops",
                table: "alert_covers",
                columns: new[] { "from_id", "from_date", "to_date" });

            migrationBuilder.CreateIndex(
                name: "IX_alert_covers_to_id_from_date_to_date",
                schema: "fleetops",
                table: "alert_covers",
                columns: new[] { "to_id", "from_date", "to_date" });

            migrationBuilder.CreateIndex(
                name: "IX_push_subscriptions_employee_id_endpoint",
                schema: "fleetops",
                table: "push_subscriptions",
                columns: new[] { "employee_id", "endpoint" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "alert_covers",
                schema: "fleetops");

            migrationBuilder.DropTable(
                name: "push_subscriptions",
                schema: "fleetops");

            migrationBuilder.DropTable(
                name: "sms_daily_usage",
                schema: "fleetops");

            migrationBuilder.DropIndex(
                name: "IX_notifications_employee_id_alert_id_template_code_channel",
                schema: "fleetops",
                table: "notifications");

            migrationBuilder.DropIndex(
                name: "IX_notifications_employee_id_template_code_channel_digest_date",
                schema: "fleetops",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "critical_email",
                schema: "fleetops",
                table: "users");

            migrationBuilder.DropColumn(
                name: "critical_push",
                schema: "fleetops",
                table: "users");

            migrationBuilder.DropColumn(
                name: "digest_email",
                schema: "fleetops",
                table: "users");

            migrationBuilder.DropColumn(
                name: "digest_date",
                schema: "fleetops",
                table: "notifications");
        }
    }
}
