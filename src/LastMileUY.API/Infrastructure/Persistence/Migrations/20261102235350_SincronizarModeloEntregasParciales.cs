using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace LastMileUY.API.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SincronizarModeloEntregasParciales : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ResultadosBultosIntentos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    IntentoEntregaId = table.Column<int>(type: "integer", nullable: false),
                    BultoId = table.Column<int>(type: "integer", nullable: false),
                    Entregado = table.Column<bool>(type: "boolean", nullable: false),
                    MotivoNoEntregaId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResultadosBultosIntentos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ResultadosBultosIntentos_Bultos_BultoId",
                        column: x => x.BultoId,
                        principalTable: "Bultos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ResultadosBultosIntentos_IntentosEntrega_IntentoEntregaId",
                        column: x => x.IntentoEntregaId,
                        principalTable: "IntentosEntrega",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ResultadosBultosIntentos_BultoId",
                table: "ResultadosBultosIntentos",
                column: "BultoId");

            migrationBuilder.CreateIndex(
                name: "IX_ResultadosBultosIntentos_IntentoEntregaId_BultoId",
                table: "ResultadosBultosIntentos",
                columns: new[] { "IntentoEntregaId", "BultoId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ResultadosBultosIntentos");
        }
    }
}
