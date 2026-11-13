using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LastMileUY.API.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Outbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MensajesOutbox",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Tipo = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    OperadorId = table.Column<int>(type: "integer", nullable: false),
                    ComercioId = table.Column<int>(type: "integer", nullable: false),
                    EnvioId = table.Column<int>(type: "integer", nullable: false),
                    CodigoSeguimiento = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Estado = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    EstadoAnterior = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Origen = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    EsPublico = table.Column<bool>(type: "boolean", nullable: false),
                    OcurridoEn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CorrelacionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    FechaCreacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FechaPublicacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Intentos = table.Column<int>(type: "integer", nullable: false),
                    UltimoError = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MensajesOutbox", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MensajesOutbox_Envios_EnvioId",
                        column: x => x.EnvioId,
                        principalTable: "Envios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MensajesOutbox_EnvioId",
                table: "MensajesOutbox",
                column: "EnvioId");

            migrationBuilder.CreateIndex(
                name: "IX_MensajesOutbox_FechaCreacion",
                table: "MensajesOutbox",
                column: "FechaCreacion",
                filter: "\"FechaPublicacion\" IS NULL");

            migrationBuilder.Sql($"""
                REVOKE SELECT, UPDATE, DELETE ON "MensajesOutbox" FROM {RolAplicacion};

                DO $$
                BEGIN
                    IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = '{RolPublicador}') THEN
                        CREATE ROLE {RolPublicador} LOGIN PASSWORD 'lastmileuy_publicador_dev'
                            NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE;
                    END IF;
                END
                $$;

                GRANT USAGE ON SCHEMA public TO {RolPublicador};
                GRANT SELECT, UPDATE ON "MensajesOutbox" TO {RolPublicador};
                """);
        }

        private const string RolAplicacion = "lastmileuy_app";
        private const string RolPublicador = "lastmileuy_publicador";

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MensajesOutbox");

            migrationBuilder.Sql($"""
                REVOKE USAGE ON SCHEMA public FROM {RolPublicador};
                DROP ROLE IF EXISTS {RolPublicador};
                """);
        }
    }
}
