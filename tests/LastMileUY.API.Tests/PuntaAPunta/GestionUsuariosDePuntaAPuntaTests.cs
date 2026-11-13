using System.Net;
using System.Net.Http.Json;
using LastMileUY.API.Modules.Rutas.Domain;
using LastMileUY.Contracts.Envios;
using LastMileUY.Contracts.Rutas;
using LastMileUY.Contracts.Usuarios;
using Npgsql;

namespace LastMileUY.API.Tests.PuntaAPunta;

[Collection(ColeccionApiDePuntaAPunta.Nombre)]
public class GestionUsuariosDePuntaAPuntaTests(
    ApiDePuntaAPunta api)
{
    private const string Admin = "admin@prueba.test";

    private static string EmailUnico() =>
        $"usuario-{Guid.NewGuid():N}@prueba.test";

    private static CrearUsuarioRequest CrearRequest(
        string email,
        string rol,
        int? operadorId = null,
        int? comercioId = null)
    {
        return new CrearUsuarioRequest(
            email,
            "Usuario",
            "Prueba",
            ApiDePuntaAPunta.Clave,
            rol,
            operadorId,
            comercioId
        );
    }

    private Task<long> ContarUsuariosAsync(string email) =>
        api.ConsultarCantidadAsync(
            """
            SELECT COUNT(*)
            FROM "AspNetUsers"
            WHERE "Email" = @email
            """,
            new NpgsqlParameter("email", email));

    [Fact]
    public async Task Operador_puede_crear_despachador()
    {
        using var cliente = await api.IniciarSesionAsync(
            ApiDePuntaAPunta.Operador1);

        var email = EmailUnico();

        using var respuesta = await cliente.PostAsJsonAsync(
            "/api/usuarios",
            CrearRequest(email, "Despachador"));

        var contenido = await respuesta.Content.ReadAsStringAsync();

        Assert.True(
            respuesta.StatusCode == HttpStatusCode.Created,
            $"HTTP {(int)respuesta.StatusCode}: {contenido}");

        var cantidad = await api.ConsultarCantidadAsync(
            """
            SELECT COUNT(*)
            FROM "AspNetUsers" u
            JOIN "AspNetUserRoles" ur ON ur."UserId" = u."Id"
            JOIN "AspNetRoles" r ON r."Id" = ur."RoleId"
            WHERE u."Email" = @email
              AND u."OperadorId" = @operador
              AND r."Name" = 'Despachador'
            """,
            new NpgsqlParameter("email", email),
            new NpgsqlParameter("operador", api.IdOperador1));

        Assert.Equal(1, cantidad);
    }

    [Fact]
    public async Task Operador_puede_crear_operario_de_deposito()
    {
        using var cliente = await api.IniciarSesionAsync(
            ApiDePuntaAPunta.Operador1);

        var email = EmailUnico();

        using var respuesta = await cliente.PostAsJsonAsync(
            "/api/usuarios",
            CrearRequest(email, "OperarioDeposito"));

        Assert.Equal(
            HttpStatusCode.Created,
            respuesta.StatusCode);

        var cantidad = await api.ConsultarCantidadAsync(
            """
            SELECT COUNT(*)
            FROM "AspNetUsers" u
            JOIN "AspNetUserRoles" ur ON ur."UserId" = u."Id"
            JOIN "AspNetRoles" r ON r."Id" = ur."RoleId"
            WHERE u."Email" = @email
              AND u."OperadorId" = @operador
              AND r."Name" = 'OperarioDeposito'
            """,
            new NpgsqlParameter("email", email),
            new NpgsqlParameter("operador", api.IdOperador1));

        Assert.Equal(1, cantidad);
    }

    [Fact]
    public async Task Operador_puede_crear_repartidor_con_ficha()
    {
        using var cliente = await api.IniciarSesionAsync(
            ApiDePuntaAPunta.Operador1);

        var email = EmailUnico();

        using var respuesta = await cliente.PostAsJsonAsync(
            "/api/usuarios",
            CrearRequest(email, "Repartidor"));

        var contenido = await respuesta.Content.ReadAsStringAsync();

        Assert.True(
            respuesta.StatusCode == HttpStatusCode.Created,
            $"HTTP {(int)respuesta.StatusCode}: {contenido}");

        var cantidad = await api.ConsultarCantidadAsync(
            """
            SELECT COUNT(*)
            FROM "Repartidores" rep
            JOIN "AspNetUsers" u ON u."Id" = rep."UsuarioId"
            JOIN "AspNetUserRoles" ur ON ur."UserId" = u."Id"
            JOIN "AspNetRoles" r ON r."Id" = ur."RoleId"
            WHERE u."Email" = @email
              AND u."OperadorId" = @operador
              AND rep."OperadorId" = @operador
              AND r."Name" = 'Repartidor'
            """,
            new NpgsqlParameter("email", email),
            new NpgsqlParameter("operador", api.IdOperador1));

        Assert.Equal(1, cantidad);
    }

    [Fact]
    public async Task Operador_no_puede_indicar_otro_operador()
    {
        using var cliente = await api.IniciarSesionAsync(
            ApiDePuntaAPunta.Operador1);

        var email = EmailUnico();

        using var respuesta = await cliente.PostAsJsonAsync(
            "/api/usuarios",
            CrearRequest(
                email,
                "Despachador",
                api.IdOperador2));

        Assert.Equal(
            HttpStatusCode.BadRequest,
            respuesta.StatusCode);

        Assert.Equal(0, await ContarUsuariosAsync(email));
    }

    [Fact]
    public async Task Operador_no_puede_indicar_comercio()
    {
        using var cliente = await api.IniciarSesionAsync(
            ApiDePuntaAPunta.Operador1);

        var email = EmailUnico();

        using var respuesta = await cliente.PostAsJsonAsync(
            "/api/usuarios",
            CrearRequest(
                email,
                "Despachador",
                comercioId: 1));

        Assert.Equal(
            HttpStatusCode.BadRequest,
            respuesta.StatusCode);

        Assert.Equal(0, await ContarUsuariosAsync(email));
    }

    [Theory]
    [InlineData("Administrador")]
    [InlineData("Operador")]
    [InlineData("Comercio")]
    public async Task Operador_no_puede_crear_roles_prohibidos(
        string rol)
    {
        using var cliente = await api.IniciarSesionAsync(
            ApiDePuntaAPunta.Operador1);

        var email = EmailUnico();

        using var respuesta = await cliente.PostAsJsonAsync(
            "/api/usuarios",
            CrearRequest(email, rol));

        Assert.Equal(
            HttpStatusCode.BadRequest,
            respuesta.StatusCode);

        Assert.Equal(0, await ContarUsuariosAsync(email));
    }

    [Theory]
    [InlineData(Admin)]
    [InlineData(ApiDePuntaAPunta.Despachador1)]
    [InlineData(ApiDePuntaAPunta.Comercio1)]
    [InlineData(ApiDePuntaAPunta.Repartidor1)]
    public async Task Usuario_sin_rol_operador_no_puede_crear(
        string emailSesion)
    {
        using var cliente = await api.IniciarSesionAsync(
            emailSesion);

        var email = EmailUnico();

        using var respuesta = await cliente.PostAsJsonAsync(
            "/api/usuarios",
            CrearRequest(email, "Despachador"));

        Assert.Equal(
            HttpStatusCode.Forbidden,
            respuesta.StatusCode);

        Assert.Equal(0, await ContarUsuariosAsync(email));
    }

    [Fact]
    public async Task Sin_sesion_no_puede_crear_usuario()
    {
        using var cliente = api.ClienteSinSesion();
        var email = EmailUnico();

        using var respuesta = await cliente.PostAsJsonAsync(
            "/api/usuarios",
            CrearRequest(email, "Despachador"));

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            respuesta.StatusCode);

        Assert.Equal(0, await ContarUsuariosAsync(email));
    }

    [Fact]
    public async Task Email_duplicado_es_rechazado()
    {
        using var cliente = await api.IniciarSesionAsync(
            ApiDePuntaAPunta.Operador1);

        var email = EmailUnico();
        var request = CrearRequest(email, "Despachador");

        using var primera = await cliente.PostAsJsonAsync(
            "/api/usuarios", request);

        Assert.Equal(
            HttpStatusCode.Created,
            primera.StatusCode);

        using var segunda = await cliente.PostAsJsonAsync(
            "/api/usuarios", request);

        Assert.Equal(
            HttpStatusCode.BadRequest,
            segunda.StatusCode);

        Assert.Equal(1, await ContarUsuariosAsync(email));
    }

    [Fact]
    public async Task Operador_solo_lista_su_personal()
    {
        using var cliente1 = await api.IniciarSesionAsync(
            ApiDePuntaAPunta.Operador1);

        using var cliente2 = await api.IniciarSesionAsync(
            ApiDePuntaAPunta.Operador2);

        var email1 = EmailUnico();
        var email2 = EmailUnico();

        using var alta1 = await cliente1.PostAsJsonAsync(
            "/api/usuarios",
            CrearRequest(email1, "Despachador"));

        using var alta2 = await cliente2.PostAsJsonAsync(
            "/api/usuarios",
            CrearRequest(email2, "Despachador"));

        Assert.Equal(HttpStatusCode.Created, alta1.StatusCode);
        Assert.Equal(HttpStatusCode.Created, alta2.StatusCode);

        var usuarios1 = await cliente1.GetFromJsonAsync<
            List<UsuarioDto>>("/api/usuarios");

        var usuarios2 = await cliente2.GetFromJsonAsync<
            List<UsuarioDto>>("/api/usuarios");

        Assert.NotNull(usuarios1);
        Assert.NotNull(usuarios2);

        Assert.Contains(usuarios1, u => u.Email == email1);
        Assert.DoesNotContain(usuarios1, u => u.Email == email2);

        Assert.Contains(usuarios2, u => u.Email == email2);
        Assert.DoesNotContain(usuarios2, u => u.Email == email1);

        Assert.All(usuarios1,
            u => Assert.Equal(api.IdOperador1, u.OperadorId));

        Assert.All(usuarios2,
            u => Assert.Equal(api.IdOperador2, u.OperadorId));
    }

    [Fact]
    public async Task Administrador_no_puede_listar_personal()
    {
        using var cliente = await api.IniciarSesionAsync(Admin);

        using var respuesta = await cliente.GetAsync(
            "/api/usuarios");

        Assert.Equal(
            HttpStatusCode.Forbidden,
            respuesta.StatusCode);
    }

    [Fact]
    public async Task Despachador_no_puede_listar_personal()
    {
        using var cliente = await api.IniciarSesionAsync(
            ApiDePuntaAPunta.Despachador1);

        using var respuesta = await cliente.GetAsync(
            "/api/usuarios");

        Assert.Equal(
            HttpStatusCode.Forbidden,
            respuesta.StatusCode);
    }


    [Fact]
    public async Task Operador_puede_desactivar_y_reactivar_usuario()
    {
        using var cliente = await api.IniciarSesionAsync(
            ApiDePuntaAPunta.Operador1);

        var email = EmailUnico();

        using var alta = await cliente.PostAsJsonAsync(
            "/api/usuarios",
            CrearRequest(email, "Despachador"));

        Assert.Equal(HttpStatusCode.Created, alta.StatusCode);

        var usuario = await alta.Content.ReadFromJsonAsync<UsuarioDto>();
        Assert.NotNull(usuario);

        using var desactivar = await cliente.PatchAsJsonAsync(
            $"/api/usuarios/{usuario.Id}/estado",
            new CambiarEstadoUsuarioRequest(false));

        Assert.Equal(HttpStatusCode.NoContent, desactivar.StatusCode);

        var cantidadInactivos = await api.ConsultarCantidadAsync(
            """
        SELECT COUNT(*)
        FROM "AspNetUsers"
        WHERE "Id" = @id AND "Activo" = false
        """,
            new NpgsqlParameter("id", usuario.Id));

        Assert.Equal(1, cantidadInactivos);

        using var reactivar = await cliente.PatchAsJsonAsync(
            $"/api/usuarios/{usuario.Id}/estado",
            new CambiarEstadoUsuarioRequest(true));

        Assert.Equal(HttpStatusCode.NoContent, reactivar.StatusCode);
    }

    [Fact]
    public async Task Operador_no_puede_desactivar_usuario_ajeno()
    {
        using var cliente1 = await api.IniciarSesionAsync(
            ApiDePuntaAPunta.Operador1);

        using var cliente2 = await api.IniciarSesionAsync(
            ApiDePuntaAPunta.Operador2);

        using var alta = await cliente2.PostAsJsonAsync(
            "/api/usuarios",
            CrearRequest(EmailUnico(), "Despachador"));

        Assert.Equal(HttpStatusCode.Created, alta.StatusCode);

        var usuario = await alta.Content.ReadFromJsonAsync<UsuarioDto>();
        Assert.NotNull(usuario);

        using var respuesta = await cliente1.PatchAsJsonAsync(
            $"/api/usuarios/{usuario.Id}/estado",
            new CambiarEstadoUsuarioRequest(false));

        Assert.True(
            respuesta.StatusCode is HttpStatusCode.BadRequest
                or HttpStatusCode.NotFound);

        var cantidadActivos = await api.ConsultarCantidadAsync(
            """
        SELECT COUNT(*)
        FROM "AspNetUsers"
        WHERE "Id" = @id AND "Activo" = true
        """,
            new NpgsqlParameter("id", usuario.Id));

        Assert.Equal(1, cantidadActivos);
    }

    [Fact]
    public async Task Desactivar_repartidor_actualiza_su_ficha()
    {
        using var cliente = await api.IniciarSesionAsync(
            ApiDePuntaAPunta.Operador1);

        using var alta = await cliente.PostAsJsonAsync(
            "/api/usuarios",
            CrearRequest(EmailUnico(), "Repartidor"));

        Assert.Equal(HttpStatusCode.Created, alta.StatusCode);

        var usuario = await alta.Content.ReadFromJsonAsync<UsuarioDto>();
        Assert.NotNull(usuario);

        using var respuesta = await cliente.PatchAsJsonAsync(
            $"/api/usuarios/{usuario.Id}/estado",
            new CambiarEstadoUsuarioRequest(false));

        Assert.Equal(HttpStatusCode.NoContent, respuesta.StatusCode);

        var cantidad = await api.ConsultarCantidadAsync(
            """
        SELECT COUNT(*)
        FROM "Repartidores" r
        JOIN "AspNetUsers" u ON u."Id" = r."UsuarioId"
        WHERE u."Id" = @id
          AND u."Activo" = false
          AND r."Activo" = false
        """,
            new NpgsqlParameter("id", usuario.Id));

        Assert.Equal(1, cantidad);
    }

    [Fact]
    public async Task Administrador_no_puede_cambiar_estado_del_personal()
    {
        using var operador = await api.IniciarSesionAsync(
            ApiDePuntaAPunta.Operador1);

        using var alta = await operador.PostAsJsonAsync(
            "/api/usuarios",
            CrearRequest(EmailUnico(), "Despachador"));

        Assert.Equal(HttpStatusCode.Created, alta.StatusCode);

        var usuario = await alta.Content.ReadFromJsonAsync<UsuarioDto>();
        Assert.NotNull(usuario);

        using var administrador = await api.IniciarSesionAsync(
            "admin@prueba.test");

        using var respuesta = await administrador.PatchAsJsonAsync(
            $"/api/usuarios/{usuario.Id}/estado",
            new CambiarEstadoUsuarioRequest(false));

        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
    }


    [Fact]
    public async Task No_permite_desactivar_repartidor_con_ruta_abierta()
    {
        using var cliente = await api.IniciarSesionAsync(
            ApiDePuntaAPunta.Operador1);

        using var alta = await cliente.PostAsJsonAsync(
            "/api/usuarios",
            CrearRequest(EmailUnico(), "Repartidor"));

        Assert.Equal(HttpStatusCode.Created, alta.StatusCode);

        var usuario = await alta.Content.ReadFromJsonAsync<UsuarioDto>();
        Assert.NotNull(usuario);

        var nombreRuta = $"Prueba bloqueo {Guid.NewGuid():N}";

        var estadoRuta = (int)EstadoRuta.Planificada;

        Assert.Contains(EstadoRuta.Planificada, Ruta.EstadosAbiertos);

        await api.EjecutarAsync($"""
        INSERT INTO "Rutas"
            ("OperadorId", "RepartidorId", "VehiculoId",
             "Fecha", "Nombre", "Estado", "FechaCreacion")
        SELECT
            r."OperadorId",
            r."Id",
            v."Id",
            CURRENT_DATE,
            '{nombreRuta}',
            {estadoRuta},
            NOW()
        FROM "Repartidores" r
        CROSS JOIN LATERAL (
            SELECT "Id"
            FROM "Vehiculos"
            WHERE "OperadorId" = r."OperadorId"
              AND "Activo" = true
            ORDER BY "Id"
            LIMIT 1
        ) v
        WHERE r."UsuarioId" = '{usuario.Id}'
          AND r."OperadorId" = {api.IdOperador1}
        """);

        var rutasCreadas = await api.ConsultarCantidadAsync(
            """
        SELECT COUNT(*)
        FROM "Rutas" r
        JOIN "Repartidores" rep
          ON rep."Id" = r."RepartidorId"
        WHERE rep."UsuarioId" = @id
          AND r."Estado" = @estado
        """,
            new NpgsqlParameter("id", usuario.Id),
            new NpgsqlParameter("estado", estadoRuta));

        Assert.Equal(1, rutasCreadas);

        try
        {
            using var respuesta = await cliente.PatchAsJsonAsync(
                $"/api/usuarios/{usuario.Id}/estado",
                new CambiarEstadoUsuarioRequest(false));

            var contenido = await respuesta.Content.ReadAsStringAsync();

            Assert.True(
                respuesta.StatusCode == HttpStatusCode.BadRequest,
                $"HTTP {(int)respuesta.StatusCode}: {contenido}");

            Assert.Contains("hojas de ruta abiertas", contenido);

            var cantidadActivos = await api.ConsultarCantidadAsync(
                """
            SELECT COUNT(*)
            FROM "AspNetUsers" u
            JOIN "Repartidores" r
              ON r."UsuarioId" = u."Id"
            WHERE u."Id" = @id
              AND u."Activo" = true
              AND r."Activo" = true
            """,
                new NpgsqlParameter("id", usuario.Id));

            Assert.Equal(1, cantidadActivos);
        }
        finally
        {
            await api.EjecutarAsync($"""
            DELETE FROM "Rutas"
            WHERE "Nombre" = '{nombreRuta}'
              AND "OperadorId" = {api.IdOperador1}
            """);
        }
    }


    [Fact]
    public async Task Usuario_desactivado_pierde_acceso_con_su_cookie_anterior()
    {
        using var operador = await api.IniciarSesionAsync(
            ApiDePuntaAPunta.Operador1);

        var email = EmailUnico();

        using var alta = await operador.PostAsJsonAsync(
            "/api/usuarios",
            CrearRequest(email, "Despachador"));

        Assert.Equal(HttpStatusCode.Created, alta.StatusCode);

        var usuario = await alta.Content.ReadFromJsonAsync<UsuarioDto>();
        Assert.NotNull(usuario);

        using var despachador = await api.IniciarSesionAsync(email);

        using (var antes = await despachador.GetAsync("/api/rutas/opciones"))
        {
            Assert.Equal(HttpStatusCode.OK, antes.StatusCode);
        }

        using var desactivar = await operador.PatchAsJsonAsync(
            $"/api/usuarios/{usuario.Id}/estado",
            new CambiarEstadoUsuarioRequest(false));

        Assert.Equal(HttpStatusCode.NoContent, desactivar.StatusCode);

        using var despues = await despachador.GetAsync(
            "/api/rutas/opciones");

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            despues.StatusCode);
    }

    [Fact]
    public async Task Crear_ruta_y_desactivar_repartidor_concurrentemente_no_deja_inconsistencias()
    {
        using var clienteRutas =
            await api.IniciarSesionAsync(ApiDePuntaAPunta.Operador1);

        using var clienteUsuarios =
            await api.IniciarSesionAsync(ApiDePuntaAPunta.Operador1);

        using var alta = await clienteUsuarios.PostAsJsonAsync(
            "/api/usuarios",
            CrearRequest(EmailUnico(), "Repartidor"));

        Assert.Equal(HttpStatusCode.Created, alta.StatusCode);

        var usuario = await alta.Content.ReadFromJsonAsync<UsuarioDto>();
        Assert.NotNull(usuario);

        var repartidorId = await api.ConsultarCantidadAsync(
            """
        SELECT "Id"
        FROM "Repartidores"
        WHERE "UsuarioId" = @usuarioId
        """,
            new NpgsqlParameter("usuarioId", usuario.Id));

        Assert.True(repartidorId > 0);

        using var opcionesResponse =
            await clienteRutas.GetAsync("/api/rutas/opciones");

        Assert.Equal(HttpStatusCode.OK, opcionesResponse.StatusCode);

        var opciones =
            await opcionesResponse.Content.ReadFromJsonAsync<OpcionesRutaDto>();

        Assert.NotNull(opciones);

        var vehiculo = opciones.Vehiculos.First(v => v.Tipo == "Furgoneta");

        var (destinatarioId, direccionId) =
            await api.DestinatarioDeAsync(api.IdComercio1);

        var solicitudEnvio = new CrearEnvioRequest
        {
            ComercioId = api.IdComercio1,
            DestinatarioId = destinatarioId,
            DireccionId = direccionId,
            ReferenciaExterna = $"CONC-{Guid.NewGuid():N}",
            Bultos =
            [
                new CrearBultoRequest
                {
                    PesoKg = 2,
                    LargoCm = 10,
                    AnchoCm = 10,
                    AltoCm = 10
                }
            ]
        };

        using var respuestaEnvio = await clienteRutas.PostAsJsonAsync(
            "/api/envios",
            solicitudEnvio);

        var contenidoEnvio = await respuestaEnvio.Content.ReadAsStringAsync();

        Assert.True(
            respuestaEnvio.StatusCode == HttpStatusCode.Created,
            $"No se pudo crear el envío: {(int)respuestaEnvio.StatusCode} - {contenidoEnvio}");

        Assert.NotNull(respuestaEnvio.Headers.Location);

        var envioId = int.Parse(
            respuestaEnvio.Headers.Location
                .ToString()
                .TrimEnd('/')
                .Split('/')
                .Last());

        Assert.Equal(HttpStatusCode.Created, respuestaEnvio.StatusCode);

        var solicitudRuta = new CrearRutaRequest
        {
            Nombre = $"Prueba concurrencia {Guid.NewGuid():N}",
            Fecha = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1),
            RepartidorId = checked((int)repartidorId),
            VehiculoId = vehiculo.Id,
            EnvioIds = [envioId]
        };

        var inicio = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var crearRuta = Task.Run(async () =>
        {
            await inicio.Task;

            return await clienteRutas.PostAsJsonAsync(
                "/api/rutas",
                solicitudRuta);
        });

        var desactivar = Task.Run(async () =>
        {
            await inicio.Task;

            return await clienteUsuarios.PatchAsJsonAsync(
                $"/api/usuarios/{usuario.Id}/estado",
                new CambiarEstadoUsuarioRequest(false));
        });

        inicio.SetResult();

        await Task.WhenAll(crearRuta, desactivar);

        using var respuestaRuta = await crearRuta;
        using var respuestaDesactivar = await desactivar;

        var inconsistencias = await api.ConsultarCantidadAsync(
            """
        SELECT COUNT(*)
        FROM "Repartidores" rep
        JOIN "AspNetUsers" u ON u."Id" = rep."UsuarioId"
        JOIN "Rutas" r ON r."RepartidorId" = rep."Id"
        WHERE rep."UsuarioId" = @usuarioId
          AND (rep."Activo" = false OR u."Activo" = false)
          AND r."Estado" IN (1, 2, 3)
        """,
            new NpgsqlParameter("usuarioId", usuario.Id));

        Assert.Equal(0, inconsistencias);

        Assert.True(
            respuestaRuta.IsSuccessStatusCode ||
            respuestaDesactivar.IsSuccessStatusCode,
            $"Crear ruta: {(int)respuestaRuta.StatusCode}, " +
            $"desactivar: {(int)respuestaDesactivar.StatusCode}");
    }

}
