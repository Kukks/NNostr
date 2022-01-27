using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Relay.Migrations
{
    public partial class Balances : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Balances",
                columns: table => new
                {
                    PublicKey = table.Column<string>(type: "text", nullable: false),
                    CurrentBalance = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Balances", x => x.PublicKey);
                });

            migrationBuilder.CreateTable(
                name: "BalanceTopups",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    BalanceId = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BalanceTopups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BalanceTopups_Balances_BalanceId",
                        column: x => x.BalanceId,
                        principalTable: "Balances",
                        principalColumn: "PublicKey",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BalanceTransactions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    BalanceId = table.Column<string>(type: "text", nullable: false),
                    BalanceTopupId = table.Column<string>(type: "text", nullable: true),
                    EventId = table.Column<string>(type: "text", nullable: true),
                    Value = table.Column<long>(type: "bigint", nullable: false),
                    Timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BalanceTransactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BalanceTransactions_Balances_BalanceId",
                        column: x => x.BalanceId,
                        principalTable: "Balances",
                        principalColumn: "PublicKey",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BalanceTransactions_BalanceTopups_BalanceTopupId",
                        column: x => x.BalanceTopupId,
                        principalTable: "BalanceTopups",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_BalanceTransactions_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_BalanceTopups_BalanceId",
                table: "BalanceTopups",
                column: "BalanceId");

            migrationBuilder.CreateIndex(
                name: "IX_BalanceTransactions_BalanceId",
                table: "BalanceTransactions",
                column: "BalanceId");

            migrationBuilder.CreateIndex(
                name: "IX_BalanceTransactions_BalanceTopupId",
                table: "BalanceTransactions",
                column: "BalanceTopupId");

            migrationBuilder.CreateIndex(
                name: "IX_BalanceTransactions_EventId",
                table: "BalanceTransactions",
                column: "EventId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BalanceTransactions");

            migrationBuilder.DropTable(
                name: "BalanceTopups");

            migrationBuilder.DropTable(
                name: "Balances");
        }
    }
}
