using System.Security.Claims;
using LastMileUY.API.Infrastructure.Multitenancy;
using LastMileUY.API.Modules.Operadores.Application;
using Microsoft.AspNetCore.Http;

namespace LastMileUY.API.Tests.Envios;

using LastMileUY.API.Modules.Envios.Application;
using LastMileUY.API.Modules.Envios.Domain;
using Multitenancy;
using LastMileUY.Contracts.Envios;
using Microsoft.EntityFrameworkCore;
using static Multitenancy.BaseConDosOperadores;

// Cada operador trabaja con sus propias reglas operativas. En la base de prueba,
// el operador A permite 1 intento de entrega fallido y el B permite 2.
[Collection(ColeccionBaseConDosOperadores.Nombre)]
public class ReglasOperativasTests(BaseConDosOperadores baseDeDatos)
{
    private static ClaimsPrincipal CrearUsuarioOperador() =>
        new(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "usuario-operador-1"),
            new Claim(ClaimTypes.Role, "Operador")
        ], "Test"));
    
    [Fact]
    public async Task Con_los_intentos_agotados_no_se_puede_reprogramar()
    {
        var id = await CrearEnvioAsync(OperadorA);
        await LlevarAsync(OperadorA, id, "EnDeposito", "AsignadoARuta", "EnTransito", "NoEntregado");

        await using var db = baseDeDatos.CrearContexto(OperadorA);
        var error = await Assert.ThrowsAsync<TransicionInvalidaException>(() => new CambiarEstadoEnvioService(db)
            .EjecutarAsync(
                id,
                new CambiarEstadoRequest { Estado = "Reprogramado" },
                "Backoffice",
                CrearUsuarioOperador()));

        Assert.Contains(ReglaDeA, error.Message);
    }

    [Fact]
    public async Task El_mismo_caso_con_el_otro_operador_si_deja_reprogramar()
    {
        var id = await CrearEnvioAsync(OperadorB);

        // Primer intento fallido: B permite 2, así que se reprograma
        await LlevarAsync(OperadorB, id, "EnDeposito", "AsignadoARuta", "EnTransito", "NoEntregado", "Reprogramado");

        // Segundo intento fallido: ahí sí se agotaron
        await LlevarAsync(OperadorB, id, "AsignadoARuta", "EnTransito", "NoEntregado");

        var detalle = await DetalleAsync(OperadorB, id);

        Assert.Equal(MaximoIntentosDeB, detalle.IntentosFallidos);
        Assert.Equal(["EnDevolucion"], detalle.EstadosSiguientes);
    }

    [Fact]
    public async Task El_detalle_no_ofrece_reprogramar_si_se_agotaron_los_intentos()
    {
        var id = await CrearEnvioAsync(OperadorA);
        await LlevarAsync(OperadorA, id, "EnDeposito", "AsignadoARuta", "EnTransito", "NoEntregado");

        var detalle = await DetalleAsync(OperadorA, id);

        Assert.Equal(["EnDevolucion"], detalle.EstadosSiguientes);
        Assert.Equal(1, detalle.IntentosFallidos);
    }

    [Fact]
    public async Task Cada_operador_ve_su_propia_regla_en_el_detalle()
    {
        var idDeA = await CrearEnvioAsync(OperadorA);
        var idDeB = await CrearEnvioAsync(OperadorB);

        var detalleDeA = await DetalleAsync(OperadorA, idDeA);
        var detalleDeB = await DetalleAsync(OperadorB, idDeB);

        Assert.Equal(ReglaDeA, detalleDeA.Regla?.Nombre);
        Assert.Equal(MaximoIntentosDeA, detalleDeA.Regla?.MaximoIntentosEntrega);
        Assert.Equal(detalleDeA.FechaCreacion.AddHours(PlazoHorasDeA), detalleDeA.FechaLimiteEntrega);

        Assert.Equal(ReglaDeB, detalleDeB.Regla?.Nombre);
        Assert.Equal(MaximoIntentosDeB, detalleDeB.Regla?.MaximoIntentosEntrega);
    }

    [Fact]
    public async Task Sin_regla_para_la_modalidad_solo_aplica_la_tabla_de_estados()
    {
        // Ninguno de los dos operadores tiene regla para Urgente
        var id = await CrearEnvioAsync(OperadorA, ModalidadEnvio.Urgente);
        await LlevarAsync(OperadorA, id, "EnDeposito", "AsignadoARuta", "EnTransito", "NoEntregado", "Reprogramado");
        await LlevarAsync(OperadorA, id, "AsignadoARuta", "EnTransito", "NoEntregado");

        var detalle = await DetalleAsync(OperadorA, id);

        Assert.Null(detalle.Regla);
        Assert.Null(detalle.FechaLimiteEntrega);
        Assert.Contains("Reprogramado", detalle.EstadosSiguientes);
    }

    [Fact]
    public async Task Cada_intento_de_entrega_queda_registrado()
    {
        var id = await CrearEnvioAsync(OperadorB);
        await LlevarAsync(OperadorB, id, "EnDeposito", "AsignadoARuta", "EnTransito", "NoEntregado", "Reprogramado",
            "AsignadoARuta", "EnTransito", "Entregado");

        await using var db = baseDeDatos.CrearContexto(OperadorB);
        var intentos = await db.Set<Modules.Entregas.Domain.IntentoEntrega>()
            .Where(i => i.EnvioId == id)
            .OrderBy(i => i.NumeroIntento)
            .ToListAsync();

        Assert.Equal([1, 2], intentos.Select(i => i.NumeroIntento));
        Assert.Equal([false, true], intentos.Select(i => i.Exitoso));
    }

    [Fact]
    public async Task Un_operador_no_ve_las_reglas_del_otro()
    {
        await using var db = baseDeDatos.CrearContexto(OperadorA);

        // También sin el filtro de EF: el RLS de la base las oculta igual
        var reglas = await db.ReglasOperativas.IgnoreQueryFilters().ToListAsync();

        Assert.NotEmpty(reglas);
        Assert.All(reglas, r => Assert.Equal(OperadorA, r.OperadorId));
    }

    private async Task<int> CrearEnvioAsync(int operadorId, ModalidadEnvio modalidad = ModalidadEnvio.Estandar)
    {
        await using var db = baseDeDatos.CrearContexto(operadorId);
        var envio = new Envio { ComercioId = ComercioCompartido, DestinatarioId = 1, Modalidad = modalidad };
        envio.AsignarDireccionEntrega(new DireccionEntrega(
            "Rivera", "1234", null, "Montevideo", "Montevideo", null, null));

        db.Envios.Add(envio);
        await db.SaveChangesAsync();

        return envio.Id;
    }

    // Pasa el envío por los estados indicados, cada uno en su propio pedido (como desde el Backoffice)
    
    private async Task LlevarAsync(
        int operadorId,
        int id,
        params string[] estados)
    {
        foreach (var estado in estados)
        {
            await using var db = baseDeDatos.CrearContexto(operadorId);

            if (estado is "AsignadoARuta" or "EnTransito")
            {
                var envio = await db.Envios
                    .Include(e => e.IntentosEntrega)
                    .FirstAsync(e => e.Id == id);

                var nuevoEstado = Enum.Parse<EstadoEnvio>(estado);

                var regla = await db.ObtenerVigenteAsync(envio.Modalidad);

                envio.CambiarEstado(
                    nuevoEstado,
                    "Sistema",
                    regla: regla);

                await db.SaveChangesAsync();
            }
            else
            {
                await new CambiarEstadoEnvioService(db)
                    .EjecutarAsync(
                        id,
                        new CambiarEstadoRequest { Estado = estado },
                        "Backoffice",
                        CrearUsuarioOperador());
            }
        }
    }


    private async Task<EnvioDetalleDto> DetalleAsync(int operadorId, int id)
    {
        await using var db = baseDeDatos.CrearContexto(operadorId);

        var httpContextAccessor = new HttpContextAccessor()
        {
            HttpContext = new DefaultHttpContext
            {
                User = CrearUsuarioOperador()
            }
        };

        var consultas = new ConsultasEnviosService(
            db,
            new ContextoOperadorFijo(operadorId),
            httpContextAccessor);

        var detalle = await consultas.ObtenerDetalleAsync(id);

        Assert.NotNull(detalle);
        return detalle;
    }
    
    [Theory]
    [InlineData("AsignadoARuta")]
    [InlineData("EnTransito")]
    public async Task No_se_puede_asignar_ni_despachar_manualmente(
        string estado)
    {
        var id = await CrearEnvioAsync(OperadorA);

        await LlevarAsync(OperadorA, id, "EnDeposito");

        await using var db = baseDeDatos.CrearContexto(OperadorA);

        await Assert.ThrowsAsync<TransicionInvalidaException>(
            () => new CambiarEstadoEnvioService(db)
                .EjecutarAsync(
                    id,
                    new CambiarEstadoRequest { Estado = estado },
                    "Backoffice",
                    CrearUsuarioOperador()));
    }
}
