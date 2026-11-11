using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LastMileUY.API.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ValidarVinculosActivosRls : Migration
    {
        /// <inheritdoc />

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
        ALTER POLICY aislamiento_comercio
        ON public."Destinatarios"
        USING (
            CASE
                WHEN NULLIF(
                    current_setting('app.comercio_id', true), ''
                )::integer IS NOT NULL
                THEN
                    "ComercioId" = NULLIF(
                        current_setting('app.comercio_id', true), ''
                    )::integer
                ELSE
                    EXISTS (
                        SELECT 1
                        FROM public."OperadoresComercios" oc
                        WHERE oc."ComercioId" =
                              "Destinatarios"."ComercioId"
                          AND oc."OperadorId" = NULLIF(
                              current_setting('app.operador_id', true), ''
                          )::integer
                          AND oc."Activo" = TRUE
                    )
            END
        )
        WITH CHECK (
            CASE
                WHEN NULLIF(
                    current_setting('app.comercio_id', true), ''
                )::integer IS NOT NULL
                THEN
                    "ComercioId" = NULLIF(
                        current_setting('app.comercio_id', true), ''
                    )::integer
                ELSE
                    EXISTS (
                        SELECT 1
                        FROM public."OperadoresComercios" oc
                        WHERE oc."ComercioId" =
                              "Destinatarios"."ComercioId"
                          AND oc."OperadorId" = NULLIF(
                              current_setting('app.operador_id', true), ''
                          )::integer
                    )
                    AND EXISTS (
                        SELECT 1
                        FROM public."OperadoresComercios" oc
                        WHERE oc."ComercioId" =
                              "Destinatarios"."ComercioId"
                          AND oc."OperadorId" = NULLIF(
                              current_setting('app.operador_id', true), ''
                          )::integer
                          AND oc."Activo" = TRUE
                    )
            END
        );
        """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
        ALTER POLICY aislamiento_comercio
        ON public."Destinatarios"
        USING (
            CASE
                WHEN NULLIF(
                    current_setting('app.comercio_id', true), ''
                )::integer IS NOT NULL
                THEN
                    "ComercioId" = NULLIF(
                        current_setting('app.comercio_id', true), ''
                    )::integer
                ELSE
                    EXISTS (
                        SELECT 1
                        FROM public."OperadoresComercios" oc
                        WHERE oc."ComercioId" =
                              "Destinatarios"."ComercioId"
                          AND oc."OperadorId" = NULLIF(
                              current_setting('app.operador_id', true), ''
                          )::integer
                    )
            END
        )
        WITH CHECK (
            CASE
                WHEN NULLIF(
                    current_setting('app.comercio_id', true), ''
                )::integer IS NOT NULL
                THEN
                    "ComercioId" = NULLIF(
                        current_setting('app.comercio_id', true), ''
                    )::integer
                ELSE
                    EXISTS (
                        SELECT 1
                        FROM public."OperadoresComercios" oc
                        WHERE oc."ComercioId" =
                              "Destinatarios"."ComercioId"
                          AND oc."OperadorId" = NULLIF(
                              current_setting('app.operador_id', true), ''
                          )::integer
                    )
            END
        );
        """);
        }

    }
}
