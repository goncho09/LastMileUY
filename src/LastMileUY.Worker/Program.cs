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

builder.Services.AddSingleton<RepositorioMensajesFallidos>();
builder.Services.AddSingleton<SenalAvisos>();
builder.Services.AddSingleton<IProcesadorEventos, ProcesadorAvisos>();
builder.Services.AddHostedService<ConsumidorEventosEnvio>();

// Manda los avisos por HTTP a los comercios. Avisos:PermitirDestinosLocales deja usar http y
// direcciones internas: solo para desarrollo, con el receptor de prueba (ver docs/mensajeria.md)
var permitirLocales = builder.Configuration.GetValue<bool>("Avisos:PermitirDestinosLocales");
builder.Services.AddHttpClient<ClienteAvisos>(http => http.Timeout = ClienteAvisos.TiempoMaximo)
    .ConfigurePrimaryHttpMessageHandler(() => ClienteAvisos.CrearManejador(permitirLocales));
builder.Services.AddHostedService<DespachadorAvisos>();

// Los registros muestran el identificador de correlación de cada evento
builder.Logging.AddSimpleConsole(opciones => opciones.IncludeScopes = true);

var host = builder.Build();
host.Run();
