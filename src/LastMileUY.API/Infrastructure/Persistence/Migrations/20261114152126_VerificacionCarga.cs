using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace LastMileUY.API.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class VerificacionCarga : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "VerificacionesCarga",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OperadorId = table.Column<int>(type: "integer", nullable: false),
                    RutaId = table.Column<int>(type: "integer", nullable: false),
                    FechaHora = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ResponsableId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    Observaciones = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Completa = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VerificacionesCarga", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VerificacionesCarga_Operadores_OperadorId",
                        column: x => x.OperadorId,
                        principalTable: "Operadores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VerificacionesCarga_Rutas_RutaId",
                        column: x => x.RutaId,
                        principalTable: "Rutas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "VerificacionesCargaBulto",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    VerificacionCargaId = table.Column<int>(type: "integer", nullable: false),
                    BultoId = table.Column<int>(type: "integer", nullable: false),
                    Cargado = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VerificacionesCargaBulto", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VerificacionesCargaBulto_Bultos_BultoId",
                        column: x => x.BultoId,
                        principalTable: "Bultos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VerificacionesCargaBulto_VerificacionesCarga_VerificacionCa~",
                        column: x => x.VerificacionCargaId,
                        principalTable: "VerificacionesCarga",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_VerificacionesCarga_OperadorId",
                table: "VerificacionesCarga",
                column: "OperadorId");

            migrationBuilder.CreateIndex(
                name: "IX_VerificacionesCarga_RutaId_FechaHora",
                table: "VerificacionesCarga",
                columns: new[] { "RutaId", "FechaHora" });

            migrationBuilder.CreateIndex(
                name: "IX_VerificacionesCargaBulto_BultoId",
                table: "VerificacionesCargaBulto",
                column: "BultoId");

            migrationBuilder.CreateIndex(
                name: "IX_VerificacionesCargaBulto_VerificacionCargaId_BultoId",
                table: "VerificacionesCargaBulto",
                columns: new[] { "VerificacionCargaId", "BultoId" },
                unique: true);

            migrationBuilder.Sql("""
                ALTER TABLE "VerificacionesCarga" ENABLE ROW LEVEL SECURITY;
                ALTER TABLE "VerificacionesCarga" FORCE ROW LEVEL SECURITY;
                CREATE POLICY aislamiento_operador ON "VerificacionesCarga"
                    USING ("OperadorId" = NULLIF(current_setting('app.operador_id', true), '')::int)
                    WITH CHECK ("OperadorId" = NULLIF(current_setting('app.operador_id', true), '')::int);

                ALTER TABLE "VerificacionesCargaBulto" ENABLE ROW LEVEL SECURITY;
                ALTER TABLE "VerificacionesCargaBulto" FORCE ROW LEVEL SECURITY;
                CREATE POLICY aislamiento_operador ON "VerificacionesCargaBulto"
                    USING (EXISTS (SELECT 1 FROM "VerificacionesCarga" v WHERE v."Id" = "VerificacionesCargaBulto"."VerificacionCargaId"))
                    WITH CHECK (EXISTS (SELECT 1 FROM "VerificacionesCarga" v WHERE v."Id" = "VerificacionesCargaBulto"."VerificacionCargaId"));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VerificacionesCargaBulto");

            migrationBuilder.DropTable(
                name: "VerificacionesCarga");
        }
    }
}
