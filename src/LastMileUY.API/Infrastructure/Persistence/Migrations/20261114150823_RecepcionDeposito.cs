using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace LastMileUY.API.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RecepcionDeposito : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RecepcionesDeposito",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OperadorId = table.Column<int>(type: "integer", nullable: false),
                    ComercioId = table.Column<int>(type: "integer", nullable: false),
                    EnvioId = table.Column<int>(type: "integer", nullable: false),
                    FechaHora = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ResponsableId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    Observaciones = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Aceptada = table.Column<bool>(type: "boolean", nullable: false),
                    TieneDiscrepancias = table.Column<bool>(type: "boolean", nullable: false),
                    FechaResolucion = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Resolucion = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ResueltaPor = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecepcionesDeposito", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecepcionesDeposito_Comercios_ComercioId",
                        column: x => x.ComercioId,
                        principalTable: "Comercios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RecepcionesDeposito_Envios_EnvioId",
                        column: x => x.EnvioId,
                        principalTable: "Envios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RecepcionesDeposito_Operadores_OperadorId",
                        column: x => x.OperadorId,
                        principalTable: "Operadores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RecepcionesBulto",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RecepcionDepositoId = table.Column<int>(type: "integer", nullable: false),
                    BultoId = table.Column<int>(type: "integer", nullable: false),
                    Estado = table.Column<int>(type: "integer", nullable: false),
                    PesoDeclaradoKg = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    PesoMedidoKg = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecepcionesBulto", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecepcionesBulto_Bultos_BultoId",
                        column: x => x.BultoId,
                        principalTable: "Bultos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RecepcionesBulto_RecepcionesDeposito_RecepcionDepositoId",
                        column: x => x.RecepcionDepositoId,
                        principalTable: "RecepcionesDeposito",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RecepcionesBulto_BultoId",
                table: "RecepcionesBulto",
                column: "BultoId");

            migrationBuilder.CreateIndex(
                name: "IX_RecepcionesBulto_RecepcionDepositoId_BultoId",
                table: "RecepcionesBulto",
                columns: new[] { "RecepcionDepositoId", "BultoId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RecepcionesDeposito_ComercioId",
                table: "RecepcionesDeposito",
                column: "ComercioId");

            migrationBuilder.CreateIndex(
                name: "IX_RecepcionesDeposito_EnvioId",
                table: "RecepcionesDeposito",
                column: "EnvioId");

            migrationBuilder.CreateIndex(
                name: "IX_RecepcionesDeposito_OperadorId_FechaHora",
                table: "RecepcionesDeposito",
                columns: new[] { "OperadorId", "FechaHora" },
                filter: "\"TieneDiscrepancias\" AND \"FechaResolucion\" IS NULL");

            migrationBuilder.Sql($"""
                ALTER TABLE "RecepcionesDeposito" ENABLE ROW LEVEL SECURITY;
                ALTER TABLE "RecepcionesDeposito" FORCE ROW LEVEL SECURITY;
                CREATE POLICY aislamiento_operador ON "RecepcionesDeposito"
                    USING ("OperadorId" = {OperadorActual})
                    WITH CHECK ("OperadorId" = {OperadorActual});
                CREATE POLICY aislamiento_comercio ON "RecepcionesDeposito" AS RESTRICTIVE
                    USING ({ComercioActual} IS NULL OR "ComercioId" = {ComercioActual})
                    WITH CHECK ({ComercioActual} IS NULL OR "ComercioId" = {ComercioActual});

                ALTER TABLE "RecepcionesBulto" ENABLE ROW LEVEL SECURITY;
                ALTER TABLE "RecepcionesBulto" FORCE ROW LEVEL SECURITY;
                CREATE POLICY aislamiento_operador ON "RecepcionesBulto"
                    USING (EXISTS (SELECT 1 FROM "RecepcionesDeposito" r WHERE r."Id" = "RecepcionesBulto"."RecepcionDepositoId"))
                    WITH CHECK (EXISTS (SELECT 1 FROM "RecepcionesDeposito" r WHERE r."Id" = "RecepcionesBulto"."RecepcionDepositoId"));
                """);
        }

        private const string OperadorActual = "NULLIF(current_setting('app.operador_id', true), '')::int";
        private const string ComercioActual = "NULLIF(current_setting('app.comercio_id', true), '')::int";

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RecepcionesBulto");

            migrationBuilder.DropTable(
                name: "RecepcionesDeposito");
        }
    }
}
