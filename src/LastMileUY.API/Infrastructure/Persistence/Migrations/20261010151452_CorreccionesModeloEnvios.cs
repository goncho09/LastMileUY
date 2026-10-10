using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LastMileUY.API.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CorreccionesModeloEnvios : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Envios_OperadorId",
                table: "Envios");

            migrationBuilder.AlterColumn<string>(
                name: "ReferenciaExterna",
                table: "Envios",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "CodigoSeguimiento",
                table: "Envios",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            // xmin es una columna de sistema de PostgreSQL: ya existe en todas
            // las tablas, por eso no se crea (solo se usa para concurrencia).

            migrationBuilder.AddColumn<int>(
                name: "ComercioId",
                table: "Destinatarios",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_Envios_OperadorId_ComercioId_ReferenciaExterna",
                table: "Envios",
                columns: new[] { "OperadorId", "ComercioId", "ReferenciaExterna" },
                unique: true,
                filter: "\"ReferenciaExterna\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Destinatarios_ComercioId",
                table: "Destinatarios",
                column: "ComercioId");

            migrationBuilder.AddForeignKey(
                name: "FK_Destinatarios_Comercios_ComercioId",
                table: "Destinatarios",
                column: "ComercioId",
                principalTable: "Comercios",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Destinatarios_Comercios_ComercioId",
                table: "Destinatarios");

            migrationBuilder.DropIndex(
                name: "IX_Envios_OperadorId_ComercioId_ReferenciaExterna",
                table: "Envios");

            migrationBuilder.DropIndex(
                name: "IX_Destinatarios_ComercioId",
                table: "Destinatarios");

            migrationBuilder.DropColumn(
                name: "ComercioId",
                table: "Destinatarios");

            migrationBuilder.AlterColumn<string>(
                name: "ReferenciaExterna",
                table: "Envios",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "CodigoSeguimiento",
                table: "Envios",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);

            migrationBuilder.CreateIndex(
                name: "IX_Envios_OperadorId",
                table: "Envios",
                column: "OperadorId");
        }
    }
}
