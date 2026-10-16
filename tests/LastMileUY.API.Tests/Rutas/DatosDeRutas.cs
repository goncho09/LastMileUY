namespace LastMileUY.API.Tests.Rutas;

using LastMileUY.API.Modules.Envios.Domain;
using LastMileUY.API.Modules.Rutas.Application;
using LastMileUY.API.Modules.Rutas.Domain;
using LastMileUY.API.Tests.Multitenancy;
using LastMileUY.Contracts.Rutas;
using Npgsql;
using static LastMileUY.API.Tests.Multitenancy.BaseConDosOperadores;

// Datos para las pruebas de hojas de ruta y de sincronización, sobre la base con dos operadores.
// Cada llamada crea datos nuevos, así las pruebas no chocan entre sí.
public static class DatosDeRutas
{
    public static readonly DateOnly Manana = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1);

    // Un envío del comercio compartido con un bulto de 2 kg y 10x10x10 cm
    public static async Task<int> CrearEnvioAsync(
        BaseConDosOperadores baseDeDatos, int operadorId, bool enDeposito = true, int? franjaId = null)
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
    // Devuelve también el Id del usuario, que es lo que identifica al repartidor al iniciar sesión.
    public static async Task<(int RepartidorId, int VehiculoId, string UsuarioId)> CrearRepartidorYVehiculoAsync(
        BaseConDosOperadores baseDeDatos, int operadorId, decimal capacidadKg = 100, decimal capacidadM3 = 1)
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

    // Una ruta ya despachada con envíos nuevos (en tránsito), lista para que el repartidor sincronice
    public static async Task<(int RutaId, string UsuarioId, int[] Envios)> CrearRutaDespachadaAsync(
        BaseConDosOperadores baseDeDatos, int operadorId, int cantidadEnvios = 1)
    {
        var (repartidor, vehiculo, usuario) = await CrearRepartidorYVehiculoAsync(baseDeDatos, operadorId);
        var envios = new int[cantidadEnvios];

        for (var i = 0; i < cantidadEnvios; i++)
        {
            envios[i] = await CrearEnvioAsync(baseDeDatos, operadorId);
        }

        await using var db = baseDeDatos.CrearContexto(operadorId);
        var planificar = new PlanificarRutaService(db);

        var rutaId = await planificar.CrearAsync(new CrearRutaRequest
        {
            Nombre = "Ruta a sincronizar",
            Fecha = Manana,
            RepartidorId = repartidor,
            VehiculoId = vehiculo,
            EnvioIds = [.. envios]
        }, "usuario-1");

        db.ChangeTracker.Clear();
        await planificar.DespacharAsync(rutaId, "usuario-1");

        return (rutaId, usuario, envios);
    }
}
