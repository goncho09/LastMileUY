using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace LastMileUY.API.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class IndicadoresDiarios : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "IndicadoresDiarios",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OperadorId = table.Column<int>(type: "integer", nullable: false),
                    Dia = table.Column<DateOnly>(type: "date", nullable: false),
                    EnviosCreados = table.Column<int>(type: "integer", nullable: false),
                    Entregados = table.Column<int>(type: "integer", nullable: false),
                    EntregadosATiempo = table.Column<int>(type: "integer", nullable: false),
                    NoEntregados = table.Column<int>(type: "integer", nullable: false),
                    IntentosFallidos = table.Column<int>(type: "integer", nullable: false),
                    Importe = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    Cerrado = table.Column<bool>(type: "boolean", nullable: false),
                    CalculadoEn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IndicadoresDiarios", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IndicadoresDiarios_Operadores_OperadorId",
                        column: x => x.OperadorId,
                        principalTable: "Operadores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IndicadoresDiarios_OperadorId_Dia",
                table: "IndicadoresDiarios",
                columns: new[] { "OperadorId", "Dia" },
                unique: true);
            migrationBuilder.Sql("""
                ALTER TABLE "IndicadoresDiarios" ENABLE ROW LEVEL SECURITY;
                ALTER TABLE "IndicadoresDiarios" FORCE ROW LEVEL SECURITY;
                CREATE POLICY aislamiento_operador ON "IndicadoresDiarios"
                    USING ("OperadorId" = NULLIF(current_setting('app.operador_id', true), '')::int)
                    WITH CHECK ("OperadorId" = NULLIF(current_setting('app.operador_id', true), '')::int);
                """);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IndicadoresDiarios");
        }
    }
}
