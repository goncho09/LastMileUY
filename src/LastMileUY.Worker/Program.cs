using LastMileUY.Worker;
using Npgsql;

var builder = Host.CreateApplicationBuilder(args);

// Consume los eventos de envío que la API publica en RabbitMQ (ver docs/mensajeria.md)
builder.Services.AddSingleton(new OpcionesConsumidor(
    builder.Configuration.GetConnectionString("RabbitMQ")
        ?? throw new InvalidOperationException("Falta configurar ConnectionStrings:RabbitMQ."),
    builder.Configuration.GetSection("Mensajeria:EsperasReintento").Get<TimeSpan[]>()
        ?? OpcionesConsumidor.EsperasPorDefecto));

// La base con el usuario de la aplicación (lastmileuy_app): le aplica el RLS del ADR-01
builder.Services.AddSingleton(NpgsqlDataSource.Create(
    builder.Configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("Falta configurar ConnectionStrings:DefaultConnection.")));

builder.Services.AddSingleton<IProcesadorEventos, ProcesadorAvisos>();
builder.Services.AddHostedService<ConsumidorEventosEnvio>();

// Los registros muestran el identificador de correlación de cada evento
builder.Logging.AddSimpleConsole(opciones => opciones.IncludeScopes = true);

var host = builder.Build();
host.Run();
