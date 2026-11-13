using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace LastMileUY.API.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AgregarReglasEntregaParcial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ReglaEntregaParcialId",
                table: "Rutas",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReglaEntregaParcialId",
                table: "OperacionesSincronizadas",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ReglasEntregaParcial",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OperadorId = table.Column<int>(type: "integer", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    PermiteEntregaParcial = table.Column<bool>(type: "boolean", nullable: false),
                    DestinoBultosPendientes = table.Column<int>(type: "integer", nullable: false),
                    Motivo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    PublicadaPorId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    FechaPublicacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReglasEntregaParcial", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReglasEntregaParcial_Operadores_OperadorId",
                        column: x => x.OperadorId,
                        principalTable: "Operadores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Rutas_ReglaEntregaParcialId",
                table: "Rutas",
                column: "ReglaEntregaParcialId");

            migrationBuilder.CreateIndex(
                name: "IX_OperacionesSincronizadas_ReglaEntregaParcialId",
                table: "OperacionesSincronizadas",
                column: "ReglaEntregaParcialId");

            migrationBuilder.CreateIndex(
                name: "IX_ReglasEntregaParcial_OperadorId_Version",
                table: "ReglasEntregaParcial",
                columns: new[] { "OperadorId", "Version" },
                unique: true);

            migrationBuilder.Sql("""
                ALTER TABLE "ReglasEntregaParcial" ENABLE ROW LEVEL SECURITY;
                ALTER TABLE "ReglasEntregaParcial" FORCE ROW LEVEL SECURITY;
                CREATE POLICY aislamiento_operador ON "ReglasEntregaParcial"
                    USING ("OperadorId" = NULLIF(current_setting('app.operador_id', true), '')::int)
                    WITH CHECK ("OperadorId" = NULLIF(current_setting('app.operador_id', true), '')::int);

                REVOKE UPDATE, DELETE, TRUNCATE ON TABLE "ReglasEntregaParcial" FROM lastmileuy_app;
                """);

            migrationBuilder.AddForeignKey(
                name: "FK_OperacionesSincronizadas_ReglasEntregaParcial_ReglaEntregaP~",
                table: "OperacionesSincronizadas",
                column: "ReglaEntregaParcialId",
                principalTable: "ReglasEntregaParcial",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Rutas_ReglasEntregaParcial_ReglaEntregaParcialId",
                table: "Rutas",
                column: "ReglaEntregaParcialId",
                principalTable: "ReglasEntregaParcial",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_OperacionesSincronizadas_ReglasEntregaParcial_ReglaEntregaP~",
                table: "OperacionesSincronizadas");

            migrationBuilder.DropForeignKey(
                name: "FK_Rutas_ReglasEntregaParcial_ReglaEntregaParcialId",
                table: "Rutas");

            migrationBuilder.DropTable(
                name: "ReglasEntregaParcial");

            migrationBuilder.DropIndex(
                name: "IX_Rutas_ReglaEntregaParcialId",
                table: "Rutas");

            migrationBuilder.DropIndex(
                name: "IX_OperacionesSincronizadas_ReglaEntregaParcialId",
                table: "OperacionesSincronizadas");

            migrationBuilder.DropColumn(
                name: "ReglaEntregaParcialId",
                table: "Rutas");

            migrationBuilder.DropColumn(
                name: "ReglaEntregaParcialId",
                table: "OperacionesSincronizadas");
        }
    }
}
