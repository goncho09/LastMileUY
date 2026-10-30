using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LastMileUY.API.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AgregarClaveFirmaAvisos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            
            migrationBuilder.AddColumn<string>(
                name: "ClaveFirma",
                table: "SuscripcionesAvisos",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.Sql("""
                                 CREATE EXTENSION IF NOT EXISTS pgcrypto;
                                 """);
            
            // Generar una clave aleatoria distinta para cada
            // suscripción que ya existe en PostgreSQL.
            migrationBuilder.Sql("""
                                 UPDATE "SuscripcionesAvisos"
                                 SET "ClaveFirma" = encode(gen_random_bytes(32), 'hex')
                                 WHERE "ClaveFirma" IS NULL;
                                 """);

            // Una vez asignadas las claves, el campo es obligatorio.
            migrationBuilder.AlterColumn<string>(
                name: "ClaveFirma",
                table: "SuscripcionesAvisos",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128,
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ClaveFirma",
                table: "SuscripcionesAvisos");
        }
    }
}
