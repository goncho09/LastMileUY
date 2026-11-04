using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LastMileUY.API.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EventosBultoOutbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CodigoBulto",
                table: "MensajesOutbox",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MotivoNoEntrega",
                table: "MensajesOutbox",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CodigoBulto",
                table: "MensajesOutbox");

            migrationBuilder.DropColumn(
                name: "MotivoNoEntrega",
                table: "MensajesOutbox");
        }
    }
}
