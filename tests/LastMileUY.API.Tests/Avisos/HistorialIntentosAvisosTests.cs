namespace LastMileUY.API.Tests.Avisos;

using LastMileUY.API.Infrastructure.Multitenancy;
using LastMileUY.API.Modules.Notificaciones.Application;
using LastMileUY.API.Modules.Notificaciones.Domain;
using LastMileUY.API.Tests.Multitenancy;
using LastMileUY.Contracts.Avisos;
using LastMileUY.Contracts.Mensajeria;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using static LastMileUY.API.Tests.Multitenancy.BaseConDosOperadores;

// El historial de intentos de los avisos (IntentosAvisos) también queda aislado por operador (ADR-01)
[Collection(ColeccionBaseConDosOperadores.Nombre)]
public class HistorialIntentosAvisosTests(BaseConDosOperadores baseDeDatos)
{
    [Fact]
    public async Task Un_operador_no_ve_el_historial_de_los_avisos_de_otro()
    {
        int avisoId;

        // Un aviso de un envío del operador A, con dos intentos
        await using (var db = baseDeDatos.CrearContexto(OperadorA, ComercioCompartido))
        {
            var configuracion = new ConfigurationBuilder().Build();
            var suscripcion = await new SuscripcionesAvisosService(
                    db, new ContextoOperadorFijo(OperadorA, ComercioCompartido), configuracion)
                .CrearAsync(new CrearSuscripcionAvisosRequest
                {
                    TipoEvento = TiposEvento.EnvioEstadoCambiado,
                    UrlDestino = "https://tienda.uy/avisos/historial"
                });

            var aviso = new EntregaAviso
            {
                SuscripcionAvisosId = suscripcion.Id,
                EnvioId = EnvioDeA,
                TipoEvento = TiposEvento.EnvioEstadoCambiado,
                Contenido = "{}",
                Intentos =
                {
                    new IntentoAviso { NumeroIntento = 1, CodigoHttp = 503, Error = "Respondió 503" },
                    new IntentoAviso { NumeroIntento = 2, Exitoso = true, CodigoHttp = 200 }
                }
            };
            db.EntregasAvisos.Add(aviso);
            await db.SaveChangesAsync();
            avisoId = aviso.Id;
        }

        await using (var conA = baseDeDatos.CrearContexto(OperadorA))
        {
            Assert.Equal(2, await conA.IntentosAvisos.CountAsync(i => i.EntregaAvisoId == avisoId));
        }

        // Ni con el filtro de EF ni salteándolo (lo frena el RLS de la base)
        await using var conB = baseDeDatos.CrearContexto(OperadorB);
        Assert.Equal(0, await conB.IntentosAvisos.CountAsync(i => i.EntregaAvisoId == avisoId));
        Assert.Equal(0, await conB.IntentosAvisos.IgnoreQueryFilters().CountAsync(i => i.EntregaAvisoId == avisoId));
    }
}
