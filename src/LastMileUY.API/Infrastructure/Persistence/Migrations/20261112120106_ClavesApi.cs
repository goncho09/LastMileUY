using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace LastMileUY.API.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ClavesApi : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ClavesApi",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OperadorId = table.Column<int>(type: "integer", nullable: false),
                    ComercioId = table.Column<int>(type: "integer", nullable: false),
                    Nombre = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Prefijo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    FechaCreacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreadaPorId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    UltimoUso = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FechaRevocacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClavesApi", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClavesApi_Comercios_ComercioId",
                        column: x => x.ComercioId,
                        principalTable: "Comercios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ClavesApi_Operadores_OperadorId",
                        column: x => x.OperadorId,
                        principalTable: "Operadores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClavesApi_ComercioId",
                table: "ClavesApi",
                column: "ComercioId");

            migrationBuilder.CreateIndex(
                name: "IX_ClavesApi_Hash",
                table: "ClavesApi",
                column: "Hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClavesApi_OperadorId_ComercioId",
                table: "ClavesApi",
                columns: new[] { "OperadorId", "ComercioId" });

            migrationBuilder.Sql($"""
                ALTER TABLE "ClavesApi" ENABLE ROW LEVEL SECURITY;
                ALTER TABLE "ClavesApi" FORCE ROW LEVEL SECURITY;
                CREATE POLICY aislamiento_operador ON "ClavesApi"
                    USING ("OperadorId" = {OperadorActual})
                    WITH CHECK ("OperadorId" = {OperadorActual});
                CREATE POLICY aislamiento_comercio ON "ClavesApi" AS RESTRICTIVE
                    USING ({ComercioActual} IS NULL OR "ComercioId" = {ComercioActual})
                    WITH CHECK ({ComercioActual} IS NULL OR "ComercioId" = {ComercioActual});
                """);

            migrationBuilder.Sql($"""
                CREATE POLICY clave_api_por_hash ON "ClavesApi"
                    FOR SELECT
                    USING ("Hash" = {HashActual});
                CREATE POLICY clave_api_ultimo_uso ON "ClavesApi"
                    FOR UPDATE
                    USING ("Hash" = {HashActual})
                    WITH CHECK ("Hash" = {HashActual});
                """);
        }

        private const string OperadorActual = "NULLIF(current_setting('app.operador_id', true), '')::int";
        private const string ComercioActual = "NULLIF(current_setting('app.comercio_id', true), '')::int";
        private const string HashActual = "NULLIF(current_setting('app.hash_clave_api', true), '')";

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClavesApi");
        }
    }
}
