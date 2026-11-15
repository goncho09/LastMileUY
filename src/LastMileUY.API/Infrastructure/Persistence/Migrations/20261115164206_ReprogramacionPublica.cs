using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace LastMileUY.API.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReprogramacionPublica : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "FechaEntregaSolicitada",
                table: "Envios",
                type: "date",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SolicitudesReprogramacion",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OperadorId = table.Column<int>(type: "integer", nullable: false),
                    EnvioId = table.Column<int>(type: "integer", nullable: false),
                    FechaSolicitada = table.Column<DateOnly>(type: "date", nullable: false),
                    FranjaHorariaId = table.Column<int>(type: "integer", nullable: true),
                    Comentario = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    FechaCreacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SolicitudesReprogramacion", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SolicitudesReprogramacion_Envios_EnvioId",
                        column: x => x.EnvioId,
                        principalTable: "Envios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SolicitudesReprogramacion_Operadores_OperadorId",
                        column: x => x.OperadorId,
                        principalTable: "Operadores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SolicitudesReprogramacion_EnvioId_FechaCreacion",
                table: "SolicitudesReprogramacion",
                columns: new[] { "EnvioId", "FechaCreacion" });

            migrationBuilder.CreateIndex(
                name: "IX_SolicitudesReprogramacion_OperadorId",
                table: "SolicitudesReprogramacion",
                column: "OperadorId");

            migrationBuilder.Sql("""
                ALTER TABLE "SolicitudesReprogramacion" ENABLE ROW LEVEL SECURITY;
                ALTER TABLE "SolicitudesReprogramacion" FORCE ROW LEVEL SECURITY;
                CREATE POLICY aislamiento_operador ON "SolicitudesReprogramacion"
                    USING ("OperadorId" = NULLIF(current_setting('app.operador_id', true), '')::int)
                    WITH CHECK ("OperadorId" = NULLIF(current_setting('app.operador_id', true), '')::int);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SolicitudesReprogramacion");

            migrationBuilder.DropColumn(
                name: "FechaEntregaSolicitada",
                table: "Envios");
        }
    }
}
