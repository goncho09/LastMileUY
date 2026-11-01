using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LastMileUY.API.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProtegerMensajesReintentoOutbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                                 REVOKE ALL PRIVILEGES ON TABLE "MensajesReintentoOutbox"
                                 FROM PUBLIC, lastmileuy_app;

                                 GRANT INSERT ON TABLE "MensajesReintentoOutbox"
                                 TO lastmileuy_app;

                                 GRANT SELECT, UPDATE ON TABLE "MensajesReintentoOutbox"
                                 TO lastmileuy_publicador;
                                 """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                                 REVOKE ALL PRIVILEGES ON TABLE "MensajesReintentoOutbox"
                                 FROM lastmileuy_publicador;

                                 REVOKE ALL PRIVILEGES ON TABLE "MensajesReintentoOutbox"
                                 FROM lastmileuy_app;

                                 GRANT SELECT, INSERT, UPDATE, DELETE
                                 ON TABLE "MensajesReintentoOutbox"
                                 TO lastmileuy_app;
                                 """);
        }
    }
}
