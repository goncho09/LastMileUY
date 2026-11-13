using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LastMileUY.API.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FuncionalidadesPorOperador : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FuncionalidadesOperador",
                columns: table => new
                {
                    OperadorId = table.Column<int>(type: "integer", nullable: false),
                    Clave = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Activa = table.Column<bool>(type: "boolean", nullable: false),
                    FechaModificacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ModificadaPor = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FuncionalidadesOperador", x => new { x.OperadorId, x.Clave });
                    table.ForeignKey(
                        name: "FK_FuncionalidadesOperador_Operadores_OperadorId",
                        column: x => x.OperadorId,
                        principalTable: "Operadores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FuncionalidadesOperador");
        }
    }
}
