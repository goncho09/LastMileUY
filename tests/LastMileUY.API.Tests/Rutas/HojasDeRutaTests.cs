namespace LastMileUY.API.Tests.Rutas;

using LastMileUY.API.Modules.Envios.Domain;
using LastMileUY.API.Modules.Operadores.Domain;
using LastMileUY.API.Modules.Rutas.Application;
using LastMileUY.API.Modules.Rutas.Domain;
using LastMileUY.API.Tests.Multitenancy;
using LastMileUY.Contracts.Rutas;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using static LastMileUY.API.Tests.Multitenancy.BaseConDosOperadores;

// Hojas de ruta contra la base real: armado, validaciones (capacidad, franjas, duplicados),
// orden de las paradas, despacho y descarga por el repartidor asignado.
// Cada prueba usa su propio repartidor y vehículo, para no chocar con las demás.
[Collection(ColeccionBaseConDosOperadores.Nombre)]
public class HojasDeRutaTests(BaseConDosOperadores baseDeDatos)
{
    private static readonly DateOnly Manana = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1);

    [Fact]
    public async Task Crear_una_ruta_guarda_las_paradas_en_el_orden_pedido_y_asigna_los_envios()
    {
        var (repartidor, vehiculo, _) = await CrearRepartidorYVehiculoAsync(OperadorA);
        var primero = await CrearEnvioEnDepositoAsync(OperadorA);
        var segundo = await CrearEnvioEnDepositoAsync(OperadorA);

        var id = await CrearRutaAsync(OperadorA, repartidor, vehiculo, segundo, primero);

        await using var db = baseDeDatos.CrearContexto(OperadorA);
        var detalle = await new ConsultasRutasService(db).ObtenerDetalleAsync(id);

        Assert.NotNull(detalle);
        Assert.Equal("Planificada", detalle.Estado);
        Assert.Equal([segundo, primero], detalle.Paradas.Select(p => p.EnvioId));
        Assert.Equal([1, 2], detalle.Paradas.Select(p => p.Orden));
        Assert.All(detalle.Paradas, p => Assert.Equal("AsignadoARuta", p.EstadoEnvio));

        // Dos envíos de un bulto de 2 kg y 10x10x10 cm (0,001 m³)
        Assert.Equal(4, detalle.PesoTotalKg);
        Assert.Equal(0.002m, detalle.VolumenTotalM3);

        // Queda en el historial de cada envío
        var evento = await db.EventosEnvio
            .Where(e => e.EnvioId == segundo)
            .OrderByDescending(e => e.Id)
            .FirstAsync();
        Assert.Equal("AsignadoARuta", evento.Tipo);
        Assert.Contains("parada 1", evento.Descripcion);
    }

    [Fact]
    public async Task Un_envio_que_no_esta_en_deposito_no_se_puede_asignar()
    {
        var (repartidor, vehiculo, _) = await CrearRepartidorYVehiculoAsync(OperadorA);
        var admitido = await CrearEnvioAsync(OperadorA, enDeposito: false);

        var error = await Assert.ThrowsAsync<ArgumentException>(() =>
            CrearRutaAsync(OperadorA, repartidor, vehiculo, admitido));

        Assert.Contains("en depósito o reprogramados", error.Message);
    }

    [Fact]
    public async Task Un_envio_repetido_en_la_lista_es_un_error()
    {
        var (repartidor, vehiculo, _) = await CrearRepartidorYVehiculoAsync(OperadorA);
        var envio = await CrearEnvioEnDepositoAsync(OperadorA);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            CrearRutaAsync(OperadorA, repartidor, vehiculo, envio, envio));
    }

    [Fact]
    public async Task Un_envio_no_puede_estar_en_dos_rutas_abiertas()
    {
        var (repartidor1, vehiculo1, _) = await CrearRepartidorYVehiculoAsync(OperadorA);
        var (repartidor2, vehiculo2, _) = await CrearRepartidorYVehiculoAsync(OperadorA);
        var envio = await CrearEnvioEnDepositoAsync(OperadorA);

        await CrearRutaAsync(OperadorA, repartidor1, vehiculo1, envio);

        await Assert.ThrowsAsync<AsignacionDuplicadaException>(() =>
            CrearRutaAsync(OperadorA, repartidor2, vehiculo2, envio));
    }

    [Fact]
    public async Task Un_repartidor_no_puede_tener_dos_rutas_abiertas_el_mismo_dia()
    {
        var (repartidor, vehiculo1, _) = await CrearRepartidorYVehiculoAsync(OperadorA);
        var (_, vehiculo2, _) = await CrearRepartidorYVehiculoAsync(OperadorA);

        await CrearRutaAsync(OperadorA, repartidor, vehiculo1, await CrearEnvioEnDepositoAsync(OperadorA));

        var otroEnvio = await CrearEnvioEnDepositoAsync(OperadorA);
        var error = await Assert.ThrowsAsync<AsignacionDuplicadaException>(() =>
            CrearRutaAsync(OperadorA, repartidor, vehiculo2, otroEnvio));
        Assert.Contains("repartidor", error.Message);

        // Otro día sí puede
        await CrearRutaAsync(OperadorA, repartidor, vehiculo2, await CrearEnvioEnDepositoAsync(OperadorA),
            fecha: Manana.AddDays(1));
    }

    [Fact]
    public async Task Un_vehiculo_no_puede_estar_en_dos_rutas_abiertas_el_mismo_dia()
    {
        var (repartidor1, vehiculo, _) = await CrearRepartidorYVehiculoAsync(OperadorA);
        var (repartidor2, _, _) = await CrearRepartidorYVehiculoAsync(OperadorA);

        await CrearRutaAsync(OperadorA, repartidor1, vehiculo, await CrearEnvioEnDepositoAsync(OperadorA));

        var otroEnvio = await CrearEnvioEnDepositoAsync(OperadorA);
        var error = await Assert.ThrowsAsync<AsignacionDuplicadaException>(() =>
            CrearRutaAsync(OperadorA, repartidor2, vehiculo, otroEnvio));
        Assert.Contains("vehículo", error.Message);
    }

    [Fact]
    public async Task Superar_el_peso_que_carga_el_vehiculo_se_rechaza()
    {
        // El vehículo carga 3 kg y cada envío pesa 2
        var (repartidor, vehiculo, _) = await CrearRepartidorYVehiculoAsync(OperadorA, capacidadKg: 3);

        var error = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await CrearRutaAsync(OperadorA, repartidor, vehiculo,
                await CrearEnvioEnDepositoAsync(OperadorA),
                await CrearEnvioEnDepositoAsync(OperadorA)));

        Assert.Contains("kg", error.Message);
    }

    [Fact]
    public async Task Superar_el_volumen_del_vehiculo_se_rechaza()
    {
        // Cada envío ocupa 0,001 m³ y el vehículo tiene lugar para uno (la columna guarda 3 decimales)
        var (repartidor, vehiculo, _) = await CrearRepartidorYVehiculoAsync(OperadorA, capacidadM3: 0.001m);

        var error = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await CrearRutaAsync(OperadorA, repartidor, vehiculo,
                await CrearEnvioEnDepositoAsync(OperadorA),
                await CrearEnvioEnDepositoAsync(OperadorA)));

        Assert.Contains("m³", error.Message);
    }

    [Fact]
    public async Task Las_paradas_con_franja_van_en_orden_de_horario()
    {
        var (manana, tarde, _) = await CrearFranjasAsync(OperadorA);
        var (repartidor, vehiculo, _) = await CrearRepartidorYVehiculoAsync(OperadorA);
        var deTarde = await CrearEnvioEnDepositoAsync(OperadorA, franjaId: tarde);
        var sinFranja = await CrearEnvioEnDepositoAsync(OperadorA);
        var deManana = await CrearEnvioEnDepositoAsync(OperadorA, franjaId: manana);

        // La de la mañana no puede ir después de la de la tarde
        var error = await Assert.ThrowsAsync<ArgumentException>(() =>
            CrearRutaAsync(OperadorA, repartidor, vehiculo, deTarde, sinFranja, deManana));
        Assert.Contains("orden de horario", error.Message);

        // En orden sí (la parada sin franja puede ir en cualquier lugar)
        var id = await CrearRutaAsync(OperadorA, repartidor, vehiculo, deManana, sinFranja, deTarde);

        await using var db = baseDeDatos.CrearContexto(OperadorA);
        var detalle = await new ConsultasRutasService(db).ObtenerDetalleAsync(id);
        Assert.Equal(["Mañana", null, "Tarde"], detalle!.Paradas.Select(p => p.Franja?.Nombre));
    }

    [Fact]
    public async Task Una_franja_inactiva_se_rechaza()
    {
        var (_, _, inactiva) = await CrearFranjasAsync(OperadorA);
        var (repartidor, vehiculo, _) = await CrearRepartidorYVehiculoAsync(OperadorA);

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await CrearRutaAsync(OperadorA, repartidor, vehiculo,
                await CrearEnvioEnDepositoAsync(OperadorA, franjaId: inactiva)));
    }

    [Fact]
    public async Task Una_fecha_pasada_se_rechaza()
    {
        var (repartidor, vehiculo, _) = await CrearRepartidorYVehiculoAsync(OperadorA);

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await CrearRutaAsync(OperadorA, repartidor, vehiculo,
                await CrearEnvioEnDepositoAsync(OperadorA),
                fecha: DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-2)));
    }

    [Fact]
    public async Task No_se_puede_armar_una_ruta_con_repartidor_vehiculo_ni_envios_de_otro_operador()
    {
        var (repartidorA, vehiculoA, _) = await CrearRepartidorYVehiculoAsync(OperadorA);
        var (repartidorB, vehiculoB, _) = await CrearRepartidorYVehiculoAsync(OperadorB);
        var envioA = await CrearEnvioEnDepositoAsync(OperadorA);
        var envioB = await CrearEnvioEnDepositoAsync(OperadorB);

        // Para el operador A, lo del operador B no existe
        var conRepartidorDeB = await Assert.ThrowsAsync<ArgumentException>(() =>
            CrearRutaAsync(OperadorA, repartidorB, vehiculoA, envioA));
        var conVehiculoDeB = await Assert.ThrowsAsync<ArgumentException>(() =>
            CrearRutaAsync(OperadorA, repartidorA, vehiculoB, envioA));
        var conEnvioDeB = await Assert.ThrowsAsync<ArgumentException>(() =>
            CrearRutaAsync(OperadorA, repartidorA, vehiculoA, envioB));

        Assert.Contains("no existe", conRepartidorDeB.Message);
        Assert.Contains("no existe", conVehiculoDeB.Message);
        Assert.Contains("no existe", conEnvioDeB.Message);
    }

    [Fact]
    public async Task Despachar_pasa_la_ruta_a_despachada_y_sus_envios_a_en_transito()
    {
        var (repartidor, vehiculo, _) = await CrearRepartidorYVehiculoAsync(OperadorA);
        var id = await CrearRutaAsync(OperadorA, repartidor, vehiculo,
            await CrearEnvioEnDepositoAsync(OperadorA),
            await CrearEnvioEnDepositoAsync(OperadorA));

        await using (var db = baseDeDatos.CrearContexto(OperadorA))
        {
            Assert.True(await new PlanificarRutaService(db).DespacharAsync(id, "usuario-1"));
        }

        await using (var db = baseDeDatos.CrearContexto(OperadorA))
        {
            var detalle = await new ConsultasRutasService(db).ObtenerDetalleAsync(id);

            Assert.Equal("Despachada", detalle!.Estado);
            Assert.All(detalle.Paradas, p => Assert.Equal("EnTransito", p.EstadoEnvio));

            // Despachar dos veces no vale
            await Assert.ThrowsAsync<EstadoDeRutaException>(() =>
                new PlanificarRutaService(db).DespacharAsync(id, "usuario-1"));
        }
    }

    [Fact]
    public async Task No_se_despacha_si_un_envio_se_saco_de_la_ruta()
    {
        var (repartidor, vehiculo, _) = await CrearRepartidorYVehiculoAsync(OperadorA);
        var envio = await CrearEnvioEnDepositoAsync(OperadorA);
        var id = await CrearRutaAsync(OperadorA, repartidor, vehiculo, envio);

        // Alguien lo devolvió al depósito a mano (AsignadoARuta -> EnDeposito)
        await using (var db = baseDeDatos.CrearContexto(OperadorA))
        {
            var cargado = await db.Envios.FirstAsync(e => e.Id == envio);
            cargado.CambiarEstado(EstadoEnvio.EnDeposito);
            await db.SaveChangesAsync();
        }

        await using (var db = baseDeDatos.CrearContexto(OperadorA))
        {
            var error = await Assert.ThrowsAsync<EstadoDeRutaException>(() =>
                new PlanificarRutaService(db).DespacharAsync(id, "usuario-1"));
            Assert.Contains("ya no está asignado", error.Message);
        }
    }

    [Fact]
    public async Task Otro_operador_no_ve_ni_despacha_la_ruta()
    {
        var (repartidor, vehiculo, _) = await CrearRepartidorYVehiculoAsync(OperadorA);
        var id = await CrearRutaAsync(OperadorA, repartidor, vehiculo, await CrearEnvioEnDepositoAsync(OperadorA));

        await using var db = baseDeDatos.CrearContexto(OperadorB);

        Assert.Null(await new ConsultasRutasService(db).ObtenerDetalleAsync(id));
        Assert.False(await new PlanificarRutaService(db).DespacharAsync(id, "usuario-b"));
        Assert.DoesNotContain(
            (await new ConsultasRutasService(db).ListarAsync(1, 100, null, null)).Items,
            r => r.Id == id);
    }

    [Fact]
    public async Task La_hoja_la_descarga_solo_el_repartidor_asignado_y_una_vez_despachada()
    {
        var (repartidor, vehiculo, usuarioAsignado) = await CrearRepartidorYVehiculoAsync(OperadorA);
        var (_, _, otroUsuario) = await CrearRepartidorYVehiculoAsync(OperadorA);
        var id = await CrearRutaAsync(OperadorA, repartidor, vehiculo, await CrearEnvioEnDepositoAsync(OperadorA));

        await using var db = baseDeDatos.CrearContexto(OperadorA);
        var consultas = new ConsultasRutasService(db);

        // Todavía planificada: no está disponible ni en sus rutas
        Assert.Equal(ResultadoHoja.NoDespachada, (await consultas.ObtenerHojaDelRepartidorAsync(id, usuarioAsignado)).Resultado);
        Assert.DoesNotContain(await consultas.ListarDelRepartidorAsync(usuarioAsignado), r => r.Id == id);

        await new PlanificarRutaService(db).DespacharAsync(id, "usuario-1");

        var (resultado, hoja) = await consultas.ObtenerHojaDelRepartidorAsync(id, usuarioAsignado);
        Assert.Equal(ResultadoHoja.Disponible, resultado);
        Assert.Single(hoja!.Paradas);
        Assert.Contains(await consultas.ListarDelRepartidorAsync(usuarioAsignado), r => r.Id == id);

        // Otro repartidor del mismo operador no
        Assert.Equal(ResultadoHoja.DeOtroRepartidor, (await consultas.ObtenerHojaDelRepartidorAsync(id, otroUsuario)).Resultado);
        Assert.DoesNotContain(await consultas.ListarDelRepartidorAsync(otroUsuario), r => r.Id == id);
    }

    [Fact]
    public async Task Las_opciones_no_ofrecen_envios_que_ya_estan_en_una_ruta()
    {
        var (repartidor, vehiculo, _) = await CrearRepartidorYVehiculoAsync(OperadorA);
        var enRuta = await CrearEnvioEnDepositoAsync(OperadorA);
        var libre = await CrearEnvioEnDepositoAsync(OperadorA);
        var admitido = await CrearEnvioAsync(OperadorA, enDeposito: false);

        await CrearRutaAsync(OperadorA, repartidor, vehiculo, enRuta);

        await using var db = baseDeDatos.CrearContexto(OperadorA);
        var opciones = await new ConsultasRutasService(db).ObtenerOpcionesAsync();

        Assert.Contains(opciones.Envios, e => e.Id == libre);
        Assert.DoesNotContain(opciones.Envios, e => e.Id == enRuta);
        Assert.DoesNotContain(opciones.Envios, e => e.Id == admitido);
        Assert.Contains(opciones.Repartidores, r => r.Id == repartidor);
        Assert.Contains(opciones.Vehiculos, v => v.Id == vehiculo);
    }

    private async Task<int> CrearRutaAsync(
        int operadorId, int repartidorId, int vehiculoId, params int[] envioIds) =>
        await CrearRutaAsync(operadorId, repartidorId, vehiculoId, envioIds[0], envioIds[1..], Manana);

    private Task<int> CrearRutaAsync(
        int operadorId, int repartidorId, int vehiculoId, int envioId, DateOnly fecha) =>
        CrearRutaAsync(operadorId, repartidorId, vehiculoId, envioId, [], fecha);

    private async Task<int> CrearRutaAsync(
        int operadorId, int repartidorId, int vehiculoId, int primerEnvio, int[] resto, DateOnly fecha)
    {
        await using var db = baseDeDatos.CrearContexto(operadorId);

        return await new PlanificarRutaService(db).CrearAsync(new CrearRutaRequest
        {
            Nombre = "Ruta de prueba",
            Fecha = fecha,
            RepartidorId = repartidorId,
            VehiculoId = vehiculoId,
            EnvioIds = [primerEnvio, .. resto]
        }, "usuario-1");
    }

    private Task<int> CrearEnvioEnDepositoAsync(int operadorId, int? franjaId = null) =>
        CrearEnvioAsync(operadorId, enDeposito: true, franjaId);

    // Un envío del comercio compartido con un bulto de 2 kg y 10x10x10 cm
    private async Task<int> CrearEnvioAsync(int operadorId, bool enDeposito, int? franjaId = null)
    {
        await using var db = baseDeDatos.CrearContexto(operadorId);
        var envio = new Envio { ComercioId = ComercioCompartido, DestinatarioId = 1, FranjaHorariaId = franjaId };
        envio.AsignarDireccionEntrega(new DireccionEntrega(
            "Rivera", "1234", null, "Montevideo", "Montevideo", null, null));
        envio.Bultos.Add(new Bulto { Codigo = "B1", PesoKg = 2, LargoCm = 10, AnchoCm = 10, AltoCm = 10 });

        if (enDeposito)
        {
            envio.CambiarEstado(EstadoEnvio.EnDeposito);
        }

        db.Envios.Add(envio);
        await db.SaveChangesAsync();

        return envio.Id;
    }

    // Un usuario repartidor (con su ficha) y un vehículo nuevos del operador.
    // Devuelve el Id del usuario también, que es lo que identifica al repartidor al iniciar sesión.
    private async Task<(int RepartidorId, int VehiculoId, string UsuarioId)> CrearRepartidorYVehiculoAsync(
        int operadorId, decimal capacidadKg = 100, decimal capacidadM3 = 1)
    {
        var usuarioId = Guid.NewGuid().ToString();

        // Los usuarios no tienen RLS, pero se cargan como superusuario igual que el resto de los datos base
        await using (var conexion = new NpgsqlConnection(baseDeDatos.CadenaAdministrador))
        {
            await conexion.OpenAsync();
            await using var comando = new NpgsqlCommand($"""
                INSERT INTO "AspNetUsers"("Id", "UserName", "OperadorId", "Nombre", "Apellido", "Activo", "FechaCreacion",
                        "EmailConfirmed", "PhoneNumberConfirmed", "TwoFactorEnabled", "LockoutEnabled", "AccessFailedCount")
                    VALUES ('{usuarioId}', '{usuarioId}', {operadorId}, 'Repartidor', 'De prueba', true, now(),
                        true, false, false, false, 0)
                """, conexion);
            await comando.ExecuteNonQueryAsync();
        }

        await using var db = baseDeDatos.CrearContexto(operadorId);
        var repartidor = new Repartidor { UsuarioId = usuarioId };
        var vehiculo = new Vehiculo
        {
            Matricula = $"T{Guid.NewGuid():N}"[..10],
            Tipo = "Furgoneta",
            CapacidadPesoKg = capacidadKg,
            CapacidadVolumenM3 = capacidadM3
        };

        db.Repartidores.Add(repartidor);
        db.Vehiculos.Add(vehiculo);
        await db.SaveChangesAsync();

        return (repartidor.Id, vehiculo.Id, usuarioId);
    }

    // Una zona del operador con franjas de mañana y tarde (activas) y una inactiva
    private async Task<(int Manana, int Tarde, int Inactiva)> CrearFranjasAsync(int operadorId)
    {
        await using var db = baseDeDatos.CrearContexto(operadorId);
        var manana = new FranjaHoraria { Nombre = "Mañana", HoraInicio = new TimeOnly(9, 0), HoraFin = new TimeOnly(12, 0) };
        var tarde = new FranjaHoraria { Nombre = "Tarde", HoraInicio = new TimeOnly(14, 0), HoraFin = new TimeOnly(18, 0) };
        var inactiva = new FranjaHoraria
        {
            Nombre = "Noche", HoraInicio = new TimeOnly(19, 0), HoraFin = new TimeOnly(21, 0), Activa = false
        };

        db.Zonas.Add(new Zona { Nombre = $"Zona {Guid.NewGuid():N}", FranjasHorarias = { manana, tarde, inactiva } });
        await db.SaveChangesAsync();

        return (manana.Id, tarde.Id, inactiva.Id);
    }
}
