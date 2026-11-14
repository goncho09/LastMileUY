using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LastMileUY.API.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CargaDesdeLaApp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Origen",
                table: "VerificacionesCarga",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Deposito");

            migrationBuilder.AddColumn<List<string>>(
                name: "Sobrantes",
                table: "VerificacionesCarga",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Origen",
                table: "VerificacionesCarga");

            migrationBuilder.DropColumn(
                name: "Sobrantes",
                table: "VerificacionesCarga");
        }
    }
}
