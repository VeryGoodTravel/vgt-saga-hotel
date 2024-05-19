using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace vgt_saga_hotel.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Hotels",
                columns: table => new
                {
                    HotelDbId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Country = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    City = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    AirportCode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    AirportName = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Hotels", x => x.HotelDbId);
                });

            migrationBuilder.CreateTable(
                name: "Rooms",
                columns: table => new
                {
                    RoomDbId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Price = table.Column<int>(type: "integer", nullable: false),
                    Amount = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    MinPeople = table.Column<int>(type: "integer", nullable: false),
                    MaxPeople = table.Column<int>(type: "integer", nullable: false),
                    MinAdults = table.Column<int>(type: "integer", nullable: false),
                    MaxAdults = table.Column<int>(type: "integer", nullable: false),
                    MinChildren = table.Column<int>(type: "integer", nullable: false),
                    MaxChildren = table.Column<int>(type: "integer", nullable: false),
                    Max10yo = table.Column<int>(type: "integer", nullable: false),
                    MaxLesserChildren = table.Column<int>(type: "integer", nullable: false),
                    HotelDbId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Rooms", x => x.RoomDbId);
                    table.ForeignKey(
                        name: "FK_Rooms_Hotels_HotelDbId",
                        column: x => x.HotelDbId,
                        principalTable: "Hotels",
                        principalColumn: "HotelDbId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Bookings",
                columns: table => new
                {
                    BookingId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    HotelDbId = table.Column<int>(type: "integer", nullable: false),
                    RoomDbId = table.Column<int>(type: "integer", nullable: false),
                    TransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Temporary = table.Column<int>(type: "integer", nullable: false),
                    TemporaryDt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    BookFrom = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    BookTo = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Bookings", x => x.BookingId);
                    table.ForeignKey(
                        name: "FK_Bookings_Hotels_HotelDbId",
                        column: x => x.HotelDbId,
                        principalTable: "Hotels",
                        principalColumn: "HotelDbId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Bookings_Rooms_RoomDbId",
                        column: x => x.RoomDbId,
                        principalTable: "Rooms",
                        principalColumn: "RoomDbId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_HotelDbId",
                table: "Bookings",
                column: "HotelDbId");

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_RoomDbId",
                table: "Bookings",
                column: "RoomDbId");

            migrationBuilder.CreateIndex(
                name: "IX_Rooms_HotelDbId",
                table: "Rooms",
                column: "HotelDbId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Bookings");

            migrationBuilder.DropTable(
                name: "Rooms");

            migrationBuilder.DropTable(
                name: "Hotels");
        }
    }
}
