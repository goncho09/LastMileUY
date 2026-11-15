using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LastMileUY.API.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Rendiciones : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<List<string>>(
                name: "BultosDeclarados",
                table: "Rendiciones",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'");

            migrationBuilder.AddColumn<List<string>>(
                name: "BultosRecibidos",
                table: "Rendiciones",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'");

            migrationBuilder.AddColumn<DateTime>(
                name: "ConfirmadaEn",
                table: "Rendiciones",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ConfirmadaPor",
                table: "Rendiciones",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeclaradaEn",
                table: "Rendiciones",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<List<string>>(
                name: "Faltantes",
                table: "Rendiciones",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'");

            migrationBuilder.AddColumn<string>(
                name: "ObservacionesRepartidor",
                table: "Rendiciones",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<List<string>>(
                name: "Sobrantes",
                table: "Rendiciones",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BultosDeclarados",
                table: "Rendiciones");

            migrationBuilder.DropColumn(
                name: "BultosRecibidos",
                table: "Rendiciones");

            migrationBuilder.DropColumn(
                name: "ConfirmadaEn",
                table: "Rendiciones");

            migrationBuilder.DropColumn(
                name: "ConfirmadaPor",
                table: "Rendiciones");

            migrationBuilder.DropColumn(
                name: "DeclaradaEn",
                table: "Rendiciones");

            migrationBuilder.DropColumn(
                name: "Faltantes",
                table: "Rendiciones");

            migrationBuilder.DropColumn(
                name: "ObservacionesRepartidor",
                table: "Rendiciones");

            migrationBuilder.DropColumn(
                name: "Sobrantes",
                table: "Rendiciones");
        }
    }
}
